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
    /// 同步只消费明确的空间挂接边；拓扑不变时复用派生顺序，普通子集关系不参与排序。
    /// </summary>
    public sealed class AttachmentPositionSyncSystem : BaseSystem<World, float>
    {
        public const int DefaultScratchCapacity = 8192;
        public const string CapacityExceededError = "GAS.ATTACH.SYNC.ERR.CapacityExceeded";
        public const string ParentPositionMissingError = "GAS.ATTACH.SYNC.ERR.ParentPositionMissing";
        public const string PoseAuthorityConflictError = "GAS.ATTACH.SYNC.ERR.PoseAuthorityConflict";
        public const string CycleError = "GAS.ATTACH.SYNC.ERR.CycleDetected";

        private static readonly QueryDescription AttachedQuery = new QueryDescription()
            .WithAll<ChildOf, AttachedLocalPose>();

        private readonly Entity[] _entities;
        private readonly ChildOf[] _childOf;
        private readonly AttachedLocalPose[] _localPose;
        private readonly int[] _depth;
        private readonly int[] _firstChild;
        private readonly int[] _lastChild;
        private readonly int[] _nextSibling;
        private readonly int[] _orderedIndices;
        private readonly Dictionary<Entity, int> _snapshotIndices;
        private readonly int _scratchCapacity;
        private readonly PoseAuthorityArbiter? _poseAuthorityArbiter;
        private int _snapshotCount;
        private int _maxDepth;
        private bool _topologyValid;

        public AttachmentPositionSyncSystem(
            World world,
            PoseAuthorityArbiter? poseAuthorityArbiter = null,
            int scratchCapacity = DefaultScratchCapacity) : base(world)
        {
            if (scratchCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scratchCapacity),
                    scratchCapacity,
                    "AttachmentPositionSync scratch capacity must be positive.");
            }

            _poseAuthorityArbiter = poseAuthorityArbiter;
            _scratchCapacity = scratchCapacity;
            _entities = new Entity[scratchCapacity];
            _childOf = new ChildOf[scratchCapacity];
            _localPose = new AttachedLocalPose[scratchCapacity];
            _depth = new int[scratchCapacity];
            _firstChild = new int[scratchCapacity];
            _lastChild = new int[scratchCapacity];
            _nextSibling = new int[scratchCapacity];
            _orderedIndices = new int[scratchCapacity];
            _snapshotIndices = new Dictionary<Entity, int>(scratchCapacity);
        }

        /// <summary>本实例预分配挂接子缓冲容量（来自 gasRuntimeCapacity，禁止热路径扩容）。</summary>
        public int ScratchCapacity => _scratchCapacity;

        /// <summary>上一 Update 处理的子实体数（headless 验收日志用）。</summary>
        public int LastAppliedCount { get; private set; }

        /// <summary>上一 Update 因父死亡被清理的子实体数。</summary>
        public int LastOrphanCleanupCount { get; private set; }

        /// <summary>上一 Update 处理到的最大挂接深度（0 = 直接子）。</summary>
        public int LastMaxDepth { get; private set; }

        public long TopologyBuildCount { get; private set; }

        public override void Update(in float dt)
        {
            LastAppliedCount = 0;
            LastOrphanCleanupCount = 0;
            LastMaxDepth = 0;

            int count = 0;
            foreach (ref var chunk in World.Query(in AttachedQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var childOfSpan = chunk.GetSpan<ChildOf>();
                var localPoseSpan = chunk.GetSpan<AttachedLocalPose>();
                foreach (var index in chunk)
                {
                    if (count >= _scratchCapacity)
                    {
                        _topologyValid = false;
                        throw new InvalidOperationException(
                            $"{CapacityExceededError}: staged={count + 1}, capacity={_scratchCapacity}.");
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ChildOf childOf = childOfSpan[index];
                    if (count >= _snapshotCount || _entities[count] != entity ||
                        _childOf[count].Parent != childOf.Parent)
                    {
                        _topologyValid = false;
                    }

                    _entities[count] = entity;
                    _childOf[count] = childOf;
                    _localPose[count] = localPoseSpan[index];
                    count++;
                }
            }

            if (count != _snapshotCount)
            {
                _topologyValid = false;
            }

            _snapshotCount = count;
            if (!_topologyValid)
            {
                BuildDependencyOrder(count);
                _topologyValid = true;
                TopologyBuildCount++;
            }

            LastMaxDepth = _maxDepth;
            for (int order = 0; order < count; order++)
            {
                int i = _orderedIndices[order];
                ProcessChild(_entities[i], in _childOf[i], in _localPose[i]);
            }
        }

        private void BuildDependencyOrder(int count)
        {
            _snapshotIndices.Clear();
            _maxDepth = 0;
            Array.Fill(_firstChild, -1, 0, count);
            Array.Fill(_lastChild, -1, 0, count);
            Array.Fill(_nextSibling, -1, 0, count);
            for (int i = 0; i < count; i++)
            {
                _snapshotIndices.Add(_entities[i], i);
            }

            int orderedCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (_snapshotIndices.TryGetValue(_childOf[i].Parent, out int parentIndex))
                {
                    int last = _lastChild[parentIndex];
                    if (last < 0) _firstChild[parentIndex] = i;
                    else _nextSibling[last] = i;
                    _lastChild[parentIndex] = i;
                }
                else
                {
                    _depth[i] = 0;
                    _orderedIndices[orderedCount++] = i;
                }
            }

            for (int order = 0; order < orderedCount; order++)
            {
                int parentIndex = _orderedIndices[order];
                for (int child = _firstChild[parentIndex]; child >= 0; child = _nextSibling[child])
                {
                    int depth = _depth[parentIndex] + 1;
                    _depth[child] = depth;
                    _maxDepth = Math.Max(_maxDepth, depth);
                    _orderedIndices[orderedCount++] = child;
                }
            }

            if (orderedCount != count)
            {
                throw new InvalidOperationException($"{CycleError}: unresolved={count - orderedCount}.");
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
