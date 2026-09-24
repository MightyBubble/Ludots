using System;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

/// <summary>Server transport that exposes datagram, connection-event, and control ports as one lifetime.</summary>
public interface ILiteNetLibServerTransport :
    IServerDatagramPort,
    IServerConnectionEventPort,
    IServerConnectionControlPort,
    IDisposable
{
    int BoundPort { get; }
}

/// <summary>Client transport that exposes datagram, connection-event, and control ports as one lifetime.</summary>
public interface ILiteNetLibClientTransport :
    IClientDatagramPort,
    IClientConnectionEventPort,
    IClientConnectionControlPort,
    IDisposable
{
}

public static class LiteNetLibTransportFactory
{
    public const string TransportIdentity = "LiteNetLib/2.1.4";

    public static ILiteNetLibServerTransport CreateServer(
        NetworkRuntimeConfig config,
        int listenPort,
        string connectionKey,
        uint faultInjectionSeed = 0,
        NetworkAdapterMetricsSampler? metrics = null)
    {
        ValidateTransport(config);
        var inner = new LiteNetLibServerDatagramPort(
            listenPort,
            connectionKey,
            config.PlayerCapacity,
            config.DatagramQueueCapacity,
            config.ConnectionEventCapacity,
            config.MaxDatagramPayloadBytes,
            config.TransportChannelCount,
            config.StateChannelId);

        if (!ShouldWrap(config))
        {
            return inner;
        }

        NetworkFaultProfileConfig profile = config.ResolveActiveFaultProfile();
        var injector = new DeterministicTransportFaultInjector(
            profile,
            faultInjectionSeed,
            slotCapacity: config.DatagramQueueCapacity,
            maxPayloadBytes: config.MaxDatagramPayloadBytes,
            useWallClock: true);
        return new DeterministicFaultInjectingServerDatagramPort(
            inner,
            injector,
            config.PlayerCapacity,
            config.MaxDatagramPayloadBytes,
            metrics);
    }

    public static ILiteNetLibClientTransport CreateClient(
        NetworkRuntimeConfig config,
        string host,
        int port,
        string connectionKey,
        uint faultInjectionSeed = 0,
        NetworkAdapterMetricsSampler? metrics = null)
    {
        ValidateTransport(config);
        var inner = new LiteNetLibClientDatagramPort(
            host,
            port,
            connectionKey,
            config.DatagramQueueCapacity,
            config.ConnectionEventCapacity,
            config.MaxDatagramPayloadBytes,
            config.TransportChannelCount,
            config.StateChannelId);

        if (!ShouldWrap(config))
        {
            return inner;
        }

        NetworkFaultProfileConfig profile = config.ResolveActiveFaultProfile();
        var injector = new DeterministicTransportFaultInjector(
            profile,
            faultInjectionSeed,
            slotCapacity: config.DatagramQueueCapacity,
            maxPayloadBytes: config.MaxDatagramPayloadBytes,
            useWallClock: true);
        return new DeterministicFaultInjectingClientDatagramPort(
            inner,
            injector,
            config.MaxDatagramPayloadBytes,
            metrics);
    }

    public static bool ShouldWrap(NetworkRuntimeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.RequiresAdapterMetricsOrFaultWrap();
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
