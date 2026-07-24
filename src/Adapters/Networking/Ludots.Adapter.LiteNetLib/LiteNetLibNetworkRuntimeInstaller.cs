using System.Buffers.Binary;
using System.Security.Cryptography;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Knowledge;
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
        NetworkRuntimeStateObserver observer)
    {
        ReplicationSchemaProjectorRegistry projectors = Require(
            engine,
            CoreServiceKeys.ReplicationSchemaProjectors);
        projectors.Freeze();
        if (!HasRegisteredProjector(projectors))
        {
            throw new InvalidOperationException(
                "Authoritative launch registered no replication schema projectors during runtime composition.");
        }
        NetworkEntityTable entities = Require(engine, CoreServiceKeys.NetworkEntityTable);
        KnowledgeProjectionStore knowledge = Require(engine, CoreServiceKeys.KnowledgeProjectionStore);
        NetworkCommandIngress commands = Require(engine, CoreServiceKeys.NetworkCommandIngress);
        NetworkCommandAdmissionResultBuffer admissions = Require(
            engine,
            CoreServiceKeys.NetworkCommandAdmissionResults);
        IAuthoritativeReplicationInterestPort replicationInterest = Require(
            engine,
            CoreServiceKeys.AuthoritativeReplicationInterest);
        var mapSession = engine.CurrentMapSession ??
            throw new InvalidOperationException(
                "Authoritative networking requires the startup map before accepting connections.");
        var controllers = new AuthoritativeSeatControllerRegistry(
            engine.World,
            mapSession.PlayerEntityLookup,
            config.PlayerCapacity);

        var capacity = NetworkRuntimeCapacity.FromConfig(config);
        var protocol = new ProtocolVersion(config.ProtocolMajor, config.ProtocolMinor);
        var sessionEpoch = IssueSessionEpoch();
        var sessions = new AuthoritativeSessionRegistry(
            config.PlayerCapacity,
            sessionEpoch,
            protocol,
            contentFingerprint,
            checked((uint)(config.ReconnectWindowSeconds * config.SimulationTickRateHz)));
        var seatFactory = new AuthoritativeReplicationSeatRuntimeFactory(
            engine.World,
            entities,
            knowledge,
            projectors,
            config);
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

            PublishAuthoritativeComposition(engine, controllers, seatFactory, fixedInput);
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
        NetworkRuntimeStateObserver observer,
        string runtimeBaseDirectory)
    {
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
                new ClientIdentityBindingNetworkRuntimeObserver(engine, observer));
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    private static void PublishAuthoritativeComposition(
        GameEngine engine,
        AuthoritativeSeatControllerRegistry controllers,
        IAuthoritativeReplicationSeatRuntimeFactory seatFactory,
        AuthoritativeFixedInputIngress fixedInput)
    {
        if (engine.TryGetService(CoreServiceKeys.AuthoritativeSeatControllers, out _) ||
            engine.TryGetService(CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory, out _) ||
            engine.TryGetService(CoreServiceKeys.AuthoritativeFixedInputIngress, out _))
        {
            throw new InvalidOperationException(
                "Authoritative network composition services are already owned by another composition root.");
        }

        engine.SetService(CoreServiceKeys.AuthoritativeSeatControllers, controllers);
        engine.SetService(CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory, seatFactory);
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
