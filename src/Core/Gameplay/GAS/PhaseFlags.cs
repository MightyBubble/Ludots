using System;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Bit flags for active <see cref="EffectPhaseId"/> phases.
    /// Used by <see cref="PresetTypeDefinition"/> to declare which phases fire for a preset type.
    /// </summary>
    [Flags]
    public enum PhaseFlags : ushort
    {
        None = 0,
        OnPropose   = 1 << 0,
        OnCalculate = 1 << 1,
        OnResolve   = 1 << 2,
        OnHit       = 1 << 3,
        OnApply     = 1 << 4,
        OnPeriod    = 1 << 5,
        OnExpire    = 1 << 6,
        OnRemove    = 1 << 7,

        // ── Common combinations ──

        /// <summary>Phases for instant effects (no period/expire/remove).</summary>
        InstantCore = OnPropose | OnCalculate | OnHit | OnApply,
        /// <summary>All phases up to OnApply, including OnResolve.</summary>
        InstantWithResolve = OnPropose | OnCalculate | OnResolve | OnHit | OnApply,
        /// <summary>Full lifecycle for duration effects.</summary>
        DurationFull = OnPropose | OnCalculate | OnHit | OnApply | OnPeriod | OnExpire | OnRemove,
        /// <summary>Duration with resolve (periodic search).</summary>
        DurationWithResolve = OnPropose | OnCalculate | OnResolve | OnHit | OnApply | OnPeriod | OnExpire | OnRemove,
    }

    public static class PhaseFlagsExtensions
    {
        /// <summary>Check if a specific phase is active.</summary>
        public static bool Has(this PhaseFlags flags, EffectPhaseId phase)
        {
            return (flags & (PhaseFlags)(1 << (int)phase)) != 0;
        }

        /// <summary>Convert an EffectPhaseId to its corresponding flag bit.</summary>
        public static PhaseFlags ToFlag(this EffectPhaseId phase)
        {
            return (PhaseFlags)(1 << (int)phase);
        }
    }
}
