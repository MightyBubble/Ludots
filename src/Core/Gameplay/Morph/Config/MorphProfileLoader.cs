using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.Morph.Config
{
    public sealed class MorphProfileLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly MorphProfileRegistry _registry;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        public MorphProfileLoader(ConfigPipeline pipeline, MorphProfileRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/morph_profiles.json")
        {
            _registry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                var item = merged[i];
                var cfg = item.Node.Deserialize<MorphProfileConfig>(JsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize morph profile '{item.Id}' from {relativePath}.");

                if (!string.Equals(cfg.Id, item.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Morph profile id mismatch in {relativePath}: '{item.Id}' vs '{cfg.Id}'.");
                }

                _registry.Register(cfg.Id, Compile(cfg, relativePath));
            }
        }

        public static MorphProfileDescriptor Compile(MorphProfileConfig cfg, string relativePath)
        {
            string ownerId = cfg.Id;
            var inherit = cfg.Inherit ?? new MorphProfileInheritConfig();

            bool copyPlayerOwner = false;
            bool copyTeam = false;
            if (inherit.Identity != null)
            {
                for (int i = 0; i < inherit.Identity.Count; i++)
                {
                    string identity = RequireString(inherit.Identity[i], ownerId, relativePath, "inherit.identity");
                    switch (identity)
                    {
                        case "PlayerOwner":
                            copyPlayerOwner = true;
                            break;
                        case "TeamIdentity":
                        case "Team":
                            copyTeam = true;
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"'{ownerId}' in {relativePath}: unsupported inherit.identity '{identity}'. Supported: PlayerOwner, Team.");
                    }
                }
            }

            MorphAttributeInheritMode attributeMode = MorphAttributeInheritMode.None;
            int[] attributeIds = [];
            if (inherit.Attributes != null)
            {
                string mode = RequireString(inherit.Attributes.Mode, ownerId, relativePath, "inherit.attributes.mode");
                attributeMode = mode switch
                {
                    "None" => MorphAttributeInheritMode.None,
                    "IntersectByName" => MorphAttributeInheritMode.IntersectByName,
                    "AllDefined" => MorphAttributeInheritMode.AllDefined,
                    _ => throw new InvalidOperationException(
                        $"'{ownerId}' in {relativePath}: unsupported inherit.attributes.mode '{mode}'. Supported: None, IntersectByName, AllDefined."),
                };

                if (attributeMode == MorphAttributeInheritMode.IntersectByName)
                {
                    attributeIds = CompileAttributeNames(inherit.Attributes.Names, ownerId, relativePath);
                }
            }

            int[] carryTagIds = CompileTagPatterns(inherit.Tags?.Carry, ownerId, relativePath, "inherit.tags.carry");
            int[] stripTagIds = CompileTagPatterns(inherit.Tags?.Strip, ownerId, relativePath, "inherit.tags.strip");

            MorphEffectInheritMode effectMode = MorphEffectInheritMode.StripAll;
            if (inherit.Effects != null)
            {
                string mode = RequireString(inherit.Effects.Mode, ownerId, relativePath, "inherit.effects.mode");
                effectMode = mode switch
                {
                    "StripAll" => MorphEffectInheritMode.StripAll,
                    _ => throw new InvalidOperationException(
                        $"'{ownerId}' in {relativePath}: unsupported inherit.effects.mode '{mode}'. Supported: StripAll."),
                };
            }

            bool replaceSelection = inherit.Selection?.ReplaceSourceInAllSets ?? false;

            return new MorphProfileDescriptor
            {
                Placement = ParsePlacement(cfg.Placement, ownerId, relativePath),
                StableIdPolicy = ParseStableIdPolicy(cfg.StableIdPolicy, ownerId, relativePath),
                DestroySource = cfg.DestroySource ?? true,
                CopyPlayerOwner = copyPlayerOwner,
                CopyTeam = copyTeam,
                AttributeInheritMode = attributeMode,
                InheritAttributeIds = attributeIds,
                CarryTagIds = carryTagIds,
                StripTagIds = stripTagIds,
                EffectInheritMode = effectMode,
                ReplaceSelection = replaceSelection,
            };
        }

        private static MorphPlacementMode ParsePlacement(string? raw, string ownerId, string relativePath)
        {
            string value = RequireString(raw, ownerId, relativePath, "placement");
            return value switch
            {
                "AtSource" => MorphPlacementMode.AtSource,
                "AtTargetPoint" => MorphPlacementMode.AtTargetPoint,
                "PreservedExplicit" => MorphPlacementMode.PreservedExplicit,
                _ => throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: unsupported placement '{value}'. Supported: AtSource, AtTargetPoint, PreservedExplicit."),
            };
        }

        private static MorphStableIdPolicy ParseStableIdPolicy(string? raw, string ownerId, string relativePath)
        {
            string value = RequireString(raw, ownerId, relativePath, "stableIdPolicy");
            return value switch
            {
                "AllocateNew" => MorphStableIdPolicy.AllocateNew,
                "Transfer" => MorphStableIdPolicy.Transfer,
                _ => throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: unsupported stableIdPolicy '{value}'. Supported: AllocateNew, Transfer."),
            };
        }

        private static int[] CompileAttributeNames(List<string>? names, string ownerId, string relativePath)
        {
            if (names == null || names.Count == 0)
            {
                throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: inherit.attributes.names is required when mode=IntersectByName.");
            }

            var ids = new int[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                string name = RequireString(names[i], ownerId, relativePath, "inherit.attributes.names");
                int id = AttributeRegistry.GetId(name);
                if (id < 0)
                {
                    throw new InvalidOperationException(
                        $"'{ownerId}' in {relativePath}: inherit.attributes.names references unknown attribute '{name}'.");
                }

                ids[i] = id;
            }

            return ids;
        }

        private static int[] CompileTagPatterns(List<string>? patterns, string ownerId, string relativePath, string fieldPath)
        {
            if (patterns == null || patterns.Count == 0)
            {
                return [];
            }

            var resolved = new List<int>();
            for (int i = 0; i < patterns.Count; i++)
            {
                string pattern = RequireString(patterns[i], ownerId, relativePath, fieldPath);
                if (pattern.EndsWith(".*", StringComparison.Ordinal))
                {
                    string prefix = pattern[..^2];
                    foreach (var mapping in TagRegistry.SnapshotMappings())
                    {
                        if (mapping.Name.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            resolved.Add(mapping.Id);
                        }
                    }
                }
                else
                {
                    int tagId = TagRegistry.GetId(pattern);
                    if (tagId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"'{ownerId}' in {relativePath}: {fieldPath} references unknown tag '{pattern}'.");
                    }

                    resolved.Add(tagId);
                }
            }

            resolved.Sort();
            for (int i = resolved.Count - 1; i > 0; i--)
            {
                if (resolved[i] == resolved[i - 1])
                {
                    resolved.RemoveAt(i);
                }
            }

            return resolved.ToArray();
        }

        private static string RequireString(string? value, string ownerId, string relativePath, string fieldPath)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"'{ownerId}' in {relativePath}: {fieldPath} is required.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"'{ownerId}' in {relativePath}: {fieldPath} must not include leading or trailing whitespace.");
            }

            return value;
        }
    }
}
