using System.Diagnostics.CodeAnalysis;
using Arch.Core;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class LiteNetLibNetworkRuntimeInstallerTests
{
    [Test]
    public void AuthoritativeComposition_MissingResolverOrFactoryFailsExplicitly()
    {
        var engine = new GameEngine();

        Assert.That(
            () => Resolve(engine),
            Throws.InvalidOperationException.With.Message.Contains(
                CoreServiceKeys.AuthoritativeSeatControllerResolver.Name));

        engine.SetService(
            CoreServiceKeys.AuthoritativeSeatControllerResolver,
            (IAuthoritativeSeatControllerResolver)new StubControllerResolver());

        Assert.That(
            () => Resolve(engine),
            Throws.InvalidOperationException.With.Message.Contains(
                CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory.Name));
    }

    [Test]
    public void AuthoritativeComposition_UsesProvidedResolverAndFactory()
    {
        var engine = new GameEngine();
        var expectedResolver = new StubControllerResolver();
        var expectedFactory = new StubSeatFactory(
            seatCapacity: 150,
            globalEntityCapacity: 25_000,
            replicationEntityCapacityPerSeat: 2_048);
        engine.SetService(
            CoreServiceKeys.AuthoritativeSeatControllerResolver,
            (IAuthoritativeSeatControllerResolver)expectedResolver);
        engine.SetService(
            CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory,
            (IAuthoritativeReplicationSeatRuntimeFactory)expectedFactory);

        LiteNetLibNetworkRuntimeInstaller.ResolveAuthoritativeComposition(
            engine,
            seatCapacity: 150,
            globalEntityCapacity: 25_000,
            replicationEntityCapacityPerSeat: 2_048,
            out IAuthoritativeSeatControllerResolver actualResolver,
            out IAuthoritativeReplicationSeatRuntimeFactory actualFactory);

        Assert.Multiple(() =>
        {
            Assert.That(actualResolver, Is.SameAs(expectedResolver));
            Assert.That(actualFactory, Is.SameAs(expectedFactory));
        });
    }

    [TestCase(149, 25_000, 2_048)]
    [TestCase(150, 24_999, 2_048)]
    [TestCase(150, 25_000, 2_047)]
    public void AuthoritativeComposition_RejectsFactoryCapacityMismatch(
        int seatCapacity,
        int globalEntityCapacity,
        int replicationEntityCapacityPerSeat)
    {
        var engine = new GameEngine();
        engine.SetService(
            CoreServiceKeys.AuthoritativeSeatControllerResolver,
            (IAuthoritativeSeatControllerResolver)new StubControllerResolver());
        engine.SetService(
            CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory,
            (IAuthoritativeReplicationSeatRuntimeFactory)new StubSeatFactory(
                seatCapacity,
                globalEntityCapacity,
                replicationEntityCapacityPerSeat));

        Assert.That(
            () => Resolve(engine),
            Throws.InvalidOperationException.With.Message.Contains("capacities do not match"));
    }

    [Test]
    public void RuntimeObserver_MissingBridgeFailsExplicitly()
    {
        var engine = new GameEngine();
        var stateObserver = new NetworkRuntimeStateObserver(seatCapacity: 150);

        Assert.That(
            () => LiteNetLibNetworkRuntimeInstaller.ResolveNetworkRuntimeObserver(engine, stateObserver),
            Throws.InvalidOperationException.With.Message.Contains(
                CoreServiceKeys.NetworkRuntimeObserverBridge.Name));
    }

    [Test]
    public void RuntimeObserver_FansOutBridgeBeforeCommittingState()
    {
        var engine = new GameEngine();
        var bridge = new RecordingObserver();
        var stateObserver = new NetworkRuntimeStateObserver(seatCapacity: 150);
        bridge.StateObserver = stateObserver;
        engine.SetService(
            CoreServiceKeys.NetworkRuntimeObserverBridge,
            (INetworkRuntimeObserver)bridge);
        var seat = new SessionSeatBinding(slot: 0, generation: 1, playerId: new PlayerId(1));

        INetworkRuntimeObserver observer =
            LiteNetLibNetworkRuntimeInstaller.ResolveNetworkRuntimeObserver(engine, stateObserver);
        observer.OnServerSeatConnected(in seat, reconnected: false);

        Assert.Multiple(() =>
        {
            Assert.That(bridge.SeatStateObservedDuringCallback, Is.EqualTo(NetworkSeatConnectionState.Empty));
            Assert.That(stateObserver.GetSeatState(0), Is.EqualTo(NetworkSeatConnectionState.Connected));
            Assert.That(observer, Is.TypeOf<NetworkRuntimeObserverFanout>());
            var fanout = (NetworkRuntimeObserverFanout)observer;
            Assert.That(fanout.StateObserver, Is.SameAs(stateObserver));
            Assert.That(fanout.Bridge, Is.SameAs(bridge));
        });

        bridge.ThrowOnSeatEvent = true;
        Assert.That(
            () => observer.OnServerSeatDisconnected(in seat, TransportDisconnectReason.Timeout),
            Throws.InvalidOperationException.With.Message.EqualTo("bridge rejected seat event"));
        Assert.That(
            stateObserver.GetSeatState(0),
            Is.EqualTo(NetworkSeatConnectionState.Connected));
    }

    [Test]
    public void ClientComposition_RequiredServicesVisible_ThenFreeze_BeforeEndpointOpen()
    {
        using var engine = new GameEngine();
        NetworkRuntimeConfig config = CreateValidConfig();
        var appliers = new ClientReplicationSchemaApplierRegistry(config.ReplicationSchemaCapacity);
        Assert.That(
            appliers.Register(1, new StubApplier()),
            Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        var payloadSource = new StubPayloadSource();
        var admissions = new NetworkCommandAdmissionResultBuffer(capacity: 8);
        engine.SetService(CoreServiceKeys.ClientReplicationSchemaAppliers, appliers);
        engine.SetService(CoreServiceKeys.FixedInputPayloadSource, (IFixedInputPayloadSource)payloadSource);
        engine.SetService(CoreServiceKeys.NetworkCommandAdmissionResults, admissions);
        engine.SetService(
            CoreServiceKeys.NetworkRuntimeObserverBridge,
            (INetworkRuntimeObserver)new RecordingObserver());

        Assert.That(appliers.IsFrozen, Is.False);
        LiteNetLibNetworkRuntimeInstaller.ClientCompositionPlan plan =
            LiteNetLibNetworkRuntimeInstaller.ValidateClientCompositionBeforeEndpointOpen(engine, config);

        Assert.Multiple(() =>
        {
            Assert.That(appliers.IsFrozen, Is.True);
            Assert.That(plan.Appliers, Is.SameAs(appliers));
            Assert.That(plan.PayloadSource, Is.SameAs(payloadSource));
            Assert.That(plan.Admissions, Is.SameAs(admissions));
            Assert.That(plan.Capacity.SimulationTickRateHz, Is.EqualTo(config.SimulationTickRateHz));
            Assert.That(plan.Capacity.FixedInputSchemaId, Is.EqualTo((ushort)config.FixedInputSchemaId));
            Assert.That(plan.Capacity.FixedInputFramePayloadBytes, Is.EqualTo((ushort)config.FixedInputFramePayloadBytes));
        });
    }

    [Test]
    public void ClientComposition_MissingPayloadSourceFailsBeforeEndpointOpen()
    {
        using var engine = new GameEngine();
        NetworkRuntimeConfig config = CreateValidConfig();
        var appliers = new ClientReplicationSchemaApplierRegistry(config.ReplicationSchemaCapacity);
        Assert.That(
            appliers.Register(1, new StubApplier()),
            Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        engine.SetService(CoreServiceKeys.ClientReplicationSchemaAppliers, appliers);
        engine.SetService(
            CoreServiceKeys.NetworkCommandAdmissionResults,
            new NetworkCommandAdmissionResultBuffer(capacity: 8));
        engine.SetService(
            CoreServiceKeys.NetworkRuntimeObserverBridge,
            (INetworkRuntimeObserver)new RecordingObserver());

        Assert.That(
            () => LiteNetLibNetworkRuntimeInstaller.ValidateClientCompositionBeforeEndpointOpen(engine, config),
            Throws.InvalidOperationException.With.Message.Contains(
                CoreServiceKeys.FixedInputPayloadSource.Name));
        Assert.That(appliers.IsFrozen, Is.False);
    }

    [Test]
    public void ClientComposition_MissingApplierRoleFailsBeforeEndpointOpen()
    {
        using var engine = new GameEngine();
        NetworkRuntimeConfig config = CreateValidConfig();
        var appliers = new ClientReplicationSchemaApplierRegistry(config.ReplicationSchemaCapacity);
        engine.SetService(CoreServiceKeys.ClientReplicationSchemaAppliers, appliers);
        engine.SetService(CoreServiceKeys.FixedInputPayloadSource, (IFixedInputPayloadSource)new StubPayloadSource());
        engine.SetService(
            CoreServiceKeys.NetworkCommandAdmissionResults,
            new NetworkCommandAdmissionResultBuffer(capacity: 8));
        engine.SetService(
            CoreServiceKeys.NetworkRuntimeObserverBridge,
            (INetworkRuntimeObserver)new RecordingObserver());

        Assert.That(
            () => LiteNetLibNetworkRuntimeInstaller.ValidateClientCompositionBeforeEndpointOpen(engine, config),
            Throws.InvalidOperationException.With.Message.Contains("no replication schema appliers"));
        Assert.That(appliers.IsFrozen, Is.False);
    }

    [Test]
    public void Install_ClientConfigureSucceedsBeforeMapOwnedServices_AndOpensNoEndpoint()
    {
        using ClientInstallHarness harness = ClientInstallHarness.Create(registerMapOwnedServices: false);
        int transportFactoryCalls = 0;
        Assert.That(
            () => LiteNetLibNetworkRuntimeInstaller.Install(
                harness.Bootstrap,
                harness.RuntimeDirectory,
                (config, host, port, key) =>
                {
                    transportFactoryCalls++;
                    return LiteNetLibTransportFactory.CreateClient(config, host, port, key);
                }),
            Throws.Nothing);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Engine.TryGetService(
                CoreServiceKeys.NetworkRuntimePort,
                out INetworkRuntimePort port), Is.True);
            Assert.That(port, Is.InstanceOf<IReplicatedClientNetworkRuntimePort>());
            Assert.That(harness.Engine.TryGetService(
                CoreServiceKeys.ReplicatedClientNetworkRuntimePort,
                out IReplicatedClientNetworkRuntimePort composite), Is.True);
            Assert.That(ReferenceEquals(composite, port), Is.True);
            Assert.That(harness.Engine.TryGetService(
                CoreServiceKeys.NetworkRuntimeStateObserver,
                out NetworkRuntimeStateObserver _), Is.True);
            Assert.That(harness.Engine.TryGetService(
                CoreServiceKeys.ReplicatedClientFixedInputClock,
                out ReplicatedClientFixedInputClock _), Is.False);
            Assert.That(harness.Engine.TryGetService(
                CoreServiceKeys.FixedInputPayloadSource,
                out IFixedInputPayloadSource _), Is.False);
            Assert.That(transportFactoryCalls, Is.Zero);
        });
    }

    [Test]
    public void FirstPumpTransport_AfterMapLoadedRegistration_FreezesOpensEndpointPublishesClock()
    {
        using ClientInstallHarness harness = ClientInstallHarness.Create(registerMapOwnedServices: false);
        int transportFactoryCalls = 0;
        LiteNetLibNetworkRuntimeInstaller.Install(
            harness.Bootstrap,
            harness.RuntimeDirectory,
            (config, host, port, key) =>
            {
                transportFactoryCalls++;
                return LiteNetLibTransportFactory.CreateClient(config, host, port, key);
            });
        Assert.That(transportFactoryCalls, Is.Zero);
        Assert.That(
            harness.Engine.TryGetService(CoreServiceKeys.ReplicatedClientFixedInputClock, out _),
            Is.False);

        harness.RegisterMapOwnedClientServices();
        INetworkRuntimePort port = harness.Engine.GetService(CoreServiceKeys.NetworkRuntimePort);
        port.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(transportFactoryCalls, Is.EqualTo(1));
            Assert.That(harness.Appliers!.IsFrozen, Is.True);
            Assert.That(
                harness.Engine.TryGetService(
                    CoreServiceKeys.ReplicatedClientFixedInputClock,
                    out ReplicatedClientFixedInputClock clock),
                Is.True);
            Assert.That(clock, Is.Not.Null);
            Assert.That(
                harness.Engine.TryGetService(
                    CoreServiceKeys.ReplicatedClientNetworkRuntimePort,
                    out IReplicatedClientNetworkRuntimePort composite),
                Is.True);
            Assert.That(ReferenceEquals(composite, port), Is.True);
        });

        port.PumpReplicatedClient(1f / 30f);
        ReplicatedClientFixedInputClock published =
            harness.Engine.GetService(CoreServiceKeys.ReplicatedClientFixedInputClock);
        ReplicatedClientFixedInputClockAdvanceResult advance = published.Advance(1f / 30f);
        Assert.That(advance.IsSuccess, Is.True);
    }

    [TestCase("applier")]
    [TestCase("source")]
    [TestCase("observer")]
    public void FirstPumpTransport_MissingMapOwnedDependency_FailsBeforeEndpointFactory_AndLeavesNoClock(
        string missing)
    {
        using ClientInstallHarness harness = ClientInstallHarness.Create(registerMapOwnedServices: false);
        int transportFactoryCalls = 0;
        LiteNetLibNetworkRuntimeInstaller.Install(
            harness.Bootstrap,
            harness.RuntimeDirectory,
            (config, host, port, key) =>
            {
                transportFactoryCalls++;
                return LiteNetLibTransportFactory.CreateClient(config, host, port, key);
            });
        harness.RegisterMapOwnedClientServices(omit: missing);

        INetworkRuntimePort port = harness.Engine.GetService(CoreServiceKeys.NetworkRuntimePort);
        Assert.That(port.PumpTransport, Throws.InvalidOperationException);
        Assert.Multiple(() =>
        {
            Assert.That(transportFactoryCalls, Is.Zero);
            Assert.That(
                harness.Engine.TryGetService(CoreServiceKeys.ReplicatedClientFixedInputClock, out _),
                Is.False);
            if (harness.Appliers != null)
            {
                Assert.That(harness.Appliers.IsFrozen, Is.False);
            }
        });
    }

    private static void Resolve(GameEngine engine)
    {
        LiteNetLibNetworkRuntimeInstaller.ResolveAuthoritativeComposition(
            engine,
            seatCapacity: 150,
            globalEntityCapacity: 25_000,
            replicationEntityCapacityPerSeat: 2_048,
            out _,
            out _);
    }

    private static NetworkRuntimeConfig CreateValidConfig() => new()
    {
        ProfileId = "installer-client-validation",
        ReferenceTransport = LiteNetLibTransportFactory.TransportIdentity,
        ProtocolMajor = 1,
        ProtocolMinor = 0,
        PlayerCapacity = 2,
        SimulationTickRateHz = 30,
        StatePublishRateHz = 10,
        GlobalNetworkEntityCapacity = 1024,
        ReplicationEntityCapacityPerSeat = 128,
        OrderQueueCapacity = 256,
        MaxCommandBatchesPerSecondPerPlayer = 32,
        CommandBurstBatchCapacity = 16,
        MaxActorsPerCommandBatch = 64,
        CommandSequenceHistoryCapacity = 64,
        MaxPastTargetTicks = 3,
        MaxFutureTargetTicks = 6,
        NetworkAdmissionResultCapacity = 64,
        EntityAdmissionResultCapacity = 128,
        ReconnectWindowSeconds = 30,
        ClientReconnectRetryMilliseconds = 500,
        ReplicationSchemaCapacity = 16,
        BaselineCapacity = 16,
        DisclosureChangeLogCapacity = 256,
        DatagramQueueCapacity = 128,
        ConnectionEventCapacity = 16,
        MaxDatagramPayloadBytes = 1200,
        TransportMaxConnectAttempts = 3,
        TransportDisconnectTimeoutMilliseconds = 5000,
        ReliableDisconnectFlushTimeoutMilliseconds = 4000,
        TransportChannelCount = 4,
        ControlChannelId = 0,
        CommandChannelId = 1,
        StateChannelId = 2,
        InputChannelId = 3,
        FixedInputHistoryTicksPerSeat = 8,
        FixedInputSchemaId = 1,
        FixedInputFramePayloadBytes = 12,
        FixedInputMaxFutureTicks = 4,
        FixedInputLeadTicks = 2,
        FixedInputMaxFramesPerBatch = 4,
        FixedInputPendingFrameCapacity = 8,
        SnapshotChunkCapacity = 32,
        MaxServerOutboundBytesPerSecondPerClient = 262144,
        TickP95BudgetMicroseconds = 26700,
        TickP99BudgetMicroseconds = 31000,
        CommandSchemas =
        {
            new NetworkCommandSchemaConfig
            {
                OrderTypeKey = "moveTo",
                TargetKind = NetworkCommandTargetKind.WorldPositionCm,
                SubmitMode = Ludots.Core.Gameplay.GAS.Orders.OrderSubmitMode.Queued,
            },
        },
        NormalConnection = new NetworkFaultProfileConfig(),
        UnstableConnection = new NetworkFaultProfileConfig(),
    };

    private sealed class ClientInstallHarness : IDisposable
    {
        private readonly string _root;

        private ClientInstallHarness(
            string root,
            GameEngine engine,
            GameBootstrapResult bootstrap,
            string runtimeDirectory)
        {
            _root = root;
            Engine = engine;
            Bootstrap = bootstrap;
            RuntimeDirectory = runtimeDirectory;
        }

        public GameEngine Engine { get; }
        public GameBootstrapResult Bootstrap { get; }
        public string RuntimeDirectory { get; }
        public ClientReplicationSchemaApplierRegistry? Appliers { get; private set; }

        public static ClientInstallHarness Create(bool registerMapOwnedServices)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_LiteNetLibInstallerLifecycle",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string runtimeDirectory = Path.Combine(root, "runtime");
            Directory.CreateDirectory(runtimeDirectory);

            NetworkRuntimeConfig networking = CreateValidConfig();
            var gameConfig = new GameConfig
            {
                Networking = networking,
            };
            var engine = new GameEngine();
            AssignMergedConfig(engine, gameConfig);
            AssignWorld(engine);

            var host = new NetworkHostBootstrapConfig
            {
                ProcessRole = "replicatedClient",
                Host = "127.0.0.1",
                Port = 40_511,
                ConnectionKey = "installer-lifecycle-test",
                ClientInstanceId = 1,
                CredentialPath = "client.credential",
            };
            string fingerprint = new string('a', ContentFingerprint.HexLength);
            var bootstrap = new GameBootstrapResult(
                engine,
                gameConfig,
                AssetsRoot: root,
                PlanFingerprint: fingerprint,
                NetworkHost: host);

            var harness = new ClientInstallHarness(root, engine, bootstrap, runtimeDirectory);
            if (registerMapOwnedServices)
            {
                harness.RegisterMapOwnedClientServices();
            }

            return harness;
        }

        public void RegisterMapOwnedClientServices(string? omit = null)
        {
            NetworkRuntimeConfig config = Bootstrap.Config.Networking!;
            Appliers = new ClientReplicationSchemaApplierRegistry(config.ReplicationSchemaCapacity);
            if (omit != "applier")
            {
                Assert.That(
                    Appliers.Register(1, new StubApplier()),
                    Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            }

            Engine.SetService(CoreServiceKeys.ClientReplicationSchemaAppliers, Appliers);

            if (omit != "source")
            {
                Engine.SetService(
                    CoreServiceKeys.FixedInputPayloadSource,
                    (IFixedInputPayloadSource)new StubPayloadSource());
            }

            Engine.SetService(
                CoreServiceKeys.NetworkCommandAdmissionResults,
                new NetworkCommandAdmissionResultBuffer(capacity: 8));

            if (omit != "observer")
            {
                Engine.SetService(
                    CoreServiceKeys.NetworkRuntimeObserverBridge,
                    (INetworkRuntimeObserver)new RecordingObserver());
            }
        }

        public void Dispose()
        {
            Engine.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static void AssignMergedConfig(GameEngine engine, GameConfig config)
        {
            typeof(GameEngine)
                .GetProperty(nameof(GameEngine.MergedConfig))!
                .SetValue(engine, config);
        }

        private static void AssignWorld(GameEngine engine)
        {
            typeof(GameEngine)
                .GetProperty(nameof(GameEngine.World))!
                .SetValue(engine, World.Create());
        }
    }

    private sealed class StubControllerResolver : IAuthoritativeSeatControllerResolver
    {
        public bool TryResolveController(in SessionSeatBinding seat, out Entity controller)
        {
            controller = Entity.Null;
            return false;
        }
    }

    private sealed class StubSeatFactory : IAuthoritativeReplicationSeatRuntimeFactory
    {
        public StubSeatFactory(
            int seatCapacity,
            int globalEntityCapacity,
            int replicationEntityCapacityPerSeat)
        {
            SeatCapacity = seatCapacity;
            GlobalEntityCapacity = globalEntityCapacity;
            ReplicationEntityCapacityPerSeat = replicationEntityCapacityPerSeat;
        }

        public int SeatCapacity { get; }

        public int GlobalEntityCapacity { get; }

        public int ReplicationEntityCapacityPerSeat { get; }

        public bool TryAcquire(
            in SessionSeatBinding seat,
            Entity viewer,
            [NotNullWhen(true)] out AuthoritativeReplicationSeatRuntime? runtime)
        {
            runtime = null;
            return false;
        }

        public bool TryRelease(
            in SessionSeatBinding seat,
            AuthoritativeReplicationSeatRuntime runtime) => false;
    }

    private sealed class StubApplier : IClientReplicationSchemaApplier
    {
        public bool CanCreate(World world, in ReplicatedEntityState state, in ReplicationApplyContext context) => true;

        public bool CanApply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context) =>
            true;

        public bool CanRelease(
            World world,
            Entity entity,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context) => true;

        public Entity Create(
            World world,
            in ReplicationMirrorIdentity identity,
            in ReplicationMirrorState state,
            in ReplicationApplyContext context) => Entity.Null;

        public void Apply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context)
        {
        }

        public void Release(
            World world,
            Entity entity,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context)
        {
        }
    }

    private sealed class StubPayloadSource : IFixedInputPayloadSource
    {
        public FixedInputPayloadSampleStatus TrySample(uint targetTick, Span<byte> destination)
        {
            destination.Clear();
            return FixedInputPayloadSampleStatus.Sampled;
        }

        public FixedInputPayloadCommitStatus TryCommit(uint targetTick, ReadOnlySpan<byte> sentPayload) =>
            FixedInputPayloadCommitStatus.Committed;
    }

    private sealed class RecordingObserver : INetworkRuntimeObserver
    {
        public NetworkRuntimeStateObserver? StateObserver { get; set; }

        public NetworkSeatConnectionState SeatStateObservedDuringCallback { get; private set; }

        public bool ThrowOnSeatEvent { get; set; }

        public void OnFault(in NetworkRuntimeFault fault)
        {
        }

        public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected)
        {
            SeatStateObservedDuringCallback = StateObserver?.GetSeatState(seat.Slot) ??
                NetworkSeatConnectionState.Empty;
            ThrowIfRequested();
        }

        public void OnServerSeatDisconnected(
            in SessionSeatBinding seat,
            TransportDisconnectReason reason)
        {
            SeatStateObservedDuringCallback = StateObserver?.GetSeatState(seat.Slot) ??
                NetworkSeatConnectionState.Empty;
            ThrowIfRequested();
        }

        public void OnServerSeatReleased(in SessionSeatBinding seat)
        {
            SeatStateObservedDuringCallback = StateObserver?.GetSeatState(seat.Slot) ??
                NetworkSeatConnectionState.Empty;
            ThrowIfRequested();
        }

        public void OnClientHandshake(in SessionHandshakeResponse response)
        {
        }

        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome)
        {
        }

        public void OnClientResyncRequired(in NetworkResyncRequired message)
        {
        }

        private void ThrowIfRequested()
        {
            if (ThrowOnSeatEvent)
            {
                throw new InvalidOperationException("bridge rejected seat event");
            }
        }
    }
}
