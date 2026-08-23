using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Attachment;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Layers;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;
using Ludots.Core.Spatial;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// nav agent 被 attach 后的求解器级写权合同：Nav→Attached 经边界结算标记 displaced
    /// （求解器跳过积分、entity-sync 跳过回写、每节拍回灌 sink 派生的已提交位姿），
    /// detach 后 Attached→Nav 回灌最终位姿并恢复原目标行军。
    /// </summary>
    [TestFixture]
    public sealed class MassNavigationAttachedAuthorityTests
    {
        private const float TickDt = 0.05f;

        private sealed class AttachedHarness : IDisposable
        {
            public AttachedHarness()
            {
                World = World.Create();
                MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
                config.ScenarioRuntime.RuntimeCapacity.DisplacedAgentCapacity = 4;
                Simulation = new MassNavigationSimulationRuntime(config);
                Simulation.BindBoardWorld(
                    new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100),
                    MassNavigationOrderChainTests.CreateLoadedChunksForTests(Simulation));

                int layerIndex = LayerRegistry.Register(MassNavigationLayerNames.Agent);
                uint mask = 1u << layerIndex;
                var layer = new MassNavigationAgentLayer(mask, mask);
                int profileId = MassNavigationProfileRegistry.Register("attached-tests-light");

                // 载具：无 MovementParticipation 的 nav agent（求解器驱动，等价 mass nav demo 单位）。
                Carrier = World.Create(
                    new MassNavigationAgent { ProfileId = profileId },
                    WorldPositionCm.FromCmFloat(1000f, 1000f),
                    new EntityLayer(layer.CategoryMask, layer.InteractionMask),
                    new FacingDirection { AngleRad = 0f });
                // 乘客：nav agent + MovementParticipation（attach 需要可仲裁写权）。
                Rider = World.Create(
                    new MassNavigationAgent { ProfileId = profileId },
                    WorldPositionCm.FromCmFloat(1500f, 1300f),
                    new EntityLayer(layer.CategoryMask, layer.InteractionMask),
                    new FacingDirection { AngleRad = 0f },
                    new MovementParticipation
                    {
                        PhysicsPresence = PhysicsPresenceKind.None,
                        DisplacementAllowed = true,
                        DisplacementHandbackSpeedThresholdCmPerSec = 10f,
                        DisplacementMaxDurationMs = 2000,
                    },
                    new PoseAuthority { Value = PoseAuthorityKind.Nav });

                Simulation.RebuildFromAuthoredAgents(
                    World,
                    new[] { Carrier, Rider },
                    new[]
                    {
                        new MassNavigationAgentSeed(1, 1000f, 1000f, heavy: false, 1f, 1f, 20f, 800f, layer),
                        new MassNavigationAgentSeed(1, 1500f, 1300f, heavy: false, 1f, 1f, 20f, 800f, layer),
                    },
                    new[] { true, true });

                Arbiter = new PoseAuthorityArbiter();
                Arbiter.AddListener(new MassNavigationPoseAuthorityBridge(() => Simulation));
                CommitSystem = new PoseAuthorityCommitSystem(World, Arbiter);
                Sink = new AttachmentPositionSyncSystem(World, Arbiter);

                int riderIndex = World.Get<MassNavigationAgentIndex>(Rider).Value;
                RiderIndex = riderIndex;
            }

            public World World { get; }
            public MassNavigationSimulationRuntime Simulation { get; }
            public PoseAuthorityArbiter Arbiter { get; }
            public PoseAuthorityCommitSystem CommitSystem { get; }
            public AttachmentPositionSyncSystem Sink { get; }
            public Entity Carrier { get; }
            public Entity Rider { get; }
            public int RiderIndex { get; }

            public void RunFixedTick(bool withSink)
            {
                CommitSystem.Update(TickDt);
                Simulation.StepNavigationForTests(World, TickDt, runHardResolve: true);
                Simulation.MassNavigationFlow.SyncDisplacedAgentPoses(World, Simulation.AgentState);
                Simulation.MassNavigationFlow.SyncEntities(World, Simulation.AgentState);
                if (withSink)
                {
                    Sink.Update(TickDt);
                }
            }

            public Vector2 GetPosition(Entity entity)
            {
                WorldPositionCm position = World.Get<WorldPositionCm>(entity);
                return new Vector2(position.Value.X.ToFloat(), position.Value.Y.ToFloat());
            }

            public void Dispose()
            {
                CommitSystem.Dispose();
                World.Dispose();
            }
        }

        [Test]
        public void AttachedNavAgent_SolverSkipsIntegration_SinkPoseMirrored_DetachResumesMarch()
        {
            using var harness = new AttachedHarness();
            // 乘客获得独立行军目标，attach 后必须停住。
            harness.Simulation.SetAgentNavigationTargetWorldCm(harness.RiderIndex, 9000f, 1300f, resetRecovery: true);
            harness.RunFixedTick(withSink: false);
            Vector2 riderAfterMarch = harness.GetPosition(harness.Rider);
            Assert.That(riderAfterMarch.X, Is.GreaterThan(1500f), "attach 前 nav 正常驱动乘客");

            AttachmentOps.Attach(
                harness.World,
                harness.Arbiter,
                harness.Rider,
                harness.Carrier,
                new AttachedLocalPose
                {
                    OffsetCm = Fix64Vec2.FromInt(0, 200),
                    LocalFacingRad = Fix64.Zero,
                    InheritParentFacing = 0,
                    OffsetRotation = AttachedOffsetRotation.None,
                });

            // 申请 tick：写权待办未结算，nav 仍持有乘客。
            Assert.That(harness.World.Get<PoseAuthority>(harness.Rider).Value, Is.EqualTo(PoseAuthorityKind.Nav));

            // 载具向 +X 行军；乘客每 tick 由 sink 派生（载具位 + (0,200)）。
            int carrierIndex = harness.World.Get<MassNavigationAgentIndex>(harness.Carrier).Value;
            harness.Simulation.SetAgentNavigationTargetWorldCm(carrierIndex, 9000f, 1000f, resetRecovery: true);
            for (int i = 0; i < 20; i++)
            {
                harness.RunFixedTick(withSink: true);
                Assert.That(harness.World.Get<PoseAuthority>(harness.Rider).Value, Is.EqualTo(PoseAuthorityKind.Attached),
                    "边界结算后 Attached 持有");
                Assert.That(harness.Simulation.MassNavigationFlow.IsAgentDisplaced(harness.RiderIndex), Is.True,
                    "求解器必须跳过 attached agent 的积分");

                Vector2 carrier = harness.GetPosition(harness.Carrier);
                Vector2 rider = harness.GetPosition(harness.Rider);
                Assert.That(rider.X, Is.EqualTo(carrier.X).Within(1.5f), "乘客 X 随载具（sink 派生 + 求解器回灌镜像）");
                Assert.That(rider.Y, Is.EqualTo(carrier.Y + 200f).Within(1.5f), "乘客保持局部偏移 (0, 200)");
            }

            Vector2 riderBeforeDetach = harness.GetPosition(harness.Rider);
            AttachmentOps.Detach(harness.World, harness.Arbiter, harness.Rider, DetachPlacement.KeepWorldPose, 0);

            harness.RunFixedTick(withSink: true);
            Assert.That(harness.World.Get<PoseAuthority>(harness.Rider).Value, Is.EqualTo(PoseAuthorityKind.Nav),
                "detach 归还写权");
            Assert.That(harness.Simulation.MassNavigationFlow.IsAgentDisplaced(harness.RiderIndex), Is.False);

            for (int i = 0; i < 10; i++)
            {
                harness.RunFixedTick(withSink: true);
            }

            Vector2 riderAfterResume = harness.GetPosition(harness.Rider);
            Assert.Multiple(() =>
            {
                Assert.That(riderAfterResume.X, Is.GreaterThan(riderBeforeDetach.X),
                    "交还后乘客继续向原目标 (9000, 1300) 行军");
                Assert.That(riderAfterResume.Y, Is.EqualTo(1300f).Within(400f),
                    "乘客回到原目标 Y 走廊（resetRecovery 恢复路径）");
            });
        }
    }
}
