using System.Buffers.Binary;
using Ludots.Core.Networking.Protocol;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class SnapshotFragmentProtocolTests
{
    private const int MaxDatagramPayloadBytes = 1200;
    private const int MaxSnapshotBytes = 4096;
    private const int MaxFragments = 16;

    [Test]
    public void ExactRoundTrip_MultiFragment_OutOfOrderReassembly()
    {
        var encoder = new SnapshotFragmentEncoder(MaxDatagramPayloadBytes, MaxSnapshotBytes, MaxFragments);
        byte[] snapshot = CreatePatternPayload(encoder.MaxFragmentDataBytes * 2 + 17);
        Assert.That(encoder.TryGetFragmentCount(snapshot.Length, out ushort count), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(count, Is.EqualTo(3));

        byte[][] wires = EncodeAll(encoder, sessionEpoch: 7, snapshotId: 99, snapshot, count);
        var reassembler = new SnapshotFragmentReassembler(MaxSnapshotBytes, MaxFragments);

        Assert.That(reassembler.TryAcceptWirePayload(wires[2]), Is.EqualTo(SnapshotReassemblyStatus.Incomplete));
        Assert.That(reassembler.Phase, Is.EqualTo(SnapshotReassemblyPhase.Assembling));
        Assert.That(reassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(SnapshotReassemblyStatus.Incomplete));
        Assert.That(reassembler.TryAcceptWirePayload(wires[1]), Is.EqualTo(SnapshotReassemblyStatus.Completed));
        Assert.That(reassembler.Phase, Is.EqualTo(SnapshotReassemblyPhase.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(snapshot), Is.True);
    }

    [Test]
    public void SingleFragment_EmptyAndExactFit_RoundTrip()
    {
        var encoder = new SnapshotFragmentEncoder(MaxDatagramPayloadBytes, MaxSnapshotBytes, MaxFragments);
        var reassembler = new SnapshotFragmentReassembler(MaxSnapshotBytes, MaxFragments);

        byte[] empty = Array.Empty<byte>();
        Assert.That(encoder.TryGetFragmentCount(0, out ushort emptyCount), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(emptyCount, Is.EqualTo(1));
        Span<byte> emptyWire = stackalloc byte[NetworkSnapshotFragmentHeader.SizeInBytes];
        Assert.That(
            encoder.TryEncodeFragment(1, 1, empty, 0, 1, emptyWire, out int emptyWritten),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(emptyWritten, Is.EqualTo(NetworkSnapshotFragmentHeader.SizeInBytes));
        Assert.That(reassembler.TryAcceptWirePayload(emptyWire), Is.EqualTo(SnapshotReassemblyStatus.Completed));
        Assert.That(reassembler.AssembledPayload.Length, Is.EqualTo(0));

        reassembler.Reset();
        byte[] exact = CreatePatternPayload(encoder.MaxFragmentDataBytes);
        Span<byte> exactWire = stackalloc byte[SnapshotFragmentWireCodec.GetWirePayloadSize(exact.Length)];
        Assert.That(
            encoder.TryEncodeFragment(2, 3, exact, 0, 1, exactWire, out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(reassembler.TryAcceptWirePayload(exactWire), Is.EqualTo(SnapshotReassemblyStatus.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(exact), Is.True);
    }

    [Test]
    public void MalformedPackets_AreRejectedWithoutMutation()
    {
        var encoder = new SnapshotFragmentEncoder(MaxDatagramPayloadBytes, MaxSnapshotBytes, MaxFragments);
        byte[] snapshot = CreatePatternPayload(encoder.MaxFragmentDataBytes + 8);
        byte[][] wires = EncodeAll(encoder, 1, 2, snapshot, fragmentCount: 2);
        var reassembler = new SnapshotFragmentReassembler(MaxSnapshotBytes, MaxFragments);

        Assert.That(reassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(SnapshotReassemblyStatus.Incomplete));
        int receivedBefore = reassembler.ReceivedFragmentCount;

        Assert.That(
            SnapshotFragmentWireCodec.TryDecode(wires[0].AsSpan()[..^1], out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.MalformedLength));

        byte[] trailing = new byte[wires[1].Length + 1];
        wires[1].CopyTo(trailing, 0);
        Assert.That(
            SnapshotFragmentWireCodec.TryDecode(trailing, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.TrailingBytes));
        Assert.That(reassembler.TryAcceptWirePayload(trailing), Is.EqualTo(SnapshotReassemblyStatus.InvalidFragment));

        byte[] badReserved = (byte[])wires[1].Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(badReserved.AsSpan(26, 2), 1);
        Assert.That(reassembler.TryAcceptWirePayload(badReserved), Is.EqualTo(SnapshotReassemblyStatus.InvalidFragment));

        byte[] badIndex = (byte[])wires[1].Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(badIndex.AsSpan(16, 2), 9);
        Assert.That(reassembler.TryAcceptWirePayload(badIndex), Is.EqualTo(SnapshotReassemblyStatus.InvalidFragment));

        Assert.That(reassembler.ReceivedFragmentCount, Is.EqualTo(receivedBefore));
        Assert.That(reassembler.Phase, Is.EqualTo(SnapshotReassemblyPhase.Assembling));
    }

    [Test]
    public void DuplicateIdentical_IsDuplicate_DisagreeingDuplicate_IsInvalidWithoutMutation()
    {
        var encoder = new SnapshotFragmentEncoder(MaxDatagramPayloadBytes, MaxSnapshotBytes, MaxFragments);
        byte[] snapshot = CreatePatternPayload(encoder.MaxFragmentDataBytes + 32);
        byte[][] wires = EncodeAll(encoder, 5, 6, snapshot, 2);
        var reassembler = new SnapshotFragmentReassembler(MaxSnapshotBytes, MaxFragments);

        Assert.That(reassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(SnapshotReassemblyStatus.Incomplete));
        Assert.That(reassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(SnapshotReassemblyStatus.Duplicate));

        byte[] mutated = (byte[])wires[0].Clone();
        mutated[^1] ^= 0xFF;
        Assert.That(reassembler.TryAcceptWirePayload(mutated), Is.EqualTo(SnapshotReassemblyStatus.InvalidFragment));
        Assert.That(reassembler.ReceivedFragmentCount, Is.EqualTo(1));

        Assert.That(reassembler.TryAcceptWirePayload(wires[1]), Is.EqualTo(SnapshotReassemblyStatus.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(snapshot), Is.True);
    }

    [Test]
    public void MixedMetadata_AndCompletedWithoutReset_AreRejected()
    {
        var encoder = new SnapshotFragmentEncoder(MaxDatagramPayloadBytes, MaxSnapshotBytes, MaxFragments);
        byte[] snapshotA = CreatePatternPayload(encoder.MaxFragmentDataBytes + 4);
        byte[] snapshotB = CreatePatternPayload(encoder.MaxFragmentDataBytes + 8);
        byte[][] wiresA = EncodeAll(encoder, sessionEpoch: 1, snapshotId: 10, snapshotA, 2);
        byte[][] wiresB = EncodeAll(encoder, sessionEpoch: 1, snapshotId: 11, snapshotB, 2);
        byte[][] wiresEpoch = EncodeAll(encoder, sessionEpoch: 2, snapshotId: 10, snapshotA, 2);

        var reassembler = new SnapshotFragmentReassembler(MaxSnapshotBytes, MaxFragments);
        Assert.That(reassembler.TryAcceptWirePayload(wiresA[0]), Is.EqualTo(SnapshotReassemblyStatus.Incomplete));
        Assert.That(reassembler.TryAcceptWirePayload(wiresB[0]), Is.EqualTo(SnapshotReassemblyStatus.MixedMetadata));
        Assert.That(reassembler.TryAcceptWirePayload(wiresEpoch[0]), Is.EqualTo(SnapshotReassemblyStatus.MixedMetadata));
        Assert.That(reassembler.ReceivedFragmentCount, Is.EqualTo(1));
        Assert.That(reassembler.SnapshotId, Is.EqualTo(10UL));

        Assert.That(reassembler.TryAcceptWirePayload(wiresA[1]), Is.EqualTo(SnapshotReassemblyStatus.Completed));
        Assert.That(reassembler.TryAcceptWirePayload(wiresB[0]), Is.EqualTo(SnapshotReassemblyStatus.StaleOrOutOfOrder));
        Assert.That(reassembler.Phase, Is.EqualTo(SnapshotReassemblyPhase.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(snapshotA), Is.True);

        reassembler.Reset();
        Assert.That(reassembler.Phase, Is.EqualTo(SnapshotReassemblyPhase.Empty));
        Assert.Throws<InvalidOperationException>(() => _ = reassembler.AssembledPayload);
        Assert.That(reassembler.TryAcceptWirePayload(wiresB[1]), Is.EqualTo(SnapshotReassemblyStatus.Incomplete));
        Assert.That(reassembler.TryAcceptWirePayload(wiresB[0]), Is.EqualTo(SnapshotReassemblyStatus.Completed));
        Assert.That(reassembler.AssembledPayload.SequenceEqual(snapshotB), Is.True);
    }

    [Test]
    public void CapacityContracts_RejectOversizedSnapshotAndFragmentCount()
    {
        var encoder = new SnapshotFragmentEncoder(MaxDatagramPayloadBytes, maxSnapshotBytes: 100, maxFragments: 2);
        Assert.That(
            encoder.TryGetFragmentCount(101, out _),
            Is.EqualTo(NetworkWireCodecStatus.CapacityExhausted));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new SnapshotFragmentEncoder(
                MaxDatagramPayloadBytes,
                maxSnapshotBytes: encoder.MaxFragmentDataBytes * 2 + 1,
                maxFragments: 2));

        var wideEncoder = new SnapshotFragmentEncoder(MaxDatagramPayloadBytes, MaxSnapshotBytes, MaxFragments);
        byte[] snapshot = CreatePatternPayload(wideEncoder.MaxFragmentDataBytes * 3);
        Assert.That(wideEncoder.TryGetFragmentCount(snapshot.Length, out ushort count), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(count, Is.EqualTo(3));
        byte[][] wires = EncodeAll(wideEncoder, 1, 1, snapshot, count);

        var tinyReassembler = new SnapshotFragmentReassembler(maxSnapshotBytes: MaxSnapshotBytes, maxFragments: 2);
        Assert.That(tinyReassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(SnapshotReassemblyStatus.CapacityExceeded));
        Assert.That(tinyReassembler.Phase, Is.EqualTo(SnapshotReassemblyPhase.Empty));

        var smallPayloadReassembler = new SnapshotFragmentReassembler(maxSnapshotBytes: 8, maxFragments: MaxFragments);
        Assert.That(smallPayloadReassembler.TryAcceptWirePayload(wires[0]), Is.EqualTo(SnapshotReassemblyStatus.CapacityExceeded));
    }

    [Test]
    public void Encoder_RejectsWrongFragmentCount_AndBufferTooSmall()
    {
        var encoder = new SnapshotFragmentEncoder(MaxDatagramPayloadBytes, MaxSnapshotBytes, MaxFragments);
        byte[] snapshot = CreatePatternPayload(encoder.MaxFragmentDataBytes + 1);
        Span<byte> buffer = stackalloc byte[SnapshotFragmentWireCodec.GetWirePayloadSize(encoder.MaxFragmentDataBytes)];
        Assert.That(
            encoder.TryEncodeFragment(1, 1, snapshot, 0, 1, buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));

        Span<byte> tooSmall = stackalloc byte[4];
        Assert.That(
            encoder.TryEncodeFragment(1, 1, snapshot, 0, 2, tooSmall, out _),
            Is.EqualTo(NetworkWireCodecStatus.BufferTooSmall));
    }

    [Test]
    public void EnvelopeKind_SnapshotFragment_IsRecognized()
    {
        var encoder = new SnapshotFragmentEncoder(MaxDatagramPayloadBytes, MaxSnapshotBytes, MaxFragments);
        byte[] snapshot = CreatePatternPayload(16);
        Span<byte> payload = stackalloc byte[SnapshotFragmentWireCodec.GetWirePayloadSize(16)];
        Assert.That(
            encoder.TryEncodeFragment(9, 8, snapshot, 0, 1, payload, out int payloadBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        Span<byte> framed = stackalloc byte[NetworkWireEnvelope.SizeInBytes + payloadBytes];
        Assert.That(
            NetworkWireEnvelopeCodec.TryEncode(
                NetworkWireKind.SnapshotFragment,
                payload[..payloadBytes],
                framed,
                out int framedBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(
            NetworkWireEnvelopeCodec.TryDecode(framed[..framedBytes], out NetworkWireEnvelope envelope, out ReadOnlySpan<byte> decoded),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(envelope.Kind, Is.EqualTo(NetworkWireKind.SnapshotFragment));
        Assert.That(decoded.SequenceEqual(payload[..payloadBytes]), Is.True);
    }

    [Test]
    public void Boundary_HeaderLayout_IsLittleEndianSsot()
    {
        var header = new NetworkSnapshotFragmentHeader(
            sessionEpoch: 0x0102030405060708UL,
            snapshotId: 0x1112131415161718UL,
            fragmentIndex: 1,
            fragmentCount: 3,
            totalPayloadLength: 0xAABBCCDD,
            fragmentPayloadLength: 4);
        Span<byte> data = stackalloc byte[4] { 0xDE, 0xAD, 0xBE, 0xEF };
        Span<byte> buffer = stackalloc byte[NetworkSnapshotFragmentHeader.SizeInBytes + 4];
        Assert.That(
            SnapshotFragmentWireCodec.TryEncode(in header, data, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(32));
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        ulong snapshotId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8));
        ushort index = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(16));
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(18));
        uint total = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20));
        ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(24));
        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(26));
        Assert.Multiple(() =>
        {
            Assert.That(epoch, Is.EqualTo(0x0102030405060708UL));
            Assert.That(snapshotId, Is.EqualTo(0x1112131415161718UL));
            Assert.That(index, Is.EqualTo(1));
            Assert.That(count, Is.EqualTo(3));
            Assert.That(total, Is.EqualTo(0xAABBCCDD));
            Assert.That(payloadLength, Is.EqualTo(4));
            Assert.That(reserved, Is.EqualTo(0));
        });
    }

    private static byte[][] EncodeAll(
        SnapshotFragmentEncoder encoder,
        ulong sessionEpoch,
        ulong snapshotId,
        byte[] snapshot,
        ushort fragmentCount)
    {
        var wires = new byte[fragmentCount][];
        for (ushort i = 0; i < fragmentCount; i++)
        {
            Assert.That(
                SnapshotFragmentWireCodec.TryGetFragmentDataRange(
                    snapshot.Length,
                    encoder.MaxFragmentDataBytes,
                    i,
                    fragmentCount,
                    out _,
                    out int length),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            wires[i] = new byte[SnapshotFragmentWireCodec.GetWirePayloadSize(length)];
            Assert.That(
                encoder.TryEncodeFragment(sessionEpoch, snapshotId, snapshot, i, fragmentCount, wires[i], out int written),
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
