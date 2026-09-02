using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Context trigger gate (#1398 S2b): every tick, diffs the world-side active interaction
    /// context set per entity — the mounted base <see cref="InteractionContextInstance"/> plus
    /// every <see cref="InteractionContextInstances"/> instance — against the trigger
    /// mounts this system owns, and mounts/unmounts the profiles' declared
    /// <c>triggers[]</c> graphs on the context subject accordingly. Entering a context
    /// activates its TriggerGraph listeners; leaving deactivates them; derived contexts gate
    /// their own triggers exactly like base mounts. This is the simulation-side twin of the
    /// client-local <c>InputContextProjectionSystem</c> (which projects IMC contexts onto
    /// seats): trigger gating follows entity world state, so it holds for every observer and
    /// writer — exec reconciliation, cast ops, derived-context ops, and template spawns —
    /// without any of them knowing about triggers.
    /// <para>
    /// Mounts are entity-domain TriggerGraph mounts (scope = the context subject) registered
    /// through the map trigger tables, so dead subjects go inert and map unload reclaims
    /// them wholesale; this system's tracking follows via <see cref="DropMap"/>. Input-action
    /// payloads seed <c>MapTrigger.Rep</c> (not SourceEntity) from the mount subject; entity-domain
    /// mounts therefore match unscoped on the bus path, while action-bound mounts are dispatched
    /// per-subject by the action binding system.
    /// </para>
    /// </summary>
    public sealed class InteractionContextTriggerGateSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _activeContextQuery =
            new QueryDescription().WithAny<InteractionContextInstance, InteractionContextInstances>();

        private readonly TriggerManager _triggerManager;
        private readonly InteractionContextProfileRegistry _contextProfiles;
        private readonly GraphRuntime.GraphProgramRegistry _programs;
        private readonly CustomEventNameRegistry _customEvents;
        private readonly EventSchemaRegistry? _eventSchemas;
        private readonly Func<MapSessionManager?> _sessions;

        private readonly Dictionary<Entity, List<ContextMountSet>> _mounted = new();
        private Entity[] _subjects = new Entity[16];
        private int _subjectCount;
        private readonly List<int> _desiredProfileIds = new(4);

        public InteractionContextTriggerGateSystem(
            World world,
            TriggerManager triggerManager,
            InteractionContextProfileRegistry contextProfiles,
            GraphRuntime.GraphProgramRegistry programs,
            CustomEventNameRegistry customEvents,
            EventSchemaRegistry? eventSchemas,
            Func<MapSessionManager?> sessions)
            : base(world)
        {
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextProfiles = contextProfiles ?? throw new ArgumentNullException(nameof(contextProfiles));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _customEvents = customEvents ?? throw new ArgumentNullException(nameof(customEvents));
            _eventSchemas = eventSchemas;
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        /// <summary>Mounted context trigger count; test observability.</summary>
        public int MountedSubjectCount => _mounted.Count;

        /// <summary>The triggers mounted for one subject × context; test observability.</summary>
        public bool TryGetMountedTriggers(Entity subject, int profileId, out IReadOnlyList<Trigger> triggers)
        {
            triggers = Array.Empty<Trigger>();
            if (!_mounted.TryGetValue(subject, out List<ContextMountSet> sets))
            {
                return false;
            }

            ContextMountSet? match = sets.Find(set => set.ProfileId == profileId);
            if (match == null)
            {
                return false;
            }

            triggers = match.Triggers;
            return true;
        }

        public override void Update(in float dt)
        {
            CollectSubjects();
            UnmountStale();
            MountMissing();
        }

        /// <summary>Drops all tracking for a map (its trigger tables were unregistered wholesale).</summary>
        public void DropMap(MapId mapId)
        {
            foreach (List<ContextMountSet> sets in _mounted.Values)
            {
                for (int i = sets.Count - 1; i >= 0; i--)
                {
                    if (sets[i].MapId.Equals(mapId))
                    {
                        sets.RemoveAt(i);
                    }
                }
            }

            PruneEmptySubjects();
        }

        private void CollectSubjects()
        {
            _subjectCount = World.CountEntities(in _activeContextQuery);
            if (_subjectCount == 0)
            {
                return;
            }

            if (_subjectCount > _subjects.Length)
            {
                int next = _subjects.Length;
                while (next < _subjectCount)
                {
                    next *= 2;
                }

                _subjects = new Entity[next];
            }

            World.GetEntities(in _activeContextQuery, _subjects);
        }

        private void UnmountStale()
        {
            foreach (KeyValuePair<Entity, List<ContextMountSet>> pair in _mounted)
            {
                Entity subject = pair.Key;
                if (World.IsAlive(subject))
                {
                    CollectDesiredProfiles(subject, _desiredProfileIds);
                }
                else
                {
                    _desiredProfileIds.Clear();
                }

                List<ContextMountSet> sets = pair.Value;
                for (int i = sets.Count - 1; i >= 0; i--)
                {
                    if (_desiredProfileIds.Contains(sets[i].ProfileId))
                    {
                        continue;
                    }

                    _triggerManager.RemoveMapTriggers(sets[i].MapId, sets[i].Triggers);
                    sets.RemoveAt(i);
                }

                _desiredProfileIds.Clear();
            }

            PruneEmptySubjects();
        }

        private void MountMissing()
        {
            for (int s = 0; s < _subjectCount; s++)
            {
                Entity subject = _subjects[s];
                if (!World.IsAlive(subject))
                {
                    continue;
                }

                CollectDesiredProfiles(subject, _desiredProfileIds);
                for (int d = 0; d < _desiredProfileIds.Count; d++)
                {
                    int profileId = _desiredProfileIds[d];
                    if (!HasMounted(subject, profileId) &&
                        TryMount(subject, profileId, out ContextMountSet? mounted))
                    {
                        Track(subject, mounted!);
                    }
                }

                _desiredProfileIds.Clear();
            }
        }

        private void CollectDesiredProfiles(Entity subject, List<int> profileIds)
        {
            profileIds.Clear();
            if (World.TryGet<InteractionContextInstance>(subject, out InteractionContextInstance baseContext) &&
                !profileIds.Contains(baseContext.ContextId))
            {
                profileIds.Add(baseContext.ContextId);
            }

            if (World.TryGet<InteractionContextInstances>(subject, out InteractionContextInstances derived))
            {
                for (int i = 0; i < derived.Count; i++)
                {
                    if (!profileIds.Contains(derived[i].ContextId))
                    {
                        profileIds.Add(derived[i].ContextId);
                    }
                }
            }
        }

        private bool TryMount(Entity subject, int profileId, out ContextMountSet? mounted)
        {
            if (!_contextProfiles.TryGetDefinition(profileId, out InteractionContextProfileDefinition definition) ||
                definition.Triggers is not { Count: > 0 })
            {
                mounted = null;
                return false;
            }

            MapSession? session = ResolveSession(subject, profileId);
            var triggers = new List<Trigger>();
            string ownerLabel = $"Interaction context '{_contextProfiles.ProfileIdRegistry.GetName(profileId)}' on entity {subject}";
            for (int i = 0; i < definition.Triggers.Count; i++)
            {
                triggers.AddRange(TriggerGraphMounting.BuildContextMountTriggers(
                    _programs,
                    subject,
                    definition.Triggers[i],
                    ownerLabel,
                    _customEvents,
                    session?.EntityIndex,
                    _eventSchemas ?? _triggerManager.EventSchemas,
                    session == null ? null : TriggerGraphMounting.CollectRegionIds(session)));
            }

            MapId mapId = session?.MapId ?? ResolveRequiredMapId(subject);
            if (_triggerManager.OwnsMapTriggers(mapId))
            {
                _triggerManager.AddMapTriggers(mapId, triggers);
            }
            else
            {
                // A map may legitimately own zero authored triggers; the gate's mounts then
                // become the initial registration (later map-lifecycle mounts still append).
                _triggerManager.RegisterMapTriggers(mapId, triggers);
            }

            mounted = new ContextMountSet(profileId, mapId, triggers);
            return true;
        }

        private MapSession? ResolveSession(Entity subject, int profileId)
        {
            MapId mapId = ResolveRequiredMapId(subject);
            return _sessions()?.GetSession(mapId);
        }

        private MapId ResolveRequiredMapId(Entity subject)
        {
            if (!World.TryGet<MapEntity>(subject, out MapEntity mapEntity) || string.IsNullOrEmpty(mapEntity.MapId.Value))
            {
                throw new InvalidOperationException(
                    $"Interaction context trigger gating requires the context subject {subject} to carry a MapEntity; entity-domain trigger mounts register through their map.");
            }

            return mapEntity.MapId;
        }

        private bool HasMounted(Entity subject, int profileId)
        {
            return _mounted.TryGetValue(subject, out List<ContextMountSet> sets) &&
                sets.Exists(set => set.ProfileId == profileId);
        }

        private void Track(Entity subject, ContextMountSet mounted)
        {
            if (!_mounted.TryGetValue(subject, out List<ContextMountSet> sets))
            {
                sets = new List<ContextMountSet>(2);
                _mounted[subject] = sets;
            }

            sets.Add(mounted);
        }

        private void PruneEmptySubjects()
        {
            List<Entity>? empty = null;
            foreach (KeyValuePair<Entity, List<ContextMountSet>> pair in _mounted)
            {
                if (pair.Value.Count == 0)
                {
                    empty ??= new List<Entity>();
                    empty.Add(pair.Key);
                }
            }

            if (empty == null)
            {
                return;
            }

            for (int i = 0; i < empty.Count; i++)
            {
                _mounted.Remove(empty[i]);
            }
        }

        private sealed class ContextMountSet
        {
            public ContextMountSet(int profileId, MapId mapId, List<Trigger> triggers)
            {
                ProfileId = profileId;
                MapId = mapId;
                Triggers = triggers;
            }

            public int ProfileId { get; }

            public MapId MapId { get; }

            public List<Trigger> Triggers { get; }
        }
    }
}
