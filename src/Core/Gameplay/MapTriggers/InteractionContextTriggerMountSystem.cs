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
    internal enum WindowMountState : byte
    {
        /// <summary>All declared triggers mounted (default, no foreground descendant active).</summary>
        Interactive = 0,
        /// <summary>Only map/passive (event-bound) triggers mounted; interactive ones parked.</summary>
        Parked = 1,
    }

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
        private readonly List<TriggerMountOwner> _deferredDeactivatedUnmounts = new(4);
        private readonly List<int> _parkedScratch = new(4);
        private readonly List<Trigger> _actionTriggerScratch = new(4);
        private readonly List<Trigger> _mountScratch = new(8);
        private readonly List<(Entity, int)> _staleOpenScratch = new(4);
        private readonly Dictionary<(Entity, int), WindowMountState> _openWindows = new();
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
            // #1398 刀3: change-point deactivations (DeactivateContext op) run the profile's
            // onDeactivated slot synchronously at the call site, but never mutate the trigger
            // ledger mid-dispatch (the change point sits inside a context-owned TriggerGraph
            // mount's own execution; inline UnregisterTrigger would break the action-binding
            // index's live snapshot). The resulting unmounts are deferred here, flushed before
            // this tick's reconcile — the same InputCollection window the retired reconcile
            // used to unmount — so the scan below finds nothing left to unregister and never
            // re-runs a slot the change point already executed.
            FlushDeferredDeactivatedUnmounts();

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

                _openWindows.Remove((subject, _ownedScratch[i].Key.OwnerId));
                _triggerManager.RemoveOwnedMounts(_ownedScratch[i].Key);
                // Context window closed on this subject — run the Deactivated slot after
                // its mounts are gone, so the slot sees a fully taken-down window.
                RunLifecycleSlot(subject, _ownedScratch[i].Key.OwnerId, InteractionContextLifecycleSlot.Deactivated);
            }
        }

        /// <summary>
        /// One pass per subject: unmount context mounts that are no longer desired, demote the
        /// interactive mounts of profiles parked by an active foreground descendant, and mount
        /// the missing ones. The desired set is computed once per subject per frame (#1398 D12);
        /// window slots (onActivated/onDeactivated) run only on desired transitions, never on a
        /// park/unpark regime change (#1398 刀4).
        /// </summary>
        private void ReconcileSubject(Entity subject)
        {
            CollectParkedProfiles(subject, _parkedScratch);

            // ── Unmount / demote pass ──
            _ownedScratch.Clear();
            _triggerManager.CollectOwnedMounts(subject, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                var owner = _ownedScratch[i].Key;
                if (owner.Kind != TriggerMountOwnerKind.InteractionContext)
                {
                    continue;
                }

                int profileId = owner.OwnerId;
                if (!_desiredProfileIds.Contains(profileId))
                {
                    // Window closed: Deactivated slot after full take-down (fallback path for
                    // non-op removals; op-path windows are unmarked below after the flush).
                    _openWindows.Remove((subject, profileId));
                    _triggerManager.RemoveOwnedMounts(owner);
                    RunLifecycleSlot(subject, profileId, InteractionContextLifecycleSlot.Deactivated);
                    continue;
                }

                if (_parkedScratch.Contains(profileId) &&
                    _openWindows.TryGetValue((subject, profileId), out WindowMountState state) &&
                    state == WindowMountState.Interactive)
                {
                    // Foreground descendant active: park the interactive (action-bound) mounts
                    // only — map/passive mounts stay, the window stays open, no Deactivated slot.
                    RemoveActionTriggerMounts(subject, _ownedScratch[i].Value);
                    _openWindows[(subject, profileId)] = WindowMountState.Parked;
                }
            }

            // Op-closed windows (knife 3 change point) can carry no mounts by the time this
            // scan runs; unmark any window whose profile is no longer desired.
            _staleOpenScratch.Clear();
            foreach (KeyValuePair<(Entity, int), WindowMountState> entry in _openWindows)
            {
                if (entry.Key.Item1 == subject && !_desiredProfileIds.Contains(entry.Key.Item2))
                {
                    _staleOpenScratch.Add(entry.Key);
                }
            }

            for (int i = 0; i < _staleOpenScratch.Count; i++)
            {
                _openWindows.Remove(_staleOpenScratch[i]);
            }

            // ── Open / restore pass ──
            for (int d = 0; d < _desiredProfileIds.Count; d++)
            {
                int profileId = _desiredProfileIds[d];
                bool parked = _parkedScratch.Contains(profileId);
                TriggerMountOwner owner = new(TriggerMountOwnerKind.InteractionContext, subject, profileId);
                if (!_openWindows.TryGetValue((subject, profileId), out WindowMountState state))
                {
                    // Window opening (newly desired profile): Activated slot runs before the
                    // trigger mounts register — "onActivated 在 trigger 开启之前" by construction.
                    state = parked ? WindowMountState.Parked : WindowMountState.Interactive;
                    _openWindows[(subject, profileId)] = state;
                    RunLifecycleSlot(subject, profileId, InteractionContextLifecycleSlot.Activated);
                    TryMount(subject, profileId, state == WindowMountState.Parked ? ContextMountEntryClass.Passive : ContextMountEntryClass.All);
                    continue;
                }

                if (state == WindowMountState.Parked && !parked)
                {
                    // Foreground descendant gone: restore the interactive mounts — the window
                    // never closed, so no Activated slot (park/unpark is a regime change only).
                    _openWindows[(subject, profileId)] = WindowMountState.Interactive;
                    // A fully-stripped parked window (map reload) restores everything; a parked
                    // window that kept its passive mounts restores only the interactive class.
                    bool fullRestore = !_triggerManager.TryGetOwnedMounts(owner, out _);
                    TryMount(
                        subject,
                        profileId,
                        fullRestore ? ContextMountEntryClass.All : ContextMountEntryClass.Interactive);
                    continue;
                }

                if (state == WindowMountState.Interactive && parked)
                {
                    // Fully-stripped interactive window (nothing owned when the demote pass ran,
                    // e.g. all-action profile) now parks: record the regime, nothing to strip.
                    _openWindows[(subject, profileId)] = WindowMountState.Parked;
                    continue;
                }

                // Self-heal runs only for interactive windows: a parked window deliberately owns
                // no interactive content, so rebuilding it would re-register the resume companions
                // of its demoted mounts every frame (they do not create owned records and the
                // ledger would never read as "present"). Interactive windows, whose entire content
                // vanished (map reload, external removal), re-mount the full set without re-running
                // the boundary slot.
                if (state == WindowMountState.Interactive && !_triggerManager.TryGetOwnedMounts(owner, out _))
                {
                    TryMount(subject, profileId, ContextMountEntryClass.All);
                }
            }
        }

        /// <summary>
        /// Parked profile set for the subject (#1398 刀4): a profile is parked while any active
        /// descendant (any depth, base mount or derived instance) declares <c>Foreground</c>.
        /// Walk every foreground node up its <c>ParentContextId</c> chain and collect ancestors.
        /// Siblings and the foreground profile itself are never parked — scope coexistence stays
        /// a non-stack set.
        /// </summary>
        private void CollectParkedProfiles(Entity subject, List<int> parkedSink)
        {
            parkedSink.Clear();
            if (_desiredProfileIds.Count == 0)
            {
                return;
            }

            bool hasBase = World.TryGet<InteractionContextInstance>(subject, out InteractionContextInstance baseContext);
            bool hasDerived = World.TryGet<InteractionContextInstances>(subject, out InteractionContextInstances derived);
            int derivedOffset = hasBase ? 1 : 0;
            for (int k = 0; k < _desiredProfileIds.Count; k++)
            {
                int nodeProfileId = _desiredProfileIds[k];
                if (!_contextProfiles.IsForeground(nodeProfileId))
                {
                    continue;
                }

                int parentProfileId = k < derivedOffset || !hasDerived
                    ? 0
                    : derived[k - derivedOffset].ParentContextId;
                int ancestor = parentProfileId;
                while (ancestor > 0)
                {
                    if (!parkedSink.Contains(ancestor))
                    {
                        parkedSink.Add(ancestor);
                    }

                    ancestor = FindParentProfileId(ancestor, hasBase, baseContext.ContextId, in derived);
                }
            }
        }

        private int FindParentProfileId(int profileId, bool hasBase, int baseProfileId, in InteractionContextInstances derived)
        {
            if (hasBase && baseProfileId == profileId)
            {
                return 0;
            }

            for (int i = 0; i < derived.Count; i++)
            {
                if (derived[i].ContextId == profileId)
                {
                    return derived[i].ParentContextId;
                }
            }

            return 0;
        }

        /// <summary>
        /// Remove only the interactive (action-bound) triggers of one owned set, keeping the
        /// map/passive mounts — the parked demotion. Called from the gate's own reconcile
        /// (InputCollection, before any dispatch), so the ledger-mutation safety the deferred
        /// flush guarantees for change points does not apply here.
        /// </summary>
        private void RemoveActionTriggerMounts(Entity subject, List<Trigger> owned)
        {
            _actionTriggerScratch.Clear();
            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i] is TriggerGraphMountTrigger actionMount &&
                    !string.IsNullOrWhiteSpace(actionMount.ActionId))
                {
                    _actionTriggerScratch.Add(owned[i]);
                }
            }

            if (_actionTriggerScratch.Count > 0)
            {
                _triggerManager.RemoveMapTriggers(ResolveRequiredMapId(subject), _actionTriggerScratch);
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
        /// Change-point contract (#1398 刀3): runs a profile's <c>onDeactivated</c> slot
        /// synchronously at the moment its context leaves the subject, so settlement
        /// (<c>selection_commit</c>) and preview teardown complete in the same tick as the
        /// <c>DeactivateContext</c> op instead of waiting for the next reconcile pass. Called
        /// by <see cref="InteractionContextInstanceRuntime.Deactivate"/> after the context
        /// component is committed away; the caller feeds every removed profile id (transitive
        /// descendants included).
        /// <para>
        /// Trigger unmount is deferred to this system's next <see cref="Update"/> flush — the
        /// change point can be inside a context-owned TriggerGraph mount's own dispatch, where
        /// inline ledger mutation would corrupt the dispatcher's live lists. The slot runs once
        /// here; the reconcile's Deactivated branch (which unmounts and runs the slot) degrades
        /// to a pure fallback for non-op component removals and finds nothing to do for
        /// op-path deactivations because the mounts are already gone.
        /// </para>
        /// </summary>
        public void RunDeactivatedSlotNow(Entity subject, int profileId)
        {
            if (!World.IsAlive(subject))
            {
                // Owner-death boundary already ran the slot and reclaimed mounts.
                return;
            }

            RunLifecycleSlot(subject, profileId, InteractionContextLifecycleSlot.Deactivated);
            _deferredDeactivatedUnmounts.Add(new TriggerMountOwner(TriggerMountOwnerKind.InteractionContext, subject, profileId));
        }

        private void FlushDeferredDeactivatedUnmounts()
        {
            for (int i = 0; i < _deferredDeactivatedUnmounts.Count; i++)
            {
                _triggerManager.RemoveOwnedMounts(_deferredDeactivatedUnmounts[i]);
            }

            _deferredDeactivatedUnmounts.Clear();
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
            if (World.TryGet<MapEntity>(entity, out MapEntity mapEntity))
            {
                MapSession? session = _sessions()?.GetSession(mapEntity.MapId);
                if (session != null && session.State == MapSessionState.Active)
                {
                    if (World.TryGet<InteractionContextInstance>(entity, out InteractionContextInstance baseContext))
                    {
                        _openWindows.Remove((entity, baseContext.ContextId));
                        RunLifecycleSlot(entity, baseContext.ContextId, InteractionContextLifecycleSlot.Deactivated);
                    }

                    if (World.TryGet<InteractionContextInstances>(entity, out InteractionContextInstances instances))
                    {
                        for (int i = 0; i < instances.Count; i++)
                        {
                            _openWindows.Remove((entity, instances[i].ContextId));
                            RunLifecycleSlot(entity, instances[i].ContextId, InteractionContextLifecycleSlot.Deactivated);
                        }
                    }
                }
            }

            // Destroy-time reclamation (#1398 刀2): the retired heartbeat sweep is gone, so
            // the gate reclaims the dead subject's own context mounts right here — after the
            // Deactivated slots, same-handler, no staged budget (naturally bounded by the dead
            // subject's mount count). Entity-domain template mounts are reclaimed by
            // EntityTriggerGraphMounts.OnEntityDestroyed on the same destroy. Ledger cleanup
            // is session-independent (it touches only the owned-mount index), so it runs even
            // when no active session is resolvable — exactly the mount-pipeline unload guard.
            _ownedScratch.Clear();
            _triggerManager.CollectOwnedMounts(entity, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                if (_ownedScratch[i].Key.Kind == TriggerMountOwnerKind.InteractionContext)
                {
                    _triggerManager.RemoveOwnedMounts(_ownedScratch[i].Key);
                }
            }
        }

        /// <summary>
        /// Mount a profile's declared triggers on the subject, filtered by entry class (#1398 刀4):
        /// <see cref="ContextMountEntryClass.All"/> for a normal window, <see cref="ContextMountEntryClass.Passive"/>
        /// while parked (map/passive listeners stay live), <see cref="ContextMountEntryClass.Interactive"/>
        /// when a parked window is restored. Class filtering happens inside the build
        /// (TriggerGraphMounting) so each kept entry's mount trigger AND its resume companion
        /// travel together — a post-build list filter would orphan the companions. Mounts are
        /// stamped with the context owner before registration; duplicate-safe because callers
        /// only mount a class the ledger lacks.
        /// </summary>
        private void TryMount(Entity subject, int profileId, ContextMountEntryClass mountClass)
        {
            if (!_contextProfiles.TryGetDefinition(profileId, out InteractionContextProfileDefinition definition) ||
                definition.Triggers is not { Count: > 0 })
            {
                return;
            }

            MapSession? session = ResolveSession(subject, profileId);
            string ownerLabel = $"Interaction context '{_contextProfiles.ProfileIdRegistry.GetName(profileId)}' on entity {subject}";
            _mountScratch.Clear();
            TriggerMountOwner owner = new(TriggerMountOwnerKind.InteractionContext, subject, profileId);
            for (int i = 0; i < definition.Triggers.Count; i++)
            {
                _mountScratch.AddRange(TriggerGraphMounting.BuildContextMountTriggers(
                    _programs,
                    subject,
                    definition.Triggers[i],
                    ownerLabel,
                    _customEvents,
                    session?.EntityIndex,
                    _eventSchemas ?? _triggerManager.EventSchemas,
                    session == null ? null : TriggerGraphMounting.CollectRegionIds(session),
                    mountClass));
            }

            if (_mountScratch.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _mountScratch.Count; i++)
            {
                if (_mountScratch[i] is TriggerGraphMountTrigger mount)
                {
                    mount.Owner = owner;
                }
            }

            MapId mapId = session?.MapId ?? ResolveRequiredMapId(subject);
            if (_triggerManager.OwnsMapTriggers(mapId))
            {
                _triggerManager.AddMapTriggers(mapId, _mountScratch);
            }
            else
            {
                // A map may legitimately own zero authored triggers; the gate's mounts then
                // become the initial registration (later map-lifecycle mounts still append).
                _triggerManager.RegisterMapTriggers(mapId, _mountScratch);
            }
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
