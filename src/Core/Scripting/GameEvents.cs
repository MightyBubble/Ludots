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
        /// Fired for networked processes after GameStart schema registration and runtime activation complete.
        /// Network-dependent systems can resolve all role-specific ports during this event.
        /// </summary>
        public static readonly EventKey NetworkRuntimeReady = new EventKey("NetworkRuntimeReady");

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

        /// <summary>
        /// Internal global pulse used to resume suspended Mod-domain TriggerGraph
        /// entries. Mod graphs are not registered in a map event index, so they
        /// cannot use the map heartbeat as their continuation clock.
        /// </summary>
        public static readonly EventKey ModTriggerResume = new EventKey("ModTriggerResume");

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
        public static readonly EventKey InputActionFired = new EventKey("InputActionFired");

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

        /// <summary>
        /// <summary>
        /// Map-scoped: fired whenever any declared map variable's value changes
        /// (int and float alike). Payload: VarName plus the old/new pair matching
        /// the variable's type (VarValueInt/OldValueInt or VarValueFloat/OldValueFloat).
        /// </summary>
        public static readonly EventKey MapVariableChanged = new EventKey("MapVariableChanged");

        /// <summary>
        /// Map-scoped: fired by the field membership system when a tracked entity's
        /// discrete-id field ownership changes to a new region. Payload: SourceEntity,
        /// RegionId, FieldLayer. Independent from the circle/rect trigger line above.
        /// </summary>
        public static readonly EventKey FieldRegionEntered = new EventKey("FieldRegionEntered");

        /// <summary>
        /// Map-scoped: fired by the field membership system when a tracked entity leaves
        /// its discrete-id field region. Payload: SourceEntity, RegionId, FieldLayer.
        /// </summary>
        public static readonly EventKey FieldRegionExited = new EventKey("FieldRegionExited");

        public static bool IsMapScoped(string eventName)
        {
            return eventName == MapLoaded.Value ||
                eventName == MapUnloaded.Value ||
                eventName == MapSuspended.Value ||
                eventName == MapResumed.Value ||
                eventName == MapHeartbeat.Value ||
                eventName == EntitySpawned.Value ||
                eventName == EntityDied.Value ||
                eventName == EntityAliveCountChanged.Value ||
                eventName == InputActionFired.Value ||
                eventName == RegionEntered.Value ||
                eventName == RegionExited.Value ||
                eventName == MapVariableChanged.Value ||
                eventName == FieldRegionEntered.Value ||
                eventName == FieldRegionExited.Value;
        }
    }
}
