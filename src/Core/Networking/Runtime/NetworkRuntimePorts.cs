using System;
using Arch.Core;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;

namespace Ludots.Core.Networking.Runtime
{
    public enum NetworkRuntimeFaultSeverity : byte
    {
        ProtocolViolation = 1,
        LocalContractViolation = 2,
    }

    public enum NetworkRuntimeFaultCode : byte
    {
        MalformedDatagram = 1,
        UnexpectedWireKind = 2,
        UnexpectedChannel = 3,
        UnknownConnection = 4,
        ConnectionCapacityExceeded = 5,
        UnauthenticatedMessage = 6,
        CommandReassemblyRejected = 7,
        CommandBatchRejected = 8,
        SnapshotReassemblyRejected = 9,
        SnapshotApplyRejected = 10,
        InvalidAcknowledgement = 11,
        OutboundQueueCapacityExceeded = 12,
        TransportClosed = 13,
        CredentialLoadFailed = 14,
        CredentialStoreFailed = 15,
        ReplicationInputRejected = 16,
        ReplicationBuildRejected = 17,
        ReplicationEncodeRejected = 18,
        AdmissionResultCapacityExceeded = 19,
        AdmissionResultUndeliverable = 20,
        SeatControllerUnavailable = 21,
        SessionContractViolation = 22,
        ConnectionAttemptRejected = 23,
    }

    public readonly struct NetworkRuntimeFault
    {
        public NetworkRuntimeFault(
            NetworkRuntimeFaultSeverity severity,
            NetworkRuntimeFaultCode code,
            int connectionValue = 0,
            NetworkWireKind wireKind = default,
            NetworkWireCodecStatus codecStatus = default,
            int detail = 0)
        {
            Severity = severity;
            Code = code;
            ConnectionValue = connectionValue;
            WireKind = wireKind;
            CodecStatus = codecStatus;
            Detail = detail;
        }

        public NetworkRuntimeFaultSeverity Severity { get; }
        public NetworkRuntimeFaultCode Code { get; }
        public int ConnectionValue { get; }
        public NetworkWireKind WireKind { get; }
        public NetworkWireCodecStatus CodecStatus { get; }
        public int Detail { get; }
    }

    public interface INetworkRuntimeObserver
    {
        void OnFault(in NetworkRuntimeFault fault);

        void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected);

        void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason);

        void OnServerSeatReleased(in SessionSeatBinding seat);

        void OnServerRoomSnapshot(
            in NetworkRoomSnapshotHeader snapshot,
            ReadOnlySpan<NetworkRoomSeatSnapshot> seats);

        void OnClientHandshake(in SessionHandshakeResponse response);

        void OnClientAdmission(in Commands.NetworkCommandAdmissionOutcome outcome);

        void OnClientResyncRequired(in NetworkResyncRequired message);

        void OnClientRoomSnapshot(
            in NetworkRoomSnapshotHeader snapshot,
            ReadOnlySpan<NetworkRoomSeatSnapshot> seats);
    }

    /// <summary>
    /// Resolves the authoritative ECS representative for a server-assigned seat. The runtime uses
    /// this only to bind the single NetworkCommandIngress instance supplied by the composition root.
    /// </summary>
    public interface IAuthoritativeSeatControllerResolver
    {
        bool TryResolveController(in SessionSeatBinding seat, out Entity controller);
    }

    /// <summary>
    /// Copies the current authoritative network-entity handle set into caller-owned fixed storage.
    /// </summary>
    public interface IAuthoritativeReplicationInputPort
    {
        bool TryCopyActiveHandles(Span<NetworkEntityHandle> destination, out int count);
    }

    public enum ClientCredentialLoadStatus : byte
    {
        Empty = 0,
        Loaded = 1,
        Failed = 2,
    }

    public readonly struct ClientSessionCredentials
    {
        public ClientSessionCredentials(SessionEpoch sessionEpoch, ReconnectToken reconnectToken)
        {
            if (sessionEpoch.IsEmpty)
            {
                throw new ArgumentException("Session epoch must be non-empty.", nameof(sessionEpoch));
            }

            if (reconnectToken.IsEmpty)
            {
                throw new ArgumentException("Reconnect token must be non-empty.", nameof(reconnectToken));
            }

            SessionEpoch = sessionEpoch;
            ReconnectToken = reconnectToken;
        }

        public SessionEpoch SessionEpoch { get; }
        public ReconnectToken ReconnectToken { get; }
    }

    /// <summary>
    /// Platform adapter for atomic reconnect credential persistence.
    /// </summary>
    public interface IClientSessionCredentialPort
    {
        ClientCredentialLoadStatus TryLoad(out ClientSessionCredentials credentials);

        bool TryStore(in ClientSessionCredentials credentials);

        bool TryClear();
    }

    public interface IClientReplicationBridgeFactory
    {
        ClientWorldReplicationBridge Create(ulong sessionEpoch);
    }

    public enum ClientConnectionControlState : byte
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
    }

    /// <summary>
    /// Explicit client-side connection control. Endpoint ownership remains in the platform adapter;
    /// the replicated runtime owns retry timing and never assumes that construction connected it.
    /// </summary>
    public interface IClientConnectionControlPort
    {
        ClientConnectionControlState State { get; }

        int RoundTripTimeMilliseconds { get; }

        bool TryConnect();

        void Disconnect();
    }

    public interface IReplicatedClientRuntimeStatus
    {
        ReplicatedClientConnectionState ConnectionState { get; }

        bool HasEstablishedSession { get; }

        bool IsAwaitingFullSnapshot { get; }

        bool IsFaulted { get; }

        float ReconnectWindowRemainingSeconds { get; }

        int RoundTripTimeMilliseconds { get; }
    }

    public interface IReplicatedClientRoomControlPort
    {
        bool TrySetRoomReady(bool ready);
    }

    public sealed class AuthoritativeReplicationSeatRuntime
    {
        public AuthoritativeReplicationSeatRuntime(
            int seatSlot,
            PlayerId playerId,
            AuthoritativeWorldReplicationBridge bridge,
            AuthoritativeReplicationChannel channel,
            ReplicationDisclosureChangeLog disclosureLog,
            ReplicationProjectionBuffer projection,
            ReplicationPacketBuffer packet)
        {
            if (seatSlot < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seatSlot));
            }

            if (playerId.Value <= 0)
            {
                throw new ArgumentException("Replication seat requires a positive player id.", nameof(playerId));
            }

            Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            DisclosureLog = disclosureLog ?? throw new ArgumentNullException(nameof(disclosureLog));
            Projection = projection ?? throw new ArgumentNullException(nameof(projection));
            Packet = packet ?? throw new ArgumentNullException(nameof(packet));

            if (!ReferenceEquals(channel.DisclosureLog, disclosureLog))
            {
                throw new ArgumentException(
                    "Replication seat channel and acknowledgement path must share one disclosure log.",
                    nameof(disclosureLog));
            }

            int capacity = bridge.EntityCapacity;
            if (projection.EntityCapacity != capacity || packet.EntityCapacity < capacity)
            {
                throw new ArgumentException("Replication seat capacities must match its world bridge.");
            }

            SeatSlot = seatSlot;
            PlayerId = playerId;
        }

        public int SeatSlot { get; }
        public PlayerId PlayerId { get; }
        public AuthoritativeWorldReplicationBridge Bridge { get; }
        public AuthoritativeReplicationChannel Channel { get; }
        public ReplicationDisclosureChangeLog DisclosureLog { get; }
        public ReplicationProjectionBuffer Projection { get; }
        public ReplicationPacketBuffer Packet { get; }
    }

    public sealed class NetworkRuntimeException : InvalidOperationException
    {
        public NetworkRuntimeException(in NetworkRuntimeFault fault)
            : base($"Network runtime contract failed: {fault.Code} ({fault.Detail}).")
        {
            Fault = fault;
        }

        public NetworkRuntimeFault Fault { get; }
    }
}
