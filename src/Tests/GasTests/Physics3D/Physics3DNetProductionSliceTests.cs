using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Threading;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
public sealed class Physics3DNetProductionSliceTests
{
    private static Physics3DNetConfig CreateConfig(
        int playerCapacity = 4,
        int clientCapacity = -1,
        int aoiEntityCapacityPerClient = 8,
        int snapshotEntitiesPerDatagram = 2,
        int reliableMaxRetries = 3,
        int reliableRetransmitIntervalTicks = 1,
        int reliablePendingCapacity = 8,
        Physics3DNetMissingInputPolicy missingInputPolicy = Physics3DNetMissingInputPolicy.HoldTick)
    {
        int clients = clientCapacity < 0 ? playerCapacity : clientCapacity;
        var config = new Physics3DNetConfig
        {
            AuthoritativeHz = 30,
            SnapshotHz = 10,
            PlayerCapacity = playerCapacity,
            ClientCapacity = clients,
            TransportEndpointCapacity = Math.Max(clients, 2),
            SnapshotEntityCapacity = 64,
            AoiEntityCapacityPerClient = aoiEntityCapacityPerClient,
            SnapshotEntitiesPerDatagram = snapshotEntitiesPerDatagram,
            LocalPredictionHistoryTicks = 16,
            RemoteInterpolationHistoryTicks = 8,
            ReplayEventCapacity = 128,
            InputHistoryTicksPerPlayer = 16,
            MaxFutureInputTicks = 8,
            ReliableMaxRetries = reliableMaxRetries,
            ReliableRetransmitIntervalTicks = reliableRetransmitIntervalTicks,
            ReliablePendingCapacity = reliablePendingCapacity,
            ReliableAckBitfieldBits = 32,
            MaxDatagramPayloadBytes = 1200,
            DatagramReceiveBufferBytes = 2048,
            DatagramSendBufferBytes = 2048,
            HandshakeFingerprintStringCapacityBytes = 64,
            MissingInputPolicy = missingInputPolicy
        };
        config.Validate();
        return config;
    }

    private static Physics3DNetCompatibilityFingerprint CreateFingerprint(Physics3DNetConfig config) =>
        new(
            buildId: "build-prod",
            configHash: Physics3DNetCompatibilityFingerprint.HashConfig(config),
            kernelId: "bepu-test",
            simdProfile: "avx2",
            workerCount: 2,
            scenarioId: "prod-slice");

    [Test]
    public void Codec_RoundTrips_AllPacketTypes_AndRejectsMalformedVersionCapacity()
    {
        Physics3DNetConfig config = CreateConfig();
        var codec = new Physics3DNetPacketCodec(config);
        var buffer = new byte[config.MaxDatagramPayloadBytes];
        var entityDest = new Physics3DNetSnapshotEntityWrite[8];
        var controlPayloadDest = new byte[32];

        var handshake = new Physics3DNetHandshakePacket(
            sessionId: 7,
            networkPlayerId: 3,
            generation: 1,
            requestedClientSlot: 0,
            CreateFingerprint(config));
        int written = codec.EncodeHandshake(handshake, buffer);
        Physics3DNetHandshakePacket decodedHandshake = codec.DecodeHandshake(buffer.AsSpan(0, written));
        Assert.That(decodedHandshake.SessionId, Is.EqualTo(7u));
        Assert.That(decodedHandshake.Fingerprint, Is.EqualTo(handshake.Fingerprint));

        var inputPacket = new Physics3DNetClientInputPacket(
            7,
            new Physics3DNetInputSubmit(1, 3, 1, 9, new Physics3DNetQuantizedAxes2(1, -2), new Physics3DNetQuantizedAxes2(3, -4), 42));
        written = codec.EncodeClientInput(inputPacket, buffer);
        Physics3DNetClientInputPacket decodedInput = codec.DecodeClientInput(buffer.AsSpan(0, written));
        Assert.That(decodedInput.Input.Buttons, Is.EqualTo(42u));
        Assert.That(decodedInput.Input.MoveAxes.Y, Is.EqualTo((short)-2));

        Physics3DNetSnapshotEntityWrite[] entities =
        [
            MakeSnapshotEntity(1, 1, Physics3DNetReplicationOp.Spawn, 1, 10f),
            MakeSnapshotEntity(2, 1, Physics3DNetReplicationOp.Update, 1, 20f)
        ];
        var snapshotHeader = new Physics3DNetSnapshotPacketHeader(7, 3, 1, 5, 0, 1, 2);
        written = codec.EncodeSnapshotPayload(snapshotHeader, entities, buffer);
        Physics3DNetSnapshotPacketHeader decodedSnapshot = codec.DecodeSnapshotPayload(
            buffer.AsSpan(0, written),
            entityDest,
            out int entityCount);
        Assert.That(entityCount, Is.EqualTo(2));
        Assert.That(decodedSnapshot.SnapshotTick, Is.EqualTo(3));
        Assert.That(entityDest[1].PositionCm.X, Is.EqualTo(20f));

        var ack = new Physics3DNetAcknowledgementPacket(7, 11, 0b101);
        written = codec.EncodeAcknowledgement(ack, buffer);
        Assert.That(codec.DecodeAcknowledgement(buffer.AsSpan(0, written)).AckBits, Is.EqualTo(0b101u));

        Span<byte> controlPayload = stackalloc byte[] { 1, 2, 3 };
        written = codec.EncodeReliableControl(7, 4, Physics3DNetReliableControlOpcode.BaselineAck, controlPayload, buffer);
        codec.DecodeReliableControlFields(
            buffer.AsSpan(0, written),
            controlPayloadDest,
            out uint sessionId,
            out uint sequence,
            out Physics3DNetReliableControlOpcode opcode,
            out int payloadLength);
        Assert.That(sessionId, Is.EqualTo(7u));
        Assert.That(sequence, Is.EqualTo(4u));
        Assert.That(opcode, Is.EqualTo(Physics3DNetReliableControlOpcode.BaselineAck));
        Assert.That(payloadLength, Is.EqualTo(3));
        Assert.That(controlPayloadDest.AsSpan(0, 3).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));

        buffer[2] = 99;
        Assert.Throws<Physics3DNetPacketVersionException>(() => codec.PeekHeader(buffer.AsSpan(0, written)));

        buffer[2] = Physics3DNetPacketCodec.ProtocolVersion;
        buffer[0] = 0;
        Assert.Throws<Physics3DNetPacketMalformedException>(() => codec.PeekHeader(buffer.AsSpan(0, written)));

        written = codec.EncodeSnapshotPayload(snapshotHeader, entities, buffer);
        Assert.Throws<Physics3DNetCapacityExceededException>(
            () => codec.EncodeSnapshotPayload(snapshotHeader, entities, stackalloc byte[16]));

        Assert.Throws<Physics3DNetCapacityExceededException>(
            () => codec.DecodeSnapshotPayload(buffer.AsSpan(0, written), entityDest.AsSpan(0, 1), out _));
    }

    [Test]
    public void Codec_SnapshotCapacityFailure_IsAtomic_ForDestinationTooSmall()
    {
        Physics3DNetConfig config = CreateConfig();
        var codec = new Physics3DNetPacketCodec(config);
        var full = new byte[config.MaxDatagramPayloadBytes];
        Physics3DNetSnapshotEntityWrite[] entities =
        [
            MakeSnapshotEntity(1, 1, Physics3DNetReplicationOp.Spawn, 1),
            MakeSnapshotEntity(2, 1, Physics3DNetReplicationOp.Spawn, 1)
        ];
        var header = new Physics3DNetSnapshotPacketHeader(1, 3, 1, 1, 0, 1, 2);
        int written = codec.EncodeSnapshotPayload(header, entities, full);
        var tiny = new Physics3DNetSnapshotEntityWrite[1];
        Assert.Throws<Physics3DNetCapacityExceededException>(
            () => codec.DecodeSnapshotPayload(full.AsSpan(0, written), tiny, out _));
        Assert.That(tiny[0].NetworkEntityId, Is.EqualTo(0));
    }

    [Test]
    public void UdpTransport_Loopback_SendReceive_AndExplicitDispose()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 2);
        using var server = new Physics3DNetUdpDatagramTransport(config);
        using var client = new Physics3DNetUdpDatagramTransport(config);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        int serverSlotOnClient = client.RegisterRemoteEndpoint(server.LocalEndPoint!);
        byte[] payload = [9, 8, 7, 6];
        client.Send(serverSlotOnClient, payload);

        var receive = new byte[config.MaxDatagramPayloadBytes];
        bool received = SpinWait.SpinUntil(
            () => server.TryReceive(receive, out int bytes, out int endpointSlot) && bytes == 4 && receive[0] == 9,
            TimeSpan.FromSeconds(2));
        Assert.That(received, Is.True);

        server.Dispose();
        Assert.That(server.IsDisposed, Is.True);
        Assert.Throws<ObjectDisposedException>(() => server.Send(0, payload));
    }

    [Test]
    public void ReliableChannel_AckRetransmitDuplicateAndWraparound()
    {
        Physics3DNetConfig config = CreateConfig(reliableMaxRetries: 2, reliableRetransmitIntervalTicks: 1);
        var codec = new Physics3DNetPacketCodec(config);
        var sender = new Physics3DNetReliableControlChannel(config, codec);
        var receiver = new Physics3DNetReliableControlChannel(config, codec);
        var datagram = new byte[config.MaxDatagramPayloadBytes];
        var payloadDest = new byte[16];
        var capture = new CapturingSender();

        int encoded = sender.EncodeAndQueue(
            sessionId: 1,
            Physics3DNetReliableControlOpcode.SessionAccept,
            ReadOnlySpan<byte>.Empty,
            currentAuthoritativeTick: 1,
            datagram,
            out uint sequence);
        Assert.That(sequence, Is.EqualTo(1u));

        Physics3DNetReliableReceiveResult first = receiver.ReceiveControlDatagram(
            datagram.AsSpan(0, encoded),
            payloadDest,
            out _);
        Assert.That(first.Delivery, Is.EqualTo(Physics3DNetReliableDeliveryResult.Accepted));

        Physics3DNetReliableReceiveResult duplicate = receiver.ReceiveControlDatagram(
            datagram.AsSpan(0, encoded),
            payloadDest,
            out _);
        Assert.That(duplicate.Delivery, Is.EqualTo(Physics3DNetReliableDeliveryResult.DuplicateSuppressed));

        Physics3DNetAcknowledgementPacket ack = receiver.BuildAcknowledgement(1);
        Assert.That(sender.ApplyAcknowledgement(ack), Is.EqualTo(1));
        Assert.That(sender.PendingCount, Is.EqualTo(0));

        // Retransmit + exhaustion
        sender.EncodeAndQueue(1, Physics3DNetReliableControlOpcode.Disconnect, ReadOnlySpan<byte>.Empty, 1, datagram, out _);
        Assert.That(sender.ProcessRetransmits(1, 2, datagram, capture).RetransmitCount, Is.EqualTo(1));
        Assert.That(sender.ProcessRetransmits(1, 3, datagram, capture).RetransmitCount, Is.EqualTo(1));
        Physics3DNetReliableChannelTickResult exhausted = sender.ProcessRetransmits(1, 4, datagram, capture);
        Assert.That(exhausted.RetryExhausted, Is.True);
        Assert.That(sender.IsTerminalExhausted, Is.True);

        // Wraparound acknowledgement arithmetic (sequence 0 is reserved and never used as ackSequence).
        Assert.That(Physics3DNetReliableControlChannel.IsSequenceNewer(1u, uint.MaxValue), Is.True);
        Assert.That(Physics3DNetReliableControlChannel.IsAcknowledged(uint.MaxValue, 1u, 0b10), Is.True);
        Assert.That(Physics3DNetReliableControlChannel.IsAcknowledged(uint.MaxValue - 1, 1u, 0b100), Is.True);
        Assert.That(Physics3DNetReliableControlChannel.IsAcknowledged(uint.MaxValue - 2, 1u, 0b10), Is.False);
    }

    [Test]
    public void ServerPump_30HzTick_PublishesCommittedSnapshot_AndSendsAoiPackets()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 2, snapshotEntitiesPerDatagram: 2);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        var store = new Physics3DNetAuthoritativeSnapshotStore(config);
        var aoi = new Physics3DNetAoiDeltaBuilder(config);
        var codec = new Physics3DNetPacketCodec(config);
        var transport = new LoopbackDatagramTransport(config);
        var simulation = new CountingSimulationPort();
        var interest = new FixedInterestProvider(
        [
            MakeInterest(10, 1, 1f),
            MakeInterest(20, 1, 2f)
        ]);
        var gate = new Physics3DNetCompatibilityGate(CreateFingerprint(config));
        var pump = new Physics3DNetAuthoritativeServerPump(
            config, life, ring, store, aoi, codec, transport, simulation, interest, gate);

        int endpointA = transport.RegisterRemoteEndpoint(new IPEndPoint(IPAddress.Loopback, 40001));
        int endpointB = transport.RegisterRemoteEndpoint(new IPEndPoint(IPAddress.Loopback, 40002));
        pump.RegisterClient(new Physics3DNetServerClientBinding(0, endpointA, 100, 0, 1, baselineId: 1));
        pump.RegisterClient(new Physics3DNetServerClientBinding(1, endpointB, 101, 1, 1, baselineId: 1));

        var entityDest = new Physics3DNetSnapshotEntityWrite[8];
        int snapshotPackets = 0;

        for (long tick = 1; tick <= 3; tick++)
        {
            InjectInput(transport, codec, endpointA, 100, 0, 1, tick);
            InjectInput(transport, codec, endpointB, 101, 1, 1, tick);
            Physics3DNetServerTickResult result = pump.TickOnce();
            Assert.That(life.ExecutingTick, Is.EqualTo(0));
            Assert.That(life.CommittedTick, Is.EqualTo(tick));
            Assert.That(simulation.Ticks.Count, Is.EqualTo(tick));

            while (transport.TryDequeueSent(out int toEndpoint, out byte[] datagram))
            {
                Physics3DNetPacketHeader header = codec.PeekHeader(datagram);
                if (header.PacketType != Physics3DNetPacketType.SnapshotPayload)
                {
                    continue;
                }

                snapshotPackets++;
                Physics3DNetSnapshotPacketHeader snapshot = codec.DecodeSnapshotPayload(datagram, entityDest, out int count);
                Assert.That(snapshot.SnapshotTick, Is.EqualTo(3));
                Assert.That(toEndpoint, Is.AnyOf(endpointA, endpointB));
                Assert.That(count, Is.GreaterThanOrEqualTo(0));
            }

            if (tick < 3)
            {
                Assert.That(result.Kind, Is.EqualTo(Physics3DNetServerTickResultKind.Advanced));
                Assert.That(life.SnapshotTick, Is.EqualTo(0));
            }
            else
            {
                Assert.That(result.Kind, Is.EqualTo(Physics3DNetServerTickResultKind.SnapshotPublished));
                Assert.That(life.SnapshotTick, Is.EqualTo(3));
                Assert.That(snapshotPackets, Is.GreaterThan(0));
            }
        }
    }

    [Test]
    public void ServerPump_MissingInputPolicy_HoldAndFailAreExplicit_NoInputReuse()
    {
        Physics3DNetConfig holdConfig = CreateConfig(playerCapacity: 2, missingInputPolicy: Physics3DNetMissingInputPolicy.HoldTick);
        var holdHarness = CreatePumpHarness(holdConfig);
        holdHarness.Pump.RegisterClient(new Physics3DNetServerClientBinding(0, holdHarness.EndpointA, 1, 0, 1, 1));
        holdHarness.Pump.RegisterClient(new Physics3DNetServerClientBinding(1, holdHarness.EndpointB, 2, 1, 1, 1));
        InjectInput(holdHarness.Transport, holdHarness.Codec, holdHarness.EndpointA, 1, 0, 1, tick: 1);
        Physics3DNetServerTickResult held = holdHarness.Pump.TickOnce();
        Assert.That(held.Kind, Is.EqualTo(Physics3DNetServerTickResultKind.HeldForMissingInputs));
        Assert.That(held.MissingInputCount, Is.EqualTo(1));
        Assert.That(holdHarness.Lifecycle.CommittedTick, Is.EqualTo(0));
        Assert.That(holdHarness.Lifecycle.ExecutingTick, Is.EqualTo(0));

        Physics3DNetConfig failConfig = CreateConfig(playerCapacity: 2, missingInputPolicy: Physics3DNetMissingInputPolicy.FailExplicit);
        var failHarness = CreatePumpHarness(failConfig);
        failHarness.Pump.RegisterClient(new Physics3DNetServerClientBinding(0, failHarness.EndpointA, 1, 0, 1, 1));
        failHarness.Pump.RegisterClient(new Physics3DNetServerClientBinding(1, failHarness.EndpointB, 2, 1, 1, 1));
        InjectInput(failHarness.Transport, failHarness.Codec, failHarness.EndpointA, 1, 0, 1, tick: 1);
        Assert.Throws<Physics3DNetMissingInputException>(() => failHarness.Pump.TickOnce());
        Assert.That(failHarness.Lifecycle.CommittedTick, Is.EqualTo(0));
    }

    [Test]
    public void ServerPump_Supports150ClientCapacityRegistration()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 150, clientCapacity: 150, aoiEntityCapacityPerClient: 4);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        var store = new Physics3DNetAuthoritativeSnapshotStore(config);
        var aoi = new Physics3DNetAoiDeltaBuilder(config);
        var codec = new Physics3DNetPacketCodec(config);
        var transport = new LoopbackDatagramTransport(config);
        var simulation = new CountingSimulationPort();
        var interest = new FixedInterestProvider([MakeInterest(1, 1)]);
        var gate = new Physics3DNetCompatibilityGate(CreateFingerprint(config));
        var pump = new Physics3DNetAuthoritativeServerPump(
            config, life, ring, store, aoi, codec, transport, simulation, interest, gate);

        for (int i = 0; i < 150; i++)
        {
            int endpoint = transport.RegisterRemoteEndpoint(new IPEndPoint(IPAddress.Loopback, 50000 + i));
            pump.RegisterClient(new Physics3DNetServerClientBinding(i, endpoint, (uint)(1000 + i), i, 1, 1));
        }

        Assert.That(pump.ConnectedClientCount, Is.EqualTo(150));
        Assert.Throws<Physics3DNetCapacityExceededException>(
            () => transport.RegisterRemoteEndpoint(new IPEndPoint(IPAddress.Loopback, 60000)));
    }

    [Test]
    public void LocalCorrection_CharacterAndVehicle_ReplayRetainedInputs_AndRejectKindMismatch()
    {
        Physics3DNetConfig config = CreateConfig();
        var coordinator = new Physics3DNetLocalCorrectionCoordinator();
        var poses = new Physics3DNetPredictedPose[8];
        var inputs = new Physics3DNetInputFrameView[8];

        var characterHistory = new Physics3DNetLocalPredictionHistory(config);
        characterHistory.BindLocalDriven(42, 1, Physics3DNetLocalDrivenKind.Character);
        for (long tick = 1; tick <= 5; tick++)
        {
            characterHistory.Record(
                new Physics3DNetPredictedPose(tick, new Vector3(tick * 10f, 0f, 0f), Quaternion.Identity, Vector3.UnitX, Vector3.Zero),
                MakeInputFrame(tick, 1, (uint)tick, buttons: 1));
        }

        var characterSim = new ScriptedLocalSim(Physics3DNetLocalDrivenKind.Character, stepX: 1f);
        var characterAdapter = new Physics3DNetCharacterCorrectionAdapter(characterSim);
        var authoritative = new Physics3DNetPredictedPose(3, new Vector3(100f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        Physics3DNetLocalCorrectionResult characterResult = coordinator.Correct(
            characterHistory,
            characterAdapter,
            authoritative,
            42,
            1,
            3,
            poses,
            inputs);
        Assert.That(characterResult.ReplayedFrameCount, Is.EqualTo(2));
        Assert.That(characterResult.FinalPose.PositionCm.X, Is.EqualTo(102f).Within(0.001f));

        var vehicleHistory = new Physics3DNetLocalPredictionHistory(config);
        vehicleHistory.BindLocalDriven(7, 1, Physics3DNetLocalDrivenKind.Vehicle);
        for (long tick = 1; tick <= 4; tick++)
        {
            vehicleHistory.Record(
                new Physics3DNetPredictedPose(tick, new Vector3(tick, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero),
                MakeInputFrame(tick, 1, (uint)tick, buttons: 2));
        }

        var vehicleSim = new ScriptedLocalSim(Physics3DNetLocalDrivenKind.Vehicle, stepX: 5f);
        var vehicleAdapter = new Physics3DNetVehicleCorrectionAdapter(vehicleSim);
        var vehicleAuth = new Physics3DNetPredictedPose(2, new Vector3(50f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        Physics3DNetLocalCorrectionResult vehicleResult = coordinator.Correct(
            vehicleHistory,
            vehicleAdapter,
            vehicleAuth,
            7,
            1,
            2,
            poses,
            inputs);
        Assert.That(vehicleResult.ReplayedFrameCount, Is.EqualTo(2));
        Assert.That(vehicleResult.FinalPose.PositionCm.X, Is.EqualTo(60f).Within(0.001f));

        Assert.Throws<InvalidOperationException>(
            () => coordinator.Correct(
                characterHistory,
                vehicleAdapter,
                authoritative,
                42,
                1,
                3,
                poses,
                inputs));

        Assert.Throws<InvalidOperationException>(
            () => coordinator.Correct(
                characterHistory,
                characterAdapter,
                authoritative,
                99,
                1,
                3,
                poses,
                inputs));

        var shortHistory = new Physics3DNetLocalPredictionHistory(config);
        shortHistory.BindLocalDriven(1, 1, Physics3DNetLocalDrivenKind.Character);
        shortHistory.Record(
            new Physics3DNetPredictedPose(1, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero),
            MakeInputFrame(1, 1, 1));
        shortHistory.Record(
            new Physics3DNetPredictedPose(2, new Vector3(1f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero),
            MakeInputFrame(2, 1, 2));
        shortHistory.Record(
            new Physics3DNetPredictedPose(4, new Vector3(3f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero),
            MakeInputFrame(4, 1, 4));
        Assert.Throws<InvalidOperationException>(
            () => coordinator.Correct(
                shortHistory,
                characterAdapter,
                new Physics3DNetPredictedPose(1, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero),
                1,
                1,
                1,
                poses,
                inputs));
    }

    [Test]
    public void WarmedFixedStep_Codec_AndReliableChannel_HaveZeroManagedAllocations()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 8, clientCapacity: 8);
        var codec = new Physics3DNetPacketCodec(config);
        var reliable = new Physics3DNetReliableControlChannel(config, codec);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        var buffer = new byte[config.MaxDatagramPayloadBytes];
        var entityDest = new Physics3DNetSnapshotEntityWrite[8];
        var capture = new CapturingSender();
        var missing = new int[8];
        Physics3DNetSnapshotEntityWrite[] entities =
        [
            MakeSnapshotEntity(1, 1, Physics3DNetReplicationOp.Update, 1, 1f)
        ];

        for (int i = 0; i < 8; i++)
        {
            ring.RegisterPlayer(i, 1, i);
        }

        void Warm()
        {
            long tick = life.CommittedTick + 1;
            var inputPacket = new Physics3DNetClientInputPacket(
                10,
                new Physics3DNetInputSubmit(tick, 0, 1, (uint)tick, new Physics3DNetQuantizedAxes2(1, 2), new Physics3DNetQuantizedAxes2(3, 4), 5));
            int encoded = codec.EncodeClientInput(inputPacket, buffer);
            _ = codec.DecodeClientInput(buffer.AsSpan(0, encoded));

            var snapshotHeader = new Physics3DNetSnapshotPacketHeader(10, 3, 1, 1, 0, 1, 1);
            encoded = codec.EncodeSnapshotPayload(snapshotHeader, entities, buffer);
            _ = codec.DecodeSnapshotPayload(buffer.AsSpan(0, encoded), entityDest, out _);

            encoded = codec.EncodeAcknowledgement(new Physics3DNetAcknowledgementPacket(10, 1, 0), buffer);
            _ = codec.DecodeAcknowledgement(buffer.AsSpan(0, encoded));

            Span<byte> controlPayload = stackalloc byte[4];
            encoded = reliable.EncodeAndQueue(
                10,
                Physics3DNetReliableControlOpcode.BaselineAck,
                controlPayload,
                tick,
                buffer,
                out uint seq);
            _ = reliable.ApplyAcknowledgement(new Physics3DNetAcknowledgementPacket(10, seq, 0));
            encoded = reliable.EncodeAndQueue(
                10,
                Physics3DNetReliableControlOpcode.SessionAccept,
                ReadOnlySpan<byte>.Empty,
                tick,
                buffer,
                out _);
            _ = reliable.ProcessRetransmits(10, tick + 1, buffer, capture);
            _ = reliable.ApplyAcknowledgement(new Physics3DNetAcknowledgementPacket(10, reliable.NextSendSequence - 1, uint.MaxValue));

            for (int p = 0; p < 8; p++)
            {
                _ = ring.Submit(new Physics3DNetInputSubmit(
                    tick,
                    p,
                    1,
                    (uint)tick,
                    new Physics3DNetQuantizedAxes2(1, 2),
                    new Physics3DNetQuantizedAxes2(3, 4),
                    7));
            }

            Assert.That(ring.TryBeginAuthoritativeExecute(tick, missing, out _), Is.True);
            life.Commit();
            ring.AcknowledgeInputFramesAfterCommit(tick);
        }

        for (int i = 0; i < 16; i++)
        {
            Warm();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Span<byte> controlPayload = stackalloc byte[4];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            long tick = life.CommittedTick + 1;
            var inputPacket = new Physics3DNetClientInputPacket(
                10,
                new Physics3DNetInputSubmit(
                    tick,
                    0,
                    1,
                    (uint)tick,
                    new Physics3DNetQuantizedAxes2(1, 2),
                    new Physics3DNetQuantizedAxes2(3, 4),
                    5));
            int encoded = codec.EncodeClientInput(inputPacket, buffer);
            _ = codec.DecodeClientInput(buffer.AsSpan(0, encoded));

            var snapshotHeader = new Physics3DNetSnapshotPacketHeader(10, 3, 1, (uint)(i + 1), 0, 1, 1);
            encoded = codec.EncodeSnapshotPayload(snapshotHeader, entities, buffer);
            _ = codec.DecodeSnapshotPayload(buffer.AsSpan(0, encoded), entityDest, out _);

            encoded = codec.EncodeAcknowledgement(new Physics3DNetAcknowledgementPacket(10, (uint)(i + 1), (uint)i), buffer);
            _ = codec.DecodeAcknowledgement(buffer.AsSpan(0, encoded));

            encoded = reliable.EncodeAndQueue(
                10,
                Physics3DNetReliableControlOpcode.BaselineAck,
                controlPayload,
                tick,
                buffer,
                out uint seq);
            _ = reliable.ApplyAcknowledgement(new Physics3DNetAcknowledgementPacket(10, seq, 0));

            for (int p = 0; p < 8; p++)
            {
                _ = ring.Submit(new Physics3DNetInputSubmit(
                    tick,
                    p,
                    1,
                    (uint)tick,
                    new Physics3DNetQuantizedAxes2(1, 2),
                    new Physics3DNetQuantizedAxes2(3, 4),
                    7));
            }

            if (!ring.TryBeginAuthoritativeExecute(tick, missing, out _))
            {
                throw new InvalidOperationException($"Expected complete inputs for tick {tick}.");
            }

            life.Commit();
            ring.AcknowledgeInputFramesAfterCommit(tick);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed codec/reliable/fixed-step paths allocated {allocated} managed bytes.");
    }

    private static void InjectInput(
        LoopbackDatagramTransport transport,
        Physics3DNetPacketCodec codec,
        int endpointSlot,
        uint sessionId,
        int networkPlayerId,
        int generation,
        long tick)
    {
        var buffer = new byte[codec.MaxDatagramPayloadBytes];
        var packet = new Physics3DNetClientInputPacket(
            sessionId,
            new Physics3DNetInputSubmit(
                tick,
                networkPlayerId,
                generation,
                sequence: (uint)tick,
                new Physics3DNetQuantizedAxes2(1, 2),
                new Physics3DNetQuantizedAxes2(3, 4),
                buttons: 7));
        int written = codec.EncodeClientInput(packet, buffer);
        transport.EnqueueReceive(endpointSlot, buffer.AsSpan(0, written));
    }

    private static PumpHarness CreatePumpHarness(Physics3DNetConfig config)
    {
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        var store = new Physics3DNetAuthoritativeSnapshotStore(config);
        var aoi = new Physics3DNetAoiDeltaBuilder(config);
        var codec = new Physics3DNetPacketCodec(config);
        var transport = new LoopbackDatagramTransport(config);
        var simulation = new CountingSimulationPort();
        var interest = new FixedInterestProvider([MakeInterest(1, 1)]);
        var gate = new Physics3DNetCompatibilityGate(CreateFingerprint(config));
        var pump = new Physics3DNetAuthoritativeServerPump(
            config, life, ring, store, aoi, codec, transport, simulation, interest, gate);
        int endpointA = transport.RegisterRemoteEndpoint(new IPEndPoint(IPAddress.Loopback, 41001));
        int endpointB = transport.RegisterRemoteEndpoint(new IPEndPoint(IPAddress.Loopback, 41002));
        return new PumpHarness(pump, life, transport, codec, endpointA, endpointB);
    }

    private static Physics3DNetSnapshotEntityWrite MakeSnapshotEntity(
        int id,
        int generation,
        Physics3DNetReplicationOp op,
        long baselineId,
        float x = 0f) =>
        new(
            id,
            generation,
            op,
            baselineId,
            new Vector3(x, 0f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DBodyKind.Dynamic,
            Physics3DNetReplicationMode.RigidBody);

    private static Physics3DNetAoiInterest MakeInterest(int id, int generation, float positionX = 0f) =>
        new(
            id,
            generation,
            new Vector3(positionX, 0f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DBodyKind.Dynamic,
            Physics3DNetReplicationMode.Character);

    private static Physics3DNetInputFrameView MakeInputFrame(long tick, int player, uint sequence, uint buttons = 0) =>
        new(tick, player, 1, sequence, new Physics3DNetQuantizedAxes2(1, 2), new Physics3DNetQuantizedAxes2(3, 4), buttons, 0);

    private sealed class PumpHarness
    {
        public PumpHarness(
            Physics3DNetAuthoritativeServerPump pump,
            Physics3DNetTickLifecycle lifecycle,
            LoopbackDatagramTransport transport,
            Physics3DNetPacketCodec codec,
            int endpointA,
            int endpointB)
        {
            Pump = pump;
            Lifecycle = lifecycle;
            Transport = transport;
            Codec = codec;
            EndpointA = endpointA;
            EndpointB = endpointB;
        }

        public Physics3DNetAuthoritativeServerPump Pump { get; }
        public Physics3DNetTickLifecycle Lifecycle { get; }
        public LoopbackDatagramTransport Transport { get; }
        public Physics3DNetPacketCodec Codec { get; }
        public int EndpointA { get; }
        public int EndpointB { get; }
    }

    private sealed class CountingSimulationPort : IPhysics3DNetSimulationTickPort
    {
        public List<long> Ticks { get; } = new();

        public void SimulateAuthoritativeTick(long executingTick) => Ticks.Add(executingTick);
    }

    private sealed class FixedInterestProvider : IPhysics3DNetClientInterestProvider
    {
        private readonly Physics3DNetAoiInterest[] _interest;

        public FixedInterestProvider(Physics3DNetAoiInterest[] interest)
        {
            _interest = interest;
        }

        public int FillInterest(int clientSlot, long snapshotTick, Span<Physics3DNetAoiInterest> destination)
        {
            if (_interest.Length > destination.Length)
            {
                throw new Physics3DNetCapacityExceededException("interest destination", destination.Length, snapshotTick);
            }

            _interest.AsSpan().CopyTo(destination);
            return _interest.Length;
        }
    }

    private sealed class ScriptedLocalSim : IPhysics3DNetLocalDrivenSimulationPort
    {
        private readonly float _stepX;
        private Physics3DNetPredictedPose _pose;

        public ScriptedLocalSim(Physics3DNetLocalDrivenKind kind, float stepX)
        {
            Kind = kind;
            _stepX = stepX;
        }

        public Physics3DNetLocalDrivenKind Kind { get; }

        public void RestoreLocalDrivenState(in Physics3DNetPredictedPose authoritativePoseAtConfirmedTick) =>
            _pose = authoritativePoseAtConfirmedTick;

        public Physics3DNetPredictedPose SimulateLocalDrivenTick(long tick, in Physics3DNetInputFrameView input)
        {
            _pose = new Physics3DNetPredictedPose(
                tick,
                new Vector3(_pose.PositionCm.X + _stepX, _pose.PositionCm.Y, _pose.PositionCm.Z),
                Quaternion.Identity,
                new Vector3(_stepX * 30f, 0f, 0f),
                Vector3.Zero);
            return _pose;
        }
    }

    private sealed class CapturingSender : IPhysics3DNetReliableDatagramSender
    {
        public int SendCount { get; private set; }

        public void SendReliableDatagram(ReadOnlySpan<byte> datagram)
        {
            if (datagram.Length == 0)
            {
                throw new ArgumentException("Empty datagram.");
            }

            SendCount++;
        }
    }

    private sealed class LoopbackDatagramTransport : IPhysics3DNetDatagramTransport
    {
        private readonly int _endpointCapacity;
        private readonly int _maxPayload;
        private readonly IPEndPoint[] _endpoints;
        private readonly bool[] _occupied;
        private readonly Queue<(int endpointSlot, byte[] datagram)> _inbound = new();
        private readonly Queue<(int endpointSlot, byte[] datagram)> _outbound = new();

        public LoopbackDatagramTransport(Physics3DNetConfig config)
        {
            _endpointCapacity = config.TransportEndpointCapacity;
            _maxPayload = config.MaxDatagramPayloadBytes;
            _endpoints = new IPEndPoint[_endpointCapacity];
            _occupied = new bool[_endpointCapacity];
        }

        public bool IsBound => true;
        public bool IsDisposed { get; private set; }
        public int EndpointCapacity => _endpointCapacity;
        public int MaxDatagramPayloadBytes => _maxPayload;

        public void Bind(IPEndPoint localEndPoint)
        {
        }

        public int RegisterRemoteEndpoint(IPEndPoint remoteEndPoint)
        {
            for (int i = 0; i < _endpointCapacity; i++)
            {
                if (_occupied[i])
                {
                    continue;
                }

                _endpoints[i] = remoteEndPoint;
                _occupied[i] = true;
                return i;
            }

            throw new Physics3DNetCapacityExceededException("transport endpoint registry", _endpointCapacity, 0);
        }

        public void UnregisterEndpoint(int endpointSlot)
        {
            _occupied[endpointSlot] = false;
            _endpoints[endpointSlot] = null!;
        }

        public bool TryGetEndpoint(int endpointSlot, out IPEndPoint endpoint)
        {
            if ((uint)endpointSlot >= (uint)_endpointCapacity || !_occupied[endpointSlot])
            {
                endpoint = null!;
                return false;
            }

            endpoint = _endpoints[endpointSlot];
            return true;
        }

        public void EnqueueReceive(int endpointSlot, ReadOnlySpan<byte> datagram)
        {
            _inbound.Enqueue((endpointSlot, datagram.ToArray()));
        }

        public bool TryReceive(Span<byte> destination, out int bytesReceived, out int endpointSlot)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_inbound.Count == 0)
            {
                bytesReceived = 0;
                endpointSlot = -1;
                return false;
            }

            (int slot, byte[] datagram) = _inbound.Dequeue();
            if (datagram.Length > destination.Length)
            {
                throw new Physics3DNetCapacityExceededException("transport receive destination", destination.Length, 0);
            }

            datagram.CopyTo(destination);
            bytesReceived = datagram.Length;
            endpointSlot = slot;
            return true;
        }

        public void Send(int endpointSlot, ReadOnlySpan<byte> payload)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (!_occupied[endpointSlot])
            {
                throw new InvalidOperationException("Endpoint not registered.");
            }

            if (payload.Length > _maxPayload)
            {
                throw new Physics3DNetCapacityExceededException("datagram payload", _maxPayload, 0);
            }

            _outbound.Enqueue((endpointSlot, payload.ToArray()));
        }

        public bool TryDequeueSent(out int endpointSlot, out byte[] datagram)
        {
            if (_outbound.Count == 0)
            {
                endpointSlot = -1;
                datagram = Array.Empty<byte>();
                return false;
            }

            (endpointSlot, datagram) = _outbound.Dequeue();
            return true;
        }

        public void Dispose() => IsDisposed = true;
    }
}
