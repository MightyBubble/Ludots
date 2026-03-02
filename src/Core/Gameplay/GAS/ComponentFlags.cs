using System;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Bit flags for parameter components and capability components
    /// declared by a <see cref="PresetTypeDefinition"/>.
    /// Each flag corresponds to a JSON config block and/or a runtime data section.
    /// </summary>
    [Flags]
    public enum ComponentFlags : uint
    {
        None = 0,

        // ── Parameter components (define JSON data fields and _ep.* keys) ──

        /// <summary>Attribute modifier list (JSON: "modifiers").</summary>
        ModifierParams = 1 << 0,
        /// <summary>Duration, period, clock (JSON: "duration").</summary>
        DurationParams = 1 << 1,
        /// <summary>Target query strategy (JSON: "targetQuery").</summary>
        TargetQueryParams = 1 << 2,
        /// <summary>Target filter conditions (JSON: "targetFilter").</summary>
        TargetFilterParams = 1 << 3,
        /// <summary>Target dispatch configuration (JSON: "targetDispatch").</summary>
        TargetDispatchParams = 1 << 4,
        /// <summary>2D physics force (JSON: configParams _ep.forceX/Y).</summary>
        ForceParams = 1 << 5,
        /// <summary>Projectile parameters (JSON: "projectile").</summary>
        ProjectileParams = 1 << 6,
        /// <summary>Unit creation parameters (JSON: "unitCreation").</summary>
        UnitCreationParams = 1 << 7,
        /// <summary>Displacement parameters (JSON: "displacement").</summary>
        DisplacementParams = 1 << 8,

        // ── Capability components (declare structural abilities) ──

        /// <summary>Allows per-template Pre/Post Graph bindings, constrained by activePhases.</summary>
        PhaseGraphBindings = 1 << 16,
        /// <summary>Allows registering reactive phase listeners. Non-Instant lifetime only.</summary>
        PhaseListenerSetup = 1 << 17,
    }
}
