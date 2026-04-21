using System.Collections.Generic;
using System.Numerics;
using Ludots.Adapter.UE5;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
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
    public void UE5IsmRenderBridge_MixedStaticPrefab_ForwardsTypedVisualRequests()
    {
        var meshes = new MeshAssetRegistry();
        int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
        int prefabId = meshes.Register(
            "prefab.ue5.mixed",
            MeshAssetDescriptor.Prefab(
                0,
                new PrefabPart
                {
                    Kind = PrefabPartKind.Mesh,
                    MeshAssetId = cubeId,
                    LocalPosition = Vector3.Zero,
                    LocalRotation = Quaternion.Identity,
                    LocalScale = Vector3.One,
                    ColorTint = Vector4.One,
                },
                new PrefabPart
                {
                    Kind = PrefabPartKind.Decal,
                    AssetKey = "decal.scorch",
                    MaterialKey = "mat.scorch",
                    LocalPosition = new Vector3(1f, 0f, 2f),
                    LocalRotation = Quaternion.Identity,
                    LocalScale = Vector3.One,
                    ColorTint = Vector4.One,
                }));

        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshes);
        engine.SetService(CoreServiceKeys.VisualHeightmap, new FlatVisualHeightmap());
        var visualRequests = new PresentationVisualRequestBuffer();
        engine.SetService(CoreServiceKeys.PresentationVisualRequestBuffer, visualRequests);
        engine.SetService(
            CoreServiceKeys.PresentationAdapterCapabilities,
            new PresentationAdapterCapabilities(PresentationVisualCapabilities.Decal));

        var snapshot = new PrimitiveDrawBuffer();
        Assert.That(snapshot.TryAdd(new PrimitiveDrawItem
        {
            StableId = 123,
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
        bridge.CollectBuckets(engine);

        Assert.That(bridge.HismBuckets.Count, Is.EqualTo(1));
        Assert.That(bridge.HismBuckets[0].MeshAssetId, Is.EqualTo(cubeId));
        Assert.That(visualRequests.Count, Is.EqualTo(1));
        var request = visualRequests.GetSpan()[0];
        Assert.That(request.Kind, Is.EqualTo(PresentationVisualRequestKind.Decal));
        Assert.That(request.AssetKey, Is.EqualTo("decal.scorch"));
        Assert.That(request.MaterialKey, Is.EqualTo("mat.scorch"));
    }

    [Test]
    public void UE5IsmRenderBridge_MixedStaticPrefab_RejectsUnsupportedTypedCapability()
    {
        var meshes = new MeshAssetRegistry();
        int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
        int prefabId = meshes.Register(
            "prefab.ue5.unsupported_decal",
            MeshAssetDescriptor.Prefab(
                0,
                new PrefabPart
                {
                    Kind = PrefabPartKind.Mesh,
                    MeshAssetId = cubeId,
                    LocalRotation = Quaternion.Identity,
                    LocalScale = Vector3.One,
                    ColorTint = Vector4.One,
                },
                new PrefabPart
                {
                    Kind = PrefabPartKind.Decal,
                    AssetKey = "decal.unsupported",
                    LocalRotation = Quaternion.Identity,
                    LocalScale = Vector3.One,
                    ColorTint = Vector4.One,
                }));

        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshes);
        engine.SetService(CoreServiceKeys.VisualHeightmap, new FlatVisualHeightmap());
        engine.SetService(CoreServiceKeys.PresentationVisualRequestBuffer, new PresentationVisualRequestBuffer());
        engine.SetService(
            CoreServiceKeys.PresentationAdapterCapabilities,
            new PresentationAdapterCapabilities(PresentationVisualCapabilities.None));

        var snapshot = new PrimitiveDrawBuffer();
        Assert.That(snapshot.TryAdd(new PrimitiveDrawItem
        {
            StableId = 321,
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
        var ex = Assert.Throws<System.InvalidOperationException>(() => bridge.CollectBuckets(engine));
        Assert.That(ex!.Message, Does.Contain("does not support"));
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
