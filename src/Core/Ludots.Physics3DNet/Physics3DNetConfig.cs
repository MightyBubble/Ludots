using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Authoritative networking config for the Physics3D vertical slice.
/// AuthoritativeHz is a hard 30Hz contract. SnapshotHz must be an integer divisor of 30.
/// </summary>
public sealed class Physics3DNetConfig
{
    public const int DefaultAuthoritativeHz = 30;
    public const int DefaultPlayerCapacity = 150;

    public int AuthoritativeHz { get; init; } = DefaultAuthoritativeHz;
    public int SnapshotHz { get; init; } = 10;
    public int PlayerCapacity { get; init; } = DefaultPlayerCapacity;
    public int InputHistoryTicksPerPlayer { get; init; } = 64;
    public int MaxFutureInputTicks { get; init; } = 8;
    public int SnapshotEntityCapacity { get; init; } = 4096;
    public int AoiEntityCapacityPerClient { get; init; } = 512;
    public int LocalPredictionHistoryTicks { get; init; } = 32;
    public int RemoteInterpolationHistoryTicks { get; init; } = 16;
    public int ReplayEventCapacity { get; init; } = 8192;
    public int ClientCapacity { get; init; } = DefaultPlayerCapacity;

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

        RequirePositive(PlayerCapacity, nameof(PlayerCapacity));
        RequirePositive(ClientCapacity, nameof(ClientCapacity));
        RequirePositive(InputHistoryTicksPerPlayer, nameof(InputHistoryTicksPerPlayer));
        RequirePositive(MaxFutureInputTicks, nameof(MaxFutureInputTicks));

        // History must cover the full future acceptance window plus at least one committed/history cell.
        int minimumHistory = MaxFutureInputTicks + 1;
        if (InputHistoryTicksPerPlayer < minimumHistory)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InputHistoryTicksPerPlayer),
                InputHistoryTicksPerPlayer,
                $"InputHistoryTicksPerPlayer must be at least MaxFutureInputTicks + 1 ({minimumHistory}).");
        }

        RequirePositive(SnapshotEntityCapacity, nameof(SnapshotEntityCapacity));
        RequirePositive(AoiEntityCapacityPerClient, nameof(AoiEntityCapacityPerClient));
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
