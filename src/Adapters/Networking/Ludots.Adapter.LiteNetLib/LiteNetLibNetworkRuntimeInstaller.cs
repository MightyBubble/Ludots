using System.Buffers.Binary;
using System.Security.Cryptography;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Hosting;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace Ludots.Adapter.LiteNetLib;

public static class LiteNetLibNetworkRuntimeInstaller
{
    private const int SessionEpochIssueAttempts = 16;

    public static void Install(in GameBootstrapResult bootstrap, string runtimeBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeBaseDirectory);
        GameEngine engine = bootstrap.Engine ?? throw new ArgumentException("Bootstrap engine is required.", nameof(bootstrap));
        NetworkRuntimeConfig config = bootstrap.Config.Networking ??
            throw new InvalidOperationException("Network runtime installation requires merged networking configuration.");
        NetworkHostBootstrapConfig host = bootstrap.NetworkHost ??
            throw new InvalidOperationException("Network runtime installation requires explicit host configuration.");
        config.Validate();
        host.Validate();

        NetworkProcessRole role = host.ResolveRole();
        var protocol = new ProtocolVersion(config.ProtocolMajor, config.ProtocolMinor);
        ResolvedModLoadPlan modPlan = Require(engine, CoreServiceKeys.ModLoadPlan);
        ContentFingerprint contentFingerprint = LiteNetLibContentFingerprintComposer.Compose(
            engine,
            modPlan,
            bootstrap.AssetsRoot,
            protocol);
        var projectors = new ReplicationSchemaProjectorRegistry(config.ReplicationSchemaCapacity);
        var appliers = new ClientReplicationSchemaApplierRegistry(config.ReplicationSchemaCapacity);
        var observer = new NetworkRuntimeStateObserver(
            config.PlayerCapacity,
            config.CommandSequenceHistoryCapacity,
            config.MaxActorsPerCommandBatch);
        if (engine.TryGetService(
                CoreServiceKeys.NetworkFaultInjectionMetrics,
                out INetworkFaultInjectionMetricsPort _))
        {
            throw new InvalidOperationException(
                "Network fault injection metrics were already installed for this engine.");
        }

        engine.SetService(CoreServiceKeys.ReplicationSchemaProjectors, projectors);
        engine.SetService(CoreServiceKeys.ClientReplicationSchemaAppliers, appliers);
        engine.SetService(CoreServiceKeys.NetworkRuntimeStateObserver, observer);
        engine.SetService(CoreServiceKeys.NetworkContentFingerprint, contentFingerprint);

        string baseDirectory = Path.GetFullPath(runtimeBaseDirectory);
        var deferred = new DeferredNetworkRuntimePort(
            role,
            () => role == NetworkProcessRole.AuthoritativeServer
                ? ComposeServer(engine, config, host, contentFingerprint, projectors, observer)
                : ComposeClient(engine, config, host, contentFingerprint, appliers, observer, baseDirectory));
        engine.ConfigureNetworkRuntime(role, deferred);
        engine.SetService(CoreServiceKeys.NetworkFaultInjectionMetrics, (INetworkFaultInjectionMetricsPort)deferred);
    }

    private static DeferredNetworkRuntimeComposition ComposeServer(
        GameEngine engine,
        NetworkRuntimeConfig config,
        NetworkHostBootstrapConfig host,
        ContentFingerprint contentFingerprint,
        ReplicationSchemaProjectorRegistry projectors,
        NetworkRuntimeStateObserver observer)
    {
        if (projectors.Count == 0)
        {
            throw new InvalidOperationException("Authoritative launch registered no replication schema projectors.");
        }

        projectors.Freeze();
        NetworkEntityTable entities = Require(engine, CoreServiceKeys.NetworkEntityTable);
        KnowledgeProjectionStore knowledge = Require(engine, CoreServiceKeys.KnowledgeProjectionStore);
        NetworkCommandIngress commands = Require(engine, CoreServiceKeys.NetworkCommandIngress);
        NetworkGameplayCommandGate gameplayCommandGate = Require(engine, CoreServiceKeys.NetworkGameplayCommandGate);
        NetworkCommandAdmissionResultBuffer admissions = Require(engine, CoreServiceKeys.NetworkCommandAdmissionResults);
        OrderAdmissionResultBuffer entityAdmissions = Require(engine, CoreServiceKeys.EntityOrderAdmissionResults);
        var mapSession = engine.CurrentMapSession ??
            throw new InvalidOperationException("Authoritative networking requires the startup map before accepting connections.");
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
            checked((uint)(config.ReconnectWindowSeconds * config.SimulationTickRateHz)),
            checked((uint)config.ReadyCountdownTicks));
        var seatFactory = new AuthoritativeReplicationSeatRuntimeFactory(
            engine.World,
            entities,
            knowledge,
            controllers,
            projectors,
            config);
        LiteNetLibServerDatagramPort transport = LiteNetLibTransportFactory.CreateServer(
            config,
            host,
            host.Port,
            host.ConnectionKey);
        try
        {
            var runtime = new AuthoritativeServerNetworkRuntime(
                in capacity,
                transport,
                transport,
                transport,
                sessions,
                commands,
                gameplayCommandGate,
                admissions,
                entityAdmissions,
                controllers,
                entities,
                seatFactory.CreateAll(),
                observer);
            return new DeferredNetworkRuntimeComposition(
                runtime,
                transport,
                () =>
                {
                    if (engine.TryGetService(
                            CoreServiceKeys.AuthoritativeSeatControllers,
                            out AuthoritativeSeatControllerRegistry _))
                    {
                        throw new InvalidOperationException(
                            "Authoritative seat controllers were already published before network activation.");
                    }

                    engine.SetService(CoreServiceKeys.AuthoritativeSeatControllers, controllers);
                });
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    private static DeferredNetworkRuntimeComposition ComposeClient(
        GameEngine engine,
        NetworkRuntimeConfig config,
        NetworkHostBootstrapConfig host,
        ContentFingerprint contentFingerprint,
        ClientReplicationSchemaApplierRegistry appliers,
        NetworkRuntimeStateObserver observer,
        string runtimeBaseDirectory)
    {
        if (appliers.Count == 0)
        {
            throw new InvalidOperationException("Replicated client launch registered no replication schema appliers.");
        }

        appliers.Freeze();
        var capacity = NetworkRuntimeCapacity.FromConfig(config);
        var protocol = new ProtocolVersion(config.ProtocolMajor, config.ProtocolMinor);
        string credentialPath = Path.IsPathRooted(host.CredentialPath)
            ? Path.GetFullPath(host.CredentialPath)
            : Path.GetFullPath(Path.Combine(runtimeBaseDirectory, host.CredentialPath));
        LiteNetLibClientDatagramPort transport = LiteNetLibTransportFactory.CreateClient(
            config,
            host,
            host.Host,
            host.Port,
            host.ConnectionKey);
        try
        {
            NetworkCommandSchemaRegistry commandSchemas = Require(engine, CoreServiceKeys.NetworkCommandSchemaRegistry);
            KnowledgeProjectionStore knowledge = Require(engine, CoreServiceKeys.KnowledgeProjectionStore);
            var runtime = new ReplicatedClientNetworkRuntime(
                in capacity,
                transport,
                transport,
                transport,
                config.ClientReconnectRetryMilliseconds / 1000f,
                config.ReconnectWindowSeconds,
                protocol,
                contentFingerprint,
                new AtomicFileClientSessionCredentialPort(credentialPath),
                new ClientReplicationBridgeFactory(
                    engine.World,
                    config.NetworkEntityCapacity,
                    appliers,
                    Require(engine, CoreServiceKeys.SpatialPartitionMembership),
                    knowledge,
                    () => engine.GetService(CoreServiceKeys.LocalPlayerEntity)),
                new ClientIdentityBindingNetworkRuntimeObserver(engine, observer));
            var commandPort = new ReplicatedClientCommandPort(
                engine.World,
                runtime,
                commandSchemas,
                config.MaxActorsPerCommandBatch);
            return new DeferredNetworkRuntimeComposition(
                runtime,
                transport,
                () =>
                {
                    if (engine.TryGetService(
                            CoreServiceKeys.ReplicatedClientCommandPort,
                            out IReplicatedClientCommandPort _) ||
                        engine.TryGetService(
                            CoreServiceKeys.ReplicatedClientRoomControlPort,
                            out IReplicatedClientRoomControlPort _))
                    {
                        throw new InvalidOperationException(
                            "Replicated-client role services were already published before network activation.");
                    }

                    engine.SetService(CoreServiceKeys.ReplicatedClientCommandPort, commandPort);
                    engine.SetService(
                        CoreServiceKeys.ReplicatedClientRoomControlPort,
                        (IReplicatedClientRoomControlPort)runtime);
                });
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    private static T Require<T>(GameEngine engine, ServiceKey<T> key)
        where T : class
    {
        return engine.GetService(key) ??
            throw new InvalidOperationException($"Network composition requires service '{key.Name}'.");
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
