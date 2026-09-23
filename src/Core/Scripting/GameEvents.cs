namespace Ludots.Core.Scripting
{
    /// <summary>
    /// Standardized event keys used by the TriggerManager.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>
        /// Fired when the game session starts, but before any map is loaded.
        /// </summary>
        public static readonly EventKey GameStart = new EventKey("GameStart");

        /// <summary>
        /// Fired when a map has finished loading and dependencies are resolved.
        /// If a host-side async world switch participates in completion, this fires only after the host world
        /// and required host-bound entities are ready.
        /// </summary>
        public static readonly EventKey MapLoaded = new EventKey("MapLoaded");

        /// <summary>
        /// Fired when the game session ends or the application is closing.
        /// </summary>
        public static readonly EventKey GameEnd = new EventKey("GameEnd");

        /// <summary>
        /// Fired after a mod is successfully loaded.
        /// Context contains "ModId".
        /// </summary>
        public static readonly EventKey ModLoaded = new EventKey("ModLoaded");

        public static readonly EventKey SimulationBudgetFused = new EventKey("SimulationBudgetFused");

        public static readonly EventKey Physics2DEnabled = new EventKey("Physics2DEnabled");
        public static readonly EventKey Physics2DDisabled = new EventKey("Physics2DDisabled");
        public static readonly EventKey Physics2DRunStarted = new EventKey("Physics2DRunStarted");
        public static readonly EventKey Physics2DRunCompleted = new EventKey("Physics2DRunCompleted");

        public static readonly EventKey GasRunStarted = new EventKey("GasRunStarted");
        public static readonly EventKey GasRunCompleted = new EventKey("GasRunCompleted");

        public static readonly EventKey TurnAdvanced = new EventKey("TurnAdvanced");

        /// <summary>
        /// Fired when a map is about to be unloaded.
        /// Triggers' OnMapExit is called during this event.
        /// </summary>
        public static readonly EventKey MapUnloaded = new EventKey("MapUnloaded");

        /// <summary>
        /// Fired when a map is suspended (e.g., an inner map is pushed on top).
        /// </summary>
        public static readonly EventKey MapSuspended = new EventKey("MapSuspended");

        /// <summary>
        /// Fired when a previously suspended map is restored to active.
        /// </summary>
        public static readonly EventKey MapResumed = new EventKey("MapResumed");

        /// <summary>
        /// Map-scoped: fired when a map's think-wave interval of fixed ticks elapses.
        /// Payload: MapTriggerEventPayloadKeys.HeartbeatIndex.
        /// </summary>
        public static readonly EventKey MapHeartbeat = new EventKey("MapHeartbeat");

        /// <summary>
        /// Map-scoped: fired at think-wave granularity for entities that joined the map
        /// during the wave. Payload: SourceEntity, SourceTeamId.
        /// </summary>
        public static readonly EventKey EntitySpawned = new EventKey("EntitySpawned");

        /// <summary>
        /// Map-scoped: fired at think-wave granularity for entities destroyed during the
        /// wave. The entity may already be recycled when the event fires; SourceTeamId was
        /// captured at destroy time. Payload: SourceEntity, SourceTeamId.
        /// </summary>
        public static readonly EventKey EntityDied = new EventKey("EntityDied");

        /// <summary>
        /// Map-scoped: fired at think-wave granularity when a team's alive-entity count
        /// (entities with AttributeBuffer) differs from the previous wave.
        /// Payload: SourceTeamId, Count, Delta.
        /// </summary>
        public static readonly EventKey EntityAliveCountChanged = new EventKey("EntityAliveCountChanged");

        /// <summary>
        /// Map-scoped: fired by the region system when an entity enters a region.
        /// Payload: SourceEntity, RegionId.
        /// </summary>
        public static readonly EventKey RegionEntered = new EventKey("RegionEntered");

        /// <summary>
        /// Map-scoped: fired by the region system when an entity exits a region.
        /// Payload: SourceEntity, RegionId.
        /// </summary>
        public static readonly EventKey RegionExited = new EventKey("RegionExited");

        // ── GAS moment bridge events (#1031 D5) ──
        // Fired by TriggerGraphMomentBridgeSystem from the GasPresentationEventBuffer
        // (same-step view). Payload: SourceEntity (actor/effect owner), TargetEntity,
        // AbilityId, EffectId, Magnitude, Moment.

        /// <summary>Ability cast started. Ability-domain lifecycle event (mount creation).</summary>
        public static readonly EventKey AbilityCastStarted = new EventKey("Ability.CastStarted");

        /// <summary>Ability cast rejected before starting. No ability mount exists.</summary>
        public static readonly EventKey AbilityCastFailed = new EventKey("Ability.CastFailed");

        /// <summary>Ability cast committed (exec instance active).</summary>
        public static readonly EventKey AbilityCastCommitted = new EventKey("Ability.CastCommitted");

        /// <summary>Ability cast finished. Ability-domain lifecycle event (mount teardown).</summary>
        public static readonly EventKey AbilityCastFinished = new EventKey("Ability.CastFinished");

        /// <summary>Ability cast interrupted. Ability-domain lifecycle event (mount teardown).</summary>
        public static readonly EventKey AbilityCastInterrupted = new EventKey("Ability.CastInterrupted");

        /// <summary>Effect applied (buffer event).</summary>
        public static readonly EventKey EffectApplied = new EventKey("Effect.Applied");

        /// <summary>Effect activated.</summary>
        public static readonly EventKey EffectActivated = new EventKey("Effect.Activated");

        /// <summary>Effect expired.</summary>
        public static readonly EventKey EffectExpired = new EventKey("Effect.Expired");

        /// <summary>Effect cancelled.</summary>
        public static readonly EventKey EffectCancelled = new EventKey("Effect.Cancelled");
    }
}
