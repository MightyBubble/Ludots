using System;
using System.Text.Json.Nodes;
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
    /// attachment 原子 op（Attach/Detach/RemoveParent）的事务化合同：
    /// 提交语义（绑定/局部姿/派生位姿/写权）、回滚语义（含写权待办撤销）、
    /// 环检测 fail-fast、容量硬顶、同事务 attach→detach 抵消。
    /// </summary>
    [TestFixture]
    public sealed class EntityAttachmentTests
    {
        private static AttachedLocalPose OffsetPose(int xCm, int yCm, bool inheritFacing = false, int facingDeg = 0, AttachedOffsetRotation rotation = AttachedOffsetRotation.None)
        {
            return new AttachedLocalPose
            {
                OffsetCm = Fix64Vec2.FromInt(xCm, yCm),
                LocalFacingRad = Fix64.FromInt(facingDeg) * (Fix64.Pi / Fix64.FromInt(180)),
                InheritParentFacing = inheritFacing ? (byte)1 : (byte)0,
                OffsetRotation = rotation,
            };
        }

        private static void AttachDirect(World world, PoseAuthorityArbiter arbiter, Entity child, Entity parent, in AttachedLocalPose pose)
        {
            AttachmentOps.Attach(world, arbiter, child, parent, in pose);
        }

        [Test]
        public void Attach_Commit_WritesBindingLocalPoseAndDerivedPose()
        {
            using World world = World.Create();
            Entity parent = world.Create(
                WorldPositionCm.FromCm(1000, 2000),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(900, 1900) },
                new FacingDirection { AngleRad = (float)(Math.PI / 2) });
            Entity child = world.Create(WorldPositionCm.FromCm(0, 0));
            using var transaction = new EffectPhaseSideEffectTransaction(
                world, null, null, null, null, attributeEntityCapacity: 4);

            transaction.Begin();
            transaction.StageAttach(child, parent, OffsetPose(0, 140, inheritFacing: true, facingDeg: 90));
            transaction.Commit();

            Assert.Multiple(() =>
            {
                Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(parent));
                Assert.That(world.Get<ChildrenBuffer>(parent).Contains(in child), Is.True);
                Assert.That(world.Get<AttachedLocalPose>(child).OffsetCm, Is.EqualTo(Fix64Vec2.FromInt(0, 140)));
                // inherit facing: child facing = parent facing (π/2) + local (π/2) = π
                Assert.That(world.Get<FacingDirection>(child).AngleRad, Is.EqualTo((float)Math.PI).Within(1e-4f));
                // derived pose: parent (1000, 2000) + fixed offset (0, 140)
                Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(1000, 2140)));
                Assert.That(world.Get<PreviousWorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(1000, 2140)));
            });
        }

        [Test]
        public void Attach_WithNavAuthority_CommitsPendingAtBoundaryAndRollbackRemovesIt()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity child = world.Create(
                WorldPositionCm.FromCm(500, 500),
                new MovementParticipation
                {
                    PhysicsPresence = PhysicsPresenceKind.None,
                    DisplacementAllowed = true,
                    DisplacementHandbackSpeedThresholdCmPerSec = 10f,
                    DisplacementMaxDurationMs = 2000,
                },
                new PoseAuthority { Value = PoseAuthorityKind.Nav });
            var arbiter = new PoseAuthorityArbiter();
            using var commitSystem = new PoseAuthorityCommitSystem(world, arbiter);
            using var transaction = new EffectPhaseSideEffectTransaction(
                world, null, null, null, null, attributeEntityCapacity: 4, poseAuthorityArbiter: arbiter);

            transaction.Begin();
            transaction.StageAttach(child, parent, OffsetPose(0, 0));
            Assert.That(arbiter.PendingTransitionCount, Is.EqualTo(1), "attach must queue the authority grant as a boundary pending");
            transaction.Commit();

            Assert.Multiple(() =>
            {
                Assert.That(world.Get<PoseAuthority>(child).Value, Is.EqualTo(PoseAuthorityKind.Nav),
                    "authority switch only lands at the fixed-step boundary");
                Assert.That(arbiter.PendingTransitionCount, Is.EqualTo(1),
                    "successful commit leaves the pending for PoseAuthorityCommitSystem");
            });

            commitSystem.Update(1f / 60f);
            Assert.That(world.Get<PoseAuthority>(child).Value, Is.EqualTo(PoseAuthorityKind.Attached));

            // 回滚路径：重新 stage 一次 attach 再回滚，待办必须被摘除，世界保持原状。
            transaction.Begin();
            Entity otherParent = world.Create(WorldPositionCm.FromCm(300, 300));
            transaction.StageAttach(child, otherParent, OffsetPose(10, 10));
            transaction.Rollback();

            Assert.Multiple(() =>
            {
                Assert.That(arbiter.PendingTransitionCount, Is.Zero, "rollback must remove the staged authority pending");
                Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(parent));
                Assert.That(world.Get<PoseAuthority>(child).Value, Is.EqualTo(PoseAuthorityKind.Attached));
            });
        }

        [Test]
        public void Attach_WhenWorldChangesAfterStaging_FailsClosedAndRollsBack()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(1000, 2000));
            Entity child = world.Create(WorldPositionCm.FromCm(5, 5));
            using var transaction = new EffectPhaseSideEffectTransaction(
                world, null, null, null, null, attributeEntityCapacity: 4);

            transaction.Begin();
            transaction.StageAttach(child, parent, OffsetPose(0, 140));
            world.Get<WorldPositionCm>(parent) = WorldPositionCm.FromCm(2000, 3000);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => transaction.Commit())!;
            transaction.Rollback();

            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.StartWith(EffectPhaseSideEffectTransaction.RelationTargetInvalidError));
                Assert.That(world.Has<ChildOf>(child), Is.False);
                Assert.That(world.Has<ChildrenBuffer>(parent), Is.False);
                Assert.That(world.Has<AttachedLocalPose>(child), Is.False);
                Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(5, 5)));
            });
        }

        [Test]
        public void Detach_PerimeterRing_LandsAtRingSlotAndHandsAuthorityBack()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(4000, 4000));
            Entity first = world.Create(
                WorldPositionCm.FromCm(4000, 4000),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(4000, 4000) },
                new PoseAuthority { Value = PoseAuthorityKind.Attached });
            Entity second = world.Create(
                WorldPositionCm.FromCm(4000, 4000),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(4000, 4000) });
            AttachDirect(world, new PoseAuthorityArbiter(), first, parent, OffsetPose(0, 0));
            AttachDirect(world, new PoseAuthorityArbiter(), second, parent, OffsetPose(0, 0));
            var arbiter = new PoseAuthorityArbiter();
            using var commitSystem = new PoseAuthorityCommitSystem(world, arbiter);
            using var transaction = new EffectPhaseSideEffectTransaction(
                world, null, null, null, null, attributeEntityCapacity: 4, poseAuthorityArbiter: arbiter);

            transaction.Begin();
            transaction.StageDetach(first, DetachPlacement.ParentPerimeterRing, perimeterRadiusCm: 300);
            transaction.StageDetach(second, DetachPlacement.ParentPerimeterRing, perimeterRadiusCm: 300);
            transaction.Commit();

            // 两个子实体在周界环上占不同槽位（槽序 = ChildrenBuffer 快照序：first=0, second=1）。
            Fix64Vec2 firstOffset = world.Get<WorldPositionCm>(first).Value - Fix64Vec2.FromInt(4000, 4000);
            Fix64Vec2 secondOffset = world.Get<WorldPositionCm>(second).Value - Fix64Vec2.FromInt(4000, 4000);
            Assert.Multiple(() =>
            {
                Assert.That(world.Has<ChildOf>(first), Is.False);
                Assert.That(world.Has<ChildOf>(second), Is.False);
                Assert.That(world.Has<AttachedLocalPose>(first), Is.False);
                Assert.That(world.Get<ChildrenBuffer>(parent).Count, Is.Zero);
                Assert.That(firstOffset.X.ToFloat(), Is.EqualTo(300f).Within(1f));
                Assert.That(firstOffset.Y.ToFloat(), Is.EqualTo(0f).Within(1f));
                Assert.That(secondOffset.X.ToFloat(), Is.EqualTo(-300f).Within(1f));
                Assert.That(secondOffset.Y.ToFloat(), Is.EqualTo(0f).Within(1f));
                Assert.That(arbiter.PendingTransitionCount, Is.EqualTo(1), "only the Attached authority holder queues a handback");
            });

            commitSystem.Update(1f / 60f);
            Assert.That(world.Get<PoseAuthority>(first).Value, Is.EqualTo(PoseAuthorityKind.Nav));
        }

        [Test]
        public void Detach_Rollback_RestoresAttachmentAndCancelsHandbackPending()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(1000, 1000));
            Entity child = world.Create(
                WorldPositionCm.FromCm(1000, 1000),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1000, 1000) },
                new PoseAuthority { Value = PoseAuthorityKind.Attached });
            var setupArbiter = new PoseAuthorityArbiter();
            AttachDirect(world, setupArbiter, child, parent, OffsetPose(0, 50));
            AttachedLocalPose originalPose = world.Get<AttachedLocalPose>(child);

            var arbiter = new PoseAuthorityArbiter();
            using var transaction = new EffectPhaseSideEffectTransaction(
                world, null, null, null, null, attributeEntityCapacity: 4, poseAuthorityArbiter: arbiter);

            transaction.Begin();
            transaction.StageDetach(child, DetachPlacement.ParentPerimeterRing, 300);
            world.Get<WorldPositionCm>(child) = WorldPositionCm.FromCm(77, 77);

            Assert.Throws<InvalidOperationException>(() => transaction.Commit());
            transaction.Rollback();

            Assert.Multiple(() =>
            {
                Assert.That(arbiter.PendingTransitionCount, Is.Zero, "rollback cancels the staged handback pending");
                Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(parent));
                Assert.That(world.Get<AttachedLocalPose>(child).OffsetCm, Is.EqualTo(originalPose.OffsetCm));
                Assert.That(world.Get<PoseAuthority>(child).Value, Is.EqualTo(PoseAuthorityKind.Attached));
                Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(77, 77)),
                    "rollback keeps the (externally written) current pose value; attachment state is what restores");
            });
        }

        [Test]
        public void AttachThenDetach_InSameTransaction_LeavesEntityUnattachedWithNoPendings()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(800, 800));
            Entity child = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new MovementParticipation
                {
                    PhysicsPresence = PhysicsPresenceKind.None,
                    DisplacementAllowed = true,
                    DisplacementHandbackSpeedThresholdCmPerSec = 10f,
                    DisplacementMaxDurationMs = 2000,
                },
                new PoseAuthority { Value = PoseAuthorityKind.Nav });
            var arbiter = new PoseAuthorityArbiter();
            using var transaction = new EffectPhaseSideEffectTransaction(
                world, null, null, null, null, attributeEntityCapacity: 4, poseAuthorityArbiter: arbiter);

            transaction.Begin();
            transaction.StageAttach(child, parent, OffsetPose(0, 100));
            transaction.StageDetach(child, DetachPlacement.KeepWorldPose, 0);
            transaction.Commit();

            Assert.Multiple(() =>
            {
                Assert.That(world.Has<ChildOf>(child), Is.False);
                Assert.That(world.Has<AttachedLocalPose>(child), Is.False);
                // 空缓冲保留与 RelationOps.RemoveParent 既有语义一致（不回收空 ChildrenBuffer）。
                Assert.That(world.Get<ChildrenBuffer>(parent).Count, Is.Zero);
                Assert.That(world.Get<PoseAuthority>(child).Value, Is.EqualTo(PoseAuthorityKind.Nav),
                    "grant and handback pendings cancel out; authority never leaves Nav");
                Assert.That(arbiter.PendingTransitionCount, Is.Zero);
            });
        }

        [Test]
        public void DirectAttachThenDetach_BeforeBoundary_CancelsGrant()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity child = world.Create(
                WorldPositionCm.FromCm(100, 100),
                new PoseAuthority { Value = PoseAuthorityKind.Nav });
            var arbiter = new PoseAuthorityArbiter();
            using var commitSystem = new PoseAuthorityCommitSystem(world, arbiter);

            AttachmentOps.Attach(world, arbiter, child, parent, OffsetPose(0, 0));
            AttachmentOps.Detach(world, arbiter, child, DetachPlacement.KeepWorldPose, 0);
            commitSystem.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(world.Has<ChildOf>(child), Is.False);
                Assert.That(world.Has<AttachedLocalPose>(child), Is.False);
                Assert.That(world.Get<PoseAuthority>(child).Value, Is.EqualTo(PoseAuthorityKind.Nav));
                Assert.That(arbiter.PendingTransitionCount, Is.Zero);
            });
        }

        [Test]
        public void DirectDetachThenAttach_BeforeBoundary_CancelsHandback()
        {
            using World world = World.Create();
            Entity firstParent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity secondParent = world.Create(WorldPositionCm.FromCm(500, 0));
            Entity child = world.Create(
                WorldPositionCm.FromCm(100, 100),
                new PoseAuthority { Value = PoseAuthorityKind.Attached });
            var arbiter = new PoseAuthorityArbiter();
            using var commitSystem = new PoseAuthorityCommitSystem(world, arbiter);

            AttachmentOps.Attach(world, arbiter, child, firstParent, OffsetPose(0, 0));
            AttachmentOps.Detach(world, arbiter, child, DetachPlacement.KeepWorldPose, 0);
            AttachmentOps.Attach(world, arbiter, child, secondParent, OffsetPose(25, 0));
            commitSystem.Update(1f / 60f);

            Assert.Multiple(() =>
            {
                Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(secondParent));
                Assert.That(world.Get<PoseAuthority>(child).Value, Is.EqualTo(PoseAuthorityKind.Attached));
                Assert.That(arbiter.PendingTransitionCount, Is.Zero);
            });
        }

        [Test]
        public void DirectDetach_PerimeterWithoutStableSlot_FailsFast()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity child = world.Create(WorldPositionCm.FromCm(0, 0));
            AttachmentOps.Attach(world, null, child, parent, OffsetPose(0, 0));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                AttachmentOps.Detach(world, null, child, DetachPlacement.ParentPerimeterRing, 200))!;

            Assert.That(error.Message, Does.StartWith(AttachmentOps.StablePerimeterSlotRequiredError));
            Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(parent));
        }

        [Test]
        public void DirectDetach_UnknownPlacementFailsWithoutChangingAttachment()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity child = world.Create(WorldPositionCm.FromCm(0, 0));
            AttachmentOps.Attach(world, null, child, parent, OffsetPose(0, 0));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                AttachmentOps.Detach(world, null, child, (DetachPlacement)byte.MaxValue, 0))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.StartWith(AttachmentOps.UnknownDetachPlacementError));
                Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(parent));
                Assert.That(world.Has<AttachedLocalPose>(child), Is.True);
            });
        }

        [Test]
        public void AttachedLocalPose_ComponentAuthoringRejectsConflictingFacingSources()
        {
            using World world = World.Create();
            Entity entity = world.Create();
            JsonNode payload = JsonNode.Parse("""
                {
                  "offsetXCm": 10,
                  "offsetYCm": 20,
                  "facingDeg": 0,
                  "inheritParentFacing": true,
                  "offsetRotation": "OwnFacing"
                }
                """)!;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(entity, "AttachedLocalPose", payload))!;

            Assert.That(error.Message, Does.Contain("inheritParentFacing=true"));
            Assert.That(world.Has<AttachedLocalPose>(entity), Is.False);
        }

        [Test]
        public void Attach_CycleAcrossStagedState_FailsFast()
        {
            using World world = World.Create();
            Entity a = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity b = world.Create(WorldPositionCm.FromCm(100, 0));
            AttachDirect(world, new PoseAuthorityArbiter(), a, b, OffsetPose(0, 0));
            using var transaction = new EffectPhaseSideEffectTransaction(
                world, null, null, null, null, attributeEntityCapacity: 4);

            transaction.Begin();
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                transaction.StageAttach(b, a, OffsetPose(0, 0)))!;
            transaction.Rollback();

            Assert.That(error.Message, Does.StartWith(AttachmentOps.CycleError));
            Assert.That(world.Get<ChildOf>(a).Parent, Is.EqualTo(b));
            Assert.That(world.Has<ChildOf>(b), Is.False);
        }

        [Test]
        public void SetParent_CycleDetection_FailsFast()
        {
            using World world = World.Create();
            Entity a = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity b = world.Create(WorldPositionCm.FromCm(100, 0));
            Entity c = world.Create(WorldPositionCm.FromCm(200, 0));
            RelationOps.SetParent(world, a, b);
            RelationOps.SetParent(world, b, c);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                RelationOps.SetParent(world, c, a))!;

            Assert.That(error.Message, Does.StartWith(RelationOps.CycleDetectedError));
        }

        [Test]
        public void SetParent_ChildrenBufferCapacity_IsHardBoundedFailFast()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity[] children = new Entity[GasConstants.MAX_CHILDREN_BUFFER_CAPACITY];
            var arbiter = new PoseAuthorityArbiter();
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = world.Create(WorldPositionCm.FromCm(i, 0));
                AttachDirect(world, arbiter, children[i], parent, OffsetPose(0, 0));
            }

            Entity overflow = world.Create(WorldPositionCm.FromCm(999, 0));
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                AttachDirect(world, arbiter, overflow, parent, OffsetPose(0, 0)))!;

            Assert.That(error.Message, Does.Contain(GasConstants.MAX_CHILDREN_BUFFER_CAPACITY.ToString()));
        }

        [Test]
        public void Attach_NavMember_SuspendsMembership_DetachRestores()
        {
            // 挂接链唯一 mass nav 约定：子实体是 nav 成员时，attach 挂起成员身份
            // （求解器槽位由绑定系统回收），detach 回放 Agent 标记由绑定系统重播种。
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity child = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new Ludots.Core.MassNavigation.Runtime.MassNavigationAgent { ProfileId = 3 },
                new Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex { Value = 7 },
                new Ludots.Core.MassNavigation.Runtime.MassNavigationAgentProfile { ProfileId = 3 });

            AttachmentOps.Attach(world, new PoseAuthorityArbiter(), child, parent, OffsetPose(0, 0));

            Assert.Multiple(() =>
            {
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgent>(child), Is.False,
                    "挂接链唯一 mass nav：子实体成员身份摘除");
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex>(child), Is.False);
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentProfile>(child), Is.False);
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.SuspendedNavMembership>(child), Is.True);
                Assert.That(world.Get<Ludots.Core.MassNavigation.Runtime.SuspendedNavMembership>(child).Agent.ProfileId,
                    Is.EqualTo(3));
            });

            AttachmentOps.Detach(world, new PoseAuthorityArbiter(), child, DetachPlacement.KeepWorldPose, 0);

            Assert.Multiple(() =>
            {
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgent>(child), Is.True,
                    "解除恢复成员身份");
                Assert.That(world.Get<Ludots.Core.MassNavigation.Runtime.MassNavigationAgent>(child).ProfileId,
                    Is.EqualTo(3));
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.SuspendedNavMembership>(child), Is.False);
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex>(child), Is.False,
                    "旧 Index 不复用，由绑定系统按已提交位姿重播种");
            });
        }

        [Test]
        public void Attach_MissingArbiter_DoesNotSuspendNavMembership()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity child = world.Create(
                WorldPositionCm.FromCm(10, 20),
                new Ludots.Core.MassNavigation.Runtime.MassNavigationAgent { ProfileId = 4 },
                new Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex { Value = 9 },
                new Ludots.Core.MassNavigation.Runtime.MassNavigationAgentProfile { ProfileId = 4 },
                new PoseAuthority { Value = PoseAuthorityKind.Nav });

            Assert.Throws<InvalidOperationException>(() =>
                AttachmentOps.Attach(world, null, child, parent, OffsetPose(0, 0)));

            Assert.Multiple(() =>
            {
                Assert.That(world.Has<ChildOf>(child), Is.False);
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgent>(child), Is.True);
                Assert.That(world.Get<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex>(child).Value, Is.EqualTo(9));
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentProfile>(child), Is.True);
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.SuspendedNavMembership>(child), Is.False);
            });
        }

        [Test]
        public void Attach_InvalidInheritedFacing_DoesNotLeavePartialState()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(100, 200));
            Entity child = world.Create(
                WorldPositionCm.FromCm(10, 20),
                new Ludots.Core.MassNavigation.Runtime.MassNavigationAgent { ProfileId = 4 });

            Assert.Throws<InvalidOperationException>(() =>
                AttachmentOps.Attach(world, new PoseAuthorityArbiter(), child, parent, OffsetPose(0, 30, inheritFacing: true)));

            Assert.Multiple(() =>
            {
                Assert.That(world.Has<ChildOf>(child), Is.False);
                Assert.That(world.Has<AttachedLocalPose>(child), Is.False);
                Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(10, 20)));
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgent>(child), Is.True);
                Assert.That(world.Has<Ludots.Core.MassNavigation.Runtime.SuspendedNavMembership>(child), Is.False);
            });
        }

        [Test]
        public void RemoveParent_Staged_CommitsAndRollsBackSymmetrically()
        {
            using World world = World.Create();
            Entity parent = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity child = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(0, 0) });
            AttachDirect(world, new PoseAuthorityArbiter(), child, parent, OffsetPose(20, 20));
            using var transaction = new EffectPhaseSideEffectTransaction(
                world, null, null, null, null, attributeEntityCapacity: 4);

            transaction.Begin();
            transaction.StageRemoveParent(child);
            transaction.Commit();
            Assert.Multiple(() =>
            {
                Assert.That(world.Has<ChildOf>(child), Is.False);
                Assert.That(world.Has<AttachedLocalPose>(child), Is.False);
                Assert.That(world.Get<ChildrenBuffer>(parent).Count, Is.Zero);
            });

            AttachDirect(world, new PoseAuthorityArbiter(), child, parent, OffsetPose(20, 20));
            transaction.Begin();
            // 周界落位会 stage 位姿写并对其做失败校验——注入外部位姿变化触发事务失败。
            transaction.StageDetach(child, DetachPlacement.ParentPerimeterRing, 200);
            world.Get<WorldPositionCm>(child) = WorldPositionCm.FromCm(50, 50);
            Assert.Throws<InvalidOperationException>(() => transaction.Commit());
            transaction.Rollback();
            Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(parent));
            Assert.That(world.Has<AttachedLocalPose>(child), Is.True);
        }
    }
}
