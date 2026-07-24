using System.Buffers.Binary;
using System.Security.Cryptography;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.LiteNetLib;

public static class LiteNetLibNetworkRuntimeInstaller
{
    private const int SessionEpochIssueAttempts = 16;

    public static void Install(in GameBootstrapResult bootstrap, string runtimeBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeBaseDirectory);
        GameEngine engine = bootstrap.Engine ??
            throw new ArgumentException("Bootstrap engine is required.", nameof(bootstrap));
        NetworkRuntimeConfig config = bootstrap.Config.Networking ??
            throw new InvalidOperationException(
                "Network runtime installation requires merged networking configuration.");
        NetworkHostBootstrapConfig host = bootstrap.NetworkHost ??
            throw new InvalidOperationException(
                "Network runtime installation requires explicit host configuration.");
        config.Validate();
        host.Validate();

        if (!ContentFingerprint.TryParseHex(bootstrap.PlanFingerprint, out ContentFingerprint contentFingerprint) ||
            contentFingerprint.IsEmpty)
        {
            throw new InvalidOperationException(
                "Networked launch requires a non-empty 64-character launcher plan fingerprint.");
        }

        if (engine.TryGetService(CoreServiceKeys.NetworkRuntimePort, out _))
        {
            throw new InvalidOperationException("A network runtime is already installed for this engine.");
        }

        if (engine.TryGetService(CoreServiceKeys.NetworkRuntimeStateObserver, out _))
        {
            throw new InvalidOperationException(
                "NetworkRuntimeStateObserver is already owned by another composition root.");
        }

        NetworkProcessRole role = host.ResolveRole();
        var observer = new NetworkRuntimeStateObserver(config.PlayerCapacity);
        string baseDirectory = Path.GetFullPath(runtimeBaseDirectory);
        var deferred = new DeferredNetworkRuntimePort(
            role,
            () => role == NetworkProcessRole.AuthoritativeServer
                ? ComposeServer(engine, config, host, contentFingerprint, observer)
                : ComposeClient(engine, config, host, contentFingerprint, observer, baseDirectory));

        bool observerInstalled = false;
        try
        {
            engine.SetService(CoreServiceKeys.NetworkRuntimeStateObserver, observer);
            observerInstalled = true;
            engine.ConfigureNetworkRuntime(role, deferred);
        }
        catch
        {
            deferred.Dispose();
            if (observerInstalled &&
                engine.TryGetService(CoreServiceKeys.NetworkRuntimeStateObserver, out NetworkRuntimeStateObserver installed) &&
                ReferenceEquals(installed, observer))
            {
                engine.RemoveService(CoreServiceKeys.NetworkRuntimeStateObserver);
            }

            throw;
        }
    }

    private static INetworkRuntimePort ComposeServer(
        GameEngine engine,
        NetworkRuntimeConfig config,
        NetworkHostBootstrapConfig host,
        ContentFingerprint contentFingerprint,
        NetworkRuntimeStateObserver stateObserver)
    {
        INetworkRuntimeObserver observer = ResolveNetworkRuntimeObserver(engine, stateObserver);
        ReplicationSchemaProjectorRegistry projectors = Require(
            engine,
            CoreServiceKeys.ReplicationSchemaProjectors);
        projectors.Freeze();
        if (!HasRegisteredProjector(projectors))
        {
            throw new InvalidOperationException(
                "Authoritative launch registered no replication schema projectors during runtime composition.");
        }
        NetworkCommandIngress commands = Require(engine, CoreServiceKeys.NetworkCommandIngress);
        NetworkCommandAdmissionResultBuffer admissions = Require(
            engine,
            CoreServiceKeys.NetworkCommandAdmissionResults);
        IAuthoritativeReplicationInterestPort replicationInterest = Require(
            engine,
            CoreServiceKeys.AuthoritativeReplicationInterest);

        var capacity = NetworkRuntimeCapacity.FromConfig(config);
        ResolveAuthoritativeComposition(
            engine,
            capacity.ConnectionCapacity,
            capacity.GlobalEntityCapacity,
            capacity.ReplicationEntityCapacityPerSeat,
            out IAuthoritativeSeatControllerResolver controllers,
            out IAuthoritativeReplicationSeatRuntimeFactory seatFactory);
        var protocol = new ProtocolVersion(config.ProtocolMajor, config.ProtocolMinor);
        var sessionEpoch = IssueSessionEpoch();
        var sessions = new AuthoritativeSessionRegistry(
            config.PlayerCapacity,
            sessionEpoch,
            protocol,
            contentFingerprint,
            checked((uint)(config.ReconnectWindowSeconds * config.SimulationTickRateHz)));
        var fixedInput = new AuthoritativeFixedInputIngress(
            capacity.CreateFixedInputProtocolConfig(sessionEpoch.Value, sessions.SeatCapacity),
            engine.GameSession.SimulationTicks);

        LiteNetLibServerDatagramPort transport = LiteNetLibTransportFactory.CreateServer(
            config,
            host.Port,
            host.ConnectionKey);
        AuthoritativeServerNetworkRuntime? runtime = null;
        try
        {
            runtime = new AuthoritativeServerNetworkRuntime(
                in capacity,
                NetworkTransportPortOwnership.Owned,
                transport,
                transport,
                transport,
                sessions,
                commands,
                admissions,
                controllers,
                replicationInterest,
                seatFactory,
                fixedInput,
                observer);

            PublishAuthoritativeFixedInput(engine, fixedInput);
            return runtime;
        }
        catch
        {
            if (runtime != null)
            {
                runtime.Dispose();
            }
            else
            {
                transport.Dispose();
            }

            throw;
        }
    }

    private static INetworkRuntimePort ComposeClient(
        GameEngine engine,
        NetworkRuntimeConfig config,
        NetworkHostBootstrapConfig host,
        ContentFingerprint contentFingerprint,
        NetworkRuntimeStateObserver stateObserver,
        string runtimeBaseDirectory)
    {
        INetworkRuntimeObserver observer = ResolveNetworkRuntimeObserver(engine, stateObserver);
        ClientReplicationSchemaApplierRegistry appliers = Require(
            engine,
            CoreServiceKeys.ClientReplicationSchemaAppliers);
        appliers.Freeze();
        if (!HasRegisteredApplier(appliers))
        {
            throw new InvalidOperationException(
                "Replicated client launch registered no replication schema appliers during runtime composition.");
        }
        NetworkCommandAdmissionResultBuffer admissions = Require(
            engine,
            CoreServiceKeys.NetworkCommandAdmissionResults);
        var capacity = NetworkRuntimeCapacity.FromConfig(config);
        var protocol = new ProtocolVersion(config.ProtocolMajor, config.ProtocolMinor);
        string credentialPath = Path.IsPathRooted(host.CredentialPath)
            ? Path.GetFullPath(host.CredentialPath)
            : Path.GetFullPath(Path.Combine(runtimeBaseDirectory, host.CredentialPath));
        LiteNetLibClientDatagramPort transport = LiteNetLibTransportFactory.CreateClient(
            config,
            host.Host,
            host.Port,
            host.ConnectionKey);
        try
        {
            return new ReplicatedClientNetworkRuntime(
                in capacity,
                NetworkTransportPortOwnership.Owned,
                transport,
                transport,
                transport,
                config.ClientReconnectRetryMilliseconds / 1000f,
                protocol,
                contentFingerprint,
                new AtomicFileClientSessionCredentialPort(credentialPath),
                new ClientReplicationBridgeFactory(
                    engine.World,
                    config.GlobalNetworkEntityCapacity,
                    appliers),
                admissions,
                observer);
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    internal static void ResolveAuthoritativeComposition(
        GameEngine engine,
        int seatCapacity,
        int globalEntityCapacity,
        int replicationEntityCapacityPerSeat,
        out IAuthoritativeSeatControllerResolver controllers,
        out IAuthoritativeReplicationSeatRuntimeFactory seatFactory)
    {
        ArgumentNullException.ThrowIfNull(engine);
        controllers = Require(engine, CoreServiceKeys.AuthoritativeSeatControllerResolver);
        seatFactory = Require(engine, CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory);
        if (seatFactory.SeatCapacity != seatCapacity ||
            seatFactory.GlobalEntityCapacity != globalEntityCapacity ||
            seatFactory.ReplicationEntityCapacityPerSeat != replicationEntityCapacityPerSeat)
        {
            throw new InvalidOperationException(
                "Authoritative replication seat factory capacities do not match the validated network runtime capacity.");
        }
    }

    internal static INetworkRuntimeObserver ResolveNetworkRuntimeObserver(
        GameEngine engine,
        NetworkRuntimeStateObserver stateObserver)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(stateObserver);
        INetworkRuntimeObserver bridge = Require(
            engine,
            CoreServiceKeys.NetworkRuntimeObserverBridge);
        return new NetworkRuntimeObserverFanout(stateObserver, bridge);
    }

    private static void PublishAuthoritativeFixedInput(
        GameEngine engine,
        AuthoritativeFixedInputIngress fixedInput)
    {
        if (engine.TryGetService(CoreServiceKeys.AuthoritativeFixedInputIngress, out _))
        {
            throw new InvalidOperationException(
                "AuthoritativeFixedInputIngress is already owned by another composition root.");
        }

        engine.SetService(CoreServiceKeys.AuthoritativeFixedInputIngress, fixedInput);
    }

    private static T Require<T>(GameEngine engine, ServiceKey<T> key)
        where T : class
    {
        return engine.GetService(key) ??
            throw new InvalidOperationException(
                $"Network composition requires service '{key.Name}'.");
    }

    private static bool HasRegisteredProjector(ReplicationSchemaProjectorRegistry registry)
    {
        for (int schemaId = 1; schemaId <= registry.SchemaCapacity; schemaId++)
        {
            if (registry.TryGet(schemaId, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRegisteredApplier(ClientReplicationSchemaApplierRegistry registry)
    {
        for (int schemaId = 1; schemaId <= registry.SchemaCapacity; schemaId++)
        {
            if (registry.TryGet(schemaId, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static SessionEpoch IssueSessionEpoch()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        for (int attempt = 0; attempt < SessionEpochIssueAttempts; attempt++)
        {
            RandomNumberGenerator.Fill(bytes);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            if (value != 0)
            {
                return new SessionEpoch(value);
            }
        }

        throw new InvalidOperationException(
            $"Failed to issue a non-empty session epoch within {SessionEpochIssueAttempts} attempts.");
    }
}
