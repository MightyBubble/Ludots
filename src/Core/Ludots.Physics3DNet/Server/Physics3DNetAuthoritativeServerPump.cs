using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Injected authoritative simulation step. Physics3DNet advances lifecycle; the port owns the Physics3D step.
/// </summary>
public interface IPhysics3DNetSimulationTickPort
{
    void SimulateAuthoritativeTick(long executingTick);
}

/// <summary>
/// Supplies sorted AOI interest for one connected client at a snapshot boundary.
/// </summary>
public interface IPhysics3DNetClientInterestProvider
{
    int FillInterest(int clientSlot, long snapshotTick, Span<Physics3DNetAoiInterest> destination);
}

public enum Physics3DNetServerTickResultKind : byte
{
    Advanced = 1,
    HeldForMissingInputs = 2,
    SnapshotPublished = 3,
    ReliableRetryExhausted = 4
}

public readonly struct Physics3DNetServerTickResult
{
    public Physics3DNetServerTickResult(
        Physics3DNetServerTickResultKind kind,
        long committedTick,
        long snapshotTick,
        int datagramsSent,
        int missingInputCount,
        uint exhaustedReliableSequence)
    {
        Kind = kind;
        CommittedTick = committedTick;
        SnapshotTick = snapshotTick;
        DatagramsSent = datagramsSent;
        MissingInputCount = missingInputCount;
        ExhaustedReliableSequence = exhaustedReliableSequence;
    }

    public Physics3DNetServerTickResultKind Kind { get; }
    public long CommittedTick { get; }
    public long SnapshotTick { get; }
    public int DatagramsSent { get; }
    public int MissingInputCount { get; }
    public uint ExhaustedReliableSequence { get; }
}

public readonly struct Physics3DNetServerClientBinding
{
    public Physics3DNetServerClientBinding(
        int clientSlot,
        int endpointSlot,
        uint sessionId,
        int networkPlayerId,
        int generation,
        long baselineId)
    {
        if ((uint)clientSlot > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(clientSlot));
        }

        Physics3DNetValidation.RequireNonNegativeId(networkPlayerId, nameof(networkPlayerId));
        Physics3DNetValidation.RequirePositiveGeneration(generation, nameof(generation));
        Physics3DNetValidation.RequireNonNegativeBaselineId(baselineId, nameof(baselineId));

        ClientSlot = clientSlot;
        EndpointSlot = endpointSlot;
        SessionId = sessionId;
        NetworkPlayerId = networkPlayerId;
        Generation = generation;
        BaselineId = baselineId;
    }

    public int ClientSlot { get; }
    public int EndpointSlot { get; }
    public uint SessionId { get; }
    public int NetworkPlayerId { get; }
    public int Generation { get; }
    public long BaselineId { get; }
}

/// <summary>
/// Fixed 30Hz authoritative pump: drain datagrams, validate session/compatibility, submit inputs,
/// advance lifecycle exactly once, invoke simulation, build/send AOI snapshots.
/// Never publishes ExecutingTick.
/// </summary>
public sealed class Physics3DNetAuthoritativeServerPump : IPhysics3DNetReliableDatagramSender
{
    private readonly Physics3DNetConfig _config;
    private readonly Physics3DNetTickLifecycle _lifecycle;
    private readonly Physics3DNetInputRing _inputRing;
    private readonly Physics3DNetAuthoritativeSnapshotStore _snapshotStore;
    private readonly Physics3DNetAoiDeltaBuilder _aoi;
    private readonly Physics3DNetPacketCodec _codec;
    private readonly IPhysics3DNetDatagramTransport _transport;
    private readonly IPhysics3DNetSimulationTickPort _simulation;
    private readonly IPhysics3DNetClientInterestProvider _interestProvider;
    private readonly Physics3DNetCompatibilityGate _compatibilityGate;
    private readonly Physics3DNetReplayTimeline? _timeline;

    private readonly bool[] _clientOccupied;
    private readonly int[] _clientEndpointSlot;
    private readonly uint[] _clientSessionId;
    private readonly int[] _clientNetworkPlayerId;
    private readonly int[] _clientGeneration;
    private readonly long[] _clientBaselineId;
    private readonly Physics3DNetReliableControlChannel[] _clientReliable;
    private readonly uint[] _clientSnapshotSequence;

    private readonly byte[] _receiveBuffer;
    private readonly byte[] _sendBuffer;
    private readonly byte[] _reliablePayloadScratch;
    private readonly int[] _missingSlots;
    private readonly Physics3DNetAoiInterest[] _interestScratch;
    private readonly Physics3DNetSnapshotEntityWrite[] _deltaScratch;
    private readonly Physics3DNetSnapshotEntityWrite[] _fragmentScratch;

    private int _activeSendEndpointSlot = -1;
    private int _datagramsSent;

    public Physics3DNetAuthoritativeServerPump(
        Physics3DNetConfig config,
        Physics3DNetTickLifecycle lifecycle,
        Physics3DNetInputRing inputRing,
        Physics3DNetAuthoritativeSnapshotStore snapshotStore,
        Physics3DNetAoiDeltaBuilder aoi,
        Physics3DNetPacketCodec codec,
        IPhysics3DNetDatagramTransport transport,
        IPhysics3DNetSimulationTickPort simulation,
        IPhysics3DNetClientInterestProvider interestProvider,
        Physics3DNetCompatibilityGate compatibilityGate,
        Physics3DNetReplayTimeline? timeline = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(inputRing);
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(aoi);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(interestProvider);
        ArgumentNullException.ThrowIfNull(compatibilityGate);
        config.Validate();

        if (!ReferenceEquals(inputRing.Lifecycle, lifecycle))
        {
            throw new ArgumentException("Input ring must observe the same tick lifecycle instance.", nameof(inputRing));
        }

        _config = config;
        _lifecycle = lifecycle;
        _inputRing = inputRing;
        _snapshotStore = snapshotStore;
        _aoi = aoi;
        _codec = codec;
        _transport = transport;
        _simulation = simulation;
        _interestProvider = interestProvider;
        _compatibilityGate = compatibilityGate;
        _timeline = timeline;

        int clientCapacity = config.ClientCapacity;
        _clientOccupied = new bool[clientCapacity];
        _clientEndpointSlot = new int[clientCapacity];
        _clientSessionId = new uint[clientCapacity];
        _clientNetworkPlayerId = new int[clientCapacity];
        _clientGeneration = new int[clientCapacity];
        _clientBaselineId = new long[clientCapacity];
        _clientReliable = new Physics3DNetReliableControlChannel[clientCapacity];
        _clientSnapshotSequence = new uint[clientCapacity];

        for (int i = 0; i < clientCapacity; i++)
        {
            _clientReliable[i] = new Physics3DNetReliableControlChannel(config, codec);
        }

        _receiveBuffer = new byte[config.DatagramReceiveBufferBytes];
        _sendBuffer = new byte[config.DatagramSendBufferBytes];
        _reliablePayloadScratch = new byte[256];
        _missingSlots = new int[config.PlayerCapacity];
        _interestScratch = new Physics3DNetAoiInterest[config.AoiEntityCapacityPerClient];
        _deltaScratch = new Physics3DNetSnapshotEntityWrite[checked(config.AoiEntityCapacityPerClient * 2)];
        _fragmentScratch = new Physics3DNetSnapshotEntityWrite[config.SnapshotEntitiesPerDatagram];
    }

    public Physics3DNetTickLifecycle Lifecycle => _lifecycle;
    public int ConnectedClientCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _clientOccupied.Length; i++)
            {
                if (_clientOccupied[i])
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int RegisterClient(in Physics3DNetServerClientBinding binding)
    {
        if ((uint)binding.ClientSlot >= (uint)_clientOccupied.Length)
        {
            throw new Physics3DNetCapacityExceededException("server client slots", _clientOccupied.Length, tick: 0);
        }

        if (_clientOccupied[binding.ClientSlot])
        {
            throw new InvalidOperationException($"Client slot {binding.ClientSlot} is already occupied.");
        }

        if (!_transport.TryGetEndpoint(binding.EndpointSlot, out _))
        {
            throw new InvalidOperationException(
                $"Endpoint slot {binding.EndpointSlot} is not registered on the transport.");
        }

        for (int i = 0; i < _clientOccupied.Length; i++)
        {
            if (!_clientOccupied[i])
            {
                continue;
            }

            if (_clientSessionId[i] == binding.SessionId)
            {
                throw new InvalidOperationException($"Session id {binding.SessionId} is already bound.");
            }

            if (_clientNetworkPlayerId[i] == binding.NetworkPlayerId)
            {
                throw new InvalidOperationException(
                    $"Network player id {binding.NetworkPlayerId} is already bound to client slot {i}.");
            }
        }

        _inputRing.RegisterPlayer(binding.NetworkPlayerId, binding.Generation, binding.ClientSlot);
        _aoi.AcknowledgeBaseline(binding.ClientSlot, binding.BaselineId);

        _clientOccupied[binding.ClientSlot] = true;
        _clientEndpointSlot[binding.ClientSlot] = binding.EndpointSlot;
        _clientSessionId[binding.ClientSlot] = binding.SessionId;
        _clientNetworkPlayerId[binding.ClientSlot] = binding.NetworkPlayerId;
        _clientGeneration[binding.ClientSlot] = binding.Generation;
        _clientBaselineId[binding.ClientSlot] = binding.BaselineId;
        _clientSnapshotSequence[binding.ClientSlot] = 0;
        return binding.ClientSlot;
    }

    public void UnregisterClient(int clientSlot)
    {
        ValidateClientSlot(clientSlot);
        if (!_clientOccupied[clientSlot])
        {
            throw new InvalidOperationException($"Client slot {clientSlot} is not connected.");
        }

        _inputRing.UnregisterPlayer(clientSlot);
        _aoi.InvalidateBaseline(clientSlot);
        _clientOccupied[clientSlot] = false;
        _clientEndpointSlot[clientSlot] = 0;
        _clientSessionId[clientSlot] = 0;
        _clientNetworkPlayerId[clientSlot] = 0;
        _clientGeneration[clientSlot] = 0;
        _clientBaselineId[clientSlot] = 0;
        _clientSnapshotSequence[clientSlot] = 0;
        _clientReliable[clientSlot] = new Physics3DNetReliableControlChannel(_config, _codec);
    }

    public bool TryGetClient(int clientSlot, out Physics3DNetServerClientBinding binding)
    {
        if ((uint)clientSlot >= (uint)_clientOccupied.Length || !_clientOccupied[clientSlot])
        {
            binding = default;
            return false;
        }

        binding = new Physics3DNetServerClientBinding(
            clientSlot,
            _clientEndpointSlot[clientSlot],
            _clientSessionId[clientSlot],
            _clientNetworkPlayerId[clientSlot],
            _clientGeneration[clientSlot],
            _clientBaselineId[clientSlot]);
        return true;
    }

    public Physics3DNetServerTickResult TickOnce()
    {
        if (_lifecycle.IsExecuting)
        {
            throw new InvalidOperationException(
                $"Cannot tick server pump while ExecutingTick {_lifecycle.ExecutingTick} is still open.");
        }

        _datagramsSent = 0;
        DrainInboundDatagrams();

        long nextTick = _lifecycle.CommittedTick + 1;
        if (!_inputRing.TryBeginAuthoritativeExecute(nextTick, _missingSlots, out Physics3DNetInputExecuteGateResult gate))
        {
            if (_config.MissingInputPolicy == Physics3DNetMissingInputPolicy.FailExplicit)
            {
                throw new Physics3DNetMissingInputException(nextTick, gate.MissingCount);
            }

            return new Physics3DNetServerTickResult(
                Physics3DNetServerTickResultKind.HeldForMissingInputs,
                _lifecycle.CommittedTick,
                _lifecycle.SnapshotTick,
                _datagramsSent,
                gate.MissingCount,
                exhaustedReliableSequence: 0);
        }

        if (_lifecycle.ExecutingTick != nextTick)
        {
            throw new InvalidOperationException(
                $"Lifecycle ExecutingTick {_lifecycle.ExecutingTick} must equal next tick {nextTick} after BeginExecute.");
        }

        _simulation.SimulateAuthoritativeTick(nextTick);
        _lifecycle.Commit();
        if (_lifecycle.ExecutingTick != 0)
        {
            throw new InvalidOperationException("Lifecycle must clear ExecutingTick on Commit.");
        }

        _inputRing.AcknowledgeInputFramesAfterCommit(nextTick);
        _timeline?.RecordInputAccepted(nextTick);

        Physics3DNetServerTickResultKind kind = Physics3DNetServerTickResultKind.Advanced;
        if (_lifecycle.IsSnapshotBoundary(nextTick))
        {
            PublishAndSendSnapshots(nextTick);
            kind = Physics3DNetServerTickResultKind.SnapshotPublished;
        }

        if (!ProcessReliableRetransmits(nextTick, out uint exhaustedSequence))
        {
            return new Physics3DNetServerTickResult(
                Physics3DNetServerTickResultKind.ReliableRetryExhausted,
                _lifecycle.CommittedTick,
                _lifecycle.SnapshotTick,
                _datagramsSent,
                missingInputCount: 0,
                exhaustedSequence);
        }

        return new Physics3DNetServerTickResult(
            kind,
            _lifecycle.CommittedTick,
            _lifecycle.SnapshotTick,
            _datagramsSent,
            missingInputCount: 0,
            exhaustedReliableSequence: 0);
    }

    void IPhysics3DNetReliableDatagramSender.SendReliableDatagram(ReadOnlySpan<byte> datagram)
    {
        if (_activeSendEndpointSlot < 0)
        {
            throw new InvalidOperationException("No active endpoint for reliable datagram send.");
        }

        _transport.Send(_activeSendEndpointSlot, datagram);
        _datagramsSent++;
    }

    private void DrainInboundDatagrams()
    {
        while (_transport.TryReceive(_receiveBuffer, out int bytesReceived, out int endpointSlot))
        {
            ReadOnlySpan<byte> datagram = _receiveBuffer.AsSpan(0, bytesReceived);
            Physics3DNetPacketHeader header = _codec.PeekHeader(datagram);
            switch (header.PacketType)
            {
                case Physics3DNetPacketType.Handshake:
                    HandleHandshake(datagram, endpointSlot);
                    break;
                case Physics3DNetPacketType.ClientInput:
                    HandleClientInput(datagram, endpointSlot);
                    break;
                case Physics3DNetPacketType.Acknowledgement:
                    HandleAcknowledgement(datagram, endpointSlot);
                    break;
                case Physics3DNetPacketType.ReliableControl:
                    HandleReliableControl(datagram, endpointSlot);
                    break;
                case Physics3DNetPacketType.SnapshotPayload:
                    throw new Physics3DNetPacketMalformedException(
                        "Server pump does not accept SnapshotPayload from clients.");
                default:
                    throw new Physics3DNetPacketMalformedException($"Unhandled packet type {header.PacketType}.");
            }
        }
    }

    private void HandleHandshake(ReadOnlySpan<byte> datagram, int endpointSlot)
    {
        Physics3DNetHandshakePacket handshake = _codec.DecodeHandshake(datagram);
        _compatibilityGate.RequireMatch(handshake.Fingerprint);

        int clientSlot = handshake.RequestedClientSlot;
        if (clientSlot < 0)
        {
            clientSlot = FindFreeClientSlot();
            if (clientSlot < 0)
            {
                throw new Physics3DNetCapacityExceededException("server client slots", _clientOccupied.Length, tick: 0);
            }
        }

        var binding = new Physics3DNetServerClientBinding(
            clientSlot,
            endpointSlot,
            handshake.SessionId == 0 ? AllocateSessionId(clientSlot) : handshake.SessionId,
            handshake.NetworkPlayerId,
            handshake.Generation,
            baselineId: 1);
        RegisterClient(binding);

        Span<byte> acceptPayload = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(acceptPayload.Slice(0, 4), binding.ClientSlot);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(acceptPayload.Slice(4, 4), binding.SessionId);

        int encoded = _clientReliable[clientSlot].EncodeAndQueue(
            binding.SessionId,
            Physics3DNetReliableControlOpcode.SessionAccept,
            acceptPayload,
            _lifecycle.CommittedTick,
            _sendBuffer,
            out _);
        _transport.Send(endpointSlot, _sendBuffer.AsSpan(0, encoded));
        _datagramsSent++;
    }

    private void HandleClientInput(ReadOnlySpan<byte> datagram, int endpointSlot)
    {
        Physics3DNetClientInputPacket packet = _codec.DecodeClientInput(datagram);
        if (!TryResolveClient(packet.SessionId, endpointSlot, out int clientSlot))
        {
            throw new InvalidOperationException(
                $"ClientInput session {packet.SessionId} is not bound to endpoint slot {endpointSlot}.");
        }

        if (packet.Input.NetworkPlayerId != _clientNetworkPlayerId[clientSlot]
            || packet.Input.Generation != _clientGeneration[clientSlot])
        {
            throw new InvalidOperationException(
                $"ClientInput identity mismatch for client slot {clientSlot}: "
                + $"packet player {packet.Input.NetworkPlayerId}:{packet.Input.Generation}, "
                + $"bound {_clientNetworkPlayerId[clientSlot]}:{_clientGeneration[clientSlot]}.");
        }

        _ = _inputRing.Submit(packet.Input);
    }

    private void HandleAcknowledgement(ReadOnlySpan<byte> datagram, int endpointSlot)
    {
        Physics3DNetAcknowledgementPacket ack = _codec.DecodeAcknowledgement(datagram);
        if (!TryResolveClient(ack.SessionId, endpointSlot, out int clientSlot))
        {
            throw new InvalidOperationException(
                $"Acknowledgement session {ack.SessionId} is not bound to endpoint slot {endpointSlot}.");
        }

        _ = _clientReliable[clientSlot].ApplyAcknowledgement(ack);
    }

    private void HandleReliableControl(ReadOnlySpan<byte> datagram, int endpointSlot)
    {
        _codec.DecodeReliableControlFields(
            datagram,
            _reliablePayloadScratch,
            out uint sessionId,
            out _,
            out Physics3DNetReliableControlOpcode opcode,
            out int payloadLength);

        if (!TryResolveClient(sessionId, endpointSlot, out int clientSlot))
        {
            throw new InvalidOperationException(
                $"ReliableControl session {sessionId} is not bound to endpoint slot {endpointSlot}.");
        }

        Physics3DNetReliableReceiveResult receive = _clientReliable[clientSlot].ReceiveControlDatagram(
            datagram,
            _reliablePayloadScratch,
            out payloadLength);
        if (receive.Delivery == Physics3DNetReliableDeliveryResult.DuplicateSuppressed)
        {
            SendAck(clientSlot);
            return;
        }

        if (opcode == Physics3DNetReliableControlOpcode.BaselineAck)
        {
            if (payloadLength < 8)
            {
                throw new Physics3DNetPacketMalformedException("BaselineAck payload must contain baseline id.");
            }

            long baselineId = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
                _reliablePayloadScratch.AsSpan(0, 8));
            _aoi.AcknowledgeBaseline(clientSlot, baselineId);
            _clientBaselineId[clientSlot] = baselineId;
        }

        SendAck(clientSlot);
    }

    private void SendAck(int clientSlot)
    {
        Physics3DNetAcknowledgementPacket ack = _clientReliable[clientSlot].BuildAcknowledgement(_clientSessionId[clientSlot]);
        int encoded = _codec.EncodeAcknowledgement(ack, _sendBuffer);
        _transport.Send(_clientEndpointSlot[clientSlot], _sendBuffer.AsSpan(0, encoded));
        _datagramsSent++;
    }

    private void PublishAndSendSnapshots(long snapshotTick)
    {
        // Snapshot store may already hold authority snapshot from the simulation port.
        // When empty, build a zero-entity published snapshot so AOI still runs.
        if (_snapshotStore.SnapshotTick != snapshotTick)
        {
            _snapshotStore.ReplaceAll(snapshotTick, baselineId: 1, ReadOnlySpan<Physics3DNetSnapshotEntityWrite>.Empty);
        }

        _lifecycle.PublishSnapshot(snapshotTick);
        if (_lifecycle.IsExecuting)
        {
            throw new InvalidOperationException("Snapshot publication must never observe an open ExecutingTick.");
        }

        _timeline?.RecordSnapshotPublished(snapshotTick);

        for (int clientSlot = 0; clientSlot < _clientOccupied.Length; clientSlot++)
        {
            if (!_clientOccupied[clientSlot])
            {
                continue;
            }

            int interestCount = _interestProvider.FillInterest(clientSlot, snapshotTick, _interestScratch);
            if (interestCount < 0 || interestCount > _interestScratch.Length)
            {
                throw new Physics3DNetCapacityExceededException(
                    "client interest destination",
                    _interestScratch.Length,
                    snapshotTick);
            }

            Physics3DNetAoiDeltaBuildResult delta = _aoi.BuildDelta(
                clientSlot,
                snapshotTick,
                _clientBaselineId[clientSlot],
                _interestScratch.AsSpan(0, interestCount),
                _deltaScratch);

            if (delta.RequiresFullSnapshot)
            {
                // Explicit resync path: restore baseline acknowledgement, then emit full tracked delta.
                _aoi.AcknowledgeBaseline(clientSlot, _clientBaselineId[clientSlot]);
                delta = _aoi.BuildDelta(
                    clientSlot,
                    snapshotTick,
                    _clientBaselineId[clientSlot],
                    _interestScratch.AsSpan(0, interestCount),
                    _deltaScratch);
                if (delta.RequiresFullSnapshot)
                {
                    throw new InvalidOperationException(
                        $"AOI baseline for client {clientSlot} remained missing after explicit AcknowledgeBaseline.");
                }
            }

            SendSnapshotFragments(
                clientSlot,
                snapshotTick,
                _clientBaselineId[clientSlot],
                _deltaScratch.AsSpan(0, delta.WrittenCount));
        }
    }

    private void SendSnapshotFragments(
        int clientSlot,
        long snapshotTick,
        long baselineId,
        ReadOnlySpan<Physics3DNetSnapshotEntityWrite> entities)
    {
        int perDatagram = _config.SnapshotEntitiesPerDatagram;
        int fragmentCount = entities.Length == 0 ? 1 : (entities.Length + perDatagram - 1) / perDatagram;
        if (fragmentCount > ushort.MaxValue)
        {
            throw new Physics3DNetCapacityExceededException("snapshot fragments", ushort.MaxValue, snapshotTick);
        }

        uint sequence = ++_clientSnapshotSequence[clientSlot];
        if (sequence == 0)
        {
            sequence = ++_clientSnapshotSequence[clientSlot];
        }

        for (int fragmentIndex = 0; fragmentIndex < fragmentCount; fragmentIndex++)
        {
            int offset = fragmentIndex * perDatagram;
            int count = entities.Length == 0 ? 0 : Math.Min(perDatagram, entities.Length - offset);
            entities.Slice(offset, count).CopyTo(_fragmentScratch);
            var header = new Physics3DNetSnapshotPacketHeader(
                _clientSessionId[clientSlot],
                snapshotTick,
                baselineId,
                sequence,
                (ushort)fragmentIndex,
                (ushort)fragmentCount,
                (ushort)count);
            int encoded = _codec.EncodeSnapshotPayload(header, _fragmentScratch.AsSpan(0, count), _sendBuffer);
            _transport.Send(_clientEndpointSlot[clientSlot], _sendBuffer.AsSpan(0, encoded));
            _datagramsSent++;
        }
    }

    private bool ProcessReliableRetransmits(long tick, out uint exhaustedSequence)
    {
        exhaustedSequence = 0;
        for (int clientSlot = 0; clientSlot < _clientOccupied.Length; clientSlot++)
        {
            if (!_clientOccupied[clientSlot])
            {
                continue;
            }

            _activeSendEndpointSlot = _clientEndpointSlot[clientSlot];
            Physics3DNetReliableChannelTickResult result = _clientReliable[clientSlot].ProcessRetransmits(
                _clientSessionId[clientSlot],
                tick,
                _sendBuffer,
                this);
            _activeSendEndpointSlot = -1;
            if (result.RetryExhausted)
            {
                exhaustedSequence = result.ExhaustedSequence;
                return false;
            }
        }

        return true;
    }

    private bool TryResolveClient(uint sessionId, int endpointSlot, out int clientSlot)
    {
        for (int i = 0; i < _clientOccupied.Length; i++)
        {
            if (_clientOccupied[i]
                && _clientSessionId[i] == sessionId
                && _clientEndpointSlot[i] == endpointSlot)
            {
                clientSlot = i;
                return true;
            }
        }

        clientSlot = -1;
        return false;
    }

    private int FindFreeClientSlot()
    {
        for (int i = 0; i < _clientOccupied.Length; i++)
        {
            if (!_clientOccupied[i])
            {
                return i;
            }
        }

        return -1;
    }

    private static uint AllocateSessionId(int clientSlot) =>
        unchecked((uint)(clientSlot + 1) * 0x9E3779B9u);

    private void ValidateClientSlot(int clientSlot)
    {
        if ((uint)clientSlot >= (uint)_clientOccupied.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(clientSlot), clientSlot, "Client slot out of range.");
        }
    }
}
