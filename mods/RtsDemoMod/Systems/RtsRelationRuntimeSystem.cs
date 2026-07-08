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

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly TagOps _tagOps;

        private int _constructingTagId;
        private int _builderAttachedTagId;
        private int _morphConsumedTagId;
        private int _warpingTagId;
        private int _ungarrisonAllTagId;

        public RtsRelationRuntimeSystem(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _tagOps = engine.GetService(CoreServiceKeys.TagOps) ?? new TagOps();
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
            SyncSelectionAvailability();
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

        private int ResolveTagId(int current, string tagName)
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
            var pendingQuery = new QueryDescription().WithAll<GameplayTagContainer, WorldPositionCm>();
            _world.Query(in pendingQuery, (Entity entity, ref GameplayTagContainer tags, ref WorldPositionCm position) =>
            {
                bool wantsBuilderAttach = tags.HasTag(_builderAttachedTagId);
                bool wantsMorphAttach = tags.HasTag(_morphConsumedTagId);
                if (!wantsBuilderAttach && !wantsMorphAttach)
                {
                    return;
                }

                if (_world.Has<ChildOf>(entity))
                {
                    return;
                }

                if (!TryFindNearestConstructingHost(entity, position.Value, out Entity host))
                {
                    return;
                }

                RelationOps.SetParent(_world, entity, host);
                SnapEntityToHost(entity, host);
            });
        }

        private bool TryFindNearestConstructingHost(Entity child, in Fix64Vec2 childPosition, out Entity host)
        {
            Entity resolvedHost = Entity.Null;
            Fix64Vec2 searchPosition = childPosition;
            bool childHasTeam = _world.Has<Team>(child);
            int childTeamId = childHasTeam ? _world.Get<Team>(child).Id : 0;
            bool found = false;
            Fix64 bestDistanceSq = Fix64.Zero;

            var hostQuery = new QueryDescription().WithAll<GameplayTagContainer, WorldPositionCm>();
            _world.Query(in hostQuery, (Entity candidate, ref GameplayTagContainer tags, ref WorldPositionCm position) =>
            {
                if (candidate == child || !tags.HasTag(_constructingTagId))
                {
                    return;
                }

                if (childHasTeam)
                {
                    if (!_world.Has<Team>(candidate) || _world.Get<Team>(candidate).Id != childTeamId)
                    {
                        return;
                    }
                }

                Fix64Vec2 delta = position.Value - searchPosition;
                Fix64 distanceSq = delta.LengthSquared();
                if (!found || distanceSq < bestDistanceSq)
                {
                    resolvedHost = candidate;
                    bestDistanceSq = distanceSq;
                    found = true;
                }
            });

            host = resolvedHost;
            return found;
        }

        private void ProcessUngarrisonCommands()
        {
            var hostQuery = new QueryDescription().WithAll<GameplayTagContainer, ChildrenBuffer, WorldPositionCm>();
            _world.Query(in hostQuery, (Entity host, ref GameplayTagContainer tags, ref ChildrenBuffer children, ref WorldPositionCm position) =>
            {
                if (!tags.HasTag(_ungarrisonAllTagId) || children.Count <= 0)
                {
                    return;
                }

                var childList = SnapshotChildren(in children);
                for (int i = 0; i < childList.Count; i++)
                {
                    Entity child = childList[i];
                    if (!_world.IsAlive(child))
                    {
                        continue;
                    }

                    DetachChildToHostPerimeter(child, host, position.Value, i, childList.Count);
                }

                RemovePersistentTag(host, _ungarrisonAllTagId);
            });
        }

        private void ProcessCompletedConstructionHosts()
        {
            var hostQuery = new QueryDescription().WithAll<ChildrenBuffer, WorldPositionCm>();
            _world.Query(in hostQuery, (Entity host, ref ChildrenBuffer children, ref WorldPositionCm position) =>
            {
                if (children.Count <= 0 || HasTag(host, _constructingTagId))
                {
                    return;
                }

                var childList = SnapshotChildren(in children);
                int detachIndex = 0;
                for (int i = 0; i < childList.Count; i++)
                {
                    Entity child = childList[i];
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
                        RelationOps.RemoveParent(_world, child);
                        _world.Destroy(child);
                        continue;
                    }

                    RemovePersistentTag(child, _builderAttachedTagId);
                    DetachChildToHostPerimeter(child, host, position.Value, detachIndex, Math.Max(1, childList.Count));
                    detachIndex++;
                }
            });
        }

        private void SyncAttachedChildrenToParents()
        {
            var childQuery = new QueryDescription().WithAll<ChildOf>();
            _world.Query(in childQuery, (Entity child, ref ChildOf childOf) =>
            {
                if (!_world.IsAlive(childOf.Parent))
                {
                    RelationOps.RemoveParent(_world, child);
                    return;
                }

                SnapEntityToHost(child, childOf.Parent);
            });
        }

        private void SyncSelectionAvailability()
        {
            var selectableQuery = new QueryDescription().WithAll<CommandSourceSelectableTag>();
            _world.Query(in selectableQuery, (Entity entity, ref CommandSourceSelectableTag _) =>
            {
                bool disabled =
                    _world.Has<ChildOf>(entity) ||
                    HasTag(entity, _constructingTagId) ||
                    HasTag(entity, _warpingTagId) ||
                    HasTag(entity, _builderAttachedTagId) ||
                    HasTag(entity, _morphConsumedTagId);

                if (_world.Has<CommandSourceSelectableState>(entity))
                {
                    ref var state = ref _world.Get<CommandSourceSelectableState>(entity);
                    state = disabled ? CommandSourceSelectableState.Disabled : CommandSourceSelectableState.EnabledByDefault;
                }
                else
                {
                    _world.Add(entity, disabled ? CommandSourceSelectableState.Disabled : CommandSourceSelectableState.EnabledByDefault);
                }
            });
        }

        private void DetachChildToHostPerimeter(Entity child, Entity host, in Fix64Vec2 hostPosition, int index, int total)
        {
            RelationOps.RemoveParent(_world, child);
            SetWorldPosition(child, hostPosition + ComputeDetachOffset(index, total));
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

        private List<Entity> SnapshotChildren(in ChildrenBuffer children)
        {
            var result = new List<Entity>(children.Count);
            for (int i = 0; i < children.Count; i++)
            {
                result.Add(children.Get(i));
            }

            return result;
        }

        private void SnapEntityToHost(Entity entity, Entity host)
        {
            if (!_world.Has<WorldPositionCm>(host))
            {
                return;
            }

            Fix64Vec2 hostPosition = _world.Get<WorldPositionCm>(host).Value;
            Fix64Vec2 previousPosition = _world.Has<PreviousWorldPositionCm>(host)
                ? _world.Get<PreviousWorldPositionCm>(host).Value
                : hostPosition;

            SetWorldPosition(entity, hostPosition, previousPosition);
        }

        private void SetWorldPosition(Entity entity, in Fix64Vec2 position)
        {
            SetWorldPosition(entity, position, position);
        }

        private void SetWorldPosition(Entity entity, in Fix64Vec2 position, in Fix64Vec2 previous)
        {
            if (_world.Has<WorldPositionCm>(entity))
            {
                ref var current = ref _world.Get<WorldPositionCm>(entity);
                current.Value = position;
            }
            else
            {
                _world.Add(entity, new WorldPositionCm { Value = position });
            }

            if (_world.Has<PreviousWorldPositionCm>(entity))
            {
                ref var previousPosition = ref _world.Get<PreviousWorldPositionCm>(entity);
                previousPosition.Value = previous;
            }
            else
            {
                _world.Add(entity, new PreviousWorldPositionCm { Value = previous });
            }
        }

        private bool HasTag(Entity entity, int tagId)
        {
            return tagId > 0 &&
                   _world.IsAlive(entity) &&
                   _world.Has<GameplayTagContainer>(entity) &&
                   _world.Get<GameplayTagContainer>(entity).HasTag(tagId);
        }

        private void RemovePersistentTag(Entity entity, int tagId)
        {
            if (tagId <= 0 || !_world.IsAlive(entity) || !_world.Has<GameplayTagContainer>(entity) || !_world.Has<TagCountContainer>(entity))
            {
                return;
            }

            if (!_world.Has<DirtyFlags>(entity))
            {
                _world.Add(entity, new DirtyFlags());
            }

            ref var tags = ref _world.Get<GameplayTagContainer>(entity);
            ref var counts = ref _world.Get<TagCountContainer>(entity);
            ref var dirty = ref _world.Get<DirtyFlags>(entity);
            _tagOps.RemoveTag(ref tags, ref counts, tagId, ref dirty);
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
    }
}
