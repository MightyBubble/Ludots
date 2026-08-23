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

        /// <summary>子实体朝向是否随父（决定 sink 是否写 FacingDirection 与 parent-moved 门的适用性）。</summary>
        public static bool DependsOnFacing(in AttachedLocalPose localPose)
        {
            return localPose.OffsetRotation != AttachedOffsetRotation.None ||
                   localPose.InheritParentFacing != 0;
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

            // 写权授予：持有 PoseAuthority 的子实体必须切到 Attached（无竞争写者化）；
            // 无 PoseAuthority 的子实体（未声明 MovementParticipation）没有其他写者，
            // attachment sink 即其唯一位姿写者，无需授予。
            TryGrantAttachedAuthority(world, arbiter, child);

            RelationOps.SetParent(world, child, parent);
            ApplyAttachedPose(world, child, parent, in localPose);
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

            Entity parent = world.Get<ChildOf>(child).Parent;
            int ringSlot = 0;
            int ringSlotCount = 0;
            if (world.IsAlive(parent))
            {
                if (!world.Has<ChildrenBuffer>(parent))
                {
                    throw new InvalidOperationException(
                        $"{ParentBufferMissingError}: parent={parent.Id}, child={child.Id}.");
                }

                ChildrenBuffer children = world.Get<ChildrenBuffer>(parent);
                ringSlotCount = children.Count;
                ringSlot = FindChildIndex(in children, in child);
                if (ringSlot < 0)
                {
                    throw new InvalidOperationException(
                        $"{ParentBufferMissingError}: parent={parent.Id}, child={child.Id}, reason=child-missing.");
                }
            }
            else if (placement == DetachPlacement.ParentPerimeterRing)
            {
                throw new InvalidOperationException(
                    $"{TargetInvalidError}: child={child.Id}, parent={parent.Id}, reason=perimeter-requires-live-parent.");
            }

            if (world.Has<PoseAuthority>(child) &&
                world.Get<PoseAuthority>(child).Value == PoseAuthorityKind.Attached)
            {
                if (arbiter == null)
                {
                    throw new InvalidOperationException(
                        $"{MissingArbiterError}: child={child.Id}, operation=AttachedHandback.");
                }

                arbiter.RequestAttachedHandback(world, child);
            }

            if (placement == DetachPlacement.ParentPerimeterRing)
            {
                if (!world.Has<WorldPositionCm>(parent))
                {
                    throw new InvalidOperationException(
                        $"{ParentPositionMissingError}: parent={parent.Id}.");
                }

                Fix64Vec2 ringOffset = AttachedPoseMath.PerimeterRingOffsetCm(ringSlot, ringSlotCount, perimeterRadiusCm);
                Fix64Vec2 ringPosition = world.Get<WorldPositionCm>(parent).Value + ringOffset;
                Upsert(world, child, new WorldPositionCm { Value = ringPosition });
                Upsert(world, child, new PreviousWorldPositionCm { Value = ringPosition });
            }

            if (world.Has<AttachedLocalPose>(child))
            {
                world.Remove<AttachedLocalPose>(child);
            }

            RelationOps.RemoveParent(world, child);
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
            float ownFacing = world.Has<FacingDirection>(child) ? world.Get<FacingDirection>(child).AngleRad : 0f;
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
        /// 返回 false 表示实体无 PoseAuthority（无竞争写者）。持有 Displacement/Physics 写权时 fail-fast。
        /// </summary>
        private static bool TryGrantAttachedAuthority(World world, PoseAuthorityArbiter? arbiter, Entity child)
        {
            if (!world.Has<PoseAuthority>(child))
            {
                return false;
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

            arbiter.RequestAttachedAuthority(world, child);
            return true;
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
    }
}
