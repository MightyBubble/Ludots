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
    /// （父先子后一步一致）、parent-moved 门（静态父位置依赖子树跳过、朝向依赖子除外）、
    /// 独立朝向保持、孤儿自愈。
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
        public void ParentMovedGate_StaticPositionDependentChildren_SkipWholeSubtree()
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
            WorldPositionCm annexBefore = world.Get<WorldPositionCm>(annex);
            WorldPositionCm towerBefore = world.Get<WorldPositionCm>(tower);

            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(sink.LastGateSkippedCount, Is.EqualTo(2), "静态父 + 无朝向依赖 → 整树跳过");
                Assert.That(sink.LastAppliedCount, Is.Zero);
                Assert.That(world.Get<WorldPositionCm>(annex).Value, Is.EqualTo(annexBefore.Value));
                Assert.That(world.Get<WorldPositionCm>(tower).Value, Is.EqualTo(towerBefore.Value));
            });
        }

        [Test]
        public void ParentMovedGate_FacingDependentChild_RecomputesOnRotationInPlace()
        {
            using World world = World.Create();
            Entity parent = world.Create(
                WorldPositionCm.FromCm(1000, 1000),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1000, 1000) },
                new FacingDirection { AngleRad = 0f });
            Entity child = world.Create(WorldPositionCm.FromCm(1220, 1000), new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1220, 1000) });
            AttachmentOps.Attach(world, null, child, parent, Pose(220, 0, rotation: AttachedOffsetRotation.ParentFacing));
            using var sink = new AttachmentPositionSyncSystem(world);

            // 原地旋转：位置未动（Current==Previous），但朝向依赖子树必须重算。
            world.Get<FacingDirection>(parent).AngleRad = (float)(Math.PI / 2);
            sink.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(sink.LastGateSkippedCount, Is.Zero, "朝向依赖子不进 parent-moved 门");
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
    }
}
