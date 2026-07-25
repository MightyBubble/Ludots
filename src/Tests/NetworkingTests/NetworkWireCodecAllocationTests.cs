using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkWireCodecAllocationTests
{
    private static readonly ProtocolVersion Protocol = new(1, 0);
    private static readonly ContentFingerprint Content = ContentFingerprintBuilder.FromCanonicalBytes("wire_0alloc"u8);
    private static readonly SessionEpoch Epoch = new(1);

    [Test]
    public void SteadyStateEncodeDecode_10000Operations_AllocatesZeroManagedBytes()
    {
        var request = new SessionHandshakeRequest(Protocol, Content, new ReconnectToken(1, 2), Epoch);
        var responseSeat = new SessionSeatBinding(0, 1, new PlayerId(1));
        SessionHandshakeResponse response = SessionHandshakeResponse.Accept(
            in responseSeat,
            new ReconnectToken(3, 4),
            Protocol,
            Content,
            Epoch,
            nextClientBatchSequence: 1);
        var confirmation = new SessionHandshakeConfirmation(Epoch, 0, 1, new ReconnectToken(3, 4));
        var commandHeader = new NetworkCommandBatchHeader(1, 1, 10, 9, 1, OrderSubmitMode.Immediate);
        var commandEntries = new NetworkCommandWireEntry[1];
        commandEntries[0] = new NetworkCommandWireEntry(
            new NetworkEntityHandle(1, 1),
            orderTypeId: 2,
            NetworkCommandTargetPayload.FromWorldPositionCm(10, 20, 30));
        var seat = new NetworkCommandSeat(0, 1, 1);
        var outcome = new NetworkCommandAdmissionOutcome(
            in seat,
            1,
            10,
            1,
            1,
            1,
            OrderSubmitResult.Queued,
            isReplay: false);
        var ack = new NetworkSnapshotAcknowledgement(1, 1, 10);
        var resync = new NetworkResyncRequired(1, NetworkResyncReason.BaselineExpired, 11, 2);

        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 4);
        var channel = new AuthoritativeReplicationChannel(4, 2, disclosureLog);
        var packet = new ReplicationPacketBuffer(4);
        var decodePacket = new ReplicationPacketBuffer(4);
        var states = new[]
        {
            new ReplicatedEntityState(
                new NetworkEntityHandle(0, 1),
                schemaId: 1,
                revision: 1,
                new ReplicationStateVector(1, 2, 3, 4)),
        };
        var disclosures = new[]
        {
            new ReplicationDisclosureInput(new NetworkEntityHandle(0, 1), KnowledgePresence.LiveVisible),
        };
        Assert.That(channel.BuildFull(1, 1, 1, states, disclosures, packet), Is.EqualTo(ReplicationBuildResult.Success));

        byte[] handshakeRequest = new byte[HandshakeWireCodec.RequestSizeInBytes];
        byte[] handshakeResponse = new byte[HandshakeWireCodec.ResponseSizeInBytes];
        byte[] handshakeConfirmation = new byte[HandshakeWireCodec.ConfirmationSizeInBytes];
        byte[] commandPayload = new byte[CommandBatchWireCodec.GetPayloadSize(1)];
        byte[] admissionPayload = new byte[CommandAdmissionWireCodec.SizeInBytes];
        byte[] replicationPayload = new byte[ReplicationPacketWireCodec.GetPayloadSize(
            packet.UpsertCount,
            packet.RemovalCount,
            packet.DisclosureChangeCount)];
        byte[] ackPayload = new byte[NetworkSnapshotAcknowledgement.SizeInBytes];
        byte[] resyncPayload = new byte[NetworkResyncRequired.SizeInBytes];
        byte[] framed = new byte[NetworkWireEnvelope.SizeInBytes + replicationPayload.Length];
        var decodeEntries = new NetworkCommandWireEntry[1];

        // Warmup / JIT.
        for (int i = 0; i < 64; i++)
        {
            RunSteadyStateOnce(
                in request,
                in response,
                in confirmation,
                in commandHeader,
                commandEntries,
                in outcome,
                in ack,
                in resync,
                packet,
                decodePacket,
                handshakeRequest,
                handshakeResponse,
                handshakeConfirmation,
                commandPayload,
                admissionPayload,
                replicationPayload,
                ackPayload,
                resyncPayload,
                framed,
                decodeEntries);
        }

        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            RunSteadyStateOnce(
                in request,
                in response,
                in confirmation,
                in commandHeader,
                commandEntries,
                in outcome,
                in ack,
                in resync,
                packet,
                decodePacket,
                handshakeRequest,
                handshakeResponse,
                handshakeConfirmation,
                commandPayload,
                admissionPayload,
                replicationPayload,
                ackPayload,
                resyncPayload,
                framed,
                decodeEntries);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), $"Expected 0 B managed allocation over 10,000 steady-state codec ops; observed {allocated} B.");
    }

    private static void RunSteadyStateOnce(
        in SessionHandshakeRequest request,
        in SessionHandshakeResponse response,
        in SessionHandshakeConfirmation confirmation,
        in NetworkCommandBatchHeader commandHeader,
        NetworkCommandWireEntry[] commandEntries,
        in NetworkCommandAdmissionOutcome outcome,
        in NetworkSnapshotAcknowledgement ack,
        in NetworkResyncRequired resync,
        ReplicationPacketBuffer packet,
        ReplicationPacketBuffer decodePacket,
        byte[] handshakeRequest,
        byte[] handshakeResponse,
        byte[] handshakeConfirmation,
        byte[] commandPayload,
        byte[] admissionPayload,
        byte[] replicationPayload,
        byte[] ackPayload,
        byte[] resyncPayload,
        byte[] framed,
        NetworkCommandWireEntry[] decodeEntries)
    {
        AssertSuccess(HandshakeWireCodec.TryEncodeRequest(in request, handshakeRequest, out _));
        AssertSuccess(HandshakeWireCodec.TryDecodeRequest(handshakeRequest, out _));
        AssertSuccess(HandshakeWireCodec.TryEncodeResponse(in response, handshakeResponse, out _));
        AssertSuccess(HandshakeWireCodec.TryDecodeResponse(handshakeResponse, out _));
        AssertSuccess(HandshakeWireCodec.TryEncodeConfirmation(in confirmation, handshakeConfirmation, out _));
        AssertSuccess(HandshakeWireCodec.TryDecodeConfirmation(handshakeConfirmation, out _));
        AssertSuccess(CommandBatchWireCodec.TryEncode(in commandHeader, commandEntries, commandPayload, out _));
        AssertSuccess(CommandBatchWireCodec.TryDecode(commandPayload, decodeEntries, out _, out _));
        var authenticatedSeat = new NetworkCommandSeat(
            outcome.SeatSlot,
            outcome.SeatGeneration,
            outcome.PlayerId);
        AssertSuccess(CommandAdmissionWireCodec.TryEncode(1, in outcome, admissionPayload, out _));
        AssertSuccess(CommandAdmissionWireCodec.TryDecode(admissionPayload, 1, in authenticatedSeat, out _));
        AssertSuccess(ReplicationPacketWireCodec.TryEncode(packet, replicationPayload, out int replicationBytes));
        AssertSuccess(ReplicationPacketWireCodec.TryDecode(replicationPayload.AsSpan(0, replicationBytes), decodePacket));
        AssertSuccess(SnapshotControlWireCodec.TryEncodeAcknowledgement(in ack, ackPayload, out _));
        AssertSuccess(SnapshotControlWireCodec.TryDecodeAcknowledgement(ackPayload, out _));
        AssertSuccess(SnapshotControlWireCodec.TryEncodeResyncRequired(in resync, resyncPayload, out _));
        AssertSuccess(SnapshotControlWireCodec.TryDecodeResyncRequired(resyncPayload, out _));
        AssertSuccess(NetworkWireEnvelopeCodec.TryEncode(
            NetworkWireKind.ReplicationPacket,
            replicationPayload.AsSpan(0, replicationBytes),
            framed,
            out int framedBytes));
        AssertSuccess(NetworkWireEnvelopeCodec.TryDecode(framed.AsSpan(0, framedBytes), out _, out _));
    }

    private static void AssertSuccess(NetworkWireCodecStatus status)
    {
        if (status != NetworkWireCodecStatus.Success)
        {
            throw new InvalidOperationException($"Expected Success, got {status}.");
        }
    }
}
