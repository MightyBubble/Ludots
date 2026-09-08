using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Evaluates region volume entities at think-wave granularity and fires the
    /// volume's enter/exit events (engine <see cref="GameEvents.RegionEntered"/>/
    /// <see cref="GameEvents.RegionExited"/> by default, an authored emission
    /// contract when the volume carries <see cref="RegionVolumeEmissionCm"/>).
    ///
    /// Semantics carried over from the retired dictionary-based region system:
    /// - Cadence: the map heartbeat fires this system's handler for that map; there
    ///   is no second tick accumulator and suspended maps never evaluate.
    /// - Inside-sets survive suspend/resume (suspended entities cannot move), so no
    ///   spurious exit/enter pair fires.
    /// - Eligible mover: MapEntity + WorldPositionCm, not SuspendedTag, not
    ///   PresentationDestroyPending, and — when the volume declares a tag filter —
    ///   carrying at least one of the declared GameplayTags (any-of).
    /// - Dead movers leave the inside-set silently: death is not a crossing.
    /// - Boundary positions count as inside; a mover that stops matching the tag
    ///   filter (or loses its position/map components) counts as an exit.
    /// - Volume entities are evaluated wherever they stand: runtime-spawned volumes
    ///   join on the next wave, and a volume whose entity dies stops evaluating —
    ///   its alive occupants receive the volume's exit event on the next update of
    ///   a still-active map (a map being torn down drops its orphans silently).
    /// </summary>
    public sealed class RegionVolumeTriggerSystem : BaseSystem<World, float>
    {
        private readonly Func<MapSessionManager?> _sessions;
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly Dictionary<Entity, VolumeRuntimeState> _states = new Dictionary<Entity, VolumeRuntimeState>();
        private readonly List<VolumeRuntimeState> _orphanScratch = new List<VolumeRuntimeState>();
        private readonly QueryDescription _volumeQuery = new QueryDescription()
            .WithAll<MapEntity, RegionVolumeCm, WorldPositionCm>();
        private readonly QueryDescription _trackedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm>()
            .WithNone<SuspendedTag, PresentationDestroyPending, RegionVolumeCm>();
        private readonly List<TrackedEntity> _trackedBuffer = new List<TrackedEntity>();
        private readonly HashSet<Entity> _matchedBuffer = new HashSet<Entity>();
        private readonly List<Entity> _exitBuffer = new List<Entity>();
        private readonly List<Entity> _silentRemovalBuffer = new List<Entity>();
        private static readonly List<string> EmptyTags = new List<string>();

        public RegionVolumeTriggerSystem(
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
            _triggerManager.RegisterEventHandler(GameEvents.MapHeartbeat, OnMapHeartbeat);
            World.SubscribeEntityDestroyed(OnVolumeEntityDestroyed);
        }

        public override void Update(in float t)
        {
            FlushOrphanedVolumes();
        }

        private Task OnMapHeartbeat(ScriptContext context)
        {
            MapId mapId = context.Get<MapId>(CoreServiceKeys.MapId);
            MapSessionManager? sessions = _sessions();
            MapSession? session = sessions?.GetSession(mapId);
            if (session == null || session.State != MapSessionState.Active)
            {
                return Task.CompletedTask;
            }

            SyncVolumeStates(session);
            CollectTrackedEntities(mapId);

            foreach (KeyValuePair<Entity, VolumeRuntimeState> pair in _states)
            {
                if (pair.Value.MapId == mapId && !pair.Value.Orphaned)
                {
                    EvaluateVolume(session, pair.Value);
                }
            }

            return Task.CompletedTask;
        }

        private void SyncVolumeStates(MapSession session)
        {
            foreach (ref var chunk in World.Query(in _volumeQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var mapEntities = chunk.GetSpan<MapEntity>();
                var volumes = chunk.GetSpan<RegionVolumeCm>();
                var positions = chunk.GetSpan<WorldPositionCm>();

                foreach (var index in chunk)
                {
                    if (mapEntities[index].MapId != session.MapId)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    RegionVolumeCm volume = volumes[index];
                    if (!_states.TryGetValue(entity, out VolumeRuntimeState? state))
                    {
                        state = new VolumeRuntimeState(session.MapId);
                        _states[entity] = state;
                    }

                    state.Refresh(World, entity, volume, positions[index].Value, _triggerManager.EventSchemas);
                }
            }
        }

        private void CollectTrackedEntities(MapId mapId)
        {
            _trackedBuffer.Clear();
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

        private void EvaluateVolume(MapSession session, VolumeRuntimeState volume)
        {
            _matchedBuffer.Clear();
            for (int i = 0; i < _trackedBuffer.Count; i++)
            {
                TrackedEntity tracked = _trackedBuffer[i];
                if (volume.HasTagFilter && (!tracked.HasTags || !tracked.Tags.Intersects(volume.TagFilter)))
                {
                    continue;
                }

                if (!volume.Shape.Contains(tracked.Position, volume.Anchor))
                {
                    continue;
                }

                _matchedBuffer.Add(tracked.Entity);
                if (volume.Inside.Add(tracked.Entity))
                {
                    FireVolumeEvent(session, volume, entering: true, tracked.Entity);
                }
            }

            _exitBuffer.Clear();
            _silentRemovalBuffer.Clear();
            foreach (Entity entity in volume.Inside)
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
                volume.Inside.Remove(_silentRemovalBuffer[i]);
            }

            for (int i = 0; i < _exitBuffer.Count; i++)
            {
                Entity entity = _exitBuffer[i];
                volume.Inside.Remove(entity);
                FireVolumeEvent(session, volume, entering: false, entity);
            }
        }

        private void OnVolumeEntityDestroyed(in Entity entity)
        {
            if (_states.TryGetValue(entity, out VolumeRuntimeState? state))
            {
                state.Orphaned = true;
            }
        }

        private void FlushOrphanedVolumes()
        {
            if (_states.Count == 0)
            {
                return;
            }

            _orphanScratch.Clear();
            foreach (KeyValuePair<Entity, VolumeRuntimeState> pair in _states)
            {
                if (pair.Value.Orphaned)
                {
                    _orphanScratch.Add(pair.Value);
                }
            }

            for (int i = 0; i < _orphanScratch.Count; i++)
            {
                VolumeRuntimeState volume = _orphanScratch[i];
                MapSession? session = _sessions()?.GetSession(volume.MapId);
                if (session == null || session.State != MapSessionState.Active)
                {
                    _states.Remove(volume.VolumeEntity);
                    continue;
                }

                foreach (Entity entity in volume.Inside)
                {
                    if (World.IsAlive(entity) && !World.Has<SuspendedTag>(entity))
                    {
                        FireVolumeEvent(session, volume, entering: false, entity);
                    }
                }

                volume.Inside.Clear();
                _states.Remove(volume.VolumeEntity);
            }
        }

        private void FireVolumeEvent(MapSession session, VolumeRuntimeState volume, bool entering, Entity entity)
        {
            EventKey eventKey = entering ? volume.EnterEvent : volume.ExitEvent;
            EventSchema? schema = entering ? volume.EnterSchema : volume.ExitSchema;
            ScriptContext context = _contextFactory();
            context.Set(CoreServiceKeys.MapId, session.MapId);
            context.Set(CoreServiceKeys.MapSession, session);
            context.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? EmptyTags);
            // The crossing entity is transport metadata (legal on any fire, read through
            // the MapTrigger.SourceEntity key); the volume key rides only on the engine
            // region events whose schemas declare it — custom schemas cannot declare
            // MapTrigger.* params, and volume identity travels in the authored payload.
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, entity);
            if (schema == null || schema.DeclaresPayloadKey(MapTriggerEventPayloadKeys.RegionId))
            {
                context.Set(MapTriggerEventPayloadKeys.RegionId, volume.VolumeKey);
            }

            if (volume.Payload != null)
            {
                for (int i = 0; i < volume.Payload.Length; i++)
                {
                    RegionVolumePayloadEntry entry = volume.Payload[i];
                    switch (entry.Type)
                    {
                        case RegionVolumePayloadValueType.Int:
                            context.Set(entry.Key, entry.IntValue);
                            break;
                        case RegionVolumePayloadValueType.Float:
                            context.Set(entry.Key, entry.FloatValue);
                            break;
                        case RegionVolumePayloadValueType.String:
                            context.Set(entry.Key, entry.StringValue ?? string.Empty);
                            break;
                    }
                }
            }

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

        private sealed class VolumeRuntimeState
        {
            public VolumeRuntimeState(MapId mapId)
            {
                MapId = mapId;
                EnterEvent = GameEvents.RegionEntered;
                ExitEvent = GameEvents.RegionExited;
            }

            public MapId MapId { get; }
            public Entity VolumeEntity { get; set; }
            public string VolumeKey { get; set; } = string.Empty;
            public RegionVolumeShape Shape { get; set; }
            public Fix64Vec2 Anchor { get; set; }
            public GameplayTagContainer TagFilter { get; set; }
            public bool HasTagFilter { get; set; }
            public EventKey EnterEvent { get; set; }
            public EventKey ExitEvent { get; set; }
            public EventSchema? EnterSchema { get; set; }
            public EventSchema? ExitSchema { get; set; }
            public RegionVolumePayloadEntry[]? Payload { get; set; }
            public bool Orphaned { get; set; }
            public HashSet<Entity> Inside { get; } = new HashSet<Entity>();

            public void Refresh(
                World world,
                Entity entity,
                RegionVolumeCm volume,
                Fix64Vec2 anchor,
                EventSchemaRegistry? schemas)
            {
                VolumeEntity = entity;
                VolumeKey = volume.VolumeKey;
                Shape = volume.Shape;
                Anchor = anchor;
                HasTagFilter = world.TryGet(entity, out RegionVolumeTagFilterCm filter);
                if (HasTagFilter)
                {
                    TagFilter = filter.Filter;
                }

                if (world.TryGet(entity, out RegionVolumeEmissionCm emission))
                {
                    EnterEvent = emission.EnterEvent;
                    ExitEvent = emission.ExitEvent;
                    Payload = emission.Payload;
                }
                else
                {
                    EnterEvent = GameEvents.RegionEntered;
                    ExitEvent = GameEvents.RegionExited;
                    Payload = null;
                }

                EnterSchema = schemas != null && schemas.TryGet(EnterEvent.Value, out EventSchema enterSchema) ? enterSchema : null;
                ExitSchema = schemas != null && schemas.TryGet(ExitEvent.Value, out EventSchema exitSchema) ? exitSchema : null;
                Orphaned = false;
            }
        }
    }
}
