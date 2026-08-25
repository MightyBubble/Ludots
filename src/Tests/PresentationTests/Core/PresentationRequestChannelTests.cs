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

        // SSOT: the pre-refactor fat PresentationRequest struct was exactly 688 bytes.
        // The <=1/10 true-scale gate must stay pinned to this literal so it cannot move
        // with future struct layout changes; the aggregate type itself no longer exists.
        private const int LegacyFatPresentationRequestBytes = 688;

        [Test]
        public void ChannelElementSizes_AreEachNarrowerThanFatPresentationRequest()
        {
            Assert.That(Unsafe.SizeOf<VisualProxyChannelItem>(), Is.LessThan(LegacyFatPresentationRequestBytes));
            Assert.That(Unsafe.SizeOf<GroundOverlayChannelItem>(), Is.LessThan(LegacyFatPresentationRequestBytes));
            Assert.That(Unsafe.SizeOf<WorldHudChannelItem>(), Is.LessThan(LegacyFatPresentationRequestBytes));
            Assert.That(Unsafe.SizeOf<SplineRibbonChannelItem>(), Is.LessThan(LegacyFatPresentationRequestBytes));
            Assert.That(Unsafe.SizeOf<SurfaceSourceChannelItem>(), Is.LessThan(LegacyFatPresentationRequestBytes));
            Assert.That(Unsafe.SizeOf<PresentationRemovalRequest>(), Is.LessThan(LegacyFatPresentationRequestBytes));
            Assert.That(Unsafe.SizeOf<PresentationRequestOp>(), Is.LessThan(LegacyFatPresentationRequestBytes));
            Assert.That(Unsafe.SizeOf<Entity>(), Is.LessThan(LegacyFatPresentationRequestBytes));
        }

        [Test]
        public void BlacksmithScalePresentationLanePreallocation_FitsUnderLegacyTenth()
        {
            PresentationRuntimeConfig presentationConfig = CreateBlacksmithScaleConfig();
            var capacities = PresentationRequestChannelCapacities.From(presentationConfig);

            long typedBytes = RequestChannelBytes(in capacities);
            long instancedBatchBytes = InstancedBatchBytes(presentationConfig);
            long legacyLaneBytes = (long)LegacyFatPresentationRequestBytes * LegacyBlacksmithPresentationLaneCapacity;

            TestContext.Out.WriteLine(
                $"requestStorageBytes={typedBytes}; instancedBatchStorageBytes={instancedBatchBytes}; " +
                $"legacyRequestStorageBytes={legacyLaneBytes}; requestRatio={typedBytes / (double)legacyLaneBytes:F4}; " +
                $"combinedRatio={(typedBytes + instancedBatchBytes) / (double)legacyLaneBytes:F4}");

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
                requests.AddVisualProxy(
                    Entity.Null,
                    new PresentationVisualProxy
                    {
                        StableId = i + 1,
                        MeshAssetId = 1,
                    });
            }

            for (int i = 0; i < BlacksmithHudPeak; i++)
            {
                requests.AddWorldHud(
                    Entity.Null,
                    new WorldHudItem
                    {
                        StableId = BlacksmithStaticPresenterPeak + i + 1,
                    },
                    LODLevel.High);
            }

            Assert.That(requests.Count, Is.EqualTo(BlacksmithStaticPresenterPeak + BlacksmithHudPeak));
            Assert.That(requests.Capacity, Is.EqualTo(BlacksmithRequestPeakCapacity));
        }

        [Test]
        public void TypedLanes_PreserveOwnerPayloadAndOrderAcrossAllChannels()
        {
            World world = World.Create();
            try
            {
                Entity visualOwner = world.Create();
                Entity overlayOwner = world.Create();
                Entity hudOwner = world.Create();
                Entity splineOwner = world.Create();
                Entity surfaceOwner = world.Create();
                Entity removalOwner = world.Create();
                Entity clearOwner = world.Create();
                var requests = new PresentationRequestBuffer(16);

                var visual = new PresentationVisualProxy { StableId = 101, MeshAssetId = 11, LOD = LODLevel.Medium };
                var overlay = new GroundOverlayItem { StableId = 102, Radius = 2.5f };
                var hud = new WorldHudItem { StableId = 103, Id0 = 17 };
                var spline = new SplineRibbonRequest { StableId = 104, Width = 3f };
                var surface = new SurfaceSourceRequest { StableId = 105, ScopeId = 19 };

                requests.AddVisualProxy(visualOwner, in visual);
                requests.AddGroundOverlay(overlayOwner, in overlay, LODLevel.High);
                requests.AddWorldHud(hudOwner, in hud, LODLevel.Low);
                requests.AddSplineRibbon(splineOwner, in spline, LODLevel.Medium);
                requests.AddSurfaceSource(surfaceOwner, in surface, LODLevel.High);
                requests.RemoveWorldHud(removalOwner, 106);
                requests.ClearTransientVisualProjection(clearOwner);

                ReadOnlySpan<PresentationRequestOp> ops = requests.Ops;
                Assert.That(ops.Length, Is.EqualTo(7));
                Assert.That(ops[0].Channel, Is.EqualTo(PresentationRequestChannel.VisualProxy));
                ref readonly VisualProxyChannelItem visualItem = ref requests.VisualProxyAt(ops[0].Slot);
                Assert.That(visualItem.Owner, Is.EqualTo(visualOwner));
                Assert.That(visualItem.VisualProxy.StableId, Is.EqualTo(101));
                Assert.That(ops[1].Channel, Is.EqualTo(PresentationRequestChannel.GroundOverlay));
                Assert.That(requests.GroundOverlayAt(ops[1].Slot).Owner, Is.EqualTo(overlayOwner));
                Assert.That(requests.GroundOverlayAt(ops[1].Slot).Item.Radius, Is.EqualTo(2.5f).Within(0.001f));
                Assert.That(ops[2].Channel, Is.EqualTo(PresentationRequestChannel.WorldHud));
                Assert.That(requests.WorldHudAt(ops[2].Slot).Owner, Is.EqualTo(hudOwner));
                Assert.That(requests.WorldHudAt(ops[2].Slot).Item.Id0, Is.EqualTo(17));
                Assert.That(ops[3].Channel, Is.EqualTo(PresentationRequestChannel.SplineRibbon));
                Assert.That(requests.SplineRibbonAt(ops[3].Slot).Owner, Is.EqualTo(splineOwner));
                Assert.That(requests.SplineRibbonAt(ops[3].Slot).Item.Width, Is.EqualTo(3f).Within(0.001f));
                Assert.That(ops[4].Channel, Is.EqualTo(PresentationRequestChannel.SurfaceSource));
                ref readonly SurfaceSourceChannelItem surfaceItem = ref requests.SurfaceSourceAt(ops[4].Slot);
                Assert.That(surfaceItem.Owner, Is.EqualTo(surfaceOwner));
                Assert.That(surfaceItem.Item.ScopeId, Is.EqualTo(19));
                Assert.That(surfaceItem.Item.StableId, Is.EqualTo(105));
                Assert.That(ops[5].Channel, Is.EqualTo(PresentationRequestChannel.Removal));
                ref readonly PresentationRemovalRequest removal = ref requests.RemovalAt(ops[5].Slot);
                Assert.That(removal.Owner, Is.EqualTo(removalOwner));
                Assert.That(removal.StableId, Is.EqualTo(106));
                Assert.That(ops[6].Channel, Is.EqualTo(PresentationRequestChannel.ClearTransient));
                Assert.That(requests.ClearTransientAt(ops[6].Slot), Is.EqualTo(clearOwner));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        [Category("benchmark")]
        public void RealScaleTypedLanes_Reach130kRequestsWithoutSteadyStateAllocation()
        {
            const int visualCount = BlacksmithStaticPresenterPeak;
            const int hudCount = BlacksmithHudPeak;
            const int totalCount = visualCount + hudCount;
            var capacities = new PresentationRequestChannelCapacities(
                visualProxy: visualCount,
                groundOverlay: 1,
                worldHud: hudCount,
                splineRibbon: 1,
                surfaceSource: 1,
                removal: 1,
                clearTransient: 1,
                totalOperationCapacity: totalCount);
            var requests = new PresentationRequestBuffer(in capacities);

            AddRealScaleFrame(requests, visualCount, hudCount);
            requests.Clear();
            AddRealScaleFrame(requests, visualCount, hudCount);
            requests.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            AddRealScaleFrame(requests, visualCount, hudCount);
            long steadyStateAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            long requestStorageBytes = RequestChannelBytes(in capacities);
            long legacyRequestStorageBytes = (long)LegacyFatPresentationRequestBytes * LegacyBlacksmithPresentationLaneCapacity;
            TestContext.Out.WriteLine(
                $"realScaleRequests={requests.Count}; requestStorageBytes={requestStorageBytes}; " +
                $"legacyRequestStorageBytes={legacyRequestStorageBytes}; requestRatio={requestStorageBytes / (double)legacyRequestStorageBytes:F4}; " +
                $"steadyStateAllocatedBytes={steadyStateAllocated}");

            Assert.That(requests.Count, Is.EqualTo(totalCount));
            Assert.That(requestStorageBytes, Is.LessThanOrEqualTo(legacyRequestStorageBytes / 10));
            Assert.That(steadyStateAllocated, Is.EqualTo(0), "Typed lane emission must not allocate after warmup.");
        }

        [Test]
        [Category("benchmark")]
        public void RealScaleConsumePath_Flushes130kRequests_WithoutSteadyStateAllocation()
        {
            const int visualCount = BlacksmithStaticPresenterPeak;
            const int hudCount = BlacksmithHudPeak;
            const int totalCount = visualCount + hudCount;
            var capacities = new PresentationRequestChannelCapacities(
                visualProxy: visualCount,
                groundOverlay: 1,
                worldHud: hudCount,
                splineRibbon: 1,
                surfaceSource: 1,
                removal: 1,
                clearTransient: 1,
                totalOperationCapacity: totalCount);
            var requests = new PresentationRequestBuffer(in capacities);

            using var world = World.Create();
            using var flush = CreateTrueScaleFlush(world, requests);

            AddRealScaleFrame(requests, visualCount, hudCount);
            flush.Update(0.016f);
            AddRealScaleFrame(requests, visualCount, hudCount);
            flush.Update(0.016f);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            AddRealScaleFrame(requests, visualCount, hudCount);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            flush.Update(0.016f);
            long consumePathAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            TestContext.Out.WriteLine(
                $"consumePathRequests={totalCount}; consumePathAllocatedBytes={consumePathAllocated}");

            Assert.That(requests.Count, Is.Zero, "Flush must drain the typed lanes.");
            Assert.That(consumePathAllocated, Is.EqualTo(0),
                "Typed lane consume (Ops + *At) must be allocation-free at true scale.");
        }

        [Test]
        public void TypedOps_PreserveMixedKindEnqueueOrder()
        {
            var requests = new PresentationRequestBuffer(8);
            requests.RemoveGroundOverlay(Entity.Null, 7);
            requests.AddGroundOverlay(
                Entity.Null,
                new GroundOverlayItem { StableId = 7, Radius = 1.5f },
                LODLevel.High);
            requests.AddVisualProxy(
                Entity.Null,
                new PresentationVisualProxy
                {
                    MeshAssetId = 11,
                    StableId = 90,
                    LOD = LODLevel.Medium,
                });

            Assert.That(requests.Count, Is.EqualTo(3));
            ReadOnlySpan<PresentationRequestOp> ops = requests.Ops;
            Assert.That(ops[0].Channel, Is.EqualTo(PresentationRequestChannel.Removal));
            ref readonly PresentationRemovalRequest removal = ref requests.RemovalAt(ops[0].Slot);
            Assert.That(removal.Kind, Is.EqualTo(PresentationRequestKind.RemoveGroundOverlay));
            Assert.That(removal.StableId, Is.EqualTo(7));
            Assert.That(ops[1].Channel, Is.EqualTo(PresentationRequestChannel.GroundOverlay));
            Assert.That(requests.GroundOverlayAt(ops[1].Slot).Item.Radius, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(ops[2].Channel, Is.EqualTo(PresentationRequestChannel.VisualProxy));
            ref readonly VisualProxyChannelItem visual = ref requests.VisualProxyAt(ops[2].Slot);
            Assert.That(visual.VisualProxy.MeshAssetId, Is.EqualTo(11));
            Assert.That(visual.VisualProxy.StableId, Is.EqualTo(90));
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

            requests.AddVisualProxy(
                Entity.Null,
                new PresentationVisualProxy { StableId = 1, MeshAssetId = 4 });

            InvalidOperationException overflow = Assert.Throws<InvalidOperationException>(() =>
                requests.AddVisualProxy(
                    Entity.Null,
                    new PresentationVisualProxy { StableId = 2, MeshAssetId = 5 }));
            Assert.That(overflow.Message, Does.Contain("kind=VisualProxy"));

            requests.AddGroundOverlay(
                Entity.Null,
                new GroundOverlayItem { StableId = 8 },
                LODLevel.High);
            Assert.That(requests.Count, Is.EqualTo(2));
            Assert.That(requests.Ops[1].Channel, Is.EqualTo(PresentationRequestChannel.GroundOverlay));
            Assert.That(requests.GroundOverlayAt(0).Item.StableId, Is.EqualTo(8));
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

            requests.AddVisualProxy(
                Entity.Null,
                new PresentationVisualProxy { StableId = 1, MeshAssetId = 4 });
            requests.AddGroundOverlay(
                Entity.Null,
                new GroundOverlayItem { StableId = 8 },
                LODLevel.High);

            InvalidOperationException overflow = Assert.Throws<InvalidOperationException>(() =>
                requests.AddWorldHud(
                    Entity.Null,
                    new WorldHudItem { StableId = 9 },
                    LODLevel.High));
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

                requests.AddGroundOverlay(
                    Entity.Null,
                    new GroundOverlayItem { StableId = 7, Radius = 2f },
                    LODLevel.High);
                flush.Update(0.016f);
                Assert.That(overlays.Count, Is.EqualTo(1));

                requests.RemoveGroundOverlay(Entity.Null, 7);
                requests.AddGroundOverlay(
                    Entity.Null,
                    new GroundOverlayItem { StableId = 7, Radius = 9f },
                    LODLevel.High);
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

                requests.AddGroundOverlay(
                    Entity.Null,
                    new GroundOverlayItem { StableId = 7, Radius = 2f },
                    LODLevel.High);
                requests.RemoveGroundOverlay(Entity.Null, 7);
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

        private static PresentationRequestFlushSystem CreateTrueScaleFlush(
            World world,
            PresentationRequestBuffer requests)
        {
            return new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                new StableDrawCache(40_000),
                new PrimitiveDrawBuffer(40_000),
                new GroundOverlayBuffer(8),
                new WorldHudBatchBuffer(110_000),
                new SplineRibbonBuffer(8),
                new PrimitiveDrawBuffer(40_000),
                new PresentationVisualProxyBuffer(40_000),
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

        private static void AddRealScaleFrame(PresentationRequestBuffer requests, int visualCount, int hudCount)
        {
            for (int i = 0; i < visualCount; i++)
            {
                var visual = new PresentationVisualProxy { StableId = i + 1, MeshAssetId = 1, LOD = LODLevel.High };
                requests.AddVisualProxy(Entity.Null, in visual);
            }

            for (int i = 0; i < hudCount; i++)
            {
                var hud = new WorldHudItem { StableId = visualCount + i + 1, Id0 = 1 };
                requests.AddWorldHud(Entity.Null, in hud, LODLevel.High);
            }
        }
    }
}
