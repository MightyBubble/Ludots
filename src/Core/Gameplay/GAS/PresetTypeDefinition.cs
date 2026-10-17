namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// This is the authoritative source for "what happens at each phase" —
    /// no hidden conventions. Both builtin C# handlers and Graph programs
    /// are configured uniformly via <see cref="PhaseHandlerMap"/>.
    /// </summary>
    public struct PresetTypeDefinition
    {
        /// <summary>Custom presets keep this as None.</summary>
        public EffectPresetType Type;

        /// <summary>Runtime preset id. Builtins use their enum numeric value; custom presets use mod-assigned ids.</summary>
        public int TypeId;

        /// <summary>Semantic preset key from config or mod code.</summary>
        public string TypeKey;

        /// <summary>Required parameter + capability components for this type.</summary>
        public ComponentFlags Components;

        public PhaseFlags ActivePhases;

        public LifetimeFlags AllowedLifetimes;

        /// <summary>
        /// Default handler (builtin or graph) for each active phase's Main slot.
        /// If a phase has no entry (Kind=None), the Main slot is empty
        /// (Pre/Post user graphs can still run).
        /// </summary>
        public PhaseHandlerMap DefaultPhaseHandlers;

        public bool HasComponent(ComponentFlags flag) => (Components & flag) != 0;

        public bool HasPhase(EffectPhaseId phase) => ActivePhases.Has(phase);

        public bool AllowsLifetime(EffectLifetimeKind kind) => AllowedLifetimes.Allows(kind);
    }
}
