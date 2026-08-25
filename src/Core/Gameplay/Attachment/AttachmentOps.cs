using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;

namespace Ludots.Core.Gameplay.Attachment
{
    /// <summary>detach 落位策略（Attach/Detach 原子 op 的参数，非 preset 开关）。</summary>
    public enum DetachPlacement : byte
    {
        /// <summary>保持当前世界位姿不动（子实体已持有派生出的世界坐标 SSOT）。</summary>
        KeepWorldPose = 0,

        /// <summary>落到父锚点周界环上：槽序取子实体在父 ChildrenBuffer 快照中的序号，整批同 detach 天然错开。</summary>
        ParentPerimeterRing = 1,
    }

    /// <summary>
    /// 父位姿 ∘ 局部位姿 → 子世界位姿的组合数学单点。
    /// attach 初始落位、AttachmentPositionSyncSystem 每步派生、模板 children 预置组合共用。
    /// </summary>
    public static class AttachedPoseMath
    {
        public static Fix64Vec2 ComposeWorldPosition(
            in Fix64Vec2 parentPositionCm,
            float parentFacingRad,
            float ownFacingRad,
            in AttachedLocalPose localPose)
        {
            switch (localPose.OffsetRotation)
            {
                case AttachedOffsetRotation.None:
                    return parentPositionCm + localPose.OffsetCm;
                case AttachedOffsetRotation.ParentFacing:
                    return parentPositionCm + Rotate(localPose.OffsetCm, parentFacingRad);
                case AttachedOffsetRotation.OwnFacing:
                    return parentPositionCm + Rotate(localPose.OffsetCm, ownFacingRad);
                default:
                    throw new InvalidOperationException(
                        $"GAS.ATTACH.ERR.UnknownOffsetRotation: rotation={(byte)localPose.OffsetRotation}.");
            }
        }

        /// <summary>
        /// OwnFacing 偏移的朝向回退链：子实体无 FacingDirection 时回退父朝向（再无则 0），
        /// 与迁移前 manifestation 的 ResolveOffsetFacing 语义一致。
        /// </summary>
        public static float ResolveOwnFacingRad(bool childHasFacing, float childFacingRad, float parentFacingRad)
        {
            return childHasFacing ? childFacingRad : parentFacingRad;
        }

        public static Fix64Vec2 Rotate(in Fix64Vec2 offsetCm, float facingRad)
        {
            Fix64 angle = Fix64.FromFloat(facingRad);
            Fix64 cos = Fix64Math.Cos(angle);
            Fix64 sin = Fix64Math.Sin(angle);
            return new Fix64Vec2(
                offsetCm.X * cos - offsetCm.Y * sin,
                offsetCm.X * sin + offsetCm.Y * cos);
        }

        public static Fix64Vec2 PerimeterRingOffsetCm(int slot, int slotCount, int radiusCm)
        {
            if (slotCount <= 0 || slot < 0 || slot >= slotCount)
            {
                throw new InvalidOperationException(
                    $"GAS.ATTACH.ERR.PerimeterSlotInvalid: slot={slot}, slotCount={slotCount}.");
            }

            Fix64 angle = Fix64.TwoPi * Fix64.FromInt(slot) / Fix64.FromInt(slotCount);
            return new Fix64Vec2(Fix64Math.Cos(angle), Fix64Math.Sin(angle)) * Fix64.FromInt(radiusCm);
        }
    }

    /// <summary>
    /// attachment 绑定原子 op 的直接执行入口（非事务路径）。
    /// GAS 事务路径（<see cref="EffectPhaseSideEffectTransaction.StageAttach"/> 等）镜像同一套校验与
    /// 语义；Mod 侧直接编排（如驻防进出）走本入口。写权授予/归还经
    /// <see cref="PoseAuthorityArbiter"/> 在固定步边界结算。
    /// </summary>
    public static class AttachmentOps
    {
        public const string TargetInvalidError = "GAS.ATTACH.ERR.TargetInvalid";
        public const string CycleError = "GAS.ATTACH.ERR.CycleDetected";
        public const string ParentPositionMissingError = "GAS.ATTACH.ERR.ParentPositionMissing";
        public const string AuthorityConflictError = "GAS.ATTACH.ERR.PoseAuthorityConflict";
        public const string MissingChildOfError = "GAS.ATTACH.ERR.MissingChildOf";
        public const string MissingArbiterError = "GAS.ATTACH.ERR.MissingPoseAuthorityArbiter";
        public const string ParentBufferMissingError = "GAS.ATTACH.ERR.ParentChildrenBufferMissing";
        public const string StablePerimeterSlotRequiredError = "GAS.ATTACH.ERR.StablePerimeterSlotRequired";
        public const string UnknownDetachPlacementError = "GAS.ATTACH.ERR.UnknownDetachPlacement";

        private enum AuthorityPendingMutation : byte
        {
            None = 0,
            AddedGrant = 1,
            RemovedGrant = 2,
            AddedHandback = 3,
            RemovedHandback = 4,
        }

        public static void Attach(
            World world,
            PoseAuthorityArbiter? arbiter,
            Entity child,
            Entity parent,
            in AttachedLocalPose localPose)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (!world.IsAlive(child) || !world.IsAlive(parent) || child == parent)
            {
                throw new InvalidOperationException(
                    $"{TargetInvalidError}: child={child.Id}, parent={parent.Id}.");
            }

            if (WouldCreateCycle(world, child, parent))
            {
                throw new InvalidOperationException(
                    $"{CycleError}: child={child.Id}, parent={parent.Id}.");
            }

            if (!world.Has<WorldPositionCm>(parent))
            {
                throw new InvalidOperationException(
                    $"{ParentPositionMissingError}: parent={parent.Id}.");
            }

            ValidateAttachedPose(world, child, parent, in localPose);
            ValidateAttachedAuthority(world, arbiter, child);
            ValidateParentCapacity(world, child, parent);
            AttachmentStateSnapshot snapshot = AttachmentStateSnapshot.Capture(world, child, parent);
            AuthorityPendingMutation authorityMutation = AuthorityPendingMutation.None;

            try
            {
                // 挂接链唯一 mass nav 约定：子实体是 nav 成员时挂起成员身份（求解器槽位由绑定系统
                // 在下一 RuntimeEntityBinding pass 回收），detach / 孤儿自愈时恢复。
                Ludots.Core.MassNavigation.Runtime.MassNavigationMembership.Suspend(world, child);

                // 写权授予：持有 PoseAuthority 的子实体必须切到 Attached（无竞争写者化）。
                authorityMutation = ApplyAttachedGrant(world, arbiter, child);
                RelationOps.SetParent(world, child, parent);
                ApplyAttachedPose(world, child, parent, in localPose);
            }
            catch
            {
                RollbackAuthorityMutation(arbiter, child, authorityMutation);
                snapshot.Restore(world, child);
                throw;
            }
        }

        /// <summary>
        /// 直接路径的周界落位 detach：槽位由调用方显式给出（批量卸下时调用方先快照
        /// ChildrenBuffer，再按快照序传入 slot/total——同批天然错开且不受缓冲收缩影响）。
        /// 事务路径（StageDetach）内部用父行槽位位图自动完成同一件事。
        /// </summary>
        public static void DetachToPerimeter(
            World world,
            PoseAuthorityArbiter? arbiter,
            Entity child,
            int perimeterRadiusCm,
            int ringSlot,
            int ringSlotCount)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (!world.IsAlive(child) || !world.Has<ChildOf>(child))
            {
                throw new InvalidOperationException($"{MissingChildOfError}: child={child.Id}.");
            }

            Entity parent = world.Get<ChildOf>(child).Parent;
            if (!world.IsAlive(parent) || !world.Has<WorldPositionCm>(parent))
            {
                throw new InvalidOperationException(
                    $"{TargetInvalidError}: child={child.Id}, parent={parent.Id}, reason=perimeter-requires-live-parent.");
            }

            ValidateAttachedHandback(world, arbiter, child);

            Fix64Vec2 ringOffset = AttachedPoseMath.PerimeterRingOffsetCm(ringSlot, ringSlotCount, perimeterRadiusCm);
            Fix64Vec2 ringPosition = world.Get<WorldPositionCm>(parent).Value + ringOffset;
            AttachmentStateSnapshot snapshot = AttachmentStateSnapshot.Capture(world, child, parent);
            AuthorityPendingMutation authorityMutation = AuthorityPendingMutation.None;
            try
            {
                authorityMutation = ApplyAttachedHandback(world, arbiter, child);
                Ludots.Core.MassNavigation.Runtime.MassNavigationMembership.Restore(world, child);
                Upsert(world, child, new WorldPositionCm { Value = ringPosition });
                Upsert(world, child, new PreviousWorldPositionCm { Value = ringPosition });
                if (world.Has<AttachedLocalPose>(child))
                {
                    world.Remove<AttachedLocalPose>(child);
                }

                RelationOps.RemoveParent(world, child);
            }
            catch
            {
                RollbackAuthorityMutation(arbiter, child, authorityMutation);
                snapshot.Restore(world, child);
                throw;
            }
        }

        public static void Detach(
            World world,
            PoseAuthorityArbiter? arbiter,
            Entity child,
            DetachPlacement placement,
            int perimeterRadiusCm)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (!world.IsAlive(child) || !world.Has<ChildOf>(child))
            {
                throw new InvalidOperationException($"{MissingChildOfError}: child={child.Id}.");
            }

            ValidateDetachPlacement(placement);
            if (placement == DetachPlacement.ParentPerimeterRing)
            {
                throw new InvalidOperationException(
                    $"{StablePerimeterSlotRequiredError}: child={child.Id}, operation=DetachToPerimeter.");
            }

            Entity parent = world.Get<ChildOf>(child).Parent;
            if (world.IsAlive(parent))
            {
                if (!world.Has<ChildrenBuffer>(parent))
                {
                    throw new InvalidOperationException(
                        $"{ParentBufferMissingError}: parent={parent.Id}, child={child.Id}.");
                }

                ChildrenBuffer children = world.Get<ChildrenBuffer>(parent);
                if (FindChildIndex(in children, in child) < 0)
                {
                    throw new InvalidOperationException(
                        $"{ParentBufferMissingError}: parent={parent.Id}, child={child.Id}, reason=child-missing.");
                }
            }
            ValidateAttachedHandback(world, arbiter, child);

            AttachmentStateSnapshot snapshot = AttachmentStateSnapshot.Capture(world, child, parent);
            AuthorityPendingMutation authorityMutation = AuthorityPendingMutation.None;
            try
            {
                authorityMutation = ApplyAttachedHandback(world, arbiter, child);
                Ludots.Core.MassNavigation.Runtime.MassNavigationMembership.Restore(world, child);
                if (world.Has<AttachedLocalPose>(child))
                {
                    world.Remove<AttachedLocalPose>(child);
                }

                RelationOps.RemoveParent(world, child);
            }
            catch
            {
                RollbackAuthorityMutation(arbiter, child, authorityMutation);
                snapshot.Restore(world, child);
                throw;
            }
        }

        internal static void ValidateDetachPlacement(DetachPlacement placement)
        {
            if (placement != DetachPlacement.KeepWorldPose &&
                placement != DetachPlacement.ParentPerimeterRing)
            {
                throw new InvalidOperationException(
                    $"{UnknownDetachPlacementError}: placement={(byte)placement}.");
            }
        }

        private static AuthorityPendingMutation ApplyAttachedHandback(
            World world,
            PoseAuthorityArbiter? arbiter,
            Entity child)
        {
            if (!world.Has<PoseAuthority>(child))
            {
                return AuthorityPendingMutation.None;
            }

            if (arbiter == null)
            {
                throw new InvalidOperationException(
                    $"{MissingArbiterError}: child={child.Id}, operation=AttachedHandback.");
            }

            PoseAuthorityKind current = world.Get<PoseAuthority>(child).Value;
            if (current == PoseAuthorityKind.Nav)
            {
                return arbiter.RemovePendingTransition(child, PoseAuthorityKind.Nav, PoseAuthorityKind.Attached)
                    ? AuthorityPendingMutation.RemovedGrant
                    : AuthorityPendingMutation.None;
            }

            if (current != PoseAuthorityKind.Attached)
            {
                throw new InvalidOperationException(
                    $"{AuthorityConflictError}: child={child.Id}, current={current}.");
            }

            if (arbiter.HasPendingTransition(child, PoseAuthorityKind.Attached, PoseAuthorityKind.Nav))
            {
                return AuthorityPendingMutation.None;
            }

            arbiter.RequestAttachedHandback(world, child);
            return AuthorityPendingMutation.AddedHandback;
        }

        private static void ValidateAttachedHandback(World world, PoseAuthorityArbiter? arbiter, Entity child)
        {
            if (!world.Has<PoseAuthority>(child))
            {
                return;
            }

            if (arbiter == null)
            {
                throw new InvalidOperationException(
                    $"{MissingArbiterError}: child={child.Id}, operation=AttachedHandback.");
            }
        }

        private static void ValidateAttachedAuthority(World world, PoseAuthorityArbiter? arbiter, Entity child)
        {
            if (!world.Has<PoseAuthority>(child))
            {
                return;
            }

            PoseAuthorityKind current = world.Get<PoseAuthority>(child).Value;
            if (current == PoseAuthorityKind.Physics || current == PoseAuthorityKind.Displacement)
            {
                throw new InvalidOperationException(
                    $"{AuthorityConflictError}: child={child.Id}, current={current}.");
            }

            if (arbiter == null)
            {
                throw new InvalidOperationException(
                    $"{MissingArbiterError}: child={child.Id}, operation=AttachedGrant.");
            }
        }

        private static void ValidateAttachedPose(
            World world,
            Entity child,
            Entity parent,
            in AttachedLocalPose localPose)
        {
            float parentFacing = world.Has<FacingDirection>(parent)
                ? world.Get<FacingDirection>(parent).AngleRad
                : 0f;
            float ownFacing = AttachedPoseMath.ResolveOwnFacingRad(
                world.Has<FacingDirection>(child),
                world.Has<FacingDirection>(child) ? world.Get<FacingDirection>(child).AngleRad : 0f,
                parentFacing);
            _ = AttachedPoseMath.ComposeWorldPosition(
                in world.Get<WorldPositionCm>(parent).Value,
                parentFacing,
                ownFacing,
                in localPose);

            if (localPose.InheritParentFacing != 0 && !world.Has<FacingDirection>(parent))
            {
                throw new InvalidOperationException(
                    $"{ParentPositionMissingError}: parent={parent.Id}, reason=inherit-facing-without-parent-facing.");
            }
        }

        private static void ValidateParentCapacity(World world, Entity child, Entity parent)
        {
            if (!world.Has<ChildrenBuffer>(parent))
            {
                return;
            }

            ChildrenBuffer children = world.Get<ChildrenBuffer>(parent);
            if (!children.Contains(in child) &&
                children.Count >= GasConstants.MAX_CHILDREN_BUFFER_CAPACITY)
            {
                throw new InvalidOperationException(
                    $"{RelationOps.ChildrenCapacityExceededError}: parent={parent.Id}, capacity={GasConstants.MAX_CHILDREN_BUFFER_CAPACITY}.");
            }
        }

        /// <summary>环检测单点在 <see cref="RelationOps.WouldCreateCycle"/>（组件边基座的所有入口共用）。</summary>
        public static bool WouldCreateCycle(World world, Entity child, Entity parent)
        {
            return RelationOps.WouldCreateCycle(world, child, parent);
        }

        /// <summary>把父位姿 ∘ 局部位姿落到子实体（attach 的初始派生位姿，含刚性零插值 Previous）。</summary>
        public static void ApplyAttachedPose(World world, Entity child, Entity parent, in AttachedLocalPose localPose)
        {
            Fix64Vec2 parentPosition = world.Get<WorldPositionCm>(parent).Value;
            float parentFacing = world.Has<FacingDirection>(parent) ? world.Get<FacingDirection>(parent).AngleRad : 0f;
            float ownFacing = AttachedPoseMath.ResolveOwnFacingRad(
                world.Has<FacingDirection>(child),
                world.Has<FacingDirection>(child) ? world.Get<FacingDirection>(child).AngleRad : 0f,
                parentFacing);
            Fix64Vec2 worldPosition = AttachedPoseMath.ComposeWorldPosition(
                in parentPosition,
                parentFacing,
                ownFacing,
                in localPose);

            Upsert(world, child, new WorldPositionCm { Value = worldPosition });
            Upsert(world, child, new PreviousWorldPositionCm { Value = worldPosition });
            Upsert(world, child, localPose);

            if (localPose.InheritParentFacing != 0)
            {
                if (!world.Has<FacingDirection>(parent))
                {
                    throw new InvalidOperationException(
                        $"{ParentPositionMissingError}: parent={parent.Id}, reason=inherit-facing-without-parent-facing.");
                }

                Upsert(world, child, new FacingDirection
                {
                    AngleRad = parentFacing + localPose.LocalFacingRad.ToFloat(),
                });
            }
            else if (!world.Has<FacingDirection>(child) && localPose.LocalFacingRad != Fix64.Zero)
            {
                Upsert(world, child, new FacingDirection { AngleRad = localPose.LocalFacingRad.ToFloat() });
            }
        }

        /// <summary>
        /// 授予 Attached 写权。返回 true 表示实体持有 PoseAuthority 且已处理（授予或本就 Attached）；
        /// 返回 false 表示实体无 PoseAuthority（无竞争写者）。持有 Displacement/Physics 写权时
        /// fail-fast；nav 成员身份已在 attach 前挂起，求解器不再是竞争写者。
        /// </summary>
        private static AuthorityPendingMutation ApplyAttachedGrant(
            World world,
            PoseAuthorityArbiter? arbiter,
            Entity child)
        {
            if (!world.Has<PoseAuthority>(child))
            {
                return AuthorityPendingMutation.None;
            }

            PoseAuthorityKind current = world.Get<PoseAuthority>(child).Value;
            if (current == PoseAuthorityKind.Physics || current == PoseAuthorityKind.Displacement)
            {
                throw new InvalidOperationException(
                    $"{AuthorityConflictError}: child={child.Id}, current={current}.");
            }

            if (arbiter == null)
            {
                throw new InvalidOperationException(
                    $"{MissingArbiterError}: child={child.Id}, operation=AttachedGrant.");
            }

            if (current == PoseAuthorityKind.Attached)
            {
                return arbiter.RemovePendingTransition(child, PoseAuthorityKind.Attached, PoseAuthorityKind.Nav)
                    ? AuthorityPendingMutation.RemovedHandback
                    : AuthorityPendingMutation.None;
            }

            if (arbiter.HasPendingTransition(child, PoseAuthorityKind.Nav, PoseAuthorityKind.Attached))
            {
                return AuthorityPendingMutation.None;
            }

            arbiter.RequestAttachedAuthority(world, child);
            return AuthorityPendingMutation.AddedGrant;
        }

        private static void RollbackAuthorityMutation(
            PoseAuthorityArbiter? arbiter,
            Entity child,
            AuthorityPendingMutation mutation)
        {
            switch (mutation)
            {
                case AuthorityPendingMutation.None:
                    return;
                case AuthorityPendingMutation.AddedGrant:
                    arbiter!.RemovePendingTransition(child, PoseAuthorityKind.Nav, PoseAuthorityKind.Attached);
                    return;
                case AuthorityPendingMutation.RemovedGrant:
                    arbiter!.RestorePendingTransition(child, PoseAuthorityKind.Nav, PoseAuthorityKind.Attached);
                    return;
                case AuthorityPendingMutation.AddedHandback:
                    arbiter!.RemovePendingTransition(child, PoseAuthorityKind.Attached, PoseAuthorityKind.Nav);
                    return;
                case AuthorityPendingMutation.RemovedHandback:
                    arbiter!.RestorePendingTransition(child, PoseAuthorityKind.Attached, PoseAuthorityKind.Nav);
                    return;
                default:
                    throw new InvalidOperationException($"Unknown authority pending mutation '{mutation}'.");
            }
        }

        internal static int FindChildIndex(in ChildrenBuffer children, in Entity child)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children.Get(i) == child)
                {
                    return i;
                }
            }

            return -1;
        }

        internal static void Upsert<T>(World world, Entity entity, in T component) where T : struct
        {
            if (world.Has<T>(entity))
            {
                world.Set(entity, component);
            }
            else
            {
                world.Add(entity, component);
            }
        }

        private struct AttachmentStateSnapshot
        {
            private bool _childOfExisted;
            private ChildOf _childOf;
            private bool _attachedPoseExisted;
            private AttachedLocalPose _attachedPose;
            private bool _facingExisted;
            private FacingDirection _facing;
            private bool _worldPositionExisted;
            private WorldPositionCm _worldPosition;
            private bool _previousPositionExisted;
            private PreviousWorldPositionCm _previousPosition;
            private bool _navAgentExisted;
            private Ludots.Core.MassNavigation.Runtime.MassNavigationAgent _navAgent;
            private bool _navIndexExisted;
            private Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex _navIndex;
            private bool _navProfileExisted;
            private Ludots.Core.MassNavigation.Runtime.MassNavigationAgentProfile _navProfile;
            private bool _suspendedNavExisted;
            private Ludots.Core.MassNavigation.Runtime.SuspendedNavMembership _suspendedNav;
            private Entity _oldParent;
            private Entity _targetParent;
            private bool _oldParentChildrenExisted;
            private ChildrenBuffer _oldParentChildren;
            private bool _targetParentChildrenExisted;
            private ChildrenBuffer _targetParentChildren;

            public static AttachmentStateSnapshot Capture(World world, Entity child, Entity targetParent)
            {
                AttachmentStateSnapshot snapshot = new()
                {
                    _childOfExisted = world.Has<ChildOf>(child),
                    _attachedPoseExisted = world.Has<AttachedLocalPose>(child),
                    _facingExisted = world.Has<FacingDirection>(child),
                    _worldPositionExisted = world.Has<WorldPositionCm>(child),
                    _previousPositionExisted = world.Has<PreviousWorldPositionCm>(child),
                    _navAgentExisted = world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgent>(child),
                    _navIndexExisted = world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex>(child),
                    _navProfileExisted = world.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentProfile>(child),
                    _suspendedNavExisted = world.Has<Ludots.Core.MassNavigation.Runtime.SuspendedNavMembership>(child),
                    _targetParent = targetParent,
                    _targetParentChildrenExisted = world.Has<ChildrenBuffer>(targetParent),
                };

                if (snapshot._childOfExisted)
                {
                    snapshot._childOf = world.Get<ChildOf>(child);
                    snapshot._oldParent = snapshot._childOf.Parent;
                }

                if (snapshot._attachedPoseExisted) snapshot._attachedPose = world.Get<AttachedLocalPose>(child);
                if (snapshot._facingExisted) snapshot._facing = world.Get<FacingDirection>(child);
                if (snapshot._worldPositionExisted) snapshot._worldPosition = world.Get<WorldPositionCm>(child);
                if (snapshot._previousPositionExisted) snapshot._previousPosition = world.Get<PreviousWorldPositionCm>(child);
                if (snapshot._navAgentExisted) snapshot._navAgent = world.Get<Ludots.Core.MassNavigation.Runtime.MassNavigationAgent>(child);
                if (snapshot._navIndexExisted) snapshot._navIndex = world.Get<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex>(child);
                if (snapshot._navProfileExisted) snapshot._navProfile = world.Get<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentProfile>(child);
                if (snapshot._suspendedNavExisted) snapshot._suspendedNav = world.Get<Ludots.Core.MassNavigation.Runtime.SuspendedNavMembership>(child);
                if (snapshot._targetParentChildrenExisted) snapshot._targetParentChildren = world.Get<ChildrenBuffer>(targetParent);

                if (snapshot._childOfExisted &&
                    snapshot._oldParent != targetParent &&
                    world.IsAlive(snapshot._oldParent) &&
                    world.Has<ChildrenBuffer>(snapshot._oldParent))
                {
                    snapshot._oldParentChildrenExisted = true;
                    snapshot._oldParentChildren = world.Get<ChildrenBuffer>(snapshot._oldParent);
                }

                return snapshot;
            }

            public void Restore(World world, Entity child)
            {
                RestoreComponent(world, child, _childOfExisted, _childOf);
                RestoreComponent(world, child, _attachedPoseExisted, _attachedPose);
                RestoreComponent(world, child, _facingExisted, _facing);
                RestoreComponent(world, child, _worldPositionExisted, _worldPosition);
                RestoreComponent(world, child, _previousPositionExisted, _previousPosition);
                RestoreComponent(world, child, _navAgentExisted, _navAgent);
                RestoreComponent(world, child, _navIndexExisted, _navIndex);
                RestoreComponent(world, child, _navProfileExisted, _navProfile);
                RestoreComponent(world, child, _suspendedNavExisted, _suspendedNav);
                RestoreChildren(world, _oldParent, _oldParentChildrenExisted, _oldParentChildren);
                RestoreChildren(world, _targetParent, _targetParentChildrenExisted, _targetParentChildren);
            }

            private static void RestoreComponent<T>(World world, Entity entity, bool existed, in T value)
                where T : struct
            {
                if (existed)
                {
                    if (world.Has<T>(entity)) world.Get<T>(entity) = value;
                    else world.Add(entity, value);
                }
                else if (world.Has<T>(entity))
                {
                    world.Remove<T>(entity);
                }
            }

            private static void RestoreChildren(World world, Entity parent, bool existed, in ChildrenBuffer value)
            {
                if (parent == Entity.Null || !world.IsAlive(parent)) return;
                if (existed)
                {
                    if (world.Has<ChildrenBuffer>(parent)) world.Get<ChildrenBuffer>(parent) = value;
                    else world.Add(parent, value);
                }
                else if (world.Has<ChildrenBuffer>(parent))
                {
                    world.Remove<ChildrenBuffer>(parent);
                }
            }
        }
    }
}
