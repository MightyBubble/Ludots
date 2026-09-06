using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class RenderAssetMapLoadCompletionGateTests
{
    [Test]
    public void Manifest_OwnsSourceUrisAndRejectsMutationAfterSeal()
    {
        string[] sourceUris = { "assets/model.glb" };
        var manifest = new MapPresentationAssetManifest();
        MapPresentationAsset asset = MapPresentationAsset.Create(
            AssetKind.Mesh,
            1,
            VisualRenderPath.StaticMesh,
            sourceUris);
        manifest.Add(in asset);

        sourceUris[0] = "assets/changed.glb";
        asset.SourceUris[0] = "assets/also-changed.glb";
        manifest.SealManifest();

        Assert.That(manifest[0].SourceUris, Is.EqualTo(new[] { "assets/model.glb" }));
        Assert.Throws<InvalidOperationException>(() => manifest.Add(in asset));
    }

    [Test]
    public void Poll_ReportsProgressAndOnlyCompletesAfterEveryRequiredAssetIsResident()
    {
        var residency = new ScriptedResidency();
        residency.Enqueue(1, RenderAssetResidencyState.Preparing, RenderAssetResidencyState.Resident);
        residency.Enqueue(2, RenderAssetResidencyState.Resident, RenderAssetResidencyState.Resident);
        using var gate = new RenderAssetMapLoadCompletionGate(residency);
        MapSession session = CreateSession("progress");
        MapPresentationAssetManifest manifest = CreateManifest(1, 2);

        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!,
            session.MapId,
            session.MapConfig,
            session,
            false,
            manifest));

        MapLoadCompletionResult first = pending.Poll();
        Assert.That(first.State, Is.EqualTo(MapLoadCompletionState.Pending));
        Assert.That(first.RequiredAssetCount, Is.EqualTo(2));
        Assert.That(first.ResidentAssetCount, Is.EqualTo(1));
        Assert.That(first.InFlightAssetCount, Is.EqualTo(1));

        MapLoadCompletionResult second = pending.Poll();
        Assert.That(second.State, Is.EqualTo(MapLoadCompletionState.Ready));
        Assert.That(second.ResidentAssetCount, Is.EqualTo(2));
        Assert.That(residency.EnsureCounts[1], Is.EqualTo(2));
        Assert.That(residency.EnsureCounts[2], Is.EqualTo(1));
        Assert.That(residency.ReleasedAssetIds, Is.Empty);

        gate.Release(session);
        Assert.That(residency.ReleasedAssetIds, Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void Poll_FailureIsStableAndReleasesAssetsAlreadyRetainedByTheMap()
    {
        var residency = new ScriptedResidency();
        residency.Enqueue(1, RenderAssetResidencyState.Resident);
        residency.Enqueue(2, RenderAssetResidencyState.Failed, failure: "missing animation");
        using var gate = new RenderAssetMapLoadCompletionGate(residency);
        MapSession session = CreateSession("failure");
        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!,
            session.MapId,
            session.MapConfig,
            session,
            false,
            CreateManifest(1, 2)));

        MapLoadCompletionResult first = pending.Poll();
        MapLoadCompletionResult second = pending.Poll();

        Assert.That(first.State, Is.EqualTo(MapLoadCompletionState.Failed));
        Assert.That(second, Is.EqualTo(first));
        Assert.That(first.ErrorMessage, Does.Contain("id=2"));
        Assert.That(first.ErrorMessage, Does.Contain("missing animation"));
        Assert.That(residency.ReleasedAssetIds, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void Poll_FailureAlsoReleasesOtherAssetsStillInFlight()
    {
        var residency = new ScriptedResidency();
        residency.Enqueue(1, RenderAssetResidencyState.Preparing);
        residency.Enqueue(2, RenderAssetResidencyState.Failed, failure: "missing model");
        using var gate = new RenderAssetMapLoadCompletionGate(residency);
        MapSession session = CreateSession("failure_with_inflight");
        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!,
            session.MapId,
            session.MapConfig,
            session,
            false,
            CreateManifest(1, 2)));

        MapLoadCompletionResult result = pending.Poll();

        Assert.That(result.State, Is.EqualTo(MapLoadCompletionState.Failed));
        Assert.That(residency.ReleasedAssetIds, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void Poll_PreviouslyPendingAssetFailureReleasesItsRequest()
    {
        var residency = new ScriptedResidency();
        residency.Enqueue(1, RenderAssetResidencyState.Preparing, RenderAssetResidencyState.Failed, "decode failed");
        using var gate = new RenderAssetMapLoadCompletionGate(residency);
        MapSession session = CreateSession("pending_then_failed");
        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!,
            session.MapId,
            session.MapConfig,
            session,
            false,
            CreateManifest(1)));

        Assert.That(pending.Poll().State, Is.EqualTo(MapLoadCompletionState.Pending));
        Assert.That(pending.Poll().State, Is.EqualTo(MapLoadCompletionState.Failed));
        Assert.That(residency.ReleasedAssetIds, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void Cancel_ReleasesAssetsThatWereStillInFlight()
    {
        var residency = new ScriptedResidency();
        residency.Enqueue(1, RenderAssetResidencyState.Preparing);
        using var gate = new RenderAssetMapLoadCompletionGate(residency);
        MapSession session = CreateSession("cancel_inflight");
        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!,
            session.MapId,
            session.MapConfig,
            session,
            false,
            CreateManifest(1)));

        Assert.That(pending.Poll().State, Is.EqualTo(MapLoadCompletionState.Pending));
        pending.Cancel();

        Assert.That(residency.ReleasedAssetIds, Is.EqualTo(new[] { 1 }));
        gate.Release(session);
        Assert.That(residency.ReleasedAssetIds, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void Poll_UnsealedManifestPrewarmsCurrentEntriesButCannotCompleteEarly()
    {
        var residency = new ScriptedResidency();
        residency.Enqueue(1, RenderAssetResidencyState.Resident, RenderAssetResidencyState.Resident);
        using var gate = new RenderAssetMapLoadCompletionGate(residency);
        MapSession session = CreateSession("late_manifest");
        var manifest = new MapPresentationAssetManifest();
        MapPresentationAsset asset = CreateAsset(1);
        manifest.SubmitManifest(in asset);
        IPendingMapLoad pending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!,
            session.MapId,
            session.MapConfig,
            session,
            false,
            manifest));

        Assert.That(pending.Poll().State, Is.EqualTo(MapLoadCompletionState.Pending));

        manifest.SealManifest();
        Assert.That(pending.Poll().State, Is.EqualTo(MapLoadCompletionState.Ready));
    }

    [Test]
    public void Release_OnlyCancelsTheExactSessionForAReusedMapId()
    {
        var residency = new ScriptedResidency();
        residency.Enqueue(1, RenderAssetResidencyState.Resident, RenderAssetResidencyState.Resident);
        using var gate = new RenderAssetMapLoadCompletionGate(residency);
        MapSession oldSession = CreateSession("same_map");
        MapSession newSession = CreateSession("same_map");

        IPendingMapLoad oldPending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, oldSession.MapId, oldSession.MapConfig, oldSession, false, CreateManifest(1)));
        Assert.That(oldPending.Poll().State, Is.EqualTo(MapLoadCompletionState.Ready));

        IPendingMapLoad newPending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, newSession.MapId, newSession.MapConfig, newSession, false, CreateManifest(1)));
        Assert.That(newPending.Poll().State, Is.EqualTo(MapLoadCompletionState.Ready));

        gate.Release(oldSession);
        Assert.That(residency.ReleasedAssetIds, Is.EqualTo(new[] { 1 }));

        gate.Release(newSession);
        Assert.That(residency.ReleasedAssetIds, Is.EqualTo(new[] { 1, 1 }));
    }

    [Test]
    public void SharedResidentAsset_IsAcquiredAndReleasedOncePerMapSession()
    {
        var residency = new ScriptedResidency();
        residency.Enqueue(1, RenderAssetResidencyState.Resident, RenderAssetResidencyState.Resident);
        using var gate = new RenderAssetMapLoadCompletionGate(residency);
        MapSession firstSession = CreateSession("first");
        MapSession secondSession = CreateSession("second");

        IPendingMapLoad firstPending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, firstSession.MapId, firstSession.MapConfig, firstSession, false, CreateManifest(1)));
        IPendingMapLoad secondPending = gate.BeginPendingLoad(new MapLoadCompletionRequest(
            null!, secondSession.MapId, secondSession.MapConfig, secondSession, false, CreateManifest(1)));

        Assert.That(firstPending.Poll().State, Is.EqualTo(MapLoadCompletionState.Ready));
        Assert.That(secondPending.Poll().State, Is.EqualTo(MapLoadCompletionState.Ready));
        Assert.That(residency.EnsureCounts[1], Is.EqualTo(2));

        gate.Release(firstSession);
        gate.Release(secondSession);
        Assert.That(residency.ReleasedAssetIds, Is.EqualTo(new[] { 1, 1 }));
    }

    private static MapSession CreateSession(string id)
    {
        var config = new MapConfig { Id = id };
        return new MapSession(new MapId(id), config);
    }

    private static MapPresentationAssetManifest CreateManifest(params int[] assetIds)
    {
        var manifest = new MapPresentationAssetManifest();
        for (int i = 0; i < assetIds.Length; i++)
        {
            MapPresentationAsset asset = CreateAsset(assetIds[i]);
            manifest.Add(in asset);
        }

        manifest.SealManifest();
        return manifest;
    }

    private static MapPresentationAsset CreateAsset(int assetId) =>
        MapPresentationAsset.Create(
            AssetKind.Mesh,
            assetId,
            VisualRenderPath.StaticMesh,
            new[] { $"assets/model-{assetId}.glb" });

    private sealed class ScriptedResidency : IRenderAssetResidency
    {
        private readonly Dictionary<int, Queue<RenderAssetResidencySnapshot>> _states = new();

        public List<int> ReleasedAssetIds { get; } = new();

        public Dictionary<int, int> EnsureCounts { get; } = new();

        public void Enqueue(int assetId, RenderAssetResidencyState first, RenderAssetResidencyState? second = null, string? failure = null)
        {
            var queue = new Queue<RenderAssetResidencySnapshot>();
            queue.Enqueue(new RenderAssetResidencySnapshot(first, failure));
            if (second.HasValue)
            {
                queue.Enqueue(new RenderAssetResidencySnapshot(second.Value, failure));
            }

            _states.Add(assetId, queue);
        }

        public RenderAssetResidencySnapshot EnsureResident(in MapPresentationAsset asset)
        {
            EnsureCounts.TryGetValue(asset.AssetId, out int count);
            EnsureCounts[asset.AssetId] = count + 1;
            Queue<RenderAssetResidencySnapshot> queue = _states[asset.AssetId];
            RenderAssetResidencySnapshot value = queue.Peek();
            if (queue.Count > 1)
            {
                queue.Dequeue();
            }

            return value;
        }

        public void Release(in MapPresentationAsset asset)
        {
            ReleasedAssetIds.Add(asset.AssetId);
        }
    }
}
