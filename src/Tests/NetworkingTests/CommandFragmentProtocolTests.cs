using System.Buffers.Binary;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class CommandFragmentProtocolTests
{
    private const int MaxDatagramPayloadBytes = 1200;
    private const int MaxCommandPayloadBytes = 8192;
    private const int MaxFragments = 16;
    private const int MaxActorsPerBatch = 128;

    [Test]
    public void ExactRoundTrip_RealCommandBatch_128Entries_OutOfOrderReassembly()
    {
        var encoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, MaxCommandPayloadBytes, MaxFragments);
        byte[] batchPayload = CreateEncodedCommandBatch(
            sessionEpoch: 7,
            clientBatchSequence: 99,
            targetTick: 1_234,
            acknowledgedCommittedTick: 1_230,
            entryCount: MaxActorsPerBatch);

        Assert.That(batchPayload.Length, Is.EqualTo(CommandBatchWireCodec.GetPayloadSize(MaxActorsPerBatch)));
        Assert.That(batchPayload.Length, Is.GreaterThan(encoder.MaxFragmentDataBytes));
        Assert.That(
            encoder.TryGetFragmentCount(batchPayload.Length, out ushort count),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(count, Is.GreaterThan(1));

        byte[][] wires = EncodeAll(encoder, sessionEpoch: 7, clientBatchSequence: 99, batchPayload, count);
        var reassembler = new CommandFragmentReassembler(MaxCommandPayloadBytes, MaxFragments);

        // Out-of-order: last → first → middle ascending.
        Assert.That(reassembler.TryAcceptWirePayload(wires[count - 1]), Is.EqualTo(CommandReassemblyStatus.Incomplete));
        Assert.That(reassembler.Phase, Is.EqualTo(CommandReassemblyPhase.Assembling));
        Assert.That(reassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(CommandReassemblyStatus.Incomplete));

        CommandReassemblyStatus lastStatus = CommandReassemblyStatus.Incomplete;
        for (ushort i = 1; i < count - 1; i++)
        {
            lastStatus = reassembler.TryAcceptWirePayload(wires[i]);
            if (i + 1 < count - 1)
            {
                Assert.That(lastStatus, Is.EqualTo(CommandReassemblyStatus.Incomplete));
            }
        }

        if (count == 2)
        {
            Assert.That(reassembler.Phase, Is.EqualTo(CommandReassemblyPhase.Assembling));
            Assert.That(reassembler.TryAcceptWirePayload(wires[1]), Is.EqualTo(CommandReassemblyStatus.Completed));
        }
        else
        {
            Assert.That(lastStatus, Is.EqualTo(CommandReassemblyStatus.Completed));
        }

        Assert.That(reassembler.Phase, Is.EqualTo(CommandReassemblyPhase.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(batchPayload), Is.True);

        Span<NetworkCommandWireEntry> decoded = stackalloc NetworkCommandWireEntry[MaxActorsPerBatch];
        Assert.That(
            CommandBatchWireCodec.TryDecode(
                reassembler.AssembledPayload,
                decoded,
                out NetworkCommandBatchHeader decodedHeader,
                out int entryCount),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(entryCount, Is.EqualTo(MaxActorsPerBatch));
        Assert.That(decodedHeader.TargetTick, Is.EqualTo(1_234));
        Assert.That(decodedHeader.SessionEpoch, Is.EqualTo(7UL));
        Assert.That(decodedHeader.ClientBatchSequence, Is.EqualTo(99UL));
        Assert.That(decodedHeader.EntryCount, Is.EqualTo((ushort)MaxActorsPerBatch));
    }

    [Test]
    public void SingleFragment_EmptyAndExactFit_RoundTrip()
    {
        var encoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, MaxCommandPayloadBytes, MaxFragments);
        var reassembler = new CommandFragmentReassembler(MaxCommandPayloadBytes, MaxFragments);

        byte[] empty = Array.Empty<byte>();
        Assert.That(encoder.TryGetFragmentCount(0, out ushort emptyCount), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(emptyCount, Is.EqualTo(1));
        Span<byte> emptyWire = stackalloc byte[NetworkCommandFragmentHeader.SizeInBytes];
        Assert.That(
            encoder.TryEncodeFragment(1, 1, empty, 0, 1, emptyWire, out int emptyWritten),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(emptyWritten, Is.EqualTo(NetworkCommandFragmentHeader.SizeInBytes));
        Assert.That(reassembler.TryAcceptWirePayload(emptyWire), Is.EqualTo(CommandReassemblyStatus.Completed));
        Assert.That(reassembler.AssembledPayload.Length, Is.EqualTo(0));

        reassembler.Reset();
        byte[] exact = CreatePatternPayload(encoder.MaxFragmentDataBytes);
        Span<byte> exactWire = stackalloc byte[CommandFragmentWireCodec.GetWirePayloadSize(exact.Length)];
        Assert.That(
            encoder.TryEncodeFragment(2, 3, exact, 0, 1, exactWire, out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(reassembler.TryAcceptWirePayload(exactWire), Is.EqualTo(CommandReassemblyStatus.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(exact), Is.True);
    }

    [Test]
    public void ZeroIdentity_IsRejectedWithoutMutation()
    {
        var encoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, MaxCommandPayloadBytes, MaxFragments);
        byte[] payload = CreatePatternPayload(16);
        Span<byte> buffer = stackalloc byte[CommandFragmentWireCodec.GetWirePayloadSize(16)];

        Assert.That(
            encoder.TryEncodeFragment(0, 1, payload, 0, 1, buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
        Assert.That(
            encoder.TryEncodeFragment(1, 0, payload, 0, 1, buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));

        Assert.That(
            encoder.TryEncodeFragment(1, 1, payload, 0, 1, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        byte[] zeroEpoch = buffer[..written].ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(zeroEpoch.AsSpan(0, 8), 0);
        Assert.That(
            CommandFragmentWireCodec.TryDecode(zeroEpoch, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));

        byte[] zeroSequence = buffer[..written].ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(zeroSequence.AsSpan(8, 8), 0);
        Assert.That(
            CommandFragmentWireCodec.TryDecode(zeroSequence, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));

        var reassembler = new CommandFragmentReassembler(MaxCommandPayloadBytes, MaxFragments);
        Assert.That(reassembler.TryAcceptWirePayload(zeroEpoch), Is.EqualTo(CommandReassemblyStatus.InvalidFragment));
        Assert.That(reassembler.TryAcceptWirePayload(zeroSequence), Is.EqualTo(CommandReassemblyStatus.InvalidFragment));
        Assert.That(reassembler.Phase, Is.EqualTo(CommandReassemblyPhase.Empty));
        Assert.That(reassembler.ReceivedFragmentCount, Is.EqualTo(0));
    }

    [Test]
    public void MalformedPackets_AreRejectedWithoutMutation()
    {
        var encoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, MaxCommandPayloadBytes, MaxFragments);
        byte[] payload = CreatePatternPayload(encoder.MaxFragmentDataBytes + 8);
        byte[][] wires = EncodeAll(encoder, 1, 2, payload, fragmentCount: 2);
        var reassembler = new CommandFragmentReassembler(MaxCommandPayloadBytes, MaxFragments);

        Assert.That(reassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(CommandReassemblyStatus.Incomplete));
        int receivedBefore = reassembler.ReceivedFragmentCount;

        Assert.That(
            CommandFragmentWireCodec.TryDecode(wires[0].AsSpan()[..^1], out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.MalformedLength));

        byte[] trailing = new byte[wires[1].Length + 1];
        wires[1].CopyTo(trailing, 0);
        Assert.That(
            CommandFragmentWireCodec.TryDecode(trailing, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.TrailingBytes));
        Assert.That(reassembler.TryAcceptWirePayload(trailing), Is.EqualTo(CommandReassemblyStatus.InvalidFragment));

        byte[] badReserved = (byte[])wires[1].Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(badReserved.AsSpan(26, 2), 1);
        Assert.That(reassembler.TryAcceptWirePayload(badReserved), Is.EqualTo(CommandReassemblyStatus.InvalidFragment));

        byte[] badIndex = (byte[])wires[1].Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(badIndex.AsSpan(16, 2), 9);
        Assert.That(reassembler.TryAcceptWirePayload(badIndex), Is.EqualTo(CommandReassemblyStatus.InvalidFragment));

        Assert.That(reassembler.ReceivedFragmentCount, Is.EqualTo(receivedBefore));
        Assert.That(reassembler.Phase, Is.EqualTo(CommandReassemblyPhase.Assembling));
    }

    [Test]
    public void DuplicateIdentical_IsDuplicate_DisagreeingDuplicate_IsInvalidWithoutMutation()
    {
        var encoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, MaxCommandPayloadBytes, MaxFragments);
        byte[] payload = CreatePatternPayload(encoder.MaxFragmentDataBytes + 32);
        byte[][] wires = EncodeAll(encoder, 5, 6, payload, 2);
        var reassembler = new CommandFragmentReassembler(MaxCommandPayloadBytes, MaxFragments);

        Assert.That(reassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(CommandReassemblyStatus.Incomplete));
        Assert.That(reassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(CommandReassemblyStatus.Duplicate));

        byte[] mutated = (byte[])wires[0].Clone();
        mutated[^1] ^= 0xFF;
        Assert.That(reassembler.TryAcceptWirePayload(mutated), Is.EqualTo(CommandReassemblyStatus.InvalidFragment));
        Assert.That(reassembler.ReceivedFragmentCount, Is.EqualTo(1));

        Assert.That(reassembler.TryAcceptWirePayload(wires[1]), Is.EqualTo(CommandReassemblyStatus.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(payload), Is.True);
    }

    [Test]
    public void MixedMetadata_AndCompletedWithoutReset_AreRejected()
    {
        var encoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, MaxCommandPayloadBytes, MaxFragments);
        byte[] payloadA = CreatePatternPayload(encoder.MaxFragmentDataBytes + 4);
        byte[] payloadB = CreatePatternPayload(encoder.MaxFragmentDataBytes + 8);
        byte[][] wiresA = EncodeAll(encoder, sessionEpoch: 1, clientBatchSequence: 10, payloadA, 2);
        byte[][] wiresB = EncodeAll(encoder, sessionEpoch: 1, clientBatchSequence: 11, payloadB, 2);
        byte[][] wiresEpoch = EncodeAll(encoder, sessionEpoch: 2, clientBatchSequence: 10, payloadA, 2);

        var reassembler = new CommandFragmentReassembler(MaxCommandPayloadBytes, MaxFragments);
        Assert.That(reassembler.TryAcceptWirePayload(wiresA[0]), Is.EqualTo(CommandReassemblyStatus.Incomplete));
        Assert.That(reassembler.TryAcceptWirePayload(wiresB[0]), Is.EqualTo(CommandReassemblyStatus.MixedMetadata));
        Assert.That(reassembler.TryAcceptWirePayload(wiresEpoch[0]), Is.EqualTo(CommandReassemblyStatus.MixedMetadata));
        Assert.That(reassembler.ReceivedFragmentCount, Is.EqualTo(1));
        Assert.That(reassembler.ClientBatchSequence, Is.EqualTo(10UL));

        Assert.That(reassembler.TryAcceptWirePayload(wiresA[1]), Is.EqualTo(CommandReassemblyStatus.Completed));
        Assert.That(reassembler.TryAcceptWirePayload(wiresB[0]), Is.EqualTo(CommandReassemblyStatus.StaleOrOutOfOrder));
        Assert.That(reassembler.Phase, Is.EqualTo(CommandReassemblyPhase.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(payloadA), Is.True);

        reassembler.Reset();
        Assert.That(reassembler.Phase, Is.EqualTo(CommandReassemblyPhase.Empty));
        Assert.Throws<InvalidOperationException>(() => _ = reassembler.AssembledPayload);
        Assert.That(reassembler.TryAcceptWirePayload(wiresB[1]), Is.EqualTo(CommandReassemblyStatus.Incomplete));
        Assert.That(reassembler.TryAcceptWirePayload(wiresB[0]), Is.EqualTo(CommandReassemblyStatus.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(payloadB), Is.True);
    }

    [Test]
    public void CapacityContracts_RejectOversizedPayloadAndFragmentCount()
    {
        var encoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, maxCommandPayloadBytes: 100, maxFragments: 2);
        Assert.That(
            encoder.TryGetFragmentCount(101, out _),
            Is.EqualTo(NetworkWireCodecStatus.CapacityExhausted));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new CommandFragmentEncoder(
                MaxDatagramPayloadBytes,
                maxCommandPayloadBytes: encoder.MaxFragmentDataBytes * 2 + 1,
                maxFragments: 2));

        var wideEncoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, MaxCommandPayloadBytes, MaxFragments);
        byte[] payload = CreatePatternPayload(wideEncoder.MaxFragmentDataBytes * 3);
        Assert.That(wideEncoder.TryGetFragmentCount(payload.Length, out ushort count), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(count, Is.EqualTo(3));
        byte[][] wires = EncodeAll(wideEncoder, 1, 1, payload, count);

        var tinyReassembler = new CommandFragmentReassembler(maxCommandPayloadBytes: MaxCommandPayloadBytes, maxFragments: 2);
        Assert.That(tinyReassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(CommandReassemblyStatus.CapacityExceeded));
        Assert.That(tinyReassembler.Phase, Is.EqualTo(CommandReassemblyPhase.Empty));

        var smallPayloadReassembler = new CommandFragmentReassembler(maxCommandPayloadBytes: 8, maxFragments: MaxFragments);
        Assert.That(smallPayloadReassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(CommandReassemblyStatus.CapacityExceeded));
    }

    [Test]
    public void Encoder_RejectsWrongFragmentCount_AndBufferTooSmall()
    {
        var encoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, MaxCommandPayloadBytes, MaxFragments);
        byte[] payload = CreatePatternPayload(encoder.MaxFragmentDataBytes + 1);
        Span<byte> buffer = stackalloc byte[CommandFragmentWireCodec.GetWirePayloadSize(encoder.MaxFragmentDataBytes)];
        Assert.That(
            encoder.TryEncodeFragment(1, 1, payload, 0, 1, buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));

        Span<byte> tooSmall = stackalloc byte[4];
        Assert.That(
            encoder.TryEncodeFragment(1, 1, payload, 0, 2, tooSmall, out _),
            Is.EqualTo(NetworkWireCodecStatus.BufferTooSmall));
    }

    [Test]
    public void EnvelopeKind_CommandFragment_IsRecognized()
    {
        var encoder = new CommandFragmentEncoder(MaxDatagramPayloadBytes, MaxCommandPayloadBytes, MaxFragments);
        byte[] payload = CreatePatternPayload(16);
        Span<byte> fragmentPayload = stackalloc byte[CommandFragmentWireCodec.GetWirePayloadSize(16)];
        Assert.That(
            encoder.TryEncodeFragment(9, 8, payload, 0, 1, fragmentPayload, out int payloadBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        Span<byte> framed = stackalloc byte[NetworkWireEnvelope.SizeInBytes + payloadBytes];
        Assert.That(
            NetworkWireEnvelopeCodec.TryEncode(
                NetworkWireKind.CommandFragment,
                fragmentPayload[..payloadBytes],
                framed,
                out int framedBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(
            NetworkWireEnvelopeCodec.TryDecode(framed[..framedBytes], out NetworkWireEnvelope envelope, out ReadOnlySpan<byte> decoded),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(envelope.Kind, Is.EqualTo(NetworkWireKind.CommandFragment));
        Assert.That(decoded.SequenceEqual(fragmentPayload[..payloadBytes]), Is.True);
    }

    [Test]
    public void Boundary_HeaderLayout_IsLittleEndianSsot()
    {
        var header = new NetworkCommandFragmentHeader(
            sessionEpoch: 0x0102030405060708UL,
            clientBatchSequence: 0x1112131415161718UL,
            fragmentIndex: 1,
            fragmentCount: 3,
            totalPayloadLength: 0xAABBCCDD,
            fragmentPayloadLength: 4);
        Span<byte> data = stackalloc byte[4] { 0xDE, 0xAD, 0xBE, 0xEF };
        Span<byte> buffer = stackalloc byte[NetworkCommandFragmentHeader.SizeInBytes + 4];
        Assert.That(
            CommandFragmentWireCodec.TryEncode(in header, data, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(32));
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8));
        ushort index = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(16));
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(18));
        uint total = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20));
        ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(24));
        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(26));
        Assert.Multiple(() =>
        {
            Assert.That(epoch, Is.EqualTo(0x0102030405060708UL));
            Assert.That(sequence, Is.EqualTo(0x1112131415161718UL));
            Assert.That(index, Is.EqualTo(1));
            Assert.That(count, Is.EqualTo(3));
            Assert.That(total, Is.EqualTo(0xAABBCCDD));
            Assert.That(payloadLength, Is.EqualTo(4));
            Assert.That(reserved, Is.EqualTo(0));
        });
    }

    private static byte[] CreateEncodedCommandBatch(
        ulong sessionEpoch,
        ulong clientBatchSequence,
        int targetTick,
        int acknowledgedCommittedTick,
        int entryCount)
    {
        var header = new NetworkCommandBatchHeader(
            sessionEpoch,
            clientBatchSequence,
            targetTick,
            acknowledgedCommittedTick,
            (ushort)entryCount);
        var entries = new NetworkCommandWireEntry[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            entries[i] = new NetworkCommandWireEntry(
                new NetworkEntityHandle(i + 1, (uint)(i + 2)),
                orderTypeId: 10 + (i % 7),
                NetworkCommandTargetPayload.FromWorldPositionCm(i * 10, i * 20, i));
        }

        byte[] buffer = new byte[CommandBatchWireCodec.GetPayloadSize(entryCount)];
        Assert.That(
            CommandBatchWireCodec.TryEncode(in header, entries, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(buffer.Length));
        return buffer;
    }

    private static byte[][] EncodeAll(
        CommandFragmentEncoder encoder,
        ulong sessionEpoch,
        ulong clientBatchSequence,
        byte[] commandPayload,
        ushort fragmentCount)
    {
        var wires = new byte[fragmentCount][];
        for (ushort i = 0; i < fragmentCount; i++)
        {
            Assert.That(
                CommandFragmentWireCodec.TryGetFragmentDataRange(
                    commandPayload.Length,
                    encoder.MaxFragmentDataBytes,
                    i,
                    fragmentCount,
                    out _,
                    out int length),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            wires[i] = new byte[CommandFragmentWireCodec.GetWirePayloadSize(length)];
            Assert.That(
                encoder.TryEncodeFragment(sessionEpoch, clientBatchSequence, commandPayload, i, fragmentCount, wires[i], out int written),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            Assert.That(written, Is.EqualTo(wires[i].Length));
        }

        return wires;
    }

    private static byte[] CreatePatternPayload(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i * 31 + 7);
        }

        return bytes;
    }
}
