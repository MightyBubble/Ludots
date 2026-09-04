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
    /// Context trigger gate (#1398 S2b + D15): every tick, diffs the world-side active
    /// interaction context set per entity — the mounted base
    /// <see cref="InteractionContextInstance"/> plus every
    /// <see cref="InteractionContextInstances"/> instance — against the context-owned
    /// mounts in the TriggerManager ledger, and mounts/unmounts the profiles' declared
    /// <c>triggers[]</c> graphs on the context subject accordingly. Entering a context
    /// activates its TriggerGraph listeners; leaving deactivates them; derived contexts gate
    /// their own triggers exactly like base mounts. This is the simulation-side twin of the
    /// client-local <c>InputContextProjectionSystem</c> (which projects IMC contexts onto
    /// seats): trigger gating follows entity world state, so it holds for every observer and
    /// writer — exec reconciliation, cast ops, derived-context ops, and template spawns —
    /// without any of them knowing about triggers.
    /// <para>
    /// D15 lifecycle slots: the same mount window is flanked by the profiles'
    /// <c>onActivated</c>/<c>onDeactivated</c> graph bodies — Activated runs as the window
    /// opens (before the profile's triggers register), Deactivated as it closes (after they
    /// are removed, plus the owner-death path via the destroy boundary). Slots are instant
    /// boundary hooks (explicitly not a per-tick period, unlike the retired
    /// <c>whileActive</c>), and belong to their own profile — no owner matching is ever
    /// needed.
    /// </para>
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
        private readonly Ludots.Core.NodeLibraries.GASGraph.GraphReturnWriter _graphReturnWriter;
        private readonly Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi _graphApi;

        private Entity[] _subjects = new Entity[16];
        private int _subjectCount;
        private readonly List<int> _desiredProfileIds = new(4);
        private readonly List<KeyValuePair<TriggerMountOwner, List<Trigger>>> _ownedScratch = new();
        private HashSet<Entity> _frameSubjects = new();
        private HashSet<Entity> _retiredSubjects = new();

        public InteractionContextTriggerMountSystem(
            World world,
            TriggerManager triggerManager,
            InteractionContextProfileRegistry contextProfiles,
            GraphRuntime.GraphProgramRegistry programs,
            CustomEventNameRegistry customEvents,
            EventSchemaRegistry? eventSchemas,
            Func<MapSessionManager?> sessions,
            Ludots.Core.NodeLibraries.GASGraph.GraphReturnWriter graphReturnWriter,
            Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi graphApi)
            : base(world)
        {
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextProfiles = contextProfiles ?? throw new ArgumentNullException(nameof(contextProfiles));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _customEvents = customEvents ?? throw new ArgumentNullException(nameof(customEvents));
            _eventSchemas = eventSchemas;
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _graphReturnWriter = graphReturnWriter ?? throw new ArgumentNullException(nameof(graphReturnWriter));
            _graphApi = graphApi ?? throw new ArgumentNullException(nameof(graphApi));
            // #1398 D15: an owner destroyed while still carrying context components never
            // went through an explicit deactivation — run each carried context's
            // onDeactivated slot at the destroy boundary so settlement/preview cleanup
            // still happen (the retired gate skipped dead subjects entirely).
            World.SubscribeEntityDestroyed(OnContextOwnerDestroyed);
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
        /// This frame's context-subject snapshot (filled by Update). Consumed by lifecycle
        /// slot execution and exposed for test observability; runs after this system in the
        /// same tick. Valid until the next Update.
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

            // Double-buffered subject sets: the retired buffer holds last frame's scan so
            // the catch-up below only reconciles subjects that actually left the scan this
            // frame (O(delta)), never the whole mount index (#1398 D12).
            HashSet<Entity> retired = _retiredSubjects;
            HashSet<Entity> current = _frameSubjects;
            current.Clear();
            for (int s = 0; s < _subjectCount; s++)
            {
                Entity subject = _subjects[s];
                if (!World.IsAlive(subject))
                {
                    // Dead subjects keep their (inert) mounts until the unified heartbeat
                    // sweep reclaims them — the same policy template mounts follow (#1398 D11).
                    continue;
                }

                current.Add(subject);
                CollectDesiredProfiles(subject, _desiredProfileIds);
                ReconcileSubject(subject);
                _desiredProfileIds.Clear();
            }

            // Catch-up: a live subject that left the scan entirely (its last context was
            // deactivated) no longer matches the component query, so the loop above cannot
            // unmount its stale context mounts. Owning no context components means its
            // desired set is empty — remove every context-owned mount it still carries.
            foreach (Entity subject in retired)
            {
                if (!current.Contains(subject) && World.IsAlive(subject))
                {
                    RemoveAllContextMounts(subject);
                }
            }

            _retiredSubjects = current;
            _frameSubjects = retired;
        }

        private void RemoveAllContextMounts(Entity subject)
        {
            _ownedScratch.Clear();
            _triggerManager.CollectOwnedMounts(subject, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                if (_ownedScratch[i].Key.Kind != TriggerMountOwnerKind.InteractionContext)
                {
                    continue;
                }

                _triggerManager.RemoveOwnedMounts(_ownedScratch[i].Key);
                // Context window closed on this subject — run the Deactivated slot after
                // its mounts are gone, so the slot sees a fully taken-down window.
                RunLifecycleSlot(subject, _ownedScratch[i].Key.OwnerId, InteractionContextLifecycleSlot.Deactivated);
            }
        }

        /// <summary>
        /// One pass per subject: unmount context mounts that are no longer desired, mount the
        /// missing ones. The desired set is computed once per subject per frame (#1398 D12).
        /// </summary>
        private void ReconcileSubject(Entity subject)
        {
            _ownedScratch.Clear();
            _triggerManager.CollectOwnedMounts(subject, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                if (_ownedScratch[i].Key.Kind != TriggerMountOwnerKind.InteractionContext ||
                    _desiredProfileIds.Contains(_ownedScratch[i].Key.OwnerId))
                {
                    continue;
                }

                _triggerManager.RemoveOwnedMounts(_ownedScratch[i].Key);
                // Window closing (profile no longer desired): Deactivated slot after unmount.
                RunLifecycleSlot(subject, _ownedScratch[i].Key.OwnerId, InteractionContextLifecycleSlot.Deactivated);
            }

            for (int d = 0; d < _desiredProfileIds.Count; d++)
            {
                int profileId = _desiredProfileIds[d];
                TriggerMountOwner owner = new(TriggerMountOwnerKind.InteractionContext, subject, profileId);
                if (_triggerManager.TryGetOwnedMounts(owner, out _))
                {
                    continue;
                }

                // Window opening (newly desired profile): Activated slot runs before the
                // trigger mounts register — "onActivated 在 trigger 开启之前" by construction.
                RunLifecycleSlot(subject, profileId, InteractionContextLifecycleSlot.Activated);
                TryMount(subject, profileId, owner);
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

        /// <summary>
        /// Runs one lifecycle slot's graph bodies on the subject (instant window-boundary
        /// hooks; see <see cref="InteractionContextLifecycleSlot"/>). The graph is a plain
        /// body via GraphReturnWriter — no entries, no bus, no owner matching: the slot
        /// belongs to its own profile, so it can never observe a sibling context.
        /// </summary>
        private void RunLifecycleSlot(Entity subject, int profileId, InteractionContextLifecycleSlot slot)
        {
            if (!_contextProfiles.TryGetLifecycleGraphIds(profileId, slot, out ReadOnlySpan<int> graphIds))
            {
                return;
            }

            for (int i = 0; i < graphIds.Length; i++)
            {
                _graphReturnWriter.Execute(
                    graphIds[i],
                    caster: subject,
                    explicitTarget: Entity.Null,
                    targetContext: Entity.Null,
                    targetPosCm: default,
                    randomSeed: 0u,
                    api: _graphApi);
            }
        }

        /// <summary>
        /// Owner-destroyed boundary: while components are still attached, run the
        /// Deactivated slot for every context the entity carries (base mount + instances).
        /// This is the death path the retired gate skipped — settlement and preview cleanup
        /// still run when an owner dies mid-gesture. Mirrors the entity-mount death
        /// dispatch guard: active map sessions only, no unload-time firing.
        /// </summary>
        private void OnContextOwnerDestroyed(in Entity entity)
        {
            if (!World.TryGet<MapEntity>(entity, out MapEntity mapEntity))
            {
                return;
            }

            MapSession? session = _sessions()?.GetSession(mapEntity.MapId);
            if (session == null || session.State != MapSessionState.Active)
            {
                return;
            }

            if (World.TryGet<InteractionContextInstance>(entity, out InteractionContextInstance baseContext))
            {
                RunLifecycleSlot(entity, baseContext.ContextId, InteractionContextLifecycleSlot.Deactivated);
            }

            if (World.TryGet<InteractionContextInstances>(entity, out InteractionContextInstances instances))
            {
                for (int i = 0; i < instances.Count; i++)
                {
                    RunLifecycleSlot(entity, instances[i].ContextId, InteractionContextLifecycleSlot.Deactivated);
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
