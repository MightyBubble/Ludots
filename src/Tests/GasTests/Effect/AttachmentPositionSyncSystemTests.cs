using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Attachment;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// AttachmentPositionSyncSystem 合同：父∘局部位姿派生（刚性零插值）、深度序
    /// （父先子后一步一致）、恒重算（无 parent-moved 门——位姿写者大量存在于 sink 之后，
    /// Previous 比对会冻结位置依赖子）、独立朝向保持、孤儿自愈。
    /// </summary>
    [TestFixture]
    public sealed class AttachmentPositionSyncSystemTests
    {
        private static AttachedLocalPose Pose(int xCm, int yCm, bool inheritFacing = false, int facingDeg = 0, AttachedOffsetRotation rotation = AttachedOffsetRotation.None)
        {
            return new AttachedLocalPose
            {
                OffsetCm = Fix64Vec2.FromInt(xCm, yCm),
                LocalFacingRad = Fix64.FromInt(facingDeg) * (Fix64.Pi / Fix64.FromInt(180)),
                InheritParentFacing = inheritFacing ? (byte)1 : (byte)0,
                OffsetRotation = rotation,
            };
        }

        [Test]
        public void DepthOrder_MultiLevelChain_IsConsistentWithinOneUpdate()
        {
            using World world = World.Create();
            Entity chassis = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(0, 0) },
                new FacingDirection { AngleRad = 0f });
            Entity turret = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero },
                new FacingDirection { AngleRad = 0f });
            Entity barrel = world.Create(WorldPositionCm.FromCm(0, 0), new PreviousWorldPositionCm { Value = Fix64Vec2.Zero });
            AttachmentOps.Attach(world, null, turret, chassis, Pose(0, 0, inheritFacing: true));
            AttachmentOps.Attach(world, null, barrel, turret, Pose(220, 0, inheritFacing: true, rotation: AttachedOffsetRotation.ParentFacing));
            using var sink = new AttachmentPositionSyncSystem(world);

            // 底盘移动 + 朝向转 90°（炮塔安装位随底盘朝向旋转是炮管链的输入）。
            world.Get<WorldPositionCm>(chassis) = WorldPositionCm.FromCm(1000, 500);
            world.Get<FacingDirection>(chassis).AngleRad = (float)(Math.PI / 2);
            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(sink.LastMaxDepth, Is.EqualTo(1), "炮管是孙层（深度 1）");
                Assert.That(world.Get<WorldPositionCm>(turret).Value, Is.EqualTo(Fix64Vec2.FromInt(1000, 500)),
                    "炮塔零偏移锚定在底盘");
                // 炮管：炮塔位姿 ∘ local(220,0) 旋转炮塔朝向。炮塔继承底盘朝向 π/2 → 偏移 (0,220)。
                Assert.That(world.Get<WorldPositionCm>(barrel).Value.X.ToFloat(), Is.EqualTo(1000f).Within(2f));
                Assert.That(world.Get<WorldPositionCm>(barrel).Value.Y.ToFloat(), Is.EqualTo(720f).Within(2f));
                Assert.That(world.Get<FacingDirection>(turret).AngleRad, Is.EqualTo((float)(Math.PI / 2)).Within(1e-4f));
                Assert.That(world.Get<FacingDirection>(barrel).AngleRad, Is.EqualTo((float)(Math.PI / 2)).Within(1e-4f));
                Assert.That(world.Get<WorldPositionCm>(barrel).Value == world.Get<PreviousWorldPositionCm>(barrel).Value, Is.True,
                    "刚性零插值：Current 与 Previous 同值");
            });
        }

        [Test]
        public void StaticParent_PositionDependentChildren_RecomputeIdempotently()
        {
            using World world = World.Create();
            Entity hall = world.Create(
                WorldPositionCm.FromCm(5000, 5000),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(5000, 5000) });
            Entity annex = world.Create(WorldPositionCm.FromCm(5700, 5000), new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(5700, 5000) });
            Entity tower = world.Create(WorldPositionCm.FromCm(4650, 5600), new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(4650, 5600) });
            AttachmentOps.Attach(world, null, annex, hall, Pose(700, 0));
            AttachmentOps.Attach(world, null, tower, hall, Pose(-350, 600));
            using var sink = new AttachmentPositionSyncSystem(world);

            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(sink.LastAppliedCount, Is.EqualTo(2), "恒重算：静态父的位置依赖子每步都重派生");
                Assert.That(world.Get<WorldPositionCm>(annex).Value, Is.EqualTo(Fix64Vec2.FromInt(5700, 5000)),
                    "静态父重派生幂等：位置不变");
                Assert.That(world.Get<WorldPositionCm>(tower).Value, Is.EqualTo(Fix64Vec2.FromInt(4650, 5600)));
            });
        }

        [Test]
        public void PostSinkWriterTiming_PositionDependentChild_StillFollows()
        {
            // HIGH-1 回归：位姿写者在 sink 之后运行（PostMovement 后段 nav 求解器、
            // AbilityActivation 订单、EffectProcessing 位移）。引擎每步首 SavePrevious 把
            // Previous=Current 抹平后 sink 运行，父的 Current==Previous 是常态而非"未移动"。
            // 旧 Previous 比对门在该时序下会让位置依赖子永久冻结在挂接位姿。
            using World world = World.Create();
            Entity parent = world.Create(
                WorldPositionCm.FromCm(1000, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) });
            Entity child = world.Create(
                WorldPositionCm.FromCm(1200, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1200, 0) });
            AttachmentOps.Attach(world, null, child, parent, Pose(200, 0));
            using var sink = new AttachmentPositionSyncSystem(world);

            // 步 1：sink 派生（父 1000,0 → 子 1200,0）。
            sink.Update(1f / 60f);
            Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(1200, 0)));

            // 步 2 模拟引擎时序：后置写者把父推到 1800，下一 步首 SavePrevious 抹平 Previous=1800，
            // sink 运行时 Current==Previous——子必须仍跟随到 2000（旧门在此冻结）。
            world.Get<WorldPositionCm>(parent) = WorldPositionCm.FromCm(1800, 0);
            world.Get<PreviousWorldPositionCm>(parent) = new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1800, 0) };
            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(sink.LastAppliedCount, Is.EqualTo(1), "Current==Previous 不得跳过（后置写者时序）");
                Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(2000, 0)),
                    "子跟随父的已提交位姿");
                Assert.That(world.Get<PreviousWorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(2000, 0)),
                    "刚性零插值");
            });
        }

        [Test]
        public void RotationInPlace_FacingDependentChild_Recomputed()
        {
            using World world = World.Create();
            Entity parent = world.Create(
                WorldPositionCm.FromCm(1000, 1000),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1000, 1000) },
                new FacingDirection { AngleRad = 0f });
            Entity child = world.Create(WorldPositionCm.FromCm(1220, 1000), new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1220, 1000) });
            AttachmentOps.Attach(world, null, child, parent, Pose(220, 0, rotation: AttachedOffsetRotation.ParentFacing));
            using var sink = new AttachmentPositionSyncSystem(world);

            // 原地旋转：位置未动（Current==Previous），朝向依赖偏移仍重算。
            world.Get<FacingDirection>(parent).AngleRad = (float)(Math.PI / 2);
            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(sink.LastAppliedCount, Is.EqualTo(1));
                Assert.That(world.Get<WorldPositionCm>(child).Value.X.ToFloat(), Is.EqualTo(1000f).Within(2f));
                Assert.That(world.Get<WorldPositionCm>(child).Value.Y.ToFloat(), Is.EqualTo(1220f).Within(2f));
            });
        }

        [Test]
        public void IndependentFacing_TurretKeepsOwnAimWhileAnchorFollows()
        {
            using World world = World.Create();
            Entity chassis = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero },
                new FacingDirection { AngleRad = 0f });
            Entity turret = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero },
                new FacingDirection { AngleRad = (float)(Math.PI / 4) });
            AttachmentOps.Attach(world, null, turret, chassis, Pose(0, 0, inheritFacing: false));
            using var sink = new AttachmentPositionSyncSystem(world);

            world.Get<WorldPositionCm>(chassis) = WorldPositionCm.FromCm(800, 300);
            world.Get<FacingDirection>(chassis).AngleRad = (float)Math.PI;
            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(world.Get<WorldPositionCm>(turret).Value, Is.EqualTo(Fix64Vec2.FromInt(800, 300)));
                Assert.That(world.Get<FacingDirection>(turret).AngleRad, Is.EqualTo((float)(Math.PI / 4)),
                    "非继承朝向：炮塔独立瞄准不被 sink 改写");
            });
        }

        [Test]
        public void OwnFacingRotation_ChildOrbitsParentByItsOwnFacing()
        {
            using World world = World.Create();
            Entity parent = world.Create(
                WorldPositionCm.FromCm(500, 500),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(500, 500) });
            Entity child = world.Create(
                WorldPositionCm.FromCm(950, 500),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(950, 500) },
                new FacingDirection { AngleRad = (float)(Math.PI / 2) });
            AttachmentOps.Attach(world, null, child, parent, Pose(450, 0, rotation: AttachedOffsetRotation.OwnFacing));
            using var sink = new AttachmentPositionSyncSystem(world);

            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(world.Get<WorldPositionCm>(child).Value.X.ToFloat(), Is.EqualTo(500f).Within(2f));
                Assert.That(world.Get<WorldPositionCm>(child).Value.Y.ToFloat(), Is.EqualTo(950f).Within(2f));
            });
        }

        [Test]
        public void DeadParent_OrphanChildSelfHeals_AndHandsBackAttachedAuthority()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity child = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero },
                new PoseAuthority { Value = PoseAuthorityKind.Attached });
            var arbiter = new PoseAuthorityArbiter();
            AttachmentOps.Attach(world, arbiter, child, parent, Pose(0, 0));
            using var sink = new AttachmentPositionSyncSystem(world, arbiter);

            world.Destroy(parent);
            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(sink.LastOrphanCleanupCount, Is.EqualTo(1));
                Assert.That(world.Has<ChildOf>(child), Is.False);
                Assert.That(world.Has<AttachedLocalPose>(child), Is.False);
                Assert.That(arbiter.PendingTransitionCount, Is.EqualTo(1), "Attached 写权必须排队归还 Nav");
            });

            using var commitSystem = new PoseAuthorityCommitSystem(world, arbiter);
            commitSystem.Update(1f / 60f);
            Assert.That(world.Get<PoseAuthority>(child).Value, Is.EqualTo(PoseAuthorityKind.Nav));
        }

        [Test]
        public void DeadParent_ManifestationWithLifecycleMarker_IsLeftToItsLifecycleSystem()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity manifestation = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero },
                new Ludots.Core.Gameplay.Spawning.DestroyWhenParentExecutionEnds());
            AttachmentOps.Attach(world, null, manifestation, parent, Pose(0, 0));
            using var sink = new AttachmentPositionSyncSystem(world);

            world.Destroy(parent);
            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(sink.LastOrphanCleanupCount, Is.Zero, "带自管生命周期标记的子实体由其生命周期系统处置");
                Assert.That(world.Has<ChildOf>(manifestation), Is.True);
            });
        }

        [Test]
        public void ChildWithNonAttachedPoseAuthority_FailsFast()
        {
            using World world = World.Create();
            Entity parent = world.Create(
                WorldPositionCm.FromCm(100, 100),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(100, 100) });
            Entity child = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero },
                new PoseAuthority { Value = PoseAuthorityKind.Nav },
                new ChildOf { Parent = parent });
            world.Add(child, Pose(0, 0));
            using var sink = new AttachmentPositionSyncSystem(world);

            world.Get<WorldPositionCm>(parent) = WorldPositionCm.FromCm(200, 200);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => sink.Update(1f / 60f))!;

            Assert.That(error.Message, Does.StartWith(AttachmentPositionSyncSystem.PoseAuthorityConflictError));
        }

        [Test]
        public void ScratchCapacityExceeded_FailsClosedWithoutDroppingSilently()
        {
            using World world = World.Create();
            Entity parent = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero });
            const int capacity = 2;
            for (int i = 0; i < capacity + 1; i++)
            {
                Entity child = world.Create(
                    WorldPositionCm.FromCm(0, 0),
                    new PreviousWorldPositionCm { Value = Fix64Vec2.Zero });
                AttachmentOps.Attach(world, null, child, parent, Pose(i * 10, 0));
            }

            using var sink = new AttachmentPositionSyncSystem(world, scratchCapacity: capacity);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => sink.Update(1f / 60f))!;
            Assert.That(error.Message, Does.StartWith(AttachmentPositionSyncSystem.CapacityExceededError));
            Assert.That(error.Message, Does.Contain($"capacity={capacity}"));
        }

        [Test]
        public void DeepAttachmentTree_UpdatesInDepthOrder_ParentBeforeChild()
        {
            using World world = World.Create();
            Entity root = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero },
                new FacingDirection { AngleRad = 0f });
            Entity mid = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero },
                new FacingDirection { AngleRad = 0f });
            Entity leaf = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero },
                new FacingDirection { AngleRad = 0f });
            AttachmentOps.Attach(world, null, mid, root, Pose(100, 0, inheritFacing: true));
            AttachmentOps.Attach(world, null, leaf, mid, Pose(50, 0, inheritFacing: true, rotation: AttachedOffsetRotation.ParentFacing));
            using var sink = new AttachmentPositionSyncSystem(world);

            world.Get<WorldPositionCm>(root) = WorldPositionCm.FromCm(1000, 0);
            world.Get<FacingDirection>(root).AngleRad = (float)(Math.PI / 2);
            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(sink.LastMaxDepth, Is.EqualTo(1));
                Assert.That(sink.LastAppliedCount, Is.EqualTo(2));
                Assert.That(world.Get<WorldPositionCm>(mid).Value.X.ToFloat(), Is.EqualTo(1100f).Within(1f));
                Assert.That(world.Get<WorldPositionCm>(mid).Value.Y.ToFloat(), Is.EqualTo(0f).Within(1f));
                // leaf offset (50,0) 随 mid 朝向 π/2 旋转 → 相对 mid 为 (0,50)
                Assert.That(world.Get<WorldPositionCm>(leaf).Value.X.ToFloat(), Is.EqualTo(1100f).Within(1f));
                Assert.That(world.Get<WorldPositionCm>(leaf).Value.Y.ToFloat(), Is.EqualTo(50f).Within(1f));
            });
        }
    }
}
