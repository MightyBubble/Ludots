using Arch.Core;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.Tests.Networking;

internal sealed class TrackingAuthoritativeReplicationSeatRuntimeFactory : IAuthoritativeReplicationSeatRuntimeFactory
{
    private readonly Func<SessionSeatBinding, Entity, AuthoritativeReplicationSeatRuntime?> _acquire;
    private readonly Func<SessionSeatBinding, AuthoritativeReplicationSeatRuntime, bool>? _release;

    public TrackingAuthoritativeReplicationSeatRuntimeFactory(
        int seatCapacity,
        int globalEntityCapacity,
        int replicationEntityCapacityPerSeat,
        Func<SessionSeatBinding, Entity, AuthoritativeReplicationSeatRuntime?> acquire,
        Func<SessionSeatBinding, AuthoritativeReplicationSeatRuntime, bool>? release = null)
    {
        SeatCapacity = seatCapacity;
        GlobalEntityCapacity = globalEntityCapacity;
        ReplicationEntityCapacityPerSeat = replicationEntityCapacityPerSeat;
        _acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
        _release = release;
    }

    public int SeatCapacity { get; }
    public int GlobalEntityCapacity { get; }
    public int ReplicationEntityCapacityPerSeat { get; }
    public int AcquireCount { get; private set; }
    public int ReleaseCount { get; private set; }
    public SessionSeatBinding LastAcquiredSeat { get; private set; }
    public SessionSeatBinding LastReleasedSeat { get; private set; }
    public Entity LastViewer { get; private set; }
    public AuthoritativeReplicationSeatRuntime? LastAcquiredRuntime { get; private set; }
    public AuthoritativeReplicationSeatRuntime? LastReleasedRuntime { get; private set; }

    public bool TryAcquire(
        in SessionSeatBinding seat,
        Entity viewer,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AuthoritativeReplicationSeatRuntime? runtime)
    {
        AcquireCount++;
        LastAcquiredSeat = seat;
        LastViewer = viewer;
        runtime = _acquire(seat, viewer);
        LastAcquiredRuntime = runtime;
        return runtime != null;
    }

    public bool TryRelease(
        in SessionSeatBinding seat,
        AuthoritativeReplicationSeatRuntime runtime)
    {
        ReleaseCount++;
        LastReleasedSeat = seat;
        LastReleasedRuntime = runtime;
        return _release?.Invoke(seat, runtime) ?? true;
    }
}
