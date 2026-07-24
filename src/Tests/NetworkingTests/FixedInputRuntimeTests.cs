using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Simulation;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class FixedInputRuntimeTests
{
    private const ushort SchemaId = 1;
    private const ushort PayloadBytes = 12;

    [Test]
    public void EndToEnd_HandshakeSubmitFutureFrame_InputSequenced_PresentCommitAckRemovesFrame()
    {
        using FixedInputHarness harness = CreateHarness(statePublishRateHz: 10);
        Handshake(harness);

        Span<byte> frame = stackalloc byte[PayloadBytes];
        frame.Fill(0xAB);
        Assert.That(harness.Client.TrySubmitFixedInput(targetTick: 1, frame), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Assert.That(
            harness.Client.TryPulseFixedInputSend().Status,
            Is.EqualTo(FixedInputSendPulseStatus.Accepted));
        Assert.That(harness.Transport.ClientFixedInputBatchCount, Is.EqualTo(1));
        Assert.That(harness.Transport.LastClientSendChannel, Is.EqualTo(harness.Capacity.InputChannel));

        harness.Server.PumpTransport();
        Assert.That(harness.Observer.Faults, Is.Zero);

        harness.TickState.Begin(1);
        harness.Server.BeforeAuthoritativeTick(1);
        Span<byte> lookup = stackalloc byte[PayloadBytes];
        Assert.That(
            harness.Server.TryGetFixedInput(harness.Client.Seat, tick: 1, lookup, out int written),
            Is.EqualTo(FixedInputLookupResult.Present));
        Assert.That(written, Is.EqualTo(PayloadBytes));
        Assert.That(lookup.ToArray(), Is.EqualTo(frame.ToArray()));
        harness.TickState.Commit(1);
        harness.Server.AfterAuthoritativeCommit(1);

        Assert.That(harness.Transport.ServerFixedInputAckCount, Is.EqualTo(1));
        Assert.That(harness.Transport.LastServerSendChannel, Is.EqualTo(harness.Capacity.InputChannel));
        Assert.That(harness.Client.FixedInputPendingCount, Is.EqualTo(1));
        harness.Client.PumpTransport();
        Assert.That(harness.Client.FixedInputPendingCount, Is.EqualTo(0));
        Assert.That(harness.Observer.Faults, Is.Zero);
    }

    [Test]
    public void MissingAtDeadline_DoesNotFabricateInput()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);

        harness.TickState.Begin(1);
        harness.Server.BeforeAuthoritativeTick(1);
        Span<byte> lookup = stackalloc byte[PayloadBytes];
        Assert.That(
            harness.Server.TryGetFixedInput(harness.Client.Seat, tick: 1, lookup, out int written),
            Is.EqualTo(FixedInputLookupResult.MissingAtDeadline));
        Assert.That(written, Is.Zero);
        Assert.That(lookup.ToArray(), Is.All.EqualTo(0));
        harness.TickState.Commit(1);
        harness.Server.AfterAuthoritativeCommit(1);
        Assert.That(harness.Server.FixedInputMissingAtDeadlineCount, Is.EqualTo(1));
    }

    [Test]
    public void Ack_IsSentEvenWhenSnapshotCadenceSkipsPublish()
    {
        using FixedInputHarness harness = CreateHarness(statePublishRateHz: 10);
        Handshake(harness);

        harness.TickState.Begin(1);
        harness.Server.BeforeAuthoritativeTick(1);
        harness.TickState.Commit(1);
        harness.Server.AfterAuthoritativeCommit(1);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Transport.ServerFixedInputAckCount, Is.EqualTo(1));
            Assert.That(harness.Transport.ServerSnapshotFragmentCount, Is.Zero);
            Assert.That(harness.Transport.ServerReplicationPacketCount, Is.Zero);
        });
    }

    [Test]
    public void BeforeAuthoritativeTick_MismatchedOrOutOfDomainTick_FailsFast()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);

        Assert.That(
            () => harness.Server.BeforeAuthoritativeTick(0),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harness.Server.IsFaulted, Is.True);
        Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.SessionContractViolation));
    }

    [Test]
    public void BeforeAuthoritativeTick_WithoutMatchingExecutingTickState_FailsFast()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);

        Assert.That(
            () => harness.Server.BeforeAuthoritativeTick(1),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.SessionContractViolation));
    }

    [Test]
    public void AfterAuthoritativeCommit_WhileStillExecutingOrMismatched_FailsFast()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);

        harness.TickState.Begin(1);
        harness.Server.BeforeAuthoritativeTick(1);
        Assert.That(
            () => harness.Server.AfterAuthoritativeCommit(1),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationBuildRejected));
    }

    [Test]
    public void AfterAuthoritativeCommit_MismatchedCommittedTick_FailsFast()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);

        harness.TickState.Begin(1);
        harness.Server.BeforeAuthoritativeTick(1);
        harness.TickState.Commit(1);
        Assert.That(
            () => harness.Server.AfterAuthoritativeCommit(2),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationBuildRejected));
    }

    [Test]
    public void BatchHeader_AcknowledgesLatestInputAck_NotStaleReplicationTick()
    {
        // 10Hz snapshot cadence (interval=3) vs 30Hz input ACK every commit.
        using FixedInputHarness harness = CreateHarness(statePublishRateHz: 10);
        Handshake(harness);

        RunAuthoritativeFrame(harness, 1);
        RunAuthoritativeFrame(harness, 2);
        harness.Client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Client.LastCommittedTick, Is.EqualTo(0u), "No replication snapshot yet.");
            Assert.That(harness.Client.FixedInputAcknowledgedCommittedTick, Is.EqualTo(2u));
            Assert.That(harness.Transport.ServerFixedInputAckCount, Is.EqualTo(2));
            Assert.That(harness.Transport.ServerSnapshotFragmentCount, Is.Zero);
            Assert.That(harness.Transport.ServerReplicationPacketCount, Is.Zero);
        });

        Span<byte> frame = stackalloc byte[PayloadBytes];
        frame.Fill(0x44);
        Assert.That(harness.Client.TrySubmitFixedInput(3, frame), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Assert.That(
            harness.Client.TryPulseFixedInputSend().Status,
            Is.EqualTo(FixedInputSendPulseStatus.Accepted));
        Assert.That(harness.Transport.LastClientBatchHeader.AcknowledgedCommittedTick, Is.EqualTo(2u));
    }

    [Test]
    public void FixedInputAck_NotReady_KeepsOneLatestSlotPerSeat()
    {
        using FixedInputHarness harness = CreateHarness(statePublishRateHz: 10);
        Handshake(harness);

        harness.Transport.BlockFixedInputAckSends = true;
        RunAuthoritativeFrame(harness, 1);
        Assert.That(harness.Transport.ServerFixedInputAckCount, Is.Zero);
        RunAuthoritativeFrame(harness, 2);
        Assert.That(harness.Transport.ServerFixedInputAckCount, Is.Zero);
        RunAuthoritativeFrame(harness, 3);
        Assert.That(harness.Transport.ServerFixedInputAckCount, Is.Zero);

        harness.Transport.BlockFixedInputAckSends = false;
        harness.Server.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Transport.ServerFixedInputAckCount, Is.EqualTo(1));
            Assert.That(harness.Transport.LastServerFixedInputAck.CommittedThroughTick, Is.EqualTo(3u));
        });

        harness.Client.PumpTransport();
        Assert.That(harness.Client.FixedInputAcknowledgedCommittedTick, Is.EqualTo(3u));
    }

    [Test]
    public void PumpReplicatedClient_RepeatedCalls_NeverPulseFixedInput()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);

        Span<byte> frame = stackalloc byte[PayloadBytes];
        frame.Fill(0x55);
        Assert.That(harness.Client.TrySubmitFixedInput(1, frame), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Assert.That(harness.Client.FixedInputPendingCount, Is.EqualTo(1));

        for (int i = 0; i < 144; i++)
        {
            harness.Client.PumpReplicatedClient(1f / 144f);
        }

        Assert.That(harness.Transport.ClientFixedInputBatchCount, Is.Zero);
        Assert.That(
            harness.Client.TryPulseFixedInputSend().Status,
            Is.EqualTo(FixedInputSendPulseStatus.Accepted));
        Assert.That(harness.Transport.ClientFixedInputBatchCount, Is.EqualTo(1));
    }

    [Test]
    public void WrongChannelOrWireKind_IsRejected()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);

        Span<byte> payload = stackalloc byte[NetworkFixedInputAcknowledgement.SizeInBytes];
        var ack = new NetworkFixedInputAcknowledgement(77, SchemaId, 0, 0, 0, 0);
        Assert.That(FixedInputWireCodec.TryEncodeAcknowledgement(in ack, payload, out int bytes), Is.EqualTo(NetworkWireCodecStatus.Success));
        harness.Transport.EnqueueClientFrame(harness.Capacity.ControlChannel, NetworkWireKind.FixedInputBatch, payload[..bytes]);
        harness.Server.PumpTransport();
        Assert.That(harness.Observer.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.UnexpectedChannel));

        harness.Observer.ResetFaults();
        harness.Transport.EnqueueClientFrame(harness.Capacity.InputChannel, NetworkWireKind.CommandFragment, payload[..bytes]);
        harness.Server.PumpTransport();
        Assert.That(harness.Observer.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.UnexpectedChannel));

        harness.Observer.ResetFaults();
        harness.Transport.EnqueueServerFrame(harness.Capacity.ControlChannel, NetworkWireKind.FixedInputAcknowledgement, payload[..bytes]);
        harness.Client.PumpTransport();
        Assert.That(harness.Observer.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.UnexpectedChannel));
    }

    [Test]
    public void SpoofedSeat_CannotBypassAuthenticatedBinding()
    {
        using FixedInputHarness harness = CreateHarness(connectionCapacity: 2, seatCapacity: 2);
        Handshake(harness);

        // Batch wire has no seat fields; authority comes only from the authenticated connection seat.
        Span<byte> frame = stackalloc byte[PayloadBytes];
        frame.Fill(0x11);
        Assert.That(harness.Client.TrySubmitFixedInput(3, frame), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Assert.That(
            harness.Client.TryPulseFixedInputSend().Status,
            Is.EqualTo(FixedInputSendPulseStatus.Accepted));
        harness.Server.PumpTransport();

        var foreignSeat = new SessionSeatBinding(1, 1, new PlayerId(2));
        Span<byte> lookup = stackalloc byte[PayloadBytes];
        Assert.That(
            harness.Server.TryGetFixedInput(in foreignSeat, 3, lookup, out _),
            Is.EqualTo(FixedInputLookupResult.InvalidSeat));
        Assert.That(
            harness.Server.TryGetFixedInput(harness.Client.Seat, 3, lookup, out _),
            Is.EqualTo(FixedInputLookupResult.Present));
    }

    [Test]
    public void SameGenerationReconnect_ClearsIngressHistoryAndPreservesClientOutboxForResend()
    {
        using FixedInputHarness harness = CreateHarness(reconnectWindowTicks: 8);
        Handshake(harness);
        AuthoritativeReplicationSeatRuntime? runtimeBefore = harness.ReplicationFactory.LastAcquiredRuntime;

        Span<byte> frame = stackalloc byte[PayloadBytes];
        frame.Fill(0x22);
        Assert.That(harness.Client.TrySubmitFixedInput(4, frame), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Assert.That(
            harness.Client.TryPulseFixedInputSend().Status,
            Is.EqualTo(FixedInputSendPulseStatus.Accepted));
        harness.Server.PumpTransport();

        SessionSeatBinding seatBefore = harness.Client.Seat;
        int pendingBefore = harness.Client.FixedInputPendingCount;
        harness.Transport.Disconnect();
        harness.Server.PumpTransport();
        harness.Client.PumpTransport();

        Assert.That(harness.Client.FixedInputPendingCount, Is.EqualTo(pendingBefore));
        Assert.That(harness.Client.TryConnectNow(), Is.True);
        harness.Client.PumpTransport();
        harness.Server.PumpTransport();
        harness.Client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(harness.Client.Seat, Is.EqualTo(seatBefore));
            Assert.That(harness.Client.FixedInputPendingCount, Is.EqualTo(pendingBefore));
            Assert.That(harness.Observer.SeatReconnections, Is.EqualTo(1));
            Assert.That(harness.ReplicationFactory.AcquireCount, Is.EqualTo(1));
            Assert.That(harness.ReplicationFactory.ReleaseCount, Is.Zero);
            Assert.That(harness.ReplicationFactory.LastAcquiredRuntime, Is.SameAs(runtimeBefore));
        });

        Span<byte> lookup = stackalloc byte[PayloadBytes];
        Assert.That(
            harness.Server.TryGetFixedInput(seatBefore, 4, lookup, out _),
            Is.EqualTo(FixedInputLookupResult.Missing));

        Assert.That(harness.Client.TryPulseFixedInputSend().IsAccepted, Is.True);
        harness.Server.PumpTransport();
        Assert.That(
            harness.Server.TryGetFixedInput(seatBefore, 4, lookup, out _),
            Is.EqualTo(FixedInputLookupResult.Present));
    }

    [Test]
    public void SeatGenerationReleaseAndEpochMismatch_ClearOutboxExactlyOnce()
    {
        using FixedInputHarness harness = CreateHarness(reconnectWindowTicks: 1);
        Handshake(harness);
        AuthoritativeReplicationSeatRuntime? firstRuntime = harness.ReplicationFactory.LastAcquiredRuntime;
        Span<byte> frame = stackalloc byte[PayloadBytes];
        frame.Fill(0x33);
        Assert.That(harness.Client.TrySubmitFixedInput(2, frame), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Assert.That(harness.Client.FixedInputPendingCount, Is.EqualTo(1));

        harness.Transport.Disconnect();
        harness.Server.PumpTransport();
        harness.Client.PumpTransport();
        RunAuthoritativeFrame(harness, 1);
        harness.TickState.Begin(2);
        harness.Server.BeforeAuthoritativeTick(2);
        harness.TickState.Commit(2);
        harness.Server.AfterAuthoritativeCommit(2);
        Assert.That(harness.Observer.SeatReleases, Is.EqualTo(1));
        Assert.That(harness.ReplicationFactory.ReleaseCount, Is.EqualTo(1));
        Assert.That(harness.ReplicationFactory.LastReleasedRuntime, Is.SameAs(firstRuntime));

        // Stale reconnect token after release clears credentials and outbox once.
        Assert.That(harness.Client.TryConnectNow(), Is.True);
        harness.Client.PumpTransport();
        harness.Server.PumpTransport();
        harness.Client.PumpTransport();
        Assert.That(harness.Client.State, Is.EqualTo(ReplicatedClientConnectionState.Disconnected));
        Assert.That(harness.Client.FixedInputPendingCount, Is.Zero);

        // Fresh join after clear receives a new seat generation with an empty outbox.
        Assert.That(harness.Client.TryConnectNow(), Is.True);
        harness.Client.PumpTransport();
        harness.Server.PumpTransport();
        harness.Client.PumpTransport();
        Assert.That(harness.Client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
        Assert.That(harness.Client.Seat.Generation, Is.EqualTo(2));
        Assert.That(harness.Client.FixedInputPendingCount, Is.Zero);
        Assert.That(harness.ReplicationFactory.AcquireCount, Is.EqualTo(2));
        Assert.That(harness.ReplicationFactory.LastAcquiredRuntime, Is.Not.SameAs(firstRuntime));

        Assert.That(harness.Client.TrySubmitFixedInput(3, frame), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Assert.That(harness.Client.FixedInputPendingCount, Is.EqualTo(1));

        SessionHandshakeResponse rejected = SessionHandshakeResponse.Reject(
            HandshakeRejectReason.SessionEpochMismatch,
            harness.Protocol,
            harness.Fingerprint,
            new SessionEpoch(999));
        Span<byte> payload = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(
            HandshakeWireCodec.TryEncodeResponse(in rejected, payload, out int payloadBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        harness.Transport.Disconnect();
        harness.Server.PumpTransport();
        harness.Client.PumpTransport();
        harness.Transport.ConnectClientOnly();
        harness.Client.PumpTransport();
        harness.Transport.EnqueueServerFrame(
            harness.Capacity.ControlChannel,
            NetworkWireKind.SessionHandshakeResponse,
            payload[..payloadBytes]);
        harness.Client.PumpTransport();
        Assert.That(harness.Client.FixedInputPendingCount, Is.Zero);
        // Second clear path is idempotent.
        harness.Client.PumpTransport();
        Assert.That(harness.Client.FixedInputPendingCount, Is.Zero);
    }

    [Test]
    public void ServerDispose_ReleasesConnectedReplicationRuntimeExactlyOnce()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);
        AuthoritativeReplicationSeatRuntime? acquired = harness.ReplicationFactory.LastAcquiredRuntime;

        harness.Server.Dispose();
        harness.Server.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(harness.ReplicationFactory.AcquireCount, Is.EqualTo(1));
            Assert.That(harness.ReplicationFactory.ReleaseCount, Is.EqualTo(1));
            Assert.That(harness.ReplicationFactory.LastReleasedRuntime, Is.SameAs(acquired));
        });
    }

    [TestCase(1, 0, 0)]
    [TestCase(0, 1, 0)]
    [TestCase(0, 0, 1)]
    public void ReplicationFactoryCapacityMismatch_FailsAtServerConstruction(
        int seatAdjustment,
        int globalAdjustment,
        int perSeatAdjustment)
    {
        Assert.That(
            () => CreateHarness(
                replicationFactorySeatCapacityAdjustment: seatAdjustment,
                replicationFactoryGlobalCapacityAdjustment: globalAdjustment,
                replicationFactoryPerSeatCapacityAdjustment: perSeatAdjustment),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("replicationSeatFactory"));
    }

    [Test]
    public void ReplicationFactoryAcquireFailure_FaultsAuthenticatedBinding()
    {
        using FixedInputHarness harness = CreateHarness(replicationAcquireSucceeds: false);

        Assert.That(() => Handshake(harness), Throws.TypeOf<NetworkRuntimeException>());
        Assert.Multiple(() =>
        {
            Assert.That(harness.Server.IsFaulted, Is.True);
            Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected));
            Assert.That(harness.ReplicationFactory.AcquireCount, Is.EqualTo(1));
            Assert.That(harness.ReplicationFactory.ReleaseCount, Is.Zero);
        });
    }

    [Test]
    public void ReplicationFactoryDirtyRuntime_IsReleasedAndRejectedBeforeSeatBinding()
    {
        using FixedInputHarness harness = CreateHarness(dirtyReplicationRuntime: true);

        Assert.That(() => Handshake(harness), Throws.TypeOf<NetworkRuntimeException>());
        Assert.Multiple(() =>
        {
            Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected));
            Assert.That(harness.ReplicationFactory.AcquireCount, Is.EqualTo(1));
            Assert.That(harness.ReplicationFactory.ReleaseCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ReplicationFactoryReleaseFailure_FaultsAndDoesNotReleaseTwiceOnDispose()
    {
        using FixedInputHarness harness = CreateHarness(
            reconnectWindowTicks: 1,
            replicationReleaseSucceeds: false);
        Handshake(harness);
        harness.Transport.Disconnect();
        harness.Server.PumpTransport();
        harness.Client.PumpTransport();
        RunAuthoritativeFrame(harness, 1);

        Assert.That(() => RunAuthoritativeFrame(harness, 2), Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected));
        Assert.That(harness.ReplicationFactory.ReleaseCount, Is.EqualTo(1));

        harness.Server.Dispose();
        Assert.That(harness.ReplicationFactory.ReleaseCount, Is.EqualTo(1));
    }

    [Test]
    public void BindingFailureAfterAcquire_ReleasesReplicationRuntimeBeforeFaulting()
    {
        var conflictingSeat = new SessionSeatBinding(0, 99, new PlayerId(1));
        using FixedInputHarness harness = CreateHarness(fixedInputPrebind: conflictingSeat);

        Assert.That(() => Handshake(harness), Throws.TypeOf<NetworkRuntimeException>());
        Assert.Multiple(() =>
        {
            Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected));
            Assert.That(harness.ReplicationFactory.AcquireCount, Is.EqualTo(1));
            Assert.That(harness.ReplicationFactory.ReleaseCount, Is.EqualTo(1));
            Assert.That(harness.ReplicationFactory.LastReleasedRuntime, Is.SameAs(harness.ReplicationFactory.LastAcquiredRuntime));
        });
    }

    [Test]
    public void NoData_DoesNotSend()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);
        Assert.That(
            harness.Client.TryPulseFixedInputSend().Status,
            Is.EqualTo(FixedInputSendPulseStatus.NoData));
        Assert.That(harness.Transport.ClientFixedInputBatchCount, Is.Zero);
        harness.Client.PumpReplicatedClient(1f);
        Assert.That(harness.Transport.ClientFixedInputBatchCount, Is.Zero);
    }

    [Test]
    public void FullRedundancyBatch_PulseReportsAcceptedRangeIncludingNewestFrame()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);

        Span<byte> frame = stackalloc byte[PayloadBytes];
        for (uint tick = 1; tick <= 5; tick++)
        {
            frame.Fill((byte)tick);
            Assert.That(
                harness.Client.TrySubmitFixedInput(tick, frame),
                Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        }

        FixedInputSendPulseResult pulse = harness.Client.TryPulseFixedInputSend();

        Assert.Multiple(() =>
        {
            Assert.That(pulse.Status, Is.EqualTo(FixedInputSendPulseStatus.Accepted));
            Assert.That(pulse.AcceptedFrameCount, Is.EqualTo(4));
            Assert.That(pulse.FirstAcceptedTargetTick, Is.EqualTo(1u));
            Assert.That(pulse.HighestAcceptedTargetTick, Is.EqualTo(5u));
            Assert.That(harness.Transport.ClientFixedInputBatchCount, Is.EqualTo(1));
            Assert.That(harness.Transport.LastClientBatchFirstTargetTick, Is.EqualTo(1u));
            Assert.That(harness.Transport.LastClientBatchHighestTargetTick, Is.EqualTo(5u));
        });
    }

    [Test]
    public void TransportClosed_PulseDoesNotReportSendSuccess()
    {
        using FixedInputHarness harness = CreateHarness();
        Handshake(harness);

        Span<byte> frame = stackalloc byte[PayloadBytes];
        frame.Fill(0x3C);
        Assert.That(harness.Client.TrySubmitFixedInput(1, frame), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        harness.Transport.CloseClientSends = true;

        Assert.That(
            harness.Client.TryPulseFixedInputSend().Status,
            Is.EqualTo(FixedInputSendPulseStatus.TransportRejected));
        Assert.That(harness.Transport.ClientFixedInputBatchCount, Is.Zero);
        Assert.That(harness.Observer.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.TransportClosed));
    }

    [Test]
    public void SteadyState_NonemptySuccessPath_AllocatesZeroManagedBytes()
    {
        using FixedInputHarness harness = CreateHarness(statePublishRateHz: 1);
        Handshake(harness);

        var frame = new byte[PayloadBytes];
        var lookup = new byte[PayloadBytes];

        void RunOnce(uint tick, bool assertOutcomes)
        {
            frame.AsSpan().Fill((byte)(tick & 0xFF));
            FixedInputOutboxEnqueueStatus enqueued = harness.Client.TrySubmitFixedInput(tick, frame);
            FixedInputSendPulseResult pulsed = harness.Client.TryPulseFixedInputSend();
            harness.Server.PumpTransport();
            harness.TickState.Begin(checked((int)tick));
            harness.Server.BeforeAuthoritativeTick(tick);
            FixedInputLookupResult lookupResult = harness.Server.TryGetFixedInput(
                harness.Client.Seat,
                tick,
                lookup,
                out _);
            harness.TickState.Commit(checked((int)tick));
            harness.Server.AfterAuthoritativeCommit(tick);
            harness.Client.PumpTransport();
            if (assertOutcomes)
            {
                Assert.That(enqueued, Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
                Assert.That(pulsed.Status, Is.EqualTo(FixedInputSendPulseStatus.Accepted));
                Assert.That(pulsed.HighestAcceptedTargetTick, Is.EqualTo(tick));
                Assert.That(lookupResult, Is.EqualTo(FixedInputLookupResult.Present));
            }
        }

        // Warmup through the first state-publish boundary (interval = 30).
        for (uint i = 1; i <= 30; i++)
        {
            RunOnce(i, assertOutcomes: true);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (uint i = 31; i <= 59; i++)
        {
            RunOnce(i, assertOutcomes: false);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), $"Expected 0 allocated bytes; got {allocated}.");
        Assert.That(harness.Observer.Faults, Is.Zero);
    }

    [Test]
    public void ConfigToCapacityMapping_IsSingleValidatedSource()
    {
        NetworkRuntimeConfig config = CreateValidConfig();
        NetworkRuntimeCapacity capacity = NetworkRuntimeCapacity.FromConfig(config);
        FixedInputProtocolConfig protocol = capacity.CreateFixedInputProtocolConfig(sessionEpoch: 9, seatCapacity: config.PlayerCapacity);

        Assert.Multiple(() =>
        {
            Assert.That(capacity.InputChannel, Is.EqualTo(new ChannelId(3)));
            Assert.That(capacity.FixedInputSchemaId, Is.EqualTo((ushort)config.FixedInputSchemaId));
            Assert.That(capacity.FixedInputFramePayloadBytes, Is.EqualTo((ushort)config.FixedInputFramePayloadBytes));
            Assert.That(capacity.FixedInputPendingFrameCapacity, Is.EqualTo(config.FixedInputPendingFrameCapacity));
            Assert.That(capacity.FixedInputLeadTicks, Is.EqualTo(config.FixedInputLeadTicks));
            Assert.That(protocol.SeatCapacity, Is.EqualTo(config.PlayerCapacity));
            Assert.That(protocol.SessionEpoch, Is.EqualTo(9UL));
            Assert.That(protocol.MaxDatagramPayloadBytes, Is.EqualTo(config.MaxDatagramPayloadBytes));
        });
    }

    [Test]
    public void DefaultOrInconsistentFixedInputConfig_FailsFast()
    {
        NetworkRuntimeConfig missing = CreateValidConfig();
        missing.FixedInputSchemaId = 0;
        Assert.That(missing.Validate, Throws.InvalidOperationException);

        NetworkRuntimeConfig channels = CreateValidConfig();
        channels.InputChannelId = channels.StateChannelId;
        Assert.That(channels.Validate, Throws.InvalidOperationException.With.Message.Contains("distinct"));

        NetworkRuntimeConfig pending = CreateValidConfig();
        pending.FixedInputPendingFrameCapacity = pending.FixedInputMaxFutureTicks - 1;
        Assert.That(pending.Validate, Throws.InvalidOperationException);

        NetworkRuntimeConfig leadZero = CreateValidConfig();
        leadZero.FixedInputLeadTicks = 0;
        Assert.That(leadZero.Validate, Throws.InvalidOperationException.With.Message.Contains("FixedInputLeadTicks"));

        NetworkRuntimeConfig leadTooHigh = CreateValidConfig();
        leadTooHigh.FixedInputLeadTicks = leadTooHigh.FixedInputMaxFutureTicks + 1;
        Assert.That(leadTooHigh.Validate, Throws.InvalidOperationException.With.Message.Contains("FixedInputLeadTicks"));
    }

    private static void Handshake(FixedInputHarness harness)
    {
        Assert.That(harness.Client.TryConnectNow(), Is.True);
        harness.Client.PumpTransport();
        harness.Server.PumpTransport();
        harness.Client.PumpTransport();
        Assert.That(harness.Client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
        Assert.That(harness.Client.FixedInputPendingCount, Is.Zero);
    }

    private static void RunAuthoritativeFrame(FixedInputHarness harness, uint tick)
    {
        int expected = harness.TickState.CommittedTick + 1;
        if ((int)tick != expected)
        {
            harness.TickState.RestoreCommittedTick(checked((int)tick) - 1);
        }

        harness.TickState.Begin(checked((int)tick));
        harness.Server.BeforeAuthoritativeTick(tick);
        harness.TickState.Commit(checked((int)tick));
        harness.Server.AfterAuthoritativeCommit(tick);
    }

    private static FixedInputHarness CreateHarness(
        int statePublishRateHz = 30,
        int reconnectWindowTicks = 4,
        int connectionCapacity = 1,
        int seatCapacity = 1,
        int replicationFactorySeatCapacityAdjustment = 0,
        int replicationFactoryGlobalCapacityAdjustment = 0,
        int replicationFactoryPerSeatCapacityAdjustment = 0,
        bool replicationAcquireSucceeds = true,
        bool replicationReleaseSucceeds = true,
        bool dirtyReplicationRuntime = false,
        SessionSeatBinding? fixedInputPrebind = null)
    {
        World serverWorld = World.Create();
        World clientWorld = World.Create();
        Entity player = serverWorld.Create(new PlayerIdentity { PlayerId = 1 });
        Entity unit = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));

        var relationshipTypes = new RelationshipTypeRegistry();
        var relationships = new RelationshipRuntime(
            serverWorld,
            relationshipTypes,
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(capacity: 8),
            new RelationshipReverseIndex(serverWorld));
        int ownsType = relationshipTypes.Register("Owns");
        int controlsType = relationshipTypes.Register("Controls");
        var ownership = new OwnershipResolver(relationships, ownsType);
        ownership.EnsureOwnership(player, unit);
        var control = new ControlDomainQuery(serverWorld, relationships, ownership, ownsType, controlsType);
        var entities = new NetworkEntityTable(capacity: 2);
        Assert.That(entities.TryAllocate(unit, out NetworkEntityHandle handle), Is.True);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
        knowledge.Upsert(player, unit, VisibleDisclosure());
        var orderTypes = new OrderTypeRegistry();
        orderTypes.Register(new OrderTypeConfig { Key = "test.move", OrderTypeId = 1 });
        var schemas = new NetworkCommandSchemaRegistry();
        schemas.Register(new NetworkCommandSchema(
            1,
            NetworkCommandTargetKind.WorldPositionCm,
            allowArg0: false,
            allowArg1: false,
            OrderSubmitMode.Immediate,
            KnowledgePositionAccess.None));
        schemas.Freeze();
        var orders = new OrderQueue(capacity: 8);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 8);
        var ingressConfig = new NetworkCommandIngressConfig(
            seatCapacity,
            simulationTickRateHz: 30,
            maxBatchesPerSecond: 30,
            burstBatchCapacity: 4,
            maxActorsPerBatch: 2,
            sequenceHistoryCapacity: 4,
            maxPastTargetTicks: 2,
            maxFutureTargetTicks: 2,
            scheduledBatchCapacity: 4);
        var commandIngress = new NetworkCommandIngress(
            in ingressConfig,
            serverWorld,
            entities,
            control,
            new KnowledgeProjectionResolver(knowledge),
            orderTypes,
            schemas,
            orders,
            results);

        var projectors = new ReplicationSchemaProjectorRegistry(schemaCapacity: 1);
        Assert.That(projectors.Register(1, new TestProjector()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        projectors.Freeze();

        NetworkRuntimeCapacity capacity = CreateCapacity(connectionCapacity, statePublishRateHz);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 9, 9, 9 });
        var protocol = new ProtocolVersion(1, 0);
        var transport = new TrackingTransport(new ConnectionId(21));
        var observer = new RecordingObserver();
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity,
            new SessionEpoch(77),
            protocol,
            fingerprint,
            (uint)reconnectWindowTicks);
        var tickState = new AuthoritativeSimulationTickState();
        var fixedInput = new AuthoritativeFixedInputIngress(
            capacity.CreateFixedInputProtocolConfig(sessions.SessionEpoch.Value, sessions.SeatCapacity),
            tickState);
        if (fixedInputPrebind.HasValue)
        {
            SessionSeatBinding prebound = fixedInputPrebind.Value;
            fixedInput.BindSeat(in prebound);
        }

        var replicationFactory = new TrackingAuthoritativeReplicationSeatRuntimeFactory(
            checked(seatCapacity + replicationFactorySeatCapacityAdjustment),
            checked(capacity.GlobalEntityCapacity + replicationFactoryGlobalCapacityAdjustment),
            checked(capacity.ReplicationEntityCapacityPerSeat + replicationFactoryPerSeatCapacityAdjustment),
            (_, viewer) =>
            {
                if (!replicationAcquireSucceeds)
                {
                    return null;
                }

                AuthoritativeReplicationSeatRuntime runtime = CreateSeatRuntime(
                    serverWorld,
                    entities,
                    knowledge,
                    viewer,
                    projectors);
                if (dirtyReplicationRuntime)
                {
                    Assert.That(
                        runtime.Channel.BuildFull(
                            sessions.SessionEpoch.Value,
                            tick: 1,
                            snapshotId: 1,
                            ReadOnlySpan<ReplicatedEntityState>.Empty,
                            ReadOnlySpan<ReplicationDisclosureInput>.Empty,
                            runtime.Packet),
                        Is.EqualTo(ReplicationBuildResult.Success));
                }

                return runtime;
            },
            (_, _) => replicationReleaseSucceeds);
        AuthoritativeServerNetworkRuntime server;
        try
        {
            server = new AuthoritativeServerNetworkRuntime(
                in capacity,
                NetworkTransportPortOwnership.Borrowed,
                transport,
                transport,
                transport,
                sessions,
                commandIngress,
                results,
                new FixedControllerResolver(player),
                new FixedReplicationInterest(handle),
                replicationFactory,
                fixedInput,
                observer);
        }
        catch
        {
            clientWorld.Dispose();
            serverWorld.Dispose();
            throw;
        }
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Borrowed,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(clientWorld, entityCapacity: 2),
            new NetworkCommandAdmissionResultBuffer(capacity: 8),
            observer);

        return new FixedInputHarness(
            serverWorld,
            clientWorld,
            capacity,
            transport,
            observer,
            replicationFactory,
            tickState,
            server,
            client,
            protocol,
            fingerprint);
    }

    private static AuthoritativeReplicationSeatRuntime CreateSeatRuntime(
        World world,
        NetworkEntityTable entities,
        KnowledgeProjectionStore knowledge,
        Entity viewer,
        ReplicationSchemaProjectorRegistry projectors)
    {
        var bridge = new AuthoritativeWorldReplicationBridge(
            world,
            entities,
            knowledge,
            viewer,
            projectors,
            replicationEntityCapacityPerSeat: 1);
        return new AuthoritativeReplicationSeatRuntime(
            bridge,
            new AuthoritativeReplicationChannel(entities, 1, baselineCapacity: 2, new ReplicationDisclosureChangeLog(4)),
            new ReplicationProjectionBuffer(1),
            new ReplicationPacketBuffer(1));
    }

    private static NetworkRuntimeCapacity CreateCapacity(int connectionCapacity, int statePublishRateHz) => new(
        simulationTickRateHz: 30,
        statePublishRateHz,
        maxDatagramPayloadBytes: 256,
        connectionCapacity,
        globalEntityCapacity: 2,
        replicationEntityCapacityPerSeat: 1,
        maxCommandEntries: 2,
        maxCommandPayloadBytes: CommandBatchWireCodec.GetPayloadSize(2),
        maxCommandFragments: 4,
        maxSnapshotBytes: ReplicationPacketWireCodec.GetPayloadSize(1, 1, 2),
        maxSnapshotFragments: 4,
        outboundQueueCapacity: 32,
        acknowledgementHistoryCapacity: 4,
        controlChannel: new ChannelId(0),
        commandChannel: new ChannelId(1),
        stateChannel: new ChannelId(2),
        inputChannel: new ChannelId(3),
        fixedInputHistoryTicksPerSeat: 8,
        fixedInputSchemaId: SchemaId,
        fixedInputFramePayloadBytes: PayloadBytes,
        fixedInputMaxFutureTicks: 4,
        fixedInputLeadTicks: 2,
        fixedInputMaxFramesPerBatch: 4,
        fixedInputPendingFrameCapacity: 8);

    private static NetworkRuntimeConfig CreateValidConfig() => new()
    {
        ProfileId = "fixed_input_runtime_v1",
        ReferenceTransport = "LiteNetLib/2.1.4",
        ProtocolMajor = 1,
        ProtocolMinor = 0,
        PlayerCapacity = 2,
        SimulationTickRateHz = 30,
        StatePublishRateHz = 10,
        GlobalNetworkEntityCapacity = 100,
        ReplicationEntityCapacityPerSeat = 8,
        OrderQueueCapacity = 32,
        MaxCommandBatchesPerSecondPerPlayer = 8,
        CommandBurstBatchCapacity = 8,
        MaxActorsPerCommandBatch = 4,
        CommandSequenceHistoryCapacity = 32,
        MaxPastTargetTicks = 2,
        MaxFutureTargetTicks = 4,
        NetworkAdmissionResultCapacity = 32,
        EntityAdmissionResultCapacity = 32,
        ReconnectWindowSeconds = 30,
        ClientReconnectRetryMilliseconds = 500,
        ReplicationSchemaCapacity = 8,
        BaselineCapacity = 8,
        DisclosureChangeLogCapacity = 32,
        DatagramQueueCapacity = 64,
        ConnectionEventCapacity = 16,
        MaxDatagramPayloadBytes = 1200,
        TransportMaxConnectAttempts = 10,
        TransportDisconnectTimeoutMilliseconds = 5_000,
        ReliableDisconnectFlushTimeoutMilliseconds = 4_000,
        TransportChannelCount = 8,
        ControlChannelId = 0,
        CommandChannelId = 1,
        StateChannelId = 2,
        InputChannelId = 3,
        FixedInputHistoryTicksPerSeat = 8,
        FixedInputSchemaId = 1,
        FixedInputFramePayloadBytes = 12,
        FixedInputMaxFutureTicks = 4,
        FixedInputLeadTicks = 2,
        FixedInputMaxFramesPerBatch = 4,
        FixedInputPendingFrameCapacity = 8,
        SnapshotChunkCapacity = 64,
        MaxServerOutboundBytesPerSecondPerClient = 256 * 1024,
        TickP95BudgetMicroseconds = 26_700,
        TickP99BudgetMicroseconds = 31_000,
        CommandSchemas =
        {
            new NetworkCommandSchemaConfig
            {
                OrderTypeKey = "moveTo",
                TargetKind = NetworkCommandTargetKind.WorldPositionCm,
                SubmitMode = OrderSubmitMode.Queued,
            },
        },
        NormalConnection = new NetworkFaultProfileConfig(),
        UnstableConnection = new NetworkFaultProfileConfig(),
    };

    private static KnowledgeDisclosureRecord VisibleDisclosure() => new(
        KnowledgePresence.LiveVisible,
        KnowledgePositionAccess.Live,
        default,
        default,
        default,
        Entity.Null,
        observedTick: 1,
        expiryTick: 0,
        confidencePermille: 1000,
        revision: 1);

    private sealed class FixedInputHarness : IDisposable
    {
        public FixedInputHarness(
            World serverWorld,
            World clientWorld,
            NetworkRuntimeCapacity capacity,
            TrackingTransport transport,
            RecordingObserver observer,
            TrackingAuthoritativeReplicationSeatRuntimeFactory replicationFactory,
            AuthoritativeSimulationTickState tickState,
            AuthoritativeServerNetworkRuntime server,
            ReplicatedClientNetworkRuntime client,
            ProtocolVersion protocol,
            ContentFingerprint fingerprint)
        {
            ServerWorld = serverWorld;
            ClientWorld = clientWorld;
            Capacity = capacity;
            Transport = transport;
            Observer = observer;
            ReplicationFactory = replicationFactory;
            TickState = tickState;
            Server = server;
            Client = client;
            Protocol = protocol;
            Fingerprint = fingerprint;
        }

        public World ServerWorld { get; }
        public World ClientWorld { get; }
        public NetworkRuntimeCapacity Capacity { get; }
        public TrackingTransport Transport { get; }
        public RecordingObserver Observer { get; }
        public TrackingAuthoritativeReplicationSeatRuntimeFactory ReplicationFactory { get; }
        public AuthoritativeSimulationTickState TickState { get; }
        public AuthoritativeServerNetworkRuntime Server { get; }
        public ReplicatedClientNetworkRuntime Client { get; }
        public ProtocolVersion Protocol { get; }
        public ContentFingerprint Fingerprint { get; }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
            ClientWorld.Dispose();
            ServerWorld.Dispose();
        }
    }

    private readonly struct TestReplicatedData
    {
        public TestReplicatedData(uint revision, long value)
        {
            Revision = revision;
            Value = value;
        }

        public uint Revision { get; }
        public long Value { get; }
    }

    private readonly struct TestAppliedState
    {
        public TestAppliedState(long value) => Value = value;
        public long Value { get; }
    }

    private sealed class TestProjector : IReplicationSchemaProjector
    {
        public bool TryProject(World world, Entity entity, in KnowledgeDisclosureRecord disclosure, out ReplicationProjectedState state)
        {
            if (!world.TryGet(entity, out TestReplicatedData data))
            {
                state = default;
                return false;
            }

            state = new ReplicationProjectedState(
                data.Revision,
                new ReplicationStateVector(data.Value, 0, 0, 0),
                ReplicationControlOwnership.Unowned);
            return true;
        }
    }

    private sealed class TestApplier : IClientReplicationSchemaApplier
    {
        public bool CanCreate(World world, in ReplicatedEntityState state, in ReplicationApplyContext context) => true;
        public bool CanApply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context)
            => world.Has<TestAppliedState>(entity);
        public bool CanRelease(
            World world,
            Entity entity,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context)
            => world.Has<TestAppliedState>(entity);

        public Entity Create(
            World world,
            in ReplicationMirrorIdentity identity,
            in ReplicationMirrorState state,
            in ReplicationApplyContext context)
        {
            var applied = new TestAppliedState(state.Values.Value0);
            return world.Create(in identity, in state, in applied);
        }

        public void Apply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context) =>
            world.Set(entity, new TestAppliedState(state.Values.Value0));

        public void Release(
            World world,
            Entity entity,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context) =>
            world.Set(entity, new TestAppliedState(0));
    }

    private sealed class ClientBridgeFactory : IClientReplicationBridgeFactory
    {
        private readonly World _world;
        private readonly int _entityCapacity;

        public ClientBridgeFactory(World world, int entityCapacity)
        {
            _world = world;
            _entityCapacity = entityCapacity;
        }

        public int GlobalEntityCapacity => _entityCapacity;

        public ClientWorldReplicationBridge Create(in SessionSeatBinding clientSeat, ulong sessionEpoch)
        {
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            Assert.That(appliers.Register(1, new TestApplier()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            appliers.Freeze();
            return new ClientWorldReplicationBridge(
                _world,
                _entityCapacity,
                in clientSeat,
                sessionEpoch,
                appliers);
        }
    }

    private sealed class FixedControllerResolver : IAuthoritativeSeatControllerResolver
    {
        private readonly Entity _controller;
        public FixedControllerResolver(Entity controller) => _controller = controller;

        public bool TryResolveController(in SessionSeatBinding seat, out Entity controller)
        {
            controller = _controller;
            return true;
        }
    }

    private sealed class FixedReplicationInterest : IAuthoritativeReplicationInterestPort
    {
        private readonly NetworkEntityHandle[] _handles;
        public FixedReplicationInterest(params NetworkEntityHandle[] handles) => _handles = handles;

        public bool TryCopyInterest(
            in SessionSeatBinding seat,
            Span<NetworkEntityHandle> destination,
            out int count)
        {
            count = _handles.Length;
            if (destination.Length < count)
            {
                return false;
            }

            _handles.CopyTo(destination);
            return true;
        }
    }

    private sealed class MemoryCredentials : IClientSessionCredentialPort
    {
        private bool _hasValue;
        private ClientSessionCredentials _value;

        public ClientCredentialLoadStatus TryLoad(out ClientSessionCredentials credentials)
        {
            credentials = _value;
            return _hasValue ? ClientCredentialLoadStatus.Loaded : ClientCredentialLoadStatus.Empty;
        }

        public bool TryStore(in ClientSessionCredentials credentials)
        {
            _value = credentials;
            _hasValue = true;
            return true;
        }

        public bool TryClear()
        {
            _value = default;
            _hasValue = false;
            return true;
        }
    }

    private sealed class RecordingObserver : INetworkRuntimeObserver
    {
        public int Faults { get; private set; }
        public int SeatReconnections { get; private set; }
        public int SeatReleases { get; private set; }
        public NetworkRuntimeFault LastFault { get; private set; }

        public void ResetFaults() => Faults = 0;

        public void OnFault(in NetworkRuntimeFault fault)
        {
            Faults++;
            LastFault = fault;
        }

        public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected)
        {
            if (reconnected) SeatReconnections++;
        }

        public void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason) { }
        public void OnServerSeatReleased(in SessionSeatBinding seat) => SeatReleases++;
        public void OnClientHandshake(in SessionHandshakeResponse response) { }
        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome) { }
        public void OnClientResyncRequired(in NetworkResyncRequired message) { }
        public void OnClientReplicationCommitted(
            in SessionSeatBinding seat,
            in ReplicationPacketHeader header) { }
        public void OnClientReplicationTornDown(in SessionSeatBinding seat, ulong sessionEpoch) { }
    }

    private sealed class TrackingTransport :
        IServerConnectionEventPort,
        IClientConnectionEventPort,
        IServerDatagramPort,
        IClientDatagramPort,
        IServerConnectionControlPort,
        IClientConnectionControlPort,
        IDisposable
    {
        private const int QueueCapacity = 64;
        private const int MaxPayload = 256;

        private readonly ConnectionId _connection;
        private readonly Queue<ServerConnectionEvent> _serverEvents = new();
        private readonly Queue<ClientConnectionEvent> _clientEvents = new();
        private readonly Frame[] _serverInbound = new Frame[QueueCapacity];
        private readonly Frame[] _clientInbound = new Frame[QueueCapacity];
        private readonly uint[] _decodeBatchTicks = new uint[4];
        private readonly byte[] _decodeBatchPayloads = new byte[4 * PayloadBytes];
        private int _serverInboundHead;
        private int _serverInboundTail;
        private int _serverInboundCount;
        private int _clientInboundHead;
        private int _clientInboundTail;
        private int _clientInboundCount;

        public TrackingTransport(ConnectionId connection)
        {
            _connection = connection;
            for (int i = 0; i < QueueCapacity; i++)
            {
                _serverInbound[i] = new Frame(MaxPayload);
                _clientInbound[i] = new Frame(MaxPayload);
            }
        }

        public int ClientFixedInputBatchCount { get; private set; }
        public int ServerFixedInputAckCount { get; private set; }
        public int ServerSnapshotFragmentCount { get; private set; }
        public int ServerReplicationPacketCount { get; private set; }
        public ChannelId LastClientSendChannel { get; private set; }
        public ChannelId LastServerSendChannel { get; private set; }
        public ClientConnectionControlState State { get; private set; }
        public bool BlockFixedInputAckSends { get; set; }
        public bool CloseClientSends { get; set; }
        public NetworkFixedInputAcknowledgement LastServerFixedInputAck { get; private set; }
        public NetworkFixedInputBatchHeader LastClientBatchHeader { get; private set; }
        public uint LastClientBatchFirstTargetTick { get; private set; }
        public uint LastClientBatchHighestTargetTick { get; private set; }

        public void Connect()
        {
            State = ClientConnectionControlState.Connected;
            _serverEvents.Enqueue(new ServerConnectionEvent(_connection, TransportConnectionEventKind.Connected));
            _clientEvents.Enqueue(new ClientConnectionEvent(TransportConnectionEventKind.Connected));
        }

        public void ConnectClientOnly() =>
            _clientEvents.Enqueue(new ClientConnectionEvent(TransportConnectionEventKind.Connected));

        public bool TryConnect()
        {
            if (State != ClientConnectionControlState.Disconnected)
            {
                return false;
            }

            State = ClientConnectionControlState.Connecting;
            Connect();
            return true;
        }

        public void Disconnect()
        {
            State = ClientConnectionControlState.Disconnected;
            _serverEvents.Enqueue(new ServerConnectionEvent(
                _connection,
                TransportConnectionEventKind.Disconnected,
                TransportDisconnectReason.RemoteClosed));
            _clientEvents.Enqueue(new ClientConnectionEvent(
                TransportConnectionEventKind.Disconnected,
                TransportDisconnectReason.RemoteClosed));
        }

        void IClientConnectionControlPort.Disconnect() => Disconnect();
        void IServerConnectionControlPort.DisconnectAfterReliableFlush(ConnectionId connectionId) => Disconnect();
        public void Dispose() { }

        public void EnqueueServerFrame(ChannelId channel, NetworkWireKind kind, ReadOnlySpan<byte> payload)
        {
            Span<byte> framed = stackalloc byte[NetworkWireEnvelopeCodec.GetFramedLength(payload.Length)];
            Assert.That(NetworkWireEnvelopeCodec.TryEncode(kind, payload, framed, out int bytes), Is.EqualTo(NetworkWireCodecStatus.Success));
            Enqueue(ref _clientInboundHead, ref _clientInboundTail, ref _clientInboundCount, _clientInbound, channel, framed[..bytes]);
        }

        public void EnqueueClientFrame(ChannelId channel, NetworkWireKind kind, ReadOnlySpan<byte> payload)
        {
            Span<byte> framed = stackalloc byte[NetworkWireEnvelopeCodec.GetFramedLength(payload.Length)];
            Assert.That(NetworkWireEnvelopeCodec.TryEncode(kind, payload, framed, out int bytes), Is.EqualTo(NetworkWireCodecStatus.Success));
            Enqueue(ref _serverInboundHead, ref _serverInboundTail, ref _serverInboundCount, _serverInbound, channel, framed[..bytes]);
        }

        public void Pump() { }

        public bool TryReceiveConnectionEvent(out ServerConnectionEvent connectionEvent) =>
            _serverEvents.TryDequeue(out connectionEvent);

        public bool TryReceiveConnectionEvent(out ClientConnectionEvent connectionEvent) =>
            _clientEvents.TryDequeue(out connectionEvent);

        public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ConnectionId connectionId, out ChannelId channelId)
        {
            if (!TryDequeue(ref _serverInboundHead, ref _serverInboundCount, _serverInbound, buffer, out bytesReceived, out channelId))
            {
                connectionId = default;
                return false;
            }

            connectionId = _connection;
            return true;
        }

        public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ChannelId channelId) =>
            TryDequeue(ref _clientInboundHead, ref _clientInboundCount, _clientInbound, buffer, out bytesReceived, out channelId);

        public DatagramSendStatus TrySend(ConnectionId connectionId, ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            if (TryGetKind(payload, out NetworkWireKind kind) &&
                kind == NetworkWireKind.FixedInputAcknowledgement &&
                BlockFixedInputAckSends)
            {
                return DatagramSendStatus.NotReady;
            }

            Enqueue(ref _clientInboundHead, ref _clientInboundTail, ref _clientInboundCount, _clientInbound, channelId, payload);
            LastServerSendChannel = channelId;
            if (TryGetKind(payload, out kind))
            {
                if (kind == NetworkWireKind.FixedInputAcknowledgement)
                {
                    ServerFixedInputAckCount++;
                    if (NetworkWireEnvelopeCodec.TryDecode(payload, out _, out ReadOnlySpan<byte> ackPayload) ==
                            NetworkWireCodecStatus.Success &&
                        FixedInputWireCodec.TryDecodeAcknowledgement(
                            ackPayload,
                            out NetworkFixedInputAcknowledgement ack) == NetworkWireCodecStatus.Success)
                    {
                        LastServerFixedInputAck = ack;
                    }
                }

                if (kind == NetworkWireKind.SnapshotFragment) ServerSnapshotFragmentCount++;
                if (kind == NetworkWireKind.ReplicationPacket) ServerReplicationPacketCount++;
            }

            return DatagramSendStatus.Sent;
        }

        public DatagramSendStatus TrySend(ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            if (CloseClientSends)
            {
                return DatagramSendStatus.Closed;
            }

            Enqueue(ref _serverInboundHead, ref _serverInboundTail, ref _serverInboundCount, _serverInbound, channelId, payload);
            LastClientSendChannel = channelId;
            if (TryGetKind(payload, out NetworkWireKind kind) && kind == NetworkWireKind.FixedInputBatch)
            {
                ClientFixedInputBatchCount++;
                if (NetworkWireEnvelopeCodec.TryDecode(payload, out _, out ReadOnlySpan<byte> batchPayload) ==
                        NetworkWireCodecStatus.Success &&
                    FixedInputWireCodec.TryDecodeBatch(
                        batchPayload,
                        _decodeBatchTicks,
                        _decodeBatchPayloads,
                        out NetworkFixedInputBatchHeader header,
                        out int frameCount) == NetworkWireCodecStatus.Success)
                {
                    LastClientBatchHeader = header;
                    LastClientBatchFirstTargetTick = _decodeBatchTicks[0];
                    LastClientBatchHighestTargetTick = _decodeBatchTicks[frameCount - 1];
                }
            }

            return DatagramSendStatus.Sent;
        }

        private static void Enqueue(
            ref int head,
            ref int tail,
            ref int count,
            Frame[] queue,
            ChannelId channel,
            ReadOnlySpan<byte> payload)
        {
            if (count >= queue.Length)
            {
                throw new InvalidOperationException("Tracking transport queue capacity exceeded.");
            }

            if (payload.Length > MaxPayload)
            {
                throw new InvalidOperationException("Tracking transport payload exceeds fixed capacity.");
            }

            Frame frame = queue[tail];
            frame.Channel = channel;
            frame.Length = payload.Length;
            payload.CopyTo(frame.Payload);
            queue[tail] = frame;
            tail = (tail + 1) % queue.Length;
            count++;
            _ = head;
        }

        private static bool TryDequeue(
            ref int head,
            ref int count,
            Frame[] queue,
            Span<byte> buffer,
            out int bytesReceived,
            out ChannelId channelId)
        {
            if (count == 0)
            {
                bytesReceived = 0;
                channelId = default;
                return false;
            }

            Frame frame = queue[head];
            if (buffer.Length < frame.Length)
            {
                throw new InvalidOperationException("Receive buffer is too small for the queued datagram.");
            }

            frame.Payload.AsSpan(0, frame.Length).CopyTo(buffer);
            bytesReceived = frame.Length;
            channelId = frame.Channel;
            head = (head + 1) % queue.Length;
            count--;
            return true;
        }

        private static bool TryGetKind(ReadOnlySpan<byte> payload, out NetworkWireKind kind)
        {
            NetworkWireCodecStatus decoded = NetworkWireEnvelopeCodec.TryDecode(payload, out NetworkWireEnvelope envelope, out _);
            kind = envelope.Kind;
            return decoded == NetworkWireCodecStatus.Success;
        }

        private struct Frame
        {
            public Frame(int capacity)
            {
                Channel = default;
                Length = 0;
                Payload = new byte[capacity];
            }

            public ChannelId Channel;
            public int Length;
            public byte[] Payload;
        }
    }
}
