using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Evaluates data-declared map regions (map JSON "Regions") at per-fixed-step
    /// granularity and fires map-scoped <see cref="GameEvents.RegionEntered"/>/<see cref="GameEvents.RegionExited"/> events
    /// with the crossing entity and region id.
    ///
    /// Semantics (changed #1398 刀2 — the retired MapHeartbeat think-wave is gone):
    /// - Cadence: evaluated once per fixed step in Update, not on a think-wave event.
    ///   Enter/exit fires only when a region's inside-set actually changes; a map with no
    ///   movement fires nothing.
    /// - Map suspend: suspended sessions are not evaluated. Inside-sets survive
    ///   suspend/resume (suspended entities cannot move), so no spurious exit/enter pair
    ///   fires.
    /// - Eligible entity: MapEntity of the map + WorldPositionCm, not SuspendedTag, not
    ///   PresentationDestroyPending, and — when the region declares entityTags — carrying at
    ///   least one of the declared GameplayTags (any-of).
    /// - Dead entities leave the inside-set silently: destroying an entity (or marking it
    ///   PresentationDestroyPending) produces no RegionExited. Death is not a crossing.
    /// - Boundary positions count as inside (see <see cref="MapRegionDefinition.Contains"/>).
    /// - An entity that stops matching a region's tag filter (or loses MapEntity/WorldPositionCm)
    ///   while alive counts as an exit: it stops being region-tracked.
    /// - Fail-closed: a region referencing a tag name that TagRegistry cannot resolve throws,
    ///   naming map, region, and tag, on the first fixed tick the session is observed.
    /// </summary>
    public sealed class RegionTriggerSystem : BaseSystem<World, float>
    {
        private readonly Func<MapSessionManager?> _sessions;
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly Dictionary<MapId, MapRegionState> _states = new Dictionary<MapId, MapRegionState>();
        private readonly QueryDescription _trackedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm>()
            .WithNone<SuspendedTag, PresentationDestroyPending>();
        private readonly List<TrackedEntity> _trackedBuffer = new List<TrackedEntity>();
        private readonly HashSet<Entity> _matchedBuffer = new HashSet<Entity>();
        private readonly List<Entity> _exitBuffer = new List<Entity>();
        private readonly List<Entity> _silentRemovalBuffer = new List<Entity>();
        private readonly List<MapId> _pruneScratch = new List<MapId>();

        public RegionTriggerSystem(
            World world,
            Func<MapSessionManager?> sessions,
            TriggerManager triggerManager,
            Func<ScriptContext> contextFactory)
            : base(world)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public override void Initialize()
        {
        }

        public override void Update(in float t)
        {
            MapSessionManager? sessions = _sessions();
            if (sessions == null)
            {
                return;
            }

            SyncSessions(sessions);

            // Per-fixed-step region evaluation: only maps that declared Regions and are
            // Active are evaluated; enter/exit fires on inside-set change only.
            foreach (KeyValuePair<MapId, MapSession> pair in sessions.All)
            {
                if (pair.Value.State != MapSessionState.Active ||
                    pair.Value.MapConfig?.Regions == null)
                {
                    continue;
                }

                MapRegionState state = GetOrBuildState(pair.Value);
                if (state.Regions.Count == 0)
                {
                    continue;
                }

                EvaluateMap(state);
            }
        }

        private void SyncSessions(MapSessionManager sessions)
        {
            foreach (KeyValuePair<MapId, MapSession> pair in sessions.All)
            {
                if (pair.Value.MapConfig?.Regions == null)
                {
                    continue;
                }

                GetOrBuildState(pair.Value);
            }

            if (_states.Count == 0)
            {
                return;
            }

            _pruneScratch.Clear();
            foreach (KeyValuePair<MapId, MapRegionState> pair in _states)
            {
                if (!sessions.All.ContainsKey(pair.Key))
                {
                    _pruneScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < _pruneScratch.Count; i++)
            {
                _states.Remove(_pruneScratch[i]);
            }
        }

        private MapRegionState GetOrBuildState(MapSession session)
        {
            if (_states.TryGetValue(session.MapId, out MapRegionState? existing) &&
                ReferenceEquals(existing.Session, session))
            {
                return existing;
            }

            MapRegionState? state = BuildState(session);
            if (state == null)
            {
                // Regionless (or empty Regions) sessions never evaluate; remember the negative
                // result so a reloaded session with the same id is re-parsed.
                state = new MapRegionState(session, new List<RegionRuntime>());
            }

            _states[session.MapId] = state;
            return state;
        }

        private static MapRegionState? BuildState(MapSession session)
        {
            string mapId = session.MapId.Value;
            List<MapRegionDefinition> definitions = MapRegionDefinition.ParseList(session.MapConfig!.Regions, mapId);
            if (definitions.Count == 0)
            {
                return null;
            }

            var runtimes = new List<RegionRuntime>(definitions.Count);
            for (int i = 0; i < definitions.Count; i++)
            {
                runtimes.Add(BuildRegionRuntime(definitions[i], mapId));
            }

            return new MapRegionState(session, runtimes);
        }

        private static RegionRuntime BuildRegionRuntime(MapRegionDefinition definition, string mapId)
        {
            var filter = new GameplayTagContainer();
            bool hasFilter = definition.EntityTags.Count > 0;
            for (int i = 0; i < definition.EntityTags.Count; i++)
            {
                string tagName = definition.EntityTags[i];
                int tagId = TagRegistry.GetId(tagName);
                if (tagId == TagRegistry.InvalidId)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' region '{definition.Id}' entityTags references unknown tag '{tagName}'.");
                }

                if (tagId > GameplayTagContainer.MAX_TAG_ID)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' region '{definition.Id}' entityTags references tag '{tagName}' with id {tagId} above the GameplayTagContainer capacity {GameplayTagContainer.MAX_TAG_ID}.");
                }

                filter.AddTag(tagId);
            }

            return new RegionRuntime(definition, filter, hasFilter);
        }

        private void EvaluateMap(MapRegionState state)
        {
            if (state.Regions.Count == 0)
            {
                return;
            }

            CollectTrackedEntities(state);

            for (int r = 0; r < state.Regions.Count; r++)
            {
                EvaluateRegion(state, state.Regions[r]);
            }
        }

        private void CollectTrackedEntities(MapRegionState state)
        {
            _trackedBuffer.Clear();
            MapId mapId = state.Session.MapId;
            foreach (ref var chunk in World.Query(in _trackedQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var mapEntities = chunk.GetSpan<MapEntity>();
                var positions = chunk.GetSpan<WorldPositionCm>();

                foreach (var index in chunk)
                {
                    if (mapEntities[index].MapId != mapId)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    bool hasTags = World.TryGet<GameplayTagContainer>(entity, out GameplayTagContainer tags);
                    _trackedBuffer.Add(new TrackedEntity(entity, positions[index].Value, tags, hasTags));
                }
            }
        }

        private void EvaluateRegion(MapRegionState state, RegionRuntime region)
        {
            MapRegionDefinition definition = region.Definition;
            _matchedBuffer.Clear();
            for (int i = 0; i < _trackedBuffer.Count; i++)
            {
                TrackedEntity tracked = _trackedBuffer[i];
                if (region.HasTagFilter && (!tracked.HasTags || !tracked.Tags.Intersects(region.TagFilter)))
                {
                    continue;
                }

                if (!definition.Contains(tracked.Position))
                {
                    continue;
                }

                _matchedBuffer.Add(tracked.Entity);
                if (region.Inside.Add(tracked.Entity))
                {
                    FireRegionEvent(state.Session, GameEvents.RegionEntered, tracked.Entity, definition.Id);
                }
            }

            _exitBuffer.Clear();
            _silentRemovalBuffer.Clear();
            foreach (Entity entity in region.Inside)
            {
                if (_matchedBuffer.Contains(entity))
                {
                    continue;
                }

                if (!World.IsAlive(entity))
                {
                    _silentRemovalBuffer.Add(entity);
                    continue;
                }

                if (World.Has<PresentationDestroyPending>(entity))
                {
                    _silentRemovalBuffer.Add(entity);
                    continue;
                }

                if (World.Has<SuspendedTag>(entity))
                {
                    continue;
                }

                _exitBuffer.Add(entity);
            }

            for (int i = 0; i < _silentRemovalBuffer.Count; i++)
            {
                region.Inside.Remove(_silentRemovalBuffer[i]);
            }

            for (int i = 0; i < _exitBuffer.Count; i++)
            {
                Entity entity = _exitBuffer[i];
                region.Inside.Remove(entity);
                FireRegionEvent(state.Session, GameEvents.RegionExited, entity, definition.Id);
            }
        }

        private void FireRegionEvent(MapSession session, EventKey eventKey, Entity entity, string regionId)
        {
            ScriptContext context = _contextFactory();
            context.Set(CoreServiceKeys.MapId, session.MapId);
            context.Set(CoreServiceKeys.MapSession, session);
            context.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? new List<string>());
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, entity);
            context.Set(MapTriggerEventPayloadKeys.RegionId, regionId);
            _triggerManager.FireMapEvent(session.MapId, eventKey, context);
        }

        private readonly struct TrackedEntity
        {
            public readonly Entity Entity;
            public readonly Fix64Vec2 Position;
            public readonly GameplayTagContainer Tags;
            public readonly bool HasTags;

            public TrackedEntity(Entity entity, Fix64Vec2 position, GameplayTagContainer tags, bool hasTags)
            {
                Entity = entity;
                Position = position;
                Tags = tags;
                HasTags = hasTags;
            }
        }

        private sealed class RegionRuntime
        {
            public RegionRuntime(MapRegionDefinition definition, GameplayTagContainer tagFilter, bool hasTagFilter)
            {
                Definition = definition;
                TagFilter = tagFilter;
                HasTagFilter = hasTagFilter;
                Inside = new HashSet<Entity>();
            }

            public MapRegionDefinition Definition { get; }
            public GameplayTagContainer TagFilter { get; }
            public bool HasTagFilter { get; }
            public HashSet<Entity> Inside { get; }
        }

        private sealed class MapRegionState
        {
            public MapRegionState(MapSession session, List<RegionRuntime> regions)
            {
                Session = session;
                Regions = regions;
            }

            public MapSession Session { get; }
            public List<RegionRuntime> Regions { get; }
        }
    }
}
