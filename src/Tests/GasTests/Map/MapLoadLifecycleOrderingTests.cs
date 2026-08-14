using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Modding;
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
                    "presenterInstanceCapacity": 16,
                    "gasPresentationEventCapacity": 16,
                    "presentationEventStreamCapacity": 16,
                    "presentationOwnerChangeCapacity": 16,
                    "presenterCommandCapacity": 16,
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
                  },
                  "selection": {
                    "targetFilter": { "relationFilter": "All" },
                    "movePathPreviewOrderTypeKeys": [ "moveTo" ]
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
