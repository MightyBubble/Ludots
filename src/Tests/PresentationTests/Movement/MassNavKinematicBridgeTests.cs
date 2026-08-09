using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Layers;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;
using Ludots.Core.Movement.Physics2DBridge;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Tests.Presentation;
using NUnit.Framework;

namespace Ludots.Tests.Presentation.Movement
{
    /// <summary>
    /// Issue #643 增量 2 / #734：massnav→kinematic 桥的合同测试。
    /// 覆盖：位姿喂送正确性（已提交 WorldPositionCm = kinematic body Position2D）、
    /// 位移写权窗口中的单位仍被喂送、半径 SSOT 漂移 fail-fast、kinematicBodyCapacity
    /// 不足 fail-fast、模板配对缺一半 fail-fast、稳态喂送零分配，
    /// 以及碰撞事件路由（注册消费者分发、允许清单校验、丢弃计数、消费异常上抛）。
    /// </summary>
    [TestFixture]
    public sealed class MassNavKinematicBridgeTests
    {
        private const int TeamId = 1;
        private const float TickDt = 0.05f;
        private const float ProfileBodyRadiusCm = 20f;

        [Test]
        public void NavAuthorityMarch_FeedsCommittedWorldPositionIntoKinematicBody()
        {
            using var harness = new KinematicBridgeHarness(
                poseBufferCapacity: 8,
                new BridgeAgentSpec(1000f, 1000f, KinematicParticipation(), PhysicsBody.KinematicCircle(ProfileBodyRadiusCm)));
            Entity agent = harness.Agents[0];
            harness.Simulation.SetAgentNavigationTargetWorldCm(0, 3000f, 1000f, resetRecovery: true);

            harness.RunFixedTick(TickDt);
            Fix64Vec2 committedAfterFirstTick = harness.World.Get<WorldPositionCm>(agent).Value;

            harness.RunFixedTick(TickDt);
            Fix64Vec2 bodyPosition = harness.World.Get<Position2D>(agent).Value;

            Assert.That(harness.FeedSystem.LastFedParticipantCount, Is.EqualTo(1),
                "the kinematic massnav participant must be fed every fixed step");
            Assert.That(bodyPosition.X.RawValue, Is.EqualTo(committedAfterFirstTick.X.RawValue),
                "kinematic body must mirror the committed WorldPositionCm of the previous fixed step verbatim (X)");
            Assert.That(bodyPosition.Y.RawValue, Is.EqualTo(committedAfterFirstTick.Y.RawValue),
                "kinematic body must mirror the committed WorldPositionCm of the previous fixed step verbatim (Y)");

            Fix64Vec2 committedNow = harness.World.Get<WorldPositionCm>(agent).Value;
            Assert.That(committedNow.X.RawValue, Is.Not.EqualTo(committedAfterFirstTick.X.RawValue),
                "sanity: the agent must actually be marching so the mirrored pose is a moving target");
        }

        [Test]
        public void DisplacedAgent_IsStillFedIntoKinematicBody()
        {
            using var harness = new KinematicBridgeHarness(
                poseBufferCapacity: 8,
                new BridgeAgentSpec(1000f, 1000f, KinematicParticipation(), PhysicsBody.KinematicCircle(ProfileBodyRadiusCm)));
            Entity agent = harness.Agents[0];

            // +Y 400cm / 8 tick 位移窗口：申请 tick + 提交 tick 后写权归 Displacement。
            CreateFixedDirectionDisplacement(harness.World, agent, totalDistanceCm: 400, totalTicks: 8, directionDeg: 90f);
            harness.RunFixedTick(TickDt);
            harness.RunFixedTick(TickDt);
            Assert.That(harness.World.Get<PoseAuthority>(agent).Value, Is.EqualTo(PoseAuthorityKind.Displacement),
                "sanity: the displacement window must hold pose authority for this scenario");

            Fix64Vec2 committedDuringWindow = harness.World.Get<WorldPositionCm>(agent).Value;
            harness.RunFixedTick(TickDt);

            Assert.That(harness.FeedSystem.LastFedParticipantCount, Is.EqualTo(1),
                "a unit inside a displacement window must still be fed — the physics view never loses the unit");
            Fix64Vec2 bodyPosition = harness.World.Get<Position2D>(agent).Value;
            Assert.That(bodyPosition.X.RawValue, Is.EqualTo(committedDuringWindow.X.RawValue),
                "kinematic body must mirror the displacement-committed WorldPositionCm (X)");
            Assert.That(bodyPosition.Y.RawValue, Is.EqualTo(committedDuringWindow.Y.RawValue),
                "kinematic body must mirror the displacement-committed WorldPositionCm (Y)");
        }

        [Test]
        public void ColliderRadiusDriftingFromAgentProfile_Throws()
        {
            using var harness = new KinematicBridgeHarness(
                poseBufferCapacity: 8,
                new BridgeAgentSpec(1000f, 1000f, KinematicParticipation(), PhysicsBody.KinematicCircle(ProfileBodyRadiusCm + 5f)));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.RunFixedTick(TickDt))!;
            Assert.That(ex.Message, Does.Contain("radius drift"));
            Assert.That(ex.Message, Does.Contain("bodyRadiusCm"));
        }

        [Test]
        public void ParticipantsExceedingKinematicBodyCapacity_Throw()
        {
            using var harness = new KinematicBridgeHarness(
                poseBufferCapacity: 1,
                new BridgeAgentSpec(1000f, 1000f, KinematicParticipation(), PhysicsBody.KinematicCircle(ProfileBodyRadiusCm)),
                new BridgeAgentSpec(1200f, 1200f, KinematicParticipation(), PhysicsBody.KinematicCircle(ProfileBodyRadiusCm)));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.RunFixedTick(TickDt))!;
            Assert.That(ex.Message, Does.Contain("kinematicBodyCapacity=1"));
        }

        [Test]
        public void KinematicParticipationWithoutPhysicsBody_Throws()
        {
            using var harness = new KinematicBridgeHarness(
                poseBufferCapacity: 8,
                new BridgeAgentSpec(1000f, 1000f, KinematicParticipation(), PhysicsBody.None()));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.RunFixedTick(TickDt))!;
            Assert.That(ex.Message, Does.Contain("missing its kinematic physics half"));
        }

        [Test]
        public void KinematicBodyWithoutMovementParticipation_Throws()
        {
            using var harness = new KinematicBridgeHarness(
                poseBufferCapacity: 8,
                new BridgeAgentSpec(1000f, 1000f, participation: null, PhysicsBody.KinematicCircle(ProfileBodyRadiusCm)));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.RunFixedTick(TickDt))!;
            Assert.That(ex.Message, Does.Contain("no MovementParticipation"));
        }

        [Test]
        public void SteadyStatePoseFeed_AllocatesZeroBytes()
        {
            using var harness = new KinematicBridgeHarness(
                poseBufferCapacity: 8,
                new BridgeAgentSpec(1000f, 1000f, KinematicParticipation(), PhysicsBody.KinematicCircle(ProfileBodyRadiusCm)),
                new BridgeAgentSpec(1200f, 1200f, KinematicParticipation(), PhysicsBody.KinematicCircle(ProfileBodyRadiusCm)));
            harness.Simulation.SetAgentNavigationTargetWorldCm(0, 3000f, 1000f, resetRecovery: true);

            for (int i = 0; i < 5; i++)
            {
                harness.RunFixedTick(TickDt);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                harness.FeedSystem.Update(TickDt);
                harness.DriveSystem.Update(TickDt);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero,
                $"steady-state pose feed must be allocation free, but allocated {allocated} bytes over 200 fixed steps");
        }

        [Test]
        public void ContactEventRouter_DispatchesRegisteredLayerAndCountsUnroutedAllowedLayers()
        {
            int plateIndex = LayerRegistry.Register("BridgeTest.Plate");
            int crateIndex = LayerRegistry.Register("BridgeTest.Crate");
            int agentIndex = LayerRegistry.Register("BridgeTest.Agent");
            var router = new ContactEventRouter2D(new[] { "BridgeTest.Plate", "BridgeTest.Crate" });
            var consumer = new RecordingConsumer();
            router.RegisterConsumer("BridgeTest.Plate", consumer);

            var routed = new ContactEvent2D
            {
                Type = ContactEventType2D.Begin,
                LayerA = new LayerMask(1u << plateIndex, uint.MaxValue),
                LayerB = new LayerMask(1u << agentIndex, uint.MaxValue),
            };
            var unroutedAllowed = new ContactEvent2D
            {
                Type = ContactEventType2D.End,
                LayerA = new LayerMask(1u << crateIndex, uint.MaxValue),
                LayerB = new LayerMask(1u << agentIndex, uint.MaxValue),
            };
            router.Dispatch(new[] { routed, unroutedAllowed });

            Assert.That(consumer.BeginCount, Is.EqualTo(1), "the plate consumer must receive the Begin event exactly once");
            Assert.That(consumer.EndCount, Is.Zero);
            Assert.That(router.GetDroppedEventCount("BridgeTest.Crate"), Is.EqualTo(1),
                "an unrouted event on an allow-listed layer must be counted, not silently discarded");
            Assert.That(router.TotalDroppedEventCount, Is.EqualTo(1));
        }

        [Test]
        public void ContactEventRouter_UnroutedEventOutsideAllowList_Throws()
        {
            int rogueIndex = LayerRegistry.Register("BridgeTest.Rogue");
            var router = new ContactEventRouter2D(Array.Empty<string>());
            var rogueEvent = new ContactEvent2D
            {
                Type = ContactEventType2D.Begin,
                LayerA = new LayerMask(1u << rogueIndex, uint.MaxValue),
                LayerB = new LayerMask(1u << rogueIndex, uint.MaxValue),
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => router.Dispatch(new[] { rogueEvent }))!;
            Assert.That(ex.Message, Does.Contain("pipeline defect"));
        }

        [Test]
        public void ContactEventRouter_DuplicateConsumerRegistration_Throws()
        {
            LayerRegistry.Register("BridgeTest.Duplicate");
            var router = new ContactEventRouter2D(new[] { "BridgeTest.Duplicate" });
            router.RegisterConsumer("BridgeTest.Duplicate", new RecordingConsumer());

            Assert.Throws<InvalidOperationException>(
                () => router.RegisterConsumer("BridgeTest.Duplicate", new RecordingConsumer()));
        }

        [Test]
        public void ContactEventRouter_ConsumerExceptionPropagates()
        {
            LayerRegistry.Register("BridgeTest.Faulty");
            var router = new ContactEventRouter2D(new[] { "BridgeTest.Faulty" });
            router.RegisterConsumer("BridgeTest.Faulty", new ThrowingConsumer());
            int faultyIndex = LayerRegistry.GetIndex("BridgeTest.Faulty");
            var contactEvent = new ContactEvent2D
            {
                Type = ContactEventType2D.Begin,
                LayerA = new LayerMask(1u << faultyIndex, uint.MaxValue),
                LayerB = new LayerMask(1u << faultyIndex, uint.MaxValue),
            };

            Assert.Throws<InvalidOperationException>(
                () => router.Dispatch(new[] { contactEvent }),
                "consumer failures must propagate — contact events are never silently swallowed");
        }

        private static MovementParticipation KinematicParticipation()
        {
            return new MovementParticipation
            {
                Execution = MovementExecutionKind.Nav,
                PhysicsPresence = PhysicsPresenceKind.Kinematic,
                DisplacementAllowed = true,
                DisplacementHandbackSpeedThresholdCmPerSec = 1f,
                DisplacementMaxDurationMs = 10_000,
            };
        }

        private static void CreateFixedDirectionDisplacement(
            World world,
            Entity target,
            int totalDistanceCm,
            int totalTicks,
            float directionDeg)
        {
            world.Create(new DisplacementState
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

        private sealed class RecordingConsumer : IContactEventConsumer2D
        {
            public int BeginCount { get; private set; }
            public int EndCount { get; private set; }

            public void OnContactEvent(in ContactEvent2D contactEvent)
            {
                if (contactEvent.Type == ContactEventType2D.Begin)
                {
                    BeginCount++;
                }
                else
                {
                    EndCount++;
                }
            }
        }

        private sealed class ThrowingConsumer : IContactEventConsumer2D
        {
            public void OnContactEvent(in ContactEvent2D contactEvent)
            {
                throw new InvalidOperationException("consumer failure for propagation test");
            }
        }

        private readonly struct PhysicsBody
        {
            private PhysicsBody(bool hasBody, float colliderRadiusCm)
            {
                HasBody = hasBody;
                ColliderRadiusCm = colliderRadiusCm;
            }

            public bool HasBody { get; }
            public float ColliderRadiusCm { get; }

            public static PhysicsBody KinematicCircle(float colliderRadiusCm) => new(true, colliderRadiusCm);
            public static PhysicsBody None() => new(false, 0f);
        }

        private readonly struct BridgeAgentSpec
        {
            public BridgeAgentSpec(float worldXCm, float worldYCm, MovementParticipation? participation, PhysicsBody body)
            {
                WorldXCm = worldXCm;
                WorldYCm = worldYCm;
                Participation = participation;
                Body = body;
            }

            public float WorldXCm { get; }
            public float WorldYCm { get; }
            public MovementParticipation? Participation { get; }
            public PhysicsBody Body { get; }
        }

        /// <summary>
        /// 无引擎固定步 harness，按 GameEngine SystemGroup 顺序驱动：
        /// SchemaUpdate（写权结算）→ InputCollection（桥喂送 → 物理 kinematic drive）
        /// → PostMovement（求解器步进 + displaced 回灌 + entity sync）→ EffectProcessing（GAS 位移）。
        /// </summary>
        private sealed class KinematicBridgeHarness : IDisposable
        {
            public KinematicBridgeHarness(int poseBufferCapacity, params BridgeAgentSpec[] specs)
            {
                World = World.Create();
                MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
                config.ScenarioRuntime.RuntimeCapacity.DisplacedAgentCapacity = 8;
                Simulation = new MassNavigationSimulationRuntime(config);
                Simulation.BindBoardWorld(
                    new Ludots.Core.Spatial.WorldSizeSpec(new Ludots.Core.Mathematics.WorldAabbCm(0, 0, 10_000, 10_000), 100),
                    MassNavigationOrderChainTests.CreateLoadedChunksForTests(Simulation));

                int layerIndex = LayerRegistry.Register(MassNavigationLayerNames.Agent);
                uint mask = 1u << layerIndex;
                var layer = new MassNavigationAgentLayer(mask, mask);
                int profileId = MassNavigationProfileRegistry.Register("light");

                ShapeStorage = new ShapeDataStorage2D();
                PoseBuffer = new KinematicTargetPoseBuffer2D(poseBufferCapacity);

                Agents = new Entity[specs.Length];
                var seeds = new MassNavigationAgentSeed[specs.Length];
                var controllable = new bool[specs.Length];
                for (int i = 0; i < specs.Length; i++)
                {
                    BridgeAgentSpec spec = specs[i];
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
                            Value = MovementParticipationRules.DeriveInitialPoseAuthority(spec.Participation.Value),
                        });
                    }

                    if (spec.Body.HasBody)
                    {
                        World.Add(entity, Mass2D.Kinematic);
                        World.Add(entity, new Position2D
                        {
                            Value = new Fix64Vec2(Fix64.FromFloat(spec.WorldXCm), Fix64.FromFloat(spec.WorldYCm)),
                        });
                        World.Add(entity, new Velocity2D());
                        World.Add(entity, new Collider2D
                        {
                            Type = ColliderType2D.Circle,
                            ShapeDataIndex = ShapeStorage.RegisterCircle(Fix64.FromFloat(spec.Body.ColliderRadiusCm)),
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
                        bodyRadiusCm: ProfileBodyRadiusCm,
                        speedCmPerSecond: 800f,
                        layer);
                    controllable[i] = true;
                }

                Simulation.RebuildFromAuthoredAgents(World, Agents, seeds, controllable);

                Arbiter = new PoseAuthorityArbiter();
                Arbiter.AddListener(new MassNavigationPoseAuthorityBridge(() => Simulation));
                CommitSystem = new PoseAuthorityCommitSystem(World, Arbiter);
                DisplacementSystem = new DisplacementRuntimeSystem(World, Arbiter);
                FeedSystem = new MassNavKinematicPoseFeedSystem2D(World, () => Simulation, PoseBuffer, ShapeStorage);
                DriveSystem = new KinematicDriveSystem2D(World, PoseBuffer);
            }

            public World World { get; }
            public MassNavigationSimulationRuntime Simulation { get; }
            public PoseAuthorityArbiter Arbiter { get; }
            public PoseAuthorityCommitSystem CommitSystem { get; }
            public DisplacementRuntimeSystem DisplacementSystem { get; }
            public MassNavKinematicPoseFeedSystem2D FeedSystem { get; }
            public KinematicDriveSystem2D DriveSystem { get; }
            public KinematicTargetPoseBuffer2D PoseBuffer { get; }
            public ShapeDataStorage2D ShapeStorage { get; }
            public Entity[] Agents { get; }

            public void RunFixedTick(float dt)
            {
                CommitSystem.Update(dt);
                FeedSystem.Update(dt);
                DriveSystem.Update(dt);
                Simulation.StepNavigationForTests(World, dt, runHardResolve: true);
                Simulation.MassNavigationFlow.SyncDisplacedAgentPoses(World, Simulation.AgentState);
                Simulation.MassNavigationFlow.SyncEntities(World, Simulation.AgentState);
                DisplacementSystem.Update(dt);
            }

            public void Dispose()
            {
                CommitSystem.Dispose();
                DisplacementSystem.Dispose();
                FeedSystem.Dispose();
                DriveSystem.Dispose();
                World.Dispose();
            }
        }
    }
}
