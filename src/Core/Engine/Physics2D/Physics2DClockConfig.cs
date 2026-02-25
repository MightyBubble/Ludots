using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Engine.Physics2D
{
    public sealed class Physics2DClockConfig
    {
        public int PhysicsHz { get; set; } = 60;
        public int MaxStepsPerFixedTick { get; set; } = 8;
    }

    public sealed class Physics2DClockConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public Physics2DClockConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public Physics2DClockConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Physics2D/clock.json")
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);

            if (mergedObject == null)
            {
                return new Physics2DClockConfig();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = mergedObject.Deserialize<Physics2DClockConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize Physics2DClockConfig.");
            }

            if (config.PhysicsHz < 0)
            {
                throw new InvalidOperationException("Physics2DClockConfig.PhysicsHz must be >= 0.");
            }

            if (config.MaxStepsPerFixedTick < 1)
            {
                throw new InvalidOperationException("Physics2DClockConfig.MaxStepsPerFixedTick must be >= 1.");
            }

            return config;
        }
    }
}
