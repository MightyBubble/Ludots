using System;
using Arch.Buffer;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Attachment;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Systems
{
    /// <summary>
    /// 驻防/施工进出的玩法编排（tag 驱动）。绑定、写权、位置跟随与周界落位全部委托 Core
    /// attachment 原子 op（AttachmentOps）：本系统只决定"谁在何时进出"，不再自带父子边
    /// 缓冲、逐帧吸附与周界散布数学。
    /// </summary>
    public sealed class RtsRelationRuntimeSystem : ISystem<float>
    {
        private const string ConstructingTagName = "State.Rts.Constructing";
        private const string BuilderAttachedTagName = "State.Rts.BuilderAttached";
        private const string MorphConsumedTagName = "State.Rts.MorphConsumed";
        private const string WarpingTagName = "State.Rts.Warping";
        private const string UngarrisonAllTagName = "Command.Rts.UngarrisonAll";
        private const int DetachPerimeterRadiusCm = 180;

        private static readonly QueryDescription TaggedPositionQuery = new QueryDescription()
            .WithAll<GameplayTagContainer, WorldPositionCm>();
        private static readonly QueryDescription UngarrisonHostQuery = new QueryDescription()
            .WithAll<GameplayTagContainer, ChildrenBuffer, WorldPositionCm>();
        private static readonly QueryDescription ConstructionHostQuery = new QueryDescription()
            .WithAll<ChildrenBuffer, WorldPositionCm>();
        private static readonly QueryDescription SelectableWithStateQuery = new QueryDescription()
            .WithAll<CommandSourceSelectableTag, CommandSourceSelectableState>();
        private static readonly QueryDescription SelectableWithoutStateQuery = new QueryDescription()
            .WithAll<CommandSourceSelectableTag>()
            .WithNone<CommandSourceSelectableState>();

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly TagOps _tagOps;
        private readonly PoseAuthorityArbiter _poseAuthorityArbiter;
        private readonly EntityScratchBuffer _constructingHosts;
        private readonly EntityScratchBuffer _pendingAttachEntities;
        private readonly EntityScratchBuffer _ungarrisonHosts;
        private readonly EntityScratchBuffer _completedHosts;
        private readonly EntityScratchBuffer _missingSelectableStates;
        private readonly CommandBuffer _structuralCommands;
        private readonly Entity[] _childrenSnapshot;

        private int _constructingTagId;
        private int _builderAttachedTagId;
        private int _morphConsumedTagId;
        private int _warpingTagId;
        private int _ungarrisonAllTagId;

        public RtsRelationRuntimeSystem(GameEngine engine, int entityScratchCapacity)
        {
            if (entityScratchCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityScratchCapacity));
            }

            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("RtsRelationRuntimeSystem requires engine TagOps.");
            _poseAuthorityArbiter = engine.GetService(CoreServiceKeys.PoseAuthorityArbiter)
                ?? throw new InvalidOperationException("RtsRelationRuntimeSystem requires engine PoseAuthorityArbiter.");
            _constructingHosts = new EntityScratchBuffer(entityScratchCapacity);
            _pendingAttachEntities = new EntityScratchBuffer(entityScratchCapacity);
            _ungarrisonHosts = new EntityScratchBuffer(entityScratchCapacity);
            _completedHosts = new EntityScratchBuffer(entityScratchCapacity);
            _missingSelectableStates = new EntityScratchBuffer(entityScratchCapacity);
            _structuralCommands = new CommandBuffer(checked(entityScratchCapacity * 2));
            _childrenSnapshot = new Entity[GasConstants.MAX_CHILDREN_BUFFER_CAPACITY];        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!IsRtsMapActive())
            {
                return;
            }

            EnsureTagIds();
            AttachPendingUnitsToConstruction();
            ProcessUngarrisonCommands();
            ProcessCompletedConstructionHosts();
            SyncCommandSourceAvailability();
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
            _structuralCommands.Dispose();
        }

        private void EnsureTagIds()
        {
            _constructingTagId = ResolveTagId(_constructingTagId, ConstructingTagName);
            _builderAttachedTagId = ResolveTagId(_builderAttachedTagId, BuilderAttachedTagName);
            _morphConsumedTagId = ResolveTagId(_morphConsumedTagId, MorphConsumedTagName);
            _warpingTagId = ResolveTagId(_warpingTagId, WarpingTagName);
            _ungarrisonAllTagId = ResolveTagId(_ungarrisonAllTagId, UngarrisonAllTagName);
        }

        private static int ResolveTagId(int current, string tagName)
        {
            if (current > 0)
            {
                return current;
            }

            int resolved = TagRegistry.GetId(tagName);
            if (resolved <= 0)
            {
                throw new InvalidOperationException($"[RtsDemoMod] Required tag '{tagName}' was not registered.");
            }

            return resolved;
        }

        private void AttachPendingUnitsToConstruction()
        {
            _constructingHosts.Clear();
            var hostJob = new CollectConstructingHostsJob
            {
                Entities = _constructingHosts,
                ConstructingTagId = _constructingTagId,
            };
            _world.InlineEntityQuery<CollectConstructingHostsJob, GameplayTagContainer, WorldPositionCm>(
                in TaggedPositionQuery,
                ref hostJob);
            _constructingHosts.Sort();

            _pendingAttachEntities.Clear();
            var pendingJob = new CollectPendingAttachEntitiesJob
            {
                Entities = _pendingAttachEntities,
                BuilderAttachedTagId = _builderAttachedTagId,
                MorphConsumedTagId = _morphConsumedTagId,
            };
            _world.InlineEntityQuery<CollectPendingAttachEntitiesJob, GameplayTagContainer, WorldPositionCm>(
                in TaggedPositionQuery,
                ref pendingJob);
            _pendingAttachEntities.Sort();

            for (int i = 0; i < _pendingAttachEntities.Count; i++)
            {
                Entity entity = _pendingAttachEntities[i];
                if (!_world.IsAlive(entity) || _world.Has<ChildOf>(entity))
                {
                    continue;
                }

                Fix64Vec2 position = _world.Get<WorldPositionCm>(entity).Value;
                if (!TryFindNearestConstructingHost(entity, in position, out Entity host))
                {
                    continue;
                }

                AttachmentOps.Attach(
                    _world,
                    _poseAuthorityArbiter,
                    entity,
                    host,
                    AnchoredAtParentPose);
            }
        }

        /// <summary>驻防吸附位姿：零偏移锚定在宿主位置（写权由 Attach 授予/归还）。</summary>
        private static AttachedLocalPose AnchoredAtParentPose => new AttachedLocalPose
        {
            OffsetCm = Fix64Vec2.Zero,
            LocalFacingRad = Fix64.Zero,
            InheritParentFacing = 0,
            OffsetRotation = AttachedOffsetRotation.None,
        };

        private bool TryFindNearestConstructingHost(Entity child, in Fix64Vec2 childPosition, out Entity host)
        {
            Entity resolvedHost = Entity.Null;
            bool childHasTeam = _world.Has<Team>(child);
            int childTeamId = childHasTeam ? _world.Get<Team>(child).Id : 0;
            bool found = false;
            Fix64 bestDistanceSq = Fix64.Zero;

            for (int i = 0; i < _constructingHosts.Count; i++)
            {
                Entity candidate = _constructingHosts[i];
                if (candidate == child || !_world.IsAlive(candidate))
                {
                    continue;
                }

                if (childHasTeam &&
                    (!_world.Has<Team>(candidate) || _world.Get<Team>(candidate).Id != childTeamId))
                {
                    continue;
                }

                Fix64Vec2 delta = _world.Get<WorldPositionCm>(candidate).Value - childPosition;
                Fix64 distanceSq = delta.LengthSquared();
                if (!found ||
                    distanceSq < bestDistanceSq ||
                    (distanceSq == bestDistanceSq && CompareStable(candidate, resolvedHost) < 0))
                {
                    resolvedHost = candidate;
                    bestDistanceSq = distanceSq;
                    found = true;
                }
            }

            host = resolvedHost;
            return found;
        }

        private void ProcessUngarrisonCommands()
        {
            _ungarrisonHosts.Clear();
            var job = new CollectUngarrisonHostsJob
            {
                Entities = _ungarrisonHosts,
                UngarrisonAllTagId = _ungarrisonAllTagId,
            };
            _world.InlineEntityQuery<CollectUngarrisonHostsJob, GameplayTagContainer, ChildrenBuffer, WorldPositionCm>(
                in UngarrisonHostQuery,
                ref job);
            _ungarrisonHosts.Sort();

            for (int hostIndex = 0; hostIndex < _ungarrisonHosts.Count; hostIndex++)
            {
                Entity host = _ungarrisonHosts[hostIndex];
                if (!_world.IsAlive(host))
                {
                    continue;
                }

                ChildrenBuffer children = _world.Get<ChildrenBuffer>(host);
                int childCount = SnapshotChildren(in children, _childrenSnapshot);
                for (int i = 0; i < childCount; i++)
                {
                    Entity child = _childrenSnapshot[i];
                    if (!_world.IsAlive(child))
                    {
                        continue;
                    }

                    AttachmentOps.DetachToPerimeter(
                        _world,
                        _poseAuthorityArbiter,
                        child,
                        DetachPerimeterRadiusCm,
                        ringSlot: i,
                        ringSlotCount: childCount);
                }

                RemovePersistentTag(host, _ungarrisonAllTagId);
            }
        }

        private void ProcessCompletedConstructionHosts()
        {
            _completedHosts.Clear();
            var job = new CollectConstructionHostsJob { Entities = _completedHosts };
            _world.InlineEntityQuery<CollectConstructionHostsJob, ChildrenBuffer, WorldPositionCm>(
                in ConstructionHostQuery,
                ref job);
            _completedHosts.Sort();

            for (int hostIndex = 0; hostIndex < _completedHosts.Count; hostIndex++)
            {
                Entity host = _completedHosts[hostIndex];
                if (!_world.IsAlive(host) || HasTag(host, _constructingTagId))
                {
                    continue;
                }

                ChildrenBuffer children = _world.Get<ChildrenBuffer>(host);
                int childCount = SnapshotChildren(in children, _childrenSnapshot);
                int detachSlot = 0;
                for (int i = 0; i < childCount; i++)
                {
                    Entity child = _childrenSnapshot[i];
                    if (!_world.IsAlive(child))
                    {
                        continue;
                    }

                    bool isBuilder = HasTag(child, _builderAttachedTagId);
                    bool isMorph = HasTag(child, _morphConsumedTagId);
                    if (!isBuilder && !isMorph)
                    {
                        continue;
                    }

                    if (isMorph)
                    {
                        RemovePersistentTag(child, _morphConsumedTagId);
                        if (_world.IsAlive(child) && _world.Has<ChildOf>(child))
                        {
                            AttachmentOps.Detach(
                                _world,
                                _poseAuthorityArbiter,
                                child,
                                DetachPlacement.KeepWorldPose,
                                0);
                        }

                        _structuralCommands.Destroy(child);
                        continue;
                    }

                    RemovePersistentTag(child, _builderAttachedTagId);
                    AttachmentOps.DetachToPerimeter(
                        _world,
                        _poseAuthorityArbiter,
                        child,
                        DetachPerimeterRadiusCm,
                        ringSlot: detachSlot,
                        ringSlotCount: Math.Max(1, childCount));
                    detachSlot++;
                }
            }

            if (_structuralCommands.Size > 0)
            {
                _structuralCommands.Playback(_world);
            }
        }

        private void SyncCommandSourceAvailability()
        {
            var job = new SyncCommandSourceAvailabilityJob
            {
                World = _world,
                ConstructingTagId = _constructingTagId,
                WarpingTagId = _warpingTagId,
                BuilderAttachedTagId = _builderAttachedTagId,
                MorphConsumedTagId = _morphConsumedTagId,
            };
            _world.InlineEntityQuery<SyncCommandSourceAvailabilityJob, CommandSourceSelectableTag, CommandSourceSelectableState>(
                in SelectableWithStateQuery,
                ref job);

            _missingSelectableStates.Clear();
            var missingJob = new CollectMissingSelectableStatesJob { Entities = _missingSelectableStates };
            _world.InlineEntityQuery<CollectMissingSelectableStatesJob, CommandSourceSelectableTag>(
                in SelectableWithoutStateQuery,
                ref missingJob);
            if (_missingSelectableStates.Count > 0)
            {
                _missingSelectableStates.Sort();
                throw new InvalidOperationException(
                    $"RTS.COMMAND_SOURCE.ERR.MissingSelectableState: entity={_missingSelectableStates[0].Id}, count={_missingSelectableStates.Count}.");
            }
        }

        private static int SnapshotChildren(in ChildrenBuffer children, Span<Entity> destination)
        {
            if (children.Count > destination.Length)
            {
                throw new InvalidOperationException(
                    $"RTS.RELATION.ERR.ChildrenSnapshotOverflow: count={children.Count}, capacity={destination.Length}.");
            }

            for (int i = 0; i < children.Count; i++)
            {
                destination[i] = children.Get(i);
            }

            return children.Count;
        }

        private bool HasTag(Entity entity, int tagId)
        {
            return HasTag(_world, entity, tagId);
        }

        private static bool HasTag(World world, Entity entity, int tagId)
        {
            return tagId > 0 &&
                   world.IsAlive(entity) &&
                   world.Has<GameplayTagContainer>(entity) &&
                   world.Get<GameplayTagContainer>(entity).HasTag(tagId);
        }

        private void RemovePersistentTag(Entity entity, int tagId)
        {
            TagOps.RequireTagState(_world, entity);
            if (!_world.Get<GameplayTagContainer>(entity).HasTag(tagId) ||
                !_tagOps.RemoveTag(_world, entity, tagId))
            {
                throw new InvalidOperationException(
                    $"RTS.TAG.ERR.RemovePersistentTagFailed: entity={entity.Id}, tagId={tagId}.");
            }
        }

        private bool IsRtsMapActive()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], "rts", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tags[i], "rts_showcase", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private struct CollectConstructingHostsJob : IForEachWithEntity<GameplayTagContainer, WorldPositionCm>
        {
            public EntityScratchBuffer Entities;
            public int ConstructingTagId;

            public void Update(Entity entity, ref GameplayTagContainer tags, ref WorldPositionCm _)
            {
                if (tags.HasTag(ConstructingTagId))
                {
                    Entities.Add(entity);
                }
            }
        }

        private struct CollectPendingAttachEntitiesJob : IForEachWithEntity<GameplayTagContainer, WorldPositionCm>
        {
            public EntityScratchBuffer Entities;
            public int BuilderAttachedTagId;
            public int MorphConsumedTagId;

            public void Update(Entity entity, ref GameplayTagContainer tags, ref WorldPositionCm _)
            {
                if (tags.HasTag(BuilderAttachedTagId) || tags.HasTag(MorphConsumedTagId))
                {
                    Entities.Add(entity);
                }
            }
        }

        private struct CollectUngarrisonHostsJob : IForEachWithEntity<GameplayTagContainer, ChildrenBuffer, WorldPositionCm>
        {
            public EntityScratchBuffer Entities;
            public int UngarrisonAllTagId;

            public void Update(
                Entity entity,
                ref GameplayTagContainer tags,
                ref ChildrenBuffer children,
                ref WorldPositionCm _)
            {
                if (children.Count > 0 && tags.HasTag(UngarrisonAllTagId))
                {
                    Entities.Add(entity);
                }
            }
        }

        private struct CollectConstructionHostsJob : IForEachWithEntity<ChildrenBuffer, WorldPositionCm>
        {
            public EntityScratchBuffer Entities;

            public void Update(Entity entity, ref ChildrenBuffer children, ref WorldPositionCm _)
            {
                if (children.Count > 0)
                {
                    Entities.Add(entity);
                }
            }
        }

        private struct SyncCommandSourceAvailabilityJob : IForEachWithEntity<CommandSourceSelectableTag, CommandSourceSelectableState>
        {
            public World World;
            public int ConstructingTagId;
            public int WarpingTagId;
            public int BuilderAttachedTagId;
            public int MorphConsumedTagId;

            public void Update(
                Entity entity,
                ref CommandSourceSelectableTag _,
                ref CommandSourceSelectableState state)
            {
                bool disabled =
                    World.Has<ChildOf>(entity) ||
                    HasTag(World, entity, ConstructingTagId) ||
                    HasTag(World, entity, WarpingTagId) ||
                    HasTag(World, entity, BuilderAttachedTagId) ||
                    HasTag(World, entity, MorphConsumedTagId);

                state = disabled
                    ? CommandSourceSelectableState.Disabled
                    : CommandSourceSelectableState.EnabledByDefault;
            }
        }

        private struct CollectMissingSelectableStatesJob : IForEachWithEntity<CommandSourceSelectableTag>
        {
            public EntityScratchBuffer Entities;

            public void Update(Entity entity, ref CommandSourceSelectableTag _)
            {
                Entities.Add(entity);
            }
        }

        private sealed class EntityScratchBuffer
        {
            private readonly Entity[] _items;

            public EntityScratchBuffer(int capacity)
            {
                _items = new Entity[capacity];
            }

            public int Count { get; private set; }
            public Entity this[int index] => _items[index];

            public void Clear()
            {
                Count = 0;
            }

            public void Add(Entity entity)
            {
                if (Count >= _items.Length)
                {
                    throw ScratchCapacityExceeded("entityScratch", Count + 1, _items.Length);
                }
                _items[Count++] = entity;
            }

            public void Sort()
            {
                HeapSort();
            }

            private void HeapSort()
            {
                for (int root = (Count >> 1) - 1; root >= 0; root--)
                {
                    SiftDown(root, Count);
                }
                for (int end = Count - 1; end > 0; end--)
                {
                    Swap(0, end);
                    SiftDown(0, end);
                }
            }

            private void SiftDown(int root, int length)
            {
                while (true)
                {
                    int child = (root << 1) + 1;
                    if (child >= length) return;
                    if (child + 1 < length && CompareStable(_items[child], _items[child + 1]) < 0)
                    {
                        child++;
                    }
                    if (CompareStable(_items[root], _items[child]) >= 0) return;
                    Swap(root, child);
                    root = child;
                }
            }

            private void Swap(int first, int second)
            {
                Entity value = _items[first];
                _items[first] = _items[second];
                _items[second] = value;
            }
        }

        private static int CompareStable(Entity first, Entity second)
        {
            int compare = first.WorldId.CompareTo(second.WorldId);
            if (compare != 0) return compare;
            compare = first.Id.CompareTo(second.Id);
            if (compare != 0) return compare;
            return first.Version.CompareTo(second.Version);
        }

        private static InvalidOperationException ScratchCapacityExceeded(string resource, int required, int capacity)
        {
            return new InvalidOperationException(
                $"RTS.RELATION.ERR.ScratchCapacityExceeded: resource={resource}, required={required}, capacity={capacity}.");
        }
    }
}
