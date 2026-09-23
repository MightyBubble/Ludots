using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet;

public enum Physics3DNetPacketType : byte
{
    Handshake = 1,
    ClientInput = 2,
    SnapshotPayload = 3,
    Acknowledgement = 4,
    ReliableControl = 5
}

public enum Physics3DNetReliableControlOpcode : byte
{
    SessionAccept = 1,
    SessionReject = 2,
    Disconnect = 3,
    BaselineAck = 4
}

public sealed class Physics3DNetPacketMalformedException : InvalidOperationException
{
    public Physics3DNetPacketMalformedException(string reason)
        : base($"Physics3DNet packet malformed: {reason}")
    {
        Reason = reason;
    }

    public string Reason { get; }
}

public sealed class Physics3DNetPacketVersionException : InvalidOperationException
{
    public Physics3DNetPacketVersionException(byte expected, byte actual)
        : base($"Physics3DNet packet version mismatch. Expected {expected}, got {actual}.")
    {
        Expected = expected;
        Actual = actual;
    }

    public byte Expected { get; }
    public byte Actual { get; }
}

public readonly struct Physics3DNetPacketHeader
{
    public Physics3DNetPacketHeader(byte version, Physics3DNetPacketType packetType, ushort payloadLength)
    {
        Version = version;
        PacketType = packetType;
        PayloadLength = payloadLength;
    }

    public byte Version { get; }
    public Physics3DNetPacketType PacketType { get; }
    public ushort PayloadLength { get; }
}

public readonly struct Physics3DNetHandshakePacket
{
    public Physics3DNetHandshakePacket(
        uint sessionId,
        int networkPlayerId,
        int generation,
        int requestedClientSlot,
        in Physics3DNetCompatibilityFingerprint fingerprint)
    {
        Physics3DNetValidation.RequireNonNegativeId(networkPlayerId, nameof(networkPlayerId));
        Physics3DNetValidation.RequirePositiveGeneration(generation, nameof(generation));
        if (requestedClientSlot < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedClientSlot), requestedClientSlot, "Requested client slot must be >= -1.");
        }

        SessionId = sessionId;
        NetworkPlayerId = networkPlayerId;
        Generation = generation;
        RequestedClientSlot = requestedClientSlot;
        Fingerprint = fingerprint;
    }

    public uint SessionId { get; }
    public int NetworkPlayerId { get; }
    public int Generation { get; }
    public int RequestedClientSlot { get; }
    public Physics3DNetCompatibilityFingerprint Fingerprint { get; }
}

public readonly struct Physics3DNetClientInputPacket
{
    public Physics3DNetClientInputPacket(uint sessionId, in Physics3DNetInputSubmit input)
    {
        SessionId = sessionId;
        Input = input;
    }

    public uint SessionId { get; }
    public Physics3DNetInputSubmit Input { get; }
}

public readonly struct Physics3DNetSnapshotPacketHeader
{
    public Physics3DNetSnapshotPacketHeader(
        uint sessionId,
        long snapshotTick,
        long baselineId,
        uint unreliableSequence,
        ushort fragmentIndex,
        ushort fragmentCount,
        ushort entityCount)
    {
        Physics3DNetValidation.RequirePositiveTick(snapshotTick, nameof(snapshotTick));
        Physics3DNetValidation.RequireNonNegativeBaselineId(baselineId, nameof(baselineId));
        if (fragmentCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fragmentCount), fragmentCount, "Fragment count must be positive.");
        }

        if (fragmentIndex >= fragmentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fragmentIndex),
                fragmentIndex,
                $"Fragment index must be < fragmentCount ({fragmentCount}).");
        }

        SessionId = sessionId;
        SnapshotTick = snapshotTick;
        BaselineId = baselineId;
        UnreliableSequence = unreliableSequence;
        FragmentIndex = fragmentIndex;
        FragmentCount = fragmentCount;
        EntityCount = entityCount;
    }

    public uint SessionId { get; }
    public long SnapshotTick { get; }
    public long BaselineId { get; }
    public uint UnreliableSequence { get; }
    public ushort FragmentIndex { get; }
    public ushort FragmentCount { get; }
    public ushort EntityCount { get; }
}

public readonly struct Physics3DNetAcknowledgementPacket
{
    public Physics3DNetAcknowledgementPacket(uint sessionId, uint ackSequence, uint ackBits)
    {
        SessionId = sessionId;
        AckSequence = ackSequence;
        AckBits = ackBits;
    }

    public uint SessionId { get; }
    public uint AckSequence { get; }
    public uint AckBits { get; }
}

public readonly struct Physics3DNetReliableControlPacket
{
    public Physics3DNetReliableControlPacket(
        uint sessionId,
        uint sequence,
        Physics3DNetReliableControlOpcode opcode,
        ReadOnlyMemory<byte> payload)
    {
        if (opcode is not (Physics3DNetReliableControlOpcode.SessionAccept
            or Physics3DNetReliableControlOpcode.SessionReject
            or Physics3DNetReliableControlOpcode.Disconnect
            or Physics3DNetReliableControlOpcode.BaselineAck))
        {
            throw new ArgumentOutOfRangeException(nameof(opcode), opcode, "Invalid reliable-control opcode.");
        }

        SessionId = sessionId;
        Sequence = sequence;
        Opcode = opcode;
        Payload = payload;
    }

    public uint SessionId { get; }
    public uint Sequence { get; }
    public Physics3DNetReliableControlOpcode Opcode { get; }
    public ReadOnlyMemory<byte> Payload { get; }
}

/// <summary>
/// Versioned little-endian binary packet codec. Hot paths write into caller buffers only.
/// Malformed, version, and capacity failures are explicit; no reflection/JSON.
/// </summary>
public sealed class Physics3DNetPacketCodec
{
    public const ushort Magic = 0x5033; // 'P''3'
    public const byte ProtocolVersion = 1;
    public const int HeaderByteCount = 8;
    public const int SnapshotEntityByteCount = 72;
    public const int SnapshotPayloadHeaderByteCount = 30;
    public const int ClientInputPayloadByteCount = 36;
    public const int AcknowledgementPayloadByteCount = 12;
    public const int ReliableControlFixedPayloadByteCount = 11;

    private readonly int _maxDatagramPayloadBytes;
    private readonly int _fingerprintStringCapacityBytes;

    public Physics3DNetPacketCodec(Physics3DNetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _maxDatagramPayloadBytes = config.MaxDatagramPayloadBytes;
        _fingerprintStringCapacityBytes = config.HandshakeFingerprintStringCapacityBytes;
    }

    public int MaxDatagramPayloadBytes => _maxDatagramPayloadBytes;

    public static int MeasureSnapshotPayloadBytes(int entityCount)
    {
        if (entityCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entityCount));
        }

        return checked(HeaderByteCount + SnapshotPayloadHeaderByteCount + (entityCount * SnapshotEntityByteCount));
    }

    public int EncodeHandshake(in Physics3DNetHandshakePacket packet, Span<byte> destination)
    {
        int fingerprintBytes = MeasureFingerprintBytes(packet.Fingerprint);
        int payloadLength = checked(16 + fingerprintBytes);
        int total = checked(HeaderByteCount + payloadLength);
        EnsureDestinationCapacity(destination, total, tick: 0);

        WriteHeader(destination, Physics3DNetPacketType.Handshake, (ushort)payloadLength);
        Span<byte> payload = destination.Slice(HeaderByteCount, payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), packet.SessionId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(4, 4), packet.NetworkPlayerId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(8, 4), packet.Generation);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(12, 4), packet.RequestedClientSlot);
        WriteFingerprint(payload.Slice(16), packet.Fingerprint);
        return total;
    }

    public Physics3DNetHandshakePacket DecodeHandshake(ReadOnlySpan<byte> datagram)
    {
        Physics3DNetPacketHeader header = DecodeHeader(datagram, Physics3DNetPacketType.Handshake);
        ReadOnlySpan<byte> payload = datagram.Slice(HeaderByteCount, header.PayloadLength);
        if (payload.Length < 16)
        {
            throw new Physics3DNetPacketMalformedException("Handshake payload too short.");
        }

        uint sessionId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
        int networkPlayerId = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
        int generation = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4));
        int requestedClientSlot = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12, 4));
        Physics3DNetCompatibilityFingerprint fingerprint = ReadFingerprint(payload.Slice(16));
        return new Physics3DNetHandshakePacket(sessionId, networkPlayerId, generation, requestedClientSlot, fingerprint);
    }

    public int EncodeClientInput(in Physics3DNetClientInputPacket packet, Span<byte> destination)
    {
        const int payloadLength = ClientInputPayloadByteCount;
        int total = HeaderByteCount + payloadLength;
        EnsureDestinationCapacity(destination, total, packet.Input.Tick);

        WriteHeader(destination, Physics3DNetPacketType.ClientInput, payloadLength);
        Span<byte> payload = destination.Slice(HeaderByteCount, payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), packet.SessionId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(4, 4), packet.Input.NetworkPlayerId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(8, 4), packet.Input.Generation);
        BinaryPrimitives.WriteInt64LittleEndian(payload.Slice(12, 8), packet.Input.Tick);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(20, 4), packet.Input.Sequence);
        BinaryPrimitives.WriteInt16LittleEndian(payload.Slice(24, 2), packet.Input.MoveAxes.X);
        BinaryPrimitives.WriteInt16LittleEndian(payload.Slice(26, 2), packet.Input.MoveAxes.Y);
        BinaryPrimitives.WriteInt16LittleEndian(payload.Slice(28, 2), packet.Input.LookAxes.X);
        BinaryPrimitives.WriteInt16LittleEndian(payload.Slice(30, 2), packet.Input.LookAxes.Y);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(32, 4), packet.Input.Buttons);
        return total;
    }

    public Physics3DNetClientInputPacket DecodeClientInput(ReadOnlySpan<byte> datagram)
    {
        Physics3DNetPacketHeader header = DecodeHeader(datagram, Physics3DNetPacketType.ClientInput);
        if (header.PayloadLength != ClientInputPayloadByteCount)
        {
            throw new Physics3DNetPacketMalformedException(
                $"ClientInput payload length must be {ClientInputPayloadByteCount}, got {header.PayloadLength}.");
        }

        ReadOnlySpan<byte> payload = datagram.Slice(HeaderByteCount, header.PayloadLength);
        uint sessionId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
        var input = new Physics3DNetInputSubmit(
            tick: BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(12, 8)),
            networkPlayerId: BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4)),
            generation: BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)),
            sequence: BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(20, 4)),
            moveAxes: new Physics3DNetQuantizedAxes2(
                BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(24, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(26, 2))),
            lookAxes: new Physics3DNetQuantizedAxes2(
                BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(28, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(30, 2))),
            buttons: BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(32, 4)));
        return new Physics3DNetClientInputPacket(sessionId, input);
    }

    public int EncodeSnapshotPayload(
        in Physics3DNetSnapshotPacketHeader header,
        ReadOnlySpan<Physics3DNetSnapshotEntityWrite> entities,
        Span<byte> destination)
    {
        if (entities.Length != header.EntityCount)
        {
            throw new ArgumentException(
                $"Entity span length {entities.Length} must equal header.EntityCount {header.EntityCount}.",
                nameof(entities));
        }

        int payloadLength = checked(SnapshotPayloadHeaderByteCount + (entities.Length * SnapshotEntityByteCount));
        int total = checked(HeaderByteCount + payloadLength);
        EnsureDestinationCapacity(destination, total, header.SnapshotTick);

        WriteHeader(destination, Physics3DNetPacketType.SnapshotPayload, (ushort)payloadLength);
        Span<byte> payload = destination.Slice(HeaderByteCount, payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), header.SessionId);
        BinaryPrimitives.WriteInt64LittleEndian(payload.Slice(4, 8), header.SnapshotTick);
        BinaryPrimitives.WriteInt64LittleEndian(payload.Slice(12, 8), header.BaselineId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(20, 4), header.UnreliableSequence);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(24, 2), header.FragmentIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(26, 2), header.FragmentCount);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(28, 2), header.EntityCount);

        int offset = SnapshotPayloadHeaderByteCount;
        for (int i = 0; i < entities.Length; i++)
        {
            WriteSnapshotEntity(payload.Slice(offset, SnapshotEntityByteCount), entities[i]);
            offset += SnapshotEntityByteCount;
        }

        return total;
    }

    public Physics3DNetSnapshotPacketHeader DecodeSnapshotPayload(
        ReadOnlySpan<byte> datagram,
        Span<Physics3DNetSnapshotEntityWrite> entityDestination,
        out int entityCount)
    {
        Physics3DNetPacketHeader packetHeader = DecodeHeader(datagram, Physics3DNetPacketType.SnapshotPayload);
        if (packetHeader.PayloadLength < SnapshotPayloadHeaderByteCount)
        {
            throw new Physics3DNetPacketMalformedException("Snapshot payload too short.");
        }

        ReadOnlySpan<byte> payload = datagram.Slice(HeaderByteCount, packetHeader.PayloadLength);
        ushort declaredEntityCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(28, 2));
        int expectedPayload = checked(SnapshotPayloadHeaderByteCount + (declaredEntityCount * SnapshotEntityByteCount));
        if (packetHeader.PayloadLength != expectedPayload)
        {
            throw new Physics3DNetPacketMalformedException(
                $"Snapshot payload length {packetHeader.PayloadLength} does not match entity count {declaredEntityCount}.");
        }

        if (declaredEntityCount > entityDestination.Length)
        {
            throw new Physics3DNetCapacityExceededException(
                "snapshot decode entity destination",
                entityDestination.Length,
                BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(4, 8)));
        }

        var header = new Physics3DNetSnapshotPacketHeader(
            sessionId: BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4)),
            snapshotTick: BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(4, 8)),
            baselineId: BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(12, 8)),
            unreliableSequence: BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(20, 4)),
            fragmentIndex: BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(24, 2)),
            fragmentCount: BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(26, 2)),
            entityCount: declaredEntityCount);

        int offset = SnapshotPayloadHeaderByteCount;
        for (int i = 0; i < declaredEntityCount; i++)
        {
            entityDestination[i] = ReadSnapshotEntity(payload.Slice(offset, SnapshotEntityByteCount));
            offset += SnapshotEntityByteCount;
        }

        entityCount = declaredEntityCount;
        return header;
    }

    public int EncodeAcknowledgement(in Physics3DNetAcknowledgementPacket packet, Span<byte> destination)
    {
        const int payloadLength = AcknowledgementPayloadByteCount;
        int total = HeaderByteCount + payloadLength;
        EnsureDestinationCapacity(destination, total, tick: 0);
        WriteHeader(destination, Physics3DNetPacketType.Acknowledgement, payloadLength);
        Span<byte> payload = destination.Slice(HeaderByteCount, payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), packet.SessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(4, 4), packet.AckSequence);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(8, 4), packet.AckBits);
        return total;
    }

    public Physics3DNetAcknowledgementPacket DecodeAcknowledgement(ReadOnlySpan<byte> datagram)
    {
        Physics3DNetPacketHeader header = DecodeHeader(datagram, Physics3DNetPacketType.Acknowledgement);
        if (header.PayloadLength != AcknowledgementPayloadByteCount)
        {
            throw new Physics3DNetPacketMalformedException(
                $"Acknowledgement payload length must be {AcknowledgementPayloadByteCount}, got {header.PayloadLength}.");
        }

        ReadOnlySpan<byte> payload = datagram.Slice(HeaderByteCount, header.PayloadLength);
        return new Physics3DNetAcknowledgementPacket(
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4)));
    }

    public int EncodeReliableControl(
        in Physics3DNetReliableControlPacket packet,
        Span<byte> destination) =>
        EncodeReliableControl(packet.SessionId, packet.Sequence, packet.Opcode, packet.Payload.Span, destination);

    public int EncodeReliableControl(
        uint sessionId,
        uint sequence,
        Physics3DNetReliableControlOpcode opcode,
        ReadOnlySpan<byte> controlPayload,
        Span<byte> destination)
    {
        if (opcode is not (Physics3DNetReliableControlOpcode.SessionAccept
            or Physics3DNetReliableControlOpcode.SessionReject
            or Physics3DNetReliableControlOpcode.Disconnect
            or Physics3DNetReliableControlOpcode.BaselineAck))
        {
            throw new ArgumentOutOfRangeException(nameof(opcode), opcode, "Invalid reliable-control opcode.");
        }

        if (controlPayload.Length > ushort.MaxValue)
        {
            throw new Physics3DNetCapacityExceededException(
                "reliable-control payload",
                ushort.MaxValue,
                tick: 0);
        }

        int payloadLength = checked(ReliableControlFixedPayloadByteCount + controlPayload.Length);
        int total = checked(HeaderByteCount + payloadLength);
        EnsureDestinationCapacity(destination, total, tick: 0);
        WriteHeader(destination, Physics3DNetPacketType.ReliableControl, (ushort)payloadLength);
        Span<byte> payload = destination.Slice(HeaderByteCount, payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(4, 4), sequence);
        payload[8] = (byte)opcode;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(9, 2), (ushort)controlPayload.Length);
        controlPayload.CopyTo(payload.Slice(ReliableControlFixedPayloadByteCount));
        return total;
    }

    /// <summary>
    /// Allocation-free reliable-control decode into caller-owned payload destination.
    /// Returns header fields via out parameters; payload bytes are copied into <paramref name="payloadDestination"/>.
    /// </summary>
    public void DecodeReliableControlFields(
        ReadOnlySpan<byte> datagram,
        Span<byte> payloadDestination,
        out uint sessionId,
        out uint sequence,
        out Physics3DNetReliableControlOpcode opcode,
        out int payloadLength)
    {
        Physics3DNetPacketHeader header = DecodeHeader(datagram, Physics3DNetPacketType.ReliableControl);
        if (header.PayloadLength < ReliableControlFixedPayloadByteCount)
        {
            throw new Physics3DNetPacketMalformedException("Reliable-control payload too short.");
        }

        ReadOnlySpan<byte> payload = datagram.Slice(HeaderByteCount, header.PayloadLength);
        ushort declaredPayloadLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(9, 2));
        int expected = checked(ReliableControlFixedPayloadByteCount + declaredPayloadLength);
        if (header.PayloadLength != expected)
        {
            throw new Physics3DNetPacketMalformedException(
                $"Reliable-control payload length {header.PayloadLength} does not match declared payload {declaredPayloadLength}.");
        }

        if (declaredPayloadLength > payloadDestination.Length)
        {
            throw new Physics3DNetCapacityExceededException(
                "reliable-control decode payload destination",
                payloadDestination.Length,
                tick: 0);
        }

        sessionId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
        sequence = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
        opcode = (Physics3DNetReliableControlOpcode)payload[8];
        if (opcode is not (Physics3DNetReliableControlOpcode.SessionAccept
            or Physics3DNetReliableControlOpcode.SessionReject
            or Physics3DNetReliableControlOpcode.Disconnect
            or Physics3DNetReliableControlOpcode.BaselineAck))
        {
            throw new Physics3DNetPacketMalformedException($"Invalid reliable-control opcode {(byte)opcode}.");
        }

        payload.Slice(ReliableControlFixedPayloadByteCount, declaredPayloadLength).CopyTo(payloadDestination);
        payloadLength = declaredPayloadLength;
    }

    public Physics3DNetPacketHeader PeekHeader(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < HeaderByteCount)
        {
            throw new Physics3DNetPacketMalformedException("Datagram shorter than packet header.");
        }

        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(0, 2));
        if (magic != Magic)
        {
            throw new Physics3DNetPacketMalformedException($"Invalid magic 0x{magic:X4}.");
        }

        byte version = datagram[2];
        if (version != ProtocolVersion)
        {
            throw new Physics3DNetPacketVersionException(ProtocolVersion, version);
        }

        var packetType = (Physics3DNetPacketType)datagram[3];
        if (packetType is not (Physics3DNetPacketType.Handshake
            or Physics3DNetPacketType.ClientInput
            or Physics3DNetPacketType.SnapshotPayload
            or Physics3DNetPacketType.Acknowledgement
            or Physics3DNetPacketType.ReliableControl))
        {
            throw new Physics3DNetPacketMalformedException($"Unknown packet type {(byte)packetType}.");
        }

        ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(4, 2));
        int total = checked(HeaderByteCount + payloadLength);
        if (datagram.Length != total)
        {
            throw new Physics3DNetPacketMalformedException(
                $"Datagram length {datagram.Length} does not match header+payload {total}.");
        }

        if (total > _maxDatagramPayloadBytes)
        {
            throw new Physics3DNetCapacityExceededException(
                "datagram payload",
                _maxDatagramPayloadBytes,
                tick: 0);
        }

        return new Physics3DNetPacketHeader(version, packetType, payloadLength);
    }

    private Physics3DNetPacketHeader DecodeHeader(ReadOnlySpan<byte> datagram, Physics3DNetPacketType expectedType)
    {
        Physics3DNetPacketHeader header = PeekHeader(datagram);
        if (header.PacketType != expectedType)
        {
            throw new Physics3DNetPacketMalformedException(
                $"Expected packet type {expectedType}, got {header.PacketType}.");
        }

        return header;
    }

    private void WriteHeader(Span<byte> destination, Physics3DNetPacketType packetType, ushort payloadLength)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), Magic);
        destination[2] = ProtocolVersion;
        destination[3] = (byte)packetType;
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), payloadLength);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), 0);
    }

    private void EnsureDestinationCapacity(Span<byte> destination, int requiredBytes, long tick)
    {
        if (requiredBytes > _maxDatagramPayloadBytes)
        {
            throw new Physics3DNetCapacityExceededException("datagram payload", _maxDatagramPayloadBytes, tick);
        }

        if (destination.Length < requiredBytes)
        {
            throw new Physics3DNetCapacityExceededException("codec destination", destination.Length, tick);
        }
    }

    private int MeasureFingerprintBytes(in Physics3DNetCompatibilityFingerprint fingerprint)
    {
        int build = MeasureUtf8(fingerprint.BuildId);
        int kernel = MeasureUtf8(fingerprint.KernelId);
        int simd = MeasureUtf8(fingerprint.SimdProfile);
        int scenario = MeasureUtf8(fingerprint.ScenarioId);
        return checked(2 + build + 8 + 2 + kernel + 2 + simd + 4 + 2 + scenario);
    }

    private int MeasureUtf8(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > _fingerprintStringCapacityBytes)
        {
            throw new Physics3DNetCapacityExceededException(
                "handshake fingerprint string",
                _fingerprintStringCapacityBytes,
                tick: 0);
        }

        return byteCount;
    }

    private void WriteFingerprint(Span<byte> destination, in Physics3DNetCompatibilityFingerprint fingerprint)
    {
        int offset = 0;
        offset += WriteBoundedUtf8(destination.Slice(offset), fingerprint.BuildId);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(offset, 8), fingerprint.ConfigHash);
        offset += 8;
        offset += WriteBoundedUtf8(destination.Slice(offset), fingerprint.KernelId);
        offset += WriteBoundedUtf8(destination.Slice(offset), fingerprint.SimdProfile);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), fingerprint.WorkerCount);
        offset += 4;
        WriteBoundedUtf8(destination.Slice(offset), fingerprint.ScenarioId);
    }

    private int WriteBoundedUtf8(Span<byte> destination, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > _fingerprintStringCapacityBytes)
        {
            throw new Physics3DNetCapacityExceededException(
                "handshake fingerprint string",
                _fingerprintStringCapacityBytes,
                tick: 0);
        }

        if (destination.Length < 2 + byteCount)
        {
            throw new Physics3DNetCapacityExceededException(
                "handshake fingerprint encode destination",
                destination.Length,
                tick: 0);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), (ushort)byteCount);
        Encoding.UTF8.GetBytes(value, destination.Slice(2, byteCount));
        return 2 + byteCount;
    }

    private Physics3DNetCompatibilityFingerprint ReadFingerprint(ReadOnlySpan<byte> source)
    {
        int offset = 0;
        string buildId = ReadBoundedUtf8(source, ref offset);
        if (source.Length - offset < 8)
        {
            throw new Physics3DNetPacketMalformedException("Fingerprint truncated at config hash.");
        }

        ulong configHash = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8));
        offset += 8;
        string kernelId = ReadBoundedUtf8(source, ref offset);
        string simdProfile = ReadBoundedUtf8(source, ref offset);
        if (source.Length - offset < 4)
        {
            throw new Physics3DNetPacketMalformedException("Fingerprint truncated at worker count.");
        }

        int workerCount = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
        offset += 4;
        string scenarioId = ReadBoundedUtf8(source, ref offset);
        if (offset != source.Length)
        {
            throw new Physics3DNetPacketMalformedException("Fingerprint has trailing bytes.");
        }

        return new Physics3DNetCompatibilityFingerprint(
            buildId,
            configHash,
            kernelId,
            simdProfile,
            workerCount,
            scenarioId);
    }

    private string ReadBoundedUtf8(ReadOnlySpan<byte> source, ref int offset)
    {
        if (source.Length - offset < 2)
        {
            throw new Physics3DNetPacketMalformedException("Fingerprint string length truncated.");
        }

        ushort byteCount = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));
        offset += 2;
        if (byteCount > _fingerprintStringCapacityBytes)
        {
            throw new Physics3DNetCapacityExceededException(
                "handshake fingerprint string",
                _fingerprintStringCapacityBytes,
                tick: 0);
        }

        if (source.Length - offset < byteCount)
        {
            throw new Physics3DNetPacketMalformedException("Fingerprint string bytes truncated.");
        }

        string value = Encoding.UTF8.GetString(source.Slice(offset, byteCount));
        offset += byteCount;
        return value;
    }

    private static void WriteSnapshotEntity(Span<byte> destination, in Physics3DNetSnapshotEntityWrite entity)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(0, 4), entity.NetworkEntityId);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4, 4), entity.Generation);
        destination[8] = (byte)entity.Op;
        destination[9] = (byte)entity.BodyKind;
        destination[10] = (byte)entity.ReplicationMode;
        destination[11] = 0;
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(12, 8), entity.BaselineId);
        WriteVector3(destination.Slice(20, 12), entity.PositionCm);
        WriteQuaternion(destination.Slice(32, 16), entity.Orientation);
        WriteVector3(destination.Slice(48, 12), entity.LinearVelocityCmPerSecond);
        WriteVector3(destination.Slice(60, 12), entity.AngularVelocityRadiansPerSecond);
    }

    private static Physics3DNetSnapshotEntityWrite ReadSnapshotEntity(ReadOnlySpan<byte> source)
    {
        return new Physics3DNetSnapshotEntityWrite(
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(0, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4, 4)),
            (Physics3DNetReplicationOp)source[8],
            BinaryPrimitives.ReadInt64LittleEndian(source.Slice(12, 8)),
            ReadVector3(source.Slice(20, 12)),
            ReadQuaternion(source.Slice(32, 16)),
            ReadVector3(source.Slice(48, 12)),
            ReadVector3(source.Slice(60, 12)),
            (Physics3DBodyKind)source[9],
            (Physics3DNetReplicationMode)source[10]);
    }

    private static void WriteVector3(Span<byte> destination, Vector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(0, 4), value.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(4, 4), value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(8, 4), value.Z);
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> source) =>
        new(
            BinaryPrimitives.ReadSingleLittleEndian(source.Slice(0, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(source.Slice(4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(source.Slice(8, 4)));

    private static void WriteQuaternion(Span<byte> destination, Quaternion value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(0, 4), value.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(4, 4), value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(8, 4), value.Z);
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(12, 4), value.W);
    }

    private static Quaternion ReadQuaternion(ReadOnlySpan<byte> source) =>
        new(
            BinaryPrimitives.ReadSingleLittleEndian(source.Slice(0, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(source.Slice(4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(source.Slice(8, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(source.Slice(12, 4)));
}
