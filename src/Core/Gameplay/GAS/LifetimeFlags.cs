using System;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Bit flags for allowed <see cref="EffectLifetimeKind"/> values.
    /// Used by <see cref="PresetTypeDefinition"/> to constrain which lifetime kinds are valid for a preset type.
    /// </summary>
    [Flags]
    public enum LifetimeFlags : byte
    {
        None = 0,
        Instant         = 1 << 0,
        After           = 1 << 1,
        Infinite        = 1 << 2,

        // ── Common combinations ──

        /// <summary>Only instant effects.</summary>
        InstantOnly = Instant,
        /// <summary>Duration-based effects (After or Infinite).</summary>
        Duration = After | Infinite,
        /// <summary>All lifetime kinds.</summary>
        All = Instant | After | Infinite,
    }

    public static class LifetimeFlagsExtensions
    {
        /// <summary>Check if a specific lifetime kind is allowed.</summary>
        public static bool Allows(this LifetimeFlags flags, EffectLifetimeKind kind)
        {
            return (flags & (LifetimeFlags)(1 << (int)kind)) != 0;
        }

        /// <summary>Convert an EffectLifetimeKind to its corresponding flag bit.</summary>
        public static LifetimeFlags ToFlag(this EffectLifetimeKind kind)
        {
            return (LifetimeFlags)(1 << (int)kind);
        }
    }
}
