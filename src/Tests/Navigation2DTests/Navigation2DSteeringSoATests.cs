using System.Numerics;
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
            Assert.That(runtime.AgentSoA.Radii[0], Is.EqualTo(kinematics.RadiusCm.ToFloat()).Within(0.001f));
            Assert.That(runtime.AgentSoA.HasPointGoals[0], Is.EqualTo(1));
            Assert.That(runtime.AgentSoA.GoalDistances[0], Is.EqualTo(1000f).Within(0.01f));
        }

        [Test]
        public void SteeringUpdate_HandlesHighEntityIdAgentMapping()
        {
            using var world = World.Create();
            using var runtime = CreateRuntime(Navigation2DAvoidanceMode.Hybrid, smartStopEnabled: false);
            var system = new Navigation2DSteeringSystem2D(world, runtime);

            for (int i = 0; i < 256; i++)
            {
                world.Create(new Position2D { Value = Fix64Vec2.FromInt(i, 0) });
            }

            var actor = world.Create(
                new NavAgent2D(),
                new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(500, 0), RadiusCm = Fix64.Zero },
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(100),
                    MaxAccelCmPerSec2 = Fix64.FromInt(1000),
                    RadiusCm = Fix64.FromInt(30),
                    NeighborDistCm = Fix64.FromInt(120),
                    TimeHorizonSec = Fix64.FromInt(2),
                    MaxNeighbors = 4
                },
                new Position2D { Value = Fix64Vec2.Zero },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero }
            );

            system.Update(1f / 60f);

            Assert.That(runtime.AgentSoA.TryGetAgentIndex(actor.Id, out int agentIndex), Is.True);
            Assert.That(agentIndex, Is.EqualTo(0));
            Assert.That(world.Get<NavDesiredVelocity2D>(actor).ValueCmPerSec.X.ToFloat(), Is.GreaterThan(0f));
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


        [Test]
        public void Navigation2DWorldSync_ReusesStableSlot_WhenAgentDataIsUnchanged()
        {
            using var agentSoA = new Navigation2DWorld(new Navigation2DWorldSettings(8, Fix64.FromInt(100)));

            agentSoA.BeginSync();
            Assert.That(agentSoA.SyncAgent(
                entityId: 42,
                position: Fix64Vec2.FromInt(10, 20).ToVector2(),
                velocity: Fix64Vec2.FromInt(1, 0).ToVector2(),
                radius: 30f,
                hasPointGoal: true,
                goalPosition: Fix64Vec2.FromInt(100, 20).ToVector2(),
                goalRadius: 10f,
                goalDistance: 90f), Is.True);
            var firstSync = agentSoA.EndSync();

            Assert.That(firstSync.SpatialDirty, Is.True);
            Assert.That(firstSync.SmartStopDirty, Is.True);
            Assert.That(agentSoA.TryGetAgentIndex(42, out int firstIndex), Is.True);
            Assert.That(firstIndex, Is.EqualTo(0));

            agentSoA.BeginSync();
            Assert.That(agentSoA.SyncAgent(
                entityId: 42,
                position: Fix64Vec2.FromInt(10, 20).ToVector2(),
                velocity: Fix64Vec2.FromInt(1, 0).ToVector2(),
                radius: 30f,
                hasPointGoal: true,
                goalPosition: Fix64Vec2.FromInt(100, 20).ToVector2(),
                goalRadius: 10f,
                goalDistance: 90f), Is.True);
            var secondSync = agentSoA.EndSync();

            Assert.That(secondSync.SpatialDirty, Is.False);
            Assert.That(secondSync.SmartStopDirty, Is.False);
            Assert.That(agentSoA.TryGetAgentIndex(42, out int secondIndex), Is.True);
            Assert.That(secondIndex, Is.EqualTo(firstIndex));
            Assert.That(agentSoA.Count, Is.EqualTo(1));
        }

        [Test]
        public void Navigation2DWorldSync_RemovesMissingAgent_WithSwapBackMappingUpdate()
        {
            using var agentSoA = new Navigation2DWorld(new Navigation2DWorldSettings(8, Fix64.FromInt(100)));

            agentSoA.BeginSync();
            Assert.That(agentSoA.SyncAgent(1, Fix64Vec2.FromInt(0, 0).ToVector2(), Fix64Vec2.Zero.ToVector2(), 30f, false, Vector2.Zero, 0f, 0f), Is.True);
            Assert.That(agentSoA.SyncAgent(2, Fix64Vec2.FromInt(10, 0).ToVector2(), Fix64Vec2.Zero.ToVector2(), 30f, false, Vector2.Zero, 0f, 0f), Is.True);
            agentSoA.EndSync();

            agentSoA.BeginSync();
            Assert.That(agentSoA.SyncAgent(2, Fix64Vec2.FromInt(10, 0).ToVector2(), Fix64Vec2.Zero.ToVector2(), 30f, false, Vector2.Zero, 0f, 0f), Is.True);
            var sync = agentSoA.EndSync();

            Assert.That(sync.SpatialDirty, Is.True);
            Assert.That(sync.SmartStopDirty, Is.True);
            Assert.That(agentSoA.Count, Is.EqualTo(1));
            Assert.That(agentSoA.TryGetAgentIndex(1, out _), Is.False);
            Assert.That(agentSoA.TryGetAgentIndex(2, out int index), Is.True);
            Assert.That(index, Is.EqualTo(0));
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
