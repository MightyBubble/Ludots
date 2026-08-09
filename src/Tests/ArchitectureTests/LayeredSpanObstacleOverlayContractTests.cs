using System;
using System.Text;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanObstacleOverlayContractTests
    {
        private const string GroundLayerId = "Ground";
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        [Test]
        public void Overlay_IntersectingWallBlocksFloor_BelowAndAboveDoNot_YTouchDoesNot()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0));
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var walk = Classify(raw, agentHeightCm: 180);

            // Wall through agent body blocks.
            Apply(raw, walk, grid, CircleObstacle(centerX: 50, centerZ: 50, radius: 40, minY: 50, maxY: 150), agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.ObstacleBlocked));
            Assert.That(walk.WalkableSpanCount, Is.EqualTo(0));

            // Rebuild classify for below-head / above-agent cases.
            walk = Classify(raw, agentHeightCm: 180);
            Apply(raw, walk, grid, CircleObstacle(centerX: 50, centerZ: 50, radius: 40, minY: -100, maxY: 0), agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));

            walk = Classify(raw, agentHeightCm: 180);
            Apply(raw, walk, grid, CircleObstacle(centerX: 50, centerZ: 50, radius: 40, minY: 180, maxY: 300), agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));

            // Exact Y boundary touch is not overlap: obstacle [180,200) vs agent [0,180).
            walk = Classify(raw, agentHeightCm: 180);
            Apply(raw, walk, grid, CircleObstacle(centerX: 50, centerZ: 50, radius: 40, minY: 180, maxY: 200), agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
        }

        [Test]
        public void Overlay_LowObstacleBlocksLowFloor_NotStackedHighFloor()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0),
                Span(minY: 500, maxY: 500, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 2, tri: 1));
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var walk = Classify(raw, agentHeightCm: 180);
            Assert.That(walk.WalkableSpanCount, Is.EqualTo(2));

            Apply(raw, walk, grid, CircleObstacle(centerX: 50, centerZ: 50, radius: 40, minY: 0, maxY: 100), agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.ObstacleBlocked));
            Assert.That(walk.SpanStatus[1], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            Assert.That(walk.WalkableSpanCount, Is.EqualTo(1));
            Assert.That(walk.WalkableSpanIndices[0], Is.EqualTo(1));
        }

        [Test]
        public void Overlay_BridgeUnderAndBridgeOver_PreserveClearanceSemantics()
        {
            // Floor at 0 and bridge deck at 300; low wall under deck should block floor only.
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0),
                Span(minY: 300, maxY: 300, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 2, tri: 1));
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var walk = Classify(raw, agentHeightCm: 180);

            Apply(raw, walk, grid, CircleObstacle(centerX: 50, centerZ: 50, radius: 40, minY: 0, maxY: 200), agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.ObstacleBlocked));
            Assert.That(walk.SpanStatus[1], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
        }

        [Test]
        public void Overlay_PolygonAndCircle_LayerAndDisabledSemantics()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0));
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);

            var polygon = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "poly",
                        Enabled = true,
                        Kind = NavObstacleKind.Polygon,
                        LayerId = GroundLayerId,
                        MinYcm = 0,
                        MaxYcm = 200,
                        Points =
                        {
                            new NavPointCm(0, 0),
                            new NavPointCm(100, 0),
                            new NavPointCm(100, 100),
                            new NavPointCm(0, 100)
                        }
                    }
                }
            };
            var walk = Classify(raw, agentHeightCm: 180);
            Apply(raw, walk, grid, polygon, agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.ObstacleBlocked));

            var wrongLayer = CircleObstacle(centerX: 50, centerZ: 50, radius: 40, minY: 0, maxY: 200);
            wrongLayer.Obstacles[0].LayerId = "Other";
            walk = Classify(raw, agentHeightCm: 180);
            Apply(raw, walk, grid, wrongLayer, agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));

            var disabled = CircleObstacle(centerX: 50, centerZ: 50, radius: 40, minY: 0, maxY: 200);
            disabled.Obstacles[0].Enabled = false;
            walk = Classify(raw, agentHeightCm: 180);
            Apply(raw, walk, grid, disabled, agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
        }

        [Test]
        public void Overlay_EmptyObstacleSet_IsDeterministicNoOpWithValidProvenance()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0));
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var walk = Classify(raw, agentHeightCm: 180);
            ulong genBefore = walk.ContentGeneration;
            var empty = new NavObstacleSet();
            Apply(raw, walk, grid, empty, agentHeightCm: 180);
            Assert.That(walk.SpanStatus[0], Is.EqualTo(LayeredSpanWalkabilityStatus.Walkable));
            Assert.That(walk.WalkableSpanCount, Is.EqualTo(1));
            Assert.That(walk.ContentGeneration, Is.EqualTo(genBefore + 1));
            Assert.That(walk.WasBuiltFrom(raw), Is.True);
        }

        [Test]
        public void Overlay_DeterministicBytes_AndRejectsMismatchedScratchIdentity()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0));
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var obstacles = CircleObstacle(50, 50, 40, 0, 100);

            var walkA = Classify(raw, agentHeightCm: 180);
            Apply(raw, walkA, grid, obstacles, agentHeightCm: 180);
            string hashA = HashWalk(walkA);

            var walkB = Classify(raw, agentHeightCm: 180);
            Apply(raw, walkB, grid, obstacles, agentHeightCm: 180);
            Assert.That(HashWalk(walkB), Is.EqualTo(hashA));

            LayeredSpanScratch otherRaw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => Apply(otherRaw, walkB, grid, obstacles, agentHeightCm: 180))!;
            Assert.That(ex.Message, Does.Contain("walkability"));
        }

        [Test]
        public void Overlay_WarmedBuild_AllocatesZeroBytes()
        {
            LayeredSpanScratch raw = SeedSingleColumn(
                Span(minY: 0, maxY: 0, FloorFlags, nx: 0, ny: 1, nz: 0, stableId: 1, tri: 0));
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var obstacles = CircleObstacle(50, 50, 40, 0, 100);
            var walk = new LayeredSpanWalkabilityScratch(raw.ColumnCount, raw.SpanCount, raw.SpanCount);
            var spec = new LayeredSpanWalkabilitySpec(
                agentHeightCm: 180,
                minWalkableUpDotQ1M: 500_000,
                sameSurfaceToleranceCm: 0);
            for (int i = 0; i < 64; i++)
            {
                LayeredSpanWalkabilityClassifier.Classify(raw, in spec, walk);
                Apply(raw, walk, grid, obstacles, agentHeightCm: 180);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                LayeredSpanWalkabilityClassifier.Classify(raw, in spec, walk);
                Apply(raw, walk, grid, obstacles, agentHeightCm: 180);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), $"Warmed walkability/overlay path allocated {allocated} bytes.");
        }

        [Test]
        public void NavObstacleSet_RejectsMissingOrInvertedVerticalExtents()
        {
            var layers = new[] { new NavLayerConfig { Id = GroundLayerId, Layer = 0 } };
            var missing = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "bad",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(0, 0),
                        RadiusCm = 10,
                        MinYcm = 0,
                        MaxYcm = 0
                    }
                }
            };
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => missing.ValidateForBake(layers, "obstacles"))!;
            Assert.That(ex.Message, Does.Contain("minYcm"));
        }

        private static void Apply(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walk,
            in LayeredSpanRasterGridSpec grid,
            INavObstacleSource obstacles,
            int agentHeightCm)
        {
            LayeredSpanObstacleOverlayBuilder.Apply(raw, walk, in grid, obstacles, GroundLayerId, agentHeightCm);
        }

        private static LayeredSpanWalkabilityScratch Classify(LayeredSpanScratch raw, int agentHeightCm)
        {
            var walk = new LayeredSpanWalkabilityScratch(raw.ColumnCount, raw.SpanCount, raw.SpanCount);
            var spec = new LayeredSpanWalkabilitySpec(agentHeightCm, minWalkableUpDotQ1M: 500_000, sameSurfaceToleranceCm: 0);
            LayeredSpanWalkabilityClassifier.Classify(raw, in spec, walk);
            return walk;
        }

        private static NavObstacleSet CircleObstacle(int centerX, int centerZ, int radius, int minY, int maxY)
            => new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "circle",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(centerX, centerZ),
                        RadiusCm = radius,
                        MinYcm = minY,
                        MaxYcm = maxY
                    }
                }
            };

        private static string HashWalk(LayeredSpanWalkabilityScratch walk)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < walk.ClassifiedSpanCount; i++)
            {
                sb.Append((byte)walk.SpanStatus[i]).Append(',').Append(walk.SpanClearanceCm[i]).Append(';');
            }

            for (int i = 0; i < walk.WalkableSpanCount; i++)
            {
                sb.Append(walk.WalkableSpanIndices[i]).Append(',');
            }

            return sb.ToString();
        }

        private readonly record struct SpanSeed(
            int MinY,
            int MaxY,
            NavTriangleSurfaceFlags Flags,
            Int128 Nx,
            Int128 Ny,
            Int128 Nz,
            int StableId,
            int Tri,
            int Column);

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
        {
            var scratch = new LayeredSpanScratch(1, spans.Length);
            scratch.PrepareColumns(1);
            Span<int> counts = scratch.MutableColumnSpanCounts;
            counts[0] = spans.Length;
            Span<int> offsets = scratch.MutableColumnSpanOffsets;
            Span<int> cursors = scratch.MutableFillCursor;
            offsets[0] = 0;
            cursors[0] = 0;
            offsets[1] = spans.Length;
            for (int i = 0; i < spans.Length; i++)
            {
                SpanSeed s = spans[i];
                int index = cursors[0]++;
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

            scratch.SortColumnSpans(0, spans.Length);
            scratch.CommitSpanCount(spans.Length);
            return scratch;
        }
    }
}
