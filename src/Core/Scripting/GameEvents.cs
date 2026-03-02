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
        /// Use this for spawning entities or initializing level-specific logic.
        /// </summary>
        public static readonly EventKey MapLoaded = new EventKey("MapLoaded");

        /// <summary>
        /// Fired when the game session ends or the application is closing.
        /// </summary>
        public static readonly EventKey GameEnd = new EventKey("GameEnd");
        
        /// <summary>
        /// Fired every frame (Use with caution!)
        /// </summary>
        public static readonly EventKey Tick = new EventKey("Tick");

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
    }
}
