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
            var inherit = cfg.Inherit ?? throw new InvalidOperationException(
                $"'{ownerId}' in {relativePath}: inherit block is required.");

            var identityFlags = MorphIdentityInheritanceRegistry.Compile(inherit.Identity, ownerId, relativePath);
            CompileAttributes(inherit.Attributes, ownerId, relativePath, out MorphAttributeInheritMode attributeMode, out MorphAttributeValueSource attributeSource, out int[] attributeIds);
            CompileTags(inherit.Tags, ownerId, relativePath, out MorphTagInheritMode tagMode, out int[] carryTagIds, out int[] stripTagIds);
            MorphEffectInheritMode effectMode = CompileEffects(inherit.Effects, ownerId, relativePath);

            if (inherit.Selection == null || !inherit.Selection.ReplaceSourceInAllSets.HasValue)
            {
                throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: inherit.selection.replaceSourceInAllSets is required.");
            }

            if (!cfg.DestroySource.HasValue)
            {
                throw new InvalidOperationException($"'{ownerId}' in {relativePath}: destroySource is required.");
            }

            return new MorphProfileDescriptor
            {
                Placement = ParsePlacement(cfg.Placement, ownerId, relativePath),
                StableIdPolicy = ParseStableIdPolicy(cfg.StableIdPolicy, ownerId, relativePath),
                DestroySource = cfg.DestroySource.Value,
                CopyPlayerOwner = identityFlags.CopyPlayerOwner,
                CopyTeam = identityFlags.CopyTeam,
                AttributeInheritMode = attributeMode,
                AttributeValueSource = attributeSource,
                InheritAttributeIds = attributeIds,
                TagInheritMode = tagMode,
                CarryTagIds = carryTagIds,
                StripTagIds = stripTagIds,
                EffectInheritMode = effectMode,
                ReplaceSelection = inherit.Selection.ReplaceSourceInAllSets.Value,
            };
        }

        private static void CompileAttributes(
            MorphProfileAttributeInheritConfig? attributes,
            string ownerId,
            string relativePath,
            out MorphAttributeInheritMode attributeMode,
            out MorphAttributeValueSource attributeSource,
            out int[] attributeIds)
        {
            if (attributes == null)
            {
                throw new InvalidOperationException($"'{ownerId}' in {relativePath}: inherit.attributes block is required.");
            }

            string mode = RequireString(attributes.Mode, ownerId, relativePath, "inherit.attributes.mode");
            attributeMode = mode switch
            {
                "None" => MorphAttributeInheritMode.None,
                "IntersectByName" => MorphAttributeInheritMode.IntersectByName,
                "AllDefined" => MorphAttributeInheritMode.AllDefined,
                _ => throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: unsupported inherit.attributes.mode '{mode}'. Supported: None, IntersectByName, AllDefined."),
            };

            attributeSource = MorphAttributeValueSource.Current;
            attributeIds = [];
            if (attributeMode == MorphAttributeInheritMode.None)
            {
                return;
            }

            string source = RequireString(attributes.Source, ownerId, relativePath, "inherit.attributes.source");
            attributeSource = source switch
            {
                "Base" => MorphAttributeValueSource.Base,
                "Current" => MorphAttributeValueSource.Current,
                _ => throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: unsupported inherit.attributes.source '{source}'. Supported: Base, Current."),
            };

            if (attributeMode == MorphAttributeInheritMode.IntersectByName)
            {
                attributeIds = CompileAttributeNames(attributes.Names, ownerId, relativePath);
            }
        }

        private static void CompileTags(
            MorphProfileTagInheritConfig? tags,
            string ownerId,
            string relativePath,
            out MorphTagInheritMode tagMode,
            out int[] carryTagIds,
            out int[] stripTagIds)
        {
            carryTagIds = [];
            stripTagIds = [];
            if (tags == null)
            {
                tagMode = MorphTagInheritMode.None;
                return;
            }

            string mode = RequireString(tags.Mode, ownerId, relativePath, "inherit.tags.mode");
            tagMode = mode switch
            {
                "None" => MorphTagInheritMode.None,
                "StripListed" => MorphTagInheritMode.StripListed,
                "CarryListed" => MorphTagInheritMode.CarryListed,
                "StripListedAndCarryListed" => MorphTagInheritMode.StripListedAndCarryListed,
                _ => throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: unsupported inherit.tags.mode '{mode}'. Supported: None, StripListed, CarryListed, StripListedAndCarryListed."),
            };

            carryTagIds = CompileTagPatterns(tags.Carry, ownerId, relativePath, "inherit.tags.carry");
            stripTagIds = CompileTagPatterns(tags.Strip, ownerId, relativePath, "inherit.tags.strip");

            switch (tagMode)
            {
                case MorphTagInheritMode.StripListed when stripTagIds.Length == 0:
                    throw new InvalidOperationException(
                        $"'{ownerId}' in {relativePath}: inherit.tags.strip is required when mode=StripListed.");
                case MorphTagInheritMode.CarryListed when carryTagIds.Length == 0:
                    throw new InvalidOperationException(
                        $"'{ownerId}' in {relativePath}: inherit.tags.carry is required when mode=CarryListed.");
                case MorphTagInheritMode.StripListedAndCarryListed when stripTagIds.Length == 0 || carryTagIds.Length == 0:
                    throw new InvalidOperationException(
                        $"'{ownerId}' in {relativePath}: inherit.tags.strip and inherit.tags.carry are required when mode=StripListedAndCarryListed.");
            }
        }

        private static MorphEffectInheritMode CompileEffects(MorphProfileEffectInheritConfig? effects, string ownerId, string relativePath)
        {
            if (effects == null)
            {
                throw new InvalidOperationException($"'{ownerId}' in {relativePath}: inherit.effects block is required.");
            }

            string mode = RequireString(effects.Mode, ownerId, relativePath, "inherit.effects.mode");
            return mode switch
            {
                "StripAll" => MorphEffectInheritMode.StripAll,
                _ => throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: unsupported inherit.effects.mode '{mode}'. Supported: StripAll."),
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
