using System.Buffers.Binary;
using System.Reflection;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkWireCodecTests
{
    private static readonly ProtocolVersion Protocol = new(1, 0);
    private static readonly ContentFingerprint Content = ContentFingerprintBuilder.FromCanonicalBytes("rts_duel_v1"u8);
    private static readonly SessionEpoch Epoch = new(42);

    [Test]
    public void Envelope_RoundTrip_AndGoldenLittleEndianBytes()
    {
        Span<byte> payload = stackalloc byte[3] { 0x11, 0x22, 0x33 };
        Span<byte> buffer = stackalloc byte[NetworkWireEnvelope.SizeInBytes + 3];

        Assert.That(
            NetworkWireEnvelopeCodec.TryEncode(NetworkWireKind.CommandBatch, payload, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(11));

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        byte version = buffer[4];
        byte kind = buffer[5];
        ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2));
        byte b0 = buffer[8];
        byte b1 = buffer[9];
        byte b2 = buffer[10];
        Assert.Multiple(() =>
        {
            Assert.That(magic, Is.EqualTo(0x504E444C));
            Assert.That(version, Is.EqualTo(1));
            Assert.That(kind, Is.EqualTo((byte)NetworkWireKind.CommandBatch));
            Assert.That(payloadLength, Is.EqualTo(3));
            Assert.That(b0, Is.EqualTo(0x11));
            Assert.That(b1, Is.EqualTo(0x22));
            Assert.That(b2, Is.EqualTo(0x33));
        });

        Assert.That(
            NetworkWireEnvelopeCodec.TryDecode(buffer, out NetworkWireEnvelope envelope, out ReadOnlySpan<byte> decodedPayload),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(envelope.Kind, Is.EqualTo(NetworkWireKind.CommandBatch));
        Assert.That(envelope.PayloadLength, Is.EqualTo(3));
        Assert.That(decodedPayload.SequenceEqual(payload), Is.True);
    }

    [Test]
    public void HandshakeRequest_RoundTrip_AndGoldenLittleEndianPrefix()
    {
        var request = new SessionHandshakeRequest(
            Protocol,
            Content,
            new ReconnectToken(0x0102030405060708UL, 0x1112131415161718UL),
            Epoch);

        Span<byte> buffer = stackalloc byte[HandshakeWireCodec.RequestSizeInBytes];
        Assert.That(
            HandshakeWireCodec.TryEncodeRequest(in request, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(HandshakeWireCodec.RequestSizeInBytes));

        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0, 2));
        ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2));
        ulong tokenLow = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(4 + 32, 8));
        ulong tokenHigh = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(4 + 32 + 8, 8));
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(4 + 32 + 16, 8));
        Assert.Multiple(() =>
        {
            Assert.That(major, Is.EqualTo(1));
            Assert.That(minor, Is.EqualTo(0));
            Assert.That(tokenLow, Is.EqualTo(0x0102030405060708UL));
            Assert.That(tokenHigh, Is.EqualTo(0x1112131415161718UL));
            Assert.That(epoch, Is.EqualTo(42UL));
        });

        Assert.That(
            HandshakeWireCodec.TryDecodeRequest(buffer, out SessionHandshakeRequest decoded),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(decoded.ProtocolVersion, Is.EqualTo(request.ProtocolVersion));
        Assert.That(decoded.ContentFingerprint, Is.EqualTo(request.ContentFingerprint));
        Assert.That(decoded.ReconnectToken, Is.EqualTo(request.ReconnectToken));
        Assert.That(decoded.SessionEpoch, Is.EqualTo(request.SessionEpoch));
    }

    [Test]
    public void HandshakeResponse_AcceptAndReject_RoundTrip()
    {
        var acceptedSeat = new SessionSeatBinding(1, 3, new PlayerId(7));
        SessionHandshakeResponse accept = SessionHandshakeResponse.Accept(
            in acceptedSeat,
            new ReconnectToken(9, 11),
            Protocol,
            Content,
            Epoch,
            nextClientBatchSequence: 17);
        Span<byte> buffer = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(HandshakeWireCodec.TryEncodeResponse(in accept, buffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(HandshakeWireCodec.TryDecodeResponse(buffer, out SessionHandshakeResponse decodedAccept), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(decodedAccept.Accepted, Is.True);
        Assert.That(decodedAccept.PlayerId, Is.EqualTo(new PlayerId(7)));
        Assert.That(decodedAccept.Seat, Is.EqualTo(acceptedSeat));
        Assert.That(decodedAccept.NextClientBatchSequence, Is.EqualTo(17));

        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(12, sizeof(int)), 0);
        Assert.That(
            HandshakeWireCodec.TryDecodeResponse(buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
        Assert.That(HandshakeWireCodec.TryEncodeResponse(in accept, buffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));

        SessionHandshakeResponse reject = SessionHandshakeResponse.Reject(
            HandshakeRejectReason.ContentMismatch,
            Protocol,
            Content,
            Epoch);
        Assert.That(HandshakeWireCodec.TryEncodeResponse(in reject, buffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(HandshakeWireCodec.TryDecodeResponse(buffer, out SessionHandshakeResponse decodedReject), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(decodedReject.Accepted, Is.False);
        Assert.That(decodedReject.RejectReason, Is.EqualTo(HandshakeRejectReason.ContentMismatch));
    }

    [Test]
    public void HandshakeResponse_MatchAlreadyStarted_RoundTrips_AndRejectsUnknownReason()
    {
        SessionHandshakeResponse response = SessionHandshakeResponse.Reject(
            HandshakeRejectReason.MatchAlreadyStarted,
            Protocol,
            Content,
            Epoch);
        Span<byte> buffer = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];

        Assert.That(
            HandshakeWireCodec.TryEncodeResponse(in response, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(HandshakeWireCodec.ResponseSizeInBytes));
        Assert.That(
            HandshakeWireCodec.TryDecodeResponse(buffer, out SessionHandshakeResponse decoded),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.Multiple(() =>
        {
            Assert.That(decoded.Accepted, Is.False);
            Assert.That(decoded.RejectReason, Is.EqualTo(HandshakeRejectReason.MatchAlreadyStarted));
        });

        SessionHandshakeResponse invalidResponse = SessionHandshakeResponse.Reject(
            (HandshakeRejectReason)((byte)HandshakeRejectReason.MatchAlreadyStarted + 1),
            Protocol,
            Content,
            Epoch);
        Assert.That(
            HandshakeWireCodec.TryEncodeResponse(in invalidResponse, buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidEnum));

        buffer[1] = checked((byte)((byte)HandshakeRejectReason.MatchAlreadyStarted + 1));
        Assert.That(
            HandshakeWireCodec.TryDecodeResponse(buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidEnum));
    }

    [Test]
    public void HandshakeConfirmation_RoundTrips_AndRejectsMalformedPayloads()
    {
        var confirmation = new SessionHandshakeConfirmation(
            Epoch,
            seatSlot: 1,
            seatGeneration: 3,
            reconnectToken: new ReconnectToken(0x0102030405060708UL, 0x1112131415161718UL));
        Span<byte> buffer = stackalloc byte[HandshakeWireCodec.ConfirmationSizeInBytes];

        Assert.That(
            typeof(SessionHandshakeConfirmation)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Any(property => property.Name.Contains("Player", StringComparison.OrdinalIgnoreCase)),
            Is.False,
            "Handshake confirmation must not carry a client-authored player identity.");

        Assert.That(
            HandshakeWireCodec.TryEncodeConfirmation(in confirmation, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(HandshakeWireCodec.ConfirmationSizeInBytes));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(buffer), Is.EqualTo(42UL));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(8, 4)), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12, 4)), Is.EqualTo(3u));
        Assert.That(
            HandshakeWireCodec.TryDecodeConfirmation(buffer, out SessionHandshakeConfirmation decoded),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.Multiple(() =>
        {
            Assert.That(decoded.SessionEpoch, Is.EqualTo(Epoch));
            Assert.That(decoded.SeatSlot, Is.EqualTo(1));
            Assert.That(decoded.SeatGeneration, Is.EqualTo(3));
            Assert.That(decoded.ReconnectToken, Is.EqualTo(confirmation.ReconnectToken));
        });

        Assert.That(
            HandshakeWireCodec.TryDecodeConfirmation(buffer[..^1], out _),
            Is.EqualTo(NetworkWireCodecStatus.MalformedLength));
        Span<byte> oversized = stackalloc byte[HandshakeWireCodec.ConfirmationSizeInBytes + 1];
        buffer.CopyTo(oversized);
        Assert.That(
            HandshakeWireCodec.TryDecodeConfirmation(oversized, out _),
            Is.EqualTo(NetworkWireCodecStatus.TrailingBytes));

        BinaryPrimitives.WriteUInt64LittleEndian(buffer, 0);
        Assert.That(
            HandshakeWireCodec.TryDecodeConfirmation(buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
        Assert.That(
            HandshakeWireCodec.TryEncodeConfirmation(
                new SessionHandshakeConfirmation(Epoch, -1, 3, confirmation.ReconnectToken),
                buffer,
                out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
        Assert.That(
            HandshakeWireCodec.TryEncodeConfirmation(
                new SessionHandshakeConfirmation(Epoch, 1, 3, ReconnectToken.Empty),
                buffer,
                out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
    }

    [Test]
    public void CommandBatch_RoundTrip_UsesNetworkEntityHandles_AndOmitsPlayerId()
    {
        AssertCommandWireContractHasNoPlayerId();

        var header = new NetworkCommandBatchHeader(
            sessionEpoch: 7,
            clientBatchSequence: 3,
            targetTick: 100,
            acknowledgedCommittedTick: 98,
            entryCount: 2,
            OrderSubmitMode.Queued);
        Span<NetworkCommandWireEntry> entries = stackalloc NetworkCommandWireEntry[2];
        entries[0] = new NetworkCommandWireEntry(
            new NetworkEntityHandle(1, 2),
            orderTypeId: 10,
            NetworkCommandTargetPayload.FromWorldPositionCm(100, 200, 0));
        entries[1] = new NetworkCommandWireEntry(
            new NetworkEntityHandle(3, 4),
            orderTypeId: 11,
            NetworkCommandTargetPayload.FromNetworkEntity(5, 6));

        Span<byte> buffer = stackalloc byte[CommandBatchWireCodec.GetPayloadSize(2)];
        Assert.That(
            CommandBatchWireCodec.TryEncode(in header, entries, buffer, out int written),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(written, Is.EqualTo(CommandBatchWireCodec.GetPayloadSize(2)));

        Span<NetworkCommandWireEntry> decodedEntries = stackalloc NetworkCommandWireEntry[2];
        Assert.That(
            CommandBatchWireCodec.TryDecode(buffer, decodedEntries, out NetworkCommandBatchHeader decodedHeader, out int count),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(count, Is.EqualTo(2));
        Assert.That(decodedHeader.ClientBatchSequence, Is.EqualTo(3UL));
        Assert.That(decodedHeader.AcknowledgedCommittedTick, Is.EqualTo(98));
        Assert.That(decodedHeader.SubmitMode, Is.EqualTo(OrderSubmitMode.Queued));
        Assert.That(decodedEntries[0].Actor, Is.EqualTo(new NetworkEntityHandle(1, 2)));
        Assert.That(decodedEntries[1].Target.TargetSlot, Is.EqualTo(5));
        Assert.That(decodedEntries[1].Target.TargetGeneration, Is.EqualTo(6u));
    }

    [Test]
    public void CommandAdmissionOutcome_RoundTrip()
    {
        var seat = new NetworkCommandSeat(slot: 1, generation: 2, playerId: 9);
        var outcome = new NetworkCommandAdmissionOutcome(
            in seat,
            clientBatchSequence: 4,
            targetTick: 50,
            actorCount: 2,
            orderId: 12,
            admissionBatchId: 3,
            NetworkCommandAdmissionCode.Queued,
            isReplay: false,
            committedTick: 49);

        Span<byte> buffer = stackalloc byte[CommandAdmissionWireCodec.SizeInBytes];
        Assert.That(CommandAdmissionWireCodec.TryEncode(7, in outcome, buffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(
            CommandAdmissionWireCodec.TryDecode(buffer, 7, in seat, out NetworkCommandAdmissionOutcome decoded),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(decoded.PlayerId, Is.EqualTo(9));
        Assert.That(decoded.Result, Is.EqualTo(NetworkCommandAdmissionCode.Queued));
        Assert.That(decoded.Stage, Is.EqualTo(NetworkCommandAdmissionStage.GlobalIntake));
        Assert.That(decoded.IsReplay, Is.False);
        Assert.That(decoded.CommittedTick, Is.EqualTo(49));
    }

    [Test]
    public void EntityAdmissionOutcome_RoundTrip_PreservesBatchRowAndStage()
    {
        var seat = new NetworkCommandSeat(slot: 1, generation: 2, playerId: 9);
        var outcome = new NetworkCommandAdmissionOutcome(
            in seat,
            clientBatchSequence: 4,
            targetTick: 50,
            actorCount: 2,
            orderId: 13,
            admissionBatchId: 3,
            admissionBatchIndex: 1,
            NetworkCommandAdmissionStage.EntityIntake,
            NetworkCommandAdmissionCode.Activated,
            isReplay: false,
            committedTick: 51);

        Span<byte> buffer = stackalloc byte[CommandAdmissionWireCodec.SizeInBytes];
        Assert.That(
            CommandAdmissionWireCodec.TryEncode(7, in outcome, buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(
            CommandAdmissionWireCodec.TryDecode(buffer, 7, in seat, out NetworkCommandAdmissionOutcome decoded),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.Multiple(() =>
        {
            Assert.That(decoded.Stage, Is.EqualTo(NetworkCommandAdmissionStage.EntityIntake));
            Assert.That(decoded.Result, Is.EqualTo(NetworkCommandAdmissionCode.Activated));
            Assert.That(decoded.AdmissionBatchIndex, Is.EqualTo(1));
            Assert.That(decoded.OrderId, Is.EqualTo(13));
            Assert.That(decoded.CommittedTick, Is.EqualTo(51));
            Assert.That(decoded.AsReplay().CommittedTick, Is.EqualTo(51));
            Assert.That(decoded.AsReplay().IsReplay, Is.True);
        });
    }

    [Test]
    public void EntityAdmissionOutcome_RejectsMissingNegativeAndTruncatedCommittedTick()
    {
        var seat = new NetworkCommandSeat(slot: 1, generation: 2, playerId: 9);
        Assert.Multiple(() =>
        {
            Assert.That(
                () => new NetworkCommandAdmissionOutcome(
                    in seat,
                    clientBatchSequence: 4,
                    targetTick: 50,
                    actorCount: 1,
                    orderId: 13,
                    admissionBatchId: 3,
                    admissionBatchIndex: 0,
                    NetworkCommandAdmissionStage.EntityIntake,
                    NetworkCommandAdmissionCode.Activated,
                    isReplay: false,
                    committedTick: 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new NetworkCommandAdmissionOutcome(
                    in seat,
                    clientBatchSequence: 4,
                    targetTick: 50,
                    actorCount: 1,
                    orderId: 13,
                    admissionBatchId: 3,
                    NetworkCommandAdmissionCode.Queued,
                    isReplay: false,
                    committedTick: -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });

        var valid = new NetworkCommandAdmissionOutcome(
            in seat,
            clientBatchSequence: 4,
            targetTick: 50,
            actorCount: 1,
            orderId: 13,
            admissionBatchId: 3,
            admissionBatchIndex: 0,
            NetworkCommandAdmissionStage.EntityIntake,
            NetworkCommandAdmissionCode.Activated,
            isReplay: false,
            committedTick: 51);
        Span<byte> buffer = stackalloc byte[CommandAdmissionWireCodec.SizeInBytes];
        Assert.That(
            CommandAdmissionWireCodec.TryEncode(7, in valid, buffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(
            CommandAdmissionWireCodec.TryDecode(buffer[..^1], 7, in seat, out _),
            Is.EqualTo(NetworkWireCodecStatus.MalformedLength));

        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20, sizeof(int)), 0);
        Assert.That(
            CommandAdmissionWireCodec.TryDecode(buffer, 7, in seat, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20, sizeof(int)), -1);
        Assert.That(
            CommandAdmissionWireCodec.TryDecode(buffer, 7, in seat, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
    }

    [Test]
    public void CommandAdmissionOutcome_RejectsDifferentSessionEpoch()
    {
        var seat = new NetworkCommandSeat(slot: 1, generation: 2, playerId: 9);
        var outcome = new NetworkCommandAdmissionOutcome(
            in seat,
            clientBatchSequence: 4,
            targetTick: 50,
            actorCount: 2,
            orderId: 12,
            admissionBatchId: 3,
            NetworkCommandAdmissionCode.Queued,
            isReplay: false,
            committedTick: 49);

        Span<byte> buffer = stackalloc byte[CommandAdmissionWireCodec.SizeInBytes];
        Assert.That(CommandAdmissionWireCodec.TryEncode(7, in outcome, buffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(
            CommandAdmissionWireCodec.TryDecode(buffer, 8, in seat, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
    }

    [Test]
    public void CommandAdmissionOutcome_RoundTripsGameplayTerminalResultsAndRejectsUnknownResult()
    {
        var seat = new NetworkCommandSeat(slot: 1, generation: 2, playerId: 9);
        Span<byte> buffer = stackalloc byte[CommandAdmissionWireCodec.SizeInBytes];
        NetworkCommandAdmissionCode[] terminalResults =
        {
            NetworkCommandAdmissionCode.TerminalCompleted,
            NetworkCommandAdmissionCode.TerminalFailed,
            NetworkCommandAdmissionCode.TerminalCancelled,
        };
        foreach (NetworkCommandAdmissionCode code in terminalResults)
        {
            var outcome = new NetworkCommandAdmissionOutcome(
                in seat,
                clientBatchSequence: 4,
                targetTick: 50,
                actorCount: 2,
                orderId: 12,
                admissionBatchId: 3,
                admissionBatchIndex: 1,
                NetworkCommandAdmissionStage.Terminal,
                code,
                isReplay: false,
                committedTick: 51);
            Assert.That(
                CommandAdmissionWireCodec.TryEncode(7, in outcome, buffer, out _),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            Assert.That(
                CommandAdmissionWireCodec.TryDecode(buffer, 7, in seat, out NetworkCommandAdmissionOutcome decoded),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            Assert.Multiple(() =>
            {
                Assert.That(decoded.Result, Is.EqualTo(code));
                Assert.That(decoded.Stage, Is.EqualTo(NetworkCommandAdmissionStage.Terminal));
                Assert.That(decoded.AdmissionBatchIndex, Is.EqualTo(1));
            });
        }

        buffer[CommandAdmissionWireCodec.SizeInBytes - 1] = 1;
        Assert.That(
            CommandAdmissionWireCodec.TryDecode(buffer, 7, in seat, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidEnum));
        buffer[CommandAdmissionWireCodec.SizeInBytes - 1] = 0;

        buffer[CommandAdmissionWireCodec.SizeInBytes - 3] =
            checked((byte)((byte)NetworkCommandAdmissionCode.TerminalCancelled + 1));
        Assert.That(
            CommandAdmissionWireCodec.TryDecode(buffer, 7, in seat, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidEnum));
    }

    [Test]
    public void ReplicationPacket_FullAndDelta_RoundTrip()
    {
        var visible = new NetworkEntityHandle(0, 1);
        var hidden = new NetworkEntityHandle(1, 1);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 8);
        var channel = new AuthoritativeReplicationChannel(entityCapacity: 4, baselineCapacity: 2, disclosureLog);
        var packet = new ReplicationPacketBuffer(entityCapacity: 4);
        var states = new[]
        {
            new ReplicatedEntityState(visible, schemaId: 1, revision: 10, new ReplicationStateVector(1, 2, 3, 4)),
            new ReplicatedEntityState(hidden, schemaId: 1, revision: 20, new ReplicationStateVector(5, 6, 7, 8)),
        };
        var disclosures = new[]
        {
            new ReplicationDisclosureInput(visible, KnowledgePresence.LiveVisible),
            new ReplicationDisclosureInput(hidden, KnowledgePresence.HiddenWithSource),
        };

        Assert.That(
            channel.BuildFull(7, 100, 1, states, disclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));

        Span<byte> buffer = stackalloc byte[ReplicationPacketWireCodec.GetPayloadSize(
            packet.UpsertCount,
            packet.RemovalCount,
            packet.DisclosureChangeCount)];
        Assert.That(ReplicationPacketWireCodec.TryEncode(packet, buffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));

        var decoded = new ReplicationPacketBuffer(entityCapacity: 4);
        Assert.That(ReplicationPacketWireCodec.TryDecode(buffer, decoded), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(decoded.Header.Kind, Is.EqualTo(ReplicationPacketKind.Full));
        Assert.That(decoded.Upserts.Length, Is.EqualTo(1));
        Assert.That(decoded.Upserts[0].Entity, Is.EqualTo(visible));
        Assert.That(decoded.DisclosureChanges.Length, Is.EqualTo(1));

        var nextStates = new[]
        {
            new ReplicatedEntityState(visible, schemaId: 1, revision: 11, new ReplicationStateVector(9, 8, 7, 6)),
        };
        var nextDisclosures = new[]
        {
            new ReplicationDisclosureInput(visible, KnowledgePresence.LiveVisible),
        };
        Assert.That(
            channel.BuildDelta(7, 103, 2, acknowledgedBaselineId: 1, nextStates, nextDisclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));

        Span<byte> deltaBuffer = stackalloc byte[ReplicationPacketWireCodec.GetPayloadSize(
            packet.UpsertCount,
            packet.RemovalCount,
            packet.DisclosureChangeCount)];
        Assert.That(ReplicationPacketWireCodec.TryEncode(packet, deltaBuffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(ReplicationPacketWireCodec.TryDecode(deltaBuffer, decoded), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(decoded.Header.Kind, Is.EqualTo(ReplicationPacketKind.Delta));
        Assert.That(decoded.Header.BaselineSnapshotId, Is.EqualTo(1UL));
        Assert.That(decoded.Upserts[0].Revision, Is.EqualTo(11u));
    }

    [Test]
    public void SnapshotAck_AndResyncRequired_RoundTrip()
    {
        var ack = new NetworkSnapshotAcknowledgement(sessionEpoch: 7, snapshotId: 9, committedTick: 120);
        Span<byte> ackBuffer = stackalloc byte[NetworkSnapshotAcknowledgement.SizeInBytes];
        Assert.That(SnapshotControlWireCodec.TryEncodeAcknowledgement(in ack, ackBuffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(SnapshotControlWireCodec.TryDecodeAcknowledgement(ackBuffer, out NetworkSnapshotAcknowledgement decodedAck), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(decodedAck.SnapshotId, Is.EqualTo(9UL));
        Assert.That(decodedAck.CommittedTick, Is.EqualTo(120u));

        var resync = new NetworkResyncRequired(
            sessionEpoch: 7,
            NetworkResyncReason.SnapshotAcknowledgementTimeout,
            latestCommittedTick: 130,
            latestSnapshotId: 11);
        Span<byte> resyncBuffer = stackalloc byte[NetworkResyncRequired.SizeInBytes];
        Assert.That(SnapshotControlWireCodec.TryEncodeResyncRequired(in resync, resyncBuffer, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(SnapshotControlWireCodec.TryDecodeResyncRequired(resyncBuffer, out NetworkResyncRequired decodedResync), Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(decodedResync.Reason, Is.EqualTo(NetworkResyncReason.SnapshotAcknowledgementTimeout));
        Assert.That(decodedResync.LatestSnapshotId, Is.EqualTo(11UL));
    }

    [Test]
    public void MalformedBoundaries_ReturnExplicitStatuses()
    {
        Span<byte> tiny = stackalloc byte[1];
        Assert.That(
            NetworkWireEnvelopeCodec.TryDecode(tiny, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.MalformedLength));

        Span<byte> envelope = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(envelope, NetworkWireEnvelope.Magic);
        envelope[4] = 99;
        envelope[5] = (byte)NetworkWireKind.CommandBatch;
        BinaryPrimitives.WriteUInt16LittleEndian(envelope.Slice(6, 2), 0);
        Assert.That(
            NetworkWireEnvelopeCodec.TryDecode(envelope, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.UnknownVersion));

        envelope[4] = NetworkWireEnvelope.CurrentVersion;
        envelope[5] = 255;
        Assert.That(
            NetworkWireEnvelopeCodec.TryDecode(envelope, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.UnknownKind));

        Span<byte> framed = stackalloc byte[9];
        Assert.That(
            NetworkWireEnvelopeCodec.TryEncode(NetworkWireKind.SnapshotAcknowledgement, ReadOnlySpan<byte>.Empty, framed.Slice(0, 8), out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        framed[8] = 0xAB;
        Assert.That(
            NetworkWireEnvelopeCodec.TryDecode(framed, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.TrailingBytes));

        Span<byte> badMagic = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(badMagic, 0xDEADBEEF);
        badMagic[4] = 1;
        badMagic[5] = 1;
        Assert.That(
            NetworkWireEnvelopeCodec.TryDecode(badMagic, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.UnknownSchema));

        Span<byte> command = stackalloc byte[NetworkCommandBatchHeader.SizeInBytes];
        command.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(command.Slice(24, 2), 1);
        Assert.That(
            CommandBatchWireCodec.TryDecode(command, stackalloc NetworkCommandWireEntry[1], out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.MalformedLength));

        var header = new NetworkCommandBatchHeader(1, 1, 1, 1, 1, OrderSubmitMode.Immediate);
        Span<NetworkCommandWireEntry> invalidHandleEntries = stackalloc NetworkCommandWireEntry[1];
        invalidHandleEntries[0] = new NetworkCommandWireEntry(default, 1, NetworkCommandTargetPayload.None);
        Span<byte> commandBuffer = stackalloc byte[CommandBatchWireCodec.GetPayloadSize(1)];
        Assert.That(
            CommandBatchWireCodec.TryEncode(in header, invalidHandleEntries, commandBuffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidHandle));

        Span<NetworkCommandWireEntry> invalidEnumEntries = stackalloc NetworkCommandWireEntry[1];
        invalidEnumEntries[0] = new NetworkCommandWireEntry(
            new NetworkEntityHandle(1, 1),
            1,
            new NetworkCommandTargetPayload((NetworkCommandTargetKind)200, 0, 0, 0, 0, 0, 0, 0));
        Assert.That(
            CommandBatchWireCodec.TryEncode(in header, invalidEnumEntries, commandBuffer, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidEnum));

        // Valid encode then truncate mid-entry ->MalformedLength.
        Assert.That(
            CommandBatchWireCodec.TryEncode(
                new NetworkCommandBatchHeader(1, 1, 1, 1, 1, OrderSubmitMode.Immediate),
                new[]
                {
                    new NetworkCommandWireEntry(
                        new NetworkEntityHandle(1, 1),
                        1,
                        NetworkCommandTargetPayload.FromWorldPositionCm(1, 2, 3)),
                },
                commandBuffer,
                out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(
            CommandBatchWireCodec.TryDecode(commandBuffer[..^1], stackalloc NetworkCommandWireEntry[1], out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.MalformedLength));

        Assert.That(
            CommandBatchWireCodec.TryDecode(commandBuffer, stackalloc NetworkCommandWireEntry[0], out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.CapacityExhausted));

        Span<byte> overflowDest = stackalloc byte[4];
        Assert.That(
            HandshakeWireCodec.TryEncodeRequest(
                new SessionHandshakeRequest(Protocol, Content),
                overflowDest,
                out _),
            Is.EqualTo(NetworkWireCodecStatus.BufferTooSmall));

        var smallPacket = new ReplicationPacketBuffer(entityCapacity: 1);
        Span<byte> largeReplication = stackalloc byte[ReplicationPacketWireCodec.GetPayloadSize(2, 0, 0)];
        // Craft header declaring 2 upserts into a capacity-1 packet.
        largeReplication.Clear();
        largeReplication[0] = (byte)ReplicationPacketKind.Full;
        BinaryPrimitives.WriteUInt16LittleEndian(largeReplication.Slice(1 + 3 + 8 + 4 + 8 + 8, 2), 2);
        Assert.That(
            ReplicationPacketWireCodec.TryDecode(largeReplication, smallPacket),
            Is.EqualTo(NetworkWireCodecStatus.CapacityExhausted));

        Span<byte> resync = stackalloc byte[NetworkResyncRequired.SizeInBytes];
        resync.Clear();
        resync[8] = 255;
        Assert.That(
            SnapshotControlWireCodec.TryDecodeResyncRequired(resync, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidEnum));

        Span<byte> ack = stackalloc byte[NetworkSnapshotAcknowledgement.SizeInBytes + 1];
        ack.Clear();
        Assert.That(
            SnapshotControlWireCodec.TryDecodeAcknowledgement(ack, out _),
            Is.EqualTo(NetworkWireCodecStatus.TrailingBytes));
    }

    private static void AssertCommandWireContractHasNoPlayerId()
    {
        foreach (Type type in new[]
                 {
                     typeof(NetworkCommandBatchHeader),
                     typeof(NetworkCommandWireEntry),
                     typeof(NetworkCommandTargetPayload),
                 })
        {
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            Assert.That(
                properties.Any(p => p.Name.Contains("Player", StringComparison.OrdinalIgnoreCase)),
                Is.False,
                $"{type.Name} must not expose PlayerId on the command wire contract.");

            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(
                fields.Any(f => f.Name.Contains("Player", StringComparison.OrdinalIgnoreCase)),
                Is.False,
                $"{type.Name} must not store PlayerId on the command wire contract.");
        }
    }
}
