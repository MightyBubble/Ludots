using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class MapLoadLifecycleOrderingTests
{
    private const string MapId = "lifecycle_ordering";
    private const string InnerMapId = "lifecycle_ordering_inner";
    private const string TemplateId = "lifecycle_ordering_entity";

    [Test]
    public void LoadMap_StartsPendingLoadBeforeMapEntitiesAreCreated()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        var gate = new ObservingPendingGate();
        engine.SetService(CoreServiceKeys.MapLoadCompletionGate, gate);

        engine.LoadMap(MapId);

        Assert.That(gate.BeginLoadCalls, Is.EqualTo(1));
        Assert.That(gate.ObservedMapId, Is.EqualTo(MapId));
        Assert.That(gate.ObservedCurrentSession, Is.SameAs(gate.ObservedRequestSession));
        Assert.That(gate.ObservedFocusedStatus, Is.EqualTo(MapLoadStatus.DeferredPending));
        Assert.That(gate.ObservedEntityCount, Is.EqualTo(0));
        Assert.That(CountMapEntities(engine.World, MapId), Is.EqualTo(1));
        Assert.That(engine.GetService(CoreServiceKeys.MapLoadStatus), Is.EqualTo(MapLoadStatus.DeferredPending));
    }

    [Test]
    public void PushMap_StartsPendingLoadBeforeInnerMapEntitiesAreCreated()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        engine.LoadMap(MapId);
        var gate = new ObservingPendingGate();
        engine.SetService(CoreServiceKeys.MapLoadCompletionGate, gate);

        engine.PushMap(InnerMapId);

        Assert.That(gate.BeginLoadCalls, Is.EqualTo(1));
        Assert.That(gate.ObservedMapId, Is.EqualTo(InnerMapId));
        Assert.That(gate.ObservedCurrentSession, Is.SameAs(gate.ObservedRequestSession));
        Assert.That(gate.ObservedFocusedStatus, Is.EqualTo(MapLoadStatus.DeferredPending));
        Assert.That(gate.ObservedEntityCount, Is.EqualTo(0));
        Assert.That(CountMapEntities(engine.World, InnerMapId), Is.EqualTo(1));
        Assert.That(engine.GetService(CoreServiceKeys.MapLoadStatus), Is.EqualTo(MapLoadStatus.DeferredPending));
    }

    [Test]
    public void PushMap_BeforeAnyMapLoad_InitializesAndFocusesInnerSession()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();

        engine.PushMap(InnerMapId);

        Assert.That(engine.MapSessions, Is.Not.Null);
        Assert.That(engine.MapSessions.FocusedSession, Is.SameAs(engine.CurrentMapSession));
        Assert.That(engine.CurrentMapSession.MapId.Value, Is.EqualTo(InnerMapId));
        Assert.That(CountMapEntities(engine.World, InnerMapId), Is.EqualTo(1));
    }

    [TestCase(NetworkProcessRole.AuthoritativeServer, true)]
    [TestCase(NetworkProcessRole.AuthoritativeServer, false)]
    [TestCase(NetworkProcessRole.ReplicatedClient, true)]
    [TestCase(NetworkProcessRole.ReplicatedClient, false)]
    public void NetworkRuntime_ActivatesAfterGameStartAndStartupMap_ThenPublishesReadyOnce(
        NetworkProcessRole role,
        bool startBeforeMap)
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        var lifecycle = new List<string>();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(engine, role, lifecycle);
        engine.TriggerManager.RegisterEventHandler(GameEvents.MapLoaded, _ =>
        {
            lifecycle.Add("map-loaded");
            return Task.CompletedTask;
        });
        engine.TriggerManager.RegisterEventHandler(GameEvents.NetworkRuntimeReady, _ =>
        {
            lifecycle.Add("network-ready");
            return Task.CompletedTask;
        });

        if (startBeforeMap)
        {
            engine.Start();
            engine.LoadStartupMap();
        }
        else
        {
            engine.LoadStartupMap();
            engine.Start();
        }

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Role, Is.EqualTo(role));
            Assert.That(runtime.ActivationCount, Is.EqualTo(1));
            Assert.That(lifecycle, Is.EqualTo(new[] { "map-loaded", "activate", "network-ready" }));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PendingStartupMap_BlocksActivationNetworkPumpAndSimulationUntilReady(bool startBeforeMap)
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        engine.SetService(
            CoreServiceKeys.MapLoadCompletionGate,
            new SequencePendingGate(
                MapLoadCompletionResult.Pending(),
                MapLoadCompletionResult.Pending(),
                MapLoadCompletionResult.Ready()));

        if (startBeforeMap)
        {
            engine.Start();
            engine.LoadStartupMap();
        }
        else
        {
            engine.LoadStartupMap();
            engine.Start();
        }

        int tickBeforePendingFrame = engine.GameSession.CurrentTick;
        engine.Tick(1f / 30f);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.Zero);
            Assert.That(runtime.PumpTransportCount, Is.Zero);
            Assert.That(runtime.BeforeAuthoritativeTickCount, Is.Zero);
            Assert.That(runtime.AfterAuthoritativeCommitCount, Is.Zero);
            Assert.That(engine.GameSession.CurrentTick, Is.EqualTo(tickBeforePendingFrame));
        });

        engine.Tick(1f / 30f);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.EqualTo(1));
            Assert.That(runtime.PumpTransportCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void SynchronousStartupMapFailure_ThrowsAndTerminatesNetworkStartup()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        engine.SetService(
            CoreServiceKeys.MapLoadCompletionGate,
            new SequencePendingGate(MapLoadCompletionResult.Failed("startup assets rejected")));
        engine.Start();

        Assert.That(
            () => engine.LoadStartupMap(),
            Throws.InvalidOperationException.With.Message.Contains(
                $"Network startup map '{MapId}' failed to load: startup assets rejected"));

        int failedTick = engine.GameSession.CurrentTick;
        engine.Tick(1f / 30f);
        engine.SetService(
            CoreServiceKeys.MapLoadCompletionGate,
            new SequencePendingGate(MapLoadCompletionResult.Ready()));
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.Zero);
            Assert.That(runtime.PumpTransportCount, Is.Zero);
            Assert.That(engine.GameSession.CurrentTick, Is.EqualTo(failedTick));
            Assert.That(
                () => engine.Start(),
                Throws.InvalidOperationException.With.Message.Contains("cannot be restarted"));
            Assert.That(
                () => engine.LoadStartupMap(),
                Throws.InvalidOperationException.With.Message.Contains("cannot be restarted"));
        });
    }

    [Test]
    public void AsynchronousStartupMapFailure_ThrowsFromTickAndTerminatesNetworkStartup()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        engine.SetService(
            CoreServiceKeys.MapLoadCompletionGate,
            new SequencePendingGate(
                MapLoadCompletionResult.Pending(),
                MapLoadCompletionResult.Failed("deferred startup assets rejected")));
        engine.Start();
        engine.LoadStartupMap();

        Assert.That(
            () => engine.Tick(1f / 30f),
            Throws.InvalidOperationException.With.Message.Contains(
                $"Network startup map '{MapId}' failed to load: deferred startup assets rejected"));

        int failedTick = engine.GameSession.CurrentTick;
        engine.Tick(1f / 30f);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.Zero);
            Assert.That(runtime.PumpTransportCount, Is.Zero);
            Assert.That(engine.GameSession.CurrentTick, Is.EqualTo(failedTick));
            Assert.That(
                () => engine.Start(),
                Throws.InvalidOperationException.With.Message.Contains("cannot be restarted"));
        });
    }

    [Test]
    public void AsynchronousStartupMapCompletionException_TerminatesNetworkStartup()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        engine.SetService(
            CoreServiceKeys.MapLoadCompletionGate,
            new SequencePendingGate(
                MapLoadCompletionResult.Pending(),
                MapLoadCompletionResult.Ready()));
        engine.TriggerManager.RegisterEventHandler(
            GameEvents.MapLoaded,
            _ => throw new InvalidOperationException("map-loaded handler failed"));
        engine.Start();
        engine.LoadStartupMap();

        Assert.That(
            () => engine.Tick(1f / 30f),
            Throws.InvalidOperationException.With.Message.EqualTo("map-loaded handler failed"));

        engine.Tick(1f / 30f);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.Zero);
            Assert.That(runtime.PumpTransportCount, Is.Zero);
            Assert.That(
                () => engine.Start(),
                Throws.InvalidOperationException.With.Message.Contains("cannot be restarted"));
        });
    }

    [Test]
    public void CancelingPendingStartupMapByChangingFocus_TerminatesNetworkStartup()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        engine.SetService(CoreServiceKeys.MapLoadCompletionGate, new ObservingPendingGate());
        engine.Start();
        engine.LoadStartupMap();

        Assert.That(
            () => engine.LoadMap(InnerMapId),
            Throws.InvalidOperationException.With.Message.Contains(
                $"Network startup map '{MapId}' failed to load: Map load canceled"));

        engine.Tick(1f / 30f);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.Zero);
            Assert.That(runtime.PumpTransportCount, Is.Zero);
            Assert.That(
                () => engine.Start(),
                Throws.InvalidOperationException.With.Message.Contains("cannot be restarted"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void StartupMapLosingFocusDuringLifecycleHandler_TerminatesNetworkStartup(
        bool switchDuringGameStart)
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        bool switchedMap = false;
        EventKey switchEvent = switchDuringGameStart
            ? GameEvents.GameStart
            : GameEvents.MapLoaded;
        engine.TriggerManager.RegisterEventHandler(switchEvent, _ =>
        {
            if (!switchedMap)
            {
                switchedMap = true;
                engine.LoadMap(InnerMapId);
            }

            return Task.CompletedTask;
        });

        if (switchDuringGameStart)
        {
            engine.LoadStartupMap();
            Assert.That(
                () => engine.Start(),
                Throws.InvalidOperationException.With.Message.Contains(
                    $"Network startup map '{MapId}' lost focus"));
        }
        else
        {
            engine.Start();
            Assert.That(
                () => engine.LoadStartupMap(),
                Throws.InvalidOperationException.With.Message.Contains(
                    $"Network startup map '{MapId}' lost focus"));
        }

        engine.Tick(1f / 30f);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.Zero);
            Assert.That(runtime.PumpTransportCount, Is.Zero);
            Assert.That(
                () => engine.Start(),
                Throws.InvalidOperationException.With.Message.Contains("cannot be restarted"));
        });
    }

    [Test]
    public void StartupMapRemovedDuringMapLoadedHandler_TerminatesNetworkStartup()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        engine.TriggerManager.RegisterEventHandler(GameEvents.MapLoaded, _ =>
        {
            engine.UnloadMap(MapId);
            return Task.CompletedTask;
        });
        engine.Start();

        Assert.That(
            () => engine.LoadStartupMap(),
            Throws.InvalidOperationException.With.Message.Contains(
                $"Network startup map '{MapId}' is no longer available"));

        engine.Tick(1f / 30f);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.Zero);
            Assert.That(runtime.PumpTransportCount, Is.Zero);
            Assert.That(
                () => engine.Start(),
                Throws.InvalidOperationException.With.Message.Contains("cannot be restarted"));
        });
    }

    [Test]
    public void NetworkRuntimeActivationFailure_DoesNotPublishReadyAndTerminatesStartup()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        runtime.ActivationFailure = new InvalidOperationException("transport activation failed");
        int readyEvents = 0;
        engine.TriggerManager.RegisterEventHandler(GameEvents.NetworkRuntimeReady, _ =>
        {
            readyEvents++;
            return Task.CompletedTask;
        });
        engine.Start();

        Assert.That(
            () => engine.LoadStartupMap(),
            Throws.InvalidOperationException.With.Message.EqualTo("transport activation failed"));

        engine.Tick(1f / 30f);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.EqualTo(1));
            Assert.That(runtime.PumpTransportCount, Is.Zero);
            Assert.That(readyEvents, Is.Zero);
        });
    }

    [Test]
    public void NetworkRuntimeReadyHandlerFailure_DoesNotMarkReadyAndTerminatesStartup()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        engine.TriggerManager.RegisterEventHandler(
            GameEvents.NetworkRuntimeReady,
            _ => throw new InvalidOperationException("network-ready handler failed"));
        engine.Start();

        Assert.That(
            () => engine.LoadStartupMap(),
            Throws.InvalidOperationException.With.Message.EqualTo("network-ready handler failed"));

        engine.Tick(1f / 30f);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.EqualTo(1));
            Assert.That(runtime.PumpTransportCount, Is.Zero);
            Assert.That(
                () => engine.Start(),
                Throws.InvalidOperationException.With.Message.Contains("cannot be restarted"));
        });
    }

    [Test]
    public void NetworkRuntimeReadyHandler_CanChangeFocusedMapWithoutReenteringActivation()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        int readyEvents = 0;
        engine.TriggerManager.RegisterEventHandler(GameEvents.NetworkRuntimeReady, _ =>
        {
            readyEvents++;
            engine.LoadMap(InnerMapId);
            return Task.CompletedTask;
        });
        engine.Start();

        Assert.That(() => engine.LoadStartupMap(), Throws.Nothing);
        engine.Tick(1f / 30f);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.EqualTo(1));
            Assert.That(readyEvents, Is.EqualTo(1));
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(InnerMapId));
            Assert.That(runtime.PumpTransportCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void NonStartupMapFailure_DoesNotTerminateNetworkStartup()
    {
        using var fixture = MapLoadLifecycleFixture.Create();
        using var engine = fixture.CreateEngine();
        RecordingNetworkRuntime runtime = ConfigureNetworkRuntime(
            engine,
            NetworkProcessRole.AuthoritativeServer);
        engine.SetService(
            CoreServiceKeys.MapLoadCompletionGate,
            new PerMapCompletionGate(
                InnerMapId,
                MapLoadCompletionResult.Failed("inner map rejected"),
                MapLoadCompletionResult.Ready()));
        engine.Start();

        Assert.That(() => engine.LoadMap(InnerMapId), Throws.Nothing);
        engine.LoadStartupMap();
        engine.Tick(1f / 30f);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActivationCount, Is.EqualTo(1));
            Assert.That(runtime.PumpTransportCount, Is.EqualTo(1));
        });
    }

    private static RecordingNetworkRuntime ConfigureNetworkRuntime(
        GameEngine engine,
        NetworkProcessRole role,
        List<string>? lifecycle = null)
    {
        engine.MergedConfig.StartupMapId = MapId;
        engine.MergedConfig.Networking = new NetworkRuntimeConfig();
        var runtime = new RecordingNetworkRuntime(role, lifecycle);
        engine.ConfigureNetworkRuntime(role, runtime);
        return runtime;
    }

    private static int CountMapEntities(World world, string mapId)
    {
        int count = 0;
        world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _, ref MapEntity mapEntity) =>
        {
            if (string.Equals(mapEntity.MapId.Value, mapId, StringComparison.Ordinal))
            {
                count++;
            }
        });

        return count;
    }

    private sealed class ObservingPendingGate : IMapLoadCompletionGate
    {
        public int BeginLoadCalls { get; private set; }
        public string? ObservedMapId { get; private set; }
        public MapSession? ObservedCurrentSession { get; private set; }
        public MapSession? ObservedRequestSession { get; private set; }
        public MapLoadStatus ObservedFocusedStatus { get; private set; }
        public int ObservedEntityCount { get; private set; }

        public IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request)
        {
            BeginLoadCalls++;
            ObservedMapId = request.MapId.Value;
            ObservedCurrentSession = request.Engine.CurrentMapSession;
            ObservedRequestSession = request.Session;
            ObservedFocusedStatus = request.Engine.GetService(CoreServiceKeys.MapLoadStatus);
            ObservedEntityCount = CountMapEntities(request.Engine.World, request.MapId.Value);
            return new AlwaysPendingLoad();
        }

        public IPendingMapLoad BeginPendingResume(in MapResumeCompletionRequest request)
        {
            return new AlwaysPendingLoad();
        }
    }

    private sealed class AlwaysPendingLoad : IPendingMapLoad
    {
        public MapLoadCompletionResult Poll()
        {
            return MapLoadCompletionResult.Pending();
        }

        public void Cancel() { }
    }

    private sealed class SequencePendingGate : IMapLoadCompletionGate
    {
        private readonly MapLoadCompletionResult[] _results;

        public SequencePendingGate(params MapLoadCompletionResult[] results)
        {
            _results = results;
        }

        public IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request)
        {
            return new SequencePendingLoad(_results);
        }

        public IPendingMapLoad BeginPendingResume(in MapResumeCompletionRequest request)
        {
            return new SequencePendingLoad(_results);
        }
    }

    private sealed class SequencePendingLoad : IPendingMapLoad
    {
        private readonly MapLoadCompletionResult[] _results;
        private int _index;

        public SequencePendingLoad(MapLoadCompletionResult[] results)
        {
            _results = results.Length > 0
                ? results
                : throw new ArgumentException("At least one completion result is required.", nameof(results));
        }

        public MapLoadCompletionResult Poll()
        {
            int index = Math.Min(_index, _results.Length - 1);
            _index++;
            return _results[index];
        }

        public void Cancel() { }
    }

    private sealed class PerMapCompletionGate : IMapLoadCompletionGate
    {
        private readonly string _failedMapId;
        private readonly MapLoadCompletionResult _failedResult;
        private readonly MapLoadCompletionResult _otherResult;

        public PerMapCompletionGate(
            string failedMapId,
            MapLoadCompletionResult failedResult,
            MapLoadCompletionResult otherResult)
        {
            _failedMapId = failedMapId;
            _failedResult = failedResult;
            _otherResult = otherResult;
        }

        public IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request)
        {
            MapLoadCompletionResult result = string.Equals(
                request.MapId.Value,
                _failedMapId,
                StringComparison.Ordinal)
                ? _failedResult
                : _otherResult;
            return new SequencePendingLoad(new[] { result });
        }

        public IPendingMapLoad BeginPendingResume(in MapResumeCompletionRequest request)
        {
            return new SequencePendingLoad(new[] { _otherResult });
        }
    }

    private sealed class RecordingNetworkRuntime : INetworkRuntimePort, IPresentationInterpolationSource
    {
        private readonly List<string>? _lifecycle;

        public RecordingNetworkRuntime(NetworkProcessRole role, List<string>? lifecycle)
        {
            Role = role;
            _lifecycle = lifecycle;
        }

        public NetworkProcessRole Role { get; }
        public float InterpolationAlpha => 0f;
        public int ActivationCount { get; private set; }
        public int PumpTransportCount { get; private set; }
        public int BeforeAuthoritativeTickCount { get; private set; }
        public int AfterAuthoritativeCommitCount { get; private set; }
        public Exception? ActivationFailure { get; set; }

        public void Activate()
        {
            ActivationCount++;
            _lifecycle?.Add("activate");
            if (ActivationFailure != null)
            {
                throw ActivationFailure;
            }
        }

        public void PumpTransport()
        {
            PumpTransportCount++;
        }

        public void BeforeAuthoritativeTick(uint executingTick)
        {
            BeforeAuthoritativeTickCount++;
        }

        public void AfterAuthoritativeCommit(uint committedTick)
        {
            AfterAuthoritativeCommitCount++;
        }

        public void PumpReplicatedClient(float frameDeltaTime) { }

        public void Dispose() { }
    }

    private sealed class MapLoadLifecycleFixture : IDisposable
    {
        private MapLoadLifecycleFixture(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static MapLoadLifecycleFixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_MapLoadLifecycleOrderingTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "TestLifecycleMod", "assets", "Entities"));
            Directory.CreateDirectory(Path.Combine(root, "TestLifecycleMod", "assets", "Maps"));

            File.WriteAllText(
                Path.Combine(root, "TestLifecycleMod", "mod.json"),
                """
                {
                  "name": "TestLifecycleMod",
                  "version": "1.0.0",
                  "description": "Asset-only lifecycle ordering fixture",
                  "priority": 0,
                  "dependencies": {}
                }
                """);
            File.WriteAllText(
                Path.Combine(root, "TestLifecycleMod", "assets", "game.json"),
                """
                {
                  "startupMapId": "lifecycle_ordering",
                  "startupInputContexts": [],
                  "presentation": {
                    "performerInstanceCapacity": 16,
                    "gasPresentationEventCapacity": 16,
                    "presentationEventStreamCapacity": 16,
                    "presentationOwnerChangeCapacity": 16,
                    "performerCommandCapacity": 16,
                    "primitiveDrawBufferCapacity": 16,
                    "visualSnapshotBufferCapacity": 16,
                    "visualProxyBufferCapacity": 16,
                    "skinnedVisualBatchCapacity": 16,
                    "presentationRequestCapacity": 16,
                    "groundOverlayCapacity": 16,
                    "roadSplineCapacity": 16,
                    "worldHudCapacity": 16,
                    "screenHudCapacity": 16,
                    "minimapMarkerCapacity": 16,
                    "runtimeEntitySpawnQueueCapacity": 16,
                    "runtimeEntitySpawnReceiptQueueCapacity": 16,
                    "cameraCulling": {
                      "highLodDistanceCm": 1000.0,
                      "mediumLodDistanceCm": 2000.0,
                      "lowLodDistanceCm": 3000.0
                    },
                    "minimap": {
                      "initialZoomNormalized": 1.0,
                      "wheelZoomNormalizedStep": 0.1,
                      "buttonZoomNormalizedStep": 0.2,
                      "zoomSliderEnabled": true,
                      "modeToggleEnabled": true,
                      "rotateToggleEnabled": true,
                      "debugMarkerSampleCapacity": 0,
                      "minZoomExtentMode": "OneChunk",
                      "maxZoomExtentMode": "FullMap",
                      "minZoomExplicitHalfExtentCm": 0.0,
                      "maxZoomExplicitHalfExtentCm": 0.0
                    }
                  },
                  "constants": {
                    "orderTypeIds": {
                      "castAbility": 100,
                      "moveTo": 101,
                      "attackTarget": 102,
                      "stop": 103
                    },
                    "responseChainOrderTypeIds": {
                      "chainPass": 1,
                      "chainNegate": 2,
                      "chainActivateEffect": 3
                    },
                    "attributes": {
                      "health": "Health"
                    }
                  }
                }
                """);
            File.WriteAllText(
                Path.Combine(root, "TestLifecycleMod", "assets", "Entities", "templates.json"),
                $$"""
                [
                  {
                    "id": "{{TemplateId}}",
                    "components": {
                      "Name": { "Value": "Lifecycle Entity" }
                    }
                  }
                ]
                """);
            WriteMap(root, MapId);
            WriteMap(root, InnerMapId);
            return new MapLoadLifecycleFixture(root);
        }

        public GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new List<string>
                {
                    Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                    Path.Combine(Root, "TestLifecycleMod")
                },
                Path.Combine(repoRoot, "assets"));
            return engine;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void WriteMap(string root, string mapId)
        {
            File.WriteAllText(
                Path.Combine(root, "TestLifecycleMod", "assets", "Maps", $"{mapId}.json"),
                $$"""
                {
                  "Id": "{{mapId}}",
                  "Tags": [ "camera.skip_default_on_load" ],
                  "Entities": [
                    { "Template": "{{TemplateId}}" }
                  ]
                }
                """);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "src")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }
    }
}
