using System;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanScratchMemoryContractTests
    {
        [Test]
        public void PreallocatedChannelPayloadBytes_IsPositive_ForBaselinePool()
        {
            var config = CreateBaselineConfig();
            var pool = new LayeredSpanScratchPool(config);

            Assert.That(pool.PreallocatedChannelPayloadBytes, Is.GreaterThan(0L));
        }

        [Test]
        public void PreallocatedChannelPayloadBytes_StrictlyIncreases_WhenCapacityFieldsGrow()
        {
            var baseline = CreateBaselineConfig();
            long baselineBytes = new LayeredSpanScratchPool(baseline).PreallocatedChannelPayloadBytes;

            AssertStrictIncrease(baseline, baselineBytes, c => c.ScratchSlotCount++, nameof(NavLayeredSpanConfig.ScratchSlotCount));
            AssertStrictIncrease(baseline, baselineBytes, c => c.ColumnCapacity++, nameof(NavLayeredSpanConfig.ColumnCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.SpanCapacity++, nameof(NavLayeredSpanConfig.SpanCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.ClassifiedSpanCapacity++, nameof(NavLayeredSpanConfig.ClassifiedSpanCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.WalkableSpanCapacity++, nameof(NavLayeredSpanConfig.WalkableSpanCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.LinkCapacity++, nameof(NavLayeredSpanConfig.LinkCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.SheetCapacity++, nameof(NavLayeredSpanConfig.SheetCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.PortalIntervalCapacity++, nameof(NavLayeredSpanConfig.PortalIntervalCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.RegionCapacity++, nameof(NavLayeredSpanConfig.RegionCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.ChartCapacity++, nameof(NavLayeredSpanConfig.ChartCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.RingCapacity++, nameof(NavLayeredSpanConfig.RingCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.ContourVertexCapacity++, nameof(NavLayeredSpanConfig.ContourVertexCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.ContourEdgeCapacity++, nameof(NavLayeredSpanConfig.ContourEdgeCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.SeamCapacity++, nameof(NavLayeredSpanConfig.SeamCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.CanonicalLinkCapacity++, nameof(NavLayeredSpanConfig.CanonicalLinkCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.SplitPointCapacity++, nameof(NavLayeredSpanConfig.SplitPointCapacity));
            AssertStrictIncrease(
                baseline,
                baselineBytes,
                c => c.TriangulationVertexCapacity++,
                nameof(NavLayeredSpanConfig.TriangulationVertexCapacity));
            AssertStrictIncrease(
                baseline,
                baselineBytes,
                c => c.TriangulationTriangleCapacity++,
                nameof(NavLayeredSpanConfig.TriangulationTriangleCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.ConstrainedEdgeCapacity++, nameof(NavLayeredSpanConfig.ConstrainedEdgeCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.BorderPortalCapacity++, nameof(NavLayeredSpanConfig.BorderPortalCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.PolygonVertexCapacity++, nameof(NavLayeredSpanConfig.PolygonVertexCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.AdjacencyEdgeCapacity++, nameof(NavLayeredSpanConfig.AdjacencyEdgeCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.BridgeCandidateCapacity++, nameof(NavLayeredSpanConfig.BridgeCandidateCapacity));
            AssertStrictIncrease(baseline, baselineBytes, c => c.RingWorkCapacity++, nameof(NavLayeredSpanConfig.RingWorkCapacity));
            AssertStrictIncrease(
                baseline,
                baselineBytes,
                c => c.TemporaryConstraintFlagCapacity++,
                nameof(NavLayeredSpanConfig.TemporaryConstraintFlagCapacity));
        }

        [Test]
        public void PreallocatedChannelPayloadBytes_DoesNotScaleWithWorldTileCount()
        {
            var config = CreateBaselineConfig();
            long bytes = new LayeredSpanScratchPool(config).PreallocatedChannelPayloadBytes;
            // Scratch pool is capacity-driven; 8x8 vs 64x64 world tile counts never appear in the formula.
            Assert.That(bytes, Is.EqualTo(new LayeredSpanScratchPool(Clone(config)).PreallocatedChannelPayloadBytes));
            Assert.That(bytes, Is.LessThan(checked(4096L * 1024L * 1024L)));
        }

        [Test]
        public void PreallocatedChannelPayloadBytes_EqualsSlotSumTimesSlotCountPlusFreeIndexArray()
        {
            var config = CreateBaselineConfig();
            var pool = new LayeredSpanScratchPool(config);
            var slots = new LayeredSpanScratchSlot[config.ScratchSlotCount];
            for (int i = 0; i < config.ScratchSlotCount; i++)
            {
                slots[i] = pool.Acquire();
            }

            long slotSum = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                slotSum = checked(slotSum + slots[i].PreallocatedChannelPayloadBytes);
            }

            long freeIndexPayload = checked((long)config.ScratchSlotCount * sizeof(int));
            Assert.That(pool.PreallocatedChannelPayloadBytes, Is.EqualTo(checked(slotSum + freeIndexPayload)));

            for (int i = 0; i < slots.Length; i++)
            {
                LayeredSpanScratchSlot slot = slots[i];
                long ownedScratchSum = LayeredSpanScratchChannelPayloadSum.ForSlot(slot);
                Assert.That(slot.PreallocatedChannelPayloadBytes, Is.EqualTo(ownedScratchSum));
            }
        }

        [Test]
        public void PreallocatedScratchChannelPayloadBytes_ExposesOwnedPoolBytes()
        {
            var config = CreateBaselineConfig();
            var pool = new LayeredSpanScratchPool(config);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);

            Assert.That(
                algorithm.PreallocatedScratchChannelPayloadBytes,
                Is.EqualTo(pool.PreallocatedChannelPayloadBytes));
        }

        private static void AssertStrictIncrease(
            NavLayeredSpanConfig baseline,
            long baselineBytes,
            Action<NavLayeredSpanConfig> mutate,
            string fieldName)
        {
            var mutated = Clone(baseline);
            mutate(mutated);
            mutated.Validate();

            long mutatedBytes = new LayeredSpanScratchPool(mutated).PreallocatedChannelPayloadBytes;
            Assert.That(mutatedBytes, Is.GreaterThan(baselineBytes), fieldName);
        }

        private static NavLayeredSpanConfig CreateBaselineConfig()
        {
            var config = new NavLayeredSpanConfig
            {
                ScratchSlotCount = 2,
                RasterCellSizeCm = 100,
                RasterHaloCells = 1,
                SameSurfaceToleranceCm = 5,
                MaxSimplificationErrorCm = 0,
                HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                MaxLawsonFlipCount = 100_000,
                ColumnCapacity = 64,
                SpanCapacity = 128,
                ClassifiedSpanCapacity = 128,
                WalkableSpanCapacity = 128,
                LinkCapacity = 256,
                SheetCapacity = 128,
                PortalIntervalCapacity = 256,
                RegionCapacity = 64,
                ChartCapacity = 32,
                RingCapacity = 32,
                ContourVertexCapacity = 256,
                ContourEdgeCapacity = 256,
                SeamCapacity = 64,
                CanonicalLinkCapacity = 256,
                SplitPointCapacity = 64,
                TriangulationVertexCapacity = 256,
                TriangulationTriangleCapacity = 512,
                ConstrainedEdgeCapacity = 512,
                BorderPortalCapacity = 64,
                PolygonVertexCapacity = 256,
                AdjacencyEdgeCapacity = 1536,
                BridgeCandidateCapacity = 256,
                RingWorkCapacity = 64,
                TemporaryConstraintFlagCapacity = 512
            };
            config.Validate();
            return config;
        }

        private static NavLayeredSpanConfig Clone(NavLayeredSpanConfig source)
        {
            return new NavLayeredSpanConfig
            {
                ScratchSlotCount = source.ScratchSlotCount,
                RasterCellSizeCm = source.RasterCellSizeCm,
                RasterHaloCells = source.RasterHaloCells,
                SameSurfaceToleranceCm = source.SameSurfaceToleranceCm,
                MaxSimplificationErrorCm = source.MaxSimplificationErrorCm,
                HeightRounding = source.HeightRounding,
                MaxLawsonFlipCount = source.MaxLawsonFlipCount,
                ColumnCapacity = source.ColumnCapacity,
                SpanCapacity = source.SpanCapacity,
                ClassifiedSpanCapacity = source.ClassifiedSpanCapacity,
                WalkableSpanCapacity = source.WalkableSpanCapacity,
                LinkCapacity = source.LinkCapacity,
                SheetCapacity = source.SheetCapacity,
                PortalIntervalCapacity = source.PortalIntervalCapacity,
                RegionCapacity = source.RegionCapacity,
                ChartCapacity = source.ChartCapacity,
                RingCapacity = source.RingCapacity,
                ContourVertexCapacity = source.ContourVertexCapacity,
                ContourEdgeCapacity = source.ContourEdgeCapacity,
                SeamCapacity = source.SeamCapacity,
                CanonicalLinkCapacity = source.CanonicalLinkCapacity,
                SplitPointCapacity = source.SplitPointCapacity,
                TriangulationVertexCapacity = source.TriangulationVertexCapacity,
                TriangulationTriangleCapacity = source.TriangulationTriangleCapacity,
                ConstrainedEdgeCapacity = source.ConstrainedEdgeCapacity,
                BorderPortalCapacity = source.BorderPortalCapacity,
                PolygonVertexCapacity = source.PolygonVertexCapacity,
                AdjacencyEdgeCapacity = source.AdjacencyEdgeCapacity,
                BridgeCandidateCapacity = source.BridgeCandidateCapacity,
                RingWorkCapacity = source.RingWorkCapacity,
                TemporaryConstraintFlagCapacity = source.TemporaryConstraintFlagCapacity
            };
        }

        /// <summary>
        /// Test-local mirror of slot-owned scratch sum; keeps assertions independent of slot internals.
        /// </summary>
        private static class LayeredSpanScratchChannelPayloadSum
        {
            internal static long ForSlot(LayeredSpanScratchSlot slot)
            {
                return checked(
                    slot.Raw.PreallocatedChannelPayloadBytes +
                    slot.Walkability.PreallocatedChannelPayloadBytes +
                    slot.Sheets.PreallocatedChannelPayloadBytes +
                    slot.Links.PreallocatedChannelPayloadBytes +
                    slot.Radius.PreallocatedChannelPayloadBytes +
                    slot.Regions.PreallocatedChannelPayloadBytes +
                    slot.Contours.PreallocatedChannelPayloadBytes +
                    slot.Triangulation.PreallocatedChannelPayloadBytes);
            }
        }
    }
}
