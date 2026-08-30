using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Adapter.Raylib;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibBackendSceneTests
{
    [Test]
    public void Catalog_ParsesBackendOwnedSceneWithoutCoreMapFields()
    {
        IReadOnlyList<RaylibBackendSceneDescriptor> descriptors = RaylibBackendSceneCatalog.Parse(
        [
            Entry(
                "gallery.environment",
                """
                {
                  "id": "gallery.environment",
                  "enabled": true,
                  "mapIds": ["gallery.empty"],
                  "sourceUris": ["SceneMod:assets/Scenes/gallery.glb"],
                  "position": [1, 2, 3],
                  "rotation": [0, 0, 0, 1],
                  "scale": [2, 2, 2]
                }
                """),
        ]);

        Assert.That(descriptors, Has.Count.EqualTo(1));
        Assert.That(descriptors[0].AppliesTo("gallery.empty"), Is.True);
        Assert.That(descriptors[0].SourceUris, Is.EqualTo(new[] { "SceneMod:assets/Scenes/gallery.glb" }));
        Assert.That(descriptors[0].Position, Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(descriptors[0].Scale, Is.EqualTo(new Vector3(2, 2, 2)));

        var coreMap = new MapConfig { Id = "gallery.empty" };
        Assert.That(coreMap.Entities, Is.Empty);
        Assert.That(typeof(MapConfig).GetProperty("RaylibScenes"), Is.Null);
    }

    [Test]
    public void Catalog_RejectsUnknownFieldsAndInvalidTransform()
    {
        Assert.Throws<InvalidOperationException>(() => RaylibBackendSceneCatalog.Parse(
        [
            Entry(
                "invalid",
                """
                {
                  "id": "invalid",
                  "mapIds": ["map"],
                  "sourceUris": ["SceneMod:assets/scene.glb"],
                  "position": [0, 0, 0],
                  "rotation": [0, 0, 0, 1],
                  "scale": [1, 0, 1],
                  "silentFallback": true
                }
                """),
        ]));
    }

    [Test]
    public void Catalog_RejectsCandidateUriFallbacks()
    {
        Assert.Throws<InvalidOperationException>(() => RaylibBackendSceneCatalog.Parse(
        [
            Entry(
                "fallback",
                """
                {
                  "id": "fallback",
                  "mapIds": ["map"],
                  "sourceUris": [
                    "SceneMod:assets/scene.glb",
                    "SceneMod:assets/fallback.glb"
                  ],
                  "position": [0, 0, 0],
                  "rotation": [0, 0, 0, 1],
                  "scale": [1, 1, 1]
                }
                """),
        ]));
    }

    [Test]
    public void CompletionGate_HoldsMapUntilCoreAssetsAndBackendSceneAreBothReady()
    {
        var events = new List<string>();
        var core = new ScriptedCoreGate(
            events,
            MapLoadCompletionResult.Pending(2, 1, 1, 0),
            MapLoadCompletionResult.Ready(2, 2, 0, 0));
        var scenes = new ScriptedSceneResidency(
            events,
            new RaylibBackendSceneResidencySnapshot(RaylibBackendSceneState.Preparing, null, 1, 0, 1, 0),
            new RaylibBackendSceneResidencySnapshot(RaylibBackendSceneState.Resident, null, 1, 1, 0, 0));
        using var gate = new RaylibSceneMapLoadCompletionGate(core, scenes);
        MapId mapId = new("timing.map");
        var map = new MapConfig { Id = mapId.Value };
        using var session = new MapSession(mapId, map);

        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, mapId, map, session, false, MapPresentationAssetManifest.Empty));
        MapLoadCompletionResult first = pending.Poll();
        MapLoadCompletionResult second = pending.Poll();

        Assert.That(first.State, Is.EqualTo(MapLoadCompletionState.Pending));
        Assert.That(first.RequiredAssetCount, Is.EqualTo(3));
        Assert.That(first.ResidentAssetCount, Is.EqualTo(1));
        Assert.That(first.InFlightAssetCount, Is.EqualTo(2));
        Assert.That(second.State, Is.EqualTo(MapLoadCompletionState.Ready));
        Assert.That(second.RequiredAssetCount, Is.EqualTo(3));
        Assert.That(second.ResidentAssetCount, Is.EqualTo(3));
        Assert.That(scenes.ReadyMapId, Is.EqualTo(mapId.Value));
        Assert.That(events, Is.EqualTo(new[]
        {
            "scene.begin:timing.map",
            "core.begin:timing.map",
            "scene.poll",
            "core.poll",
            "scene.poll",
            "core.poll",
            "scene.ready:timing.map",
        }));
    }

    [Test]
    public void CompletionGate_BackendFailureCancelsCoreAndFailsLoud()
    {
        var events = new List<string>();
        var core = new ScriptedCoreGate(events, MapLoadCompletionResult.Pending(1, 0, 1, 0));
        var scenes = new ScriptedSceneResidency(
            events,
            new RaylibBackendSceneResidencySnapshot(
                RaylibBackendSceneState.Failed,
                "scene package missing",
                1,
                0,
                0,
                1));
        using var gate = new RaylibSceneMapLoadCompletionGate(core, scenes);
        MapId mapId = new("failed.map");
        var map = new MapConfig { Id = mapId.Value };
        using var session = new MapSession(mapId, map);

        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, mapId, map, session, false, MapPresentationAssetManifest.Empty));
        MapLoadCompletionResult result = pending.Poll();

        Assert.That(result.State, Is.EqualTo(MapLoadCompletionState.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("scene package missing"));
        Assert.That(core.Pending.CancelCount, Is.EqualTo(1));
        Assert.That(events, Is.EqualTo(new[]
        {
            "scene.begin:failed.map",
            "core.begin:failed.map",
            "scene.poll",
            "core.cancel",
            "scene.release:failed.map",
        }));
    }

    [Test]
    public void CompletionGate_CoreFailureReleasesBackendScene()
    {
        var events = new List<string>();
        var core = new ScriptedCoreGate(
            events,
            MapLoadCompletionResult.Failed("core manifest failed", 2, 1, 0, 1));
        var scenes = new ScriptedSceneResidency(
            events,
            new RaylibBackendSceneResidencySnapshot(
                RaylibBackendSceneState.Resident,
                null,
                1,
                1,
                0,
                0));
        using var gate = new RaylibSceneMapLoadCompletionGate(core, scenes);
        MapId mapId = new("core-failed.map");
        var map = new MapConfig { Id = mapId.Value };
        using var session = new MapSession(mapId, map);

        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, mapId, map, session, false, MapPresentationAssetManifest.Empty));
        MapLoadCompletionResult result = pending.Poll();

        Assert.That(result.State, Is.EqualTo(MapLoadCompletionState.Failed));
        Assert.That(events, Is.EqualTo(new[]
        {
            "scene.begin:core-failed.map",
            "core.begin:core-failed.map",
            "scene.poll",
            "core.poll",
            "scene.release:core-failed.map",
        }));
    }

    [Test]
    public void CompletionGate_CancelingPendingLoadReleasesPreviousSceneBeforeStartingNextMap()
    {
        var events = new List<string>();
        var core = new ScriptedCoreGate(
            events,
            MapLoadCompletionResult.Pending(1, 0, 1, 0),
            MapLoadCompletionResult.Pending(1, 0, 1, 0));
        var scenes = new ScriptedSceneResidency(
            events,
            new RaylibBackendSceneResidencySnapshot(RaylibBackendSceneState.Preparing, null, 1, 0, 1, 0),
            new RaylibBackendSceneResidencySnapshot(RaylibBackendSceneState.Preparing, null, 1, 0, 1, 0));
        using var gate = new RaylibSceneMapLoadCompletionGate(core, scenes);
        MapId firstId = new("first.map");
        MapId secondId = new("second.map");
        var firstMap = new MapConfig { Id = firstId.Value };
        var secondMap = new MapConfig { Id = secondId.Value };
        using var firstSession = new MapSession(firstId, firstMap);
        using var secondSession = new MapSession(secondId, secondMap);

        IPendingMapLoad first = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, firstId, firstMap, firstSession, false, MapPresentationAssetManifest.Empty));
        Assert.That(first.Poll().State, Is.EqualTo(MapLoadCompletionState.Pending));

        _ = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, secondId, secondMap, secondSession, false, MapPresentationAssetManifest.Empty));

        Assert.That(events, Is.EqualTo(new[]
        {
            "scene.begin:first.map",
            "core.begin:first.map",
            "scene.poll",
            "core.poll",
            "core.cancel",
            "scene.release:first.map",
            "scene.begin:second.map",
            "core.begin:second.map",
        }));
    }

    [Test]
    public void CompletionGate_ReadyResultIsStableAndDoesNotMarkSceneReadyTwice()
    {
        var events = new List<string>();
        var core = new ScriptedCoreGate(events, MapLoadCompletionResult.Ready(1, 1, 0, 0));
        var scenes = new ScriptedSceneResidency(
            events,
            new RaylibBackendSceneResidencySnapshot(RaylibBackendSceneState.Resident, null, 1, 1, 0, 0));
        using var gate = new RaylibSceneMapLoadCompletionGate(core, scenes);
        MapId mapId = new("ready.map");
        var map = new MapConfig { Id = mapId.Value };
        using var session = new MapSession(mapId, map);

        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, mapId, map, session, false, MapPresentationAssetManifest.Empty));
        Assert.That(pending.Poll().State, Is.EqualTo(MapLoadCompletionState.Ready));
        Assert.That(pending.Poll().State, Is.EqualTo(MapLoadCompletionState.Ready));

        Assert.That(events, Is.EqualTo(new[]
        {
            "scene.begin:ready.map",
            "core.begin:ready.map",
            "scene.poll",
            "core.poll",
            "scene.ready:ready.map",
        }));
    }

    private static MergedConfigEntry Entry(string id, string json)
    {
        return new MergedConfigEntry(
            id,
            JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Test JSON must be an object."));
    }

    private sealed class ScriptedCoreGate : IMapLoadCompletionGate, IDisposable
    {
        private readonly List<string> _events;

        public ScriptedCoreGate(List<string> events, params MapLoadCompletionResult[] results)
        {
            _events = events;
            Pending = new ScriptedPending(events, results);
        }

        public ScriptedPending Pending { get; }

        public IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request)
        {
            _events.Add($"core.begin:{request.MapId.Value}");
            return Pending;
        }

        public IPendingMapLoad BeginPendingResume(in MapResumeCompletionRequest request)
        {
            _events.Add($"core.resume:{request.ResumedSession.MapId.Value}");
            return Pending;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedPending : IPendingMapLoad
    {
        private readonly List<string> _events;
        private readonly MapLoadCompletionResult[] _results;
        private int _index;

        public ScriptedPending(List<string> events, MapLoadCompletionResult[] results)
        {
            _events = events;
            _results = results;
        }

        public int CancelCount { get; private set; }

        public MapLoadCompletionResult Poll()
        {
            _events.Add("core.poll");
            int index = Math.Min(_index, _results.Length - 1);
            _index++;
            return _results[index];
        }

        public void Cancel()
        {
            CancelCount++;
            _events.Add("core.cancel");
        }
    }

    private sealed class ScriptedSceneResidency : IRaylibBackendSceneResidency, IDisposable
    {
        private readonly List<string> _events;
        private readonly RaylibBackendSceneResidencySnapshot[] _results;
        private int _index;

        public ScriptedSceneResidency(
            List<string> events,
            params RaylibBackendSceneResidencySnapshot[] results)
        {
            _events = events;
            _results = results;
        }

        public string? ReadyMapId { get; private set; }

        public void BeginMap(string mapId)
        {
            _events.Add($"scene.begin:{mapId}");
        }

        public RaylibBackendSceneResidencySnapshot Poll()
        {
            _events.Add("scene.poll");
            int index = Math.Min(_index, _results.Length - 1);
            _index++;
            return _results[index];
        }

        public void MarkMapReady(string mapId)
        {
            ReadyMapId = mapId;
            _events.Add($"scene.ready:{mapId}");
        }

        public void Release(string mapId)
        {
            _events.Add($"scene.release:{mapId}");
        }

        public void Dispose()
        {
        }
    }
}
