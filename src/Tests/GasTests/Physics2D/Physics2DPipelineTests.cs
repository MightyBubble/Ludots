using System;
using Arch.Core;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Ticking;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Physics2D
{
    [TestFixture]
    public sealed class Physics2DPipelineTests
    {
        [Test]
        public void ProductionPipeline_DeclaresDeterministicStepOrder()
        {
            using var world = World.Create();
            var solverConfig = new Physics2DSolverConfig();
            var tickPolicy = new Physics2DTickPolicy(targetHz: 15, maxStepsPerFixedTick: 8);
            var shapeStorage = new ShapeDataStorage2D();

            Physics2DPipelineDefinition pipeline = Physics2DPipelineFactory.CreateProduction(
                world,
                solverConfig,
                tickPolicy,
                shapeStorage,
                new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 64),
                new ContactEventQueue2D(contactEventQueueCapacity: 256),
                new Physics2DKinematicConfig
                {
                    KinematicBodyCapacity = 64,
                    ContactEventQueueCapacity = 256,
                    ContactEventEmitterLayers = new List<string>()
                });

            Assert.That(pipeline.StepNames.ToArray(), Is.EqualTo(new[]
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
            }));
            Assert.That(pipeline.Systems.Length, Is.EqualTo(pipeline.StepNames.Length));
            Assert.That(pipeline.Build, Is.SameAs(pipeline.Systems[2]));
            Assert.That(pipeline.Spatial, Is.SameAs(pipeline.Systems[3]));
        }
    }
}
