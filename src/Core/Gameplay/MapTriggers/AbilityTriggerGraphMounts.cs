using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Ability-domain TriggerGraph mount pipeline. An ability definition declares
    /// "TriggerGraphs"; each cast of that ability mounts one set (scope = the caster)
    /// and the set lives exactly as long as the cast instance:
    ///
    /// - Cast start (GasPresentationEventKind.CastStarted): mount created from the
    ///   ability definition's graphs, entries with "Ability.CastStarted" dispatch
    ///   immediately at mount creation (mount-local dispatch, like EntitySpawned);
    ///   every other entry key registers on the caster's map trigger bus
    ///   (Ability.CastCommitted, Effect.*, Gas.Event.*, MapHeartbeat resumes...).
    /// - Terminal moment (CastFinished / CastInterrupted): the matching terminal
    ///   entry dispatches mount-locally, then the set unregisters and is untracked.
    ///   CastFailed never creates a mount (the cast never started).
    /// - A caster that dies mid-cast leaves inert mounts (TriggerGraphMountTrigger
    ///   CheckConditions false on dead scope) that are swept lazily by the mount
    ///   system each tick; map unload drops the map's sets before entity teardown.
    ///
    /// One set per cast instance, keyed by (caster, ability slot, ability id), so
    /// concurrent casts of the same ability keep independent graph state. The set
    /// registers through the caster's map (TriggerManager map registration), so
    /// ability-domain graphs see the same map bus as the map/entity domains.
    /// </summary>
    public sealed class AbilityTriggerGraphMounts
    {
        private readonly World _world;
        private readonly Func<MapSessionManager?> _sessions;
        private readonly TriggerManager _triggerManager;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly Func<TriggerDecoratorRegistry?> _decorators;
        private readonly Func<GraphProgramRegistry?> _programs;
        private readonly Dictionary<CastKey, AbilityMountSet> _mounts = new();
        private readonly List<CastKey> _sweepScratch = new();

        /// <summary>CastStarted events whose caster has no active map session (no mount created).</summary>
        public int DroppedNoMapEvents { get; private set; }

        /// <summary>CastStarted events for an ability id with no registered definition (no mount created).</summary>
        public int DroppedUnknownAbilityEvents { get; private set; }

        public AbilityTriggerGraphMounts(
            World world,
            Func<MapSessionManager?> sessions,
            TriggerManager triggerManager,
            AbilityDefinitionRegistry abilityDefinitions,
            Func<ScriptContext> contextFactory,
            Func<TriggerDecoratorRegistry?> decorators,
            Func<GraphProgramRegistry?> programs)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _abilityDefinitions = abilityDefinitions ?? throw new ArgumentNullException(nameof(abilityDefinitions));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _decorators = decorators ?? throw new ArgumentNullException(nameof(decorators));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        }

        /// <summary>Live ability mounts (scope alive); test observability.</summary>
        public int GetLiveMountCount()
        {
            int live = 0;
            foreach (var kvp in _mounts)
            {
                if (kvp.Value.Scope != Entity.Null && kvp.Value.Scope != default && _world.IsAlive(kvp.Value.Scope))
                {
                    live++;
                }
            }

            return live;
        }

        /// <summary>Inert ability mounts whose caster died; test observability.</summary>
        public int GetDeadMountCount()
        {
            int dead = 0;
            foreach (var kvp in _mounts)
            {
                if (kvp.Value.Scope == Entity.Null || kvp.Value.Scope == default || !_world.IsAlive(kvp.Value.Scope))
                {
                    dead++;
                }
            }

            return dead;
        }

        /// <summary>
        /// Processes this tick's GAS presentation moments (called by the mount system
        /// right after AbilityExecSystem wrote the buffer): creates ability mounts on
        /// CastStarted, tears them down on terminal moments, and sweeps inert dead-caster
        /// mounts. Read-only for every other event kind.
        /// </summary>
        public void ProcessPresentationEvents(ReadOnlySpan<GasPresentationEvent> events)
        {
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly GasPresentationEvent evt = ref events[i];
                switch (evt.Kind)
                {
                    case GasPresentationEventKind.CastStarted:
                        CreateMountForCast(in evt);
                        break;
                    case GasPresentationEventKind.CastFinished:
                        TearDownMount(in evt, GameEvents.AbilityCastFinished);
                        break;
                    case GasPresentationEventKind.CastInterrupted:
                        TearDownMount(in evt, GameEvents.AbilityCastInterrupted);
                        break;
                    default:
                        break;
                }
            }

            SweepDeadScopes();
        }

        /// <summary>Drops all ability-mount state for a map; called before entity teardown on unload.</summary>
        public void DropMap(MapId mapId)
        {
            _sweepScratch.Clear();
            foreach (var kvp in _mounts)
            {
                if (kvp.Value.MapId == mapId)
                {
                    _sweepScratch.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _sweepScratch.Count; i++)
            {
                _mounts.Remove(_sweepScratch[i]);
            }

            _sweepScratch.Clear();
        }

        private void CreateMountForCast(in GasPresentationEvent evt)
        {
            if (!_abilityDefinitions.TryGet(evt.AbilityId, out AbilityDefinition definition) ||
                !definition.HasTriggerGraphs ||
                definition.TriggerGraphs == null ||
                definition.TriggerGraphs.Count == 0)
            {
                if (!_abilityDefinitions.TryGet(evt.AbilityId, out _))
                {
                    DroppedUnknownAbilityEvents++;
                }

                return;
            }

            if (evt.Actor == Entity.Null || evt.Actor == default || !_world.IsAlive(evt.Actor))
            {
                return;
            }

            MapSession? session = ResolveActiveSession(evt.Actor);
            if (session == null)
            {
                DroppedNoMapEvents++;
                return;
            }

            GraphProgramRegistry? programs = _programs()
                ?? throw new InvalidOperationException(
                    $"Ability '{evt.AbilityId}' declares TriggerGraphs but GraphProgramRegistry is not available.");

            var allTriggers = new List<Trigger>();
            var busTriggers = new List<Trigger>();
            for (int g = 0; g < definition.TriggerGraphs.Count; g++)
            {
                List<Trigger> graphTriggers = TriggerGraphMounting.BuildAbilityMountTriggers(
                    programs,
                    evt.Actor,
                    definition.TriggerGraphs[g],
                    $"ability '{evt.AbilityId}'");
                TriggerDecoratorRegistry? decorators = _decorators();
                for (int i = 0; i < graphTriggers.Count; i++)
                {
                    decorators?.Apply(graphTriggers[i]);
                    allTriggers.Add(graphTriggers[i]);
                    if (!IsAbilityLifecycleTrigger(graphTriggers[i]))
                    {
                        busTriggers.Add(graphTriggers[i]);
                    }
                }
            }

            var key = new CastKey(evt.Actor, evt.AbilitySlot, evt.AbilityId);
            if (_mounts.TryGetValue(key, out AbilityMountSet stale))
            {
                // Same cast instance (re)started without a terminal moment in between:
                // drop the stale set first so its bus registrations cannot leak.
                if (stale.BusTriggers.Count > 0)
                {
                    _triggerManager.RemoveMapTriggers(stale.MapId, stale.BusTriggers);
                }

                _mounts.Remove(key);
            }

            _mounts[key] = new AbilityMountSet(evt.Actor, evt.AbilityId, session.MapId, allTriggers, busTriggers);

            if (busTriggers.Count > 0)
            {
                _triggerManager.AddMapTriggers(session.MapId, busTriggers);
            }

            DispatchLifecycleEvents(session, in evt, GameEvents.AbilityCastStarted, allTriggers);
        }

        private void TearDownMount(in GasPresentationEvent evt, EventKey terminalKey)
        {
            var key = new CastKey(evt.Actor, evt.AbilitySlot, evt.AbilityId);
            if (!_mounts.TryGetValue(key, out AbilityMountSet set))
            {
                return;
            }

            MapSession? session = _sessions()?.GetSession(set.MapId);
            if (session != null && session.State == MapSessionState.Active)
            {
                DispatchLifecycleEvents(session, in evt, terminalKey, set.AllTriggers);
            }

            if (set.BusTriggers.Count > 0)
            {
                _triggerManager.RemoveMapTriggers(set.MapId, set.BusTriggers);
            }

            _mounts.Remove(key);
        }

        private void SweepDeadScopes()
        {
            _sweepScratch.Clear();
            foreach (var kvp in _mounts)
            {
                if (kvp.Value.Scope == Entity.Null || kvp.Value.Scope == default || !_world.IsAlive(kvp.Value.Scope))
                {
                    _sweepScratch.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _sweepScratch.Count; i++)
            {
                if (_mounts.TryGetValue(_sweepScratch[i], out AbilityMountSet dead))
                {
                    if (dead.BusTriggers.Count > 0)
                    {
                        _triggerManager.RemoveMapTriggers(dead.MapId, dead.BusTriggers);
                    }

                    _mounts.Remove(_sweepScratch[i]);
                }
            }

            _sweepScratch.Clear();
        }

        private static bool IsAbilityLifecycleTrigger(Trigger trigger)
        {
            if (trigger is not TriggerGraphMountTrigger mount || mount.Domain != TriggerGraphMountDomain.Ability)
            {
                return false;
            }

            return mount.EventKey == GameEvents.AbilityCastStarted ||
                   mount.EventKey == GameEvents.AbilityCastFinished ||
                   mount.EventKey == GameEvents.AbilityCastInterrupted;
        }

        private MapSession? ResolveActiveSession(Entity actor)
        {
            if (!_world.Has<MapEntity>(actor))
            {
                return null;
            }

            MapId mapId = _world.Get<MapEntity>(actor).MapId;
            MapSession? session = _sessions()?.GetSession(mapId);
            if (session == null || session.State != MapSessionState.Active)
            {
                return null;
            }

            return session;
        }

        private void DispatchLifecycleEvents(MapSession session, in GasPresentationEvent evt, EventKey eventKey, List<Trigger> triggers)
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
                context.Set(MapTriggerEventPayloadKeys.SourceEntity, evt.Actor);
                context.Set(MapTriggerEventPayloadKeys.SourceTeamId, ResolveTeamId(evt.Actor));
                context.Set(MapTriggerEventPayloadKeys.TargetEntity, evt.Target);
                context.Set(MapTriggerEventPayloadKeys.AbilityId, evt.AbilityId);
                context.Set(MapTriggerEventPayloadKeys.EffectId, evt.EffectTemplateId);
                context.Set(MapTriggerEventPayloadKeys.Magnitude, evt.Delta);
                context.Set(MapTriggerEventPayloadKeys.Moment, evt.Kind.ToString());
                _ = mount.ExecuteLifecycleDispatch(context);
            }
        }

        private int ResolveTeamId(Entity entity)
        {
            return _world.Has<Team>(entity) ? _world.Get<Team>(entity).Id : 0;
        }

        private readonly struct CastKey : IEquatable<CastKey>
        {
            public CastKey(Entity actor, int abilitySlot, int abilityId)
            {
                Actor = actor;
                AbilitySlot = abilitySlot;
                AbilityId = abilityId;
            }

            public Entity Actor { get; }
            public int AbilitySlot { get; }
            public int AbilityId { get; }

            public bool Equals(CastKey other)
                => Actor == other.Actor && AbilitySlot == other.AbilitySlot && AbilityId == other.AbilityId;

            public override bool Equals(object? obj) => obj is CastKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Actor.GetHashCode();
                    hash = (hash * 397) ^ AbilitySlot;
                    hash = (hash * 397) ^ AbilityId;
                    return hash;
                }
            }
        }

        private sealed class AbilityMountSet
        {
            public AbilityMountSet(
                Entity scope,
                int abilityId,
                MapId mapId,
                List<Trigger> allTriggers,
                List<Trigger> busTriggers)
            {
                Scope = scope;
                AbilityId = abilityId;
                MapId = mapId;
                AllTriggers = allTriggers;
                BusTriggers = busTriggers;
            }

            public Entity Scope { get; }
            public int AbilityId { get; }
            public MapId MapId { get; }
            public List<Trigger> AllTriggers { get; }
            public List<Trigger> BusTriggers { get; }
        }
    }
}
