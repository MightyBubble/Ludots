using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Engine.Physics2D
{
    public enum Physics2DBroadphaseStrategyKind
    {
        SortAndSweep = 0,
        UniformGrid = 1
    }

    public sealed class Physics2DBroadphaseConfig
    {
        public Physics2DBroadphaseStrategyKind Strategy { get; set; } = Physics2DBroadphaseStrategyKind.SortAndSweep;
        public int CellSizeCm { get; set; } = 256;
    }

    public sealed class Physics2DClockConfig
    {
        public int PhysicsHz { get; set; } = 60;
        public int MaxStepsPerFixedTick { get; set; } = 8;
        public Physics2DBroadphaseConfig Broadphase { get; set; } = new Physics2DBroadphaseConfig();
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
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);

            if (mergedObject == null)
            {
                return new Physics2DClockConfig();
            }

            var options = StrictJsonOptions.CreateExact();
            options.Converters.Add(new JsonStringEnumConverter());

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

            config.Broadphase ??= new Physics2DBroadphaseConfig();
            if (!Enum.IsDefined(config.Broadphase.Strategy))
            {
                throw new InvalidOperationException($"Physics2DClockConfig.Broadphase.Strategy is invalid: {config.Broadphase.Strategy}.");
            }

            if (config.Broadphase.CellSizeCm < 1)
            {
                throw new InvalidOperationException("Physics2DClockConfig.Broadphase.CellSizeCm must be >= 1.");
            }

            return config;
        }
    }
}
