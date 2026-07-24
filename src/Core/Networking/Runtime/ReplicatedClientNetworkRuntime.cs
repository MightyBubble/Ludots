using System;
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

    public sealed class ReplicatedClientNetworkRuntime : INetworkRuntimePort
    {
        private readonly NetworkRuntimeCapacity _capacity;
        private readonly NetworkTransportPortOwnership _transportOwnership;
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

        public ReplicatedClientNetworkRuntime(
            in NetworkRuntimeCapacity capacity,
            NetworkTransportPortOwnership transportOwnership,
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
            _transportOwnership = transportOwnership;
            _connectionEvents = connectionEvents ?? throw new ArgumentNullException(nameof(connectionEvents));
            _datagrams = datagrams ?? throw new ArgumentNullException(nameof(datagrams));
            _connectionControl = connectionControl ?? throw new ArgumentNullException(nameof(connectionControl));
            NetworkTransportPortLifetime.Validate(
                transportOwnership,
                _connectionEvents,
                _datagrams,
                _connectionControl);
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
            if (_replicationFactory.GlobalEntityCapacity != capacity.GlobalEntityCapacity)
            {
                throw new ArgumentException(
                    "Client replication factory capacity must match the global network entity table.",
                    nameof(replicationFactory));
            }

            _admissions = admissions ?? throw new ArgumentNullException(nameof(admissions));
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
            _commandEncoder = new CommandFragmentEncoder(
                capacity.MaxDatagramPayloadBytes,
                capacity.MaxCommandPayloadBytes,
                capacity.MaxCommandFragments);
            _snapshotReassembler = new SnapshotFragmentReassembler(
                capacity.MaxSnapshotBytes,
                capacity.MaxSnapshotFragments);
            _replicationPacket = new ReplicationPacketBuffer(capacity.ReplicationEntityCapacityPerSeat);
            _outbound = new FixedClientDatagramSendQueue(capacity.OutboundQueueCapacity, capacity.MaxDatagramPayloadBytes);
            _receiveBuffer = new byte[capacity.MaxDatagramPayloadBytes];
            _payloadBuffer = new byte[Math.Max(capacity.MaxDatagramPayloadBytes, HandshakeWireCodec.RequestSizeInBytes)];
            _datagramBuffer = new byte[capacity.MaxDatagramPayloadBytes];
            _commandBuffer = new byte[capacity.MaxCommandPayloadBytes];
        }

        public NetworkProcessRole Role => NetworkProcessRole.ReplicatedClient;
        public ReplicatedClientConnectionState State => _state;
        public SessionSeatBinding Seat => _seat;
        public SessionEpoch SessionEpoch => _sessionEpoch;
        public ReconnectToken ReconnectToken => _reconnectToken;
        public uint LastCommittedTick => _lastCommittedTick;
        public bool IsFaulted => _faulted;
        public NetworkRuntimeFault LastFault => _lastFault;

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
            while (_datagrams.TryReceive(_receiveBuffer, out int bytesReceived, out ChannelId channel))
            {
                ProcessDatagram(channel, _receiveBuffer.AsSpan(0, bytesReceived));
            }

            while (_connectionEvents.TryReceiveConnectionEvent(out ClientConnectionEvent connectionEvent))
            {
                ProcessConnectionEvent(in connectionEvent);
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
            if (_disposed) return;
            _disposed = true;
            NetworkTransportPortLifetime.DisposeOwned(
                _transportOwnership,
                _connectionEvents,
                _datagrams,
                _connectionControl);
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
                _reconnectElapsedSeconds = 0f;
            }

            _snapshotReassembler.Reset();
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
            if (decoded != NetworkWireCodecStatus.Success ||
                (response.Accepted &&
                 (response.ProtocolVersion != _protocolVersion ||
                  response.ContentFingerprint != _contentFingerprint)))
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

                    if (response.RejectReason == HandshakeRejectReason.SessionEpochMismatch &&
                        !_sessionEpoch.IsEmpty &&
                        response.SessionEpoch != _sessionEpoch)
                    {
                        TeardownReplicationEpoch(NetworkWireKind.SessionHandshakeResponse);
                    }

                    _connectionControl.Disconnect();
                    _state = ReplicatedClientConnectionState.Disconnected;
                    _reconnectElapsedSeconds = 0f;
                }
                else
                {
                    _state = ReplicatedClientConnectionState.Rejected;
                }

                return;
            }

            if (_replicationBridge != null &&
                !_sessionEpoch.IsEmpty &&
                _sessionEpoch != response.SessionEpoch)
            {
                if (!_credentials.TryClear())
                {
                    Fail(NetworkRuntimeFaultCode.CredentialStoreFailed, NetworkWireKind.SessionHandshakeResponse);
                }

                TeardownReplicationEpoch(NetworkWireKind.SessionHandshakeResponse);
            }

            var stored = new ClientSessionCredentials(response.SessionEpoch, response.ReconnectToken);
            if (!_credentials.TryStore(in stored))
            {
                Fail(NetworkRuntimeFaultCode.CredentialStoreFailed, NetworkWireKind.SessionHandshakeResponse);
            }

            if (_replicationBridge == null)
            {
                ClientWorldReplicationBridge bridge = _replicationFactory.Create(response.SessionEpoch.Value) ??
                    throw new InvalidOperationException("Client replication bridge factory returned null.");
                if (bridge.EntityCapacity != _capacity.GlobalEntityCapacity)
                {
                    throw new InvalidOperationException(
                        "Client replication bridge capacity differs from its factory and the global network entity table.");
                }

                if (bridge.SessionEpoch != response.SessionEpoch.Value)
                {
                    throw new InvalidOperationException(
                        "Client replication bridge SessionEpoch differs from the accepted handshake epoch.");
                }

                _replicationBridge = bridge;
            }
            else if (_sessionEpoch != response.SessionEpoch)
            {
                Fail(NetworkRuntimeFaultCode.SessionContractViolation, NetworkWireKind.SessionHandshakeResponse);
            }

            _seat = response.Seat;
            _sessionEpoch = response.SessionEpoch;
            _reconnectToken = response.ReconnectToken;
            _state = ReplicatedClientConnectionState.Connected;
            _awaitingFullSnapshot = true;
            _snapshotReassembler.Reset();
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

        private void TeardownReplicationEpoch(NetworkWireKind wireKind)
        {
            if (_replicationBridge != null)
            {
                ReplicationBridgeResult tornDown = _replicationBridge.Teardown();
                if (tornDown != ReplicationBridgeResult.Success)
                {
                    Fail(
                        NetworkRuntimeFaultCode.SessionContractViolation,
                        wireKind,
                        detail: (int)tornDown);
                }

                _replicationBridge = null;
            }

            _seat = default;
            _reconnectToken = ReconnectToken.Empty;
            _sessionEpoch = SessionEpoch.Empty;
            _lastCommittedTick = 0;
            _awaitingFullSnapshot = false;
            _snapshotReassembler.Reset();
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
                NetworkWireKind.ResyncRequired => _capacity.ControlChannel,
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
