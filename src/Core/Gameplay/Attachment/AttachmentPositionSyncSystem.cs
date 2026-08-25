using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;

namespace Ludots.Core.Gameplay.Attachment
{
    /// <summary>
    /// attachment 位置同步 sink：父位姿 ∘ 局部位姿 → 子实体 WorldPositionCm + Previous（刚性零插值）。
    /// 运行在 PostMovement 组、WorldToGridSyncSystem 之前——父实体位姿已落定、网格派生链在其后。
    /// 深度序（父先子后）保证多层结构（底盘→炮塔→炮管）一步内一致；0 alloc（全部缓冲预分配复用）。
    /// 写权合同：sink 只写 PoseAuthority==Attached 或无 PoseAuthority 的子实体。
    /// 恒重算（无 parent-moved 门）：位姿写者大量存在于本系统之后（PostMovement 后段的 nav 求解器、
    /// AbilityActivation 的订单移动、EffectProcessing 的位移/投射物），Previous 比对在 sink 时点
    /// 恒为"未移动"，门会让位置依赖子冻结；compose 只是几次 Fix64 运算，恒重算不构成热点。
    /// </summary>
    public sealed class AttachmentPositionSyncSystem : BaseSystem<World, float>
    {
        public const string CapacityExceededError = "GAS.ATTACH.SYNC.ERR.CapacityExceeded";
        public const string ParentPositionMissingError = "GAS.ATTACH.SYNC.ERR.ParentPositionMissing";
        public const string PoseAuthorityConflictError = "GAS.ATTACH.SYNC.ERR.PoseAuthorityConflict";

        private static readonly QueryDescription AttachedQuery = new QueryDescription()
            .WithAll<ChildOf, AttachedLocalPose>();

        private readonly int _scratchCapacity;
        private readonly Entity[] _entities;
        private readonly ChildOf[] _childOf;
        private readonly AttachedLocalPose[] _localPose;
        private readonly int[] _depth;
        private readonly byte[] _visitState;
        private readonly int[] _visitStack;
        private readonly int[] _depthCounts;
        private readonly int[] _depthOffsets;
        private readonly int[] _orderedIndices;
        private readonly Dictionary<Entity, int> _entityIndices;
        private readonly PoseAuthorityArbiter? _poseAuthorityArbiter;

        public AttachmentPositionSyncSystem(
            World world,
            int scratchCapacity,
            PoseAuthorityArbiter? poseAuthorityArbiter = null)
            : base(world)
        {
            if (scratchCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scratchCapacity));
            }

            _scratchCapacity = scratchCapacity;
            _entities = new Entity[scratchCapacity];
            _childOf = new ChildOf[scratchCapacity];
            _localPose = new AttachedLocalPose[scratchCapacity];
            _depth = new int[scratchCapacity];
            _visitState = new byte[scratchCapacity];
            _visitStack = new int[scratchCapacity];
            _depthCounts = new int[scratchCapacity];
            _depthOffsets = new int[scratchCapacity];
            _orderedIndices = new int[scratchCapacity];
            _entityIndices = new Dictionary<Entity, int>(scratchCapacity);
            _poseAuthorityArbiter = poseAuthorityArbiter;
        }

        /// <summary>上一 Update 处理的子实体数（headless 验收日志用）。</summary>
        public int LastAppliedCount { get; private set; }

        /// <summary>上一 Update 因父死亡被清理的子实体数。</summary>
        public int LastOrphanCleanupCount { get; private set; }

        /// <summary>上一 Update 处理到的最大挂接深度（0 = 直接子）。</summary>
        public int LastMaxDepth { get; private set; }

        public override void Update(in float dt)
        {
            LastAppliedCount = 0;
            LastOrphanCleanupCount = 0;
            LastMaxDepth = 0;

            int count = 0;
            _entityIndices.Clear();
            foreach (ref var chunk in World.Query(in AttachedQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var childOfSpan = chunk.GetSpan<ChildOf>();
                var localPoseSpan = chunk.GetSpan<AttachedLocalPose>();
                foreach (var index in chunk)
                {
                    if (count >= _scratchCapacity)
                    {
                        throw new InvalidOperationException(
                            $"{CapacityExceededError}: staged={count + 1}, capacity={_scratchCapacity}.");
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ChildOf childOf = childOfSpan[index];
                    _entities[count] = entity;
                    _childOf[count] = childOf;
                    _localPose[count] = localPoseSpan[index];
                    _depth[count] = -1;
                    _visitState[count] = 0;
                    _entityIndices.Add(entity, count);
                    count++;
                }
            }

            int maxDepth = ResolveDepths(count);
            LastMaxDepth = maxDepth;
            BuildDepthOrder(count, maxDepth);
            for (int orderIndex = 0; orderIndex < count; orderIndex++)
            {
                int i = _orderedIndices[orderIndex];
                ValidateChildPreconditions(_entities[i], in _childOf[i], in _localPose[i]);
            }

            for (int orderIndex = 0; orderIndex < count; orderIndex++)
            {
                int i = _orderedIndices[orderIndex];
                ProcessChild(_entities[i], in _childOf[i], in _localPose[i]);
            }
        }

        private int ResolveDepths(int count)
        {
            int maxDepth = 0;
            for (int start = 0; start < count; start++)
            {
                if (_depth[start] >= 0)
                {
                    continue;
                }

                int stackCount = 0;
                int current = start;
                while (current >= 0 && _depth[current] < 0)
                {
                    if (_visitState[current] == 1)
                    {
                        throw new InvalidOperationException("GAS.ATTACH.SYNC.ERR.CycleDetected");
                    }

                    _visitState[current] = 1;
                    _visitStack[stackCount++] = current;
                    current = _entityIndices.TryGetValue(_childOf[current].Parent, out int parentIndex)
                        ? parentIndex
                        : -1;
                }

                int depth = current >= 0 ? _depth[current] + 1 : 0;
                while (stackCount > 0)
                {
                    int index = _visitStack[--stackCount];
                    _depth[index] = depth;
                    _visitState[index] = 2;
                    if (depth > maxDepth)
                    {
                        maxDepth = depth;
                    }

                    depth++;
                }
            }

            return maxDepth;
        }

        private void BuildDepthOrder(int count, int maxDepth)
        {
            Array.Clear(_depthCounts, 0, maxDepth + 1);
            for (int i = 0; i < count; i++)
            {
                _depthCounts[_depth[i]]++;
            }

            int offset = 0;
            for (int depth = 0; depth <= maxDepth; depth++)
            {
                _depthOffsets[depth] = offset;
                offset += _depthCounts[depth];
            }

            for (int i = 0; i < count; i++)
            {
                _orderedIndices[_depthOffsets[_depth[i]]++] = i;
            }
        }

        private void ProcessChild(Entity child, in ChildOf childOf, in AttachedLocalPose localPose)
        {
            Entity parent = childOf.Parent;
            if (!World.IsAlive(parent) || !World.Has<WorldPositionCm>(parent))
            {
                // 带自管生命周期的子实体（如 DestroyWhenParentExecutionEnds 的 manifestation）
                // 由其生命周期系统处置死父；sink 不抢拆它们的 ChildOf，位置冻结在最后派生位姿
                //（与迁移前的 manifestation 行为一致）。
                if (World.Has<Ludots.Core.Gameplay.Spawning.DestroyWhenParentExecutionEnds>(child))
                {
                    return;
                }

                CleanupOrphan(child, parent);
                return;
            }

            ref readonly WorldPositionCm parentPosition = ref World.Get<WorldPositionCm>(parent);
            float parentFacing = World.Has<FacingDirection>(parent) ? World.Get<FacingDirection>(parent).AngleRad : 0f;
            float ownFacing = AttachedPoseMath.ResolveOwnFacingRad(
                World.Has<FacingDirection>(child),
                World.Has<FacingDirection>(child) ? World.Get<FacingDirection>(child).AngleRad : 0f,
                parentFacing);
            Fix64Vec2 worldPosition = AttachedPoseMath.ComposeWorldPosition(
                in parentPosition.Value,
                parentFacing,
                ownFacing,
                in localPose);

            ref WorldPositionCm current = ref World.Get<WorldPositionCm>(child);
            current.Value = worldPosition;
            World.Get<PreviousWorldPositionCm>(child).Value = worldPosition;

            if (localPose.InheritParentFacing != 0)
            {
                World.Get<FacingDirection>(child).AngleRad = parentFacing + localPose.LocalFacingRad.ToFloat();
            }

            LastAppliedCount++;
        }

        private void ValidateChildPreconditions(Entity child, in ChildOf childOf, in AttachedLocalPose localPose)
        {
            Entity parent = childOf.Parent;
            if (!World.IsAlive(parent) || !World.Has<WorldPositionCm>(parent))
            {
                if (!World.Has<Ludots.Core.Gameplay.Spawning.DestroyWhenParentExecutionEnds>(child) &&
                    World.Has<PoseAuthority>(child) &&
                    World.Get<PoseAuthority>(child).Value == PoseAuthorityKind.Attached &&
                    _poseAuthorityArbiter == null)
                {
                    throw new InvalidOperationException(
                        $"{PoseAuthorityConflictError}: child={child.Id}, reason=orphan-cleanup-requires-arbiter.");
                }

                return;
            }

            AttachedLocalPoseAuthoring.ValidateFacingSources(
                localPose.InheritParentFacing != 0,
                localPose.OffsetRotation,
                "AttachmentPositionSyncSystem");

            if (World.Has<PoseAuthority>(child) &&
                World.Get<PoseAuthority>(child).Value != PoseAuthorityKind.Attached)
            {
                throw new InvalidOperationException(
                    $"{PoseAuthorityConflictError}: child={child.Id}, authority={World.Get<PoseAuthority>(child).Value}.");
            }

            if (!World.Has<WorldPositionCm>(child))
            {
                throw new InvalidOperationException(
                    $"{ParentPositionMissingError}: child={child.Id}, reason=attached-child-without-world-position.");
            }

            if (!World.Has<PreviousWorldPositionCm>(child))
            {
                throw new InvalidOperationException(
                    $"{ParentPositionMissingError}: child={child.Id}, reason=attached-child-without-previous-world-position.");
            }

            if (localPose.InheritParentFacing != 0)
            {
                if (!World.Has<FacingDirection>(parent))
                {
                    throw new InvalidOperationException(
                        $"{ParentPositionMissingError}: parent={parent.Id}, reason=inherit-facing-without-parent-facing.");
                }

                if (!World.Has<FacingDirection>(child))
                {
                    throw new InvalidOperationException(
                        $"{ParentPositionMissingError}: child={child.Id}, reason=inherit-facing-without-child-facing.");
                }
            }

            float parentFacing = World.Has<FacingDirection>(parent)
                ? World.Get<FacingDirection>(parent).AngleRad
                : 0f;
            float ownFacing = AttachedPoseMath.ResolveOwnFacingRad(
                World.Has<FacingDirection>(child),
                World.Has<FacingDirection>(child) ? World.Get<FacingDirection>(child).AngleRad : 0f,
                parentFacing);
            _ = AttachedPoseMath.ComposeWorldPosition(
                in World.Get<WorldPositionCm>(parent).Value,
                parentFacing,
                ownFacing,
                in localPose);
        }

        /// <summary>
        /// 父已死的挂接子实体自愈：拆边、摘局部姿、Attached 写权归还 Nav。
        /// 与 RTS 先例同构——父死亡是常规玩法事件，子实体不允许带着永久 Attached 写权滞留。
        /// </summary>
        private void CleanupOrphan(Entity child, Entity parent)
        {
            if (World.Has<PoseAuthority>(child) &&
                World.Get<PoseAuthority>(child).Value == PoseAuthorityKind.Attached)
            {
                if (_poseAuthorityArbiter == null)
                {
                    throw new InvalidOperationException(
                        $"{PoseAuthorityConflictError}: child={child.Id}, reason=orphan-cleanup-requires-arbiter.");
                }

                _poseAuthorityArbiter.RequestAttachedHandback(World, child);
            }

            // 挂接期间挂起的 nav 成员身份随自愈恢复——父死亡是常规玩法事件，
            // 子实体以独立 nav 成员身份重新出现（绑定系统按已提交位姿重新播种）。
            Ludots.Core.MassNavigation.Runtime.MassNavigationMembership.Restore(World, child);

            if (World.Has<AttachedLocalPose>(child))
            {
                World.Remove<AttachedLocalPose>(child);
            }

            if (World.Has<ChildOf>(child) && World.Get<ChildOf>(child).Parent == parent)
            {
                RelationOps.RemoveParent(World, child);
            }

            LastOrphanCleanupCount++;
        }

    }
}
