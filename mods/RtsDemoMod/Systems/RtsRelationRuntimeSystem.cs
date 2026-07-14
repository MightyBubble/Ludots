using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Systems
{
    public sealed class RtsRelationRuntimeSystem : ISystem<float>
    {
        private const string ConstructingTagName = "State.Rts.Constructing";
        private const string BuilderAttachedTagName = "State.Rts.BuilderAttached";
        private const string MorphConsumedTagName = "State.Rts.MorphConsumed";
        private const string WarpingTagName = "State.Rts.Warping";
        private const string UngarrisonAllTagName = "Command.Rts.UngarrisonAll";

        private static readonly QueryDescription TaggedPositionQuery = new QueryDescription()
            .WithAll<GameplayTagContainer, WorldPositionCm>();
        private static readonly QueryDescription UngarrisonHostQuery = new QueryDescription()
            .WithAll<GameplayTagContainer, ChildrenBuffer, WorldPositionCm>();
        private static readonly QueryDescription ConstructionHostQuery = new QueryDescription()
            .WithAll<ChildrenBuffer, WorldPositionCm>();
        private static readonly QueryDescription AttachedChildQuery = new QueryDescription()
            .WithAll<ChildOf>();
        private static readonly QueryDescription SelectableWithStateQuery = new QueryDescription()
            .WithAll<CommandSourceSelectableTag, CommandSourceSelectableState>();
        private static readonly QueryDescription SelectableWithoutStateQuery = new QueryDescription()
            .WithAll<CommandSourceSelectableTag>()
            .WithNone<CommandSourceSelectableState>();

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly TagOps _tagOps;
        private readonly List<Entity> _constructingHosts = new(64);
        private readonly List<Entity> _pendingAttachEntities = new(256);
        private readonly List<Entity> _ungarrisonHosts = new(64);
        private readonly List<Entity> _completedHosts = new(64);
        private readonly List<Entity> _attachedChildren = new(256);
        private readonly List<Entity> _missingSelectableStates = new(64);

        private int _constructingTagId;
        private int _builderAttachedTagId;
        private int _morphConsumedTagId;
        private int _warpingTagId;
        private int _ungarrisonAllTagId;

        public RtsRelationRuntimeSystem(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("RtsRelationRuntimeSystem requires engine TagOps.");
        }

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
            SyncAttachedChildrenToParents();
            SyncCommandSourceAvailability();
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
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
            SortEntities(_constructingHosts);

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
            SortEntities(_pendingAttachEntities);

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

                RelationOps.SetParent(_world, entity, host);
                if (!_world.Has<ChildOf>(entity) || _world.Get<ChildOf>(entity).Parent != host)
                {
                    throw new InvalidOperationException(
                        $"RTS.RELATION.ERR.AttachFailed: child={entity.Id}, host={host.Id}.");
                }

                SnapEntityToHost(entity, host);
            }
        }

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
                    (distanceSq == bestDistanceSq && EntityStableComparer.Instance.Compare(candidate, resolvedHost) < 0))
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
            SortEntities(_ungarrisonHosts);

            Span<Entity> childrenSnapshot = stackalloc Entity[GasConstants.MAX_CHILDREN_BUFFER_CAPACITY];
            for (int hostIndex = 0; hostIndex < _ungarrisonHosts.Count; hostIndex++)
            {
                Entity host = _ungarrisonHosts[hostIndex];
                if (!_world.IsAlive(host))
                {
                    continue;
                }

                ChildrenBuffer children = _world.Get<ChildrenBuffer>(host);
                int childCount = SnapshotChildren(in children, childrenSnapshot);
                Fix64Vec2 hostPosition = _world.Get<WorldPositionCm>(host).Value;
                for (int i = 0; i < childCount; i++)
                {
                    Entity child = childrenSnapshot[i];
                    if (!_world.IsAlive(child))
                    {
                        continue;
                    }

                    DetachChildToHostPerimeter(child, host, in hostPosition, i, childCount);
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
            SortEntities(_completedHosts);

            Span<Entity> childrenSnapshot = stackalloc Entity[GasConstants.MAX_CHILDREN_BUFFER_CAPACITY];
            for (int hostIndex = 0; hostIndex < _completedHosts.Count; hostIndex++)
            {
                Entity host = _completedHosts[hostIndex];
                if (!_world.IsAlive(host) || HasTag(host, _constructingTagId))
                {
                    continue;
                }

                ChildrenBuffer children = _world.Get<ChildrenBuffer>(host);
                int childCount = SnapshotChildren(in children, childrenSnapshot);
                Fix64Vec2 hostPosition = _world.Get<WorldPositionCm>(host).Value;
                int detachIndex = 0;
                for (int i = 0; i < childCount; i++)
                {
                    Entity child = childrenSnapshot[i];
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
                        RemoveRequiredParent(child, host);
                        _world.Destroy(child);
                        continue;
                    }

                    RemovePersistentTag(child, _builderAttachedTagId);
                    DetachChildToHostPerimeter(
                        child,
                        host,
                        in hostPosition,
                        detachIndex,
                        Math.Max(1, childCount));
                    detachIndex++;
                }
            }
        }

        private void SyncAttachedChildrenToParents()
        {
            _attachedChildren.Clear();
            var job = new CollectAttachedChildrenJob { Entities = _attachedChildren };
            _world.InlineEntityQuery<CollectAttachedChildrenJob, ChildOf>(in AttachedChildQuery, ref job);
            SortEntities(_attachedChildren);

            for (int i = 0; i < _attachedChildren.Count; i++)
            {
                Entity child = _attachedChildren[i];
                if (!_world.IsAlive(child) || !_world.Has<ChildOf>(child))
                {
                    continue;
                }

                Entity parent = _world.Get<ChildOf>(child).Parent;
                if (!_world.IsAlive(parent))
                {
                    RelationOps.RemoveParent(_world, child);
                    continue;
                }

                SnapEntityToHost(child, parent);
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
                SortEntities(_missingSelectableStates);
                throw new InvalidOperationException(
                    $"RTS.COMMAND_SOURCE.ERR.MissingSelectableState: entity={_missingSelectableStates[0].Id}, count={_missingSelectableStates.Count}.");
            }
        }

        private void DetachChildToHostPerimeter(
            Entity child,
            Entity host,
            in Fix64Vec2 hostPosition,
            int index,
            int total)
        {
            RemoveRequiredParent(child, host);
            SetWorldPosition(child, hostPosition + ComputeDetachOffset(index, total));
        }

        private void RemoveRequiredParent(Entity child, Entity expectedParent)
        {
            if (!_world.Has<ChildOf>(child))
            {
                throw new InvalidOperationException(
                    $"RTS.RELATION.ERR.MissingChildOf: child={child.Id}, expectedParent={expectedParent.Id}.");
            }

            Entity actualParent = _world.Get<ChildOf>(child).Parent;
            if (actualParent != expectedParent)
            {
                throw new InvalidOperationException(
                    $"RTS.RELATION.ERR.ParentMismatch: child={child.Id}, expectedParent={expectedParent.Id}, actualParent={actualParent.Id}.");
            }

            RelationOps.RemoveParent(_world, child);
            if (_world.Has<ChildOf>(child) ||
                (_world.IsAlive(expectedParent) &&
                 _world.Has<ChildrenBuffer>(expectedParent) &&
                 _world.Get<ChildrenBuffer>(expectedParent).Contains(in child)))
            {
                throw new InvalidOperationException(
                    $"RTS.RELATION.ERR.DetachFailed: child={child.Id}, parent={expectedParent.Id}.");
            }
        }

        private static Fix64Vec2 ComputeDetachOffset(int index, int total)
        {
            if (total <= 0)
            {
                return Fix64Vec2.Zero;
            }

            float angle = (MathF.PI * 2f * index) / Math.Max(1, total);
            return Fix64Vec2.FromFloat(MathF.Cos(angle) * 180f, MathF.Sin(angle) * 180f);
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

        private void SnapEntityToHost(Entity entity, Entity host)
        {
            RequirePositionState(host, "host");
            Fix64Vec2 hostPosition = _world.Get<WorldPositionCm>(host).Value;
            Fix64Vec2 previousPosition = _world.Get<PreviousWorldPositionCm>(host).Value;
            SetWorldPosition(entity, in hostPosition, in previousPosition);
        }

        private void SetWorldPosition(Entity entity, in Fix64Vec2 position)
        {
            SetWorldPosition(entity, in position, in position);
        }

        private void SetWorldPosition(Entity entity, in Fix64Vec2 position, in Fix64Vec2 previous)
        {
            RequirePositionState(entity, "entity");
            ref WorldPositionCm current = ref _world.Get<WorldPositionCm>(entity);
            current.Value = position;
            ref PreviousWorldPositionCm previousPosition = ref _world.Get<PreviousWorldPositionCm>(entity);
            previousPosition.Value = previous;
        }

        private void RequirePositionState(Entity entity, string role)
        {
            if (!_world.IsAlive(entity) ||
                !_world.Has<WorldPositionCm>(entity) ||
                !_world.Has<PreviousWorldPositionCm>(entity))
            {
                throw new InvalidOperationException(
                    $"RTS.POSITION.ERR.MissingPositionState: role={role}, entity={entity.Id}.");
            }
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

        private static void SortEntities(List<Entity> entities)
        {
            if (entities.Count > 1)
            {
                entities.Sort(EntityStableComparer.Instance);
            }
        }

        private sealed class EntityStableComparer : IComparer<Entity>
        {
            public static readonly EntityStableComparer Instance = new EntityStableComparer();

            public int Compare(Entity x, Entity y)
            {
                int compare = x.WorldId.CompareTo(y.WorldId);
                if (compare != 0) return compare;
                compare = x.Id.CompareTo(y.Id);
                if (compare != 0) return compare;
                return x.Version.CompareTo(y.Version);
            }
        }

        private struct CollectConstructingHostsJob : IForEachWithEntity<GameplayTagContainer, WorldPositionCm>
        {
            public List<Entity> Entities;
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
            public List<Entity> Entities;
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
            public List<Entity> Entities;
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
            public List<Entity> Entities;

            public void Update(Entity entity, ref ChildrenBuffer children, ref WorldPositionCm _)
            {
                if (children.Count > 0)
                {
                    Entities.Add(entity);
                }
            }
        }

        private struct CollectAttachedChildrenJob : IForEachWithEntity<ChildOf>
        {
            public List<Entity> Entities;

            public void Update(Entity entity, ref ChildOf _)
            {
                Entities.Add(entity);
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
            public List<Entity> Entities;

            public void Update(Entity entity, ref CommandSourceSelectableTag _)
            {
                Entities.Add(entity);
            }
        }
    }
}
