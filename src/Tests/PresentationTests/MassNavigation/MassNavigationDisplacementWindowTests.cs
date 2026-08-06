using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Layers;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Issue #643 阶段 0+1：GAS 位移写权窗口与 displaced 态的合同测试。
    /// 覆盖：窗口全生命周期（行军→位移→求解器镜像已提交位姿→交还→继续原目标）、
    /// 同一固定步内三个 WorldPositionCm 写入者实体集互斥（双写守护）、
    /// 速度低于交还阈值提前结束、maxDurationMs 超时 fail-fast、
    /// displacedAgentCapacity 超限 fail-fast、缺 MovementParticipation / 不允许位移 fail-fast。
    /// </summary>
    [TestFixture]
    public sealed class MassNavigationDisplacementWindowTests
    {
        private const int TeamId = 1;
        private const float TickDt = 0.05f;

        [Test]
        public void DisplacementWindow_FullLifecycle_HoldsAuthorityMirrorsPoseAndResumesOriginalTarget()
        {
            using var harness = new DisplacementWindowHarness(
                displacedAgentCapacity: 4,
                new AgentSpec(1000f, 1000f, CreateParticipation()),
                new AgentSpec(1100f, 1300f, null));
            Entity agent = harness.Agents[0];
            harness.Simulation.SetAgentNavigationTargetWorldCm(0, 3000f, 1000f, resetRecovery: true);

            float xBeforeMarch = harness.GetWorldPositionCm(agent).X;
            for (int i = 0; i < 5; i++)
            {
                harness.RunFixedTick(TickDt);
            }

            Vector2 marchPosition = harness.GetWorldPositionCm(agent);
            Assert.That(marchPosition.X, Is.GreaterThan(xBeforeMarch), "nav must drive the agent toward its target before the window");

            // +Y 位移 400cm / 8 tick（50cm/tick = 1000cm/s @ dt 0.05）。
            CreateFixedDirectionDisplacement(harness.World, agent, totalDistanceCm: 400, totalTicks: 8, directionDeg: 90f);

            // 申请 tick：窗口只入队，Nav 仍持有写权且位移不施加运动。
            harness.RunFixedTick(TickDt);
            Vector2 requestTickPosition = harness.GetWorldPositionCm(agent);
            Assert.That(harness.World.Get<PoseAuthority>(agent).Value, Is.EqualTo(PoseAuthorityKind.Nav));
            Assert.That(harness.Simulation.MassNavigationFlow.IsAgentDisplaced(0), Is.False);
            Assert.That(harness.Arbiter.PendingTransitionCount, Is.EqualTo(1));
            Assert.That(requestTickPosition.X, Is.GreaterThan(marchPosition.X), "nav still owns the pose in the request tick");
            Assert.That(requestTickPosition.Y, Is.EqualTo(marchPosition.Y).Within(5f), "displacement motion must not start before the boundary commit");

            // 窗口内 8 个位移 tick：位移驱动 WorldPositionCm，SyncEntities 跳过，求解器镜像已提交位姿。
            for (int i = 0; i < 8; i++)
            {
                Vector2 before = harness.GetWorldPositionCm(agent);
                harness.RunCommitPhase(TickDt);
                Assert.That(harness.World.Get<PoseAuthority>(agent).Value, Is.EqualTo(PoseAuthorityKind.Displacement));
                Assert.That(harness.Simulation.MassNavigationFlow.IsAgentDisplaced(0), Is.True);

                harness.RunNavPhase(TickDt);
                Vector2 afterNav = harness.GetWorldPositionCm(agent);
                Assert.That(afterNav.X, Is.EqualTo(before.X), "SyncEntities must not write a displaced agent");
                Assert.That(afterNav.Y, Is.EqualTo(before.Y), "SyncEntities must not write a displaced agent");

                // displaced agent 仍在求解器中且内部位置随已提交 WorldPositionCm 走（邻居据此持续避让）。
                Assert.That(harness.Simulation.MassNavigationFlow.UnitCount, Is.EqualTo(2));
                Vector2 solverPose = harness.Simulation.GetAgentWorldPositionCm(0);
                Assert.That(solverPose.X, Is.EqualTo(afterNav.X).Within(0.01f));
                Assert.That(solverPose.Y, Is.EqualTo(afterNav.Y).Within(0.01f));

                harness.RunDisplacementPhase(TickDt);
                Vector2 afterDisplacement = harness.GetWorldPositionCm(agent);
                Assert.That(afterDisplacement.Y, Is.EqualTo(before.Y + 50f).Within(0.1f), "displacement drives WorldPositionCm during the window");
                Assert.That(afterDisplacement.X, Is.EqualTo(before.X).Within(0.1f));
            }

            // 效果耗尽：位移系统已申请交还，位移实体销毁，等待边界结算。
            Assert.That(harness.CountDisplacementStates(), Is.Zero);
            Assert.That(harness.Arbiter.PendingTransitionCount, Is.EqualTo(1));
            Vector2 windowEndPosition = harness.GetWorldPositionCm(agent);
            Assert.That(windowEndPosition.Y, Is.EqualTo(requestTickPosition.Y + 400f).Within(1f));

            // 交还 tick：写权回 Nav，displaced 清除，原目标保留并继续。
            harness.RunFixedTick(TickDt);
            Assert.That(harness.World.Get<PoseAuthority>(agent).Value, Is.EqualTo(PoseAuthorityKind.Nav));
            Assert.That(harness.Simulation.MassNavigationFlow.IsAgentDisplaced(0), Is.False);
            Assert.That(harness.Arbiter.ActiveWindowCount, Is.Zero);
            Assert.That(harness.Simulation.TryGetAgentNavigationTargetLocalCm(0, out float targetX, out float targetY), Is.True);
            Assert.That(targetX, Is.EqualTo(3000f).Within(1f), "the displacement window must not shift the original target");
            Assert.That(targetY, Is.EqualTo(1000f).Within(1f), "the displacement window must not shift the original target");

            Vector2 resumeStart = harness.GetWorldPositionCm(agent);
            for (int i = 0; i < 10; i++)
            {
                harness.RunFixedTick(TickDt);
            }

            Vector2 resumed = harness.GetWorldPositionCm(agent);
            Assert.That(resumed.X, Is.GreaterThan(resumeStart.X), "the agent must resume marching toward its original target");
            Assert.That(resumed.Y, Is.LessThan(resumeStart.Y), "the agent must steer back toward the original target Y");
        }

        [Test]
        public void DoubleWriteGuard_WritersTouchDisjointEntitySetsWithinOneFixedStep()
        {
            using var harness = new DisplacementWindowHarness(
                displacedAgentCapacity: 4,
                new AgentSpec(1000f, 1000f, CreateParticipation()),
                new AgentSpec(1600f, 1600f, null));
            Entity displacedAgent = harness.Agents[0];
            Entity navOwnedAgent = harness.Agents[1];
            using var physicsSyncSystem = new Physics2DToWorldPositionSyncSystem(harness.World);
            Entity physicsBody = harness.World.Create(
                Position2D.FromCmFloat(4000f, 4000f),
                WorldPositionCm.FromCmFloat(0f, 0f));

            harness.Simulation.SetAgentNavigationTargetWorldCm(0, 3000f, 1000f, resetRecovery: true);
            harness.Simulation.SetAgentNavigationTargetWorldCm(1, 3000f, 1600f, resetRecovery: true);
            CreateFixedDirectionDisplacement(harness.World, displacedAgent, totalDistanceCm: 400, totalTicks: 8, directionDeg: 90f);
            harness.RunFixedTick(TickDt); // 申请窗口
            harness.RunFixedTick(TickDt); // 窗口生效并施加第一步位移

            // massnav agent 无 Position2D：物理同步系统结构上不可能触碰它。
            Assert.That(harness.World.Has<Position2D>(displacedAgent), Is.False);
            Assert.That(harness.World.Has<Position2D>(navOwnedAgent), Is.False);

            // 同一固定步内逐相位验证三个写入者的实体集互斥。
            harness.RunCommitPhase(TickDt);
            Vector2 displacedBefore = harness.GetWorldPositionCm(displacedAgent);
            Vector2 navOwnedBefore = harness.GetWorldPositionCm(navOwnedAgent);
            Vector2 physicsBefore = harness.GetWorldPositionCm(physicsBody);

            harness.RunNavPhase(TickDt);
            Vector2 displacedAfterNav = harness.GetWorldPositionCm(displacedAgent);
            Vector2 navOwnedAfterNav = harness.GetWorldPositionCm(navOwnedAgent);
            Assert.That(displacedAfterNav, Is.EqualTo(displacedBefore), "SyncEntities must skip the agent inside a displacement window");
            Assert.That(navOwnedAfterNav.X, Is.GreaterThan(navOwnedBefore.X), "SyncEntities still writes nav-owned agents");
            Assert.That(harness.GetWorldPositionCm(physicsBody), Is.EqualTo(physicsBefore), "SyncEntities must not touch physics bodies");

            physicsSyncSystem.Update(TickDt);
            Vector2 physicsAfterSync = harness.GetWorldPositionCm(physicsBody);
            Assert.That(physicsAfterSync.X, Is.EqualTo(4000f).Within(0.001f), "physics sync writes the physics body");
            Assert.That(physicsAfterSync.Y, Is.EqualTo(4000f).Within(0.001f));
            Assert.That(harness.GetWorldPositionCm(displacedAgent), Is.EqualTo(displacedAfterNav), "physics sync must not touch massnav agents");
            Assert.That(harness.GetWorldPositionCm(navOwnedAgent), Is.EqualTo(navOwnedAfterNav), "physics sync must not touch massnav agents");

            harness.RunDisplacementPhase(TickDt);
            Vector2 displacedAfterEffect = harness.GetWorldPositionCm(displacedAgent);
            Assert.That(displacedAfterEffect.Y, Is.GreaterThan(displacedAfterNav.Y), "the displacement system writes the window holder");
            Assert.That(harness.GetWorldPositionCm(navOwnedAgent), Is.EqualTo(navOwnedAfterNav), "the displacement system must not touch nav-owned agents");
            Assert.That(harness.GetWorldPositionCm(physicsBody), Is.EqualTo(physicsAfterSync), "the displacement system must not touch physics bodies");
        }

        [Test]
        public void DisplacementSpeedBelowHandbackThreshold_EndsWindowWithoutMotionAndResumesNav()
        {
            using var harness = new DisplacementWindowHarness(
                displacedAgentCapacity: 4,
                new AgentSpec(1000f, 1000f, CreateParticipation(handbackThresholdCmPerSec: 2000f)));
            Entity agent = harness.Agents[0];
            harness.Simulation.SetAgentNavigationTargetWorldCm(0, 3000f, 1000f, resetRecovery: true);

            // 50cm/tick @ dt 0.05 = 1000cm/s，低于 2000cm/s 阈值 → 首个位移 tick 直接交还。
            Entity displacementEntity = CreateFixedDirectionDisplacement(harness.World, agent, totalDistanceCm: 400, totalTicks: 8, directionDeg: 90f);
            harness.RunFixedTick(TickDt); // 申请窗口
            float yBeforeWindow = harness.GetWorldPositionCm(agent).Y;

            harness.RunFixedTick(TickDt); // 窗口生效但速度低于阈值：无运动、申请交还、销毁位移实体
            Assert.That(harness.World.IsAlive(displacementEntity), Is.False);
            Assert.That(harness.GetWorldPositionCm(agent).Y, Is.EqualTo(yBeforeWindow).Within(0.001f), "no displacement motion below the handback threshold");
            Assert.That(harness.Arbiter.PendingTransitionCount, Is.EqualTo(1));

            harness.RunFixedTick(TickDt); // 边界结算交还
            Assert.That(harness.World.Get<PoseAuthority>(agent).Value, Is.EqualTo(PoseAuthorityKind.Nav));
            Assert.That(harness.Simulation.MassNavigationFlow.IsAgentDisplaced(0), Is.False);
            Assert.That(harness.Arbiter.ActiveWindowCount, Is.Zero);
        }

        [Test]
        public void DisplacementWindowExceedingMaxDurationMs_ThrowsAtFixedStepBoundary()
        {
            using var harness = new DisplacementWindowHarness(
                displacedAgentCapacity: 4,
                new AgentSpec(1000f, 1000f, CreateParticipation(handbackThresholdCmPerSec: 1f, maxDurationMs: 60)));
            // 10cm/tick = 200cm/s，高于 1cm/s 阈值 → 窗口保持打开直至超时。
            CreateFixedDirectionDisplacement(harness.World, harness.Agents[0], totalDistanceCm: 1000, totalTicks: 100, directionDeg: 90f);

            harness.RunFixedTick(TickDt); // 申请窗口
            harness.RunFixedTick(TickDt); // 窗口生效并推进到 50ms（≤ 60ms）

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.RunFixedTick(TickDt))!; // 推进到 100ms > 60ms
            Assert.That(ex.Message, Does.Contain("maxDurationMs"));
        }

        [Test]
        public void ConcurrentDisplacementsExceedingDisplacedAgentCapacity_Throw()
        {
            using var harness = new DisplacementWindowHarness(
                displacedAgentCapacity: 1,
                new AgentSpec(1000f, 1000f, CreateParticipation()),
                new AgentSpec(2000f, 2000f, CreateParticipation()));
            CreateFixedDirectionDisplacement(harness.World, harness.Agents[0], totalDistanceCm: 400, totalTicks: 8, directionDeg: 90f);
            CreateFixedDirectionDisplacement(harness.World, harness.Agents[1], totalDistanceCm: 400, totalTicks: 8, directionDeg: 90f);

            harness.RunFixedTick(TickDt); // 两个窗口同时入队

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.RunFixedTick(TickDt))!; // 第二个窗口生效时超出容量
            Assert.That(ex.Message, Does.Contain("displacedAgentCapacity"));
        }

        [Test]
        public void DisplacementOnNavAgentWithoutMovementParticipation_Throws()
        {
            using var harness = new DisplacementWindowHarness(
                displacedAgentCapacity: 4,
                new AgentSpec(1000f, 1000f, null));
            CreateFixedDirectionDisplacement(harness.World, harness.Agents[0], totalDistanceCm: 400, totalTicks: 8, directionDeg: 90f);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.RunFixedTick(TickDt))!;
            Assert.That(ex.Message, Does.Contain("MovementParticipation"));
        }

        [Test]
        public void DisplacementOnNavAgentWithDisplacementDisallowed_Throws()
        {
            using var harness = new DisplacementWindowHarness(
                displacedAgentCapacity: 4,
                new AgentSpec(1000f, 1000f, CreateParticipation(allowed: false)));
            CreateFixedDirectionDisplacement(harness.World, harness.Agents[0], totalDistanceCm: 400, totalTicks: 8, directionDeg: 90f);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.RunFixedTick(TickDt))!;
            Assert.That(ex.Message, Does.Contain("displacement.allowed"));
        }

        private static MovementParticipation CreateParticipation(
            bool allowed = true,
            float handbackThresholdCmPerSec = 1f,
            int maxDurationMs = 10_000)
        {
            return new MovementParticipation
            {
                PhysicsPresence = PhysicsPresenceKind.None,
                DisplacementAllowed = allowed,
                DisplacementHandbackSpeedThresholdCmPerSec = handbackThresholdCmPerSec,
                DisplacementMaxDurationMs = maxDurationMs,
            };
        }

        private static Entity CreateFixedDirectionDisplacement(
            World world,
            Entity target,
            int totalDistanceCm,
            int totalTicks,
            float directionDeg)
        {
            return world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = target,
                DirectionMode = DisplacementDirectionMode.Fixed,
                FixedDirectionRad = Fix64.FromFloat(directionDeg) * Fix64.Deg2Rad,
                TotalDistanceCm = totalDistanceCm,
                RemainingDistanceCm = Fix64.FromInt(totalDistanceCm),
                TotalDurationTicks = totalTicks,
                RemainingTicks = totalTicks,
                OverrideNavigation = false,
            });
        }

        private readonly struct AgentSpec
        {
            public AgentSpec(float worldXCm, float worldYCm, MovementParticipation? participation)
            {
                WorldXCm = worldXCm;
                WorldYCm = worldYCm;
                Participation = participation;
            }

            public float WorldXCm { get; }
            public float WorldYCm { get; }
            public MovementParticipation? Participation { get; }
        }

        /// <summary>
        /// 无引擎的固定步 harness，按 GameEngine SystemGroup 顺序驱动一个固定步：
        /// SchemaUpdate（写权结算）→ PostMovement（求解器步进 + displaced 回灌 + entity sync）
        /// → EffectProcessing（GAS 位移）。
        /// </summary>
        private sealed class DisplacementWindowHarness : IDisposable
        {
            private static readonly QueryDescription _displacementQuery = new QueryDescription().WithAll<DisplacementState>();

            public DisplacementWindowHarness(int displacedAgentCapacity, params AgentSpec[] specs)
            {
                World = World.Create();
                MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
                config.ScenarioRuntime.RuntimeCapacity.DisplacedAgentCapacity = displacedAgentCapacity;
                Simulation = new MassNavigationSimulationRuntime(config);
                Simulation.BindBoardWorld(
                    new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100),
                    MassNavigationOrderChainTests.CreateLoadedChunksForTests(Simulation));

                int layerIndex = LayerRegistry.Register(MassNavigationLayerNames.Agent);
                uint mask = 1u << layerIndex;
                var layer = new MassNavigationAgentLayer(mask, mask);
                int profileId = MassNavigationProfileRegistry.Register("light");

                Agents = new Entity[specs.Length];
                var seeds = new MassNavigationAgentSeed[specs.Length];
                var controllable = new bool[specs.Length];
                for (int i = 0; i < specs.Length; i++)
                {
                    AgentSpec spec = specs[i];
                    Entity entity = World.Create(
                        new MassNavigationAgent { ProfileId = profileId },
                        WorldPositionCm.FromCmFloat(spec.WorldXCm, spec.WorldYCm),
                        new EntityLayer(layer.CategoryMask, layer.InteractionMask),
                        new FacingDirection { AngleRad = 0f });
                    if (spec.Participation.HasValue)
                    {
                        World.Add(entity, spec.Participation.Value);
                        World.Add(entity, new PoseAuthority
                        {
                            Value = MovementParticipationRules.DeriveInitialPoseAuthority(spec.Participation.Value.PhysicsPresence),
                        });
                    }

                    Agents[i] = entity;
                    seeds[i] = new MassNavigationAgentSeed(
                        teamId: TeamId,
                        localPositionXCm: spec.WorldXCm,
                        localPositionYCm: spec.WorldYCm,
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        layer);
                    controllable[i] = true;
                }

                Simulation.RebuildFromAuthoredAgents(World, Agents, seeds, controllable);

                Arbiter = new PoseAuthorityArbiter();
                Arbiter.AddListener(new MassNavigationPoseAuthorityBridge(() => Simulation));
                CommitSystem = new PoseAuthorityCommitSystem(World, Arbiter);
                DisplacementSystem = new DisplacementRuntimeSystem(World, Arbiter);
            }

            public World World { get; }
            public MassNavigationSimulationRuntime Simulation { get; }
            public PoseAuthorityArbiter Arbiter { get; }
            public PoseAuthorityCommitSystem CommitSystem { get; }
            public DisplacementRuntimeSystem DisplacementSystem { get; }
            public Entity[] Agents { get; }

            public void RunCommitPhase(float dt)
            {
                CommitSystem.Update(dt);
            }

            public void RunNavPhase(float dt)
            {
                Simulation.StepNavigationForTests(World, dt, runHardResolve: true);
                Simulation.MassNavigationFlow.SyncDisplacedAgentPoses(World, Simulation.AgentState);
                Simulation.MassNavigationFlow.SyncEntities(World, Simulation.AgentState);
            }

            public void RunDisplacementPhase(float dt)
            {
                DisplacementSystem.Update(dt);
            }

            public void RunFixedTick(float dt)
            {
                RunCommitPhase(dt);
                RunNavPhase(dt);
                RunDisplacementPhase(dt);
            }

            public Vector2 GetWorldPositionCm(Entity entity)
            {
                WorldPositionCm position = World.Get<WorldPositionCm>(entity);
                return new Vector2(position.Value.X.ToFloat(), position.Value.Y.ToFloat());
            }

            public int CountDisplacementStates()
            {
                return World.CountEntities(in _displacementQuery);
            }

            public void Dispose()
            {
                CommitSystem.Dispose();
                DisplacementSystem.Dispose();
                World.Dispose();
            }
        }
    }
}
