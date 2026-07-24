using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Rejects equal/older remote sample pushes. Distinct from capacity overflow.
/// </summary>
public sealed class Physics3DNetTemporalOrderException : InvalidOperationException
{
    public Physics3DNetTemporalOrderException(int networkEntityId, long newestTick, long attemptedTick)
        : base(
            $"Remote interpolation push for entity {networkEntityId} violates temporal order. "
            + $"Newest tick {newestTick}, attempted tick {attemptedTick}.")
    {
        NetworkEntityId = networkEntityId;
        NewestTick = newestTick;
        AttemptedTick = attemptedTick;
    }

    public int NetworkEntityId { get; }
    public long NewestTick { get; }
    public long AttemptedTick { get; }
}
