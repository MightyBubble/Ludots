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

    /// <summary>Fixed UDP receive buffer bytes per transport instance.</summary>
    public int DatagramReceiveBufferBytes { get; init; } = 2048;

    /// <summary>Fixed UDP send buffer bytes per transport instance.</summary>
    public int DatagramSendBufferBytes { get; init; } = 2048;

    /// <summary>Maximum encoded datagram payload accepted by codec and transport.</summary>
    public int MaxDatagramPayloadBytes { get; init; } = 1200;

    /// <summary>Remote endpoint / client registry capacity owned by transport and server pump.</summary>
    public int TransportEndpointCapacity { get; init; } = DefaultPlayerCapacity;

    /// <summary>Entities packed into one snapshot datagram before fragmentation.</summary>
    public int SnapshotEntitiesPerDatagram { get; init; } = 8;

    /// <summary>ACK bitfield window size in bits for reliable-control sequences.</summary>
    public int ReliableAckBitfieldBits { get; init; } = 32;

    /// <summary>Pending reliable-control outbound slots per endpoint.</summary>
    public int ReliablePendingCapacity { get; init; } = 64;

    /// <summary>Maximum retransmit attempts before explicit retry exhaustion / disconnect.</summary>
    public int ReliableMaxRetries { get; init; } = 8;

    /// <summary>Authoritative ticks between reliable-control retransmit attempts.</summary>
    public int ReliableRetransmitIntervalTicks { get; init; } = 2;

    /// <summary>Maximum UTF-8 bytes for each compatibility fingerprint string field in handshake packets.</summary>
    public int HandshakeFingerprintStringCapacityBytes { get; init; } = 64;

    /// <summary>
    /// Explicit missing-input policy. Never invents or reuses prior input frames.
    /// </summary>
    public Physics3DNetMissingInputPolicy MissingInputPolicy { get; init; } =
        Physics3DNetMissingInputPolicy.HoldTick;

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
        RequirePositive(DatagramReceiveBufferBytes, nameof(DatagramReceiveBufferBytes));
        RequirePositive(DatagramSendBufferBytes, nameof(DatagramSendBufferBytes));
        RequirePositive(MaxDatagramPayloadBytes, nameof(MaxDatagramPayloadBytes));
        RequirePositive(TransportEndpointCapacity, nameof(TransportEndpointCapacity));
        RequirePositive(SnapshotEntitiesPerDatagram, nameof(SnapshotEntitiesPerDatagram));
        RequirePositive(ReliableAckBitfieldBits, nameof(ReliableAckBitfieldBits));
        if (ReliableAckBitfieldBits > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReliableAckBitfieldBits),
                ReliableAckBitfieldBits,
                "Reliable ACK bitfield is packed into a 32-bit mask.");
        }

        RequirePositive(ReliablePendingCapacity, nameof(ReliablePendingCapacity));
        RequirePositive(ReliableMaxRetries, nameof(ReliableMaxRetries));
        RequirePositive(ReliableRetransmitIntervalTicks, nameof(ReliableRetransmitIntervalTicks));
        RequirePositive(HandshakeFingerprintStringCapacityBytes, nameof(HandshakeFingerprintStringCapacityBytes));

        if (MissingInputPolicy is not (Physics3DNetMissingInputPolicy.HoldTick
            or Physics3DNetMissingInputPolicy.FailExplicit))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MissingInputPolicy),
                MissingInputPolicy,
                "Missing-input policy must be HoldTick or FailExplicit; silent input reuse is forbidden.");
        }

        if (DatagramReceiveBufferBytes < MaxDatagramPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DatagramReceiveBufferBytes),
                DatagramReceiveBufferBytes,
                $"Receive buffer must cover MaxDatagramPayloadBytes ({MaxDatagramPayloadBytes}).");
        }

        if (DatagramSendBufferBytes < MaxDatagramPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DatagramSendBufferBytes),
                DatagramSendBufferBytes,
                $"Send buffer must cover MaxDatagramPayloadBytes ({MaxDatagramPayloadBytes}).");
        }

        if (TransportEndpointCapacity < ClientCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TransportEndpointCapacity),
                TransportEndpointCapacity,
                $"Transport endpoint capacity must cover ClientCapacity ({ClientCapacity}).");
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value must be greater than zero.");
        }
    }
}
