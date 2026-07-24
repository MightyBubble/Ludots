using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Diagnostics.CodeAnalysis;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;

namespace Ludots.Core.Networking.Runtime
{
    public enum ReplicatedClientConnectionState : byte
    {
        Disconnected = 0,
        Handshaking = 1,
        Connected = 2,
        Rejected = 3,
    }

    public sealed class ReplicatedClientNetworkRuntime : INetworkRuntimePort, IReplicatedClientRoomControlPort
    {
        private readonly NetworkRuntimeCapacity _capacity;
        private readonly IClientConnectionEventPort _connectionEvents;
        private readonly IClientDatagramPort _datagrams;
        private readonly IClientConnectionControlPort _connectionControl;
        private readonly ProtocolVersion _protocolVersion;
        private readonly ContentFingerprint _contentFingerprint;
        private readonly IClientSessionCredentialPort _credentials;
        private readonly IClientReplicationBridgeFactory _replicationFactory;
        private readonly NetworkCommandAdmissionResultBuffer _admissions;
        private readonly INetworkRuntimeObserver _observer;
        private readonly CommandFragmentEncoder _commandEncoder;
        private readonly SnapshotFragmentReassembler _snapshotReassembler;
        private readonly ReplicationPacketBuffer _replicationPacket;
        private readonly FixedClientDatagramSendQueue _outbound;
        private readonly float _reconnectRetrySeconds;

        private readonly byte[] _receiveBuffer;
        private readonly byte[] _payloadBuffer;
        private readonly byte[] _datagramBuffer;
        private readonly byte[] _commandBuffer;
        private readonly NetworkRoomSeatSnapshot[] _roomSeats;
        private readonly NetworkRoomSeatSnapshot[] _roomDecodeSeats;

        private ReplicatedClientConnectionState _state;
        private SessionSeatBinding _seat;
        private ReconnectToken _reconnectToken;
        private SessionEpoch _sessionEpoch;
        private ClientWorldReplicationBridge? _replicationBridge;
        private uint _lastCommittedTick;
        private bool _awaitingFullSnapshot;
        private bool _disposed;
        private bool _faulted;
        private NetworkRuntimeFault _lastFault;
        private float _reconnectElapsedSeconds;
        private bool _hasRoomSnapshot;
        private NetworkRoomSnapshotHeader _roomSnapshot;

        public ReplicatedClientNetworkRuntime(
            in NetworkRuntimeCapacity capacity,
            IClientConnectionEventPort connectionEvents,
            IClientDatagramPort datagrams,
            IClientConnectionControlPort connectionControl,
            float reconnectRetrySeconds,
            ProtocolVersion protocolVersion,
            ContentFingerprint contentFingerprint,
            IClientSessionCredentialPort credentials,
            IClientReplicationBridgeFactory replicationFactory,
            NetworkCommandAdmissionResultBuffer admissions,
            INetworkRuntimeObserver observer)
        {
            if (!protocolVersion.IsWellFormed)
            {
                throw new ArgumentException("Protocol version must be well-formed.", nameof(protocolVersion));
            }

            if (contentFingerprint.IsEmpty)
            {
                throw new ArgumentException("Content fingerprint must be non-empty.", nameof(contentFingerprint));
            }

            _capacity = capacity;
            _connectionEvents = connectionEvents ?? throw new ArgumentNullException(nameof(connectionEvents));
            _datagrams = datagrams ?? throw new ArgumentNullException(nameof(datagrams));
            _connectionControl = connectionControl ?? throw new ArgumentNullException(nameof(connectionControl));
            if (!float.IsFinite(reconnectRetrySeconds) || reconnectRetrySeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(reconnectRetrySeconds));
            }

            _reconnectRetrySeconds = reconnectRetrySeconds;
            _reconnectElapsedSeconds = reconnectRetrySeconds;
            _protocolVersion = protocolVersion;
            _contentFingerprint = contentFingerprint;
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _replicationFactory = replicationFactory ?? throw new ArgumentNullException(nameof(replicationFactory));
            _admissions = admissions ?? throw new ArgumentNullException(nameof(admissions));
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
            if (NetworkWireEnvelope.SizeInBytes + RoomControlWireCodec.GetSnapshotPayloadSize(capacity.ConnectionCapacity) >
                capacity.MaxDatagramPayloadBytes)
            {
                throw new ArgumentException(
                    "Configured datagram capacity cannot carry one complete room snapshot.",
                    nameof(capacity));
            }

            _commandEncoder = new CommandFragmentEncoder(
                capacity.MaxDatagramPayloadBytes,
                capacity.MaxCommandPayloadBytes,
                capacity.MaxCommandFragments);
            _snapshotReassembler = new SnapshotFragmentReassembler(
                capacity.MaxSnapshotBytes,
                capacity.MaxSnapshotFragments);
            _replicationPacket = new ReplicationPacketBuffer(capacity.EntityCapacity);
            _outbound = new FixedClientDatagramSendQueue(capacity.OutboundQueueCapacity, capacity.MaxDatagramPayloadBytes);
            _receiveBuffer = new byte[capacity.MaxDatagramPayloadBytes];
            _payloadBuffer = new byte[Math.Max(capacity.MaxDatagramPayloadBytes, HandshakeWireCodec.RequestSizeInBytes)];
            _datagramBuffer = new byte[capacity.MaxDatagramPayloadBytes];
            _commandBuffer = new byte[capacity.MaxCommandPayloadBytes];
            _roomSeats = new NetworkRoomSeatSnapshot[capacity.ConnectionCapacity];
            _roomDecodeSeats = new NetworkRoomSeatSnapshot[capacity.ConnectionCapacity];
        }

        public NetworkProcessRole Role => NetworkProcessRole.ReplicatedClient;
        public ReplicatedClientConnectionState State => _state;
        public SessionSeatBinding Seat => _seat;
        public SessionEpoch SessionEpoch => _sessionEpoch;
        public ReconnectToken ReconnectToken => _reconnectToken;
        public uint LastCommittedTick => _lastCommittedTick;
        public bool IsFaulted => _faulted;
        public NetworkRuntimeFault LastFault => _lastFault;
        public bool HasRoomSnapshot => _hasRoomSnapshot;
        public NetworkRoomSnapshotHeader LatestRoomSnapshot => _roomSnapshot;

        public bool TryCopyRoomSeats(Span<NetworkRoomSeatSnapshot> destination, out int seatCount)
        {
            EnsureOperational();
            seatCount = _hasRoomSnapshot ? _roomSnapshot.SeatCount : 0;
            if (destination.Length < seatCount)
            {
                return false;
            }

            _roomSeats.AsSpan(0, seatCount).CopyTo(destination);
            return _hasRoomSnapshot;
        }

        public bool TrySetRoomReady(bool ready)
        {
            EnsureOperational();
            if (_state != ReplicatedClientConnectionState.Connected ||
                _sessionEpoch.IsEmpty ||
                (_hasRoomSnapshot && _roomSnapshot.Phase == NetworkRoomPhase.Started))
            {
                return false;
            }

            var intent = new NetworkRoomReadyIntent(
                _sessionEpoch,
                ready ? NetworkRoomReadyState.Ready : NetworkRoomReadyState.Unready);
            NetworkWireCodecStatus encoded = RoomControlWireCodec.TryEncodeReadyIntent(
                in intent,
                _payloadBuffer,
                out int payloadBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, NetworkWireKind.RoomReadyIntent, encoded);
            }

            SendFramed(
                _capacity.ControlChannel,
                NetworkWireKind.RoomReadyIntent,
                _payloadBuffer.AsSpan(0, payloadBytes));
            return true;
        }

        public bool TrySubmitCommand(
            in NetworkCommandBatchHeader header,
            ReadOnlySpan<NetworkCommandWireEntry> entries)
        {
            EnsureOperational();
            if (_state != ReplicatedClientConnectionState.Connected ||
                header.SessionEpoch != _sessionEpoch.Value ||
                header.ClientBatchSequence == 0 ||
                header.EntryCount != entries.Length ||
                entries.Length > _capacity.MaxCommandEntries)
            {
                return false;
            }

            NetworkWireCodecStatus encoded = CommandBatchWireCodec.TryEncode(
                in header,
                entries,
                _commandBuffer,
                out int commandBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                ProtocolFault(NetworkRuntimeFaultCode.CommandBatchRejected, NetworkWireKind.CommandFragment, encoded);
                return false;
            }

            NetworkWireCodecStatus countStatus = _commandEncoder.TryGetFragmentCount(commandBytes, out ushort fragmentCount);
            if (countStatus != NetworkWireCodecStatus.Success)
            {
                ProtocolFault(NetworkRuntimeFaultCode.CommandBatchRejected, NetworkWireKind.CommandFragment, countStatus);
                return false;
            }

            for (ushort fragment = 0; fragment < fragmentCount; fragment++)
            {
                NetworkWireCodecStatus fragmentStatus = _commandEncoder.TryEncodeFragment(
                    _sessionEpoch.Value,
                    header.ClientBatchSequence,
                    _commandBuffer.AsSpan(0, commandBytes),
                    fragment,
                    fragmentCount,
                    _payloadBuffer,
                    out int payloadBytes);
                if (fragmentStatus != NetworkWireCodecStatus.Success)
                {
                    Fail(NetworkRuntimeFaultCode.CommandBatchRejected, NetworkWireKind.CommandFragment, fragmentStatus);
                }

                SendFramed(
                    _capacity.CommandChannel,
                    NetworkWireKind.CommandFragment,
                    _payloadBuffer.AsSpan(0, payloadBytes));
            }

            return true;
        }

        public bool TryConnectNow()
        {
            EnsureOperational();
            if (_state != ReplicatedClientConnectionState.Disconnected ||
                _connectionControl.State != ClientConnectionControlState.Disconnected)
            {
                return false;
            }

            _reconnectElapsedSeconds = 0f;
            bool started = _connectionControl.TryConnect();
            if (!started)
            {
                ProtocolFault(NetworkRuntimeFaultCode.ConnectionAttemptRejected);
            }

            return started;
        }

        public void PumpTransport()
        {
            EnsureOperational();
            FlushOutbound();
            _connectionEvents.Pump();
            while (_connectionEvents.TryReceiveConnectionEvent(out ClientConnectionEvent connectionEvent))
            {
                ProcessConnectionEvent(in connectionEvent);
            }

            while (_datagrams.TryReceive(_receiveBuffer, out int bytesReceived, out ChannelId channel))
            {
                ProcessDatagram(channel, _receiveBuffer.AsSpan(0, bytesReceived));
            }

            FlushOutbound();
        }

        public void BeforeAuthoritativeTick(uint executingTick)
        {
            EnsureOperational();
            throw new InvalidOperationException("The replicated client cannot run an authoritative tick.");
        }

        public void AfterAuthoritativeCommit(uint committedTick)
        {
            EnsureOperational();
            throw new InvalidOperationException("The replicated client cannot commit authoritative state.");
        }

        public void PumpReplicatedClient(float frameDeltaTime)
        {
            EnsureOperational();
            if (!float.IsFinite(frameDeltaTime) || frameDeltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(frameDeltaTime));
            }

            if (_state != ReplicatedClientConnectionState.Disconnected ||
                _connectionControl.State != ClientConnectionControlState.Disconnected)
            {
                return;
            }

            _reconnectElapsedSeconds = Math.Min(
                _reconnectRetrySeconds,
                _reconnectElapsedSeconds + frameDeltaTime);
            if (_reconnectElapsedSeconds >= _reconnectRetrySeconds)
            {
                TryConnectNow();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            List<Exception>? failures = null;
            try
            {
                if (_connectionControl.State != ClientConnectionControlState.Disconnected)
                {
                    _connectionControl.Disconnect();
                }
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>(3)).Add(exception);
            }

            try
            {
                ClearReplicationSession();
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>(3)).Add(exception);
            }

            DisposeDistinctPorts(ref failures);
            if (failures == null)
            {
                return;
            }

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            throw new AggregateException("Client network runtime disposal failed.", failures);
        }

        private void ProcessConnectionEvent(in ClientConnectionEvent connectionEvent)
        {
            if (connectionEvent.Kind == TransportConnectionEventKind.Connected)
            {
                if (_state is ReplicatedClientConnectionState.Handshaking or ReplicatedClientConnectionState.Connected)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.SessionContractViolation);
                    return;
                }

                SendHandshakeRequest();
                _state = ReplicatedClientConnectionState.Handshaking;
                return;
            }

            if (_state != ReplicatedClientConnectionState.Rejected)
            {
                _state = ReplicatedClientConnectionState.Disconnected;
            }
            _reconnectElapsedSeconds = 0f;
            _snapshotReassembler.Reset();
            ClearRoomSnapshot();
        }

        private void SendHandshakeRequest()
        {
            ClientCredentialLoadStatus load = _credentials.TryLoad(out ClientSessionCredentials stored);
            if (load == ClientCredentialLoadStatus.Failed)
            {
                Fail(NetworkRuntimeFaultCode.CredentialLoadFailed);
            }

            SessionEpoch epoch = load == ClientCredentialLoadStatus.Loaded ? stored.SessionEpoch : SessionEpoch.Empty;
            ReconnectToken token = load == ClientCredentialLoadStatus.Loaded ? stored.ReconnectToken : ReconnectToken.Empty;
            var request = new SessionHandshakeRequest(_protocolVersion, _contentFingerprint, token, epoch);
            NetworkWireCodecStatus encoded = HandshakeWireCodec.TryEncodeRequest(in request, _payloadBuffer, out int payloadBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, NetworkWireKind.SessionHandshakeRequest, encoded);
            }

            SendFramed(
                _capacity.ControlChannel,
                NetworkWireKind.SessionHandshakeRequest,
                _payloadBuffer.AsSpan(0, payloadBytes));
        }

        private void ProcessDatagram(ChannelId channel, ReadOnlySpan<byte> datagram)
        {
            NetworkWireCodecStatus decoded = NetworkWireEnvelopeCodec.TryDecode(
                datagram,
                out NetworkWireEnvelope envelope,
                out ReadOnlySpan<byte> payload);
            if (decoded != NetworkWireCodecStatus.Success)
            {
                ProtocolFault(NetworkRuntimeFaultCode.MalformedDatagram, codecStatus: decoded);
                return;
            }

            ChannelId expected = GetExpectedServerChannel(envelope.Kind);
            if (channel != expected)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnexpectedChannel, envelope.Kind, detail: channel.Value);
                return;
            }

            switch (envelope.Kind)
            {
                case NetworkWireKind.SessionHandshakeResponse:
                    ProcessHandshakeResponse(payload);
                    return;
                case NetworkWireKind.CommandAdmissionResult:
                    ProcessAdmission(payload);
                    return;
                case NetworkWireKind.ReplicationPacket:
                    ProcessReplicationPacket(payload, expectedSnapshotId: 0);
                    return;
                case NetworkWireKind.SnapshotFragment:
                    ProcessSnapshotFragment(payload);
                    return;
                case NetworkWireKind.ResyncRequired:
                    ProcessServerResync(payload);
                    return;
                case NetworkWireKind.RoomSnapshot:
                    ProcessRoomSnapshot(payload);
                    return;
                default:
                    ProtocolFault(NetworkRuntimeFaultCode.UnexpectedWireKind, envelope.Kind);
                    return;
            }
        }

        private void ProcessHandshakeResponse(ReadOnlySpan<byte> payload)
        {
            if (_state != ReplicatedClientConnectionState.Handshaking)
            {
                ProtocolFault(NetworkRuntimeFaultCode.SessionContractViolation, NetworkWireKind.SessionHandshakeResponse);
                return;
            }

            NetworkWireCodecStatus decoded = HandshakeWireCodec.TryDecodeResponse(payload, out SessionHandshakeResponse response);
            if (decoded != NetworkWireCodecStatus.Success || !IsHandshakeResponseConsistent(in response))
            {
                ProtocolFault(NetworkRuntimeFaultCode.MalformedDatagram, NetworkWireKind.SessionHandshakeResponse, decoded);
                return;
            }

            _observer.OnClientHandshake(in response);
            if (!response.Accepted)
            {
                if (response.RejectReason is HandshakeRejectReason.StaleOrInvalidReconnectToken or
                    HandshakeRejectReason.SessionEpochMismatch)
                {
                    if (!_credentials.TryClear())
                    {
                        Fail(NetworkRuntimeFaultCode.CredentialStoreFailed, NetworkWireKind.SessionHandshakeResponse);
                    }

                    ClearReplicationSession();
                    _state = ReplicatedClientConnectionState.Disconnected;
                    _reconnectElapsedSeconds = 0f;
                }
                else
                {
                    _state = ReplicatedClientConnectionState.Rejected;
                }

                _connectionControl.Disconnect();

                return;
            }

            var stored = new ClientSessionCredentials(response.SessionEpoch, response.ReconnectToken);
            if (!_credentials.TryStore(in stored))
            {
                Fail(NetworkRuntimeFaultCode.CredentialStoreFailed, NetworkWireKind.SessionHandshakeResponse);
            }

            if (_replicationBridge != null && _sessionEpoch != response.SessionEpoch)
            {
                ClearReplicationSession();
            }

            if (_replicationBridge == null)
            {
                _replicationBridge = _replicationFactory.Create(response.SessionEpoch.Value) ??
                    throw new InvalidOperationException("Client replication bridge factory returned null.");
            }

            _seat = response.Seat;
            _sessionEpoch = response.SessionEpoch;
            _reconnectToken = response.ReconnectToken;
            _state = ReplicatedClientConnectionState.Connected;
            _awaitingFullSnapshot = true;
            _snapshotReassembler.Reset();
        }

        private void ClearRoomSnapshot()
        {
            _hasRoomSnapshot = false;
            _roomSnapshot = default;
            Array.Clear(_roomSeats, 0, _roomSeats.Length);
            Array.Clear(_roomDecodeSeats, 0, _roomDecodeSeats.Length);
        }

        private bool IsHandshakeResponseConsistent(in SessionHandshakeResponse response)
        {
            if (response.Accepted)
            {
                return response.RejectReason == HandshakeRejectReason.None &&
                    response.ProtocolVersion == _protocolVersion &&
                    response.ContentFingerprint == _contentFingerprint &&
                    response.Seat.IsValid &&
                    !response.ReconnectToken.IsEmpty &&
                    !response.SessionEpoch.IsEmpty;
            }

            if (response.RejectReason == HandshakeRejectReason.None ||
                response.Seat.IsValid ||
                !response.ReconnectToken.IsEmpty ||
                response.SessionEpoch.IsEmpty)
            {
                return false;
            }

            return response.RejectReason switch
            {
                HandshakeRejectReason.ProtocolMismatch =>
                    response.ProtocolVersion != _protocolVersion,
                HandshakeRejectReason.ContentMismatch =>
                    response.ProtocolVersion == _protocolVersion &&
                    response.ContentFingerprint != _contentFingerprint,
                _ => response.ProtocolVersion == _protocolVersion &&
                    response.ContentFingerprint == _contentFingerprint,
            };
        }

        private void ClearReplicationSession()
        {
            if (_replicationBridge != null)
            {
                ReplicationBridgeResult cleared = _replicationBridge.Clear();
                if (cleared != ReplicationBridgeResult.Success)
                {
                    Fail(
                        NetworkRuntimeFaultCode.SnapshotApplyRejected,
                        NetworkWireKind.ReplicationPacket,
                        detail: (int)cleared);
                }
            }

            _replicationBridge = null;
            _seat = default;
            _reconnectToken = default;
            _sessionEpoch = default;
            _lastCommittedTick = 0;
            _awaitingFullSnapshot = false;
            _snapshotReassembler.Reset();
            ClearRoomSnapshot();
        }

        private void DisposeDistinctPorts(ref List<Exception>? failures)
        {
            object[] ports = { _connectionEvents, _datagrams, _connectionControl };
            for (int i = 0; i < ports.Length; i++)
            {
                object port = ports[i];
                bool alreadyVisited = false;
                for (int prior = 0; prior < i; prior++)
                {
                    alreadyVisited |= ReferenceEquals(port, ports[prior]);
                }

                if (alreadyVisited || port is not IDisposable disposable)
                {
                    continue;
                }

                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>(3)).Add(exception);
                }
            }
        }

        private void ProcessAdmission(ReadOnlySpan<byte> payload)
        {
            if (_state != ReplicatedClientConnectionState.Connected)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnauthenticatedMessage, NetworkWireKind.CommandAdmissionResult);
                return;
            }

            NetworkCommandSeat commandSeat = new(_seat.Slot, _seat.Generation, _seat.PlayerId.Value);
            NetworkWireCodecStatus decoded = CommandAdmissionWireCodec.TryDecode(
                payload,
                _sessionEpoch.Value,
                in commandSeat,
                out NetworkCommandAdmissionOutcome outcome);
            if (decoded != NetworkWireCodecStatus.Success)
            {
                ProtocolFault(NetworkRuntimeFaultCode.CommandBatchRejected, NetworkWireKind.CommandAdmissionResult, decoded);
                return;
            }

            if (!_admissions.TryWrite(in outcome))
            {
                Fail(NetworkRuntimeFaultCode.AdmissionResultCapacityExceeded, NetworkWireKind.CommandAdmissionResult);
            }

            _observer.OnClientAdmission(in outcome);
        }

        private void ProcessRoomSnapshot(ReadOnlySpan<byte> payload)
        {
            if (_state != ReplicatedClientConnectionState.Connected)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnauthenticatedMessage, NetworkWireKind.RoomSnapshot);
                return;
            }

            NetworkWireCodecStatus decoded = RoomControlWireCodec.TryDecodeSnapshot(
                payload,
                _roomDecodeSeats,
                out NetworkRoomSnapshotHeader snapshot,
                out int seatCount);
            if (decoded != NetworkWireCodecStatus.Success)
            {
                ProtocolFault(NetworkRuntimeFaultCode.MalformedDatagram, NetworkWireKind.RoomSnapshot, decoded, seatCount);
                return;
            }

            if (snapshot.SessionEpoch != _sessionEpoch ||
                _seat.Slot >= seatCount ||
                _roomDecodeSeats[_seat.Slot].ConnectionState != NetworkRoomSeatConnectionState.Connected ||
                _roomDecodeSeats[_seat.Slot].Generation != _seat.Generation ||
                _roomDecodeSeats[_seat.Slot].PlayerId != _seat.PlayerId ||
                (_hasRoomSnapshot && snapshot.Revision <= _roomSnapshot.Revision))
            {
                ProtocolFault(NetworkRuntimeFaultCode.SessionContractViolation, NetworkWireKind.RoomSnapshot);
                return;
            }

            _roomDecodeSeats.AsSpan(0, seatCount).CopyTo(_roomSeats);
            if (seatCount < _roomSeats.Length)
            {
                Array.Clear(_roomSeats, seatCount, _roomSeats.Length - seatCount);
            }

            _roomSnapshot = snapshot;
            _hasRoomSnapshot = true;
            _observer.OnClientRoomSnapshot(in snapshot, _roomSeats.AsSpan(0, seatCount));
        }

        private void ProcessSnapshotFragment(ReadOnlySpan<byte> payload)
        {
            if (_state != ReplicatedClientConnectionState.Connected)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnauthenticatedMessage, NetworkWireKind.SnapshotFragment);
                return;
            }

            SnapshotReassemblyStatus accepted = _snapshotReassembler.TryAcceptWirePayload(payload);
            if (accepted == SnapshotReassemblyStatus.Incomplete)
            {
                return;
            }

            if (accepted != SnapshotReassemblyStatus.Completed)
            {
                ProtocolFault(NetworkRuntimeFaultCode.SnapshotReassemblyRejected, NetworkWireKind.SnapshotFragment, detail: (int)accepted);
                return;
            }

            ulong epoch = _snapshotReassembler.SessionEpoch;
            ulong snapshotId = _snapshotReassembler.SnapshotId;
            if (epoch != _sessionEpoch.Value)
            {
                ProtocolFault(NetworkRuntimeFaultCode.SnapshotReassemblyRejected, NetworkWireKind.SnapshotFragment);
                _snapshotReassembler.Reset();
                return;
            }

            ProcessReplicationPacket(_snapshotReassembler.AssembledPayload, snapshotId);
            _snapshotReassembler.Reset();
        }

        private void ProcessReplicationPacket(ReadOnlySpan<byte> payload, ulong expectedSnapshotId)
        {
            if (_state != ReplicatedClientConnectionState.Connected || _replicationBridge == null)
            {
                ProtocolFault(NetworkRuntimeFaultCode.UnauthenticatedMessage, NetworkWireKind.ReplicationPacket);
                return;
            }

            NetworkWireCodecStatus decoded = ReplicationPacketWireCodec.TryDecode(payload, _replicationPacket);
            if (decoded != NetworkWireCodecStatus.Success ||
                _replicationPacket.Header.SessionEpoch != _sessionEpoch.Value ||
                (expectedSnapshotId != 0 && _replicationPacket.Header.SnapshotId != expectedSnapshotId))
            {
                ProtocolFault(NetworkRuntimeFaultCode.MalformedDatagram, NetworkWireKind.ReplicationPacket, decoded);
                RequestResync(NetworkResyncReason.SnapshotGap);
                return;
            }

            if (_awaitingFullSnapshot && _replicationPacket.Header.Kind != ReplicationPacketKind.Full)
            {
                ProtocolFault(NetworkRuntimeFaultCode.SnapshotApplyRejected, NetworkWireKind.ReplicationPacket, detail: (int)_replicationPacket.Header.Kind);
                RequestResync(NetworkResyncReason.SnapshotGap);
                return;
            }

            ReplicationBridgeResult applied = _replicationBridge.Apply(_replicationPacket);
            if (applied != ReplicationBridgeResult.Success)
            {
                ProtocolFault(NetworkRuntimeFaultCode.SnapshotApplyRejected, NetworkWireKind.ReplicationPacket, detail: (int)applied);
                RequestResync(applied == ReplicationBridgeResult.ResyncRequired
                    ? NetworkResyncReason.BaselineUnavailable
                    : NetworkResyncReason.SnapshotGap);
                return;
            }

            _lastCommittedTick = _replicationPacket.Header.Tick;
            if (_replicationPacket.Header.Kind == ReplicationPacketKind.Full)
            {
                _awaitingFullSnapshot = false;
            }

            SendAcknowledgement(_replicationPacket.Header.SnapshotId, _replicationPacket.Header.Tick);
        }

        private void ProcessServerResync(ReadOnlySpan<byte> payload)
        {
            NetworkWireCodecStatus decoded = SnapshotControlWireCodec.TryDecodeResyncRequired(payload, out NetworkResyncRequired message);
            if (_state != ReplicatedClientConnectionState.Connected ||
                decoded != NetworkWireCodecStatus.Success ||
                message.SessionEpoch != _sessionEpoch.Value)
            {
                ProtocolFault(NetworkRuntimeFaultCode.MalformedDatagram, NetworkWireKind.ResyncRequired, decoded);
                return;
            }

            _awaitingFullSnapshot = true;
            _snapshotReassembler.Reset();
            _observer.OnClientResyncRequired(in message);
        }

        private void SendAcknowledgement(ulong snapshotId, uint committedTick)
        {
            var acknowledgement = new NetworkSnapshotAcknowledgement(_sessionEpoch.Value, snapshotId, committedTick);
            NetworkWireCodecStatus encoded = SnapshotControlWireCodec.TryEncodeAcknowledgement(
                in acknowledgement,
                _payloadBuffer,
                out int payloadBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationEncodeRejected, NetworkWireKind.SnapshotAcknowledgement, encoded);
            }

            SendFramed(
                _capacity.ControlChannel,
                NetworkWireKind.SnapshotAcknowledgement,
                _payloadBuffer.AsSpan(0, payloadBytes));
        }

        private void RequestResync(NetworkResyncReason reason)
        {
            _awaitingFullSnapshot = true;
            _snapshotReassembler.Reset();
            var request = new NetworkResyncRequired(
                _sessionEpoch.Value,
                reason,
                _lastCommittedTick,
                _replicationBridge?.LastSnapshotId ?? 0);
            NetworkWireCodecStatus encoded = SnapshotControlWireCodec.TryEncodeResyncRequired(
                in request,
                _payloadBuffer,
                out int payloadBytes);
            if (encoded != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationEncodeRejected, NetworkWireKind.ResyncRequired, encoded);
            }

            SendFramed(_capacity.ControlChannel, NetworkWireKind.ResyncRequired, _payloadBuffer.AsSpan(0, payloadBytes));
        }

        private void SendFramed(ChannelId channel, NetworkWireKind kind, ReadOnlySpan<byte> payload)
        {
            NetworkWireCodecStatus framed = NetworkWireEnvelopeCodec.TryEncode(kind, payload, _datagramBuffer, out int bytes);
            if (framed != NetworkWireCodecStatus.Success)
            {
                Fail(NetworkRuntimeFaultCode.ReplicationEncodeRejected, kind, framed);
            }

            SendOrQueue(channel, _datagramBuffer.AsSpan(0, bytes));
        }

        private void SendOrQueue(ChannelId channel, ReadOnlySpan<byte> datagram)
        {
            if (_outbound.Count == 0)
            {
                DatagramSendStatus sent = _datagrams.TrySend(channel, datagram);
                if (sent == DatagramSendStatus.Sent)
                {
                    return;
                }

                if (sent == DatagramSendStatus.Closed)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.TransportClosed);
                    return;
                }
            }

            if (!_outbound.TryEnqueue(channel, datagram))
            {
                Fail(NetworkRuntimeFaultCode.OutboundQueueCapacityExceeded, detail: _outbound.Count);
            }
        }

        private void FlushOutbound()
        {
            while (_outbound.TryPeek(out ChannelId channel, out ReadOnlySpan<byte> payload))
            {
                DatagramSendStatus sent = _datagrams.TrySend(channel, payload);
                if (sent == DatagramSendStatus.NotReady)
                {
                    return;
                }

                _outbound.RemoveHead();
                if (sent == DatagramSendStatus.Closed)
                {
                    ProtocolFault(NetworkRuntimeFaultCode.TransportClosed);
                }
            }
        }

        private ChannelId GetExpectedServerChannel(NetworkWireKind kind)
        {
            return kind switch
            {
                NetworkWireKind.CommandAdmissionResult => _capacity.CommandChannel,
                NetworkWireKind.ReplicationPacket => _capacity.StateChannel,
                NetworkWireKind.SessionHandshakeResponse or
                NetworkWireKind.SnapshotFragment or
                NetworkWireKind.ResyncRequired or
                NetworkWireKind.RoomSnapshot => _capacity.ControlChannel,
                _ => default,
            };
        }

        private void ProtocolFault(
            NetworkRuntimeFaultCode code,
            NetworkWireKind wireKind = default,
            NetworkWireCodecStatus codecStatus = default,
            int detail = 0)
        {
            var fault = new NetworkRuntimeFault(
                NetworkRuntimeFaultSeverity.ProtocolViolation,
                code,
                wireKind: wireKind,
                codecStatus: codecStatus,
                detail: detail);
            _observer.OnFault(in fault);
        }

        [DoesNotReturn]
        private void Fail(
            NetworkRuntimeFaultCode code,
            NetworkWireKind wireKind = default,
            NetworkWireCodecStatus codecStatus = default,
            int detail = 0)
        {
            _lastFault = new NetworkRuntimeFault(
                NetworkRuntimeFaultSeverity.LocalContractViolation,
                code,
                wireKind: wireKind,
                codecStatus: codecStatus,
                detail: detail);
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
