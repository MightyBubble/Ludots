using System;
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
    /// 深度序（父先子后）保证多层结构（底盘→炮塔→炮管）一步内一致；parent-moved 门跳过
    /// 未移动父的整棵位置依赖子树（静态父样例零重算）；0 alloc（全部缓冲预分配复用）。
    /// 写权合同：sink 只写 PoseAuthority==Attached 或无 PoseAuthority 的子实体。
    /// </summary>
    public sealed class AttachmentPositionSyncSystem : BaseSystem<World, float>
    {
        public const int ScratchCapacity = 8192;
        public const string CapacityExceededError = "GAS.ATTACH.SYNC.ERR.CapacityExceeded";
        public const string ParentPositionMissingError = "GAS.ATTACH.SYNC.ERR.ParentPositionMissing";
        public const string PoseAuthorityConflictError = "GAS.ATTACH.SYNC.ERR.PoseAuthorityConflict";

        private static readonly QueryDescription AttachedQuery = new QueryDescription()
            .WithAll<ChildOf, AttachedLocalPose>();

        private readonly Entity[] _entities = new Entity[ScratchCapacity];
        private readonly ChildOf[] _childOf = new ChildOf[ScratchCapacity];
        private readonly AttachedLocalPose[] _localPose = new AttachedLocalPose[ScratchCapacity];
        private readonly int[] _depth = new int[ScratchCapacity];
        private readonly PoseAuthorityArbiter? _poseAuthorityArbiter;

        public AttachmentPositionSyncSystem(World world, PoseAuthorityArbiter? poseAuthorityArbiter = null) : base(world)
        {
            _poseAuthorityArbiter = poseAuthorityArbiter;
        }

        /// <summary>上一 Update 处理的子实体数（headless 验收日志用）。</summary>
        public int LastAppliedCount { get; private set; }

        /// <summary>上一 Update 被 parent-moved 门跳过的子实体数。</summary>
        public int LastGateSkippedCount { get; private set; }

        /// <summary>上一 Update 因父死亡被清理的子实体数。</summary>
        public int LastOrphanCleanupCount { get; private set; }

        /// <summary>上一 Update 处理到的最大挂接深度（0 = 直接子）。</summary>
        public int LastMaxDepth { get; private set; }

        public override void Update(in float dt)
        {
            LastAppliedCount = 0;
            LastGateSkippedCount = 0;
            LastOrphanCleanupCount = 0;
            LastMaxDepth = 0;

            int count = 0;
            int maxDepth = 0;
            foreach (ref var chunk in World.Query(in AttachedQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var childOfSpan = chunk.GetSpan<ChildOf>();
                var localPoseSpan = chunk.GetSpan<AttachedLocalPose>();
                foreach (var index in chunk)
                {
                    if (count >= ScratchCapacity)
                    {
                        throw new InvalidOperationException(
                            $"{CapacityExceededError}: staged={count + 1}, capacity={ScratchCapacity}.");
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ChildOf childOf = childOfSpan[index];
                    _entities[count] = entity;
                    _childOf[count] = childOf;
                    _localPose[count] = localPoseSpan[index];
                    int depth = ResolveAttachmentDepth(entity, childOf.Parent);
                    _depth[count] = depth;
                    if (depth > maxDepth)
                    {
                        maxDepth = depth;
                    }

                    count++;
                }
            }

            LastMaxDepth = maxDepth;

            // 深度序：同一固定步内父层先落定，子层读到的是本步父位姿。
            for (int depth = 0; depth <= maxDepth; depth++)
            {
                for (int i = 0; i < count; i++)
                {
                    if (_depth[i] != depth)
                    {
                        continue;
                    }

                    ProcessChild(_entities[i], in _childOf[i], in _localPose[i]);
                }
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

            ref readonly WorldPositionCm parentPosition = ref World.Get<WorldPositionCm>(parent);
            bool parentMoved = World.Has<PreviousWorldPositionCm>(parent)
                ? parentPosition.Value != World.Get<PreviousWorldPositionCm>(parent).Value
                : true;
            if (!parentMoved && !AttachedPoseMath.DependsOnFacing(in localPose))
            {
                LastGateSkippedCount++;
                return;
            }

            float parentFacing = World.Has<FacingDirection>(parent) ? World.Get<FacingDirection>(parent).AngleRad : 0f;
            float ownFacing = World.Has<FacingDirection>(child) ? World.Get<FacingDirection>(child).AngleRad : 0f;
            Fix64Vec2 worldPosition = AttachedPoseMath.ComposeWorldPosition(
                in parentPosition.Value,
                parentFacing,
                ownFacing,
                in localPose);

            ref WorldPositionCm current = ref World.Get<WorldPositionCm>(child);
            current.Value = worldPosition;
            if (World.Has<PreviousWorldPositionCm>(child))
            {
                World.Get<PreviousWorldPositionCm>(child).Value = worldPosition;
            }
            else
            {
                World.Add(child, new PreviousWorldPositionCm { Value = worldPosition });
            }

            if (localPose.InheritParentFacing != 0)
            {
                if (!World.Has<FacingDirection>(parent))
                {
                    throw new InvalidOperationException(
                        $"{ParentPositionMissingError}: parent={parent.Id}, reason=inherit-facing-without-parent-facing.");
                }

                if (World.Has<FacingDirection>(child))
                {
                    World.Get<FacingDirection>(child).AngleRad = parentFacing + localPose.LocalFacingRad.ToFloat();
                }
                else
                {
                    World.Add(child, new FacingDirection
                    {
                        AngleRad = parentFacing + localPose.LocalFacingRad.ToFloat(),
                    });
                }
            }

            LastAppliedCount++;
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

        private int ResolveAttachmentDepth(Entity child, Entity parent)
        {
            int depth = 0;
            Entity current = parent;
            while (World.IsAlive(current) && World.Has<ChildOf>(current))
            {
                depth++;
                current = World.Get<ChildOf>(current).Parent;
                if (depth > 1024)
                {
                    throw new InvalidOperationException(
                        "GAS.ATTACH.SYNC.ERR.DepthWalkExceeded: the ChildOf graph invariant is broken.");
                }
            }

            return depth;
        }
    }
}
