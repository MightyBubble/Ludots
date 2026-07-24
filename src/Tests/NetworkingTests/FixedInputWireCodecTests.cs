using System.Buffers.Binary;
using Ludots.Core.Networking.Protocol;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class FixedInputWireCodecTests
{
    private const ushort SchemaId = 7;
    private const ushort PayloadBytes = 12;

    [Test]
    public void WireKinds_AppendFixedInputWithoutRenumberingExistingKinds()
    {
        Assert.Multiple(() =>
        {
            Assert.That((byte)NetworkWireKind.SessionHandshakeRequest, Is.EqualTo(1));
            Assert.That((byte)NetworkWireKind.CommandFragment, Is.EqualTo(9));
            Assert.That((byte)NetworkWireKind.FixedInputBatch, Is.EqualTo(10));
            Assert.That((byte)NetworkWireKind.FixedInputAcknowledgement, Is.EqualTo(11));
        });
    }

    [Test]
    public void Batch_RoundTrip_AndGoldenLittleEndianHeader()
    {
        var header = new NetworkFixedInputBatchHeader(
            sessionEpoch: 42,
            schemaId: SchemaId,
            framePayloadBytes: PayloadBytes,
            acknowledgedCommittedTick: 100,
            frameCount: 2);
        Span<uint> ticks = stackalloc uint[2] { 101, 103 };
        Span<byte> payloads = stackalloc byte[PayloadBytes * 2];
        payloads.Clear();
        payloads[0] = 0x11;
        payloads[PayloadBytes] = 0x22;

        Span<byte> buffer = stackalloc byte[FixedInputWireCodec.GetBatchPayloadSize(PayloadBytes, 2)];
        Assert.That(
            FixedInputWireCodec.TryEncodeBatch(in header, ticks, payloads, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(FixedInputWireCodec.GetBatchPayloadSize(PayloadBytes, 2)));
        Assert.That(NetworkFixedInputBatchHeader.SizeInBytes, Is.EqualTo(20));

        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        ushort schema = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(8, 2));
        ushort payloadBytes = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(10, 2));
        uint ackCommitted = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12, 4));
        ushort frameCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(16, 2));
        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(18, 2));
        uint tick0 = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20, 4));
        byte payload0 = buffer[24];
        uint tick1 = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20 + 4 + PayloadBytes, 4));
        Assert.Multiple(() =>
        {
            Assert.That(epoch, Is.EqualTo(42UL));
            Assert.That(schema, Is.EqualTo(SchemaId));
            Assert.That(payloadBytes, Is.EqualTo(PayloadBytes));
            Assert.That(ackCommitted, Is.EqualTo(100u));
            Assert.That(frameCount, Is.EqualTo(2));
            Assert.That(reserved, Is.EqualTo(0));
            Assert.That(tick0, Is.EqualTo(101u));
            Assert.That(payload0, Is.EqualTo(0x11));
            Assert.That(tick1, Is.EqualTo(103u));
        });

        Span<uint> decodedTicks = stackalloc uint[2];
        Span<byte> decodedPayloads = stackalloc byte[PayloadBytes * 2];
        Assert.That(
            FixedInputWireCodec.TryDecodeBatch(buffer, decodedTicks, decodedPayloads, out NetworkFixedInputBatchHeader decoded, out int count),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(count, Is.EqualTo(2));
        Assert.That(decoded.SessionEpoch, Is.EqualTo(42UL));
        Assert.That(decodedTicks[0], Is.EqualTo(101u));
        Assert.That(decodedTicks[1], Is.EqualTo(103u));
        Assert.That(decodedPayloads[0], Is.EqualTo(0x11));
        Assert.That(decodedPayloads[PayloadBytes], Is.EqualTo(0x22));
    }

    [Test]
    public void Acknowledgement_RoundTrip_Fixed32Bytes_AndMaskBit0IsLatestReceived()
    {
        Assert.That(NetworkFixedInputAcknowledgement.SizeInBytes, Is.EqualTo(32));
        var ack = new NetworkFixedInputAcknowledgement(
            sessionEpoch: 9,
            schemaId: SchemaId,
            committedThroughTick: 50,
            latestReceivedTick: 55,
            receivedMask: 0b1011UL,
            latestMissingInputTick: 53);
        Span<byte> buffer = stackalloc byte[NetworkFixedInputAcknowledgement.SizeInBytes];
        Assert.That(
            FixedInputWireCodec.TryEncodeAcknowledgement(in ack, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(32));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(10, 2)), Is.EqualTo(0));

        Assert.That(
            FixedInputWireCodec.TryDecodeAcknowledgement(buffer, out NetworkFixedInputAcknowledgement decoded),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(decoded.LatestReceivedTick, Is.EqualTo(55u));
        Assert.That(decoded.ReceivedMask & 1UL, Is.EqualTo(1UL));
        Assert.That(decoded.LatestMissingInputTick, Is.EqualTo(53u));
    }

    [Test]
    public void Envelope_AcceptsFixedInputKinds()
    {
        Span<byte> payload = stackalloc byte[1] { 0xAB };
        Span<byte> framed = stackalloc byte[NetworkWireEnvelope.SizeInBytes + 1];
        Assert.That(
            NetworkWireEnvelopeCodec.TryEncode(NetworkWireKind.FixedInputBatch, payload, framed, out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(
            NetworkWireEnvelopeCodec.TryDecode(framed, out NetworkWireEnvelope envelope, out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(envelope.Kind, Is.EqualTo(NetworkWireKind.FixedInputBatch));

        Assert.That(
            NetworkWireEnvelopeCodec.TryEncode(NetworkWireKind.FixedInputAcknowledgement, payload, framed, out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));
    }

    [Test]
    public void Batch_RejectsNonIncreasingTicks_ReservedNonZero_AndTrailingBytes()
    {
        var header = new NetworkFixedInputBatchHeader(1, SchemaId, PayloadBytes, 0, 2);
        Span<uint> badTicks = stackalloc uint[2] { 5, 5 };
        Span<byte> payloads = stackalloc byte[PayloadBytes * 2];
        Span<byte> buffer = stackalloc byte[FixedInputWireCodec.GetBatchPayloadSize(PayloadBytes, 2)];
        Assert.That(
            FixedInputWireCodec.TryEncodeBatch(in header, badTicks, payloads, buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));

        Span<uint> goodTicks = stackalloc uint[2] { 5, 6 };
        Assert.That(
            FixedInputWireCodec.TryEncodeBatch(
                new NetworkFixedInputBatchHeader(1, SchemaId, PayloadBytes, 0, 2),
                goodTicks,
                payloads,
                buffer,
                out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        Span<byte> reservedCorrupt = stackalloc byte[buffer.Length];
        buffer.CopyTo(reservedCorrupt);
        BinaryPrimitives.WriteUInt16LittleEndian(reservedCorrupt.Slice(18, 2), 1);
        Assert.That(
            FixedInputWireCodec.TryDecodeBatch(reservedCorrupt, stackalloc uint[2], stackalloc byte[PayloadBytes * 2], out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));

        Span<byte> trailing = stackalloc byte[buffer.Length + 1];
        buffer.CopyTo(trailing);
        Assert.That(
            FixedInputWireCodec.TryDecodeBatch(trailing, stackalloc uint[2], stackalloc byte[PayloadBytes * 2], out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.TrailingBytes));
    }

    [Test]
    public void Acknowledgement_RejectsReservedNonZero()
    {
        var ack = new NetworkFixedInputAcknowledgement(1, SchemaId, 1, 2, 1, 0);
        Span<byte> buffer = stackalloc byte[NetworkFixedInputAcknowledgement.SizeInBytes];
        Assert.That(FixedInputWireCodec.TryEncodeAcknowledgement(in ack, buffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(10, 2), 9);
        Assert.That(
            FixedInputWireCodec.TryDecodeAcknowledgement(buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
    }
}
