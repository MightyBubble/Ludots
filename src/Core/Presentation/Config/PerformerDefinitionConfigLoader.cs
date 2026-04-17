using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Config
{
    /// <summary>
    /// Loads <see cref="PerformerDefinition"/> entries from
    /// <c>Presentation/performers.json</c>.
    /// </summary>
    public sealed class PerformerDefinitionConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly PerformerDefinitionRegistry _registry;
        private readonly Func<string, int> _resolveAttributeName;
        private readonly Func<string, int> _resolveMeshId;
        private readonly Func<string, int> _resolveTextTokenId;
        private readonly Func<string, int> _resolveEntityTemplateKey;
        private readonly Func<string, int> _resolveEffectTemplateId;

        public PerformerDefinitionConfigLoader(
            ConfigPipeline configs,
            PerformerDefinitionRegistry registry,
            Func<string, int> resolveAttributeName = null,
            Func<string, int> resolveMeshId = null,
            Func<string, int> resolveTextTokenId = null,
            Func<string, int> resolveEntityTemplateKey = null,
            Func<string, int> resolveEffectTemplateId = null)
        {
            _configs = configs;
            _registry = registry;
            _resolveAttributeName = resolveAttributeName ?? (_ => 0);
            _resolveMeshId = resolveMeshId ?? (_ => 0);
            _resolveTextTokenId = resolveTextTokenId ?? (_ => 0);
            _resolveEntityTemplateKey = resolveEntityTemplateKey ?? (_ => 0);
            _resolveEffectTemplateId = resolveEffectTemplateId ?? (_ => 0);
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/performers.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            if (report != null)
            {
                var deletions = report.GetDeletions(entry.RelativePath);
                for (int i = 0; i < deletions.Count; i++)
                {
                    _registry.Unregister(deletions[i].Id);
                }
            }

            if (merged.Count == 0)
            {
                return;
            }

            for (int i = 0; i < merged.Count; i++)
            {
                string key = merged[i].Node["id"]?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _registry.GetOrRegisterId(key);
                }
            }

            for (int i = 0; i < merged.Count; i++)
            {
                var (key, def) = ParseDefinition(merged[i].Node);
                if (key != null && def != null)
                {
                    _registry.Register(key, def);
                }
            }
        }

        private (string key, PerformerDefinition def) ParseDefinition(JsonNode node)
        {
            string key = node["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(key))
            {
                return (null, null);
            }

            RejectLegacyFields(node, key);

            var def = new PerformerDefinition
            {
                VisualKind = ParseEnum(node["visualKind"]?.GetValue<string>(), PerformerVisualKind.GroundOverlay),
                MeshOrShapeId = ResolveMeshOrShape(node["meshOrShapeId"], ParseEnum(node["visualKind"]?.GetValue<string>(), PerformerVisualKind.GroundOverlay)),
                DefaultColor = ParseColor(node["defaultColor"]),
                DefaultScale = node["defaultScale"]?.GetValue<float>() ?? 1f,
                DefaultLifetime = node["defaultLifetime"]?.GetValue<float>() ?? 0f,
                DefaultFontSize = node["defaultFontSize"]?.GetValue<int>() ?? 16,
                PositionOffset = ParseVector3(node["positionOffset"]),
                PositionYDriftPerSecond = node["positionYDriftPerSecond"]?.GetValue<float>() ?? 0f,
                AlphaFadeOverLifetime = node["alphaFadeOverLifetime"]?.GetValue<bool>() ?? false,
                VisibilityCondition = ParseConditionRef(node["visibility"]),
                Rules = ParseRules(node["rules"]),
                Bindings = ParseBindings(node["bindings"]),
            };

            if (def.VisualKind == PerformerVisualKind.SurfaceSource)
            {
                def.Surface = ParseSurface(node["surface"], key);
            }
            else if (node["surface"] != null)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' declares a surface block but visualKind '{def.VisualKind}' is not '{PerformerVisualKind.SurfaceSource}'.");
            }

            return (key, def);
        }

        private SurfaceAuthoringBlock ParseSurface(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' uses visualKind '{PerformerVisualKind.SurfaceSource}' but is missing required object field 'surface'.");
            }

            return new SurfaceAuthoringBlock
            {
                Kind = ParseEnum(obj["kind"]?.GetValue<string>(), PerformerSurfaceKind.SplineRibbon),
                ProfileId = obj["profileId"]?.GetValue<string>() ?? string.Empty,
                GeometrySource = ParseSurfaceGeometrySource(obj["geometrySource"], key),
                ChunkBake = ParseChunkBakePolicy(obj["chunkBake"], key),
                MaterialSet = ParseMaterialSet(obj["materialSet"], key),
                LodProfileId = obj["lodProfileId"]?.GetValue<string>() ?? string.Empty,
                Grounding = ParseGroundingPolicy(obj["grounding"]),
                BoundsPolicy = obj["boundsPolicy"]?.GetValue<string>() ?? string.Empty,
            };
        }

        private PerformerSurfaceGeometrySource ParseSurfaceGeometrySource(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"SurfaceSource performer '{key}' must declare object field 'surface.geometrySource'.");
            }

            return new PerformerSurfaceGeometrySource
            {
                ControlPointSource = ParseSurfaceValueSource(obj["controlPointSource"]),
                WidthSource = ParseSurfaceValueSource(obj["widthSource"]),
                FlowDirectionSource = ParseSurfaceValueSource(obj["flowDirectionSource"]),
                SegmentationPolicy = obj["segmentationPolicy"]?.GetValue<string>() ?? string.Empty,
                BoundaryPointSource = ParseSurfaceValueSource(obj["boundaryPointSource"]),
                TriangulationPolicy = obj["triangulationPolicy"]?.GetValue<string>() ?? string.Empty,
                MeshPayloadSource = ParseSurfaceValueSource(obj["meshPayloadSource"]),
            };
        }

        private PerformerSurfaceChunkBakePolicy ParseChunkBakePolicy(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"SurfaceSource performer '{key}' must declare object field 'surface.chunkBake'.");
            }

            return new PerformerSurfaceChunkBakePolicy
            {
                Enabled = obj["enabled"]?.GetValue<bool>() ?? true,
                Ownership = ParseEnum(obj["ownership"]?.GetValue<string>(), PerformerSurfaceChunkOwnership.PerChunk),
                ChunkInfluencePolicy = obj["chunkInfluencePolicy"]?.GetValue<string>() ?? string.Empty,
                RebakePolicy = obj["rebakePolicy"]?.GetValue<string>() ?? string.Empty,
                UsageHint = ParseEnum(obj["usageHint"]?.GetValue<string>(), Assets.ProceduralMeshUsageHint.Static),
            };
        }

        private PerformerSurfaceMaterialSet ParseMaterialSet(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"SurfaceSource performer '{key}' must declare object field 'surface.materialSet'.");
            }

            return new PerformerSurfaceMaterialSet
            {
                PrimaryMaterialId = obj["primaryMaterialId"]?.GetValue<string>() ?? string.Empty,
                SecondaryMaterialId = obj["secondaryMaterialId"]?.GetValue<string>() ?? string.Empty,
                AllowInstanceOverride = obj["allowInstanceOverride"]?.GetValue<bool>() ?? false,
            };
        }

        private PerformerSurfaceGroundingPolicy ParseGroundingPolicy(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                return new PerformerSurfaceGroundingPolicy();
            }

            return new PerformerSurfaceGroundingPolicy
            {
                Mode = obj["mode"]?.GetValue<string>() ?? string.Empty,
            };
        }

        private PerformerSurfaceValueSource? ParseSurfaceValueSource(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                return null;
            }

            return new PerformerSurfaceValueSource
            {
                Kind = ParseEnum(obj["kind"]?.GetValue<string>(), PerformerSurfaceValueSourceKind.Constant),
                Id = obj["id"]?.GetValue<string>() ?? string.Empty,
                GraphProgramId = obj["graphProgramId"]?.GetValue<int>() ?? 0,
            };
        }

        private static void RejectLegacyFields(JsonNode node, string key)
        {
            if (node["entityScope"] != null)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' still uses removed field 'entityScope'. Migrate it to lifecycle rules.");
            }

            if (node["requiredTemplate"] != null)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' still uses removed field 'requiredTemplate'. Migrate it to event.key + lifecycle rules.");
            }
        }

        private int ResolveMeshOrShape(JsonNode meshNode, PerformerVisualKind visualKind)
        {
            if (meshNode == null)
            {
                return 0;
            }

            string meshStr = meshNode.ToString().Trim('"');
            if (string.IsNullOrWhiteSpace(meshStr))
            {
                return 0;
            }

            if (visualKind == PerformerVisualKind.GroundOverlay)
            {
                if (Enum.TryParse<GroundOverlayShape>(meshStr, ignoreCase: true, out var shape))
                {
                    return (int)shape;
                }

                return 0;
            }

            return _resolveMeshId(meshStr);
        }

        private PerformerRule[] ParseRules(JsonNode node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<PerformerRule>();
            }

            var rules = new PerformerRule[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                rules[i] = ParseRule(arr[i]!);
            }

            return rules;
        }

        private PerformerRule ParseRule(JsonNode node)
        {
            return new PerformerRule
            {
                Event = ParseEventFilter(node["event"]),
                Condition = ParseConditionRef(node["condition"]),
                Command = ParsePerformerCommand(node["command"]),
            };
        }

        private EventFilter ParseEventFilter(JsonNode node)
        {
            if (node == null)
            {
                return default;
            }

            PresentationEventKind kind = ParseEnum(node["kind"]?.GetValue<string>(), PresentationEventKind.None);
            int keyId = ResolveEventKey(kind, node);

            return new EventFilter
            {
                Kind = kind,
                KeyId = keyId,
            };
        }

        private int ResolveEventKey(PresentationEventKind kind, JsonNode node)
        {
            if (node["keyId"] != null)
            {
                return node["keyId"]!.GetValue<int>();
            }

            string key = node["key"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return -1;
            }

            return kind switch
            {
                PresentationEventKind.EntitySpawned => ResolveEntityTemplateKey(kind, key),
                PresentationEventKind.EntityDestroyed => ResolveEntityTemplateKey(kind, key),
                PresentationEventKind.ProjectileSpawned => ResolveEffectTemplateId(kind, key),
                _ => throw new InvalidOperationException(
                    $"Presentation event kind '{kind}' does not support string key '{key}'."),
            };
        }

        private int ResolveEntityTemplateKey(PresentationEventKind kind, string key)
        {
            int keyId = _resolveEntityTemplateKey(key);
            if (keyId <= 0)
            {
                throw new InvalidOperationException(
                    $"Presentation event '{kind}' references unknown entity template '{key}'.");
            }

            return keyId;
        }

        private int ResolveEffectTemplateId(PresentationEventKind kind, string key)
        {
            int keyId = _resolveEffectTemplateId(key);
            if (keyId <= 0)
            {
                throw new InvalidOperationException(
                    $"Presentation event '{kind}' references unknown effect template '{key}'.");
            }

            return keyId;
        }

        private PerformerCommand ParsePerformerCommand(JsonNode node)
        {
            if (node == null)
            {
                return default;
            }

            string perfRef = node["performerDefinitionId"]?.GetValue<string>();
            int perfId = string.IsNullOrWhiteSpace(perfRef) ? 0 : _registry.GetId(perfRef);

            return new PerformerCommand
            {
                CommandKind = ParseEnum(node["commandKind"]?.GetValue<string>(), PresentationCommandKind.None),
                PerformerDefinitionId = perfId,
                ScopeId = node["scopeId"]?.GetValue<int>() ?? -1,
                ScopeSource = ParseEnum(node["scopeSource"]?.GetValue<string>(), PerformerCommandScopeSource.Fixed),
                ParamKey = node["paramKey"]?.GetValue<int>() ?? 0,
                ParamValue = node["paramValue"]?.GetValue<float>() ?? 0f,
                ParamGraphProgramId = node["paramGraphProgramId"]?.GetValue<int>() ?? 0,
            };
        }

        private PerformerParamBinding[] ParseBindings(JsonNode node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<PerformerParamBinding>();
            }

            var bindings = new PerformerParamBinding[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                bindings[i] = ParseBinding(arr[i]!);
            }

            return bindings;
        }

        private PerformerParamBinding ParseBinding(JsonNode node)
        {
            return new PerformerParamBinding
            {
                ParamKey = node["paramKey"]?.GetValue<int>() ?? 0,
                Value = ParseValueRef(node),
            };
        }

        private ValueRef ParseValueRef(JsonNode node)
        {
            string source = node["source"]?.GetValue<string>();
            return source?.ToLowerInvariant() switch
            {
                "attribute" => ValueRef.FromAttribute(ResolveAttributeId(node)),
                "attributeratio" => ValueRef.FromAttributeRatio(ResolveAttributeId(node)),
                "attributebase" => ValueRef.FromAttributeBase(ResolveAttributeId(node)),
                "graph" => ValueRef.FromGraph(node["sourceId"]?.GetValue<int>() ?? 0),
                "entitycolor" => ValueRef.FromEntityColor(node["sourceId"]?.GetValue<int>() ?? 0),
                "facingradians" => ValueRef.FromFacingRadians(),
                "facingdegrees" => ValueRef.FromFacingDegrees(),
                "texttoken" => ValueRef.FromConstant(ResolveTextTokenId(node)),
                _ => ValueRef.FromConstant(node["constantValue"]?.GetValue<float>() ?? 0f),
            };
        }

        private int ResolveTextTokenId(JsonNode node)
        {
            string tokenKey = node["textToken"]?.GetValue<string>() ?? node["sourceKey"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tokenKey))
            {
                throw new InvalidOperationException("Performer WorldText textToken binding requires a non-empty 'textToken'.");
            }

            int tokenId = _resolveTextTokenId(tokenKey);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException($"Performer WorldText references unknown text token '{tokenKey}'.");
            }

            return tokenId;
        }

        private int ResolveAttributeId(JsonNode node)
        {
            var idNode = node["sourceId"];
            if (idNode != null)
            {
                return idNode.GetValue<int>();
            }

            string name = node["attributeName"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return _resolveAttributeName(name);
            }

            return 0;
        }

        private ConditionRef ParseConditionRef(JsonNode node)
        {
            if (node == null)
            {
                return ConditionRef.AlwaysTrue;
            }

            var cond = new ConditionRef();
            string inline = node["inline"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(inline))
            {
                cond.Inline = ParseEnum(inline, InlineConditionKind.None);
            }

            cond.GraphProgramId = node["graphProgramId"]?.GetValue<int>() ?? 0;
            return cond;
        }

        private static Vector3 ParseVector3(JsonNode node)
        {
            if (node is JsonArray arr && arr.Count >= 3)
            {
                return new Vector3(
                    arr[0]?.GetValue<float>() ?? 0f,
                    arr[1]?.GetValue<float>() ?? 0f,
                    arr[2]?.GetValue<float>() ?? 0f);
            }

            return Vector3.Zero;
        }

        private static Vector4 ParseColor(JsonNode node)
        {
            if (node is JsonArray arr && arr.Count >= 4)
            {
                return new Vector4(
                    arr[0]?.GetValue<float>() ?? 1f,
                    arr[1]?.GetValue<float>() ?? 1f,
                    arr[2]?.GetValue<float>() ?? 1f,
                    arr[3]?.GetValue<float>() ?? 1f);
            }

            return new Vector4(1f, 1f, 1f, 1f);
        }

        private static T ParseEnum<T>(string s, T fallback) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return fallback;
            }

            if (Enum.TryParse<T>(s, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }
}
