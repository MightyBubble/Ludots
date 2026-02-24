namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Complete definition of an Effect preset type.
    /// Declares required components, active phases, lifetime constraints,
    /// and the default phase handler for each active phase.
    ///
    /// This is the authoritative source for "what happens at each phase" —
    /// no hidden conventions. Both builtin C# handlers and Graph programs
    /// are configured uniformly via <see cref="PhaseHandlerMap"/>.
    /// </summary>
    public struct PresetTypeDefinition
    {
        /// <summary>The preset type this definition describes.</summary>
        public EffectPresetType Type;

        /// <summary>Required parameter + capability components for this type.</summary>
        public ComponentFlags Components;

        /// <summary>Which lifecycle phases are active for this type.</summary>
        public PhaseFlags ActivePhases;

        /// <summary>Which lifetime kinds are valid for effects of this type.</summary>
        public LifetimeFlags AllowedLifetimes;

        /// <summary>
        /// Default handler (builtin or graph) for each active phase's Main slot.
        /// If a phase has no entry (Kind=None), the Main slot is empty
        /// (Pre/Post user graphs can still run).
        /// </summary>
        public PhaseHandlerMap DefaultPhaseHandlers;

        /// <summary>Check if a specific component flag is declared.</summary>
        public bool HasComponent(ComponentFlags flag) => (Components & flag) != 0;

        /// <summary>Check if a specific phase is active.</summary>
        public bool HasPhase(EffectPhaseId phase) => ActivePhases.Has(phase);

        /// <summary>Check if a specific lifetime kind is allowed.</summary>
        public bool AllowsLifetime(EffectLifetimeKind kind) => AllowedLifetimes.Allows(kind);
    }
}
