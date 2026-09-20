using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Mixed-scene contracts for the transient/static dual-bucket projection targets:
    /// a frame carrying transient (Movable/skinned) proxies must only rebuild the transient
    /// bucket, while the static projection is patched exclusively through its own revision
    /// gates (full re-projection or ApplyStaticInstanceDelta).
    /// </summary>
    [TestFixture]
    public sealed class PresentationRequestFlushMixedTransientStaticTests
    {
        private const int StaticCount = 5000;
        private const int MovableCount = 100;
        private const int StaticStableIdBase = 100000;
        private const int MovableStableIdBase = 200000;

        [Test]
        public void MixedFrame_StaticChangeWhileTransientsFlow_PatchesDeltaWithoutFullReProjection()
        {
            World world = World.Create();
            try
            {
                FlushFixture fixture = FlushFixture.Create(world);
                foreach (int stableId in StaticIds())
                {
                    fixture.Requests.AddVisualProxy(Entity.Null, CreateStaticProxy(stableId, posX: 1f));
                }

                fixture.Flush.Update(0.016f);
                Assert.That(fixture.Snapshot.Count, Is.EqualTo(StaticCount));
                Assert.That(fixture.ProxyBuffer.Count, Is.EqualTo(StaticCount));

                for (int i = 0; i < MovableCount; i++)
                {
                    fixture.Requests.AddVisualProxy(
                        Entity.Null,
                        CreateMovableProxy(MovableStableIdBase + i, posX: 10f + i));
                }

                fixture.Requests.AddVisualProxy(Entity.Null, CreateStaticProxy(StaticStableIdBase, posX: 99f));
                fixture.Flush.Update(0.016f);

                Dictionary<int, Vector3> snapshotById = IndexSnapshot(fixture.Snapshot);
                Dictionary<int, Vector3> proxyById = IndexProxy(fixture.ProxyBuffer);
                Assert.That(fixture.Snapshot.Count, Is.EqualTo(StaticCount + MovableCount),
                    "Mixed frame must keep every static resident and append the transient bucket.");
                Assert.That(snapshotById[StaticStableIdBase].X, Is.EqualTo(99f).Within(0.001f),
                    "Static change must patch the snapshot through the delta path.");
                Assert.That(snapshotById[MovableStableIdBase].X, Is.EqualTo(10f).Within(0.001f),
                    "Transient projection must reach the snapshot in the same frame.");
                Assert.That(fixture.ProxyBuffer.Count, Is.EqualTo(StaticCount + MovableCount));
                Assert.That(proxyById[StaticStableIdBase].X, Is.EqualTo(1f).Within(0.001f),
                    "Transient presence must not trigger a full re-projection of the static bucket.");
                Assert.That(fixture.DrawBuffer.Count, Is.EqualTo(StaticCount + MovableCount));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void TransientsStopEmitting_NextFrameDropsTransientBucketAndKeepsStaticProjection()
        {
            World world = World.Create();
            try
            {
                FlushFixture fixture = FlushFixture.Create(world);
                foreach (int stableId in StaticIds())
                {
                    fixture.Requests.AddVisualProxy(Entity.Null, CreateStaticProxy(stableId, posX: 1f));
                }

                fixture.Flush.Update(0.016f);

                for (int frame = 0; frame < 3; frame++)
                {
                    for (int i = 0; i < MovableCount; i++)
                    {
                        fixture.Requests.AddVisualProxy(
                            Entity.Null,
                            CreateMovableProxy(MovableStableIdBase + i, posX: 10f + i + frame));
                    }

                    fixture.Flush.Update(0.016f);
                }

                fixture.Flush.Update(0.016f);

                Assert.That(fixture.DrawBuffer.Count, Is.EqualTo(StaticCount),
                    "The frame after the last transient frame must drop only the transient bucket.");
                Assert.That(fixture.Snapshot.Count, Is.EqualTo(StaticCount),
                    "The static projection must persist without being rebuilt.");
                Assert.That(fixture.ProxyBuffer.Count, Is.EqualTo(StaticCount));
                Dictionary<int, Vector3> snapshotById = IndexSnapshot(fixture.Snapshot);
                Assert.That(snapshotById[StaticStableIdBase].X, Is.EqualTo(1f).Within(0.001f),
                    "Static residents must survive the transient bucket teardown unchanged.");
                Assert.That(snapshotById.ContainsKey(MovableStableIdBase), Is.False);
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        [Category("benchmark")]
        public void MixedSteadyState_FlushCostTracksTransientBucketOnly()
        {
            World world = World.Create();
            try
            {
                FlushFixture fixture = FlushFixture.Create(world);
                foreach (int stableId in StaticIds())
                {
                    fixture.Requests.AddVisualProxy(Entity.Null, CreateStaticProxy(stableId, posX: 1f));
                }

                fixture.Flush.Update(0.016f);

                for (int frame = 0; frame < 24; frame++)
                {
                    EnqueueMovables(fixture.Requests, frame);
                    fixture.Flush.Update(0.016f);
                }

                const int SteadyFrames = 200;
                double steadyMs = MeasureFrames(fixture, SteadyFrames, bumpTargetGeneration: false);
                const int FullProjectionFrames = 40;
                double fullMs = MeasureFrames(fixture, FullProjectionFrames, bumpTargetGeneration: true);

                TestContext.Out.WriteLine(
                    $"[Benchmark] MixedFlush statics={StaticCount} movables={MovableCount}: steady avg {steadyMs:F4} ms vs full-projection avg {fullMs:F4} ms");

                Assert.That(steadyMs * 5d, Is.LessThan(fullMs),
                    "Steady mixed flush must only pay for the transient bucket, far below a full static re-projection frame.");
            }
            finally
            {
                World.Destroy(world);
            }
        }

        private static double MeasureFrames(FlushFixture fixture, int frames, bool bumpTargetGeneration)
        {
            double totalMs = 0d;
            for (int frame = 0; frame < frames; frame++)
            {
                EnqueueMovables(fixture.Requests, frame);
                if (bumpTargetGeneration)
                {
                    fixture.TargetGeneration.MarkTargetChanged();
                }

                long start = Stopwatch.GetTimestamp();
                fixture.Flush.Update(0.016f);
                totalMs += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            }

            return totalMs / frames;
        }

        private static void EnqueueMovables(PresentationRequestBuffer requests, int frame)
        {
            for (int i = 0; i < MovableCount; i++)
            {
                requests.AddVisualProxy(
                    Entity.Null,
                    CreateMovableProxy(MovableStableIdBase + i, posX: 10f + i + frame * 0.25f));
            }
        }

        private static IEnumerable<int> StaticIds()
        {
            for (int i = 0; i < StaticCount; i++)
            {
                yield return StaticStableIdBase + i;
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

        private static PresentationVisualProxy CreateStaticProxy(int stableId, float posX)
        {
            return new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 10,
                MaterialId = 1,
                StableId = stableId,
                TemplateId = 1000 + stableId,
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

        private static PresentationVisualProxy CreateMovableProxy(int stableId, float posX)
        {
            PresentationVisualProxy proxy = CreateStaticProxy(stableId, posX);
            proxy.Mobility = VisualMobility.Movable;
            return proxy;
        }

        private sealed class FlushFixture
        {
            private FlushFixture(
                PresentationRequestFlushSystem flush,
                PresentationRequestBuffer requests,
                PrimitiveDrawBuffer drawBuffer,
                PrimitiveDrawBuffer snapshot,
                PresentationVisualProxyBuffer proxyBuffer,
                PresentationTargetGeneration targetGeneration)
            {
                Flush = flush;
                Requests = requests;
                DrawBuffer = drawBuffer;
                Snapshot = snapshot;
                ProxyBuffer = proxyBuffer;
                TargetGeneration = targetGeneration;
            }

            public PresentationRequestFlushSystem Flush { get; }
            public PresentationRequestBuffer Requests { get; }
            public PrimitiveDrawBuffer DrawBuffer { get; }
            public PrimitiveDrawBuffer Snapshot { get; }
            public PresentationVisualProxyBuffer ProxyBuffer { get; }
            public PresentationTargetGeneration TargetGeneration { get; }

            public static FlushFixture Create(World world)
            {
                var requests = new PresentationRequestBuffer();
                var drawBuffer = new PrimitiveDrawBuffer(16384);
                var snapshot = new PrimitiveDrawBuffer(16384);
                var proxyBuffer = new PresentationVisualProxyBuffer(16384);
                var targetGeneration = new PresentationTargetGeneration();
                var flush = new PresentationRequestFlushSystem(
                    world,
                    requests,
                    new MeshAssetRegistry(),
                    new StableDrawCache(16384),
                    drawBuffer,
                    new GroundOverlayBuffer(),
                    new WorldHudBatchBuffer(),
                    new SplineRibbonBuffer(),
                    snapshot,
                    proxyBuffer,
                    new SkinnedVisualBatchBuffer(),
                    targetGeneration: targetGeneration);
                return new FlushFixture(flush, requests, drawBuffer, snapshot, proxyBuffer, targetGeneration);
            }
        }
    }
}
