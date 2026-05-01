namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Marks effects that need periodic lifetime ticks.
    /// </summary>
    public readonly struct EffectPeriodicTick
    {
    }

    /// <summary>
    /// Marks effects that need expiration or cancellation checks.
    /// </summary>
    public readonly struct EffectExpirationCheck
    {
    }
}
