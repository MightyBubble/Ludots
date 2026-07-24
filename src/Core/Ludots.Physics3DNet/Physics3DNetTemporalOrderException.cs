using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Rejects equal/older remote sample pushes. Distinct from capacity overflow.
/// </summary>
public sealed class Physics3DNetTemporalOrderException : InvalidOperationException
{
    public Physics3DNetTemporalOrderException(int networkEntitySlot, long newestTick, long attemptedTick)
        : base(
            $"Remote interpolation push for entity slot {networkEntitySlot} violates temporal order. "
            + $"Newest tick {newestTick}, attempted tick {attemptedTick}.")
    {
        NetworkEntitySlot = networkEntitySlot;
        NewestTick = newestTick;
        AttemptedTick = attemptedTick;
    }

    public int NetworkEntitySlot { get; }
    public long NewestTick { get; }
    public long AttemptedTick { get; }
}
