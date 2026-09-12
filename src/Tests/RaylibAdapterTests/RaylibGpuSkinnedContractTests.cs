using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibGpuSkinnedContractTests
{
    private sealed class AssetPathResolver : IRenderAssetPathResolver
    {
        private readonly string _root;

        public AssetPathResolver(string root)
        {
            _root = root;
        }

        public bool TryResolveFullPath(string uri, out string fullPath)
        {
            fullPath = Path.Combine(_root, uri.Replace("mod:", string.Empty, StringComparison.Ordinal));
            return true;
        }
    }

    [Test]
    public void Capacity_RequiresExplicitPositiveLimits()
    {
        Assert.DoesNotThrow(() => new RaylibGpuSkinnedCapacity(10_000, 8, 128, 32).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RaylibGpuSkinnedCapacity(0, 8, 128, 32).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RaylibGpuSkinnedCapacity(10_000, 0, 128, 32).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RaylibGpuSkinnedCapacity(10_000, 8, 0, 32).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RaylibGpuSkinnedCapacity(10_000, 8, 128, RaylibGpuSkinnedCapacity.MaxBoneSlotCapacity + 1).Validate());
    }

    [Test]
    public void BatchRenderer_PreallocatesConfiguredBatchSlots()
    {
        using var cache = new RaylibGpuSkinnedModelCache(vfs: null);
        using var renderer = new RaylibGpuSkinnedBatchRenderer(
            cache,
            new RaylibInstancedMaterialPipeline(materialLibrary: null),
            maxModelInstancesPerDraw: 10_000,
            new RaylibGpuSkinnedCapacity(10_000, 7, 128, 32));

        Assert.That(renderer.BatchSlotCapacity, Is.EqualTo(7));
        Assert.That(renderer.RegisteredBatchCount, Is.Zero);
    }

    [Test]
    public void BatchRenderer_DeviceInitializationRequiresExplicitCapacityBeforeNativeAccess()
    {
        using var cache = new RaylibGpuSkinnedModelCache(vfs: null);
        using var renderer = new RaylibGpuSkinnedBatchRenderer(
            cache,
            new RaylibInstancedMaterialPipeline(materialLibrary: null),
            maxModelInstancesPerDraw: 10_000,
            capacity: null);

        Assert.That(renderer.DeviceResourcesInitialized, Is.False);
        Assert.That(
            renderer.InitializeDeviceResources,
            Throws.InvalidOperationException.With.Message.Contains("explicit capacity"));
    }

    [Test]
    public void BatchRenderer_DeviceInitializationCannotAllocateInsideActiveFrame()
    {
        using var cache = new RaylibGpuSkinnedModelCache(vfs: null);
        using var renderer = new RaylibGpuSkinnedBatchRenderer(
            cache,
            new RaylibInstancedMaterialPipeline(materialLibrary: null),
            maxModelInstancesPerDraw: 10_000,
            new RaylibGpuSkinnedCapacity(10_000, 8, 128, 32));

        renderer.BeginFrame();
        try
        {
            Assert.That(
                renderer.InitializeDeviceResources,
                Throws.InvalidOperationException.With.Message.Contains("outside an active frame"));
        }
        finally
        {
            renderer.EndFrame();
        }
    }

    [Test]
    public void MeshSubmissionStats_CountChunkedDrawsAndInstancedTriangles()
    {
        RaylibGpuSkinnedBatchRenderer.GpuSkinnedMeshSubmission submission =
            RaylibGpuSkinnedBatchRenderer.CalculateMeshSubmission(
                instanceCount: 10_001,
                triangleCount: 240,
                maxInstancesPerDraw: 10_000);

        Assert.That(submission.DrawCalls, Is.EqualTo(2));
        Assert.That(submission.Triangles, Is.EqualTo(2_400_240));
        Assert.That(
            RaylibGpuSkinnedBatchRenderer.CalculateMeshSubmission(25, 0, 10).DrawCalls,
            Is.EqualTo(3));
    }

    [Test]
    public void BatchRenderer_FrameStateRequiresOneSealAndCanBeReusedAfterEnd()
    {
        using var cache = new RaylibGpuSkinnedModelCache(vfs: null);
        using var renderer = new RaylibGpuSkinnedBatchRenderer(
            cache,
            new RaylibInstancedMaterialPipeline(materialLibrary: null),
            maxModelInstancesPerDraw: 10_000,
            new RaylibGpuSkinnedCapacity(10_000, 8, 128, 32));

        renderer.BeginFrame();
        Assert.Throws<InvalidOperationException>(renderer.BeginFrame);
        renderer.SealFrame();
        Assert.That(renderer.FramePrepared, Is.True);
        Assert.Throws<InvalidOperationException>(renderer.SealFrame);

        renderer.EndFrame();
        Assert.That(renderer.FramePrepared, Is.False);
        Assert.DoesNotThrow(renderer.BeginFrame);
        renderer.EndFrame();
    }

    [Test]
    public void BatchRenderer_EmptyPrepareFrames_AreZeroAllocationAndReportWholePrepareTiming()
    {
        using var cache = new RaylibGpuSkinnedModelCache(vfs: null);
        using var renderer = new RaylibGpuSkinnedBatchRenderer(
            cache,
            new RaylibInstancedMaterialPipeline(materialLibrary: null),
            maxModelInstancesPerDraw: 10_000,
            new RaylibGpuSkinnedCapacity(10_000, 8, 128, 32));

        renderer.BeginFrame();
        renderer.SealFrame();
        renderer.EndFrame();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            renderer.BeginFrame();
            renderer.SealFrame();
            renderer.EndFrame();
        }
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.That(allocatedBytes, Is.Zero);
        Assert.That(renderer.LastPrepareCpuMs, Is.GreaterThanOrEqualTo(0d));
        Assert.That(renderer.LastCollectedUniqueInstances, Is.Zero);
        Assert.That(renderer.LastCollectedHighLodInstances, Is.Zero);
        Assert.That(renderer.LastCollectedMediumLodInstances, Is.Zero);
        Assert.That(renderer.LastCollectedLowLodInstances, Is.Zero);
        Assert.That(renderer.LastMainDrawCalls, Is.Zero);
        Assert.That(renderer.LastShadowDrawCalls, Is.Zero);
    }

    [Test]
    public void StableIdIndex_RejectsSameFrameDuplicateAndReusesCapacityNextFrame()
    {
        var index = new RaylibGpuSkinnedBatchRenderer.StableIdFrameIndex(maxEntries: 4);

        index.BeginFrame();
        index.Add(11);
        index.Add(19);
        index.Add(27);
        index.Add(35);
        Assert.That(index.Count, Is.EqualTo(4));
        Assert.That(
            () => index.Add(19),
            Throws.InvalidOperationException.With.Message.Contains("StableId=19"));
        Assert.That(
            () => index.Add(43),
            Throws.InvalidOperationException.With.Message.Contains("configured maxEntries=4"));

        index.BeginFrame();
        Assert.DoesNotThrow(() => index.Add(19));
        Assert.That(index.Count, Is.EqualTo(1));
        Assert.That(
            () => index.Add(0),
            Throws.InvalidOperationException.With.Message.Contains("positive StableId"));
    }

    [Test]
    public void PreparedFrame_RejectsDifferentSnapshotMeshRegistryOrScale()
    {
        using var renderer = new RaylibPrimitiveRenderer(
            gpuSkinnedCapacity: new RaylibGpuSkinnedCapacity(4, 2, 2, 4));
        var snapshot = new SkinnedVisualBatchBuffer(4);
        var meshes = new MeshAssetRegistry();

        renderer.PrepareSkinnedFrame(snapshot, meshes, scaleMul: 1f);
        try
        {
            Assert.DoesNotThrow(() => renderer.ValidatePreparedSkinnedFrame(snapshot, meshes, 1f));
            Assert.Throws<InvalidOperationException>(() =>
                renderer.ValidatePreparedSkinnedFrame(new SkinnedVisualBatchBuffer(4), meshes, 1f));
            Assert.Throws<InvalidOperationException>(() =>
                renderer.ValidatePreparedSkinnedFrame(snapshot, new MeshAssetRegistry(), 1f));
            Assert.Throws<InvalidOperationException>(() =>
                renderer.ValidatePreparedSkinnedFrame(snapshot, meshes, 2f));
        }
        finally
        {
            renderer.EndSkinnedFrame();
        }
    }

    [Test]
    public void AnimationBindings_MapMassNavigationIdleAndWalkStatesToModelClips()
    {
        string root = Path.Combine(Path.GetTempPath(), $"raylib-animation-bindings-{Guid.NewGuid():N}");
        var resolver = new AssetPathResolver(root);
        var meshes = new MeshAssetRegistry();
        int meshId = meshes.Register("soldier", MeshAssetDescriptor.Model(0, "mod:soldier.glb"));

        var clips = new AnimationClipRegistry();
        int idleClipId = clips.Register(
            "soldier.idle",
            new AnimationClipDefinition
            {
                Locators = [new AnimationClipLocatorDefinition("raylib", "mod:soldier.glb#anim:5")],
            });
        int walkClipId = clips.Register(
            "soldier.walk",
            new AnimationClipDefinition
            {
                Locators = [new AnimationClipLocatorDefinition("raylib", "mod:soldier.glb#anim:6")],
            });

        var profiles = new AnimationProfileRegistry();
        int profileId = profiles.Register(
            "soldier",
            new AnimationProfileDefinition
            {
                StateClips =
                [
                    new AnimationStateClipBinding { PackedStateIndex = 41, ClipAssetId = idleClipId },
                    new AnimationStateClipBinding { PackedStateIndex = 42, ClipAssetId = walkClipId },
                ],
            });

        var bindings = new RaylibAnimationProfileBindings(profiles, clips, meshes, resolver);
        IReadOnlyDictionary<int, int>? stateMap = bindings.Resolve(profileId, meshId);

        Assert.That(stateMap, Is.Not.Null);
        Assert.That(stateMap![41], Is.EqualTo(5));
        Assert.That(stateMap[42], Is.EqualTo(6));
    }
}
