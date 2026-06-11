using System;
using System.Numerics;
using Ludots.Adapter.UE5;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace GasTests
{
    [TestFixture]
    public sealed class UE5SurfacePerformerLaneContractTests
    {
        [Test]
        public void CollectBuckets_RoutesSurfaceItemsSeparately_FromStaticMeshBuckets()
        {
            using var engine = new GameEngine();
            var meshes = new MeshAssetRegistry();
            int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
            var snapshot = new PrimitiveDrawBuffer();
            MaterialCustomData surfaceData = MaterialCustomData.Empty.WithSlot(0, new Vector4(1f, 2f, 3f, 4f));

            Assert.That(snapshot.TryAdd(CreateItem(
                stableId: 101,
                assetKind: AssetKind.Mesh,
                renderPath: VisualRenderPath.InstancedStaticMesh,
                meshAssetId: cubeId,
                materialId: 11)), Is.True);
            Assert.That(snapshot.TryAdd(CreateItem(
                stableId: 202,
                assetKind: AssetKind.Surface,
                renderPath: VisualRenderPath.Surface,
                meshAssetId: cubeId,
                materialId: 22,
                surfaceLayerKey: "terrain.visual",
                sortId: 7,
                materialCustomData: surfaceData)), Is.True);

            engine.SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshes);
            engine.SetService(CoreServiceKeys.PresentationVisualSnapshotBuffer, snapshot);

            var bridge = new UE5IsmRenderBridge();
            bridge.CollectBuckets(engine);

            Assert.That(bridge.HismBuckets.Count, Is.EqualTo(1));
            Assert.That(bridge.HismBuckets[0].Instances.Count, Is.EqualTo(1));
            Assert.That(bridge.HismBuckets[0].Instances[0].StableId, Is.EqualTo(101));
            Assert.That(bridge.SurfaceItems.Count, Is.EqualTo(1));
            Assert.That(bridge.SurfaceItems[0].StableId, Is.EqualTo(202));
            Assert.That(bridge.SurfaceItems[0].SurfaceLayerKey, Is.EqualTo("terrain.visual"));
            Assert.That(bridge.SurfaceItems[0].SortId, Is.EqualTo(7));
            Assert.That(bridge.SurfaceItems[0].MaterialCustomData, Is.EqualTo(surfaceData));
            Assert.That(bridge.SurfaceItems[0].Position, Is.EqualTo(new Vector3(100f, 300f, 200f)));
        }

        [Test]
        public void CollectBuckets_Throws_WhenSurfacePathCarriesNonSurfaceAssetKind()
        {
            using var engine = new GameEngine();
            var meshes = new MeshAssetRegistry();
            var snapshot = new PrimitiveDrawBuffer();
            Assert.That(snapshot.TryAdd(CreateItem(
                stableId: 101,
                assetKind: AssetKind.Mesh,
                renderPath: VisualRenderPath.Surface,
                meshAssetId: meshes.GetId(WellKnownMeshKeys.Cube),
                materialId: 11)), Is.True);

            engine.SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshes);
            engine.SetService(CoreServiceKeys.PresentationVisualSnapshotBuffer, snapshot);

            var bridge = new UE5IsmRenderBridge();
            var ex = Assert.Throws<InvalidOperationException>(() => bridge.CollectBuckets(engine));
            Assert.That(ex!.Message, Does.Contain("non-Surface"));
        }

        [Test]
        public void CollectBuckets_PropagatesMaterialCustomData_ToStaticAndSkinnedAdapterItems()
        {
            using var engine = new GameEngine();
            var meshes = new MeshAssetRegistry();
            int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
            var snapshot = new PrimitiveDrawBuffer();
            var skinned = new SkinnedVisualBatchBuffer();
            MaterialCustomData staticData = MaterialCustomData.Empty.WithSlot(1, new Vector4(2f, 3f, 4f, 5f));
            MaterialCustomData skinnedData = MaterialCustomData.Empty.WithSlot(2, new Vector4(6f, 7f, 8f, 9f));

            Assert.That(snapshot.TryAdd(CreateItem(
                stableId: 301,
                assetKind: AssetKind.Mesh,
                renderPath: VisualRenderPath.InstancedStaticMesh,
                meshAssetId: cubeId,
                materialId: 11,
                materialCustomData: staticData)), Is.True);
            Assert.That(skinned.TryAdd(new SkinnedVisualBatchItem
            {
                AssetKind = AssetKind.SkinnedMesh,
                StableId = 401,
                MeshAssetId = cubeId,
                MaterialId = 22,
                TemplateId = 33,
                AnimationProfileId = 44,
                RenderPath = VisualRenderPath.GpuSkinnedInstance,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                Visibility = VisualVisibility.Visible,
                MaterialCustomData = skinnedData,
            }), Is.True);

            engine.SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshes);
            engine.SetService(CoreServiceKeys.PresentationVisualSnapshotBuffer, snapshot);
            engine.SetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer, skinned);

            var bridge = new UE5IsmRenderBridge();
            bridge.CollectBuckets(engine);

            Assert.That(bridge.HismBuckets.Count, Is.EqualTo(1));
            Assert.That(bridge.HismBuckets[0].Instances[0].MaterialCustomData, Is.EqualTo(staticData));
            Assert.That(bridge.AllegroItems.Count, Is.EqualTo(1));
            Assert.That(bridge.AllegroItems[0].MaterialCustomData, Is.EqualTo(skinnedData));
        }

        private static PrimitiveDrawItem CreateItem(
            int stableId,
            AssetKind assetKind,
            VisualRenderPath renderPath,
            int meshAssetId,
            int materialId,
            string surfaceLayerKey = "",
            int sortId = 0,
            MaterialCustomData materialCustomData = default)
        {
            return new PrimitiveDrawItem
            {
                AssetKind = assetKind,
                MeshAssetId = meshAssetId,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                StableId = stableId,
                MaterialId = materialId,
                TemplateId = stableId + 1000,
                RenderPath = renderPath,
                Mobility = VisualMobility.Static,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
                SurfaceLayerKey = surfaceLayerKey,
                SortId = sortId,
                MaterialCustomData = materialCustomData,
            };
        }
    }
}
