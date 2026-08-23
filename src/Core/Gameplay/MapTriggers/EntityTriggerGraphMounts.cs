using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Entity-domain TriggerGraph mount pipeline. Entities whose template declares
    /// "TriggerGraphs" get one mount per graph (scope = the entity itself; caster =
    /// explicit target = E[0] convention = self), built by TriggerGraphMounting and
    /// registered through the map trigger pipeline of the entity's map
    /// (TriggerManager map registration, decorators, unload cleanup).
    ///
    /// Lifecycle contract (entity mounts align with the entity, not with the wave):
    /// - Spawn: entries with event "EntitySpawned" execute immediately at mount
    ///   creation (mount-local dispatch through the same slice executor; the
    ///   TriggerManager bus is NOT fired, so map-domain observers keep think-wave
    ///   granularity for spawns).
    /// - Destroy: entries with event "EntityDied" execute on the destroy tick for
    ///   that entity's own mounts, routed from one global World.SubscribeEntityDestroyed
    ///   handler (Arch raises it while components are still readable, so team and map
    ///   ownership are captured at destroy time). Map-domain EntityDied observers
    ///   keep wave granularity.
    /// - After death the entity's mounts are inert (TriggerGraphMountTrigger
    ///   CheckConditions false on dead scope) and swept lazily at think waves with a
    ///   bounded budget; entity mounts with any other event key dispatch through the
    ///   map bus registration normally. Entity payloads are scope-filtered: an
    ///   unmarked scope accepts only its own source/target, while a scope carrying
    ///   EntityTriggerGraphAggregateRoot accepts attached descendants too.
    /// - Map unload drops the map's entity mounts before entity teardown, so
    ///   unload-time destruction produces no death dispatches.
    /// </summary>
    public sealed class EntityTriggerGraphMounts
    {
        public const int SweepBudgetPerWave = 64;

        private readonly World _world;
        private readonly Func<MapSessionManager?> _sessions;
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly Func<TriggerDecoratorRegistry?> _decorators;
        private readonly Func<GraphProgramRegistry?> _programs;
        private readonly Dictionary<MapId, List<EntityMountSet>> _mapMounts = new();
        private readonly List<MapLoadSpawn> _mapLoadBuffer = new();
        private readonly List<EntityMountSet> _sweepScratch = new();

        public EntityTriggerGraphMounts(
            World world,
            Func<MapSessionManager?> sessions,
            TriggerManager triggerManager,
            Func<ScriptContext> contextFactory,
            Func<TriggerDecoratorRegistry?> decorators,
            Func<GraphProgramRegistry?> programs)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _decorators = decorators ?? throw new ArgumentNullException(nameof(decorators));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _world.SubscribeEntityDestroyed(OnEntityDestroyed);
            _triggerManager.RegisterEventHandler(GameEvents.MapHeartbeat, OnMapHeartbeat);
        }

        /// <summary>Mounts created for still-unswept dead entities; test observability.</summary>
        public int GetDeadMountCount(MapId mapId)
        {
            return _mapMounts.TryGetValue(mapId, out List<EntityMountSet> sets)
                ? CountDead(sets)
                : 0;
        }

        /// <summary>
        /// Buffers a map-load spawn (entity + its template's declared graph names).
        /// MapLoader reports both spawn lanes; the engine flushes the buffer into
        /// mount triggers inside map instantiation, before the map's triggers are
        /// registered, so one RegisterMapTriggers call covers map- and entity-domain
        /// mounts alike.
        /// </summary>
        public void BufferMapLoadSpawn(Entity entity, string templateId, IReadOnlyList<string> graphNames)
        {
            if (entity == Entity.Null || entity == default)
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' declares TriggerGraphs but map-load buffering received a null entity.");
            }

            _mapLoadBuffer.Add(new MapLoadSpawn(entity, templateId, graphNames));
        }

        /// <summary>
        /// Builds mounts for every buffered map-load spawn (spawn entries dispatch
        /// immediately) and returns all triggers for the caller's registration.
        /// </summary>
        public List<Trigger> FlushMapLoadMounts(MapSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var triggers = new List<Trigger>();
            if (_mapLoadBuffer.Count == 0)
            {
                return triggers;
            }

            try
            {
                for (int i = 0; i < _mapLoadBuffer.Count; i++)
                {
                    MapLoadSpawn spawn = _mapLoadBuffer[i];
                    List<Trigger> spawnTriggers = BuildEntityGraphList(
                        spawn.Entity,
                        spawn.GraphNames,
                        $"entity template '{spawn.TemplateId}'");
                    DecorateTrackAndDispatch(session, spawn.Entity, spawnTriggers);
                    triggers.AddRange(spawnTriggers);
                }
            }
            finally
            {
                _mapLoadBuffer.Clear();
            }

            return triggers;
        }

        /// <summary>
        /// Mounts a runtime-spawned entity (spawn queue or lifecycle materialization):
        /// resolves the entity's map from its MapEntity component, builds the mounts,
        /// dispatches spawn entries, decorates, and appends to the map's registered
        /// trigger list.
        /// </summary>
        public void MountRuntimeSpawned(Entity entity, string templateId, IReadOnlyList<string> graphNames)
        {
            if (graphNames == null || graphNames.Count == 0)
            {
                return;
            }

            MapId mapId = ResolveRequiredMapId(entity, templateId);
            MapSession? session = _sessions()?.GetSession(mapId);
            if (session == null || session.State != MapSessionState.Active)
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' declares TriggerGraphs but the entity's map '{mapId.Value}' has no active session; entity-domain mounts register through their map.");
            }

            List<Trigger> triggers = BuildEntityGraphList(entity, graphNames, $"entity template '{templateId}'");
            DecorateTrackAndDispatch(session, entity, triggers);
            _triggerManager.AddMapTriggers(mapId, triggers);
        }

        /// <summary>
        /// Builds one entity-domain mount: triggers (decorated here — the engine's
        /// post-instantiation decorator pass skips entity-domain mounts), immediate
        /// EntitySpawned dispatch, and registry tracking. Registration stays with the
        /// caller (map load registers in bulk; runtime spawns append).
        /// </summary>
        public List<Trigger> MountEntityGraphs(MapSession session, Entity scope, string graph, string ownerLabel)
        {
            if (!_world.IsAlive(scope))
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} cannot mount TriggerGraph '{graph}' on a dead scope entity.");
            }

            List<Trigger> triggers = BuildEntityGraphList(scope, new[] { graph }, ownerLabel);
            DecorateTrackAndDispatch(session, scope, triggers);
            return triggers;
        }

        private void DecorateTrackAndDispatch(MapSession session, Entity scope, List<Trigger> triggers)
        {
            Track(session.MapId, scope, triggers);
            TriggerDecoratorRegistry? decorators = _decorators();
            for (int i = 0; i < triggers.Count; i++)
            {
                decorators?.Apply(triggers[i]);
            }

            DispatchLifecycle(session, scope, GameEvents.EntitySpawned, triggers);
        }

        private List<Trigger> BuildEntityGraphList(Entity scope, IReadOnlyList<string> graphNames, string ownerLabel)
        {
            GraphProgramRegistry programs = _programs()
                ?? throw new InvalidOperationException($"{ownerLabel} requires GraphProgramRegistry to mount TriggerGraphs.");
            var triggers = new List<Trigger>(graphNames.Count);
            for (int g = 0; g < graphNames.Count; g++)
            {
                // Build every graph before tracking or dispatching any lifecycle entry.
                // A missing graph therefore cannot leave a partially mounted entity.
                triggers.AddRange(TriggerGraphMounting.BuildEntityMountTriggers(
                    programs,
                    scope,
                    graphNames[g],
                    ownerLabel));
            }

            return triggers;
        }

        /// <summary>Drops all entity-mount state for a map; called before entity teardown on unload.</summary>
        public void DropMap(MapId mapId)
        {
            _mapMounts.Remove(mapId);
        }

        private void Track(MapId mapId, Entity scope, List<Trigger> triggers)
        {
            if (!_mapMounts.TryGetValue(mapId, out List<EntityMountSet> sets))
            {
                sets = new List<EntityMountSet>();
                _mapMounts[mapId] = sets;
            }

            sets.Add(new EntityMountSet(scope, triggers));
        }

        private void OnEntityDestroyed(in Entity entity)
        {
            // Arch raises EntityDestroyed before components are stripped; map ownership
            // and team are still readable here and captured for the dispatch payload.
            if (!_world.Has<MapEntity>(entity))
            {
                return;
            }

            MapId mapId = _world.Get<MapEntity>(entity).MapId;
            if (!_mapMounts.TryGetValue(mapId, out List<EntityMountSet> sets))
            {
                return;
            }

            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i].Dead || sets[i].Scope != entity)
                {
                    continue;
                }

                MapSession? session = _sessions()?.GetSession(mapId);
                if (session != null && session.State == MapSessionState.Active)
                {
                    DispatchLifecycle(session, entity, GameEvents.EntityDied, sets[i].Triggers, destroyTick: true);
                }

                sets[i].Dead = true;
            }
        }

        private Task OnMapHeartbeat(ScriptContext context)
        {
            MapId mapId = context.Get<MapId>(CoreServiceKeys.MapId);
            if (mapId.Value != null && _mapMounts.TryGetValue(mapId, out List<EntityMountSet> sets))
            {
                SweepDeadMounts(mapId, sets);
            }

            return Task.CompletedTask;
        }

        private void SweepDeadMounts(MapId mapId, List<EntityMountSet> sets)
        {
            _sweepScratch.Clear();
            for (int i = 0; i < sets.Count && _sweepScratch.Count < SweepBudgetPerWave; i++)
            {
                if (sets[i].Dead)
                {
                    _sweepScratch.Add(sets[i]);
                }
            }

            for (int i = 0; i < _sweepScratch.Count; i++)
            {
                sets.Remove(_sweepScratch[i]);
                _triggerManager.RemoveMapTriggers(mapId, _sweepScratch[i].Triggers);
            }

            if (sets.Count == 0)
            {
                _mapMounts.Remove(mapId);
            }
        }

        private void DispatchLifecycle(
            MapSession session,
            Entity scope,
            EventKey eventKey,
            List<Trigger> triggers,
            bool destroyTick = false)
        {
            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i] is not TriggerGraphMountTrigger mount || mount.EventKey != eventKey)
                {
                    continue;
                }

                ScriptContext context = _contextFactory();
                context.Set(CoreServiceKeys.MapId, session.MapId);
                context.Set(CoreServiceKeys.MapSession, session);
                context.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? new List<string>());
                context.Set(MapTriggerEventPayloadKeys.SourceEntity, scope);
                context.Set(MapTriggerEventPayloadKeys.SourceTeamId, ResolveTeamId(scope));
                _ = mount.ExecuteLifecycleDispatch(context);
            }
        }

        private int ResolveTeamId(Entity entity)
        {
            // The destroy-tick handler runs while components are still readable; every
            // other dispatch site sees a live entity.
            return _world.Has<Team>(entity) ? _world.Get<Team>(entity).Id : 0;
        }

        private MapId ResolveRequiredMapId(Entity entity, string templateId)
        {
            if (entity == Entity.Null || entity == default || !_world.Has<MapEntity>(entity))
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' declares TriggerGraphs but the spawned entity has no MapEntity component; entity-domain mounts require map ownership.");
            }

            return _world.Get<MapEntity>(entity).MapId;
        }

        private static int CountDead(List<EntityMountSet> sets)
        {
            int dead = 0;
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i].Dead)
                {
                    dead++;
                }
            }

            return dead;
        }

        private readonly struct MapLoadSpawn
        {
            public MapLoadSpawn(Entity entity, string templateId, IReadOnlyList<string> graphNames)
            {
                Entity = entity;
                TemplateId = templateId;
                GraphNames = graphNames;
            }

            public Entity Entity { get; }
            public string TemplateId { get; }
            public IReadOnlyList<string> GraphNames { get; }
        }

        private sealed class EntityMountSet
        {
            public EntityMountSet(Entity scope, List<Trigger> triggers)
            {
                Scope = scope;
                Triggers = triggers;
            }

            public Entity Scope { get; }
            public List<Trigger> Triggers { get; }
            public bool Dead { get; set; }
        }
    }
}
