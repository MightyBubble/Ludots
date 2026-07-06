using System;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Vision.Config
{
    public sealed class VisionFogLayerConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public int? CellSizeCm { get; set; }
        public int? UpdateHz { get; set; }
    }

    public sealed class VisionFogLayerConfigLoader
    {
        public const string DefaultRelativePath = "Vision/fog_layers.json";

        private readonly ConfigPipeline _pipeline;
        private readonly FogLayerRegistry _registry;

        public VisionFogLayerConfigLoader(ConfigPipeline pipeline, FogLayerRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = DefaultRelativePath)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var options = StrictJsonOptions.CreateCamelCase();

            for (int i = 0; i < merged.Count; i++)
            {
                var item = merged[i];
                var cfg = item.Node.Deserialize<VisionFogLayerConfig>(options)
                    ?? throw new InvalidOperationException($"Failed to deserialize fog layer '{item.Id}' from {relativePath}.");

                if (!string.Equals(cfg.Id, item.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Fog layer id mismatch in {relativePath}: '{item.Id}' vs '{cfg.Id}'.");
                }

                string id = RequireCanonicalId(cfg.Id, relativePath);
                int cellSizeCm = RequirePositive(cfg.CellSizeCm, id, relativePath, "cellSizeCm");
                int updateHz = RequirePositive(cfg.UpdateHz, id, relativePath, "updateHz");
                _registry.Register(id, cellSizeCm, updateHz);
            }
        }

        private static string RequireCanonicalId(string value, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{relativePath}: fog layer id is required.");
            }

            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{relativePath}: fog layer id '{value}' must not include leading or trailing whitespace.");
            }

            return value;
        }

        private static int RequirePositive(int? value, string id, string relativePath, string fieldPath)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Fog layer '{id}' in {relativePath}: {fieldPath} is required.");
            }

            if (value.Value <= 0)
            {
                throw new InvalidOperationException($"Fog layer '{id}' in {relativePath}: {fieldPath} must be > 0.");
            }

            return value.Value;
        }
    }
}
