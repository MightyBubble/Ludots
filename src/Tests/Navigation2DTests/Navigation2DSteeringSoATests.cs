using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    [NonParallelizable]
    public sealed class Navigation2DSteeringSoATests
    {
        [Test]
        public void SteeringUpdate_PopulatesAgentSoA_FromKinematicsAndGoal()
        {
            using var world = World.Create();
            using var runtime = CreateRuntime(Navigation2DAvoidanceMode.Hybrid, smartStopEnabled: true);
            var system = new Navigation2DSteeringSystem2D(world, runtime);
            var kinematics = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromInt(250),
                MaxAccelCmPerSec2 = Fix64.FromInt(1200),
                RadiusCm = Fix64.FromInt(35),
                NeighborDistCm = Fix64.FromInt(420),
                TimeHorizonSec = Fix64.FromInt(3),
                MaxNeighbors = 7
            };

            world.Create(
                new NavAgent2D(),
                new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(1000, 0), RadiusCm = Fix64.Zero },
                kinematics,
                new Position2D { Value = Fix64Vec2.Zero },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero }
            );

            system.Update(1f / 60f);

            Assert.That(runtime.AgentSoA.Count, Is.EqualTo(1));
            Assert.That(runtime.AgentSoA.MaxAccels[0], Is.EqualTo(kinematics.MaxAccelCmPerSec2.ToFloat()).Within(0.001f));
            Assert.That(runtime.AgentSoA.NeighborDistances[0], Is.EqualTo(kinematics.NeighborDistCm.ToFloat()).Within(0.001f));
            Assert.That(runtime.AgentSoA.TimeHorizons[0], Is.EqualTo(kinematics.TimeHorizonSec.ToFloat()).Within(0.001f));
            Assert.That(runtime.AgentSoA.MaxNeighbors[0], Is.EqualTo(kinematics.MaxNeighbors));
            Assert.That(runtime.AgentSoA.PreferredVelocities[0].X, Is.EqualTo(kinematics.MaxSpeedCmPerSec.ToFloat()).Within(0.001f));
            Assert.That(runtime.AgentSoA.PreferredVelocities[0].Y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(runtime.AgentSoA.HasPointGoals[0], Is.EqualTo(1));
            Assert.That(runtime.AgentSoA.GoalDistances[0], Is.EqualTo(1000f).Within(0.01f));
        }

        [Test]
        public void SteeringUpdate_RespectsPerAgentMaxNeighbors_InOrcaMode()
        {
            var desiredWithoutNeighbors = RunDesiredVelocity(maxNeighbors: 0, Navigation2DAvoidanceMode.Orca);
            var desiredWithNeighbors = RunDesiredVelocity(maxNeighbors: 1, Navigation2DAvoidanceMode.Orca);

            Assert.That(desiredWithoutNeighbors.X.ToFloat(), Is.EqualTo(100f).Within(0.01f));
            Assert.That(desiredWithoutNeighbors.Y.ToFloat(), Is.EqualTo(0f).Within(0.01f));

            bool changedByAvoidance =
                System.MathF.Abs(desiredWithNeighbors.X.ToFloat() - desiredWithoutNeighbors.X.ToFloat()) > 0.01f ||
                System.MathF.Abs(desiredWithNeighbors.Y.ToFloat() - desiredWithoutNeighbors.Y.ToFloat()) > 0.01f;

            Assert.That(changedByAvoidance, Is.True, "Neighbor-aware ORCA solve should differ from the zero-neighbor straight steering result.");
        }

        [Test]
        public void SteeringUpdate_SmartStop_StopsAgentBehindArrivedNeighbor()
        {
            using var world = World.Create();
            using var runtime = CreateRuntime(Navigation2DAvoidanceMode.Hybrid, smartStopEnabled: true);
            runtime.Config.Steering.SmartStop.QueryRadiusCm = 60;
            runtime.Config.Steering.SmartStop.MaxNeighbors = 4;
            runtime.Config.Steering.SmartStop.SelfGoalDistanceLimitCm = 40;
            runtime.Config.Steering.SmartStop.GoalToleranceCm = 20;
            runtime.Config.Steering.SmartStop.ArrivedSlackCm = 10;

            var system = new Navigation2DSteeringSystem2D(world, runtime);
            var kin = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromInt(100),
                MaxAccelCmPerSec2 = Fix64.FromInt(5000),
                RadiusCm = Fix64.FromInt(20),
                NeighborDistCm = Fix64.FromInt(100),
                TimeHorizonSec = Fix64.FromInt(2),
                MaxNeighbors = 4
            };

            var actor = world.Create(
                new NavAgent2D(),
                new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(100, 0), RadiusCm = Fix64.FromInt(10) },
                kin,
                new Position2D { Value = Fix64Vec2.FromInt(80, 0) },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero }
            );

            world.Create(
                new NavAgent2D(),
                new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(100, 0), RadiusCm = Fix64.FromInt(10) },
                kin,
                new Position2D { Value = Fix64Vec2.FromInt(96, 0) },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero }
            );

            system.Update(1f / 60f);

            var desired = world.Get<NavDesiredVelocity2D>(actor).ValueCmPerSec;
            Assert.That(desired.X.ToFloat(), Is.EqualTo(0f).Within(0.01f));
            Assert.That(desired.Y.ToFloat(), Is.EqualTo(0f).Within(0.01f));
        }

        private static Fix64Vec2 RunDesiredVelocity(int maxNeighbors, Navigation2DAvoidanceMode mode)
        {
            using var world = World.Create();
            using var runtime = CreateRuntime(mode, smartStopEnabled: false);
            var system = new Navigation2DSteeringSystem2D(world, runtime);
            var actorKin = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromInt(100),
                MaxAccelCmPerSec2 = Fix64.FromInt(5000),
                RadiusCm = Fix64.FromInt(30),
                NeighborDistCm = Fix64.FromInt(200),
                TimeHorizonSec = Fix64.FromInt(2),
                MaxNeighbors = maxNeighbors
            };

            var obstacleKin = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.Zero,
                MaxAccelCmPerSec2 = Fix64.Zero,
                RadiusCm = Fix64.FromInt(30),
                NeighborDistCm = Fix64.FromInt(200),
                TimeHorizonSec = Fix64.FromInt(2),
                MaxNeighbors = 0
            };

            var actor = world.Create(
                new NavAgent2D(),
                new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(1000, 0), RadiusCm = Fix64.Zero },
                actorKin,
                new Position2D { Value = Fix64Vec2.Zero },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero }
            );

            world.Create(
                new NavAgent2D(),
                obstacleKin,
                new Position2D { Value = Fix64Vec2.FromInt(50, 0) },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero }
            );

            system.Update(1f / 60f);
            return world.Get<NavDesiredVelocity2D>(actor).ValueCmPerSec;
        }

        private static Navigation2DRuntime CreateRuntime(Navigation2DAvoidanceMode mode, bool smartStopEnabled)
        {
            var config = new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = 16,
                FlowIterationsPerTick = 0,
                Steering = new Navigation2DSteeringConfig
                {
                    Mode = mode,
                    QueryBudget = new Navigation2DQueryBudgetConfig
                    {
                        MaxNeighborsPerAgent = 8,
                        MaxCandidateChecksPerAgent = 32,
                    },
                    Orca = new Navigation2DOrcaConfig
                    {
                        Enabled = true,
                        FallbackToPreferredVelocity = true,
                    },
                    Sonar = new Navigation2DSonarConfig
                    {
                        Enabled = true,
                        MaxSteerAngleDeg = 280,
                        BackwardPenaltyAngleDeg = 230,
                        IgnoreBehindMovingAgents = true,
                        BlockedStop = false,
                        PredictionTimeScale = 0.9f,
                    },
                    Hybrid = new Navigation2DHybridAvoidanceConfig
                    {
                        Enabled = true,
                        DenseNeighborThreshold = 6,
                        MinSpeedForOrcaCmPerSec = 120,
                        MinOpposingNeighborsForOrca = 1,
                        OpposingVelocityDotThreshold = -0.25f,
                    },
                    SmartStop = new Navigation2DSmartStopConfig
                    {
                        Enabled = smartStopEnabled,
                        QueryRadiusCm = 100,
                        MaxNeighbors = 8,
                        SelfGoalDistanceLimitCm = 160,
                        GoalToleranceCm = 80,
                        ArrivedSlackCm = 20,
                        StoppedSpeedThresholdCmPerSec = 5,
                    }
                }
            };

            return new Navigation2DRuntime(config, gridCellSizeCm: 100, loadedChunks: null);
        }
    }
}
