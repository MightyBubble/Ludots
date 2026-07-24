using System;
using Ludots.Core.Networking.Configuration;

namespace Ludots.Adapter.LiteNetLib;

public static class LiteNetLibTransportFactory
{
    public const string TransportIdentity = "LiteNetLib/2.1.4";

    public static LiteNetLibServerDatagramPort CreateServer(
        NetworkRuntimeConfig config,
        int listenPort,
        string connectionKey)
    {
        ValidateTransport(config);
        return new LiteNetLibServerDatagramPort(
            listenPort,
            connectionKey,
            config.PlayerCapacity,
            config.DatagramQueueCapacity,
            config.ConnectionEventCapacity,
            config.MaxDatagramPayloadBytes,
            config.TransportMaxConnectAttempts,
            config.TransportDisconnectTimeoutMilliseconds,
            config.ReliableDisconnectFlushTimeoutMilliseconds,
            config.TransportChannelCount,
            new Core.Networking.Transport.ChannelId(checked((byte)config.ControlChannelId)),
            new Core.Networking.Transport.ChannelId(checked((byte)config.CommandChannelId)),
            new Core.Networking.Transport.ChannelId(checked((byte)config.StateChannelId)),
            new Core.Networking.Transport.ChannelId(checked((byte)config.InputChannelId)));
    }

    public static LiteNetLibClientDatagramPort CreateClient(
        NetworkRuntimeConfig config,
        string host,
        int port,
        string connectionKey)
    {
        ValidateTransport(config);
        return new LiteNetLibClientDatagramPort(
            host,
            port,
            connectionKey,
            config.DatagramQueueCapacity,
            config.ConnectionEventCapacity,
            config.MaxDatagramPayloadBytes,
            config.TransportMaxConnectAttempts,
            config.TransportDisconnectTimeoutMilliseconds,
            config.TransportChannelCount,
            new Core.Networking.Transport.ChannelId(checked((byte)config.ControlChannelId)),
            new Core.Networking.Transport.ChannelId(checked((byte)config.CommandChannelId)),
            new Core.Networking.Transport.ChannelId(checked((byte)config.StateChannelId)),
            new Core.Networking.Transport.ChannelId(checked((byte)config.InputChannelId)));
    }

    private static void ValidateTransport(NetworkRuntimeConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        config.Validate();
        if (!string.Equals(config.ReferenceTransport, TransportIdentity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Networking profile requires '{config.ReferenceTransport}', but this adapter certifies '{TransportIdentity}'.");
        }
    }
}
