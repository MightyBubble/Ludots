using System;
using System.Diagnostics.CodeAnalysis;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Networking.Commands;
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
        private readonly IServerConnectionEventPort _connectionEvents;
        private readonly IServerDatagramPort _datagrams;
        private readonly IServerConnectionControlPort _connectionControl;
        private readonly AuthoritativeSessionRegistry _sessions;
        private readonly NetworkCommandIngress _commands;
        private readonly NetworkGameplayCommandGate _gameplayCommandGate;
        private readonly NetworkCommandAdmissionResultBuffer _commandResults;
        private readonly OrderAdmissionResultBuffer _orderAdmissionResults;
        private readonly OrderTerminalResultBuffer _terminalResults;
        private readonly IAuthoritativeSeatControllerResolver _controllers;
        private readonly IAuthoritativeReplicationInputPort _replicationInput;
        private readonly AuthoritativeReplicationSeatRuntime[] _replicationSeats;
        private readonly INetworkRuntimeObserver _observer;
        private readonly SnapshotFragmentEncoder _snapshotEncoder;
        private readonly FixedServerDatagramSendQueue _outbound;

        private readonly int[] _transportConnections;
        private readonly bool[] _disconnectAfterFlush;
        private readonly byte[] _seatStates;
        private readonly int[] _seatConnections;
        private readonly uint[] _seatGenerations;
        private readonly int[] _seatPlayerIds;
        private readonly uint[] _seatDisconnectTicks;
        private readonly bool[] _seatNeedsFull;
        private readonly ulong[] _seatAcknowledgedSnapshots;
        private readonly ulong[] _seatLastSentSnapshots;
        private readonly uint[] _seatSnapshotSentTicks;
        private readonly ulong[] _seatIgnoredAcknowledgementsThrough;
        private readonly ulong[] _seatLastDisclosureSequences;
        private readonly ulong[] _ackHistorySnapshotIds;
        private readonly ulong[] _ackHistoryDisclosureSequences;
        private readonly int[] _ackHistoryWriteIndices;
        private readonly CommandFragmentReassembler[] _commandReassemblers;
        private readonly NetworkCommandWireEntry[] _commandEntries;
        private readonly NetworkEntityHandle[] _activeHandles;
        private readonly SessionSeatBinding[] _expiredSeats;
        private readonly NetworkRoomSeatSnapshot[] _roomSeats;
        private readonly NetworkCommandAdmissionOutcome[] _pendingAdmissions;
        private readonly bool[] _pendingAdmissionActive;
        private uint _lastProcessedAdmissionGeneration;
        private int _processedAdmissionCount;
        private bool _hasProcessedAdmissionGeneration;
        private uint _lastProcessedTerminalGeneration;
        private int _processedTerminalCount;
        private bool _hasProcessedTerminalGeneration;

        private readonly byte[] _receiveBuffer;
        private readonly byte[] _payloadBuffer;
        private readonly byte[] _datagramBuffer;
        private readonly byte[] _snapshotBuffer;

        private uint _currentTick;
        private uint _lastCommittedTick;
        private bool _authoritativeTickOpen;
        private ulong _nextSnapshotId;
        private ulong _lastPublishedRoomRevision;
        private bool _disposed;
        private bool _faulted;
        private NetworkRuntimeFault _lastFault;

        public AuthoritativeServerNetworkRuntime(
            in NetworkRuntimeCapacity capacity,
            IServerConnectionEventPort connectionEvents,
            IServerDatagramPort datagrams,
            IServerConnectionControlPort connectionControl,
            AuthoritativeSessionRegistry sessions,
            NetworkCommandIngress commands,
            NetworkGameplayCommandGate gameplayCommandGate,
            NetworkCommandAdmissionResultBuffer commandResults,
            OrderAdmissionResultBuffer orderAdmissionResults,
            OrderTerminalResultBuffer terminalResults,
            IAuthoritativeSeatControllerResolver controllers,
            IAuthoritativeReplicationInputPort replicationInput,
            AuthoritativeReplicationSeatRuntime[] replicationSeats,
            INetworkRuntimeObserver observer)
        {
            _capacity = capacity;
            _connectionEvents = connectionEvents ?? throw new ArgumentNullException(nameof(connectionEvents));
            _datagrams = datagrams ?? throw new ArgumentNullException(nameof(datagrams));
            _connectionControl = connectionControl ?? throw new ArgumentNullException(nameof(connectionControl));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _gameplayCommandGate = gameplayCommandGate ?? throw new ArgumentNullException(nameof(gameplayCommandGate));
            _commandResults = commandResults ?? throw new ArgumentNullException(nameof(commandResults));
            _orderAdmissionResults = orderAdmissionResults ?? throw new ArgumentNullException(nameof(orderAdmissionResults));
            _terminalResults = terminalResults ?? throw new ArgumentNullException(nameof(terminalResults));
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            _replicationInput = replicationInput ?? throw new ArgumentNullException(nameof(replicationInput));
            _replicationSeats = replicationSeats ?? throw new ArgumentNullException(nameof(replicationSeats));
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));

            if (sessions.SeatCapacity != replicationSeats.Length || sessions.SeatCapacity > capacity.ConnectionCapacity)
            {
                throw new ArgumentException("Session, replication-seat, and connection capacities must agree.");
            }

            if (NetworkWireEnvelope.SizeInBytes + RoomControlWireCodec.GetSnapshotPayloadSize(sessions.SeatCapacity) >
                capacity.MaxDatagramPayloadBytes)
            {
                throw new ArgumentException(
                    "Configured datagram capacity cannot carry one complete authoritative room snapshot.",
                    nameof(capacity));
            }

            for (int i = 0; i < replicationSeats.Length; i++)
            {
                if (replicationSeats[i] == null ||
                    replicationSeats[i].SeatSlot != i ||
                    replicationSeats[i].PlayerId.Value != i + 1 ||
                    replicationSeats[i].Bridge.EntityCapacity != capacity.EntityCapacity)
                {
                    throw new ArgumentException(
                        "Every authoritative seat requires a slot-ordered, player-bound replication runtime with matching capacity.",
                        nameof(replicationSeats));
                }
            }

            _snapshotEncoder = new SnapshotFragmentEncoder(
                capacity.MaxDatagramPayloadBytes,
                capacity.MaxSnapshotBytes,
                capacity.MaxSnapshotFragments);
            _outbound = new FixedServerDatagramSendQueue(capacity.OutboundQueueCapacity, capacity.MaxDatagramPayloadBytes);

            _transportConnections = new int[capacity.ConnectionCapacity];
            _disconnectAfterFlush = new bool[capacity.ConnectionCapacity];
            int seats = sessions.SeatCapacity;
            _seatStates = new byte[seats];
            _seatConnections = new int[seats];
            _seatGenerations = new uint[seats];
            _seatPlayerIds = new int[seats];
            _seatDisconnectTicks = new uint[seats];
            _seatNeedsFull = new bool[seats];
            _seatAcknowledgedSnapshots = new ulong[seats];
            _seatLastSentSnapshots = new ulong[seats];
            _seatSnapshotSentTicks = new uint[seats];
            _seatIgnoredAcknowledgementsThrough = new ulong[seats];
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
            _activeHandles = new NetworkEntityHandle[capacity.EntityCapacity];
            _expiredSeats = new SessionSeatBinding[seats];
            _roomSeats = new NetworkRoomSeatSnapshot[seats];
            int pendingAdmissionCapacity = checked(
                (commandResults.Capacity + _orderAdmissionResults.GenerationCapacity) * sessions.SeatCapacity);
            _pendingAdmissions = new NetworkCommandAdmissionOutcome[pendingAdmissionCapacity];
            _pendingAdmissionActive = new bool[pendingAdmissionCapacity];
            _receiveBuffer = new byte[capacity.MaxDatagramPayloadBytes];
            _payloadBuffer = new byte[Math.Max(capacity.MaxDatagramPayloadBytes, HandshakeWireCodec.ResponseSizeInBytes)];
            _datagramBuffer = new byte[capacity.MaxDatagramPayloadBytes];
            _snapshotBuffer = new byte[capacity.MaxSnapshotBytes];
        }

        public NetworkProcessRole Role => NetworkProcessRole.AuthoritativeServer;
        public bool IsFaulted => _faulted;
        public NetworkRuntimeFault LastFault => _lastFault;

        public void Activate() => EnsureOperational();

        public void PumpTransport()
        {
            EnsureOperational();
            FlushOutbound();
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
            FlushRejectedConnections();
        }

        public void BeforeAuthoritativeTick(uint executingTick)
        {
            EnsureOperational();
            if (_authoritativeTickOpen ||
                executingTick <= _currentTick ||
                executingTick <= _lastCommittedTick)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: unchecked((int)executingTick));
            }

            _currentTick = executingTick;
            _authoritativeTickOpen = true;
            ExpireDisconnectedSeats(executingTick);
            PublishRoomSnapshotIfChanged();
            _commands.DrainScheduled(
                checked((int)executingTick),
                checked((int)_lastCommittedTick));
            FlushAdmissionResults();
            FlushPendingAdmissions();
        }

        public void AfterAuthoritativeCommit(uint committedTick)
        {
            EnsureOperational();
            if (!_authoritativeTickOpen ||
                committedTick != _currentTick ||
                committedTick <= _lastCommittedTick)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationBuildRejected, detail: unchecked((int)committedTick));
            }

            _lastCommittedTick = committedTick;
            _sessions.AdvanceRoomCountdown(committedTick);
            if (_sessions.RoomPhase == NetworkRoomPhase.Started)
            {
                _gameplayCommandGate.StartMatch();
            }
            PublishRoomSnapshotIfChanged();
            FlushEntityAdmissionResults();
            FlushTerminalResults();
            _authoritativeTickOpen = false;
            FlushPendingAdmissions();
            bool scheduledPublish = committedTick % (uint)_capacity.StatePublishIntervalTicks == 0;
            bool anyConnected = false;
            bool acknowledgementRecoveryRequired = false;
            for (int i = 0; i < _seatStates.Length; i++)
            {
                bool connected = _seatStates[i] == SeatConnected;
                anyConnected |= connected;
                acknowledgementRecoveryRequired |= connected && HasSnapshotAcknowledgementTimedOut(i, committedTick);
            }

            if (!anyConnected || (!scheduledPublish && !acknowledgementRecoveryRequired))
            {
                return;
            }

            if (!_replicationInput.TryCopyActiveHandles(_activeHandles, out int activeCount) ||
                (uint)activeCount > (uint)_activeHandles.Length)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationInputRejected, detail: activeCount);
            }

            ReadOnlySpan<NetworkEntityHandle> active = _activeHandles.AsSpan(0, activeCount);
            for (int seat = 0; seat < _seatStates.Length; seat++)
            {
                if (_seatStates[seat] != SeatConnected)
                {
                    continue;
                }

                bool recovering = TryBeginSnapshotAcknowledgementRecovery(seat, committedTick);
                if (scheduledPublish || recovering)
                {
                    BuildAndSendReplication(seat, committedTick, active);
                }
            }

            FlushOutbound();
        }

        public void PumpReplicatedClient(float frameDeltaTime)
        {
            EnsureOperational();
            throw new InvalidOperationException("The authoritative runtime cannot pump a replicated client.");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            object events = _connectionEvents;
            object datagrams = _datagrams;
            object control = _connectionControl;
            if (events is IDisposable disposableEvents)
            {
                disposableEvents.Dispose();
            }

            if (!ReferenceEquals(datagrams, events) && datagrams is IDisposable disposableDatagrams)
            {
                disposableDatagrams.Dispose();
            }

            if (!ReferenceEquals(control, events) &&
                !ReferenceEquals(control, datagrams) &&
                control is IDisposable disposableControl)
            {
                disposableControl.Dispose();
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
                if (_sessions.TryDisconnect(connectionEvent.ConnectionId, _currentTick))
                {
                    PublishRoomSnapshotIfChanged();
                }
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
            _observer.OnServerSeatDisconnected(in binding, connectionEvent.DisconnectReason);
            PublishRoomSnapshotIfChanged();
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
                case NetworkWireKind.SessionHandshakeConfirmation:
                    ProcessHandshakeConfirmation(connection, payload);
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
                case NetworkWireKind.RoomReadyIntent:
                    ProcessRoomReadyIntent(connection, payload);
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
                ulong nextClientBatchSequence = ResolvePreparedHandshakeCursor(in acceptedSeat);
                response = SessionHandshakeResponse.Accept(
                    in acceptedSeat,
                    response.ReconnectToken,
                    response.ProtocolVersion,
                    response.ContentFingerprint,
                    response.SessionEpoch,
                    nextClientBatchSequence);
            }

            SendHandshakeResponse(connection, in response);
            if (!response.Accepted)
            {
                int transportSlot = FindTransportConnection(connection.Value);
                if (transportSlot < 0)
                {
                    Fail(NetworkRuntimeFaultCode.UnknownConnection, connection.Value);
                }

                _disconnectAfterFlush[transportSlot] = true;
            }
        }

        private void ProcessHandshakeConfirmation(ConnectionId connection, ReadOnlySpan<byte> payload)
        {
            NetworkWireCodecStatus decoded = HandshakeWireCodec.TryDecodeConfirmation(
                payload,
                out SessionHandshakeConfirmation confirmation);
            if (decoded != NetworkWireCodecStatus.Success ||
                !_sessions.TryConfirmHandshake(
                    connection,
                    in confirmation,
                    out SessionSeatBinding binding,
                    out bool reconnect))
            {
                ProtocolFault(
                    NetworkRuntimeFaultCode.SessionContractViolation,
                    connection.Value,
                    NetworkWireKind.SessionHandshakeConfirmation,
                    decoded);
                return;
            }

            _ = BindAcceptedSeat(connection, in binding, reconnect);
            PublishRoomSnapshotIfChanged();
        }

        private ulong ResolvePreparedHandshakeCursor(in SessionSeatBinding binding)
        {
            int seat = binding.Slot;
            if ((uint)seat >= (uint)_seatStates.Length ||
                _replicationSeats[seat].SeatSlot != seat ||
                _replicationSeats[seat].PlayerId != binding.PlayerId)
            {
                Fail(NetworkRuntimeFaultCode.SeatControllerUnavailable, detail: seat);
            }

            if (_seatStates[seat] == SeatEmpty)
            {
                return ReplicatedClientCommandStreamIdentity.FirstBatchSequence;
            }

            if (_seatStates[seat] == SeatAwaitingReconnect &&
                _seatGenerations[seat] == binding.Generation &&
                _seatPlayerIds[seat] == binding.PlayerId.Value)
            {
                NetworkCommandSeat commandSeat = ToCommandSeat(in binding);
                return _commands.GetNextClientBatchSequence(in commandSeat);
            }

            Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: seat);
            return 0;
        }

        private void FlushRejectedConnections()
        {
            if (_outbound.Count != 0)
            {
                return;
            }

            for (int slot = 0; slot < _disconnectAfterFlush.Length; slot++)
            {
                if (!_disconnectAfterFlush[slot])
                {
                    continue;
                }

                int connectionValue = _transportConnections[slot];
                if (connectionValue == 0)
                {
                    Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: slot);
                }

                _disconnectAfterFlush[slot] = false;
                _connectionControl.Disconnect(new ConnectionId(connectionValue));
            }
        }

        private ulong BindAcceptedSeat(
            ConnectionId connection,
            in SessionSeatBinding binding,
            bool expectedReconnect)
        {
            int seat = binding.Slot;
            Arch.Core.Entity controller = default;
            if ((uint)seat >= (uint)_seatStates.Length ||
                _replicationSeats[seat].SeatSlot != seat ||
                _replicationSeats[seat].PlayerId != binding.PlayerId ||
                !_controllers.TryResolveController(in binding, out controller))
            {
                Fail(NetworkRuntimeFaultCode.SeatControllerUnavailable, connection.Value, detail: seat);
            }

            bool reconnect = _seatStates[seat] == SeatAwaitingReconnect &&
                _seatGenerations[seat] == binding.Generation &&
                _seatPlayerIds[seat] == binding.PlayerId.Value;
            if (reconnect != expectedReconnect)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, connection.Value, detail: seat);
            }
            NetworkCommandSeat commandSeat = ToCommandSeat(in binding);
            if (reconnect)
            {
                _commands.RebindSeat(in commandSeat, controller, checked((int)_currentTick));
            }
            else
            {
                if (_seatStates[seat] != SeatEmpty)
                {
                    Fail(NetworkRuntimeFaultCode.SessionContractViolation, connection.Value, detail: seat);
                }

                _commands.BindSeat(in commandSeat, controller, checked((int)_currentTick));
            }

            _seatStates[seat] = SeatConnected;
            _seatConnections[seat] = connection.Value;
            _seatGenerations[seat] = binding.Generation;
            _seatPlayerIds[seat] = binding.PlayerId.Value;
            _seatDisconnectTicks[seat] = 0;
            _seatNeedsFull[seat] = true;
            _seatAcknowledgedSnapshots[seat] = 0;
            IgnoreAcknowledgementsThrough(seat, _seatLastSentSnapshots[seat]);
            _seatLastSentSnapshots[seat] = 0;
            _seatSnapshotSentTicks[seat] = 0;
            if (_seatLastDisclosureSequences[seat] != 0)
            {
                _replicationSeats[seat].DisclosureLog.TryAcknowledgeThrough(
                    _seatLastDisclosureSequences[seat]);
                _seatLastDisclosureSequences[seat] = 0;
            }

            ClearAcknowledgementHistory(seat);
            _commandReassemblers[seat].Reset();
            _observer.OnServerSeatConnected(in binding, reconnect);
            return _commands.GetNextClientBatchSequence(in commandSeat);
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
                assembler.Reset();
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
            NetworkCommandAdmissionOutcome outcome = _commands.Schedule(
                in commandSeat,
                in header,
                checked((int)_currentTick),
                checked((int)_lastCommittedTick),
                _commandEntries.AsSpan(0, entryCount));
            if (outcome.Result == NetworkCommandAdmissionCode.NetworkAdmissionBackpressured)
            {
                SendAdmissionOutcome(connection, in outcome);
            }
        }

        private void ProcessRoomReadyIntent(ConnectionId connection, ReadOnlySpan<byte> payload)
        {
            int seat = FindSeatByConnection(connection.Value);
            if (seat < 0)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnauthenticatedMessage, connection.Value, NetworkWireKind.RoomReadyIntent);
                return;
            }

            NetworkWireCodecStatus decoded = RoomControlWireCodec.TryDecodeReadyIntent(
                payload,
                out NetworkRoomReadyIntent intent);
            if (decoded != NetworkWireCodecStatus.Success)
            {
                ProtocolFault(
                    NetworkRuntimeFaultCode.MalformedDatagram,
                    connection.Value,
                    NetworkWireKind.RoomReadyIntent,
                    decoded);
                return;
            }

            if (intent.SessionEpoch != _sessions.SessionEpoch)
            {
                ProtocolFault(
                    NetworkRuntimeFaultCode.SessionContractViolation,
                    connection.Value,
                    NetworkWireKind.RoomReadyIntent);
                return;
            }

            RoomReadyIntentApplyResult applied = _sessions.ApplyRoomReadyIntent(
                connection,
                intent.ReadyState,
                _lastCommittedTick);
            if (applied == RoomReadyIntentApplyResult.Unauthenticated)
            {
                Fail(
                    NetworkRuntimeFaultCode.SessionContractViolation,
                    connection.Value,
                    NetworkWireKind.RoomReadyIntent,
                    detail: seat);
            }

            if (applied == RoomReadyIntentApplyResult.MatchAlreadyStarted)
            {
                ProtocolFault(
                    NetworkRuntimeFaultCode.SessionContractViolation,
                    connection.Value,
                    NetworkWireKind.RoomReadyIntent,
                    detail: (int)applied);
                return;
            }

            PublishRoomSnapshotIfChanged();
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
                ack.CommittedTick > _lastCommittedTick)
            {
                ProtocolFault(NetworkRuntimeFaultCode.InvalidAcknowledgement, connection.Value, NetworkWireKind.SnapshotAcknowledgement, decoded);
                return;
            }

            if (ack.SnapshotId <= _seatIgnoredAcknowledgementsThrough[seat] ||
                ack.SnapshotId < _seatAcknowledgedSnapshots[seat])
            {
                return;
            }

            if (ack.SnapshotId > _seatLastSentSnapshots[seat] ||
                !TryFindAcknowledgementHistory(seat, ack.SnapshotId, out ulong disclosureSequence))
            {
                ProtocolFault(NetworkRuntimeFaultCode.InvalidAcknowledgement, connection.Value, NetworkWireKind.SnapshotAcknowledgement, decoded);
                return;
            }

            _seatAcknowledgedSnapshots[seat] = ack.SnapshotId;
            _seatSnapshotSentTicks[seat] = 0;
            if (disclosureSequence != 0)
            {
                _replicationSeats[seat].DisclosureLog.TryAcknowledgeThrough(disclosureSequence);
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

            _seatNeedsFull[seat] = true;
            _seatAcknowledgedSnapshots[seat] = 0;
            IgnoreAcknowledgementsThrough(seat, _seatLastSentSnapshots[seat]);
            _seatLastSentSnapshots[seat] = 0;
            _seatSnapshotSentTicks[seat] = 0;
            ClearAcknowledgementHistory(seat);
        }

        private bool HasSnapshotAcknowledgementTimedOut(int seat, uint committedTick)
        {
            ulong lastSent = _seatLastSentSnapshots[seat];
            return lastSent != 0 &&
                lastSent != _seatAcknowledgedSnapshots[seat] &&
                committedTick >= _seatSnapshotSentTicks[seat] &&
                committedTick - _seatSnapshotSentTicks[seat] >=
                    (uint)_capacity.SnapshotAcknowledgementTimeoutTicks;
        }

        private bool TryBeginSnapshotAcknowledgementRecovery(int seat, uint committedTick)
        {
            if (!HasSnapshotAcknowledgementTimedOut(seat, committedTick))
            {
                return false;
            }

            ulong timedOutSnapshotId = _seatLastSentSnapshots[seat];
            SendResyncRequired(seat, NetworkResyncReason.SnapshotAcknowledgementTimeout);
            IgnoreAcknowledgementsThrough(seat, timedOutSnapshotId);
            _seatNeedsFull[seat] = true;
            _seatAcknowledgedSnapshots[seat] = 0;
            _seatLastSentSnapshots[seat] = 0;
            _seatSnapshotSentTicks[seat] = 0;
            ClearAcknowledgementHistory(seat);
            return true;
        }

        private void BuildAndSendReplication(int seat, uint committedTick, ReadOnlySpan<NetworkEntityHandle> activeHandles)
        {
            if (_seatLastSentSnapshots[seat] != 0 &&
                _seatLastSentSnapshots[seat] != _seatAcknowledgedSnapshots[seat])
            {
                // Every delta is based on the last acknowledged snapshot. Keep exactly one
                // snapshot in flight so a later delta can never reuse an obsolete baseline.
                return;
            }

            AuthoritativeReplicationSeatRuntime runtime = _replicationSeats[seat];
            ulong snapshotId = checked(++_nextSnapshotId);
            ReplicationBridgeResult built;
            if (_seatNeedsFull[seat] || _seatAcknowledgedSnapshots[seat] == 0)
            {
                built = runtime.Bridge.BuildFull(
                    runtime.Channel,
                    _sessions.SessionEpoch.Value,
                    committedTick,
                    snapshotId,
                    activeHandles,
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
                    activeHandles,
                    runtime.Projection,
                    runtime.Packet);
                if (built == ReplicationBridgeResult.ResyncRequired)
                {
                    SendResyncRequired(seat, NetworkResyncReason.BaselineUnavailable);
                    _seatAcknowledgedSnapshots[seat] = 0;
                    ClearAcknowledgementHistory(seat);
                    built = runtime.Bridge.BuildFull(
                        runtime.Channel,
                        _sessions.SessionEpoch.Value,
                        committedTick,
                        snapshotId,
                        activeHandles,
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
            _seatSnapshotSentTicks[seat] = committedTick;
            _seatNeedsFull[seat] = false;
            RecordAcknowledgementHistory(seat, snapshotId, disclosureSequence);
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

        private void PublishRoomSnapshotIfChanged()
        {
            if (_lastPublishedRoomRevision == _sessions.RoomRevision)
            {
                return;
            }

            if (!_sessions.TryCopyRoomSnapshot(_roomSeats, out NetworkRoomSnapshotHeader snapshot, out int seatCount) ||
                seatCount != _roomSeats.Length)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: seatCount);
            }

            NetworkWireCodecStatus encoded = RoomControlWireCodec.TryEncodeSnapshot(
                in snapshot,
                _roomSeats,
                _payloadBuffer,
                out int payloadBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                Fail(
                    NetworkRuntimeFaultCode.SessionContractViolation,
                    wireKind: NetworkWireKind.RoomSnapshot,
                    codecStatus: encoded);
            }

            _observer.OnServerRoomSnapshot(in snapshot, _roomSeats.AsSpan(0, seatCount));

            for (int seat = 0; seat < _seatStates.Length; seat++)
            {
                if (_seatStates[seat] != SeatConnected)
                {
                    continue;
                }

                SendFramed(
                    new ConnectionId(_seatConnections[seat]),
                    _capacity.ControlChannel,
                    NetworkWireKind.RoomSnapshot,
                    _payloadBuffer.AsSpan(0, payloadBytes));
            }

            _lastPublishedRoomRevision = snapshot.Revision;
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
                int lastCommittedTick = checked((int)_lastCommittedTick);
                if ((!outcome.IsReplay && outcome.CommittedTick != lastCommittedTick) ||
                    (outcome.IsReplay && outcome.CommittedTick > lastCommittedTick))
                {
                    Fail(
                        NetworkRuntimeFaultCode.AdmissionResultUndeliverable,
                        detail: outcome.CommittedTick);
                }

                EnqueuePendingAdmission(in outcome);
            }
        }

        private void FlushEntityAdmissionResults()
        {
            if (!_authoritativeTickOpen || _lastCommittedTick != _currentTick)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationBuildRejected, detail: unchecked((int)_lastCommittedTick));
            }

            NetworkCommandCorrelationTable correlations = _commands.Correlations;
            uint generation = _orderAdmissionResults.Generation;
            if (!_hasProcessedAdmissionGeneration || generation != _lastProcessedAdmissionGeneration)
            {
                if (_hasProcessedAdmissionGeneration && generation != unchecked(_lastProcessedAdmissionGeneration + 1u))
                {
                    Fail(NetworkRuntimeFaultCode.AdmissionResultUndeliverable, detail: unchecked((int)generation));
                }

                _lastProcessedAdmissionGeneration = generation;
                _processedAdmissionCount = 0;
                _hasProcessedAdmissionGeneration = true;
            }

            int currentCount = _orderAdmissionResults.CurrentGenerationCount;
            if (currentCount < _processedAdmissionCount)
            {
                Fail(NetworkRuntimeFaultCode.AdmissionResultUndeliverable, detail: currentCount);
            }

            for (int i = _processedAdmissionCount; i < currentCount; i++)
            {
                ref readonly OrderAdmissionOutcome entityOutcome = ref _orderAdmissionResults[i];
                if (entityOutcome.AdmissionBatchId <= 0 ||
                    !correlations.TryFindByAdmissionBatchId(
                        entityOutcome.AdmissionBatchId,
                        out int correlation,
                        out NetworkCommandCorrelationContext context))
                {
                    continue;
                }

                if (entityOutcome.Stage != OrderAdmissionStage.EntityIntake)
                {
                    continue;
                }

                int actorCount = context.ActorCount;
                if (entityOutcome.PlayerId != context.PlayerId ||
                    entityOutcome.AdmissionBatchSize != actorCount ||
                    entityOutcome.AdmissionBatchIndex >= actorCount ||
                    !IsEntityAdmissionResult(entityOutcome.Result))
                {
                    Fail(
                        NetworkRuntimeFaultCode.AdmissionResultUndeliverable,
                        detail: entityOutcome.AdmissionBatchId);
                }

                byte previousState = correlations.GetRowState(correlation, entityOutcome.AdmissionBatchIndex);
                bool accepted = OrderSubmitResultSemantics.IsAccepted(entityOutcome.Result);
                bool waitsForActivation = entityOutcome.Result is OrderSubmitResult.Queued or OrderSubmitResult.Pending;
                if ((waitsForActivation && previousState != 0) ||
                    (!waitsForActivation && previousState == 2))
                {
                    Fail(
                        NetworkRuntimeFaultCode.AdmissionResultUndeliverable,
                        detail: entityOutcome.AdmissionBatchId);
                }

                if (accepted)
                {
                    correlations.SetRowState(correlation, entityOutcome.AdmissionBatchIndex, 1);
                }
                else
                {
                    correlations.SetRowState(correlation, entityOutcome.AdmissionBatchIndex, 2);
                    correlations.IncrementTerminalCount(correlation);
                }

                var seat = new NetworkCommandSeat(
                    context.SeatSlot,
                    context.SeatGeneration,
                    context.PlayerId);
                NetworkCommandAdmissionOutcome outcome = NetworkCommandAdmissionOutcome.FromCoreAdmission(
                    in seat,
                    context.ClientBatchSequence,
                    context.TargetTick,
                    actorCount,
                    in entityOutcome,
                    isReplay: false,
                    committedTick: checked((int)_lastCommittedTick));
                if (correlations.GetDeliver(correlation))
                {
                    _commands.RecordEntityAdmission(in outcome);
                    EnqueuePendingAdmission(in outcome);
                }

                if (correlations.GetTerminalCount(correlation) == actorCount)
                {
                    correlations.Clear(correlation);
                }
            }

            _processedAdmissionCount = currentCount;
        }

        private void FlushTerminalResults()
        {
            if (!_authoritativeTickOpen || _lastCommittedTick != _currentTick)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationBuildRejected, detail: unchecked((int)_lastCommittedTick));
            }

            uint generation = _terminalResults.Generation;
            if (!_hasProcessedTerminalGeneration || generation != _lastProcessedTerminalGeneration)
            {
                if (_hasProcessedTerminalGeneration && generation != unchecked(_lastProcessedTerminalGeneration + 1u))
                {
                    Fail(NetworkRuntimeFaultCode.AdmissionResultUndeliverable, detail: unchecked((int)generation));
                }

                _lastProcessedTerminalGeneration = generation;
                _processedTerminalCount = 0;
                _hasProcessedTerminalGeneration = true;
            }

            if (_terminalResults.Count < _processedTerminalCount)
            {
                Fail(NetworkRuntimeFaultCode.AdmissionResultUndeliverable, detail: _terminalResults.Count);
            }

            NetworkCommandCorrelationTable correlations = _commands.Correlations;
            for (int i = _processedTerminalCount; i < _terminalResults.Count; i++)
            {
                ref readonly OrderTerminalOutcome terminalOutcome = ref _terminalResults[i];
                if (!correlations.TryFindByOrderIdAndActor(
                        terminalOutcome.OrderId,
                        terminalOutcome.Actor,
                        out int correlation,
                        out ushort admissionBatchIndex,
                        out NetworkCommandCorrelationContext context))
                {
                    continue;
                }

                if (context.PlayerId <= 0 ||
                    admissionBatchIndex >= context.ActorCount)
                {
                    Fail(
                        NetworkRuntimeFaultCode.AdmissionResultUndeliverable,
                        detail: terminalOutcome.OrderId);
                }

                byte previousState = correlations.GetRowState(correlation, admissionBatchIndex);
                if (previousState == 2 ||
                    (terminalOutcome.State == OrderTerminalState.Completed && previousState != 1))
                {
                    Fail(
                        NetworkRuntimeFaultCode.AdmissionResultUndeliverable,
                        detail: terminalOutcome.OrderId);
                }

                correlations.SetRowState(correlation, admissionBatchIndex, 2);
                correlations.IncrementTerminalCount(correlation);

                var seat = new NetworkCommandSeat(
                    context.SeatSlot,
                    context.SeatGeneration,
                    context.PlayerId);
                NetworkCommandAdmissionOutcome outcome = NetworkCommandAdmissionOutcome.FromTerminal(
                    in seat,
                    context.ClientBatchSequence,
                    context.TargetTick,
                    context.ActorCount,
                    terminalOutcome.OrderId,
                    context.AdmissionBatchId,
                    admissionBatchIndex,
                    terminalOutcome.State,
                    isReplay: false,
                    committedTick: checked((int)_lastCommittedTick));
                if (correlations.GetDeliver(correlation))
                {
                    EnqueuePendingAdmission(in outcome);
                }

                if (correlations.GetTerminalCount(correlation) == context.ActorCount)
                {
                    correlations.Clear(correlation);
                }
            }

            _processedTerminalCount = _terminalResults.Count;
        }

        private static bool IsEntityAdmissionResult(OrderSubmitResult result)
        {
            return result switch
            {
                OrderSubmitResult.Activated => true,
                OrderSubmitResult.Queued => true,
                OrderSubmitResult.Pending => true,
                OrderSubmitResult.RejectedQueueFull => true,
                OrderSubmitResult.RejectedByRule => true,
                OrderSubmitResult.RejectedValidation => true,
                OrderSubmitResult.RejectedInvalidActor => true,
                OrderSubmitResult.RejectedInvalidOrderType => true,
                OrderSubmitResult.RejectedBlackboardCapacity => true,
                OrderSubmitResult.RejectedMissingBlackboard => true,
                OrderSubmitResult.RejectedAdmissionCapacity => true,
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown order submit result."),
            };
        }

        private void EnqueuePendingAdmission(in NetworkCommandAdmissionOutcome outcome)
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
                Fail(NetworkRuntimeFaultCode.AdmissionResultCapacityExceeded, detail: outcome.AdmissionBatchId);
            }

            _pendingAdmissions[free] = outcome;
            _pendingAdmissionActive[free] = true;
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

                SendAdmissionOutcome(new ConnectionId(_seatConnections[seat]), in outcome);
                _pendingAdmissionActive[i] = false;
            }
        }

        private void SendAdmissionOutcome(
            ConnectionId connection,
            in NetworkCommandAdmissionOutcome outcome)
        {
            NetworkWireCodecStatus encoded = CommandAdmissionWireCodec.TryEncode(
                _sessions.SessionEpoch.Value,
                in outcome,
                _payloadBuffer,
                out int payloadBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.CommandBatchRejected, connection.Value, codecStatus: encoded);
            }

            SendFramed(
                connection,
                _capacity.CommandChannel,
                NetworkWireKind.CommandAdmissionResult,
                _payloadBuffer.AsSpan(0, payloadBytes));
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
                if (!_commands.TryReleaseSeat(in commandSeat, checked((int)_lastCommittedTick)))
                {
                    Fail(NetworkRuntimeFaultCode.SessionContractViolation, detail: seat);
                }
                FlushAdmissionResults();

                if (_seatLastDisclosureSequences[seat] != 0)
                {
                    _replicationSeats[seat].DisclosureLog.TryAcknowledgeThrough(
                        _seatLastDisclosureSequences[seat]);
                }

                AbandonAdmissionDelivery(in binding);

                _seatStates[seat] = SeatEmpty;
                _seatConnections[seat] = 0;
                _seatDisconnectTicks[seat] = 0;
                _seatNeedsFull[seat] = false;
                _seatAcknowledgedSnapshots[seat] = 0;
                IgnoreAcknowledgementsThrough(seat, _seatLastSentSnapshots[seat]);
                _seatLastSentSnapshots[seat] = 0;
                _seatSnapshotSentTicks[seat] = 0;
                _seatLastDisclosureSequences[seat] = 0;
                ClearAcknowledgementHistory(seat);
                _commandReassemblers[seat].Reset();
                _observer.OnServerSeatReleased(in binding);
            }
        }

        private void AbandonAdmissionDelivery(in SessionSeatBinding binding)
        {
            for (int i = 0; i < _pendingAdmissionActive.Length; i++)
            {
                if (_pendingAdmissionActive[i] &&
                    _pendingAdmissions[i].SeatSlot == binding.Slot &&
                    _pendingAdmissions[i].SeatGeneration == binding.Generation)
                {
                    _pendingAdmissionActive[i] = false;
                }
            }

            _commands.AbandonCorrelationDeliveryForSeat(binding.Slot, binding.Generation);
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

        private void IgnoreAcknowledgementsThrough(int seat, ulong snapshotId)
        {
            _seatIgnoredAcknowledgementsThrough[seat] = Math.Max(
                _seatIgnoredAcknowledgementsThrough[seat],
                snapshotId);
        }

        private ChannelId GetExpectedClientChannel(NetworkWireKind kind)
        {
            return kind switch
            {
                NetworkWireKind.CommandFragment => _capacity.CommandChannel,
                NetworkWireKind.SessionHandshakeRequest or
                NetworkWireKind.SessionHandshakeConfirmation or
                NetworkWireKind.SnapshotAcknowledgement or
                NetworkWireKind.ResyncRequired or
                NetworkWireKind.RoomReadyIntent => _capacity.ControlChannel,
                _ => default,
            };
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
            int detail = 0)
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
            throw new NetworkRuntimeException(in _lastFault);
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
