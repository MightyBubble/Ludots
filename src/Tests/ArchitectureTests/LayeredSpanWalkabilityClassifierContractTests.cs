using System;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanWalkabilityClassifierContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly NavTriangleSurfaceFlags SolidOnly = NavTriangleSurfaceFlags.Solid;

        [Test]
        public void Classify_FlatUnderSolidCeiling_ExactClearance_ShortAcceptedTallRejected()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0),
                Span(minY: 200, maxY: 200, SolidOnly, nx: 0, ny: 1, nz: 0, stableId: 2, tri: 1));

            var shortSpec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 180,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var shortOut = new LayeredSpanWalkabilityScratch(1, 2, 2);
            LayeredSpanWalkabilityClassifier.Classify(raw, in shortSpec, shortOut);

            Assert.That(shortOut.ClassifiedSpanCount, Is.EqualTo(2));
            Assert.That(shortOut.SpanClearanceCm[0], Is.EqualTo(200));
            Assert.That(shortOut.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            Assert.That(shortOut.SpanStatus[1], Is.EqualTo(LayeredSpanWalkabilityStatus.SolidOnly));
            Assert.That(shortOut.WalkableSpanCount, Is.EqualTo(1));
            Assert.That(shortOut.WalkableSpanIndices[0], Is.EqualTo(0));

            var tallSpec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 250,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var tallOut = new LayeredSpanWalkabilityScratch(1, 2, 2);
            LayeredSpanWalkabilityClassifier.Classify(raw, in tallSpec, tallOut);

            Assert.That(tallOut.SpanClearanceCm[0], Is.EqualTo(200));
            Assert.That(tallOut.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.ClearanceRejected));
            Assert.That(tallOut.WalkableSpanCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_GroundUnderWalkCandidateBridge_DistinguishableClearanceBothWalkable()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 10, tri: 0),
                Span(minY: 300, maxY: 300, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 20, tri: 1));

            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 200,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var output = new LayeredSpanWalkabilityScratch(1, 2, 2);
            LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output);

            Assert.That(output.SpanClearanceCm[0], Is.EqualTo(300));
            Assert.That(output.SpanClearanceCm[1], Is.EqualTo(int.MaxValue));
            Assert.That(output.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            Assert.That(output.SpanStatus[1], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            Assert.That(ToArray(output.WalkableSpanIndices), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(output.ColumnWalkableCounts[0], Is.EqualTo(2));
            Assert.That(ToArray(output.ColumnWalkableOffsets), Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void Classify_VerticalSolidWallOverlappingFloorHeadspace_ClearanceZeroRejectsFloor()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0),
                Span(minY: 0, maxY: 180, SolidOnly, nx: 1, ny: 0, nz: 0, stableId: 2, tri: 1));

            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 100,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var output = new LayeredSpanWalkabilityScratch(1, 2, 2);
            LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output);

            Assert.That(output.SpanClearanceCm[0], Is.EqualTo(0));
            Assert.That(output.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.ClearanceRejected));
            Assert.That(output.SpanStatus[1], Is.EqualTo(LayeredSpanWalkabilityStatus.SolidOnly));
            Assert.That(output.WalkableSpanCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_SteepRejectedFlatAccepted_ReverseWindingDoesNotChangeSlope()
        {
            // Large vertical gaps so clearance stays open; this case asserts slope only.
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 100, nz: 0, stableId: 1, tri: 0),
                Span(minY: 1_000, maxY: 1_000, FloorFlags, nx: 100, ny: 1, nz: 0, stableId: 2, tri: 1),
                Span(minY: 2_000, maxY: 2_000, FloorFlags, nx: 0, ny: -100, nz: 0, stableId: 3, tri: 2));

            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 50,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var output = new LayeredSpanWalkabilityScratch(1, 3, 3);
            LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output);

            Assert.That(output.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            Assert.That(output.SpanStatus[1], Is.EqualTo(LayeredSpanWalkabilityStatus.SlopeRejected));
            Assert.That(output.SpanStatus[2], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            Assert.That(ToArray(output.WalkableSpanIndices), Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void Classify_SolidOnlyAndZeroNormalCandidate_Statuses()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 50, SolidOnly, nx: 1, ny: 0, nz: 0, stableId: 1, tri: 0),
                Span(minY: 100, maxY: 100, FloorFlags, nx: 0, ny: 0, nz: 0, stableId: 2, tri: 1));

            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 50,
                minWalkableUpDotQ1M: 1,
                sameSurfaceToleranceCm: 0);
            var output = new LayeredSpanWalkabilityScratch(1, 2, 2);
            LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output);

            Assert.That(output.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.SolidOnly));
            Assert.That(output.SpanStatus[1], Is.EqualTo(LayeredSpanWalkabilityStatus.DegenerateNormal));
            Assert.That(output.WalkableSpanCount, Is.EqualTo(0));
        }

        [Test]
        public void Classify_ColumnCapacityFailure_NamesOwnerAndRequired_EmptyOutput()
        {
            LayeredSpanScratch raw = SeedColumns(
                columnCount: 2,
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0, column: 0),
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 2, tri: 1, column: 1));
            var spec = new LayeredSpanWalkabilitySpec(50, 500_000, 0);
            var output = new LayeredSpanWalkabilityScratch(
                columnCapacity: 1,
                classifiedSpanCapacity: 8,
                walkableSpanCapacity: 8);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanWalkabilityScratch.columnCapacity"));
            Assert.That(ex.Message, Does.Contain("required 2"));
            AssertEmpty(output);
        }

        [Test]
        public void Classify_ClassifiedSpanCapacityFailure_NamesOwnerAndRequired_EmptyOutput()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0),
                Span(minY: 100, maxY: 100, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 2, tri: 1));
            var spec = new LayeredSpanWalkabilitySpec(50, 500_000, 0);
            var output = new LayeredSpanWalkabilityScratch(
                columnCapacity: 1,
                classifiedSpanCapacity: 1,
                walkableSpanCapacity: 8);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanWalkabilityScratch.classifiedSpanCapacity"));
            Assert.That(ex.Message, Does.Contain("required 2"));
            AssertEmpty(output);
        }

        [Test]
        public void Classify_WalkableSpanCapacityFailure_NamesOwnerAndRequired_EmptyOutput()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0),
                Span(minY: 100, maxY: 100, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 2, tri: 1));
            var spec = new LayeredSpanWalkabilitySpec(50, 500_000, 0);
            var output = new LayeredSpanWalkabilityScratch(
                columnCapacity: 1,
                classifiedSpanCapacity: 2,
                walkableSpanCapacity: 1);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanWalkabilityScratch.walkableSpanCapacity"));
            Assert.That(ex.Message, Does.Contain("required 2"));
            AssertEmpty(output);
        }

        [Test]
        public void Classify_ManySolidOnlyPlusOneWalkable_WalkableCapacityOne_Succeeds()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 10, SolidOnly, nx: 1, ny: 0, nz: 0, stableId: 1, tri: 0),
                Span(minY: 20, maxY: 30, SolidOnly, nx: 1, ny: 0, nz: 0, stableId: 2, tri: 1),
                Span(minY: 40, maxY: 50, SolidOnly, nx: 1, ny: 0, nz: 0, stableId: 3, tri: 2),
                Span(minY: 60, maxY: 70, SolidOnly, nx: 1, ny: 0, nz: 0, stableId: 4, tri: 3),
                Span(minY: 80, maxY: 90, SolidOnly, nx: 1, ny: 0, nz: 0, stableId: 5, tri: 4),
                Span(minY: 200, maxY: 200, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 6, tri: 5));

            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 50,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var output = new LayeredSpanWalkabilityScratch(
                columnCapacity: 1,
                classifiedSpanCapacity: 6,
                walkableSpanCapacity: 1);

            LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output);

            Assert.That(raw.SpanCount, Is.EqualTo(6));
            Assert.That(output.WalkableSpanCount, Is.EqualTo(1));
            Assert.That(output.WalkableSpanIndices[0], Is.EqualTo(5));
            Assert.That(output.SpanStatus[5], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            for (int i = 0; i < 5; i++)
            {
                Assert.That(output.SpanStatus[i], Is.EqualTo(LayeredSpanWalkabilityStatus.SolidOnly));
            }
        }

        [Test]
        public void Classify_WalkableCapacityOverflow_ReportsActualAcceptedNotRawSpanCount_EmptyOutput()
        {
            // raw spanCount = 5; actual accepted walkables = 2 (SolidOnly + SlopeRejected skipped).
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0),
                Span(minY: 50, maxY: 50, SolidOnly, nx: 0, ny: 1, nz: 0, stableId: 2, tri: 1),
                Span(minY: 200, maxY: 200, FloorFlags, nx: 100, ny: 1, nz: 0, stableId: 3, tri: 2),
                Span(minY: 400, maxY: 400, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 4, tri: 3),
                Span(minY: 600, maxY: 600, SolidOnly, nx: 1, ny: 0, nz: 0, stableId: 5, tri: 4));

            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 50,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var output = new LayeredSpanWalkabilityScratch(
                columnCapacity: 1,
                classifiedSpanCapacity: 5,
                walkableSpanCapacity: 1);

            Assert.That(raw.SpanCount, Is.EqualTo(5));
            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanWalkabilityScratch.walkableSpanCapacity"));
            Assert.That(ex.Message, Does.Contain("required 2"));
            Assert.That(ex.Message, Does.Not.Contain("required 5"));
            AssertEmpty(output);
        }

        [Test]
        public void Classify_ExtremeScaledNormals_ReverseWindingMatchesNearThreshold_ZeroStaysDegenerate()
        {
            // Components above 2^40 force shared magnitude scaling; odd extreme X is not a mirror under
            // arithmetic signed right-shift, so abs-then-restore-sign must keep winding-reversed status identical.
            Int128 nx = ((Int128)1 << 41) + 1;
            Int128 nyAccept = 1_269_606_668_548;
            Int128 nyReject = nyAccept - 1;

            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx, nyAccept, nz: 0, stableId: 1, tri: 0),
                Span(minY: 1_000, maxY: 1_000, FloorFlags, -nx, -nyAccept, nz: 0, stableId: 2, tri: 1),
                Span(minY: 2_000, maxY: 2_000, FloorFlags, nx, nyReject, nz: 0, stableId: 3, tri: 2),
                Span(minY: 3_000, maxY: 3_000, FloorFlags, -nx, -nyReject, nz: 0, stableId: 4, tri: 3),
                Span(minY: 4_000, maxY: 4_000, FloorFlags, nx: 0, ny: 0, nz: 0, stableId: 5, tri: 4));

            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 50,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var output = new LayeredSpanWalkabilityScratch(1, 5, 5);
            LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output);

            Assert.That(output.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            Assert.That(output.SpanStatus[1], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            Assert.That(output.SpanStatus[0], Is.EqualTo(output.SpanStatus[1]));
            Assert.That(output.SpanStatus[2], Is.EqualTo(LayeredSpanWalkabilityStatus.SlopeRejected));
            Assert.That(output.SpanStatus[3], Is.EqualTo(LayeredSpanWalkabilityStatus.SlopeRejected));
            Assert.That(output.SpanStatus[2], Is.EqualTo(output.SpanStatus[3]));
            Assert.That(output.SpanStatus[4], Is.EqualTo(LayeredSpanWalkabilityStatus.DegenerateNormal));
            Assert.That(ToArray(output.WalkableSpanIndices), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Classify_InputOrder_BuildsDeterministicCompactWalkableCsr()
        {
            // Two columns; walkables compact in source-span order with Solid-only gaps skipped.
            LayeredSpanScratch raw = SeedColumns(
                columnCount: 2,
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0, column: 0),
                Span(minY: 50, maxY: 50, SolidOnly, nx: 0, ny: 1, nz: 0, stableId: 2, tri: 1, column: 0),
                Span(minY: 200, maxY: 200, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 3, tri: 2, column: 0),
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 4, tri: 3, column: 1),
                Span(minY: 1_000, maxY: 1_000, FloorFlags, nx: 100, ny: 1, nz: 0, stableId: 5, tri: 4, column: 1));

            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 50,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var output = new LayeredSpanWalkabilityScratch(2, 5, 5);
            LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output);

            Assert.That(ToArray(output.WalkableSpanIndices), Is.EqualTo(new[] { 0, 2, 3 }));
            Assert.That(ToArray(output.ColumnWalkableCounts), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(ToArray(output.ColumnWalkableOffsets), Is.EqualTo(new[] { 0, 2, 3 }));
            Assert.That(output.SpanStatus[1], Is.EqualTo(LayeredSpanWalkabilityStatus.SolidOnly));
            Assert.That(output.SpanStatus[4], Is.EqualTo(LayeredSpanWalkabilityStatus.SlopeRejected));
        }

        [Test]
        public void Classify_WarmedRepeatedClassify_AllocatesExactlyZeroBytes()
        {
            LayeredSpanScratch raw = SeedColumns(
                columnCount: 2,
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0, column: 0),
                Span(minY: 250, maxY: 250, SolidOnly, nx: 0, ny: 1, nz: 0, stableId: 2, tri: 1, column: 0),
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 100, nz: 0, stableId: 3, tri: 2, column: 1),
                Span(minY: 0, maxY: 120, SolidOnly, nx: 1, ny: 0, nz: 0, stableId: 4, tri: 3, column: 1),
                Span(minY: 400, maxY: 400, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 5, tri: 4, column: 1));
            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 180,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            var output = new LayeredSpanWalkabilityScratch(2, 8, 8);

            for (int i = 0; i < 64; i++)
            {
                LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                LayeredSpanWalkabilityClassifier.Classify(raw, in spec, output);
                if (output.ClassifiedSpanCount < 0)
                {
                    throw new InvalidOperationException("Unreachable guard to keep output live.");
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), $"Warmed layered-span walkability classify allocated {allocated} bytes.");
            Assert.That(output.WalkableSpanCount, Is.GreaterThan(0));
        }

        private static void AssertEmpty(LayeredSpanWalkabilityScratch output)
        {
            Assert.That(output.ColumnCount, Is.EqualTo(0));
            Assert.That(output.ClassifiedSpanCount, Is.EqualTo(0));
            Assert.That(output.WalkableSpanCount, Is.EqualTo(0));
            Assert.That(output.SpanStatus.Length, Is.EqualTo(0));
            Assert.That(output.WalkableSpanIndices.Length, Is.EqualTo(0));
        }

        private readonly struct SpanSeed
        {
            public SpanSeed(
                int minY,
                int maxY,
                NavTriangleSurfaceFlags flags,
                Int128 nx,
                Int128 ny,
                Int128 nz,
                int stableId,
                int tri,
                int column)
            {
                MinY = minY;
                MaxY = maxY;
                Flags = flags;
                Nx = nx;
                Ny = ny;
                Nz = nz;
                StableId = stableId;
                Tri = tri;
                Column = column;
            }

            public int MinY { get; }
            public int MaxY { get; }
            public NavTriangleSurfaceFlags Flags { get; }
            public Int128 Nx { get; }
            public Int128 Ny { get; }
            public Int128 Nz { get; }
            public int StableId { get; }
            public int Tri { get; }
            public int Column { get; }
        }

        private static SpanSeed Span(
            int minY,
            int maxY,
            NavTriangleSurfaceFlags flags,
            Int128 nx,
            Int128 ny,
            Int128 nz,
            int stableId,
            int tri,
            int column = 0)
            => new(minY, maxY, flags, nx, ny, nz, stableId, tri, column);

        private static LayeredSpanScratch SeedSingleColumn(params SpanSeed[] spans)
            => SeedColumns(columnCount: 1, spans);

        private static LayeredSpanScratch SeedColumns(int columnCount, params SpanSeed[] spans)
        {
            var scratch = new LayeredSpanScratch(columnCount, spans.Length);
            scratch.PrepareColumns(columnCount);
            Span<int> counts = scratch.MutableColumnSpanCounts;
            for (int i = 0; i < spans.Length; i++)
            {
                counts[spans[i].Column]++;
            }

            Span<int> offsets = scratch.MutableColumnSpanOffsets;
            Span<int> cursors = scratch.MutableFillCursor;
            int sum = 0;
            for (int c = 0; c < columnCount; c++)
            {
                offsets[c] = sum;
                cursors[c] = sum;
                sum += counts[c];
            }

            offsets[columnCount] = sum;

            for (int i = 0; i < spans.Length; i++)
            {
                SpanSeed s = spans[i];
                int index = cursors[s.Column]++;
                scratch.WriteSpan(
                    index,
                    s.MinY,
                    s.MaxY,
                    s.Tri,
                    s.StableId,
                    areaId: 1,
                    s.Flags,
                    s.Nx,
                    s.Ny,
                    s.Nz,
                    LayeredSpanBoundaryMask.None,
                    westMinYcm: 0,
                    westMaxYcm: 0,
                    westMinZcm: 0,
                    westMaxZcm: 0,
                    eastMinYcm: 0,
                    eastMaxYcm: 0,
                    eastMinZcm: 0,
                    eastMaxZcm: 0,
                    northMinYcm: 0,
                    northMaxYcm: 0,
                    northMinXcm: 0,
                    northMaxXcm: 0,
                    southMinYcm: 0,
                    southMaxYcm: 0,
                    southMinXcm: 0,
                    southMaxXcm: 0);
            }

            for (int c = 0; c < columnCount; c++)
            {
                scratch.SortColumnSpans(offsets[c], counts[c]);
            }

            scratch.CommitSpanCount(sum);
            return scratch;
        }

        private static int[] ToArray(ReadOnlySpan<int> span)
        {
            var result = new int[span.Length];
            span.CopyTo(result);
            return result;
        }
    }
}
