using System;
using System.Collections.Generic;
using System.Text.Json;
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
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };

            for (int i = 0; i < merged.Count; i++)
            {
                var entryItem = merged[i];
                var cfg = entryItem.Node.Deserialize<TargetDispatchPresetConfig>(options)
                    ?? throw new InvalidOperationException($"Failed to deserialize target dispatch preset '{entryItem.Id}' from {relativePath}.");

                if (string.IsNullOrWhiteSpace(cfg.Id))
                {
                    cfg.Id = entryItem.Id;
                }

                if (!string.Equals(cfg.Id, entryItem.Id, StringComparison.OrdinalIgnoreCase))
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
            if (string.IsNullOrWhiteSpace(slot))
            {
                throw new InvalidOperationException(
                    $"'{ownerId}' in {relativePath}: {fieldPath} requires one of OriginalSource, OriginalTarget, OriginalTargetContext, ResolvedEntity.");
            }

            if (string.Equals(slot, nameof(ContextSlot.OriginalSource), StringComparison.OrdinalIgnoreCase))
            {
                return ContextSlot.OriginalSource;
            }

            if (string.Equals(slot, nameof(ContextSlot.OriginalTarget), StringComparison.OrdinalIgnoreCase))
            {
                return ContextSlot.OriginalTarget;
            }

            if (string.Equals(slot, nameof(ContextSlot.OriginalTargetContext), StringComparison.OrdinalIgnoreCase))
            {
                return ContextSlot.OriginalTargetContext;
            }

            if (string.Equals(slot, nameof(ContextSlot.ResolvedEntity), StringComparison.OrdinalIgnoreCase))
            {
                return ContextSlot.ResolvedEntity;
            }

            throw new InvalidOperationException(
                $"'{ownerId}' in {relativePath}: unsupported {fieldPath} '{slot}'. Supported: OriginalSource, OriginalTarget, OriginalTargetContext, ResolvedEntity.");
        }
    }
}
