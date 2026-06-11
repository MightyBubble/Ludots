using System;
using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationVisualProxyEmitterContractTests
    {
        [Test]
        public void Emit_PropagatesMaterialCustomData_ToStaticSkinnedAndSurfacePayloads()
        {
            var draw = new PrimitiveDrawBuffer();
            var snapshot = new PrimitiveDrawBuffer();
            var skinned = new SkinnedVisualBatchBuffer();
            var emitter = new PresentationVisualProxyEmitter(draw, snapshotBuffer: snapshot, skinnedBatchBuffer: skinned);

            MaterialCustomData staticData = MaterialCustomData.Empty.WithSlot(0, new Vector4(1f, 2f, 3f, 4f));
            MaterialCustomData skinnedData = MaterialCustomData.Empty.WithSlot(1, new Vector4(5f, 6f, 7f, 8f));
            MaterialCustomData surfaceData = MaterialCustomData.Empty.WithSlot(2, new Vector4(9f, 10f, 11f, 12f));

            emitter.Emit(CreateProxy(101, AssetKind.Mesh, VisualRenderPath.InstancedStaticMesh, staticData));
            emitter.Emit(CreateProxy(202, AssetKind.SkinnedMesh, VisualRenderPath.GpuSkinnedInstance, skinnedData));
            emitter.Emit(CreateProxy(303, AssetKind.Surface, VisualRenderPath.Surface, surfaceData));

            Assert.That(snapshot.Count, Is.EqualTo(3));
            Assert.That(draw.Count, Is.EqualTo(3));
            Assert.That(skinned.Count, Is.EqualTo(1));

            ReadOnlySpan<PrimitiveDrawItem> snapshotSpan = snapshot.GetSpan();
            Assert.That(snapshotSpan[0].MaterialCustomData, Is.EqualTo(staticData));
            Assert.That(snapshotSpan[1].MaterialCustomData, Is.EqualTo(skinnedData));
            Assert.That(snapshotSpan[2].MaterialCustomData, Is.EqualTo(surfaceData));
            Assert.That(snapshotSpan[2].AssetKind, Is.EqualTo(AssetKind.Surface));
            Assert.That(snapshotSpan[2].RenderPath, Is.EqualTo(VisualRenderPath.Surface));
            Assert.That(snapshotSpan[2].SurfaceLayerKey, Is.EqualTo("terrain.visual"));
            Assert.That(snapshotSpan[2].SortId, Is.EqualTo(7));

            ReadOnlySpan<SkinnedVisualBatchItem> skinnedSpan = skinned.GetSpan();
            Assert.That(skinnedSpan[0].StableId, Is.EqualTo(202));
            Assert.That(skinnedSpan[0].MaterialCustomData, Is.EqualTo(skinnedData));
        }

        [Test]
        public void Emit_Throws_WhenVisibleDrawBufferWouldOverflow()
        {
            var draw = new PrimitiveDrawBuffer(capacity: 1);
            var emitter = new PresentationVisualProxyEmitter(draw);

            emitter.Emit(CreateProxy(101, AssetKind.Mesh, VisualRenderPath.InstancedStaticMesh, MaterialCustomData.Empty));

            var ex = Assert.Throws<InvalidOperationException>(
                () => emitter.Emit(CreateProxy(202, AssetKind.Mesh, VisualRenderPath.InstancedStaticMesh, MaterialCustomData.Empty)));

            Assert.That(ex!.Message, Does.Contain("primitive draw buffer overflowed"));
            Assert.That(ex.Message, Does.Contain("stableId=202"));
        }

        private static PresentationVisualProxy CreateProxy(
            int stableId,
            AssetKind assetKind,
            VisualRenderPath renderPath,
            MaterialCustomData materialCustomData)
        {
            return new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                AssetKind = assetKind,
                MeshAssetId = stableId + 10,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                StableId = stableId,
                MaterialId = stableId + 20,
                TemplateId = stableId + 30,
                AnimationProfileId = stableId + 40,
                RenderPath = renderPath,
                Mobility = VisualMobility.Static,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
                SurfaceLayerKey = assetKind == AssetKind.Surface ? "terrain.visual" : string.Empty,
                SortId = assetKind == AssetKind.Surface ? 7 : 0,
                MaterialCustomData = materialCustomData,
            };
        }
    }
}
