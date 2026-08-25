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
    /// 挂接链唯一 mass nav 约定的求解器级合同：nav 成员 attach 时成员身份摘除
    /// （SuspendedNavMembership 快照，绑定系统 rebuild 回收求解器槽位——挂接链上只剩
    /// 独立移动的根一个 agent），sink 从载具本步最新位姿派生乘客；
    /// detach 回放成员身份、按已提交位姿重播种，写权 Nav↔Attached 经边界结算切换。
    /// </summary>
    [TestFixture]
    public sealed class MassNavigationAttachedAuthorityTests
    {
        private const float TickDt = 0.05f;

        private sealed class AttachedHarness : IDisposable
        {
            private readonly int _profileId;
            private readonly MassNavigationAgentLayer _layer;

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
                _layer = layer;
                int profileId = MassNavigationProfileRegistry.Register("attached-tests-light");
                _profileId = profileId;

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
                        new MassNavigationAgentSeed(profileId, 1000f, 1000f, heavy: false, 1f, 1f, 20f, 800f, layer),
                        new MassNavigationAgentSeed(1, 1500f, 1300f, heavy: false, 1f, 1f, 20f, 800f, layer),
                    },
                    new[] { true, true });

                Arbiter = new PoseAuthorityArbiter();
                Arbiter.AddListener(new MassNavigationPoseAuthorityBridge(() => Simulation));
                CommitSystem = new PoseAuthorityCommitSystem(World, Arbiter);
                Sink = new AttachmentPositionSyncSystem(World, 64, Arbiter);

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

            /// <summary>
            /// 镜像 MassNavigationAuthoredAgentBindingSystem 的 rebuild 路径：
            /// 按当前在场成员（MassNavigationAgent 组件）与其已提交位姿重建求解器 agent 集。
            /// </summary>
            public void RebuildBoundAgents(params Entity[] entities)
            {
                var seeds = new MassNavigationAgentSeed[entities.Length];
                var controllable = new bool[entities.Length];
                for (int i = 0; i < entities.Length; i++)
                {
                    WorldPositionCm position = World.Get<WorldPositionCm>(entities[i]);
                    seeds[i] = new MassNavigationAgentSeed(
                        _profileId,
                        position.Value.X.ToFloat(),
                        position.Value.Y.ToFloat(),
                        heavy: false,
                        1f,
                        1f,
                        20f,
                        800f,
                        _layer);
                    controllable[i] = true;
                }

                Simulation.RebuildFromAuthoredAgents(World, entities, seeds, controllable);
            }

            public void RunFixedTick(bool withSink)
            {
                // 引擎真实时序：写权结算 → 求解器推进并同步载具 → attachment sink 派生乘客。
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
        public void AttachedNavMember_SuspendsMembership_SinkPoseMirrored_DetachRestoresMembership()
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

            // 挂接链唯一 mass nav：子实体成员身份即刻摘除并留快照（写权待办在下一边界结算）。
            Assert.Multiple(() =>
            {
                Assert.That(harness.World.Has<MassNavigationAgent>(harness.Rider), Is.False, "乘客成员身份摘除");
                Assert.That(harness.World.Has<MassNavigationAgentIndex>(harness.Rider), Is.False);
                Assert.That(harness.World.Has<MassNavigationAgentProfile>(harness.Rider), Is.False);
                Assert.That(harness.World.Has<SuspendedNavMembership>(harness.Rider), Is.True, "挂起快照在场");
                Assert.That(harness.World.Get<PoseAuthority>(harness.Rider).Value, Is.EqualTo(PoseAuthorityKind.Nav),
                    "申请 tick：写权待办未结算");
            });

            // 镜像绑定系统行为：成员数变化触发 rebuild，求解器只剩载具一个 agent。
            harness.RebuildBoundAgents(harness.Carrier);
            Assert.That(harness.Simulation.AgentState.TotalAgents, Is.EqualTo(1), "求解器槽位回收，仅载具为 agent");

            // 载具向 +X 行军；乘客每 tick 由 sink 从载具本步最新位姿派生（载具位 + (0,200)）。
            int carrierIndex = harness.World.Get<MassNavigationAgentIndex>(harness.Carrier).Value;
            harness.Simulation.SetAgentNavigationTargetWorldCm(carrierIndex, 9000f, 1000f, resetRecovery: true);
            for (int i = 0; i < 20; i++)
            {
                harness.RunFixedTick(withSink: true);
                Assert.That(harness.World.Get<PoseAuthority>(harness.Rider).Value, Is.EqualTo(PoseAuthorityKind.Attached),
                    "边界结算后 Attached 持有");

                Vector2 rider = harness.GetPosition(harness.Rider);
                Vector2 carrierLatest = harness.GetPosition(harness.Carrier);
                Assert.That(rider.X, Is.EqualTo(carrierLatest.X).Within(0.5f), "乘客 X 镜像载具本步最新位姿");
                Assert.That(rider.Y, Is.EqualTo(carrierLatest.Y + 200f).Within(0.5f), "乘客保持局部偏移 (0, 200)");
            }

            Vector2 riderBeforeDetach = harness.GetPosition(harness.Rider);
            AttachmentOps.Detach(harness.World, harness.Arbiter, harness.Rider, DetachPlacement.KeepWorldPose, 0);

            Assert.Multiple(() =>
            {
                Assert.That(harness.World.Has<MassNavigationAgent>(harness.Rider), Is.True, "解除恢复成员身份");
                Assert.That(harness.World.Has<MassNavigationAgentIndex>(harness.Rider), Is.False,
                    "旧 Index 不复用，由绑定系统重播种");
                Assert.That(harness.World.Has<SuspendedNavMembership>(harness.Rider), Is.False);
            });

            // 镜像绑定系统：两位成员按当前位姿重播种，写权边界归还 Nav。
            harness.RebuildBoundAgents(harness.Carrier, harness.Rider);
            Assert.That(harness.Simulation.AgentState.TotalAgents, Is.EqualTo(2));
            harness.RunFixedTick(withSink: true);
            Assert.That(harness.World.Get<PoseAuthority>(harness.Rider).Value, Is.EqualTo(PoseAuthorityKind.Nav),
                "detach 归还写权");

            int riderIndex = harness.World.Get<MassNavigationAgentIndex>(harness.Rider).Value;
            harness.Simulation.SetAgentNavigationTargetWorldCm(riderIndex, 9000f, 1300f, resetRecovery: true);
            for (int i = 0; i < 10; i++)
            {
                harness.RunFixedTick(withSink: true);
            }

            Vector2 riderAfterResume = harness.GetPosition(harness.Rider);
            Assert.Multiple(() =>
            {
                Assert.That(riderAfterResume.X, Is.GreaterThan(riderBeforeDetach.X),
                    "重新播种后乘客向新目标 (9000, 1300) 行军");
                Assert.That(riderAfterResume.Y, Is.EqualTo(1300f).Within(400f),
                    "乘客回到目标 Y 走廊（resetRecovery 恢复路径）");
            });
        }
    }
}
