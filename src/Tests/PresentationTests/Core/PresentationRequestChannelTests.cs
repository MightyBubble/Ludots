using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationRequestChannelTests
    {
        [Test]
        public void ChannelElementSizes_AreEachNarrowerThanFatPresentationRequest()
        {
            int fat = Unsafe.SizeOf<PresentationRequest>();
            Assert.That(Unsafe.SizeOf<VisualProxyChannelItem>(), Is.LessThan(fat));
            Assert.That(Unsafe.SizeOf<GroundOverlayChannelItem>(), Is.LessThan(fat));
            Assert.That(Unsafe.SizeOf<WorldHudChannelItem>(), Is.LessThan(fat));
            Assert.That(Unsafe.SizeOf<SplineRibbonChannelItem>(), Is.LessThan(fat));
            Assert.That(Unsafe.SizeOf<SurfaceSourceChannelItem>(), Is.LessThan(fat));
            Assert.That(Unsafe.SizeOf<PresentationRemovalRequest>(), Is.LessThan(fat));
            Assert.That(Unsafe.SizeOf<PresentationRequestOp>(), Is.LessThan(fat));
            Assert.That(Unsafe.SizeOf<Entity>(), Is.LessThan(fat));
        }

        [Test]
        public void BlacksmithScaleTypedChannels_UseLessResidentMemoryThanFatRequestArray()
        {
            var capacities = new PresentationRequestChannelCapacities(
                visualProxy: 1_048_576,
                groundOverlay: 65_536,
                worldHud: 1_048_576,
                splineRibbon: 65_536,
                surfaceSource: 1_048_576,
                removal: 1_048_576 + 65_536 + 65_536 + 1_048_576,
                clearTransient: 1_048_576);

            long typedBytes =
                (long)Unsafe.SizeOf<VisualProxyChannelItem>() * capacities.VisualProxy
                + (long)Unsafe.SizeOf<GroundOverlayChannelItem>() * capacities.GroundOverlay
                + (long)Unsafe.SizeOf<WorldHudChannelItem>() * capacities.WorldHud
                + (long)Unsafe.SizeOf<SplineRibbonChannelItem>() * capacities.SplineRibbon
                + (long)Unsafe.SizeOf<SurfaceSourceChannelItem>() * capacities.SurfaceSource
                + (long)Unsafe.SizeOf<PresentationRemovalRequest>() * capacities.Removal
                + (long)Unsafe.SizeOf<Entity>() * capacities.ClearTransient
                + (long)Unsafe.SizeOf<PresentationRequestOp>() * capacities.TotalOperationCapacity;

            long fatBytes = (long)Unsafe.SizeOf<PresentationRequest>() * 2_097_152;
            Assert.That(typedBytes, Is.LessThan(fatBytes));
        }

        [Test]
        public void GetSpan_ReconstructsMixedKindsInEnqueueOrder()
        {
            var requests = new PresentationRequestBuffer(8);
            requests.Add(PresentationRequest.RemoveGroundOverlay(Entity.Null, 7));
            requests.Add(PresentationRequest.FromGroundOverlay(
                Entity.Null,
                new GroundOverlayItem { StableId = 7, Radius = 1.5f },
                LODLevel.High));
            requests.Add(PresentationRequest.FromVisualProxy(
                Entity.Null,
                new PresentationVisualProxy
                {
                    MeshAssetId = 11,
                    StableId = 90,
                    LOD = LODLevel.Medium,
                }));

            Assert.That(requests.Count, Is.EqualTo(3));
            ReadOnlySpan<PresentationRequest> span = requests.GetSpan();
            Assert.That(span[0].Kind, Is.EqualTo(PresentationRequestKind.RemoveGroundOverlay));
            Assert.That(span[0].StableId, Is.EqualTo(7));
            Assert.That(span[1].Kind, Is.EqualTo(PresentationRequestKind.GroundOverlay));
            Assert.That(span[1].GroundOverlay.Radius, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(span[2].Kind, Is.EqualTo(PresentationRequestKind.VisualProxy));
            Assert.That(span[2].VisualProxy.MeshAssetId, Is.EqualTo(11));
            Assert.That(requests.Get(2).VisualProxy.StableId, Is.EqualTo(90));
        }

        [Test]
        public void Add_OverflowsTheFilledChannel_WithoutBlockingOtherChannels()
        {
            var requests = new PresentationRequestBuffer(new PresentationRequestChannelCapacities(
                visualProxy: 1,
                groundOverlay: 1,
                worldHud: 1,
                splineRibbon: 1,
                surfaceSource: 1,
                removal: 1,
                clearTransient: 1));

            requests.Add(PresentationRequest.FromVisualProxy(
                Entity.Null,
                new PresentationVisualProxy { StableId = 1, MeshAssetId = 4 }));

            InvalidOperationException overflow = Assert.Throws<InvalidOperationException>(() =>
                requests.Add(PresentationRequest.FromVisualProxy(
                    Entity.Null,
                    new PresentationVisualProxy { StableId = 2, MeshAssetId = 5 })));
            Assert.That(overflow.Message, Does.Contain("kind=VisualProxy"));

            requests.Add(PresentationRequest.FromGroundOverlay(
                Entity.Null,
                new GroundOverlayItem { StableId = 8 },
                LODLevel.High));
            Assert.That(requests.Count, Is.EqualTo(2));
            Assert.That(requests.GetSpan()[1].Kind, Is.EqualTo(PresentationRequestKind.GroundOverlay));
        }

        [Test]
        public void Flush_RemoveThenUpsertSameStableId_KeepsTheNewOverlay()
        {
            World world = World.Create();
            try
            {
                var requests = new PresentationRequestBuffer(8);
                var overlays = new GroundOverlayBuffer(8);
                using var flush = CreateFlush(world, requests, overlays);

                requests.Add(PresentationRequest.FromGroundOverlay(
                    Entity.Null,
                    new GroundOverlayItem { StableId = 7, Radius = 2f },
                    LODLevel.High));
                flush.Update(0.016f);
                Assert.That(overlays.Count, Is.EqualTo(1));

                requests.Add(PresentationRequest.RemoveGroundOverlay(Entity.Null, 7));
                requests.Add(PresentationRequest.FromGroundOverlay(
                    Entity.Null,
                    new GroundOverlayItem { StableId = 7, Radius = 9f },
                    LODLevel.High));
                flush.Update(0.016f);

                Assert.That(overlays.Count, Is.EqualTo(1),
                    "Same-frame remove-then-add must keep the new overlay; flushing by channel would delete it.");
                Assert.That(overlays.GetSpan()[0].Radius, Is.EqualTo(9f).Within(0.001f));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void Flush_UpsertThenRemoveSameStableId_DeletesTheOverlay()
        {
            World world = World.Create();
            try
            {
                var requests = new PresentationRequestBuffer(8);
                var overlays = new GroundOverlayBuffer(8);
                using var flush = CreateFlush(world, requests, overlays);

                requests.Add(PresentationRequest.FromGroundOverlay(
                    Entity.Null,
                    new GroundOverlayItem { StableId = 7, Radius = 2f },
                    LODLevel.High));
                requests.Add(PresentationRequest.RemoveGroundOverlay(Entity.Null, 7));
                flush.Update(0.016f);

                Assert.That(overlays.Count, Is.EqualTo(0));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        private static PresentationRequestFlushSystem CreateFlush(
            World world,
            PresentationRequestBuffer requests,
            GroundOverlayBuffer overlays)
        {
            return new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                new StableDrawCache(),
                new PrimitiveDrawBuffer(),
                overlays,
                new WorldHudBatchBuffer(8),
                new SplineRibbonBuffer(8),
                new PrimitiveDrawBuffer(),
                new PresentationVisualProxyBuffer(8),
                new SkinnedVisualBatchBuffer(8));
        }
    }
}
