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

        InstantOnly = Instant,
        Duration = After | Infinite,
        All = Instant | After | Infinite,
    }

    public static class LifetimeFlagsExtensions
    {
        public static bool Allows(this LifetimeFlags flags, EffectLifetimeKind kind)
        {
            return (flags & (LifetimeFlags)(1 << (int)kind)) != 0;
        }

        /// <summary>Flag bit is 1 &lt;&lt; (int)kind; requires the kind's numeric value to be its bit position.</summary>
        public static LifetimeFlags ToFlag(this EffectLifetimeKind kind)
        {
            return (LifetimeFlags)(1 << (int)kind);
        }
    }
}
