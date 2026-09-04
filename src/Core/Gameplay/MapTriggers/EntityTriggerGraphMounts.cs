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
        private readonly Func<CustomEventNameRegistry?> _customEvents;
        private readonly List<MapLoadSpawn> _mapLoadBuffer = new();
        private readonly List<KeyValuePair<TriggerMountOwner, List<Trigger>>> _ownedScratch = new();
        private readonly List<TriggerMountOwner> _reclaimScratch = new();

        public EntityTriggerGraphMounts(
            World world,
            Func<MapSessionManager?> sessions,
            TriggerManager triggerManager,
            Func<ScriptContext> contextFactory,
            Func<TriggerDecoratorRegistry?> decorators,
            Func<GraphProgramRegistry?> programs,
            Func<CustomEventNameRegistry?> customEvents)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _decorators = decorators ?? throw new ArgumentNullException(nameof(decorators));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _customEvents = customEvents ?? throw new ArgumentNullException(nameof(customEvents));
            _world.SubscribeEntityDestroyed(OnEntityDestroyed);
            _triggerManager.RegisterEventHandler(GameEvents.MapHeartbeat, OnMapHeartbeat);
        }

        /// <summary>
        /// Mount owners whose subject is dead but whose mounts are not yet reclaimed;
        /// test observability. Counts template and context mounts alike — both follow the
        /// same budgeted heartbeat sweep (#1398 D11).
        /// </summary>
        public int GetDeadMountCount(MapId mapId)
        {
            _ownedScratch.Clear();
            _triggerManager.CollectOwnedMounts(mapId, _ownedScratch);
            int dead = 0;
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                if (!_world.IsAlive(_ownedScratch[i].Key.Subject))
                {
                    dead++;
                }
            }

            return dead;
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
                        session,
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

            List<Trigger> triggers = BuildEntityGraphList(session, entity, graphNames, $"entity template '{templateId}'");
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

            List<Trigger> triggers = BuildEntityGraphList(session, scope, new[] { graph }, ownerLabel);
            DecorateTrackAndDispatch(session, scope, triggers);
            return triggers;
        }

        private void DecorateTrackAndDispatch(MapSession session, Entity scope, List<Trigger> triggers)
        {
            TriggerMountOwner owner = new(TriggerMountOwnerKind.TemplateEntity, scope, 0);
            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i] is TriggerGraphMountTrigger mount)
                {
                    mount.Owner = owner;
                }
            }

            TriggerDecoratorRegistry? decorators = _decorators();
            for (int i = 0; i < triggers.Count; i++)
            {
                decorators?.Apply(triggers[i]);
            }

            DispatchLifecycle(session, scope, GameEvents.EntitySpawned, triggers);
        }

        private List<Trigger> BuildEntityGraphList(MapSession session, Entity scope, IReadOnlyList<string> graphNames, string ownerLabel)
        {
            GraphProgramRegistry programs = _programs()
                ?? throw new InvalidOperationException($"{ownerLabel} requires GraphProgramRegistry to mount TriggerGraphs.");
            CustomEventNameRegistry customEvents = _customEvents()
                ?? throw new InvalidOperationException($"{ownerLabel} requires CustomEventNameRegistry to validate TriggerGraphs.");
            var triggers = new List<Trigger>(graphNames.Count);
            for (int g = 0; g < graphNames.Count; g++)
            {
                // Build every graph before tracking or dispatching any lifecycle entry.
                // A missing graph therefore cannot leave a partially mounted entity.
                triggers.AddRange(TriggerGraphMounting.BuildEntityMountTriggers(
                    programs,
                    scope,
                    graphNames[g],
                    ownerLabel,
                    customEvents,
                    session.EntityIndex,
                    _triggerManager.EventSchemas,
                    TriggerGraphMounting.CollectRegionIds(session)));
            }

            return triggers;
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
            MapSession? session = _sessions()?.GetSession(mapId);
            if (session == null || session.State != MapSessionState.Active)
            {
                return;
            }

            _ownedScratch.Clear();
            _triggerManager.CollectOwnedMounts(entity, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                if (_ownedScratch[i].Key.Kind == TriggerMountOwnerKind.TemplateEntity)
                {
                    DispatchLifecycle(session, entity, GameEvents.EntityDied, _ownedScratch[i].Value, destroyTick: true);
                }
            }

            // Reclamation is not done here: the heartbeat sweep below reclaims dead-subject
            // mounts of both kinds with a bounded budget (#1398 D11).
        }

        private Task OnMapHeartbeat(ScriptContext context)
        {
            MapId mapId = context.Get<MapId>(CoreServiceKeys.MapId);
            if (mapId.Value != null)
            {
                SweepDeadSubjectMounts(mapId);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Reclaim owned mounts whose subject died — template and context mounts alike,
        /// bounded by <see cref="SweepBudgetPerWave"/> per heartbeat wave (#1398 D11:
        /// one reclamation policy for every entity-domain mount kind).
        /// </summary>
        private void SweepDeadSubjectMounts(MapId mapId)
        {
            _ownedScratch.Clear();
            _triggerManager.CollectOwnedMounts(mapId, _ownedScratch);
            _reclaimScratch.Clear();
            for (int i = 0; i < _ownedScratch.Count && _reclaimScratch.Count < SweepBudgetPerWave; i++)
            {
                if (!_world.IsAlive(_ownedScratch[i].Key.Subject))
                {
                    _reclaimScratch.Add(_ownedScratch[i].Key);
                }
            }

            for (int i = 0; i < _reclaimScratch.Count; i++)
            {
                _triggerManager.RemoveOwnedMounts(_reclaimScratch[i]);
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
                _triggerManager.EventSchemas?.ValidateFirePayload(eventKey, context);
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
    }
}
