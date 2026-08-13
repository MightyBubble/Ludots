using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationRequestFlushIncrementalStaticTests
    {
        [Test]
        public void StaticOnlyChange_PatchesSnapshotIncrementally_WithoutFullReProjection()
        {
            World world = World.Create();
            try
            {
                var requests = new PresentationRequestBuffer();
                var stableDrawCache = new StableDrawCache();
                var drawBuffer = new PrimitiveDrawBuffer();
                var snapshotBuffer = new PrimitiveDrawBuffer();
                var proxyBuffer = new PresentationVisualProxyBuffer();
                var skinnedBatchBuffer = new SkinnedVisualBatchBuffer();

                using var flush = new PresentationRequestFlushSystem(
                    world,
                    requests,
                    new PrefabRegistry(),
                    new MeshAssetRegistry(),
                    stableDrawCache,
                    drawBuffer,
                    new GroundOverlayBuffer(),
                    new WorldHudBatchBuffer(),
                    new SplineRibbonBuffer(),
                    snapshotBuffer,
                    proxyBuffer,
                    skinnedBatchBuffer);

                requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, CreateStaticProxy(9001, posX: 1f)));
                requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, CreateStaticProxy(9002, posX: 2f)));
                flush.Update(0.016f);

                Assert.That(snapshotBuffer.Count, Is.EqualTo(2));
                Assert.That(proxyBuffer.Count, Is.EqualTo(2));

                requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, CreateStaticProxy(9001, posX: 99f)));
                requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, CreateStaticProxy(9002, posX: 2f)));
                flush.Update(0.016f);

                Dictionary<int, Vector3> snapshotById = IndexSnapshot(snapshotBuffer);
                Assert.That(snapshotBuffer.Count, Is.EqualTo(2), "Incremental patch must not duplicate or drop residents.");
                Assert.That(snapshotById[9001].X, Is.EqualTo(99f).Within(0.001f), "Snapshot must reflect the moved building.");
                Assert.That(snapshotById[9002].X, Is.EqualTo(2f).Within(0.001f));

                Dictionary<int, Vector3> proxyById = IndexProxy(proxyBuffer);
                Assert.That(proxyById[9001].X, Is.EqualTo(1f).Within(0.001f),
                    "Static-only change must not trigger a full re-projection of every resident instance.");
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void StaticInstanceAddedNextFrame_AppendsToSnapshot_WithoutFullReProjection()
        {
            World world = World.Create();
            try
            {
                var requests = new PresentationRequestBuffer();
                var stableDrawCache = new StableDrawCache();
                var proxyBuffer = new PresentationVisualProxyBuffer();

                using var flush = new PresentationRequestFlushSystem(
                    world,
                    requests,
                    new PrefabRegistry(),
                    new MeshAssetRegistry(),
                    stableDrawCache,
                    new PrimitiveDrawBuffer(),
                    new GroundOverlayBuffer(),
                    new WorldHudBatchBuffer(),
                    new SplineRibbonBuffer(),
                    new PrimitiveDrawBuffer(),
                    proxyBuffer,
                    new SkinnedVisualBatchBuffer());

                requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, CreateStaticProxy(9001, posX: 1f)));
                flush.Update(0.016f);
                int proxyCountAfterFull = proxyBuffer.Count;

                requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, CreateStaticProxy(9001, posX: 1f)));
                requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, CreateStaticProxy(9002, posX: 5f)));
                flush.Update(0.016f);

                Assert.That(stableDrawCache.Count, Is.EqualTo(2));
                Assert.That(proxyBuffer.Count, Is.EqualTo(proxyCountAfterFull),
                    "Appending a static instance must not re-run the full projection that rewrites the proxy buffer.");
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void StaticPayloadChange_PatchesSnapshotIncrementally()
        {
            World world = World.Create();
            try
            {
                var requests = new PresentationRequestBuffer();
                var stableDrawCache = new StableDrawCache();
                var snapshotBuffer = new PrimitiveDrawBuffer();
                var proxyBuffer = new PresentationVisualProxyBuffer();

                using var flush = new PresentationRequestFlushSystem(
                    world,
                    requests,
                    new PrefabRegistry(),
                    new MeshAssetRegistry(),
                    stableDrawCache,
                    new PrimitiveDrawBuffer(),
                    new GroundOverlayBuffer(),
                    new WorldHudBatchBuffer(),
                    new SplineRibbonBuffer(),
                    snapshotBuffer,
                    proxyBuffer,
                    new SkinnedVisualBatchBuffer());

                requests.Add(PresentationRequest.FromVisualProxy(
                    Entity.Null,
                    CreateStaticProxy(9001, posX: 1f, templateId: 1001)));
                flush.Update(0.016f);

                requests.Add(PresentationRequest.FromVisualProxy(
                    Entity.Null,
                    CreateStaticProxy(9001, posX: 1f, templateId: 2002)));
                flush.Update(0.016f);

                Assert.That(snapshotBuffer.Count, Is.EqualTo(1));
                Assert.That(snapshotBuffer.GetSpan()[0].TemplateId, Is.EqualTo(2002));
                Assert.That(proxyBuffer.GetSpan()[0].TemplateId, Is.EqualTo(1001),
                    "Static payload-only changes must use the delta patch path, not a full proxy re-projection.");
            }
            finally
            {
                World.Destroy(world);
            }
        }

        private static Dictionary<int, Vector3> IndexSnapshot(PrimitiveDrawBuffer buffer)
        {
            var map = new Dictionary<int, Vector3>();
            System.ReadOnlySpan<PrimitiveDrawItem> span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                map[span[i].StableId] = span[i].Position;
            }

            return map;
        }

        private static Dictionary<int, Vector3> IndexProxy(PresentationVisualProxyBuffer buffer)
        {
            var map = new Dictionary<int, Vector3>();
            System.ReadOnlySpan<PresentationVisualProxy> span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                map[span[i].StableId] = span[i].Position;
            }

            return map;
        }

        private static PresentationVisualProxy CreateStaticProxy(int stableId, float posX, int? templateId = null)
        {
            return new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 10,
                MaterialId = 1,
                StableId = stableId,
                TemplateId = templateId ?? 1000 + stableId,
                Position = new Vector3(posX, 0f, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = new Vector4(1f, 1f, 1f, 1f),
                RenderPath = VisualRenderPath.InstancedStaticMesh,
                Mobility = VisualMobility.Static,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
            };
        }
    }
}
