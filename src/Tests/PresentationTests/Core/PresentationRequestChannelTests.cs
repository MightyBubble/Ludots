using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Instancing;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationRequestChannelTests
    {
        private const int LegacyBlacksmithPresentationLaneCapacity = 2_097_152;
        private const int BlacksmithRequestPeakCapacity = 196_608;
        private const int BlacksmithStaticPresenterPeak = 30_000;
        private const int BlacksmithHudPeak = 100_000;
        private const int BlacksmithInstancedBatchCapacity = 32_768;

        [Test]
        public void ChannelElementSizes_AreEachNarrowerThanFatPresentationRequest()
        {
            int fat = Unsafe.SizeOf<PresentationRequest>();
            Assert.That(fat, Is.LessThan(688), "The compatibility snapshot must not retain the legacy 688-byte padding fields.");
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
        public void BlacksmithScalePresentationLanePreallocation_FitsUnderLegacyTenth()
        {
            PresentationRuntimeConfig presentationConfig = CreateBlacksmithScaleConfig();
            var capacities = PresentationRequestChannelCapacities.From(presentationConfig);

            long typedBytes = RequestChannelBytes(in capacities);
            long instancedBatchBytes = InstancedBatchBytes(presentationConfig);
            long legacyLaneBytes = (long)Unsafe.SizeOf<PresentationRequest>() * LegacyBlacksmithPresentationLaneCapacity;

            Assert.That(capacities.TotalOperationCapacity, Is.EqualTo(BlacksmithRequestPeakCapacity));
            Assert.That(typedBytes + instancedBatchBytes, Is.LessThanOrEqualTo(legacyLaneBytes / 10));
        }

        [Test]
        public void BlacksmithScaleRequestCapacity_CoversThirtyKStaticAndHundredKHudPeakBeforeFlush()
        {
            PresentationRuntimeConfig presentationConfig = CreateBlacksmithScaleConfig();
            var requests = new PresentationRequestBuffer(
                PresentationRequestChannelCapacities.From(presentationConfig));

            for (int i = 0; i < BlacksmithStaticPresenterPeak; i++)
            {
                requests.Add(PresentationRequest.FromVisualProxy(
                    Entity.Null,
                    new PresentationVisualProxy
                    {
                        StableId = i + 1,
                        MeshAssetId = 1,
                    }));
            }

            for (int i = 0; i < BlacksmithHudPeak; i++)
            {
                requests.Add(PresentationRequest.FromWorldHud(
                    Entity.Null,
                    new WorldHudItem
                    {
                        StableId = BlacksmithStaticPresenterPeak + i + 1,
                    },
                    LODLevel.High));
            }

            Assert.That(requests.Count, Is.EqualTo(BlacksmithStaticPresenterPeak + BlacksmithHudPeak));
            Assert.That(requests.Capacity, Is.EqualTo(BlacksmithRequestPeakCapacity));
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
        public void Add_OverflowsTotalOperationBudget_WithoutExpandingChannels()
        {
            var requests = new PresentationRequestBuffer(new PresentationRequestChannelCapacities(
                visualProxy: 4,
                groundOverlay: 4,
                worldHud: 4,
                splineRibbon: 4,
                surfaceSource: 4,
                removal: 4,
                clearTransient: 4,
                totalOperationCapacity: 2));

            requests.Add(PresentationRequest.FromVisualProxy(
                Entity.Null,
                new PresentationVisualProxy { StableId = 1, MeshAssetId = 4 }));
            requests.Add(PresentationRequest.FromGroundOverlay(
                Entity.Null,
                new GroundOverlayItem { StableId = 8 },
                LODLevel.High));

            InvalidOperationException overflow = Assert.Throws<InvalidOperationException>(() =>
                requests.Add(PresentationRequest.FromWorldHud(
                    Entity.Null,
                    new WorldHudItem { StableId = 9 },
                    LODLevel.High)));
            Assert.That(overflow.Message, Does.Contain("kind=WorldHud"));
            Assert.That(requests.Count, Is.EqualTo(2));
            Assert.That(requests.WorldHudAt(0).Item.StableId, Is.EqualTo(0));
        }

        [Test]
        public void Replay_CapturesAndReplaysTypedChannelWithoutReconstructingFacade()
        {
            World world = World.Create();
            try
            {
                Entity owner = world.Create();
                var source = new PresentationRequestBuffer(4);
                var proxy = new PresentationVisualProxy
                {
                    StableId = 42,
                    MeshAssetId = 9,
                    Position = new Vector3(3f, 4f, 5f),
                    LOD = LODLevel.Medium,
                };
                source.AddVisualProxy(owner, in proxy);

                PresentationRequestReplay replay = source.CaptureReplay(0);
                Assert.That(replay.Kind, Is.EqualTo(PresentationRequestKind.VisualProxy));
                Assert.That(replay.Owner, Is.EqualTo(owner));
                Assert.That(replay.VisualProxy.StableId, Is.EqualTo(42));

                var target = new PresentationRequestBuffer(4);
                target.Replay(in replay);

                Assert.That(target.Count, Is.EqualTo(1));
                Assert.That(target.VisualProxyAt(0).Owner, Is.EqualTo(owner));
                Assert.That(target.VisualProxyAt(0).VisualProxy.Position, Is.EqualTo(proxy.Position));
                Assert.That(target.VisualProxyAt(0).VisualProxy.MeshAssetId, Is.EqualTo(9));
            }
            finally
            {
                World.Destroy(world);
            }
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

        private static PresentationRuntimeConfig CreateBlacksmithScaleConfig()
        {
            return new PresentationRuntimeConfig
            {
                VisualProxyBufferCapacity = 1_048_576,
                GroundOverlayCapacity = 65_536,
                WorldHudCapacity = 1_048_576,
                SplineRibbonCapacity = 65_536,
                PresenterInstanceCapacity = 1_048_576,
                PresentationRequestCapacity = BlacksmithRequestPeakCapacity,
                ClearTransientVisualProjectionCapacity = 65_536,
                InstancedBatchRequestCapacity = BlacksmithInstancedBatchCapacity,
                InstancedBatchOperationCapacity = BlacksmithInstancedBatchCapacity,
            };
        }

        private static long RequestChannelBytes(in PresentationRequestChannelCapacities capacities)
        {
            return (long)Unsafe.SizeOf<VisualProxyChannelItem>() * capacities.VisualProxy
                + (long)Unsafe.SizeOf<GroundOverlayChannelItem>() * capacities.GroundOverlay
                + (long)Unsafe.SizeOf<WorldHudChannelItem>() * capacities.WorldHud
                + (long)Unsafe.SizeOf<SplineRibbonChannelItem>() * capacities.SplineRibbon
                + (long)Unsafe.SizeOf<SurfaceSourceChannelItem>() * capacities.SurfaceSource
                + (long)Unsafe.SizeOf<PresentationRemovalRequest>() * capacities.Removal
                + (long)Unsafe.SizeOf<Entity>() * capacities.ClearTransient
                + (long)Unsafe.SizeOf<PresentationRequestOp>() * capacities.TotalOperationCapacity;
        }

        private static long InstancedBatchBytes(PresentationRuntimeConfig config)
        {
            return (long)Unsafe.SizeOf<InstancedBatchRequest>() * config.InstancedBatchRequestCapacity
                + (long)Unsafe.SizeOf<InstancedBatchOperation>() * config.InstancedBatchOperationCapacity;
        }
    }
}
