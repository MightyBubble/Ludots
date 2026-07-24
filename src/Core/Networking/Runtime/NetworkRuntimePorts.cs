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
        FixedInputRejected = 24,
        ReplicationSeatRuntimeRejected = 25,
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

        void OnClientHandshake(in SessionHandshakeResponse response);

        void OnClientAdmission(in Commands.NetworkCommandAdmissionOutcome outcome);

        void OnClientResyncRequired(in NetworkResyncRequired message);
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
    /// Copies one seat's strictly slot-ordered authoritative interest set into caller-owned fixed storage.
    /// The port selects spatial or gameplay interest; Core remains the sole owner of disclosure and baseline semantics.
    /// </summary>
    public interface IAuthoritativeReplicationInterestPort
    {
        bool TryCopyInterest(
            in SessionSeatBinding seat,
            Span<NetworkEntityHandle> destination,
            out int count);
    }

    /// <summary>
    /// Owns lazily-created replication state for authenticated authoritative seats.
    /// A successful acquire must return a pristine runtime exclusively leased to the exact
    /// seat generation and viewer until the matching release call.
    /// </summary>
    public interface IAuthoritativeReplicationSeatRuntimeFactory
    {
        int SeatCapacity { get; }

        int GlobalEntityCapacity { get; }

        int ReplicationEntityCapacityPerSeat { get; }

        bool TryAcquire(
            in SessionSeatBinding seat,
            Entity viewer,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AuthoritativeReplicationSeatRuntime? runtime);

        bool TryRelease(
            in SessionSeatBinding seat,
            AuthoritativeReplicationSeatRuntime runtime);
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
        int GlobalEntityCapacity { get; }

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

        bool TryConnect();

        void Disconnect();
    }

    public sealed class AuthoritativeReplicationSeatRuntime
    {
        public AuthoritativeReplicationSeatRuntime(
            AuthoritativeWorldReplicationBridge bridge,
            AuthoritativeReplicationChannel channel,
            ReplicationProjectionBuffer projection,
            ReplicationPacketBuffer packet)
        {
            Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            Projection = projection ?? throw new ArgumentNullException(nameof(projection));
            Packet = packet ?? throw new ArgumentNullException(nameof(packet));

            int capacity = bridge.ReplicationEntityCapacityPerSeat;
            if (projection.EntityCapacity != capacity || packet.EntityCapacity != capacity)
            {
                throw new ArgumentException("Replication seat capacities must match its world bridge.");
            }

            if (channel.ReplicationEntityCapacityPerSeat != capacity)
            {
                throw new ArgumentException("Replication channel capacity must match its world bridge.");
            }

            if (!ReferenceEquals(bridge.EntityTable, channel.EntityTable))
            {
                throw new ArgumentException("Replication bridge and channel must share the same network entity table.");
            }

            if (channel.DisclosureChangeLogCapacity < packet.DisclosureCapacity)
            {
                throw new ArgumentException("Replication disclosure log must hold one maximum-area transition.");
            }
        }

        public AuthoritativeWorldReplicationBridge Bridge { get; }
        public AuthoritativeReplicationChannel Channel { get; }
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

        public NetworkRuntimeException(in NetworkRuntimeFault fault, Exception innerException)
            : base($"Network runtime contract failed: {fault.Code} ({fault.Detail}).", innerException)
        {
            Fault = fault;
        }

        public NetworkRuntimeFault Fault { get; }
    }
}
