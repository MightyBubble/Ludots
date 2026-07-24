using Arch.Core;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Input;

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
            ClientReplicationSchemaApplierRegistry appliers = CreateFrozenAppliers(config.Physics3DReplication);
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
                reconnectRetrySeconds: networking.ClientReconnectRetryMilliseconds / 1000f,
                protocol,
                config.PlanFingerprint,
                credentials,
                new ClientReplicationBridgeFactory(
                    world,
                    capacity.GlobalEntityCapacity,
                    capacity.ReplicationEntityCapacityPerSeat,
                    appliers),
                admissions,
                observer);
            transport = null; // ownership transferred to runtime

            var clock = new ReplicatedClientFixedInputClock(
                runtime,
                new Physics3DLoadClientFixedInputPayloadSource(config.MovementInput.Value),
                networking.SimulationTickRateHz,
                capacity.FixedInputFramePayloadBytes,
                networking.FixedInputLeadTicks,
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

    internal static ClientReplicationSchemaApplierRegistry CreateFrozenAppliers(
        Physics3DReplicationSchemaConfig replication)
    {
        ArgumentNullException.ThrowIfNull(replication);
        var appliers = new ClientReplicationSchemaApplierRegistry(replication.SchemaId);
        var applier = new Physics3DHeadlessReplicationApplier(
            replication.SchemaId,
            replication.Quantization);
        ReplicationSchemaRegistrationResult registered = appliers.Register(replication.SchemaId, applier);
        if (registered != ReplicationSchemaRegistrationResult.Success)
        {
            throw new InvalidOperationException(
                $"Failed to register Physics3D load-client replication schema id {replication.SchemaId}: {registered}.");
        }

        appliers.Freeze();
        return appliers;
    }
}
