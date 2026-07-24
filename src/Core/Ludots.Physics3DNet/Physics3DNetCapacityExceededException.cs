using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Fixed-capacity overflow for Physics3D networking buffers.
/// Failures are explicit: no silent truncation or defaulting.
/// </summary>
public sealed class Physics3DNetCapacityExceededException : InvalidOperationException
{
    public Physics3DNetCapacityExceededException(string resource, int capacity, long tick)
        : base($"Physics3DNet capacity exceeded for '{resource}' (capacity: {capacity}, tick: {tick}).")
    {
        Resource = resource;
        Capacity = capacity;
        Tick = tick;
    }

    public string Resource { get; }
    public int Capacity { get; }
    public long Tick { get; }
}
