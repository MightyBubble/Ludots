using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Per-map think-wave pump: every active map accumulates one tick per fixed step and,
    /// every HeartbeatIntervalTicks ticks, flushes a wave — firing map-scoped
    /// MapHeartbeat / EntitySpawned / EntityDied / EntityAliveCountChanged events with
    /// <see cref="MapTriggerEventPayloadKeys"/> payloads. Suspended maps do not accumulate.
    /// Spawns are observed as a membership diff over the map's MapEntity entities (Arch is
    /// compiled without EVENTS, so component-added subscriptions are inert here); deaths are
    /// observed through World.SubscribeEntityDestroyed, which fires while components are
    /// still readable so SourceTeamId is captured at destroy time.
    /// </summary>
    public sealed class MapHeartbeatClockSystem : ISystem<float>
    {
        public const int DefaultIntervalTicks = 30;
        public const int LifecycleQueueCapacity = 1024;
        private const int DrainChunkSize = 64;

        private readonly Func<MapSessionManager?> _sessions;
        private readonly World _world;
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly Dictionary<MapId, MapHeartbeatState> _states = new();
        private readonly QueuedLifecycleEvent[] _drainScratch = new QueuedLifecycleEvent[DrainChunkSize];
        private readonly List<MapId> _pruneScratch = new();
        private readonly List<KeyValuePair<MapId, MapSession>> _sessionScratch = new();

        private static readonly QueryDescription _mapEntityQuery =
            new QueryDescription().WithAll<MapEntity>();

        public MapHeartbeatClockSystem(
            Func<MapSessionManager?> sessions,
            World world,
            TriggerManager triggerManager,
            Func<ScriptContext> contextFactory)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _world.SubscribeEntityDestroyed(OnEntityDestroyed);
        }

        /// <summary>Total lifecycle events dropped before firing, across all maps.</summary>
        public int TotalDroppedLifecycleEvents
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<MapId, MapHeartbeatState> pair in _states)
                {
                    total += pair.Value.DroppedLifecycleEvents;
                }

                return total;
            }
        }

        public int GetDroppedLifecycleEvents(MapId mapId)
        {
            return _states.TryGetValue(mapId, out MapHeartbeatState state) ? state.DroppedLifecycleEvents : 0;
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            MapSessionManager? sessions = _sessions();
            if (sessions == null)
            {
                return;
            }

            PruneStatesForUnloadedMaps(sessions);

            // Wave events dispatch synchronously into handlers that may load or unload maps;
            // iterate a snapshot so the session map can be mutated mid-wave.
            _sessionScratch.Clear();
            foreach (KeyValuePair<MapId, MapSession> pair in sessions.All)
            {
                _sessionScratch.Add(pair);
            }

            for (int i = 0; i < _sessionScratch.Count; i++)
            {
                KeyValuePair<MapId, MapSession> pair = _sessionScratch[i];
                MapSession session = pair.Value;
                if (session.State != MapSessionState.Active)
                {
                    continue;
                }

                MapHeartbeatState state = GetOrCreateState(pair.Key);
                state.TicksAccumulated++;
                int interval = ResolveIntervalTicks(session);
                if (state.TicksAccumulated < interval)
                {
                    continue;
                }

                state.TicksAccumulated -= interval;
                FlushWave(session, state);
            }
        }

        public void AfterUpdate(in float dt) { }

        public void Dispose() { }

        private void OnEntityDestroyed(in Entity entity)
        {
            // Arch raises EntityDestroyed before components are stripped, so map ownership
            // and team can still be read here and stored for consumers of the dead reference.
            if (!_world.Has<MapEntity>(entity))
            {
                return;
            }

            MapEntity mapEntity = _world.Get<MapEntity>(entity);
            string mapIdValue = mapEntity.MapId.Value ?? string.Empty;
            if (mapIdValue.Length == 0)
            {
                return;
            }

            MapHeartbeatState state = GetOrCreateState(new MapId(mapIdValue));
            if (!state.Deaths.Enqueue(new QueuedLifecycleEvent(entity, ResolveTeamId(entity))))
            {
                state.DroppedLifecycleEvents++;
            }
        }

        private int ResolveTeamId(Entity entity)
        {
            return _world.Has<Team>(entity) ? _world.Get<Team>(entity).Id : 0;
        }

        private MapHeartbeatState GetOrCreateState(MapId mapId)
        {
            if (!_states.TryGetValue(mapId, out MapHeartbeatState state))
            {
                state = new MapHeartbeatState();
                _states[mapId] = state;
            }

            return state;
        }

        private void PruneStatesForUnloadedMaps(MapSessionManager sessions)
        {
            if (_states.Count == 0)
            {
                return;
            }

            _pruneScratch.Clear();
            foreach (KeyValuePair<MapId, MapHeartbeatState> pair in _states)
            {
                // An unloaded map's queued death events die with it: its map-scoped
                // triggers are already unregistered, so flushing would have no receiver.
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

        private static int ResolveIntervalTicks(MapSession session)
        {
            int interval = session.MapConfig?.HeartbeatIntervalTicks ?? DefaultIntervalTicks;
            if (interval < 1)
            {
                throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' HeartbeatIntervalTicks must be >= 1 (got {interval}).");
            }

            return interval;
        }

        private void FlushWave(MapSession session, MapHeartbeatState state)
        {
            state.HeartbeatIndex++;
            CollectCurrentMembers(session, state);
            FlushSpawnDiff(session, state);
            FlushLifecycleRing(session, state, state.Deaths, GameEvents.EntityDied);
            FlushAliveCounts(session, state);
            FireHeartbeat(session, state.HeartbeatIndex);
        }

        private void CollectCurrentMembers(MapSession session, MapHeartbeatState state)
        {
            state.CurrentMembers.Clear();
            state.CurrentAliveCounts.Clear();
            MapId mapId = session.MapId;
            _world.Query(in _mapEntityQuery, (Entity entity, ref MapEntity mapEntity) =>
            {
                if (mapEntity.MapId != mapId)
                {
                    return;
                }

                state.CurrentMembers.Add(entity);
                if (_world.Has<Team>(entity) && _world.Has<AttributeBuffer>(entity))
                {
                    int teamId = _world.Get<Team>(entity).Id;
                    state.CurrentAliveCounts.TryGetValue(teamId, out int count);
                    state.CurrentAliveCounts[teamId] = count + 1;
                }
            });
        }

        private void FlushSpawnDiff(MapSession session, MapHeartbeatState state)
        {
            state.SpawnScratch.Clear();
            foreach (Entity entity in state.CurrentMembers)
            {
                if (!state.PreviousMembers.Contains(entity))
                {
                    state.SpawnScratch.Add(entity);
                }
            }

            if (state.SpawnScratch.Count > LifecycleQueueCapacity)
            {
                state.DroppedLifecycleEvents += state.SpawnScratch.Count - LifecycleQueueCapacity;
                state.SpawnScratch.RemoveRange(LifecycleQueueCapacity, state.SpawnScratch.Count - LifecycleQueueCapacity);
            }

            for (int i = 0; i < state.SpawnScratch.Count; i++)
            {
                Entity spawned = state.SpawnScratch[i];
                int teamId = ResolveTeamId(spawned);
                FireSource(session, GameEvents.EntitySpawned, spawned, teamId);
            }

            HashSet<Entity> swapped = state.PreviousMembers;
            state.PreviousMembers = state.CurrentMembers;
            state.CurrentMembers = swapped;
        }

        private void FlushLifecycleRing(
            MapSession session,
            MapHeartbeatState state,
            LifecycleRing ring,
            EventKey eventKey)
        {
            int drained;
            while ((drained = ring.DequeueBatch(_drainScratch)) > 0)
            {
                for (int i = 0; i < drained; i++)
                {
                    QueuedLifecycleEvent queued = _drainScratch[i];
                    FireSource(session, eventKey, queued.Source, queued.TeamId);
                }
            }
        }

        private void FlushAliveCounts(MapSession session, MapHeartbeatState state)
        {
            if (!state.HasAliveBaseline)
            {
                state.LastAliveCounts.Clear();
                foreach (KeyValuePair<int, int> pair in state.CurrentAliveCounts)
                {
                    state.LastAliveCounts[pair.Key] = pair.Value;
                }

                state.HasAliveBaseline = true;
                return;
            }

            foreach (KeyValuePair<int, int> current in state.CurrentAliveCounts)
            {
                state.LastAliveCounts.TryGetValue(current.Key, out int last);
                if (current.Value == last)
                {
                    continue;
                }

                int teamId = current.Key;
                int count = current.Value;
                int delta = count - last;
                state.LastAliveCounts[current.Key] = count;
                FireAliveCountChanged(session, teamId, count, delta);
            }

            state.TeamPruneScratch.Clear();
            foreach (KeyValuePair<int, int> last in state.LastAliveCounts)
            {
                if (!state.CurrentAliveCounts.ContainsKey(last.Key))
                {
                    state.TeamPruneScratch.Add(last.Key);
                }
            }

            for (int i = 0; i < state.TeamPruneScratch.Count; i++)
            {
                int teamId = state.TeamPruneScratch[i];
                int last = state.LastAliveCounts[teamId];
                state.LastAliveCounts.Remove(teamId);
                if (last == 0)
                {
                    continue;
                }

                FireAliveCountChanged(session, teamId, 0, -last);
            }
        }

        private void FireHeartbeat(MapSession session, int heartbeatIndex)
        {
            ScriptContext context = CreateMapEventContext(session);
            context.Set(MapTriggerEventPayloadKeys.HeartbeatIndex, heartbeatIndex);
            _triggerManager.FireMapEvent(session.MapId, GameEvents.MapHeartbeat, context);
        }

        private void FireSource(MapSession session, EventKey eventKey, Entity source, int teamId)
        {
            ScriptContext context = CreateMapEventContext(session);
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, source);
            context.Set(MapTriggerEventPayloadKeys.SourceTeamId, teamId);
            _triggerManager.FireMapEvent(session.MapId, eventKey, context);
        }

        private void FireAliveCountChanged(MapSession session, int teamId, int count, int delta)
        {
            ScriptContext context = CreateMapEventContext(session);
            context.Set(MapTriggerEventPayloadKeys.SourceTeamId, teamId);
            context.Set(MapTriggerEventPayloadKeys.Count, count);
            context.Set(MapTriggerEventPayloadKeys.Delta, delta);
            _triggerManager.FireMapEvent(session.MapId, GameEvents.EntityAliveCountChanged, context);
        }

        private ScriptContext CreateMapEventContext(MapSession session)
        {
            ScriptContext context = _contextFactory();
            context.Set(CoreServiceKeys.MapId, session.MapId);
            context.Set(CoreServiceKeys.MapSession, session);
            context.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? new List<string>());
            return context;
        }

        private readonly struct QueuedLifecycleEvent
        {
            public QueuedLifecycleEvent(Entity source, int teamId)
            {
                Source = source;
                TeamId = teamId;
            }

            public Entity Source { get; }
            public int TeamId { get; }
        }

        private sealed class MapHeartbeatState
        {
            public int TicksAccumulated;
            public int HeartbeatIndex;
            public int DroppedLifecycleEvents;
            public bool HasAliveBaseline;
            public readonly LifecycleRing Deaths = new();
            public HashSet<Entity> CurrentMembers = new();
            public HashSet<Entity> PreviousMembers = new();
            public readonly List<Entity> SpawnScratch = new();
            public readonly Dictionary<int, int> LastAliveCounts = new();
            public readonly Dictionary<int, int> CurrentAliveCounts = new();
            public readonly List<int> TeamPruneScratch = new();
        }

        private sealed class LifecycleRing
        {
            private readonly QueuedLifecycleEvent[] _items = new QueuedLifecycleEvent[LifecycleQueueCapacity];
            private int _head;
            private int _count;

            public bool Enqueue(QueuedLifecycleEvent item)
            {
                if (_count >= _items.Length)
                {
                    return false;
                }

                _items[(_head + _count) % _items.Length] = item;
                _count++;
                return true;
            }

            public int DequeueBatch(QueuedLifecycleEvent[] scratch)
            {
                int take = Math.Min(_count, scratch.Length);
                for (int i = 0; i < take; i++)
                {
                    scratch[i] = _items[(_head + i) % _items.Length];
                }

                _head = (_head + take) % _items.Length;
                _count -= take;
                return take;
            }
        }
    }
}
