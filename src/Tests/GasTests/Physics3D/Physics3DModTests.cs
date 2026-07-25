using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Client;
using Ludots.Core.Physics3DNet.Input;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Physics3DMod;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DModTests
{
    [Test]
    public void Runtime_InstallsIdempotentlyAndUnloadsOwnedState()
    {
        using GameEngine engine = CreateEngine();
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);
        var runtime = new Physics3DRuntime();

        runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult();
        IPhysics3DWorld world = engine.GetService(Physics3DServiceKeys.World);
        Physics3DSimulationSystem system = engine.GetService(Physics3DServiceKeys.SimulationSystem);
        runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult();

        Assert.That(engine.GetService(Physics3DServiceKeys.World), Is.SameAs(world));
        Assert.That(engine.GetService(Physics3DServiceKeys.SimulationSystem), Is.SameAs(system));

        runtime.Dispose();
        Assert.That(engine.TryGetService(Physics3DServiceKeys.World, out _), Is.False);
        Assert.That(engine.TryGetService(Physics3DServiceKeys.SimulationSystem, out _), Is.False);
    }

    [Test]
    public void Runtime_RejectsExistingServiceWithoutOverwritingIt()
    {
        using GameEngine engine = CreateEngine();
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);
        using var existing = new Physics3DWorld(Physics3DWorldTests.CreateConfig(1, 0, workerCount: 1));
        engine.SetService(Physics3DServiceKeys.World, (IPhysics3DWorld)existing);
        var runtime = new Physics3DRuntime();

        Assert.Throws<InvalidOperationException>(() =>
            runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult());
        Assert.That(engine.GetService(Physics3DServiceKeys.World), Is.SameAs(existing));

        runtime.Dispose();
        Assert.That(engine.RemoveService(Physics3DServiceKeys.World), Is.True);
    }

    [Test]
    public void Runtime_RefusesToUnloadStateItNoLongerOwns()
    {
        using GameEngine engine = CreateEngine();
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);
        var runtime = new Physics3DRuntime();
        runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult();
        IPhysics3DWorld ownedWorld = engine.GetService(Physics3DServiceKeys.World);
        using var replacement = new Physics3DWorld(Physics3DWorldTests.CreateConfig(1, 0, workerCount: 1));
        engine.SetService(Physics3DServiceKeys.World, (IPhysics3DWorld)replacement);

        Assert.Throws<InvalidOperationException>(() => runtime.Dispose());
        Assert.That(engine.GetService(Physics3DServiceKeys.World), Is.SameAs(replacement));

        engine.SetService(Physics3DServiceKeys.World, ownedWorld);
        runtime.Dispose();
    }

    [Test]
    public void Runtime_AuthoritativeCompositionReserves150PlayersAndOwnsHostServices()
    {
        using GameEngine engine = CreateNetworkEngine(NetworkProcessRole.AuthoritativeServer, playerCapacity: 150);
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);
        var runtime = new Physics3DRuntime();

        runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult();
        var resolver = (Physics3DNetworkPlayerLifecycle)engine.GetService(
            CoreServiceKeys.AuthoritativeSeatControllerResolver);
        KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore);
        for (int slot = 0; slot < 150; slot++)
        {
            var seat = new SessionSeatBinding(slot, generation: 1, new PlayerId(slot + 1));
            Assert.That(resolver.TryResolveController(in seat, out _), Is.True, $"seat {slot}");
        }

        var systems = GetSystems(engine, SystemGroup.InputCollection);
        int inputIndex = systems.FindIndex(system => system is Physics3DAuthoritativeFixedInputSystem);
        int simulationIndex = systems.FindIndex(system => system is Physics3DSimulationSystem);
        Assert.Multiple(() =>
        {
            Assert.That(knowledge.RecordCapacity, Is.GreaterThanOrEqualTo(32_768));
            Assert.That(knowledge.RecordCount, Is.Zero);
            Assert.That(resolver.ActivePlayerCount, Is.EqualTo(150));
            Assert.That(
                engine.GetService(Physics3DNetworkServiceKeys.BodyRegistry),
                Is.TypeOf<Physics3DNetworkBodyRegistry>());
            Assert.That(
                engine.GetService(CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory),
                Is.TypeOf<Physics3DAuthoritativeReplicationSeatRuntimeFactory>());
            Assert.That(
                engine.GetService(CoreServiceKeys.AuthoritativeReplicationInterest),
                Is.TypeOf<Physics3DNetworkAoiInterestPort>());
            Assert.That(
                engine.GetService(CoreServiceKeys.NetworkRuntimeObserverBridge),
                Is.TypeOf<Physics3DNetworkPlayerLifecycleObserver>());
            Assert.That(inputIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(simulationIndex, Is.GreaterThan(inputIndex));
        });

        runtime.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(engine.TryGetService(CoreServiceKeys.AuthoritativeSeatControllerResolver, out _), Is.False);
            Assert.That(engine.TryGetService(CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory, out _), Is.False);
            Assert.That(engine.TryGetService(CoreServiceKeys.AuthoritativeReplicationInterest, out _), Is.False);
            Assert.That(engine.TryGetService(CoreServiceKeys.NetworkRuntimeObserverBridge, out _), Is.False);
            Assert.That(engine.TryGetService(Physics3DNetworkServiceKeys.BodyRegistry, out _), Is.False);
        });
    }

    [Test]
    public void Runtime_AuthoritativeFirstStepRequiresStablePublishedIngress()
    {
        using GameEngine engine = CreateNetworkEngine(NetworkProcessRole.AuthoritativeServer, playerCapacity: 2);
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);
        var runtime = new Physics3DRuntime();
        runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult();
        Physics3DAuthoritativeFixedInputSystem inputSystem = FindSystem<Physics3DAuthoritativeFixedInputSystem>(
            engine,
            SystemGroup.InputCollection);

        engine.GameSession.BeginSimulationTick();
        Assert.Throws<InvalidOperationException>(() => inputSystem.Update(Time.FixedDeltaTime));
        var config = new FixedInputProtocolConfig(
            seatCapacity: 2,
            historyTicksPerSeat: 8,
            schemaId: 1,
            framePayloadBytes: Physics3DFixedInputFrameCodec.PayloadBytes,
            maxFutureTicks: 4,
            maxFramesPerBatch: 4,
            maxDatagramPayloadBytes: 1_200,
            sessionEpoch: 91);
        var first = new AuthoritativeFixedInputIngress(in config, engine.GameSession.SimulationTicks);
        engine.SetService(CoreServiceKeys.AuthoritativeFixedInputIngress, first);
        Assert.DoesNotThrow(() => inputSystem.Update(Time.FixedDeltaTime));
        engine.GameSession.CommitFixedUpdate();

        engine.SetService(
            CoreServiceKeys.AuthoritativeFixedInputIngress,
            new AuthoritativeFixedInputIngress(in config, engine.GameSession.SimulationTicks));
        engine.GameSession.BeginSimulationTick();
        Assert.Throws<InvalidOperationException>(() => inputSystem.Update(Time.FixedDeltaTime));
        engine.GameSession.CommitFixedUpdate();
        runtime.Dispose();
    }

    [Test]
    public void Runtime_ReplicatedClientRegistersApplierAndObserverBeforeFirstPump()
    {
        using GameEngine engine = CreateNetworkEngine(NetworkProcessRole.ReplicatedClient, playerCapacity: 2);
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);
        var runtime = new Physics3DRuntime();

        runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult();
        ClientReplicationSchemaApplierRegistry appliers = engine.GetService(
            CoreServiceKeys.ClientReplicationSchemaAppliers);
        Assert.That(appliers.IsFrozen, Is.False);
        appliers.Freeze();
        Assert.Multiple(() =>
        {
            Assert.That(appliers.TryGet(1, out IClientReplicationSchemaApplier applier), Is.True);
            Assert.That(applier, Is.TypeOf<Physics3DClientBodyReplicationApplier>());
            Assert.That(
                engine.GetService(CoreServiceKeys.NetworkRuntimeObserverBridge),
                Is.TypeOf<Physics3DClientNetworkRuntimeObserver>());
            Assert.That(
                engine.GetService(CoreServiceKeys.FixedInputPayloadSource),
                Is.SameAs(engine.GetService(Physics3DNetworkServiceKeys.ClientConvergence)));
            Assert.That(engine.TryGetService(CoreServiceKeys.AuthoritativeSeatControllerResolver, out _), Is.False);
            Assert.That(
                GetSystems(engine, SystemGroup.InputCollection).Exists(
                    system => system is Physics3DAuthoritativeFixedInputSystem),
                Is.False);
        });

        runtime.Dispose();
        Assert.That(engine.TryGetService(CoreServiceKeys.NetworkRuntimeObserverBridge, out _), Is.False);
        Assert.That(engine.TryGetService(CoreServiceKeys.FixedInputPayloadSource, out _), Is.False);
        Assert.That(engine.TryGetService(Physics3DNetworkServiceKeys.ClientConvergence, out _), Is.False);
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "Physics3DMod" }),
            Path.Combine(repoRoot, "assets"));
        return engine;
    }

    private static GameEngine CreateNetworkEngine(NetworkProcessRole role, int playerCapacity)
    {
        GameEngine engine = CreateEngine();
        NetworkRuntimeConfig network = CreateNetworkConfig(playerCapacity);
        engine.MergedConfig.Networking = network;
        engine.SetService(CoreServiceKeys.NetworkProcessRole, role);
        engine.SetService(
            CoreServiceKeys.NetworkEntityTable,
            new NetworkEntityTable(network.GlobalNetworkEntityCapacity));
        engine.SetService(
            CoreServiceKeys.ReplicationSchemaProjectors,
            new ReplicationSchemaProjectorRegistry(network.ReplicationSchemaCapacity));
        engine.SetService(
            CoreServiceKeys.ClientReplicationSchemaAppliers,
            new ClientReplicationSchemaApplierRegistry(network.ReplicationSchemaCapacity));
        if (role == NetworkProcessRole.ReplicatedClient)
        {
            engine.SetService(
                Physics3DNetworkServiceKeys.ClientInputSource,
                (IPhysics3DClientInputSource)new StubClientInputSource());
            engine.SetService(
                Physics3DNetworkServiceKeys.LocalPredictionDriver,
                (IPhysics3DLocalPredictionDriver)new StubPredictionDriver());
        }
        return engine;
    }

    private static NetworkRuntimeConfig CreateNetworkConfig(int playerCapacity) => new()
    {
        ProfileId = "physics3d_150_v1",
        ReferenceTransport = "LiteNetLib/2.1.4",
        ProtocolMajor = 1,
        ProtocolMinor = 0,
        PlayerCapacity = playerCapacity,
        SimulationTickRateHz = 30,
        StatePublishRateHz = 10,
        GlobalNetworkEntityCapacity = 100_000,
        ReplicationEntityCapacityPerSeat = 512,
        OrderQueueCapacity = 1_024,
        MaxCommandBatchesPerSecondPerPlayer = 8,
        CommandBurstBatchCapacity = 4,
        MaxActorsPerCommandBatch = 1,
        CommandSequenceHistoryCapacity = Math.Max(8, playerCapacity * 4),
        MaxPastTargetTicks = 3,
        MaxFutureTargetTicks = 6,
        NetworkAdmissionResultCapacity = Math.Max(8, playerCapacity * 4),
        EntityAdmissionResultCapacity = 1_024,
        ReconnectWindowSeconds = 30,
        ClientReconnectRetryMilliseconds = 500,
        ReplicationSchemaCapacity = 32,
        BaselineCapacity = 512,
        DisclosureChangeLogCapacity = 1_024,
        DatagramQueueCapacity = 1_024,
        ConnectionEventCapacity = Math.Max(8, playerCapacity),
        MaxDatagramPayloadBytes = 1_200,
        TransportMaxConnectAttempts = 10,
        TransportDisconnectTimeoutMilliseconds = 5_000,
        ReliableDisconnectFlushTimeoutMilliseconds = 4_000,
        TransportChannelCount = 8,
        ControlChannelId = 0,
        CommandChannelId = 1,
        StateChannelId = 2,
        InputChannelId = 3,
        FixedInputHistoryTicksPerSeat = 8,
        FixedInputSchemaId = 1,
        FixedInputFramePayloadBytes = Physics3DFixedInputFrameCodec.PayloadBytes,
        FixedInputMaxFutureTicks = 4,
        FixedInputLeadTicks = 1,
        FixedInputMaxFramesPerBatch = 4,
        FixedInputPendingFrameCapacity = 8,
        SnapshotChunkCapacity = 512,
        MaxServerOutboundBytesPerSecondPerClient = 256 * 1_024,
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

    private static List<ISystem<float>> GetSystems(GameEngine engine, SystemGroup group)
    {
        FieldInfo field = typeof(GameEngine).GetField(
            "_systemGroups",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GameEngine system groups field is missing.");
        var groups = (Dictionary<SystemGroup, List<ISystem<float>>>)field.GetValue(engine)!;
        return groups[group];
    }

    private static T FindSystem<T>(GameEngine engine, SystemGroup group)
        where T : class, ISystem<float>
    {
        List<ISystem<float>> systems = GetSystems(engine, group);
        for (int i = 0; i < systems.Count; i++)
        {
            if (systems[i] is T found)
            {
                return found;
            }
        }

        throw new InvalidOperationException($"System '{typeof(T).Name}' was not registered.");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the Physics3D test output directory.");
    }

    private sealed class StubClientInputSource : IPhysics3DClientInputSource
    {
        public bool TrySampleMovement(uint targetTick, out Vector2 movement)
        {
            movement = Vector2.Zero;
            return targetTick > 0;
        }
    }

    private sealed class StubPredictionDriver : IPhysics3DLocalPredictionDriver
    {
        public bool Supports(Physics3DNetLocalDrivenKind kind) =>
            kind is Physics3DNetLocalDrivenKind.Character or Physics3DNetLocalDrivenKind.Vehicle;

        public bool TryStep(
            Entity entity,
            Physics3DBodyId body,
            Physics3DNetLocalDrivenKind kind,
            uint targetTick,
            in Physics3DFixedInputFrame input,
            out Physics3DBodyState predictedState)
        {
            predictedState = default;
            return false;
        }
    }
}
