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
    /// every <see cref="InteractionContextInstances"/> instance — against the context-owned
    /// mounts in the TriggerManager ledger, and mounts/unmounts the profiles' declared
    /// <c>triggers[]</c> graphs on the context subject accordingly. Entering a context
    /// activates its TriggerGraph listeners; leaving deactivates them; derived contexts gate
    /// their own triggers exactly like base mounts. This is the simulation-side twin of the
    /// client-local <c>InputContextProjectionSystem</c> (which projects IMC contexts onto
    /// seats): trigger gating follows entity world state, so it holds for every observer and
    /// writer — exec reconciliation, cast ops, derived-context ops, and template spawns —
    /// without any of them knowing about triggers.
    /// <para>
    /// Ledger: mounts are stamped with a <see cref="TriggerMountOwner"/> before registration
    /// and this system keeps no parallel mount list (#1398 D10); the TriggerManager owner
    /// index answers "what is mounted for (subject, profile)" and removes by owner. Dead
    /// subjects are skipped here — their inert mounts are reclaimed by the unified
    /// heartbeat sweep in <see cref="EntityTriggerGraphMounts"/> (#1398 D11), the same
    /// policy template mounts follow.
    /// </para>
    /// <para>
    /// Mounts are entity-domain TriggerGraph mounts (scope = the context subject) registered
    /// through the map trigger tables, so map unload reclaims them wholesale when the map's
    /// tables are unregistered; dead subjects go inert and the sweep reclaims them with a
    /// bounded budget. Input-action payloads seed <c>MapTrigger.Rep</c> (not SourceEntity)
    /// from the mount subject; entity-domain mounts therefore match unscoped on the bus path,
    /// while action-bound mounts are dispatched per-subject by the action binding system.
    /// </para>
    /// </summary>
    public sealed class InteractionContextTriggerMountSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _activeContextQuery =
            new QueryDescription().WithAny<InteractionContextInstance, InteractionContextInstances>();

        private readonly TriggerManager _triggerManager;
        private readonly InteractionContextProfileRegistry _contextProfiles;
        private readonly GraphRuntime.GraphProgramRegistry _programs;
        private readonly CustomEventNameRegistry _customEvents;
        private readonly EventSchemaRegistry? _eventSchemas;
        private readonly Func<MapSessionManager?> _sessions;

        private Entity[] _subjects = new Entity[16];
        private int _subjectCount;
        private readonly List<int> _desiredProfileIds = new(4);
        private readonly List<KeyValuePair<TriggerMountOwner, List<Trigger>>> _ownedScratch = new();
        private readonly HashSet<Entity> _frameSubjectSet = new();

        public InteractionContextTriggerMountSystem(
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

        /// <summary>
        /// Live subjects carrying context-owned mounts. Dead subjects awaiting the budget
        /// sweep do not count: their mounts are inert the tick they die.
        /// </summary>
        public int MountedSubjectCount
            => _triggerManager.CountOwnedMountSubjects(
                TriggerMountOwnerKind.InteractionContext,
                subject => World.IsAlive(subject));

        /// <summary>The triggers mounted for one subject × context; test observability.</summary>
        public bool TryGetMountedTriggers(Entity subject, int profileId, out IReadOnlyList<Trigger> triggers)
        {
            return _triggerManager.TryGetOwnedMounts(
                new TriggerMountOwner(TriggerMountOwnerKind.InteractionContext, subject, profileId),
                out triggers);
        }

        /// <summary>
        /// This frame's context-subject snapshot (filled by Update). Consumed by
        /// <see cref="InteractionContextWhileActiveSystem"/> so both systems share one world
        /// scan per tick instead of each running the same query (#1398 D12). Valid until the
        /// next Update; the consumer must run after this system in the same tick.
        /// </summary>
        public bool TryGetFrameSubjects(out Entity[] subjects, out int count)
        {
            subjects = _subjects;
            count = _subjectCount;
            return count > 0;
        }

        public override void Update(in float dt)
        {
            CollectSubjects();
            _frameSubjectSet.Clear();
            for (int s = 0; s < _subjectCount; s++)
            {
                Entity subject = _subjects[s];
                if (!World.IsAlive(subject))
                {
                    // Dead subjects keep their (inert) mounts until the unified heartbeat
                    // sweep reclaims them — the same policy template mounts follow (#1398 D11).
                    continue;
                }

                _frameSubjectSet.Add(subject);
                CollectDesiredProfiles(subject, _desiredProfileIds);
                ReconcileSubject(subject);
                _desiredProfileIds.Clear();
            }

            // Catch-up: a live subject that left the scan entirely (its last context was
            // deactivated) no longer matches the component query, so the loop above cannot
            // unmount its stale context mounts. Owning no context components means its
            // desired set is empty — remove every context-owned mount it still carries.
            _ownedScratch.Clear();
            _triggerManager.CollectOwnedMounts(TriggerMountOwnerKind.InteractionContext, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                Entity subject = _ownedScratch[i].Key.Subject;
                if (World.IsAlive(subject) && !_frameSubjectSet.Contains(subject))
                {
                    _triggerManager.RemoveOwnedMounts(_ownedScratch[i].Key);
                }
            }
        }

        /// <summary>
        /// One pass per subject: unmount context mounts that are no longer desired, mount the
        /// missing ones. The desired set is computed once per subject per frame (#1398 D12).
        /// </summary>
        private void ReconcileSubject(Entity subject)
        {
            _ownedScratch.Clear();
            _triggerManager.CollectOwnedMounts(subject, TriggerMountOwnerKind.InteractionContext, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                if (_desiredProfileIds.Contains(_ownedScratch[i].Key.OwnerId))
                {
                    continue;
                }

                _triggerManager.RemoveOwnedMounts(_ownedScratch[i].Key);
            }

            for (int d = 0; d < _desiredProfileIds.Count; d++)
            {
                int profileId = _desiredProfileIds[d];
                TriggerMountOwner owner = new(TriggerMountOwnerKind.InteractionContext, subject, profileId);
                if (!_triggerManager.TryGetOwnedMounts(owner, out _))
                {
                    TryMount(subject, profileId, owner);
                }
            }
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

        private void CollectDesiredProfiles(Entity subject, List<int> profileIds)
        {
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

        private bool TryMount(Entity subject, int profileId, TriggerMountOwner owner)
        {
            if (!_contextProfiles.TryGetDefinition(profileId, out InteractionContextProfileDefinition definition) ||
                definition.Triggers is not { Count: > 0 })
            {
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
            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i] is TriggerGraphMountTrigger mount)
                {
                    mount.Owner = owner;
                }
            }

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
    }
}
