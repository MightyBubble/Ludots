using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;

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
        public int PhysicsHz { get; set; } = 15;
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

    public sealed class Physics2DSolverConfig
    {
        public int SolverIterations { get; set; } = 6;
        public float Epsilon { get; set; } = 0.000001f;
        public float PositionCorrectionPercentage { get; set; } = 0.4f;
        public float PositionCorrectionSlopCm { get; set; } = 0.01f;
        public float MinVelocityCmPerSecSquared { get; set; } = 0.0001f;
        public float LinearMotionThresholdCmPerSec { get; set; } = 0.01f;
        public float AngularMotionThresholdRadPerSec { get; set; } = 0.01f;
        public float SleepTimeSeconds { get; set; } = 4f;
        public float DefaultFriction { get; set; } = 0.5f;
        public float DefaultRestitution { get; set; } = 0f;
        public float DefaultBaseDamping { get; set; } = 0.98f;
        public int CollisionPairInitialCapacity { get; set; } = 0;
        public int CollisionPairGrowthStep { get; set; } = 256;
        public int MaxCollisionPairs { get; set; } = 4096;

        public Fix64 EpsilonFix64 => Fix64.FromFloat(Epsilon);
        public Fix64 PositionCorrectionPercentageFix64 => Fix64.FromFloat(PositionCorrectionPercentage);
        public Fix64 PositionCorrectionSlopFix64 => Fix64.FromFloat(PositionCorrectionSlopCm);
        public Fix64 MinVelocitySqFix64 => Fix64.FromFloat(MinVelocityCmPerSecSquared);
        public Fix64 LinearMotionThresholdFix64 => Fix64.FromFloat(LinearMotionThresholdCmPerSec);
        public Fix64 AngularMotionThresholdFix64 => Fix64.FromFloat(AngularMotionThresholdRadPerSec);
        public Fix64 DefaultFrictionFix64 => Fix64.FromFloat(DefaultFriction);
        public Fix64 DefaultRestitutionFix64 => Fix64.FromFloat(DefaultRestitution);
        public Fix64 DefaultBaseDampingFix64 => Fix64.FromFloat(DefaultBaseDamping);
    }

    public sealed class Physics2DSolverConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public Physics2DSolverConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public Physics2DSolverConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Physics2D/solver.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);

            if (mergedObject == null)
            {
                return new Physics2DSolverConfig();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = mergedObject.Deserialize<Physics2DSolverConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize Physics2DSolverConfig.");
            }

            Validate(config);
            return config;
        }

        private static void Validate(Physics2DSolverConfig config)
        {
            if (config.SolverIterations < 1)
            {
                throw new InvalidOperationException("Physics2DSolverConfig.SolverIterations must be >= 1.");
            }

            if (!(config.Epsilon > 0f))
            {
                throw new InvalidOperationException("Physics2DSolverConfig.Epsilon must be > 0.");
            }

            if (config.PositionCorrectionPercentage < 0f || config.PositionCorrectionPercentage > 1f)
            {
                throw new InvalidOperationException("Physics2DSolverConfig.PositionCorrectionPercentage must be in [0, 1].");
            }

            if (config.PositionCorrectionSlopCm < 0f)
            {
                throw new InvalidOperationException("Physics2DSolverConfig.PositionCorrectionSlopCm must be >= 0.");
            }

            if (config.MinVelocityCmPerSecSquared < 0f ||
                config.LinearMotionThresholdCmPerSec < 0f ||
                config.AngularMotionThresholdRadPerSec < 0f)
            {
                throw new InvalidOperationException("Physics2DSolverConfig motion thresholds must be >= 0.");
            }

            if (config.SleepTimeSeconds < 0f)
            {
                throw new InvalidOperationException("Physics2DSolverConfig.SleepTimeSeconds must be >= 0.");
            }

            if (config.DefaultFriction < 0f ||
                config.DefaultRestitution < 0f ||
                config.DefaultBaseDamping < 0f)
            {
                throw new InvalidOperationException("Physics2DSolverConfig default material values must be >= 0.");
            }

            if (config.CollisionPairInitialCapacity < 0 ||
                config.CollisionPairGrowthStep < 1 ||
                config.MaxCollisionPairs < 0 ||
                config.CollisionPairInitialCapacity > config.MaxCollisionPairs)
            {
                throw new InvalidOperationException("Physics2DSolverConfig collision pair pool capacity values are invalid.");
            }
        }
    }
}
