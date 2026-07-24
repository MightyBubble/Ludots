using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Thrown when a registered player is missing input for an execute gate or post-commit acknowledgement.
/// Lifecycle authority and confirmation cells remain unchanged on the failing call.
/// </summary>
public sealed class Physics3DNetMissingInputException : InvalidOperationException
{
    public Physics3DNetMissingInputException(long tick, int missingCount)
        : base($"Cannot proceed for tick {tick}: {missingCount} registered player(s) are missing input.")
    {
        Tick = tick;
        MissingCount = missingCount;
    }

    public long Tick { get; }
    public int MissingCount { get; }
}
