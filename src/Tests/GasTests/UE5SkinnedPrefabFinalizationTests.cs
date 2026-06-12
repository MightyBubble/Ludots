using System.Collections.Generic;
using System.Numerics;
using Ludots.Adapter.UE5;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas;

[TestFixture]
public sealed class UE5SkinnedPrefabFinalizationTests
{
    [Test]
    public void UE5IsmRenderBridge_SkinnedBatch_FinalizesPrefabLeavesWithGrounding()
    {
        var meshes = new MeshAssetRegistry();
        int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
        int sphereId = meshes.GetId(WellKnownMeshKeys.Sphere);
        int prefabId = RegisterGroundedPrefab(meshes, cubeId, sphereId);

        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshes);
        engine.SetService(CoreServiceKeys.VisualHeightmap, CreateRuntimeHeightmap());

        var batch = new SkinnedVisualBatchBuffer();
        AnimatorPackedState animator = AnimatorPackedState.Create(controllerId: 77);
        Assert.That(batch.TryAdd(new SkinnedVisualBatchItem
        {
            StableId = 23,
            MeshAssetId = prefabId,
            AnimationProfileId = 9,
            RenderPath = VisualRenderPath.SkinnedMesh,
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
            Color = new Vector4(0.6f, 0.8f, 1f, 1f),
            Animator = animator,
            Visibility = VisualVisibility.Visible,
        }), Is.True);
        engine.SetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer, batch);

        var bridge = new UE5IsmRenderBridge();
        bridge.CollectBuckets(engine);

        AssertGroundedSkinnedLeaves(bridge.AllegroItems, cubeId, sphereId, stableRootId: 23, animatorControllerId: 77);
    }

    [Test]
    public void UE5IsmRenderBridge_SkinnedFallback_FinalizesPrefabLeavesWithGrounding()
    {
        var meshes = new MeshAssetRegistry();
        int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
        int sphereId = meshes.GetId(WellKnownMeshKeys.Sphere);
        int prefabId = RegisterGroundedPrefab(meshes, cubeId, sphereId);

        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshes);
        engine.SetService(CoreServiceKeys.VisualHeightmap, CreateRuntimeHeightmap());

        var snapshot = new PrimitiveDrawBuffer();
        AnimatorPackedState animator = AnimatorPackedState.Create(controllerId: 91);
        Assert.That(snapshot.TryAdd(new PrimitiveDrawItem
        {
            StableId = 41,
            MeshAssetId = prefabId,
            AnimationProfileId = 12,
            RenderPath = VisualRenderPath.SkinnedMesh,
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
            Color = new Vector4(1f, 0.7f, 0.5f, 1f),
            Animator = animator,
            Visibility = VisualVisibility.Visible,
        }), Is.True);
        engine.SetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, snapshot);

        var bridge = new UE5IsmRenderBridge();
        bridge.CollectBuckets(engine);

        AssertGroundedSkinnedLeaves(bridge.AllegroItems, cubeId, sphereId, stableRootId: 41, animatorControllerId: 91);
    }

    [Test]
    public void UE5IsmRenderBridge_WhenGroundedPrefabHasNoVisualHeightmapTruth_ThrowsExplicitly()
    {
        var meshes = new MeshAssetRegistry();
        int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
        int sphereId = meshes.GetId(WellKnownMeshKeys.Sphere);
        int prefabId = RegisterGroundedPrefab(meshes, cubeId, sphereId);

        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshes);

        var batch = new SkinnedVisualBatchBuffer();
        Assert.That(batch.TryAdd(new SkinnedVisualBatchItem
        {
            StableId = 51,
            MeshAssetId = prefabId,
            AnimationProfileId = 9,
            RenderPath = VisualRenderPath.SkinnedMesh,
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
            Color = Vector4.One,
            Animator = AnimatorPackedState.Create(controllerId: 11),
            Visibility = VisualVisibility.Visible,
        }), Is.True);
        engine.SetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer, batch);

        var bridge = new UE5IsmRenderBridge();
        var ex = Assert.Throws<InvalidOperationException>(() => bridge.CollectBuckets(engine));
        Assert.That(ex!.Message, Does.Contain("visual grounding"));
        Assert.That(ex.Message, Does.Contain("unavailable"));
    }

    [Test]
    public void UE5IsmRenderBridge_WhenPrefabFinalizesToTypedNonMeshVisual_ThrowsExplicitly()
    {
        var meshes = new MeshAssetRegistry();
        int prefabId = meshes.Register(
            "prefab.ue5.decal_only",
            MeshAssetDescriptor.Prefab(
                0,
                PrefabPart.Decal(materialId: 17, size: new Vector2(2f, 3f))));

        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshes);

        var snapshot = new PrimitiveDrawBuffer();
        Assert.That(snapshot.TryAdd(new PrimitiveDrawItem
        {
            StableId = 77,
            MeshAssetId = prefabId,
            RenderPath = VisualRenderPath.StaticMesh,
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
            Color = Vector4.One,
            Visibility = VisualVisibility.Visible,
        }), Is.True);
        engine.SetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, snapshot);

        var bridge = new UE5IsmRenderBridge();
        var ex = Assert.Throws<InvalidOperationException>(() => bridge.CollectBuckets(engine));
        Assert.That(ex!.Message, Does.Contain("does not support finalized visual kind 'Decal'"));
    }

    [Test]
    public void UE5IsmRenderBridge_SurfacePrimitive_CollectsSurfaceItemsOutsideStaticBuckets()
    {
        using var engine = new GameEngine();
        var snapshot = new PrimitiveDrawBuffer();
        var customData = new MaterialCustomDataPayload { Count = 2 };
        customData.SetSlot(0, new Vector4(0.25f, 0.5f, 0.75f, 1f));
        customData.SetSlot(1, new Vector4(2f, 3f, 4f, 5f));
        Assert.That(snapshot.TryAdd(new PrimitiveDrawItem
        {
            StableId = 101,
            MeshAssetId = 202,
            MaterialId = 303,
            AssetKind = AssetKind.Surface,
            RenderPath = VisualRenderPath.Surface,
            SurfaceLayerKey = "terrain.rvt",
            SortId = 404,
            Position = new Vector3(1f, 2f, 3f),
            Rotation = Quaternion.Identity,
            Scale = new Vector3(4f, 5f, 6f),
            Visibility = VisualVisibility.Visible,
            MaterialCustomData = customData,
        }), Is.True);
        engine.SetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, snapshot);

        var bridge = new UE5IsmRenderBridge();
        bridge.CollectBuckets(engine);

        Assert.That(bridge.HismBuckets, Is.Empty);
        Assert.That(bridge.AllegroItems, Is.Empty);
        Assert.That(bridge.SurfaceItems.Count, Is.EqualTo(1));
        SurfaceDrawItem item = bridge.SurfaceItems[0];
        Assert.That(item.StableId, Is.EqualTo(101));
        Assert.That(item.MeshAssetId, Is.EqualTo(202));
        Assert.That(item.MaterialId, Is.EqualTo(303));
        Assert.That(item.SurfaceLayerKey, Is.EqualTo("terrain.rvt"));
        Assert.That(item.SortId, Is.EqualTo(404));
        Assert.That(item.Position, Is.EqualTo(new Vector3(100f, 300f, 200f)));
        Assert.That(item.Scale, Is.EqualTo(new Vector3(4f, 6f, 5f)));
        Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));
        Assert.That(item.MaterialCustomData.Count, Is.EqualTo(2));
        Assert.That(item.MaterialCustomData.GetSlot(0), Is.EqualTo(new Vector4(0.25f, 0.5f, 0.75f, 1f)));
        Assert.That(item.MaterialCustomData.GetSlot(1), Is.EqualTo(new Vector4(2f, 3f, 4f, 5f)));
    }

    [Test]
    public void UE5IsmRenderBridge_SurfaceRenderPathWithoutSurfaceAssetKind_ThrowsExplicitly()
    {
        using var engine = new GameEngine();
        var snapshot = new PrimitiveDrawBuffer();
        Assert.That(snapshot.TryAdd(new PrimitiveDrawItem
        {
            StableId = 102,
            MeshAssetId = 202,
            AssetKind = AssetKind.Mesh,
            RenderPath = VisualRenderPath.Surface,
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
            Color = Vector4.One,
            Visibility = VisualVisibility.Visible,
        }), Is.True);
        engine.SetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, snapshot);

        var bridge = new UE5IsmRenderBridge();
        var ex = Assert.Throws<InvalidOperationException>(() => bridge.CollectBuckets(engine));
        Assert.That(ex!.Message, Does.Contain("non-Surface assetKind"));
    }

    private static int RegisterGroundedPrefab(MeshAssetRegistry meshes, int cubeId, int sphereId)
    {
        return meshes.Register(
            "prefab.ue5.skinned_grounded",
            MeshAssetDescriptor.Prefab(
                0,
                new PrefabPart
                {
                    MeshAssetId = cubeId,
                    LocalPosition = new Vector3(1f, 99f, 1f),
                    LocalRotation = Quaternion.Identity,
                    LocalScale = Vector3.One,
                    ColorTint = Vector4.One,
                    Grounding = new PrefabPartGrounding(
                        PrefabPartGroundingMode.VisualHeightmap,
                        verticalOffsetMeters: 0.25f),
                },
                new PrefabPart
                {
                    MeshAssetId = sphereId,
                    LocalPosition = new Vector3(2f, 3f, 4f),
                    LocalRotation = Quaternion.Identity,
                    LocalScale = new Vector3(1.5f, 1.5f, 1.5f),
                    ColorTint = Vector4.One,
                }));
    }

    private static void AssertGroundedSkinnedLeaves(
        IReadOnlyList<AllegroDrawItem> items,
        int cubeId,
        int sphereId,
        int stableRootId,
        int animatorControllerId)
    {
        Assert.That(items.Count, Is.EqualTo(2));

        int groundedStableId = PrefabTransformUtility.BuildChildStableId(stableRootId, depth: 0, childIndex: 0, meshAssetId: cubeId);
        int childStableId = PrefabTransformUtility.BuildChildStableId(stableRootId, depth: 0, childIndex: 1, meshAssetId: sphereId);

        AllegroDrawItem grounded = items[0];
        Assert.That(grounded.MeshAssetId, Is.EqualTo(cubeId));
        Assert.That(grounded.StableId, Is.EqualTo(groundedStableId));
        Assert.That(grounded.RenderPath, Is.EqualTo(VisualRenderPath.SkinnedMesh));
        Assert.That(grounded.Animator.GetControllerId(), Is.EqualTo(animatorControllerId));
        Assert.That(grounded.Position.X, Is.EqualTo(100f).Within(0.001f));
        Assert.That(grounded.Position.Y, Is.EqualTo(100f).Within(0.001f));
        Assert.That(grounded.Position.Z, Is.EqualTo(45f).Within(0.001f), "Grounded child should use finalized heightmap Y before UE axis conversion.");

        AllegroDrawItem child = items[1];
        Assert.That(child.MeshAssetId, Is.EqualTo(sphereId));
        Assert.That(child.StableId, Is.EqualTo(childStableId));
        Assert.That(child.Scale.X, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(child.Scale.Y, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(child.Scale.Z, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(child.Position.X, Is.EqualTo(200f).Within(0.001f));
        Assert.That(child.Position.Y, Is.EqualTo(400f).Within(0.001f));
        Assert.That(child.Position.Z, Is.EqualTo(300f).Within(0.001f));
    }

    private static IVisualHeightmap CreateRuntimeHeightmap()
    {
        return new VisualHeightmapRuntime(
            VisualHeightmapAsset.CreateSingleLayer(
                new WorldAabbCm(0, 0, 1000, 1000),
                sampleColumns: 2,
                sampleRows: 2,
                new short[]
                {
                    0, 100,
                    100, 200,
                }));
    }
}
