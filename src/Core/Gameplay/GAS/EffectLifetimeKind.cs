namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Core lifetime kinds for effects. Tag-driven expiration is handled
    /// orthogonally via <see cref="GasConditionHandle"/> on the effect entity,
    /// not as a lifetime kind.
    /// </summary>
    public enum EffectLifetimeKind : byte
    {
        /// <summary>Processed and destroyed in the same frame.</summary>
        Instant = 0,
        /// <summary>Expires after N ticks (duration-based).</summary>
        After = 1,
        /// <summary>Never expires on its own; must be explicitly removed.</summary>
        Infinite = 2,
    }
}
