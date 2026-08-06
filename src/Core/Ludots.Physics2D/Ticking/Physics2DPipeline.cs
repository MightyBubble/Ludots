using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Physics2D.Systems;

namespace Ludots.Core.Physics2D.Ticking
{
    public sealed class Physics2DPipelineDefinition
    {
        private readonly ISystem<float>[] _systems;
        private readonly string[] _stepNames;

        public Physics2DPipelineDefinition(ISystem<float>[] systems, string[] stepNames)
        {
            _systems = systems ?? throw new ArgumentNullException(nameof(systems));
            _stepNames = stepNames ?? throw new ArgumentNullException(nameof(stepNames));
            if (_systems.Length != _stepNames.Length)
            {
                throw new ArgumentException("Physics2D pipeline systems and step names must have the same length.", nameof(stepNames));
            }
        }

        public BuildPhysicsWorldSystem2D Build { get; init; } = null!;
        public AdaptiveSpatialSystem2D Spatial { get; init; } = null!;
        public ReadOnlySpan<ISystem<float>> Systems => _systems;
        public ReadOnlySpan<string> StepNames => _stepNames;
    }

    public static class Physics2DPipelineFactory
    {
        public static Physics2DPipelineDefinition CreateProduction(
            World world,
            Physics2DSolverConfig solverConfig,
            Physics2DTickPolicy tickPolicy,
            ShapeDataStorage2D shapeStorage,
            KinematicTargetPoseBuffer2D kinematicPoses,
            ContactEventQueue2D contactEvents,
            Physics2DKinematicConfig kinematicConfig)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(solverConfig);
            ArgumentNullException.ThrowIfNull(tickPolicy);
            ArgumentNullException.ThrowIfNull(shapeStorage);
            ArgumentNullException.ThrowIfNull(kinematicPoses);
            ArgumentNullException.ThrowIfNull(contactEvents);
            ArgumentNullException.ThrowIfNull(kinematicConfig);

            var build = new BuildPhysicsWorldSystem2D(world, shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, solverConfig);

            return new Physics2DPipelineDefinition(
                new ISystem<float>[]
                {
                    new KinematicDriveSystem2D(world, kinematicPoses),
                    new ForceInputWakeSystem2D(world),
                    build,
                    spatial,
                    new NarrowPhaseSystem2D(world, shapeStorage),
                    new SolverSystem2D(world, solverConfig),
                    new ApplyImpulsesSystem2D(world),
                    new PositionCorrectionSystem2D(world, solverConfig),
                    new FieldDetectorSystem(world),
                    new IntegrationSystem2D(world, solverConfig),
                    new UpdateMotionSystem(world, solverConfig),
                    new BuildIslandsSystem(world),
                    new SleepingSystem(world, solverConfig, tickPolicy),
                    new ContactEventSystem2D(
                        world,
                        contactEvents,
                        kinematicConfig.ContactEventEmitterLayers,
                        Math.Max(1, solverConfig.MaxCollisionPairs)),
                    new CleanupSystem2D(world)
                },
                new[]
                {
                    "KinematicDrive",
                    "ForceInputWake",
                    "BuildPhysicsWorld",
                    "SpatialBroadphase",
                    "NarrowPhase",
                    "Solver",
                    "ApplyImpulses",
                    "PositionCorrection",
                    "FieldDetector",
                    "Integration",
                    "UpdateMotion",
                    "BuildIslands",
                    "Sleeping",
                    "ContactEvents",
                    "Cleanup"
                })
            {
                Build = build,
                Spatial = spatial
            };
        }
    }
}
