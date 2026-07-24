using Arch.Core;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.App.LoadClients;

/// <summary>
/// Production slot factory: one AtomicFile credential path and one real LiteNetLib UDP endpoint per client.
/// </summary>
public sealed class LiteNetLibLoadClientSlotFactory : ILoadClientSlotFactory
{
    public LoadClientSlot Create(int clientIndex, LoadClientHostConfig config, string credentialDirectory)
    {
        if (clientIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientIndex));
        }

        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialDirectory);

        string credentialPath = Path.Combine(
            credentialDirectory,
            FormattableString.Invariant($"load-client-{clientIndex:D4}.cred"));
        NetworkRuntimeConfig networking = config.Networking;
        NetworkRuntimeCapacity capacity = NetworkRuntimeCapacity.FromConfig(networking);
        var protocol = new ProtocolVersion(networking.ProtocolMajor, networking.ProtocolMinor);
        World world = World.Create();
        LiteNetLibClientDatagramPort? transport = null;
        ReplicatedClientNetworkRuntime? runtime = null;
        try
        {
            ClientReplicationSchemaApplierRegistry appliers = CreateFrozenAppliers(config.ReplicationSchemaIds);
            var credentials = new AtomicFileClientSessionCredentialPort(credentialPath);
            transport = LiteNetLibTransportFactory.CreateClient(
                networking,
                config.Host,
                config.Port,
                config.ConnectionKey);
            int boundPort = transport.BoundPort;
            if (boundPort <= 0 || boundPort > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"LiteNetLib client {clientIndex} bound an invalid local UDP port {boundPort}.");
            }

            var observer = new LoadClientNetworkObserver();
            var admissions = new NetworkCommandAdmissionResultBuffer(
                Math.Max(1, networking.NetworkAdmissionResultCapacity));
            runtime = new ReplicatedClientNetworkRuntime(
                in capacity,
                NetworkTransportPortOwnership.Owned,
                transport,
                transport,
                transport,
                reconnectRetrySeconds: config.ClientReconnectRetryMilliseconds / 1000f,
                protocol,
                config.PlanFingerprint,
                credentials,
                new LoadClientReplicationBridgeFactory(
                    world,
                    capacity.GlobalEntityCapacity,
                    appliers),
                admissions,
                observer);
            transport = null; // ownership transferred to runtime

            var clock = new ReplicatedClientFixedInputClock(
                runtime,
                new DeterministicFixedInputPayloadSource(clientIndex),
                config.SimulationTickRateHz,
                capacity.FixedInputFramePayloadBytes,
                config.FixedInputLeadTicks,
                capacity.FixedInputMaxFutureTicks,
                config.MaxStepsPerAdvance,
                config.MaxAccumulatedSteps);

            return new LoadClientSlot(
                clientIndex,
                boundPort,
                credentialPath,
                world,
                runtime,
                clock,
                observer);
        }
        catch
        {
            runtime?.Dispose();
            transport?.Dispose();
            world.Dispose();
            throw;
        }
    }

    internal static ClientReplicationSchemaApplierRegistry CreateFrozenAppliers(int[] schemaIds)
    {
        int capacity = Math.Max(1, schemaIds.Length);
        var appliers = new ClientReplicationSchemaApplierRegistry(capacity);
        var shared = new LoadClientMirrorSchemaApplier();
        for (int i = 0; i < schemaIds.Length; i++)
        {
            ReplicationSchemaRegistrationResult registered = appliers.Register(schemaIds[i], shared);
            if (registered != ReplicationSchemaRegistrationResult.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to register load-client replication schema id {schemaIds[i]}: {registered}.");
            }
        }

        appliers.Freeze();
        return appliers;
    }
}
