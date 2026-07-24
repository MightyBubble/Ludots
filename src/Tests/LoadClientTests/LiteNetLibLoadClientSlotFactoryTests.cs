using Arch.Core;
using Ludots.Adapter.LiteNetLib;
using Ludots.App.LoadClients;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Input;
using NUnit.Framework;

namespace Ludots.Tests.LoadClients;

[TestFixture]
public sealed class LiteNetLibLoadClientSlotFactoryTests
{
    private string _credentialDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _credentialDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(LiteNetLibLoadClientSlotFactoryTests),
            Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_credentialDirectory))
        {
            Directory.Delete(_credentialDirectory, recursive: true);
        }
    }

    [Test]
    public void Create_MultipleClients_UseDistinctCredentialFilesAndDistinctBoundPorts()
    {
        LoadClientHostConfig config = LoadClientHostConfig.ParseJson(
            LoadClientHostConfigTests.CreateValidJson(clientCount: 3));
        var factory = new LiteNetLibLoadClientSlotFactory();
        LoadClientSlot[] slots = new LoadClientSlot[3];
        try
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = factory.Create(i, config, _credentialDirectory);
            }

            Assert.Multiple(() =>
            {
                Assert.That(slots[0].BoundPort, Is.Not.EqualTo(slots[1].BoundPort));
                Assert.That(slots[0].BoundPort, Is.Not.EqualTo(slots[2].BoundPort));
                Assert.That(slots[1].BoundPort, Is.Not.EqualTo(slots[2].BoundPort));
                Assert.That(slots[0].CredentialPath, Is.Not.EqualTo(slots[1].CredentialPath));
                Assert.That(slots[0].Clock.SimulationTickRateHz, Is.EqualTo(30));
                Assert.That(slots[0].Clock.PayloadBytes, Is.EqualTo(Physics3DFixedInputFrameCodec.PayloadBytes));
                Assert.That(File.Exists(slots[0].CredentialPath), Is.False); // empty until store
            });

            // Prove credential ports are distinct atomic-file adapters by storing unique epochs.
            var first = new AtomicFileClientSessionCredentialPort(slots[0].CredentialPath);
            var second = new AtomicFileClientSessionCredentialPort(slots[1].CredentialPath);
            Assert.That(
                first.TryStore(new ClientSessionCredentials(new SessionEpoch(11), new ReconnectToken(1, 2))),
                Is.True);
            Assert.That(
                second.TryStore(new ClientSessionCredentials(new SessionEpoch(22), new ReconnectToken(3, 4))),
                Is.True);
            Assert.That(File.Exists(slots[0].CredentialPath), Is.True);
            Assert.That(File.Exists(slots[1].CredentialPath), Is.True);
            Assert.That(
                File.ReadAllBytes(slots[0].CredentialPath),
                Is.Not.EqualTo(File.ReadAllBytes(slots[1].CredentialPath)));
        }
        finally
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i]?.Dispose();
            }
        }
    }

    [Test]
    public void CreateFrozenAppliers_RegistersOnlyFormalPhysics3DHeadlessSchema()
    {
        LoadClientHostConfig config = LoadClientHostConfig.ParseJson(
            LoadClientHostConfigTests.CreateValidJson());

        ClientReplicationSchemaApplierRegistry appliers =
            LiteNetLibLoadClientSlotFactory.CreateFrozenAppliers(config.Physics3DReplication);
        using World world = World.Create();
        var factory = new ClientReplicationBridgeFactory(
            world,
            config.Networking.GlobalNetworkEntityCapacity,
            config.Networking.ReplicationEntityCapacityPerSeat,
            appliers);
        var clientSeat = new SessionSeatBinding(0, 1, new PlayerId(1));
        ClientWorldReplicationBridge bridge = factory.Create(in clientSeat, sessionEpoch: 42);

        Assert.Multiple(() =>
        {
            Assert.That(appliers.IsFrozen, Is.True);
            Assert.That(
                appliers.TryGet(config.Physics3DReplication.SchemaId, out IClientReplicationSchemaApplier applier),
                Is.True);
            Assert.That(applier, Is.TypeOf<Physics3DHeadlessReplicationApplier>());
            Assert.That(appliers.TryGet(config.Physics3DReplication.SchemaId + 1, out _), Is.False);
            Assert.That(factory.GlobalEntityCapacity, Is.EqualTo(config.Networking.GlobalNetworkEntityCapacity));
            Assert.That(
                factory.ReplicationEntityCapacityPerSeat,
                Is.EqualTo(config.Networking.ReplicationEntityCapacityPerSeat));
            Assert.That(bridge.GlobalEntityCapacity, Is.EqualTo(config.Networking.GlobalNetworkEntityCapacity));
            Assert.That(
                bridge.ActiveMirrorCapacity,
                Is.EqualTo(config.Networking.ReplicationEntityCapacityPerSeat));
            Assert.That(bridge.SessionEpoch, Is.EqualTo(42));
        });
    }

    [Test]
    public void Factory_ProductionScaleCapacities_CreateOneHundredFortyNineBridgesWithoutGlobalSizedArrays()
    {
        LoadClientHostConfig config = LoadClientHostConfig.ParseJson(
            LoadClientHostConfigTests.CreateValidJson()
                .Replace("\"globalNetworkEntityCapacity\": 1024", "\"globalNetworkEntityCapacity\": 100000", StringComparison.Ordinal)
                .Replace("\"replicationEntityCapacityPerSeat\": 64", "\"replicationEntityCapacityPerSeat\": 512", StringComparison.Ordinal)
                .Replace("\"disclosureChangeLogCapacity\": 256", "\"disclosureChangeLogCapacity\": 1024", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(config.Networking.GlobalNetworkEntityCapacity, Is.EqualTo(100_000));
            Assert.That(config.Networking.ReplicationEntityCapacityPerSeat, Is.EqualTo(512));
        });

        ClientReplicationSchemaApplierRegistry appliers =
            LiteNetLibLoadClientSlotFactory.CreateFrozenAppliers(config.Physics3DReplication);
        const int bridgeCount = 149;
        const long allocationCeilingBytes = 256L * 1024L * 1024L;
        var worlds = new World[bridgeCount];
        var bridges = new ClientWorldReplicationBridge[bridgeCount];
        var clientSeat = new SessionSeatBinding(0, 1, new PlayerId(1));

        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < bridgeCount; i++)
        {
            worlds[i] = World.Create();
            var factory = new ClientReplicationBridgeFactory(
                worlds[i],
                config.Networking.GlobalNetworkEntityCapacity,
                config.Networking.ReplicationEntityCapacityPerSeat,
                appliers);
            Assert.That(factory.GlobalEntityCapacity, Is.EqualTo(100_000));
            Assert.That(factory.ReplicationEntityCapacityPerSeat, Is.EqualTo(512));
            bridges[i] = factory.Create(in clientSeat, sessionEpoch: 7);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine(
            $"LoadClient measured allocation for {bridgeCount} bridges at global=100000, active=512: {allocated} bytes");
        Assert.That(allocated, Is.GreaterThan(0));
        Assert.That(allocated, Is.LessThan(allocationCeilingBytes));
        Assert.That(bridges[0].GlobalEntityCapacity, Is.EqualTo(100_000));
        Assert.That(bridges[0].ActiveMirrorCapacity, Is.EqualTo(512));
        Assert.That(bridges[bridgeCount - 1].ActiveMirrorCapacity, Is.EqualTo(512));

        for (int i = 0; i < bridgeCount; i++)
        {
            worlds[i].Dispose();
        }
    }
}
