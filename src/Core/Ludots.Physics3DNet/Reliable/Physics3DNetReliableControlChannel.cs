using System;

namespace Ludots.Core.Physics3DNet;

public enum Physics3DNetReliableDeliveryResult : byte
{
    Accepted = 1,
    DuplicateSuppressed = 2
}

public readonly struct Physics3DNetReliableReceiveResult
{
    public Physics3DNetReliableReceiveResult(
        Physics3DNetReliableDeliveryResult delivery,
        uint sequence,
        Physics3DNetReliableControlOpcode opcode,
        int payloadLength)
    {
        Delivery = delivery;
        Sequence = sequence;
        Opcode = opcode;
        PayloadLength = payloadLength;
    }

    public Physics3DNetReliableDeliveryResult Delivery { get; }
    public uint Sequence { get; }
    public Physics3DNetReliableControlOpcode Opcode { get; }
    public int PayloadLength { get; }
}

public readonly struct Physics3DNetReliableChannelTickResult
{
    public Physics3DNetReliableChannelTickResult(
        int retransmitCount,
        bool retryExhausted,
        uint exhaustedSequence)
    {
        RetransmitCount = retransmitCount;
        RetryExhausted = retryExhausted;
        ExhaustedSequence = exhaustedSequence;
    }

    public int RetransmitCount { get; }
    public bool RetryExhausted { get; }
    public uint ExhaustedSequence { get; }
}

public interface IPhysics3DNetReliableDatagramSender
{
    void SendReliableDatagram(ReadOnlySpan<byte> datagram);
}

/// <summary>
/// Bounded sequence / ACK-bitfield reliable channel for control messages only.
/// Physics snapshots remain sequenced unreliable and never enter this channel.
/// </summary>
public sealed class Physics3DNetReliableControlChannel
{
    private readonly Physics3DNetPacketCodec _codec;
    private readonly int _ackBits;
    private readonly int _pendingCapacity;
    private readonly int _maxRetries;
    private readonly int _retransmitIntervalTicks;
    private readonly int _maxPayloadBytes;

    private readonly uint[] _pendingSequence;
    private readonly Physics3DNetReliableControlOpcode[] _pendingOpcode;
    private readonly ushort[] _pendingPayloadLength;
    private readonly byte[] _pendingPayloadStorage;
    private readonly int[] _pendingRetries;
    private readonly long[] _pendingNextRetransmitTick;
    private readonly bool[] _pendingOccupied;

    private uint _receivedAckBits;
    private uint _nextSendSequence = 1;
    private uint _latestReceivedSequence;
    private bool _hasReceived;
    private bool _terminalExhausted;
    private uint _exhaustedSequence;
    private int _retransmitScanIndex;

    public Physics3DNetReliableControlChannel(Physics3DNetConfig config, Physics3DNetPacketCodec codec)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(codec);
        config.Validate();

        _codec = codec;
        _ackBits = config.ReliableAckBitfieldBits;
        _pendingCapacity = config.ReliablePendingCapacity;
        _maxRetries = config.ReliableMaxRetries;
        _retransmitIntervalTicks = config.ReliableRetransmitIntervalTicks;
        _maxPayloadBytes = checked(
            config.MaxDatagramPayloadBytes
            - Physics3DNetPacketCodec.HeaderByteCount
            - Physics3DNetPacketCodec.ReliableControlFixedPayloadByteCount);
        if (_maxPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                config.MaxDatagramPayloadBytes,
                "MaxDatagramPayloadBytes is too small for reliable-control framing.");
        }

        _pendingSequence = new uint[_pendingCapacity];
        _pendingOpcode = new Physics3DNetReliableControlOpcode[_pendingCapacity];
        _pendingPayloadLength = new ushort[_pendingCapacity];
        _pendingPayloadStorage = new byte[checked(_pendingCapacity * _maxPayloadBytes)];
        _pendingRetries = new int[_pendingCapacity];
        _pendingNextRetransmitTick = new long[_pendingCapacity];
        _pendingOccupied = new bool[_pendingCapacity];
    }

    public uint NextSendSequence => _nextSendSequence;
    public uint LatestReceivedSequence => _latestReceivedSequence;
    public bool IsTerminalExhausted => _terminalExhausted;
    public uint ExhaustedSequence => _exhaustedSequence;

    public int PendingCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _pendingCapacity; i++)
            {
                if (_pendingOccupied[i])
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int EncodeAndQueue(
        uint sessionId,
        Physics3DNetReliableControlOpcode opcode,
        ReadOnlySpan<byte> payload,
        long currentAuthoritativeTick,
        Span<byte> datagramDestination,
        out uint sequence)
    {
        EnsureNotTerminal();
        if (payload.Length > _maxPayloadBytes)
        {
            throw new Physics3DNetCapacityExceededException(
                "reliable-control payload",
                _maxPayloadBytes,
                currentAuthoritativeTick);
        }

        int freeSlot = FindFreePendingSlot();
        if (freeSlot < 0)
        {
            throw new Physics3DNetCapacityExceededException(
                "reliable-control pending",
                _pendingCapacity,
                currentAuthoritativeTick);
        }

        sequence = AllocateSendSequence();
        int encoded = _codec.EncodeReliableControl(sessionId, sequence, opcode, payload, datagramDestination);

        _pendingSequence[freeSlot] = sequence;
        _pendingOpcode[freeSlot] = opcode;
        _pendingPayloadLength[freeSlot] = (ushort)payload.Length;
        payload.CopyTo(PendingPayloadSlot(freeSlot));
        _pendingRetries[freeSlot] = 0;
        _pendingNextRetransmitTick[freeSlot] = currentAuthoritativeTick + _retransmitIntervalTicks;
        _pendingOccupied[freeSlot] = true;
        return encoded;
    }

    public Physics3DNetReliableReceiveResult ReceiveControlDatagram(
        ReadOnlySpan<byte> datagram,
        Span<byte> payloadDestination,
        out int payloadLength)
    {
        EnsureNotTerminal();
        _codec.DecodeReliableControlFields(
            datagram,
            payloadDestination,
            out _,
            out uint sequence,
            out Physics3DNetReliableControlOpcode opcode,
            out payloadLength);

        if (sequence == 0)
        {
            throw new Physics3DNetPacketMalformedException("Reliable-control sequence 0 is reserved.");
        }

        if (IsDuplicate(sequence))
        {
            return new Physics3DNetReliableReceiveResult(
                Physics3DNetReliableDeliveryResult.DuplicateSuppressed,
                sequence,
                opcode,
                payloadLength);
        }

        MarkReceived(sequence);
        return new Physics3DNetReliableReceiveResult(
            Physics3DNetReliableDeliveryResult.Accepted,
            sequence,
            opcode,
            payloadLength);
    }

    public Physics3DNetAcknowledgementPacket BuildAcknowledgement(uint sessionId) =>
        new(sessionId, _hasReceived ? _latestReceivedSequence : 0u, _receivedAckBits);

    public int ApplyAcknowledgement(in Physics3DNetAcknowledgementPacket acknowledgement)
    {
        EnsureNotTerminal();
        int cleared = 0;
        for (int i = 0; i < _pendingCapacity; i++)
        {
            if (!_pendingOccupied[i])
            {
                continue;
            }

            if (IsAcknowledged(_pendingSequence[i], acknowledgement.AckSequence, acknowledgement.AckBits))
            {
                _pendingOccupied[i] = false;
                cleared++;
            }
        }

        return cleared;
    }

    public Physics3DNetReliableChannelTickResult ProcessRetransmits(
        uint sessionId,
        long currentAuthoritativeTick,
        Span<byte> datagramDestination,
        IPhysics3DNetReliableDatagramSender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        EnsureNotTerminal();

        int retransmits = 0;
        for (int pass = 0; pass < _pendingCapacity; pass++)
        {
            int i = _retransmitScanIndex;
            _retransmitScanIndex++;
            if (_retransmitScanIndex >= _pendingCapacity)
            {
                _retransmitScanIndex = 0;
            }

            if (!_pendingOccupied[i])
            {
                continue;
            }

            if (currentAuthoritativeTick < _pendingNextRetransmitTick[i])
            {
                continue;
            }

            if (_pendingRetries[i] >= _maxRetries)
            {
                _terminalExhausted = true;
                _exhaustedSequence = _pendingSequence[i];
                return new Physics3DNetReliableChannelTickResult(
                    retransmits,
                    retryExhausted: true,
                    _exhaustedSequence);
            }

            int payloadLength = _pendingPayloadLength[i];
            int encoded = _codec.EncodeReliableControl(
                sessionId,
                _pendingSequence[i],
                _pendingOpcode[i],
                PendingPayloadSlot(i).Slice(0, payloadLength),
                datagramDestination);
            sender.SendReliableDatagram(datagramDestination.Slice(0, encoded));
            _pendingRetries[i]++;
            _pendingNextRetransmitTick[i] = currentAuthoritativeTick + _retransmitIntervalTicks;
            retransmits++;
        }

        return new Physics3DNetReliableChannelTickResult(retransmits, retryExhausted: false, exhaustedSequence: 0);
    }

    public static bool IsSequenceNewer(uint candidate, uint current) =>
        unchecked((int)(candidate - current)) > 0;

    public static bool IsAcknowledged(uint sequence, uint ackSequence, uint ackBits)
    {
        if (sequence == 0 || ackSequence == 0)
        {
            return false;
        }

        if (sequence == ackSequence)
        {
            return true;
        }

        uint distance = unchecked(ackSequence - sequence);
        if (distance == 0 || distance > 32)
        {
            return false;
        }

        uint bitIndex = distance - 1;
        return ((ackBits >> (int)bitIndex) & 1u) != 0;
    }

    private uint AllocateSendSequence()
    {
        uint sequence = _nextSendSequence++;
        if (sequence == 0)
        {
            sequence = _nextSendSequence++;
        }

        return sequence;
    }

    private int FindFreePendingSlot()
    {
        for (int i = 0; i < _pendingCapacity; i++)
        {
            if (!_pendingOccupied[i])
            {
                return i;
            }
        }

        return -1;
    }

    private Span<byte> PendingPayloadSlot(int slot) =>
        _pendingPayloadStorage.AsSpan(slot * _maxPayloadBytes, _maxPayloadBytes);

    private bool IsDuplicate(uint sequence)
    {
        if (!_hasReceived)
        {
            return false;
        }

        if (sequence == _latestReceivedSequence)
        {
            return true;
        }

        if (IsSequenceNewer(sequence, _latestReceivedSequence))
        {
            return false;
        }

        uint distance = unchecked(_latestReceivedSequence - sequence);
        if (distance == 0 || distance > (uint)_ackBits)
        {
            return true;
        }

        uint bitIndex = distance - 1;
        return ((_receivedAckBits >> (int)bitIndex) & 1u) != 0;
    }

    private void MarkReceived(uint sequence)
    {
        if (!_hasReceived)
        {
            _latestReceivedSequence = sequence;
            _receivedAckBits = 0;
            _hasReceived = true;
            return;
        }

        if (IsSequenceNewer(sequence, _latestReceivedSequence))
        {
            uint advance = unchecked(sequence - _latestReceivedSequence);
            if (advance >= 32)
            {
                _receivedAckBits = 0;
            }
            else
            {
                // Previous latest becomes bit0 after one step; shift left by (advance-1) then set bit0.
                // Example: advance=1 => bits <<= 1; bit0 = 1 (old latest)
                // advance=2 => bits <<= 2; bit0 = 0 (hole), bit1 = 1 (old latest)
                _receivedAckBits <<= (int)advance;
                _receivedAckBits |= 1u << (int)(advance - 1);
                if (_ackBits < 32)
                {
                    uint keepMask = _ackBits == 32 ? uint.MaxValue : (1u << _ackBits) - 1u;
                    _receivedAckBits &= keepMask;
                }
            }

            _latestReceivedSequence = sequence;
            return;
        }

        uint distance = unchecked(_latestReceivedSequence - sequence);
        if (distance > 0 && distance <= (uint)_ackBits)
        {
            _receivedAckBits |= 1u << (int)(distance - 1);
        }
    }

    private void EnsureNotTerminal()
    {
        if (_terminalExhausted)
        {
            throw new InvalidOperationException(
                $"Reliable-control channel is terminal after retry exhaustion on sequence {_exhaustedSequence}.");
        }
    }
}
