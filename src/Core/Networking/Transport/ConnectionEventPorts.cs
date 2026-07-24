using System;

namespace Ludots.Core.Networking.Transport
{
    public enum TransportConnectionEventKind : byte
    {
        Connected = 0,
        Disconnected = 1,
    }

    public enum TransportDisconnectReason : byte
    {
        None = 0,
        RemoteClosed = 1,
        Timeout = 2,
        Rejected = 3,
        TransportError = 4,
        LocalClosed = 5,
    }

    public readonly struct ServerConnectionEvent
    {
        public ServerConnectionEvent(
            ConnectionId connectionId,
            TransportConnectionEventKind kind,
            TransportDisconnectReason disconnectReason = TransportDisconnectReason.None)
        {
            if (kind == TransportConnectionEventKind.Connected && disconnectReason != TransportDisconnectReason.None)
            {
                throw new ArgumentException("Connected events cannot carry a disconnect reason.", nameof(disconnectReason));
            }

            if (kind == TransportConnectionEventKind.Disconnected && disconnectReason == TransportDisconnectReason.None)
            {
                throw new ArgumentException("Disconnected events require an explicit reason.", nameof(disconnectReason));
            }

            ConnectionId = connectionId;
            Kind = kind;
            DisconnectReason = disconnectReason;
        }

        public ConnectionId ConnectionId { get; }
        public TransportConnectionEventKind Kind { get; }
        public TransportDisconnectReason DisconnectReason { get; }
    }

    public readonly struct ClientConnectionEvent
    {
        public ClientConnectionEvent(
            TransportConnectionEventKind kind,
            TransportDisconnectReason disconnectReason = TransportDisconnectReason.None)
        {
            if (kind == TransportConnectionEventKind.Connected && disconnectReason != TransportDisconnectReason.None)
            {
                throw new ArgumentException("Connected events cannot carry a disconnect reason.", nameof(disconnectReason));
            }

            if (kind == TransportConnectionEventKind.Disconnected && disconnectReason == TransportDisconnectReason.None)
            {
                throw new ArgumentException("Disconnected events require an explicit reason.", nameof(disconnectReason));
            }

            Kind = kind;
            DisconnectReason = disconnectReason;
        }

        public TransportConnectionEventKind Kind { get; }
        public TransportDisconnectReason DisconnectReason { get; }
    }

    public interface IServerConnectionEventPort
    {
        void Pump();
        bool TryReceiveConnectionEvent(out ServerConnectionEvent connectionEvent);
    }

    public interface IClientConnectionEventPort
    {
        void Pump();
        bool TryReceiveConnectionEvent(out ClientConnectionEvent connectionEvent);
    }
}
