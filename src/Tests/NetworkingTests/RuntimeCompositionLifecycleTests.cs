using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
[NonParallelizable]
public sealed class RuntimeCompositionLifecycleTests
{
    [Test]
    public void Initialize_FiresRuntimeCompositionExactlyOnceWithReadyRuntimeState()
    {
        RuntimeCompositionProbeState.Reset();
        using var fixture = RuntimeCompositionModFixture.Create(includeNetworking: false);
        GameEngine engine = new();
        SynchronizationContext? previousContext = SynchronizationContext.Current;

        try
        {
            fixture.Initialize(engine);

            Assert.Multiple(() =>
            {
                Assert.That(RuntimeCompositionProbeState.ModLoadCount, Is.EqualTo(1));
                Assert.That(RuntimeCompositionProbeState.CompositionStartedCount, Is.EqualTo(1));
                Assert.That(RuntimeCompositionProbeState.CompositionCompletedCount, Is.EqualTo(1));
                Assert.That(RuntimeCompositionProbeState.ConfigurationVisible, Is.True);
                Assert.That(RuntimeCompositionProbeState.WorldVisible, Is.True);
                Assert.That(RuntimeCompositionProbeState.CoreServiceVisible, Is.True);
                Assert.That(RuntimeCompositionProbeState.CoreSystemVisible, Is.True);
                Assert.That(RuntimeCompositionProbeState.GameStartCount, Is.Zero);
                Assert.That(RuntimeCompositionProbeState.MapLoadedCount, Is.Zero);
                Assert.That(RuntimeCompositionProbeState.Events, Is.EqualTo(new[]
                {
                    "ModLoaded",
                    "RuntimeCompositionStarted",
                    "RuntimeCompositionCompleted",
                }));
            });
        }
        finally
        {
            engine.Dispose();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Test]
    public void RuntimeComposition_PrecedesGameStartAndMapLoadedWithoutReplacingEitherEvent()
    {
        RuntimeCompositionProbeState.Reset();
        using var fixture = RuntimeCompositionModFixture.Create(includeNetworking: false);
        GameEngine engine = new();
        SynchronizationContext? previousContext = SynchronizationContext.Current;

        try
        {
            fixture.Initialize(engine);
            engine.Start();
            engine.LoadStartupMap();

            Assert.Multiple(() =>
            {
                Assert.That(RuntimeCompositionProbeState.CompositionCompletedCount, Is.EqualTo(1));
                Assert.That(RuntimeCompositionProbeState.GameStartCount, Is.EqualTo(1));
                Assert.That(RuntimeCompositionProbeState.MapLoadedCount, Is.EqualTo(1));
                Assert.That(RuntimeCompositionProbeState.Events, Is.EqualTo(new[]
                {
                    "ModLoaded",
                    "RuntimeCompositionStarted",
                    "RuntimeCompositionCompleted",
                    "GameStart",
                    "MapLoaded",
                }));
            });
        }
        finally
        {
            engine.Dispose();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [TestCase(RuntimeCompositionFailureMode.Synchronous)]
    [TestCase(RuntimeCompositionFailureMode.Asynchronous)]
    public void RuntimeCompositionFailure_AbortsInitializationAndRunsExistingModCleanup(
        RuntimeCompositionFailureMode failureMode)
    {
        RuntimeCompositionProbeState.Reset(failureMode: failureMode);
        using var fixture = RuntimeCompositionModFixture.Create(includeNetworking: false);
        using GameEngine engine = new();
        SynchronizationContext? previousContext = SynchronizationContext.Current;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => fixture.Initialize(engine))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo(RuntimeCompositionProbeState.FailureMessage));
            Assert.That(RuntimeCompositionProbeState.CompositionStartedCount, Is.EqualTo(1));
            Assert.That(RuntimeCompositionProbeState.CompositionCompletedCount, Is.Zero);
            Assert.That(RuntimeCompositionProbeState.ModUnloadCount, Is.EqualTo(1));
            Assert.That(RuntimeCompositionProbeState.GameStartCount, Is.Zero);
            Assert.That(ReferenceEquals(SynchronizationContext.Current, previousContext), Is.True);
        });
    }

    [Test]
    public void RuntimeComposition_CanInstallNetworkRuntimeBeforeStartValidation()
    {
        RuntimeCompositionProbeState.Reset(installNetworkRuntime: true);
        using var fixture = RuntimeCompositionModFixture.Create(includeNetworking: true);
        GameEngine engine = new();
        SynchronizationContext? previousContext = SynchronizationContext.Current;

        try
        {
            fixture.Initialize(engine);

            Assert.That(
                engine.GetService(CoreServiceKeys.NetworkRuntimePort),
                Is.SameAs(RuntimeCompositionProbeState.InstalledNetworkRuntime));
            Assert.That(RuntimeCompositionProbeState.NetworkingCompositionServicesVisible, Is.True);
            Assert.DoesNotThrow(engine.Start);
            Assert.That(RuntimeCompositionProbeState.GameStartCount, Is.EqualTo(1));
        }
        finally
        {
            engine.Dispose();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        Assert.That(RuntimeCompositionProbeState.InstalledNetworkRuntime!.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void RuntimeCompositionFailure_DisposesRuntimeInstalledByAnEarlierCompositionStep()
    {
        RuntimeCompositionProbeState.Reset(
            failureMode: RuntimeCompositionFailureMode.Asynchronous,
            installNetworkRuntime: true);
        using var fixture = RuntimeCompositionModFixture.Create(includeNetworking: true);
        using GameEngine engine = new();

        Assert.Throws<InvalidOperationException>(() => fixture.Initialize(engine));

        Assert.Multiple(() =>
        {
            Assert.That(RuntimeCompositionProbeState.InstalledNetworkRuntime, Is.Not.Null);
            Assert.That(RuntimeCompositionProbeState.InstalledNetworkRuntime!.DisposeCount, Is.EqualTo(1));
            Assert.That(engine.TryGetService(CoreServiceKeys.NetworkRuntimePort, out INetworkRuntimePort _), Is.False);
            Assert.That(engine.GetService(CoreServiceKeys.NetworkProcessRole), Is.EqualTo(NetworkProcessRole.Standalone));
        });
    }

    private sealed class RuntimeCompositionModFixture : IDisposable
    {
        private const string ModName = "NetworkingTests";
        private const string StartupMapId = "runtime_composition_lifecycle";

        private RuntimeCompositionModFixture(string root, string modPath)
        {
            Root = root;
            ModPath = modPath;
        }

        private string Root { get; }

        private string ModPath { get; }

        public static RuntimeCompositionModFixture Create(bool includeNetworking)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_RuntimeCompositionLifecycleTests",
                Guid.NewGuid().ToString("N"));
            string modPath = Path.Combine(root, ModName);
            string outputPath = Path.Combine(modPath, "bin", "net8.0");
            string assetsPath = Path.Combine(modPath, "assets");
            string mapsPath = Path.Combine(assetsPath, "Maps");
            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(mapsPath);

            File.Copy(
                typeof(RuntimeCompositionProbeMod).Assembly.Location,
                Path.Combine(outputPath, $"{ModName}.dll"));
            File.WriteAllText(
                Path.Combine(modPath, "mod.json"),
                """
                {
                  "name": "NetworkingTests",
                  "version": "1.0.0",
                  "description": "Runtime composition lifecycle fixture",
                  "main": "bin/net8.0/NetworkingTests.dll",
                  "priority": 0,
                  "dependencies": {
                    "LudotsCoreMod": "^1.0.0"
                  }
                }
                """);
            File.WriteAllText(
                Path.Combine(assetsPath, "game.json"),
                CreateGameConfig(includeNetworking));
            File.WriteAllText(
                Path.Combine(mapsPath, $"{StartupMapId}.json"),
                """
                {
                  "Id": "runtime_composition_lifecycle",
                  "Tags": [ "camera.skip_default_on_load" ],
                  "Entities": []
                }
                """);

            return new RuntimeCompositionModFixture(root, modPath);
        }

        public void Initialize(GameEngine engine)
        {
            string repoRoot = FindRepoRoot();
            engine.InitializeWithConfigPipeline(
                new List<string>
                {
                    Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                    ModPath,
                },
                Path.Combine(repoRoot, "assets"));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string CreateGameConfig(bool includeNetworking)
        {
            if (!includeNetworking)
            {
                return """
                {
                  "startupMapId": "runtime_composition_lifecycle"
                }
                """;
            }

            return """
            {
              "startupMapId": "runtime_composition_lifecycle",
              "networking": {
                "profileId": "runtime_composition_test",
                "referenceTransport": "test",
                "protocolMajor": 1,
                "protocolMinor": 0,
                "playerCapacity": 2,
                "simulationTickRateHz": 30,
                "statePublishRateHz": 10,
                "globalNetworkEntityCapacity": 1024,
                "replicationEntityCapacityPerSeat": 128,
                "orderQueueCapacity": 256,
                "maxCommandBatchesPerSecondPerPlayer": 32,
                "commandBurstBatchCapacity": 16,
                "maxActorsPerCommandBatch": 64,
                "commandSequenceHistoryCapacity": 64,
                "maxPastTargetTicks": 3,
                "maxFutureTargetTicks": 6,
                "networkAdmissionResultCapacity": 64,
                "entityAdmissionResultCapacity": 128,
                "reconnectWindowSeconds": 30,
                "clientReconnectRetryMilliseconds": 500,
                "replicationSchemaCapacity": 16,
                "baselineCapacity": 16,
                "disclosureChangeLogCapacity": 256,
                "datagramQueueCapacity": 128,
                "connectionEventCapacity": 16,
                "maxDatagramPayloadBytes": 1200,
                "transportMaxConnectAttempts": 3,
                "transportDisconnectTimeoutMilliseconds": 5000,
                "reliableDisconnectFlushTimeoutMilliseconds": 4000,
                "transportChannelCount": 4,
                "controlChannelId": 0,
                "commandChannelId": 1,
                "stateChannelId": 2,
                "inputChannelId": 3,
                "fixedInputHistoryTicksPerSeat": 8,
                "fixedInputSchemaId": 1,
                "fixedInputFramePayloadBytes": 12,
                "fixedInputMaxFutureTicks": 4,
                "fixedInputLeadTicks": 2,
                "fixedInputMaxFramesPerBatch": 4,
                "fixedInputPendingFrameCapacity": 8,
                "snapshotChunkCapacity": 32,
                "maxServerOutboundBytesPerSecondPerClient": 262144,
                "tickP95BudgetMicroseconds": 26700,
                "tickP99BudgetMicroseconds": 31000,
                "commandSchemas": [
                  {
                    "orderTypeKey": "moveTo",
                    "targetKind": "WorldPositionCm",
                    "submitMode": "Queued"
                  },
                  {
                    "orderTypeKey": "attackTarget",
                    "targetKind": "NetworkEntity",
                    "submitMode": "Queued",
                    "requiredTargetPositionAccess": "LastKnown"
                  },
                  {
                    "orderTypeKey": "stop",
                    "targetKind": "None",
                    "submitMode": "Immediate"
                  }
                ],
                "normalConnection": {
                  "roundTripLatencyMs": 0,
                  "jitterMs": 0,
                  "packetLossPermille": 0,
                  "reorderPermille": 0
                },
                "unstableConnection": {
                  "roundTripLatencyMs": 180,
                  "jitterMs": 30,
                  "packetLossPermille": 50,
                  "reorderPermille": 20
                }
              }
            }
            """;
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && directory != null; i++)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }
    }
}

public enum RuntimeCompositionFailureMode : byte
{
    None = 0,
    Synchronous = 1,
    Asynchronous = 2,
}

public static class RuntimeCompositionProbeState
{
    public const string FailureMessage = "Runtime composition fixture failure.";

    private static readonly List<string> EventLog = new();

    public static RuntimeCompositionFailureMode FailureMode { get; private set; }

    public static bool InstallNetworkRuntime { get; private set; }

    public static int ModLoadCount { get; set; }

    public static int ModUnloadCount { get; set; }

    public static int CompositionStartedCount { get; set; }

    public static int CompositionCompletedCount { get; set; }

    public static int GameStartCount { get; set; }

    public static int MapLoadedCount { get; set; }

    public static bool ConfigurationVisible { get; set; }

    public static bool WorldVisible { get; set; }

    public static bool CoreServiceVisible { get; set; }

    public static bool CoreSystemVisible { get; set; }

    public static bool NetworkingCompositionServicesVisible { get; set; }

    public static RuntimeCompositionNetworkRuntime? InstalledNetworkRuntime { get; set; }

    public static IReadOnlyList<string> Events => EventLog;

    public static void Reset(
        RuntimeCompositionFailureMode failureMode = RuntimeCompositionFailureMode.None,
        bool installNetworkRuntime = false)
    {
        FailureMode = failureMode;
        InstallNetworkRuntime = installNetworkRuntime;
        ModLoadCount = 0;
        ModUnloadCount = 0;
        CompositionStartedCount = 0;
        CompositionCompletedCount = 0;
        GameStartCount = 0;
        MapLoadedCount = 0;
        ConfigurationVisible = false;
        WorldVisible = false;
        CoreServiceVisible = false;
        CoreSystemVisible = false;
        NetworkingCompositionServicesVisible = false;
        InstalledNetworkRuntime = null;
        EventLog.Clear();
    }

    public static void Record(string eventName)
    {
        EventLog.Add(eventName);
    }
}

public sealed class RuntimeCompositionProbeMod : IMod
{
    public void OnLoad(IModContext context)
    {
        RuntimeCompositionProbeState.ModLoadCount++;
        RuntimeCompositionProbeState.Record("ModLoaded");
        context.OnEvent(GameEvents.RuntimeComposition, HandleRuntimeComposition);
        context.OnEvent(GameEvents.GameStart, _ =>
        {
            RuntimeCompositionProbeState.GameStartCount++;
            RuntimeCompositionProbeState.Record("GameStart");
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ =>
        {
            RuntimeCompositionProbeState.MapLoadedCount++;
            RuntimeCompositionProbeState.Record("MapLoaded");
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
        RuntimeCompositionProbeState.ModUnloadCount++;
        RuntimeCompositionProbeState.Record("ModUnloaded");
    }

    private static Task HandleRuntimeComposition(ScriptContext context)
    {
        RuntimeCompositionProbeState.CompositionStartedCount++;
        RuntimeCompositionProbeState.Record("RuntimeCompositionStarted");

        GameEngine engine = context.Get(CoreServiceKeys.Engine);
        RuntimeCompositionProbeState.ConfigurationVisible =
            ReferenceEquals(context.Get(CoreServiceKeys.GameConfig), engine.MergedConfig) &&
            string.Equals(
                engine.MergedConfig.StartupMapId,
                "runtime_composition_lifecycle",
                StringComparison.Ordinal);
        RuntimeCompositionProbeState.WorldVisible =
            engine.World != null &&
            ReferenceEquals(context.Get(CoreServiceKeys.World), engine.World);
        RuntimeCompositionProbeState.CoreServiceVisible =
            context.Get(CoreServiceKeys.OrderTypeRegistry) != null;
        RuntimeCompositionProbeState.CoreSystemVisible =
            context.Get(CoreServiceKeys.PresentationFrameSetup) != null;
        RuntimeCompositionProbeState.NetworkingCompositionServicesVisible =
            engine.MergedConfig.Networking != null &&
            engine.TryGetService(
                CoreServiceKeys.ReplicationSchemaProjectors,
                out ReplicationSchemaProjectorRegistry projectors) &&
            engine.TryGetService(
                CoreServiceKeys.ClientReplicationSchemaAppliers,
                out ClientReplicationSchemaApplierRegistry appliers) &&
            projectors.SchemaCapacity == engine.MergedConfig.Networking.ReplicationSchemaCapacity &&
            appliers.SchemaCapacity == engine.MergedConfig.Networking.ReplicationSchemaCapacity;

        if (RuntimeCompositionProbeState.InstallNetworkRuntime)
        {
            var runtime = new RuntimeCompositionNetworkRuntime();
            engine.ConfigureNetworkRuntime(NetworkProcessRole.AuthoritativeServer, runtime);
            RuntimeCompositionProbeState.InstalledNetworkRuntime = runtime;
        }

        if (RuntimeCompositionProbeState.FailureMode == RuntimeCompositionFailureMode.Synchronous)
        {
            throw new InvalidOperationException(RuntimeCompositionProbeState.FailureMessage);
        }

        return CompleteRuntimeCompositionAsync(engine);
    }

    private static async Task CompleteRuntimeCompositionAsync(GameEngine engine)
    {
        await Task.Yield();

        if (RuntimeCompositionProbeState.FailureMode == RuntimeCompositionFailureMode.Asynchronous)
        {
            throw new InvalidOperationException(RuntimeCompositionProbeState.FailureMessage);
        }

        RuntimeCompositionProbeState.CompositionCompletedCount++;
        RuntimeCompositionProbeState.Record("RuntimeCompositionCompleted");
    }
}

public sealed class RuntimeCompositionNetworkRuntime : INetworkRuntimePort
{
    public NetworkProcessRole Role => NetworkProcessRole.AuthoritativeServer;

    public int DisposeCount { get; private set; }

    public void PumpTransport()
    {
    }

    public void BeforeAuthoritativeTick(uint executingTick)
    {
    }

    public void AfterAuthoritativeCommit(uint committedTick)
    {
    }

    public void PumpReplicatedClient(float frameDeltaTime)
    {
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}
