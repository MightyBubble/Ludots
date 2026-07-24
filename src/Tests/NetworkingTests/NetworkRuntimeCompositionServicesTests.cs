using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkRuntimeCompositionServicesTests
{
    [Test]
    public void SeatFactory_AcquiresLazily_RejectsDuplicateAndReleasesExactGeneration()
    {
        using World world = World.Create();
        Entity viewer = world.Create(new PlayerIdentity { PlayerId = 1 });
        NetworkRuntimeConfig config = CreateConfig();
        var entities = new NetworkEntityTable(config.GlobalNetworkEntityCapacity);
        var projectors = new ReplicationSchemaProjectorRegistry(config.ReplicationSchemaCapacity);
        projectors.Freeze();
        var factory = new AuthoritativeReplicationSeatRuntimeFactory(
            world,
            entities,
            new KnowledgeProjectionStore(initialCapacity: 8),
            projectors,
            config);
        var firstGeneration = new SessionSeatBinding(0, 1, new PlayerId(1));
        var secondGeneration = new SessionSeatBinding(0, 2, new PlayerId(1));

        Assert.That(factory.TryAcquire(in firstGeneration, viewer, out var first), Is.True);
        Assert.That(first, Is.Not.Null);
        Assert.That(factory.TryAcquire(in firstGeneration, viewer, out _), Is.False);
        Assert.That(factory.TryRelease(in secondGeneration, first!), Is.False);
        Assert.That(factory.TryRelease(in firstGeneration, first!), Is.True);
        Assert.That(factory.TryAcquire(in secondGeneration, viewer, out var second), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(factory.SeatCapacity, Is.EqualTo(config.PlayerCapacity));
            Assert.That(factory.GlobalEntityCapacity, Is.EqualTo(config.GlobalNetworkEntityCapacity));
            Assert.That(factory.ReplicationEntityCapacityPerSeat, Is.EqualTo(config.ReplicationEntityCapacityPerSeat));
        });
    }

    [Test]
    public void SeatControllerRegistry_RequiresCompletePlayerRepresentatives()
    {
        using World world = World.Create();
        Entity first = world.Create(new PlayerIdentity { PlayerId = 1 });
        var incomplete = new PlayerEntityLookup();
        incomplete.Register(1, first);

        Assert.That(
            () => new AuthoritativeSeatControllerRegistry(world, incomplete, seatCapacity: 2),
            Throws.InvalidOperationException.With.Message.Contains("player 2"));

        Entity second = world.Create(new PlayerIdentity { PlayerId = 2 });
        incomplete.Register(2, second);
        var registry = new AuthoritativeSeatControllerRegistry(world, incomplete, seatCapacity: 2);
        var seat = new SessionSeatBinding(1, 3, new PlayerId(2));

        Assert.That(registry.TryResolveController(in seat, out Entity resolved), Is.True);
        Assert.That(resolved, Is.EqualTo(second));
    }

    [Test]
    public void RuntimeObserver_TracksReconnectWindowWithoutAcceptingStaleEvents()
    {
        var observer = new NetworkRuntimeStateObserver(seatCapacity: 2);
        var seat = new SessionSeatBinding(0, 4, new PlayerId(1));
        var stale = new SessionSeatBinding(0, 3, new PlayerId(1));

        observer.OnServerSeatConnected(in seat, reconnected: false);
        Assert.That(observer.GetSeatState(0), Is.EqualTo(NetworkSeatConnectionState.Connected));
        observer.OnServerSeatDisconnected(in seat, TransportDisconnectReason.Timeout);
        Assert.That(observer.GetSeatState(0), Is.EqualTo(NetworkSeatConnectionState.AwaitingReconnect));
        Assert.That(
            () => observer.OnServerSeatReleased(in stale),
            Throws.InvalidOperationException.With.Message.Contains("stale"));
        observer.OnServerSeatReleased(in seat);

        Assert.Multiple(() =>
        {
            Assert.That(observer.GetSeatState(0), Is.EqualTo(NetworkSeatConnectionState.Empty));
            Assert.That(observer.TryGetSeatBinding(0, out _), Is.False);
        });
    }

    private static NetworkRuntimeConfig CreateConfig() => new()
    {
        ProfileId = "composition_services_test",
        ReferenceTransport = "LiteNetLib/2.1.4",
        ProtocolMajor = 1,
        ProtocolMinor = 0,
        PlayerCapacity = 2,
        SimulationTickRateHz = 30,
        StatePublishRateHz = 10,
        GlobalNetworkEntityCapacity = 32,
        ReplicationEntityCapacityPerSeat = 8,
        OrderQueueCapacity = 32,
        MaxCommandBatchesPerSecondPerPlayer = 8,
        CommandBurstBatchCapacity = 8,
        MaxActorsPerCommandBatch = 4,
        CommandSequenceHistoryCapacity = 32,
        MaxPastTargetTicks = 2,
        MaxFutureTargetTicks = 4,
        NetworkAdmissionResultCapacity = 32,
        EntityAdmissionResultCapacity = 32,
        ReconnectWindowSeconds = 30,
        ClientReconnectRetryMilliseconds = 500,
        ReplicationSchemaCapacity = 8,
        BaselineCapacity = 8,
        DisclosureChangeLogCapacity = 32,
        DatagramQueueCapacity = 64,
        ConnectionEventCapacity = 16,
        MaxDatagramPayloadBytes = 1200,
        TransportMaxConnectAttempts = 10,
        TransportDisconnectTimeoutMilliseconds = 5_000,
        ReliableDisconnectFlushTimeoutMilliseconds = 4_000,
        TransportChannelCount = 4,
        ControlChannelId = 0,
        CommandChannelId = 1,
        StateChannelId = 2,
        InputChannelId = 3,
        FixedInputHistoryTicksPerSeat = 8,
        FixedInputSchemaId = 1,
        FixedInputFramePayloadBytes = 12,
        FixedInputMaxFutureTicks = 4,
        FixedInputMaxFramesPerBatch = 4,
        FixedInputPendingFrameCapacity = 8,
        SnapshotChunkCapacity = 64,
        MaxServerOutboundBytesPerSecondPerClient = 256 * 1024,
        TickP95BudgetMicroseconds = 26_700,
        TickP99BudgetMicroseconds = 31_000,
        CommandSchemas =
        {
            new NetworkCommandSchemaConfig
            {
                OrderTypeKey = "moveTo",
                TargetKind = NetworkCommandTargetKind.WorldPositionCm,
                SubmitMode = OrderSubmitMode.Queued,
            },
        },
        NormalConnection = new NetworkFaultProfileConfig(),
        UnstableConnection = new NetworkFaultProfileConfig(),
    };
}
