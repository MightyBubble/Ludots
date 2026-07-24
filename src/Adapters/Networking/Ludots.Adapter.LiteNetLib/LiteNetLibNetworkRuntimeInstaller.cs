using System.Buffers.Binary;
using System.Security.Cryptography;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
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
        GameEngine engine = bootstrap.Engine ?? throw new ArgumentException("Bootstrap engine is required.", nameof(bootstrap));
        NetworkRuntimeConfig config = bootstrap.Config.Networking ??
            throw new InvalidOperationException("Network runtime installation requires merged networking configuration.");
        NetworkHostBootstrapConfig host = bootstrap.NetworkHost ??
            throw new InvalidOperationException("Network runtime installation requires explicit host configuration.");
        config.Validate();
        host.Validate();

        if (!ContentFingerprint.TryParseHex(bootstrap.PlanFingerprint, out ContentFingerprint contentFingerprint) ||
            contentFingerprint.IsEmpty)
        {
            throw new InvalidOperationException(
                "Networked launch requires a non-empty 64-character launcher plan fingerprint.");
        }

        NetworkProcessRole role = host.ResolveRole();
        var projectors = new ReplicationSchemaProjectorRegistry(config.ReplicationSchemaCapacity);
        var appliers = new ClientReplicationSchemaApplierRegistry(config.ReplicationSchemaCapacity);
        var observer = new NetworkRuntimeStateObserver(config.PlayerCapacity);
        engine.SetService(CoreServiceKeys.ReplicationSchemaProjectors, projectors);
        engine.SetService(CoreServiceKeys.ClientReplicationSchemaAppliers, appliers);
        engine.SetService(CoreServiceKeys.NetworkRuntimeStateObserver, observer);

        string baseDirectory = Path.GetFullPath(runtimeBaseDirectory);
        var deferred = new DeferredNetworkRuntimePort(
            role,
            () => role == NetworkProcessRole.AuthoritativeServer
                ? ComposeServer(engine, config, host, contentFingerprint, projectors, observer)
                : ComposeClient(engine, config, host, contentFingerprint, appliers, observer, baseDirectory));
        engine.ConfigureNetworkRuntime(role, deferred);
    }

    private static INetworkRuntimePort ComposeServer(
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
        var mapSession = engine.CurrentMapSession ??
            throw new InvalidOperationException("Authoritative networking requires the startup map before accepting connections.");
        var controllers = new AuthoritativeSeatControllerRegistry(
            engine.World,
            mapSession.PlayerEntityLookup,
            config.PlayerCapacity);
        engine.SetService(CoreServiceKeys.AuthoritativeSeatControllers, controllers);
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
            host.Port,
            host.ConnectionKey);
        return new AuthoritativeServerNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            sessions,
            commands,
            gameplayCommandGate,
            admissions,
            controllers,
            entities,
            seatFactory.CreateAll(),
            observer);
    }

    private static INetworkRuntimePort ComposeClient(
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
        NetworkCommandAdmissionResultBuffer admissions = Require(engine, CoreServiceKeys.NetworkCommandAdmissionResults);
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
        NetworkCommandSchemaRegistry commandSchemas = Require(engine, CoreServiceKeys.NetworkCommandSchemaRegistry);
        var runtime = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            config.ClientReconnectRetryMilliseconds / 1000f,
            protocol,
            contentFingerprint,
            new AtomicFileClientSessionCredentialPort(credentialPath),
            new ClientReplicationBridgeFactory(engine.World, config.NetworkEntityCapacity, appliers),
            admissions,
            new ClientIdentityBindingNetworkRuntimeObserver(engine, observer));
        engine.SetService(
            CoreServiceKeys.ReplicatedClientCommandPort,
            new ReplicatedClientCommandPort(
                engine.World,
                runtime,
                commandSchemas,
                config.MaxActorsPerCommandBatch));
        engine.SetService(CoreServiceKeys.ReplicatedClientRoomControlPort, (IReplicatedClientRoomControlPort)runtime);
        return runtime;
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
