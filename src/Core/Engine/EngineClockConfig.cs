using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Engine
{
    public sealed class EngineClockConfig
    {
        public int FixedHz { get; set; } = 50;
    }

    public sealed class EngineClockConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public EngineClockConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public EngineClockConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Engine/clock.json")
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);

            if (mergedObject == null)
            {
                return new EngineClockConfig();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = mergedObject.Deserialize<EngineClockConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize EngineClockConfig.");
            }

            if (config.FixedHz < 1)
            {
                throw new InvalidOperationException("EngineClockConfig.FixedHz must be >= 1.");
            }

            return config;
        }
    }
}
