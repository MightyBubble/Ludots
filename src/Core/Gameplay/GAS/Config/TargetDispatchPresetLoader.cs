using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class TargetDispatchPresetConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string PayloadSource { get; set; } = string.Empty;
        public string PayloadTarget { get; set; } = string.Empty;
        public string PayloadTargetContext { get; set; } = string.Empty;
    }

    public sealed class TargetDispatchPresetLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly TargetDispatchPresetRegistry _registry;

        public TargetDispatchPresetLoader(ConfigPipeline pipeline, TargetDispatchPresetRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/target_dispatch_presets.json")
        {
            _registry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var fragments = _pipeline.CollectFragmentsWithSources(in entry);
            ValidateRawIds(fragments, entry.RelativePath);
            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);
            var options = StrictJsonOptions.CreateCamelCase();

            for (int i = 0; i < merged.Count; i++)
            {
                var entryItem = merged[i];
                var cfg = entryItem.Node.Deserialize<TargetDispatchPresetConfig>(options)
                    ?? throw new InvalidOperationException($"Failed to deserialize target dispatch preset '{entryItem.Id}' from {relativePath}.");

                if (!string.Equals(cfg.Id, entryItem.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Target dispatch preset id mismatch in {relativePath}: '{entryItem.Id}' vs '{cfg.Id}'.");
                }

                var mapping = new TargetResolverContextMapping
                {
                    PayloadSource = ParseContextSlotStrict(cfg.PayloadSource, cfg.Id, "payloadSource", relativePath),
                    PayloadTarget = ParseContextSlotStrict(cfg.PayloadTarget, cfg.Id, "payloadTarget", relativePath),
                    PayloadTargetContext = ParseContextSlotStrict(cfg.PayloadTargetContext, cfg.Id, "payloadTargetContext", relativePath),
                };

                _registry.Register(cfg.Id, in mapping);
            }
        }

        public static ContextSlot ParseContextSlotStrict(string slot, string ownerId, string fieldPath, string relativePath)
        {
            slot = RequireCanonicalString(
                slot,
                $"'{ownerId}' in {relativePath}: {fieldPath}");

            return slot switch
            {
                nameof(ContextSlot.OriginalSource) => ContextSlot.OriginalSource,
                nameof(ContextSlot.OriginalTarget) => ContextSlot.OriginalTarget,
                nameof(ContextSlot.OriginalTargetContext) => ContextSlot.OriginalTargetContext,
                nameof(ContextSlot.ResolvedEntity) => ContextSlot.ResolvedEntity,
                _ => throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: unsupported {fieldPath} '{slot}'. Supported: OriginalSource, OriginalTarget, OriginalTargetContext, ResolvedEntity."),
            };
        }

        private static void ValidateRawIds(IReadOnlyList<ConfigFragment> fragments, string relativePath)
        {
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                if (fragments[fragmentIndex].Node is not JsonArray arr)
                {
                    continue;
                }

                for (int entryIndex = 0; entryIndex < arr.Count; entryIndex++)
                {
                    if (arr[entryIndex] is not JsonObject obj)
                    {
                        throw new InvalidOperationException(
                            $"{relativePath} entry at index {entryIndex} must be a JSON object.");
                    }

                    if (!obj.TryGetPropertyValue("id", out JsonNode? idNode) ||
                        idNode is not JsonValue idValue ||
                        !idValue.TryGetValue<string>(out string? id))
                    {
                        throw new InvalidOperationException(
                            $"{relativePath} entry at index {entryIndex} must declare exact string field 'id'.");
                    }

                    RequireCanonicalString(id, $"{relativePath} entry id");
                }
            }
        }

        private static string RequireCanonicalString(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"{context} requires one of OriginalSource, OriginalTarget, OriginalTargetContext, ResolvedEntity.");
            }

            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must not include leading or trailing whitespace.");
            }

            return value;
        }
    }
}
