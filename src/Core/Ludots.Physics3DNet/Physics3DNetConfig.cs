using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Physics3DNet client/local contract: hard 30Hz authority, snapshot divisor, and bounded
/// prediction / interpolation / replay capacities. Authoritative tick, fixed input, AOI, and
/// replication capacities live in Core Networking / Physics3D network bridge config.
/// </summary>
public sealed class Physics3DNetConfig
{
    public const int DefaultAuthoritativeHz = 30;

    public int AuthoritativeHz { get; init; } = DefaultAuthoritativeHz;
    public int SnapshotHz { get; init; } = 10;
    public int LocalPredictionHistoryTicks { get; init; } = 32;
    public int RemoteInterpolationHistoryTicks { get; init; } = 16;
    public int ReplayEventCapacity { get; init; } = 8192;

    public int SnapshotIntervalTicks
    {
        get
        {
            Validate();
            return AuthoritativeHz / SnapshotHz;
        }
    }

    public void Validate()
    {
        if (AuthoritativeHz != DefaultAuthoritativeHz)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AuthoritativeHz),
                AuthoritativeHz,
                $"AuthoritativeHz is a hard {DefaultAuthoritativeHz}Hz contract for Physics3DNet.");
        }

        RequirePositive(SnapshotHz, nameof(SnapshotHz));
        if (AuthoritativeHz % SnapshotHz != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SnapshotHz),
                SnapshotHz,
                $"SnapshotHz must be an integer divisor of AuthoritativeHz ({AuthoritativeHz}).");
        }

        RequirePositive(LocalPredictionHistoryTicks, nameof(LocalPredictionHistoryTicks));
        RequirePositive(RemoteInterpolationHistoryTicks, nameof(RemoteInterpolationHistoryTicks));
        RequirePositive(ReplayEventCapacity, nameof(ReplayEventCapacity));
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value must be greater than zero.");
        }
    }
}
