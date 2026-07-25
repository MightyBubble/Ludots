using System;
using System.Diagnostics.CodeAnalysis;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;

namespace Ludots.Core.Networking.Runtime
{
    public sealed class AuthoritativeServerNetworkRuntime : INetworkRuntimePort
    {
        private const byte SeatEmpty = 0;
        private const byte SeatConnected = 1;
        private const byte SeatAwaitingReconnect = 2;

        private readonly NetworkRuntimeCapacity _capacity;
        private readonly NetworkTransportPortOwnership _transportOwnership;
        private readonly IServerConnectionEventPort _connectionEvents;
        private readonly IServerDatagramPort _datagrams;
        private readonly IServerConnectionControlPort _connectionControl;
        private readonly AuthoritativeSessionRegistry _sessions;
        private readonly NetworkCommandIngress _commands;
        private readonly NetworkCommandAdmissionResultBuffer _commandResults;
        private readonly IAuthoritativeSeatControllerResolver _controllers;
        private readonly IAuthoritativeReplicationInterestBatchPort _replicationInterest;
        private readonly IAuthoritativeReplicationSeatRuntimeFactory _replicationSeatFactory;
        private readonly AuthoritativeReplicationSeatRuntime?[] _replicationSeats;
        private readonly AuthoritativeFixedInputIngress _fixedInput;
        private readonly INetworkRuntimeObserver _observer;
        private readonly SnapshotFragmentEncoder _snapshotEncoder;
        private readonly FixedServerDatagramSendQueue _outbound;

        private readonly int[] _transportConnections;
        private readonly byte[] _seatStates;
        private readonly int[] _seatConnections;
        private readonly uint[] _seatGenerations;
        private readonly int[] _seatPlayerIds;
        private readonly Arch.Core.Entity[] _seatControllers;
        private readonly uint[] _seatDisconnectTicks;
        private readonly bool[] _seatNeedsFull;
        private readonly ulong[] _seatAcknowledgedSnapshots;
        private readonly ulong[] _seatLastSentSnapshots;
        private readonly ulong[] _seatLastDisclosureSequences;
        private readonly ulong[] _ackHistorySnapshotIds;
        private readonly ulong[] _ackHistoryDisclosureSequences;
        private readonly int[] _ackHistoryWriteIndices;
        private readonly CommandFragmentReassembler[] _commandReassemblers;
        private readonly NetworkCommandWireEntry[] _commandEntries;
        private readonly SessionSeatBinding[] _publishSeats;
        private readonly SessionSeatBinding[] _expiredSeats;
        private readonly NetworkCommandAdmissionOutcome[] _pendingAdmissions;
        private readonly bool[] _pendingAdmissionActive;

        private readonly byte[] _receiveBuffer;
        private readonly byte[] _payloadBuffer;
        private readonly byte[] _datagramBuffer;
        private readonly byte[] _snapshotBuffer;
        private readonly uint[] _fixedInputTargetTicks;
        private readonly byte[] _fixedInputPayloads;
        private readonly FixedInputAdmissionDisposition[] _fixedInputDispositions;
        private readonly bool[] _pendingFixedInputAck;
        private readonly byte[] _pendingFixedInputAckPayload;
        private readonly int[] _pendingFixedInputAckBytes;

        private uint _currentTick;
        private uint _lastCommittedTick;
        private ulong _nextSnapshotId;
        private bool _disposed;
        private bool _faulted;
        private NetworkRuntimeFault _lastFault;

        public AuthoritativeServerNetworkRuntime(
            in NetworkRuntimeCapacity capacity,
            NetworkTransportPortOwnership transportOwnership,
            IServerConnectionEventPort connectionEvents,
            IServerDatagramPort datagrams,
            IServerConnectionControlPort connectionControl,
            AuthoritativeSessionRegistry sessions,
            NetworkCommandIngress commands,
            NetworkCommandAdmissionResultBuffer commandResults,
            IAuthoritativeSeatControllerResolver controllers,
            IAuthoritativeReplicationInterestBatchPort replicationInterest,
            IAuthoritativeReplicationSeatRuntimeFactory replicationSeatFactory,
            AuthoritativeFixedInputIngress fixedInput,
            INetworkRuntimeObserver observer)
        {
            _capacity = capacity;
            _transportOwnership = transportOwnership;
            _connectionEvents = connectionEvents ?? throw new ArgumentNullException(nameof(connectionEvents));
            _datagrams = datagrams ?? throw new ArgumentNullException(nameof(datagrams));
            _connectionControl = connectionControl ?? throw new ArgumentNullException(nameof(connectionControl));
            NetworkTransportPortLifetime.Validate(
                transportOwnership,
                _connectionEvents,
                _datagrams,
                _connectionControl);
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _commandResults = commandResults ?? throw new ArgumentNullException(nameof(commandResults));
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            _replicationInterest = replicationInterest ?? throw new ArgumentNullException(nameof(replicationInterest));
            _replicationSeatFactory = replicationSeatFactory ?? throw new ArgumentNullException(nameof(replicationSeatFactory));
            _fixedInput = fixedInput ?? throw new ArgumentNullException(nameof(fixedInput));
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));

            if (sessions.SeatCapacity != replicationSeatFactory.SeatCapacity ||
                sessions.SeatCapacity != replicationInterest.SeatCapacity ||
                sessions.SeatCapacity > capacity.ConnectionCapacity ||
                replicationSeatFactory.GlobalEntityCapacity != capacity.GlobalEntityCapacity ||
                replicationSeatFactory.ReplicationEntityCapacityPerSeat != capacity.ReplicationEntityCapacityPerSeat ||
                replicationInterest.EntityCapacityPerSeat != capacity.ReplicationEntityCapacityPerSeat)
            {
                throw new ArgumentException(
                    "Session, replication-seat factory, connection, global entity, and per-seat capacities must agree.",
                    nameof(replicationSeatFactory));
            }

            ValidateFixedInputIngress(in capacity, sessions, fixedInput);

            _snapshotEncoder = new SnapshotFragmentEncoder(
                capacity.MaxDatagramPayloadBytes,
                capacity.MaxSnapshotBytes,
                capacity.MaxSnapshotFragments);
            _outbound = new FixedServerDatagramSendQueue(capacity.OutboundQueueCapacity, capacity.MaxDatagramPayloadBytes);

            _transportConnections = new int[capacity.ConnectionCapacity];
            int seats = sessions.SeatCapacity;
            _seatStates = new byte[seats];
            _seatConnections = new int[seats];
            _seatGenerations = new uint[seats];
            _seatPlayerIds = new int[seats];
            _seatControllers = new Arch.Core.Entity[seats];
            _replicationSeats = new AuthoritativeReplicationSeatRuntime?[seats];
            _seatDisconnectTicks = new uint[seats];
            _seatNeedsFull = new bool[seats];
            _seatAcknowledgedSnapshots = new ulong[seats];
            _seatLastSentSnapshots = new ulong[seats];
            _seatLastDisclosureSequences = new ulong[seats];
            _ackHistorySnapshotIds = new ulong[checked(seats * capacity.AcknowledgementHistoryCapacity)];
            _ackHistoryDisclosureSequences = new ulong[_ackHistorySnapshotIds.Length];
            _ackHistoryWriteIndices = new int[seats];
            _commandReassemblers = new CommandFragmentReassembler[seats];
            for (int i = 0; i < seats; i++)
            {
                _commandReassemblers[i] = new CommandFragmentReassembler(
                    capacity.MaxCommandPayloadBytes,
                    capacity.MaxCommandFragments);
            }

            _commandEntries = new NetworkCommandWireEntry[capacity.MaxCommandEntries];
            _publishSeats = new SessionSeatBinding[seats];
            _expiredSeats = new SessionSeatBinding[seats];
            _pendingAdmissions = new NetworkCommandAdmissionOutcome[commandResults.Capacity];
            _pendingAdmissionActive = new bool[commandResults.Capacity];
            _receiveBuffer = new byte[capacity.MaxDatagramPayloadBytes];
            _payloadBuffer = new byte[Math.Max(capacity.MaxDatagramPayloadBytes, HandshakeWireCodec.ResponseSizeInBytes)];
            _datagramBuffer = new byte[capacity.MaxDatagramPayloadBytes];
            _snapshotBuffer = new byte[capacity.MaxSnapshotBytes];
            _fixedInputTargetTicks = new uint[capacity.FixedInputMaxFramesPerBatch];
            _fixedInputPayloads = new byte[checked(capacity.FixedInputMaxFramesPerBatch * capacity.FixedInputFramePayloadBytes)];
            _fixedInputDispositions = new FixedInputAdmissionDisposition[capacity.FixedInputMaxFramesPerBatch];
            _pendingFixedInputAck = new bool[seats];
            _pendingFixedInputAckPayload = new byte[checked(seats * NetworkFixedInputAcknowledgement.SizeInBytes)];
            _pendingFixedInputAckBytes = new int[seats];
        }

        public NetworkProcessRole Role => NetworkProcessRole.AuthoritativeServer;
        public bool IsFaulted => _faulted;
        public NetworkRuntimeFault LastFault => _lastFault;
        public int FixedInputMissingAtDeadlineCount => _fixedInput.MissingAtDeadlineCount;
        public int FixedInputAcceptedCount => _fixedInput.AcceptedCount;

        public FixedInputLookupResult TryGetFixedInput(
            in SessionSeatBinding seat,
            uint tick,
            Span<byte> destination,
            out int bytesWritten) =>
            _fixedInput.TryGet(in seat, tick, destination, out bytesWritten);

        public void PumpTransport()
        {
            EnsureOperational();
            FlushOutbound();
            FlushPendingFixedInputAcknowledgements();
            _connectionEvents.Pump();
            while (_connectionEvents.TryReceiveConnectionEvent(out ServerConnectionEvent connectionEvent))
            {
                ProcessConnectionEvent(in connectionEvent);
            }

            FlushAdmissionResults();
            while (_datagrams.TryReceive(
                _receiveBuffer,
                out int bytesReceived,
                out ConnectionId connection,
                out ChannelId channel))
            {
                ProcessDatagram(connection, channel, _receiveBuffer.AsSpan(0, bytesReceived));
                FlushAdmissionResults();
            }

            FlushPendingAdmissions();
            FlushOutbound();
            FlushPendingFixedInputAcknowledgements();
        }

        public void BeforeAuthoritativeTick(uint executingTick)
        {
            EnsureOperational();
            if (executingTick == 0 || executingTick > FixedInputWireCodec.MaxSimulationTick)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: unchecked((int)executingTick));
            }

            // Command scheduling and fixed-input admission share one Tick truth.
            if (!_fixedInput.TickState.IsExecuting ||
                (uint)_fixedInput.TickState.ExecutingTick != executingTick)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: unchecked((int)executingTick));
            }

            if (executingTick < _currentTick)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: unchecked((int)executingTick));
            }

            _currentTick = executingTick;
            ExpireDisconnectedSeats(executingTick);
            _commands.DrainScheduled(checked((int)executingTick));
            FlushAdmissionResults();
            FlushPendingAdmissions();
        }

        public void AfterAuthoritativeCommit(uint committedTick)
        {
            EnsureOperational();
            if (committedTick == 0 || committedTick > FixedInputWireCodec.MaxSimulationTick)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationBuildRejected, detail: unchecked((int)committedTick));
            }

            // ACK CommittedThrough and replication snapshot Tick must publish the same commit.
            if (_fixedInput.TickState.IsExecuting ||
                (uint)_fixedInput.TickState.CommittedTick != committedTick)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationBuildRejected, detail: unchecked((int)committedTick));
            }

            if (committedTick < _lastCommittedTick)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationBuildRejected, detail: unchecked((int)committedTick));
            }

            _lastCommittedTick = committedTick;

            if (committedTick % (uint)_capacity.StatePublishIntervalTicks != 0)
            {
                QueueFixedInputAcknowledgements();
                FlushPendingFixedInputAcknowledgements();
                FlushOutbound();
                return;
            }

            bool anyConnected = false;
            for (int i = 0; i < _seatStates.Length; i++)
            {
                anyConnected |= _seatStates[i] == SeatConnected;
            }

            if (!anyConnected)
            {
                QueueFixedInputAcknowledgements();
                FlushPendingFixedInputAcknowledgements();
                FlushOutbound();
                return;
            }

            int publishSeatCount = 0;
            for (int seat = 0; seat < _seatStates.Length; seat++)
            {
                if (_seatStates[seat] != SeatConnected)
                {
                    continue;
                }

                if (!_seatNeedsFull[seat] &&
                    _seatAcknowledgedSnapshots[seat] == 0 &&
                    _seatLastSentSnapshots[seat] != 0)
                {
                    // Reliable full snapshot is still in flight; AOI selection is not needed yet.
                    continue;
                }

                _publishSeats[publishSeatCount++] = GetSeatBinding(seat);
            }

            if (publishSeatCount == 0)
            {
                QueueFixedInputAcknowledgements();
                FlushPendingFixedInputAcknowledgements();
                FlushOutbound();
                return;
            }

            ReadOnlySpan<SessionSeatBinding> publishSeats = _publishSeats.AsSpan(0, publishSeatCount);
            if (!_replicationInterest.TryPrepareBatch(publishSeats))
            {
                Fail(NetworkRuntimeFaultCode.ReplicationInputRejected, detail: publishSeatCount);
            }

            try
            {
                for (int publishIndex = 0; publishIndex < publishSeats.Length; publishIndex++)
                {
                    SessionSeatBinding binding = publishSeats[publishIndex];
                    if (!_replicationInterest.TryGetPreparedInterest(in binding, out ReadOnlySpan<NetworkEntityHandle> handles) ||
                        handles.Length > _capacity.ReplicationEntityCapacityPerSeat)
                    {
                        Fail(NetworkRuntimeFaultCode.ReplicationInputRejected, _seatConnections[binding.Slot], detail: handles.Length);
                    }

                    for (int i = 0; i < handles.Length; i++)
                    {
                        NetworkEntityHandle handle = handles[i];
                        if (!handle.IsValid ||
                            (uint)handle.Slot >= (uint)_capacity.GlobalEntityCapacity ||
                            (i > 0 && handle.Slot <= handles[i - 1].Slot))
                        {
                            Fail(NetworkRuntimeFaultCode.ReplicationInputRejected, _seatConnections[binding.Slot], detail: i);
                        }
                    }
                }

                _replicationInterest.CommitPreparedKnowledge();
                for (int publishIndex = 0; publishIndex < publishSeats.Length; publishIndex++)
                {
                    SessionSeatBinding binding = publishSeats[publishIndex];
                    if (!_replicationInterest.TryGetPreparedInterest(in binding, out ReadOnlySpan<NetworkEntityHandle> handles))
                    {
                        throw new InvalidOperationException(
                            $"Prepared replication interest for seat {binding.Slot}:{binding.Generation} changed after validation.");
                    }

                    BuildAndSendReplication(
                        binding.Slot,
                        committedTick,
                        handles);
                }

                QueueFixedInputAcknowledgements();
            }
            finally
            {
                _replicationInterest.CompletePreparedBatch();
            }

            FlushPendingFixedInputAcknowledgements();
            FlushOutbound();
        }

        public void PumpReplicatedClient(float frameDeltaTime)
        {
            EnsureOperational();
            throw new InvalidOperationException("The authoritative runtime cannot pump a replicated client.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Exception? releaseException = null;
            int failedSeat = -1;
            for (int seat = 0; seat < _replicationSeats.Length; seat++)
            {
                AuthoritativeReplicationSeatRuntime? runtime = _replicationSeats[seat];
                if (runtime == null)
                {
                    continue;
                }

                SessionSeatBinding binding = GetSeatBinding(seat);
                _replicationSeats[seat] = null;
                try
                {
                    if (!_replicationSeatFactory.TryRelease(in binding, runtime) && failedSeat < 0)
                    {
                        failedSeat = seat;
                    }
                }
                catch (Exception exception)
                {
                    if (failedSeat < 0)
                    {
                        failedSeat = seat;
                        releaseException = exception;
                    }
                }
            }

            NetworkTransportPortLifetime.DisposeOwned(
                _transportOwnership,
                _connectionEvents,
                _datagrams,
                _connectionControl);

            if (failedSeat >= 0)
            {
                Fail(
                    NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected,
                    detail: failedSeat,
                    innerException: releaseException);
            }
        }

        private void ProcessConnectionEvent(in ServerConnectionEvent connectionEvent)
        {
            if (connectionEvent.Kind == TransportConnectionEventKind.Connected)
            {
                if (FindTransportConnection(connectionEvent.ConnectionId.Value) >= 0)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.SessionContractViolation, connectionEvent.ConnectionId.Value);
                    return;
                }

                int empty = FindTransportConnection(0);
                if (empty < 0)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.ConnectionCapacityExceeded, connectionEvent.ConnectionId.Value);
                    return;
                }

                _transportConnections[empty] = connectionEvent.ConnectionId.Value;
                return;
            }

            int transportSlot = FindTransportConnection(connectionEvent.ConnectionId.Value);
            if (transportSlot < 0)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnknownConnection, connectionEvent.ConnectionId.Value);
                return;
            }

            _transportConnections[transportSlot] = 0;
            int seat = FindSeatByConnection(connectionEvent.ConnectionId.Value);
            if (seat < 0)
            {
                return;
            }

            SessionSeatBinding binding = GetSeatBinding(seat);
            NetworkCommandSeat commandSeat = ToCommandSeat(in binding);
            if (!_sessions.TryDisconnect(connectionEvent.ConnectionId, _currentTick) ||
                !_commands.UnbindSeat(in commandSeat))
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, connectionEvent.ConnectionId.Value, detail: seat);
            }

            _seatStates[seat] = SeatAwaitingReconnect;
            _seatConnections[seat] = 0;
            _seatDisconnectTicks[seat] = _currentTick;
            _commandReassemblers[seat].Reset();
            ClearPendingFixedInputAcknowledgement(seat);
            _observer.OnServerSeatDisconnected(in binding, connectionEvent.DisconnectReason);
        }

        private void ProcessDatagram(ConnectionId connection, ChannelId channel, ReadOnlySpan<byte> datagram)
        {
            if (FindTransportConnection(connection.Value) < 0)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnknownConnection, connection.Value);
                return;
            }

            NetworkWireCodecStatus envelopeStatus = NetworkWireEnvelopeCodec.TryDecode(
                datagram,
                out NetworkWireEnvelope envelope,
                out ReadOnlySpan<byte> payload);
            if (envelopeStatus != NetworkWireCodecStatus.Success)
            {
                ProtocolFault(NetworkRuntimeFaultCode.MalformedDatagram, connection.Value, codecStatus: envelopeStatus);
                return;
            }

            ChannelId expected = GetExpectedClientChannel(envelope.Kind);
            if (channel != expected)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnexpectedChannel, connection.Value, envelope.Kind, detail: channel.Value);
                return;
            }

            switch (envelope.Kind)
            {
                case NetworkWireKind.SessionHandshakeRequest:
                    ProcessHandshake(connection, payload);
                    return;
                case NetworkWireKind.CommandFragment:
                    ProcessCommandFragment(connection, payload);
                    return;
                case NetworkWireKind.SnapshotAcknowledgement:
                    ProcessAcknowledgement(connection, payload);
                    return;
                case NetworkWireKind.ResyncRequired:
                    ProcessClientResyncRequest(connection, payload);
                    return;
                case NetworkWireKind.FixedInputBatch:
                    ProcessFixedInputBatch(connection, payload);
                    return;
                default:
                    ProtocolFault(NetworkRuntimeFaultCode.UnexpectedWireKind, connection.Value, envelope.Kind);
                    return;
            }
        }

        private void ProcessHandshake(ConnectionId connection, ReadOnlySpan<byte> payload)
        {
            if (FindSeatByConnection(connection.Value) >= 0)
            {
                ProtocolFault(NetworkRuntimeFaultCode.SessionContractViolation, connection.Value, NetworkWireKind.SessionHandshakeRequest);
                _connectionControl.DisconnectAfterReliableFlush(connection);
                return;
            }

            NetworkWireCodecStatus decoded = HandshakeWireCodec.TryDecodeRequest(payload, out SessionHandshakeRequest request);
            SessionHandshakeResponse response;
            if (decoded != NetworkWireCodecStatus.Success)
            {
                ProtocolFault(NetworkRuntimeFaultCode.MalformedDatagram, connection.Value, NetworkWireKind.SessionHandshakeRequest, decoded);
                response = SessionHandshakeResponse.Reject(
                    HandshakeRejectReason.MalformedRequest,
                    _sessions.RequiredProtocolVersion,
                    _sessions.RequiredContentFingerprint,
                    _sessions.SessionEpoch);
            }
            else
            {
                _sessions.TryHandshake(connection, in request, _currentTick, out response);
            }

            if (response.Accepted)
            {
                SessionSeatBinding acceptedSeat = response.Seat;
                BindAcceptedSeat(connection, in acceptedSeat);
            }

            SendHandshakeResponse(connection, in response);
            if (!response.Accepted)
            {
                _connectionControl.DisconnectAfterReliableFlush(connection);
            }
        }

        private void BindAcceptedSeat(ConnectionId connection, in SessionSeatBinding binding)
        {
            int seat = binding.Slot;
            Arch.Core.Entity controller = default;
            if ((uint)seat >= (uint)_seatStates.Length ||
                !_controllers.TryResolveController(in binding, out controller) ||
                controller == Arch.Core.Entity.Null)
            {
                Fail(NetworkRuntimeFaultCode.SeatControllerUnavailable, connection.Value, detail: seat);
            }

            bool reconnect = _seatStates[seat] == SeatAwaitingReconnect &&
                _seatGenerations[seat] == binding.Generation &&
                _seatPlayerIds[seat] == binding.PlayerId.Value;
            NetworkCommandSeat commandSeat = ToCommandSeat(in binding);
            if (reconnect)
            {
                if (_replicationSeats[seat] == null || _seatControllers[seat] != controller)
                {
                    Fail(NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected, connection.Value, detail: seat);
                }

                try
                {
                    _commands.RebindSeat(in commandSeat, controller, checked((int)_currentTick));
                    _fixedInput.RebindSeat(in binding);
                }
                catch (Exception exception)
                {
                    Fail(
                        NetworkRuntimeFaultCode.SessionContractViolation,
                        connection.Value,
                        detail: seat,
                        innerException: exception);
                }
            }
            else
            {
                if (_seatStates[seat] != SeatEmpty || _replicationSeats[seat] != null)
                {
                    Fail(NetworkRuntimeFaultCode.SessionContractViolation, connection.Value, detail: seat);
                }

                AuthoritativeReplicationSeatRuntime runtime = AcquireReplicationRuntime(
                    connection,
                    in binding,
                    controller);
                bool commandBound = false;
                try
                {
                    _commands.BindSeat(in commandSeat, controller, checked((int)_currentTick));
                    commandBound = true;
                    _fixedInput.BindSeat(in binding);
                }
                catch (Exception exception)
                {
                    bool commandReleased = !commandBound || _commands.TryReleaseSeat(in commandSeat);
                    bool runtimeReleased = TryReleaseAcquiredRuntime(in binding, runtime, out Exception? releaseException);
                    Fail(
                        NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected,
                        connection.Value,
                        detail: commandReleased && runtimeReleased ? seat : ~seat,
                        innerException: releaseException ?? exception);
                }

                _replicationSeats[seat] = runtime;
            }

            _seatStates[seat] = SeatConnected;
            _seatConnections[seat] = connection.Value;
            _seatGenerations[seat] = binding.Generation;
            _seatPlayerIds[seat] = binding.PlayerId.Value;
            _seatControllers[seat] = controller;
            _seatDisconnectTicks[seat] = 0;
            PrepareFullSnapshot(seat);
            _commandReassemblers[seat].Reset();
            ClearPendingFixedInputAcknowledgement(seat);
            _observer.OnServerSeatConnected(in binding, reconnect);
        }

        private void ProcessCommandFragment(ConnectionId connection, ReadOnlySpan<byte> payload)
        {
            int seat = FindSeatByConnection(connection.Value);
            if (seat < 0 || _seatStates[seat] != SeatConnected)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnauthenticatedMessage, connection.Value, NetworkWireKind.CommandFragment);
                return;
            }

            CommandFragmentReassembler assembler = _commandReassemblers[seat];
            CommandReassemblyStatus accepted = assembler.TryAcceptWirePayload(payload);
            if (accepted == CommandReassemblyStatus.Incomplete)
            {
                return;
            }

            if (accepted != CommandReassemblyStatus.Completed)
            {
                ProtocolFault(NetworkRuntimeFaultCode.CommandReassemblyRejected, connection.Value, NetworkWireKind.CommandFragment, detail: (int)accepted);
                return;
            }

            ulong fragmentEpoch = assembler.SessionEpoch;
            ulong fragmentSequence = assembler.ClientBatchSequence;
            NetworkWireCodecStatus decoded = CommandBatchWireCodec.TryDecode(
                assembler.AssembledPayload,
                _commandEntries,
                out NetworkCommandBatchHeader header,
                out int entryCount);
            assembler.Reset();

            if (decoded != NetworkWireCodecStatus.Success ||
                header.SessionEpoch != _sessions.SessionEpoch.Value ||
                header.SessionEpoch != fragmentEpoch ||
                header.ClientBatchSequence != fragmentSequence)
            {
                ProtocolFault(NetworkRuntimeFaultCode.CommandBatchRejected, connection.Value, NetworkWireKind.CommandFragment, decoded);
                return;
            }

            SessionSeatBinding binding = GetSeatBinding(seat);
            NetworkCommandSeat commandSeat = ToCommandSeat(in binding);
            _commands.Schedule(
                in commandSeat,
                in header,
                checked((int)_currentTick),
                _commandEntries.AsSpan(0, entryCount));
        }

        private void ProcessFixedInputBatch(ConnectionId connection, ReadOnlySpan<byte> payload)
        {
            int seat = FindSeatByConnection(connection.Value);
            if (seat < 0 || _seatStates[seat] != SeatConnected)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnauthenticatedMessage, connection.Value, NetworkWireKind.FixedInputBatch);
                return;
            }

            NetworkWireCodecStatus decoded = FixedInputWireCodec.TryDecodeBatch(
                payload,
                _fixedInputTargetTicks,
                _fixedInputPayloads,
                out NetworkFixedInputBatchHeader header,
                out int frameCount);
            if (decoded != NetworkWireCodecStatus.Success)
            {
                ProtocolFault(
                    NetworkRuntimeFaultCode.FixedInputRejected,
                    connection.Value,
                    NetworkWireKind.FixedInputBatch,
                    decoded);
                return;
            }

            // Authority is derived exclusively from the authenticated connection seat binding.
            SessionSeatBinding binding = GetSeatBinding(seat);
            ReadOnlySpan<byte> payloads = _fixedInputPayloads.AsSpan(
                0,
                checked(frameCount * _capacity.FixedInputFramePayloadBytes));
            FixedInputBatchAdmissionStatus admitted = _fixedInput.TryAdmitBatch(
                in binding,
                in header,
                _fixedInputTargetTicks.AsSpan(0, frameCount),
                payloads,
                _fixedInputDispositions.AsSpan(0, frameCount));
            if (admitted != FixedInputBatchAdmissionStatus.Success)
            {
                ProtocolFault(
                    NetworkRuntimeFaultCode.FixedInputRejected,
                    connection.Value,
                    NetworkWireKind.FixedInputBatch,
                    detail: (int)_fixedInputDispositions[0]);
            }
        }

        private void ProcessAcknowledgement(ConnectionId connection, ReadOnlySpan<byte> payload)
        {
            int seat = FindSeatByConnection(connection.Value);
            if (seat < 0)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnauthenticatedMessage, connection.Value, NetworkWireKind.SnapshotAcknowledgement);
                return;
            }

            NetworkWireCodecStatus decoded = SnapshotControlWireCodec.TryDecodeAcknowledgement(payload, out NetworkSnapshotAcknowledgement ack);
            if (decoded != NetworkWireCodecStatus.Success ||
                ack.SessionEpoch != _sessions.SessionEpoch.Value ||
                ack.SnapshotId == 0 ||
                ack.SnapshotId > _seatLastSentSnapshots[seat] ||
                ack.SnapshotId < _seatAcknowledgedSnapshots[seat] ||
                ack.CommittedTick > _lastCommittedTick ||
                !TryFindAcknowledgementHistory(seat, ack.SnapshotId, out ulong disclosureSequence))
            {
                ProtocolFault(NetworkRuntimeFaultCode.InvalidAcknowledgement, connection.Value, NetworkWireKind.SnapshotAcknowledgement, decoded);
                return;
            }

            _seatAcknowledgedSnapshots[seat] = ack.SnapshotId;
            if (disclosureSequence != 0)
            {
                GetReplicationRuntime(seat).Channel.TryAcknowledgeDisclosureChangesThrough(disclosureSequence);
            }
        }

        private void ProcessClientResyncRequest(ConnectionId connection, ReadOnlySpan<byte> payload)
        {
            int seat = FindSeatByConnection(connection.Value);
            NetworkWireCodecStatus decoded = SnapshotControlWireCodec.TryDecodeResyncRequired(payload, out NetworkResyncRequired request);
            if (seat < 0 || decoded != NetworkWireCodecStatus.Success || request.SessionEpoch != _sessions.SessionEpoch.Value)
            {
                ProtocolFault(NetworkRuntimeFaultCode.InvalidAcknowledgement, connection.Value, NetworkWireKind.ResyncRequired, decoded);
                return;
            }

            PrepareFullSnapshot(seat);
        }

        private void BuildAndSendReplication(int seat, uint committedTick, ReadOnlySpan<NetworkEntityHandle> interestHandles)
        {
            AuthoritativeReplicationSeatRuntime runtime = GetReplicationRuntime(seat);
            ulong snapshotId = checked(++_nextSnapshotId);
            ReplicationBridgeResult built;
            if (_seatNeedsFull[seat] || _seatAcknowledgedSnapshots[seat] == 0)
            {
                built = runtime.Bridge.BuildFull(
                    runtime.Channel,
                    _sessions.SessionEpoch.Value,
                    committedTick,
                    snapshotId,
                    interestHandles,
                    runtime.Projection,
                    runtime.Packet);
            }
            else
            {
                built = runtime.Bridge.BuildDelta(
                    runtime.Channel,
                    _sessions.SessionEpoch.Value,
                    committedTick,
                    snapshotId,
                    _seatAcknowledgedSnapshots[seat],
                    interestHandles,
                    runtime.Projection,
                    runtime.Packet);
                if (built == ReplicationBridgeResult.ResyncRequired)
                {
                    SendResyncRequired(seat, NetworkResyncReason.BaselineUnavailable);
                    PrepareFullSnapshot(seat);
                    built = runtime.Bridge.BuildFull(
                        runtime.Channel,
                        _sessions.SessionEpoch.Value,
                        committedTick,
                        snapshotId,
                        interestHandles,
                        runtime.Projection,
                        runtime.Packet);
                }
            }

            if (built != ReplicationBridgeResult.Success)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationBuildRejected, _seatConnections[seat], detail: (int)built);
            }

            NetworkWireCodecStatus encoded = ReplicationPacketWireCodec.TryEncode(runtime.Packet, _snapshotBuffer, out int snapshotBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationEncodeRejected, _seatConnections[seat], codecStatus: encoded);
            }

            bool full = runtime.Packet.Header.Kind == ReplicationPacketKind.Full;
            int framedLength = NetworkWireEnvelope.SizeInBytes + snapshotBytes;
            if (full || framedLength > _capacity.MaxDatagramPayloadBytes)
            {
                SendSnapshotFragments(seat, snapshotId, _snapshotBuffer.AsSpan(0, snapshotBytes));
            }
            else
            {
                SendFramed(
                    new ConnectionId(_seatConnections[seat]),
                    _capacity.StateChannel,
                    NetworkWireKind.ReplicationPacket,
                    _snapshotBuffer.AsSpan(0, snapshotBytes));
            }

            ulong disclosureSequence = _seatLastDisclosureSequences[seat];
            ReadOnlySpan<ReplicationDisclosureChange> disclosures = runtime.Packet.DisclosureChanges;
            for (int i = 0; i < disclosures.Length; i++)
            {
                disclosureSequence = Math.Max(disclosureSequence, disclosures[i].Sequence);
            }

            _seatLastDisclosureSequences[seat] = disclosureSequence;
            _seatLastSentSnapshots[seat] = snapshotId;
            _seatNeedsFull[seat] = false;
            RecordAcknowledgementHistory(seat, snapshotId, disclosureSequence);
        }

        private void PrepareFullSnapshot(int seat)
        {
            _seatNeedsFull[seat] = true;
            _seatAcknowledgedSnapshots[seat] = 0;
            ulong disclosureSequence = _seatLastDisclosureSequences[seat];
            if (disclosureSequence != 0)
            {
                GetReplicationRuntime(seat).Channel.TryAcknowledgeDisclosureChangesThrough(disclosureSequence);
                _seatLastDisclosureSequences[seat] = 0;
            }

            ClearAcknowledgementHistory(seat);
        }

        private void SendSnapshotFragments(int seat, ulong snapshotId, ReadOnlySpan<byte> snapshot)
        {
            NetworkWireCodecStatus countStatus = _snapshotEncoder.TryGetFragmentCount(snapshot.Length, out ushort fragmentCount);
            if (countStatus != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationEncodeRejected, _seatConnections[seat], codecStatus: countStatus);
            }

            ConnectionId connection = new(_seatConnections[seat]);
            for (ushort fragment = 0; fragment < fragmentCount; fragment++)
            {
                NetworkWireCodecStatus encoded = _snapshotEncoder.TryEncodeFragment(
                    _sessions.SessionEpoch.Value,
                    snapshotId,
                    snapshot,
                    fragment,
                    fragmentCount,
                    _payloadBuffer,
                    out int payloadBytes);
                if (encoded != NetworkWireCodecStatus.Success)
                {
                    Fail(NetworkRuntimeFaultCode.ReplicationEncodeRejected, connection.Value, codecStatus: encoded);
                }

                SendFramed(
                    connection,
                    _capacity.ControlChannel,
                    NetworkWireKind.SnapshotFragment,
                    _payloadBuffer.AsSpan(0, payloadBytes));
            }
        }

        private void SendHandshakeResponse(ConnectionId connection, in SessionHandshakeResponse response)
        {
            NetworkWireCodecStatus encoded = HandshakeWireCodec.TryEncodeResponse(
                in response,
                _payloadBuffer,
                out int payloadBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, connection.Value, codecStatus: encoded);
            }

            SendFramed(connection, _capacity.ControlChannel, NetworkWireKind.SessionHandshakeResponse, _payloadBuffer.AsSpan(0, payloadBytes));
        }

        private void SendResyncRequired(int seat, NetworkResyncReason reason)
        {
            var message = new NetworkResyncRequired(
                _sessions.SessionEpoch.Value,
                reason,
                _lastCommittedTick,
                _seatLastSentSnapshots[seat]);
            NetworkWireCodecStatus encoded = SnapshotControlWireCodec.TryEncodeResyncRequired(
                in message,
                _payloadBuffer,
                out int payloadBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationEncodeRejected, _seatConnections[seat], codecStatus: encoded);
            }

            SendFramed(
                new ConnectionId(_seatConnections[seat]),
                _capacity.ControlChannel,
                NetworkWireKind.ResyncRequired,
                _payloadBuffer.AsSpan(0, payloadBytes));
        }

        private void FlushAdmissionResults()
        {
            while (_commandResults.TryRead(out NetworkCommandAdmissionOutcome outcome))
            {
                int free = -1;
                for (int i = 0; i < _pendingAdmissionActive.Length; i++)
                {
                    if (!_pendingAdmissionActive[i])
                    {
                        free = i;
                        break;
                    }
                }

                if (free < 0)
                {
                    Fail(NetworkRuntimeFaultCode.AdmissionResultCapacityExceeded, detail: _commandResults.Count);
                }

                _pendingAdmissions[free] = outcome;
                _pendingAdmissionActive[free] = true;
            }
        }

        private void FlushPendingAdmissions()
        {
            for (int i = 0; i < _pendingAdmissionActive.Length; i++)
            {
                if (!_pendingAdmissionActive[i])
                {
                    continue;
                }

                NetworkCommandAdmissionOutcome outcome = _pendingAdmissions[i];
                int seat = outcome.SeatSlot;
                if ((uint)seat >= (uint)_seatStates.Length ||
                    _seatGenerations[seat] != outcome.SeatGeneration)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.AdmissionResultUndeliverable, detail: seat);
                    _pendingAdmissionActive[i] = false;
                    continue;
                }

                if (_seatStates[seat] == SeatAwaitingReconnect)
                {
                    continue;
                }

                if (_seatStates[seat] != SeatConnected)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.AdmissionResultUndeliverable, detail: seat);
                    _pendingAdmissionActive[i] = false;
                    continue;
                }

                NetworkWireCodecStatus encoded = CommandAdmissionWireCodec.TryEncode(
                    _sessions.SessionEpoch.Value,
                    in outcome,
                    _payloadBuffer,
                    out int payloadBytes);
                if (encoded != NetworkWireCodecStatus.Success)
                {
                    Fail(NetworkRuntimeFaultCode.CommandBatchRejected, _seatConnections[seat], codecStatus: encoded);
                }

                SendFramed(
                    new ConnectionId(_seatConnections[seat]),
                    _capacity.CommandChannel,
                    NetworkWireKind.CommandAdmissionResult,
                    _payloadBuffer.AsSpan(0, payloadBytes));
                _pendingAdmissionActive[i] = false;
            }
        }

        private void ExpireDisconnectedSeats(uint currentTick)
        {
            if (!_sessions.TryExpireAwaitingSeats(currentTick, _expiredSeats, out int expiredCount))
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: _expiredSeats.Length);
            }

            for (int i = 0; i < expiredCount; i++)
            {
                SessionSeatBinding binding = _expiredSeats[i];
                int seat = binding.Slot;
                if ((uint)seat >= (uint)_seatStates.Length ||
                    _seatStates[seat] != SeatAwaitingReconnect ||
                    _seatGenerations[seat] != binding.Generation ||
                    _seatPlayerIds[seat] != binding.PlayerId.Value)
                {
                    Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: seat);
                }

                NetworkCommandSeat commandSeat = ToCommandSeat(in binding);
                if (!_commands.TryReleaseSeat(in commandSeat))
                {
                    Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: seat);
                }

                if (!_fixedInput.TryReleaseSeat(in binding))
                {
                    Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: seat);
                }

                AuthoritativeReplicationSeatRuntime? runtime = _replicationSeats[seat];
                if (runtime == null)
                {
                    Fail(NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected, detail: seat);
                }

                _replicationSeats[seat] = null;
                try
                {
                    if (!_replicationSeatFactory.TryRelease(in binding, runtime))
                    {
                        Fail(NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected, detail: seat);
                    }
                }
                catch (NetworkRuntimeException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Fail(
                        NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected,
                        detail: seat,
                        innerException: exception);
                }

                _seatStates[seat] = SeatEmpty;
                _seatConnections[seat] = 0;
                _seatGenerations[seat] = 0;
                _seatPlayerIds[seat] = 0;
                _seatControllers[seat] = default;
                _seatDisconnectTicks[seat] = 0;
                _seatNeedsFull[seat] = false;
                _seatAcknowledgedSnapshots[seat] = 0;
                _seatLastSentSnapshots[seat] = 0;
                _seatLastDisclosureSequences[seat] = 0;
                ClearAcknowledgementHistory(seat);
                ClearPendingFixedInputAcknowledgement(seat);
                _commandReassemblers[seat].Reset();
                _observer.OnServerSeatReleased(in binding);
            }
        }

        private void SendFramed(
            ConnectionId connection,
            ChannelId channel,
            NetworkWireKind kind,
            ReadOnlySpan<byte> payload)
        {
            NetworkWireCodecStatus framed = NetworkWireEnvelopeCodec.TryEncode(kind, payload, _datagramBuffer, out int bytes);
            if (framed != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationEncodeRejected, connection.Value, kind, framed);
            }

            SendOrQueue(connection, channel, _datagramBuffer.AsSpan(0, bytes));
        }

        private void SendOrQueue(ConnectionId connection, ChannelId channel, ReadOnlySpan<byte> datagram)
        {
            if (_outbound.Count == 0)
            {
                DatagramSendStatus sent = _datagrams.TrySend(connection, channel, datagram);
                if (sent == DatagramSendStatus.Sent)
                {
                    return;
                }

                if (sent == DatagramSendStatus.Closed)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.TransportClosed, connection.Value);
                    return;
                }
            }

            if (!_outbound.TryEnqueue(connection, channel, datagram))
            {
                Fail(NetworkRuntimeFaultCode.OutboundQueueCapacityExceeded, connection.Value, detail: _outbound.Count);
            }
        }

        private void FlushOutbound()
        {
            while (_outbound.TryPeek(out ConnectionId connection, out ChannelId channel, out ReadOnlySpan<byte> payload))
            {
                DatagramSendStatus sent = _datagrams.TrySend(connection, channel, payload);
                if (sent == DatagramSendStatus.NotReady)
                {
                    return;
                }

                _outbound.RemoveHead();
                if (sent == DatagramSendStatus.Closed)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.TransportClosed, connection.Value);
                }
            }
        }

        private void RecordAcknowledgementHistory(int seat, ulong snapshotId, ulong disclosureSequence)
        {
            int write = _ackHistoryWriteIndices[seat];
            int index = (seat * _capacity.AcknowledgementHistoryCapacity) + write;
            _ackHistorySnapshotIds[index] = snapshotId;
            _ackHistoryDisclosureSequences[index] = disclosureSequence;
            _ackHistoryWriteIndices[seat] = (write + 1) % _capacity.AcknowledgementHistoryCapacity;
        }

        private bool TryFindAcknowledgementHistory(int seat, ulong snapshotId, out ulong disclosureSequence)
        {
            int offset = seat * _capacity.AcknowledgementHistoryCapacity;
            for (int i = 0; i < _capacity.AcknowledgementHistoryCapacity; i++)
            {
                if (_ackHistorySnapshotIds[offset + i] == snapshotId)
                {
                    disclosureSequence = _ackHistoryDisclosureSequences[offset + i];
                    return true;
                }
            }

            disclosureSequence = 0;
            return false;
        }

        private void ClearAcknowledgementHistory(int seat)
        {
            int offset = seat * _capacity.AcknowledgementHistoryCapacity;
            Array.Clear(_ackHistorySnapshotIds, offset, _capacity.AcknowledgementHistoryCapacity);
            Array.Clear(_ackHistoryDisclosureSequences, offset, _capacity.AcknowledgementHistoryCapacity);
            _ackHistoryWriteIndices[seat] = 0;
        }

        private ChannelId GetExpectedClientChannel(NetworkWireKind kind)
        {
            return kind switch
            {
                NetworkWireKind.CommandFragment => _capacity.CommandChannel,
                NetworkWireKind.FixedInputBatch => _capacity.InputChannel,
                NetworkWireKind.SessionHandshakeRequest or
                NetworkWireKind.SnapshotAcknowledgement or
                NetworkWireKind.ResyncRequired => _capacity.ControlChannel,
                _ => default,
            };
        }

        private void QueueFixedInputAcknowledgements()
        {
            for (int seat = 0; seat < _seatStates.Length; seat++)
            {
                if (_seatStates[seat] != SeatConnected)
                {
                    continue;
                }

                SessionSeatBinding binding = GetSeatBinding(seat);
                NetworkFixedInputAcknowledgement acknowledgement = _fixedInput.BuildAcknowledgement(in binding);
                Span<byte> destination = _pendingFixedInputAckPayload.AsSpan(
                    seat * NetworkFixedInputAcknowledgement.SizeInBytes,
                    NetworkFixedInputAcknowledgement.SizeInBytes);
                NetworkWireCodecStatus encoded = FixedInputWireCodec.TryEncodeAcknowledgement(
                    in acknowledgement,
                    destination,
                    out int payloadBytes);
                if (encoded != NetworkWireCodecStatus.Success)
                {
                    Fail(
                        NetworkRuntimeFaultCode.FixedInputRejected,
                        _seatConnections[seat],
                        NetworkWireKind.FixedInputAcknowledgement,
                        encoded);
                }

                // Latest-per-seat overwrite: never accumulate stale ACK payloads in FIFO order.
                _pendingFixedInputAckBytes[seat] = payloadBytes;
                _pendingFixedInputAck[seat] = true;
            }
        }

        private void FlushPendingFixedInputAcknowledgements()
        {
            for (int seat = 0; seat < _pendingFixedInputAck.Length; seat++)
            {
                if (!_pendingFixedInputAck[seat] || _seatStates[seat] != SeatConnected)
                {
                    continue;
                }

                ReadOnlySpan<byte> payload = _pendingFixedInputAckPayload.AsSpan(
                    seat * NetworkFixedInputAcknowledgement.SizeInBytes,
                    _pendingFixedInputAckBytes[seat]);
                NetworkWireCodecStatus framed = NetworkWireEnvelopeCodec.TryEncode(
                    NetworkWireKind.FixedInputAcknowledgement,
                    payload,
                    _datagramBuffer,
                    out int bytes);
                if (framed != NetworkWireCodecStatus.Success)
                {
                    Fail(
                        NetworkRuntimeFaultCode.FixedInputRejected,
                        _seatConnections[seat],
                        NetworkWireKind.FixedInputAcknowledgement,
                        framed);
                }

                DatagramSendStatus sent = _datagrams.TrySend(
                    new ConnectionId(_seatConnections[seat]),
                    _capacity.InputChannel,
                    _datagramBuffer.AsSpan(0, bytes));
                if (sent == DatagramSendStatus.Sent)
                {
                    _pendingFixedInputAck[seat] = false;
                    _pendingFixedInputAckBytes[seat] = 0;
                    continue;
                }

                if (sent == DatagramSendStatus.Closed)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.TransportClosed, _seatConnections[seat]);
                    ClearPendingFixedInputAcknowledgement(seat);
                    continue;
                }

                // NotReady: retain the latest pending ACK for this seat only.
            }
        }

        private void ClearPendingFixedInputAcknowledgement(int seat)
        {
            _pendingFixedInputAck[seat] = false;
            _pendingFixedInputAckBytes[seat] = 0;
            _pendingFixedInputAckPayload.AsSpan(
                seat * NetworkFixedInputAcknowledgement.SizeInBytes,
                NetworkFixedInputAcknowledgement.SizeInBytes).Clear();
        }

        private AuthoritativeReplicationSeatRuntime AcquireReplicationRuntime(
            ConnectionId connection,
            in SessionSeatBinding binding,
            Arch.Core.Entity viewer)
        {
            AuthoritativeReplicationSeatRuntime? runtime = null;
            bool acquired;
            try
            {
                acquired = _replicationSeatFactory.TryAcquire(in binding, viewer, out runtime);
            }
            catch (Exception exception)
            {
                Fail(
                    NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected,
                    connection.Value,
                    detail: binding.Slot,
                    innerException: exception);
                throw;
            }

            if (!acquired || runtime == null)
            {
                Exception? releaseException = null;
                bool released = runtime == null ||
                    TryReleaseAcquiredRuntime(in binding, runtime, out releaseException);
                Fail(
                    NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected,
                    connection.Value,
                    detail: released ? binding.Slot : ~binding.Slot,
                    innerException: releaseException);
            }

            bool valid = runtime.Bridge.GlobalEntityCapacity == _capacity.GlobalEntityCapacity &&
                runtime.Bridge.ReplicationEntityCapacityPerSeat == _capacity.ReplicationEntityCapacityPerSeat &&
                runtime.Channel.IsPristine;
            for (int i = 0; valid && i < _replicationSeats.Length; i++)
            {
                valid = !ReferenceEquals(runtime, _replicationSeats[i]);
            }

            if (!valid)
            {
                bool released = TryReleaseAcquiredRuntime(in binding, runtime, out Exception? releaseException);
                Fail(
                    NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected,
                    connection.Value,
                    detail: released ? binding.Slot : ~binding.Slot,
                    innerException: releaseException);
            }

            return runtime;
        }

        private bool TryReleaseAcquiredRuntime(
            in SessionSeatBinding binding,
            AuthoritativeReplicationSeatRuntime runtime,
            out Exception? exception)
        {
            try
            {
                exception = null;
                return _replicationSeatFactory.TryRelease(in binding, runtime);
            }
            catch (Exception caught)
            {
                exception = caught;
                return false;
            }
        }

        private AuthoritativeReplicationSeatRuntime GetReplicationRuntime(int seat)
        {
            AuthoritativeReplicationSeatRuntime? runtime = _replicationSeats[seat];
            if (runtime == null)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationSeatRuntimeRejected, detail: seat);
            }

            return runtime;
        }

        private static void ValidateFixedInputIngress(
            in NetworkRuntimeCapacity capacity,
            AuthoritativeSessionRegistry sessions,
            AuthoritativeFixedInputIngress fixedInput)
        {
            FixedInputProtocolConfig expected = capacity.CreateFixedInputProtocolConfig(
                sessions.SessionEpoch.Value,
                sessions.SeatCapacity);
            FixedInputProtocolConfig actual = fixedInput.Config;
            if (actual.SeatCapacity != expected.SeatCapacity ||
                actual.SeatCapacity != sessions.SeatCapacity ||
                actual.HistoryTicksPerSeat != expected.HistoryTicksPerSeat ||
                actual.SchemaId != expected.SchemaId ||
                actual.FramePayloadBytes != expected.FramePayloadBytes ||
                actual.MaxFutureTicks != expected.MaxFutureTicks ||
                actual.MaxFramesPerBatch != expected.MaxFramesPerBatch ||
                actual.MaxDatagramPayloadBytes != expected.MaxDatagramPayloadBytes ||
                actual.SessionEpoch != expected.SessionEpoch)
            {
                throw new ArgumentException(
                    "Fixed-input ingress config must match NetworkRuntimeCapacity and the session epoch.",
                    nameof(fixedInput));
            }
        }

        private int FindTransportConnection(int value)
        {
            for (int i = 0; i < _transportConnections.Length; i++)
            {
                if (_transportConnections[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindSeatByConnection(int connectionValue)
        {
            for (int i = 0; i < _seatConnections.Length; i++)
            {
                if (_seatStates[i] == SeatConnected && _seatConnections[i] == connectionValue)
                {
                    return i;
                }
            }

            return -1;
        }

        private SessionSeatBinding GetSeatBinding(int seat) =>
            new(seat, _seatGenerations[seat], new PlayerId(_seatPlayerIds[seat]));

        private static NetworkCommandSeat ToCommandSeat(in SessionSeatBinding seat) =>
            new(seat.Slot, seat.Generation, seat.PlayerId.Value);

        private void ProtocolFault(
            NetworkRuntimeFaultCode code,
            int connectionValue = 0,
            NetworkWireKind wireKind = default,
            NetworkWireCodecStatus codecStatus = default,
            int detail = 0)
        {
            var fault = new NetworkRuntimeFault(
                NetworkRuntimeFaultSeverity.ProtocolViolation,
                code,
                connectionValue,
                wireKind,
                codecStatus,
                detail);
            _observer.OnFault(in fault);
        }

        [DoesNotReturn]
        private void Fail(
            NetworkRuntimeFaultCode code,
            int connectionValue = 0,
            NetworkWireKind wireKind = default,
            NetworkWireCodecStatus codecStatus = default,
            int detail = 0,
            Exception? innerException = null)
        {
            _lastFault = new NetworkRuntimeFault(
                NetworkRuntimeFaultSeverity.LocalContractViolation,
                code,
                connectionValue,
                wireKind,
                codecStatus,
                detail);
            _faulted = true;
            _observer.OnFault(in _lastFault);
            if (innerException == null)
            {
                throw new NetworkRuntimeException(in _lastFault);
            }

            throw new NetworkRuntimeException(in _lastFault, innerException);
        }

        private void EnsureOperational()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_faulted)
            {
                throw new NetworkRuntimeException(in _lastFault);
            }
        }
    }
}
