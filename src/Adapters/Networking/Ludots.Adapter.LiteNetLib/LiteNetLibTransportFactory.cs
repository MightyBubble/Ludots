using System;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Configuration;

namespace Ludots.Adapter.LiteNetLib;

public static class LiteNetLibTransportFactory
{
    public const string TransportIdentity = "LiteNetLib/2.1.4";

    public static LiteNetLibServerDatagramPort CreateServer(
        NetworkRuntimeConfig config,
        NetworkHostBootstrapConfig host,
        int listenPort,
        string connectionKey)
    {
        ValidateTransport(config);
        ArgumentNullException.ThrowIfNull(host);
        host.Validate();
        LiteNetLibFaultInjectionSettings faults = LiteNetLibFaultInjectionSettings.Create(config, host);
        return new LiteNetLibServerDatagramPort(
            listenPort,
            connectionKey,
            config.PlayerCapacity,
            config.DatagramQueueCapacity,
            config.ConnectionEventCapacity,
            config.MaxDatagramPayloadBytes,
            config.TransportChannelCount,
            config.StateChannelId,
            in faults);
    }

    public static LiteNetLibClientDatagramPort CreateClient(
        NetworkRuntimeConfig config,
        NetworkHostBootstrapConfig hostConfig,
        string host,
        int port,
        string connectionKey)
    {
        ValidateTransport(config);
        ArgumentNullException.ThrowIfNull(hostConfig);
        hostConfig.Validate();
        LiteNetLibFaultInjectionSettings faults = LiteNetLibFaultInjectionSettings.Create(config, hostConfig);
        return new LiteNetLibClientDatagramPort(
            host,
            port,
            connectionKey,
            config.DatagramQueueCapacity,
            config.ConnectionEventCapacity,
            config.MaxDatagramPayloadBytes,
            config.TransportChannelCount,
            config.StateChannelId,
            in faults);
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
