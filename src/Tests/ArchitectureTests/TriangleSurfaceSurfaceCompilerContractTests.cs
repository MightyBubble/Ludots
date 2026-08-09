using System;
using System.Collections.Generic;
using Ludots.Core.Map.Board;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Geometry;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class TriangleSurfaceSurfaceCompilerContractTests
    {
        private const string GroundLayerId = "Ground";

        private static void AssertTriangleGeometryEqual(
            NavTriangleSurfaceSnapshot left,
            int leftTriangle,
            NavTriangleSurfaceSnapshot right,
            int rightTriangle,
            int localX,
            int localZ,
            int triangleOffset)
        {
            Assert.That(right.TriAreaIds[rightTriangle], Is.EqualTo(left.TriAreaIds[leftTriangle]));
            Assert.That(right.TriFlags[rightTriangle], Is.EqualTo(left.TriFlags[leftTriangle]));

            int leftA = left.TriA[leftTriangle];
            int leftB = left.TriB[leftTriangle];
            int leftC = left.TriC[leftTriangle];
            int rightA = right.TriA[rightTriangle];
            int rightB = right.TriB[rightTriangle];
            int rightC = right.TriC[rightTriangle];
            Assert.That(
                new[]
                {
                    right.VertexXcm[rightA], right.VertexYcm[rightA], right.VertexZcm[rightA],
                    right.VertexXcm[rightB], right.VertexYcm[rightB], right.VertexZcm[rightB],
                    right.VertexXcm[rightC], right.VertexYcm[rightC], right.VertexZcm[rightC]
                },
                Is.EqualTo(new[]
                {
                    left.VertexXcm[leftA], left.VertexYcm[leftA], left.VertexZcm[leftA],
                    left.VertexXcm[leftB], left.VertexYcm[leftB], left.VertexZcm[leftB],
                    left.VertexXcm[leftC], left.VertexYcm[leftC], left.VertexZcm[leftC]
                }),
                $"Equivalent resident-local tile ({localX},{localZ}) triangle[{triangleOffset}] geometry must match.");
        }

        [Test]
        public void LogicTerrainCompiler_Grid_IsDeterministicAcrossRuns()
        {
            var terrain = new FlatGridLogicTerrainField(8, 8, chunkSizeCells: 4);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex a = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, haloPaddingCm: 100);
            NavTriangleSurfaceTileIndex b = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, haloPaddingCm: 100);

            Assert.That(a.Surface.TriangleCount, Is.EqualTo(b.Surface.TriangleCount));
            Assert.That(a.Surface.TriStableIds.ToArray(), Is.EqualTo(b.Surface.TriStableIds.ToArray()));
            Assert.That(a.Surface.VertexXcm.ToArray(), Is.EqualTo(b.Surface.VertexXcm.ToArray()));
            Assert.That(a.Surface.VertexYcm.ToArray(), Is.EqualTo(b.Surface.VertexYcm.ToArray()));
            Assert.That(a.Surface.VertexZcm.ToArray(), Is.EqualTo(b.Surface.VertexZcm.ToArray()));
            Assert.That(a.GetTriangleIndices(0, 0).ToArray(), Is.EqualTo(b.GetTriangleIndices(0, 0).ToArray()));
        }

        [Test]
        public void FlatGridTileCsr_EquivalentInteriorResidentNeighborhood_IsWorldSizeInvariant()
        {
            const int smallChunks = 8;
            const int largeChunks = 64;
            const int residentOriginInLarge = 28;
            const int chunkSizeCells = 64;
            const int cellSizeCm = SpatialScaleDefaults.CellCm;
            const int haloPaddingCm = 200;
            int smallCells = checked(smallChunks * chunkSizeCells);
            int largeCells = checked(largeChunks * chunkSizeCells);
            int smallOriginCm = checked(-(smallCells * cellSizeCm) / 2);
            int largeOriginCm = checked(-(largeCells * cellSizeCm) / 2);
            var smallTerrain = new FlatGridLogicTerrainField(
                widthCells: smallCells,
                heightCells: smallCells,
                cellSizeCm: cellSizeCm,
                chunkSizeCells: chunkSizeCells,
                originXcm: smallOriginCm,
                originZcm: smallOriginCm);
            var largeTerrain = new FlatGridLogicTerrainField(
                widthCells: largeCells,
                heightCells: largeCells,
                cellSizeCm: cellSizeCm,
                chunkSizeCells: chunkSizeCells,
                originXcm: largeOriginCm,
                originZcm: largeOriginCm);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex small = LogicTerrainTriangleSurfaceCompiler.Compile(
                smallTerrain,
                build,
                haloPaddingCm);
            NavTriangleSurfaceTileIndex large = LogicTerrainTriangleSurfaceCompiler.Compile(
                largeTerrain,
                build,
                haloPaddingCm);

            Assert.That(small.Grid.TileWidthCm, Is.EqualTo(large.Grid.TileWidthCm));
            Assert.That(small.Grid.TileHeightCm, Is.EqualTo(large.Grid.TileHeightCm));
            Assert.That(small.Grid.HaloPaddingCm, Is.EqualTo(haloPaddingCm));
            Assert.That(large.Grid.HaloPaddingCm, Is.EqualTo(haloPaddingCm));

            int insetTilesX = CeilDivPositive(small.Grid.HaloPaddingCm, small.Grid.TileWidthCm);
            int insetTilesZ = CeilDivPositive(small.Grid.HaloPaddingCm, small.Grid.TileHeightCm);
            Assert.That(checked(insetTilesX * 2), Is.LessThan(smallChunks),
                "Halo-safe X interior must be non-empty for the authored resident window.");
            Assert.That(checked(insetTilesZ * 2), Is.LessThan(smallChunks),
                "Halo-safe Z interior must be non-empty for the authored resident window.");

            long smallFullRefs = CountTriangleReferences(small, 0, 0, smallChunks, smallChunks);
            long largeFullRefs = CountTriangleReferences(
                large,
                residentOriginInLarge,
                residentOriginInLarge,
                smallChunks,
                smallChunks);
            Assert.That(
                smallFullRefs,
                Is.LessThan(largeFullRefs),
                "Full small-world CSR triangle refs must be less than the open-world interior " +
                $"(world-boundary halo truncation); small={smallFullRefs} large={largeFullRefs}.");

            long smallMatchedRefs = 0L;
            long largeMatchedRefs = 0L;
            for (int localZ = insetTilesZ; localZ < checked(smallChunks - insetTilesZ); localZ++)
            {
                for (int localX = insetTilesX; localX < checked(smallChunks - insetTilesX); localX++)
                {
                    ReadOnlySpan<int> smallTriangles = small.GetTriangleIndices(localX, localZ);
                    ReadOnlySpan<int> largeTriangles = large.GetTriangleIndices(
                        residentOriginInLarge + localX,
                        residentOriginInLarge + localZ);
                    Assert.That(
                        largeTriangles.Length,
                        Is.EqualTo(smallTriangles.Length),
                        $"Equivalent halo-safe resident-local tile ({localX},{localZ}) must receive equal triangle counts.");
                    smallMatchedRefs = checked(smallMatchedRefs + smallTriangles.Length);
                    largeMatchedRefs = checked(largeMatchedRefs + largeTriangles.Length);
                    for (int i = 0; i < smallTriangles.Length; i++)
                    {
                        AssertTriangleGeometryEqual(
                            small.Surface,
                            smallTriangles[i],
                            large.Surface,
                            largeTriangles[i],
                            localX,
                            localZ,
                            i);
                    }
                }
            }

            Assert.That(
                largeMatchedRefs,
                Is.EqualTo(smallMatchedRefs),
                "Matched halo-safe interior CSR triangle refs must be equal across world sizes. " +
                $"small={smallMatchedRefs} large={largeMatchedRefs}.");
        }

        private static long CountTriangleReferences(
            NavTriangleSurfaceTileIndex surface,
            int originTileX,
            int originTileZ,
            int widthTiles,
            int heightTiles)
        {
            long total = 0L;
            for (int localZ = 0; localZ < heightTiles; localZ++)
            {
                for (int localX = 0; localX < widthTiles; localX++)
                {
                    total = checked(total + surface.GetTriangleIndices(
                        checked(originTileX + localX),
                        checked(originTileZ + localZ)).Length);
                }
            }

            return total;
        }

        private static int CeilDivPositive(int numerator, int denominator)
        {
            if (numerator < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "Numerator must be nonnegative.");
            }

            if (denominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "Denominator must be positive.");
            }

            if (numerator == 0)
            {
                return 0;
            }

            return checked((numerator + denominator - 1) / denominator);
        }

        [Test]
        public void LogicTerrainCompiler_Hex_IsDeterministicAndEmitsWalkCandidateSolid()
        {
            var terrain = new HexLikeFlatLogicTerrainField(widthCells: 4, heightCells: 4, chunkSizeCells: 4);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex a = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, haloPaddingCm: 100);
            NavTriangleSurfaceTileIndex b = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, haloPaddingCm: 100);

            Assert.That(terrain.Topology, Is.EqualTo(LogicTerrainTopology.Hex));
            Assert.That(a.Surface.TriangleCount, Is.GreaterThan(0));
            Assert.That(
                a.Grid.TileWidthCm,
                Is.EqualTo((int)MathF.Round(HexCoordinates.HexWidth * terrain.ChunkSizeCells * SpatialScaleDefaults.CellCm)));
            Assert.That(
                a.Grid.TileHeightCm,
                Is.EqualTo((int)MathF.Round(HexCoordinates.RowSpacing * terrain.ChunkSizeCells * SpatialScaleDefaults.CellCm)));
            Assert.That(a.Surface.TriStableIds.ToArray(), Is.EqualTo(b.Surface.TriStableIds.ToArray()));
            for (int i = 0; i < a.Surface.TriangleCount; i++)
            {
                Assert.That(
                    a.Surface.TriFlags[i],
                    Is.EqualTo(NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate));
            }
        }

        [Test]
        public void FlatGridSparseCompile_EightByEightChunks_IsExactlyOneHundredTwentyEightTrianglesAndOChunks()
        {
            const int chunkSize = 4;
            const int chunks = 8;
            var inner = new FlatGridLogicTerrainField(chunks * chunkSize, chunks * chunkSize, chunkSizeCells: chunkSize);
            var counting = new CountingLogicTerrainField(inner);
            var build = new NavBuildConfig(1f, 0.6f, 1);

            NavTriangleSurfaceTileIndex first = LogicTerrainTriangleSurfaceCompiler.Compile(counting, build, haloPaddingCm: 100);
            int getCellCalls = counting.GetCellCalls;
            int getWorldCalls = counting.GetWorldPositionCalls;
            Assert.That(first.Surface.TriangleCount, Is.EqualTo(128));
            Assert.That(first.Grid.TileCountX, Is.EqualTo(8));
            Assert.That(first.Grid.TileCountZ, Is.EqualTo(8));
            Assert.That(getCellCalls, Is.EqualTo(1), "Uniform FlatGrid must sample one cell, not enumerate every cell.");
            Assert.That(getWorldCalls, Is.LessThanOrEqualTo(4), "World-position work must stay O(chunks), not O(cells).");
            Assert.That(getCellCalls + getWorldCalls, Is.LessThan(chunks * chunks));

            for (int tz = 0; tz < 8; tz++)
            {
                for (int tx = 0; tx < 8; tx++)
                {
                    ReadOnlySpan<int> local = first.GetTriangleIndices(tx, tz);
                    Assert.That(local.Length, Is.GreaterThan(0), $"Tile ({tx},{tz}) must retain local triangle evidence.");
                }
            }

            counting.ResetCounters();
            NavTriangleSurfaceTileIndex second = LogicTerrainTriangleSurfaceCompiler.Compile(counting, build, haloPaddingCm: 100);
            Assert.That(second.Surface.TriangleCount, Is.EqualTo(128));
            Assert.That(second.Surface.TriStableIds.ToArray(), Is.EqualTo(first.Surface.TriStableIds.ToArray()));
            Assert.That(second.Surface.VertexXcm.ToArray(), Is.EqualTo(first.Surface.VertexXcm.ToArray()));
            Assert.That(second.Surface.VertexZcm.ToArray(), Is.EqualTo(first.Surface.VertexZcm.ToArray()));
            Assert.That(counting.GetCellCalls, Is.EqualTo(1));
        }

        [Test]
        public void FlatGridSparseCompile_SixtyFourBySixtyFourChunks_IsExactlyEightThousandOneHundredNinetyTwoAndNoPerCellEnumeration()
        {
            const int chunkSize = SpatialScaleDefaults.TerrainChunkCells;
            const int chunks = 64;
            var inner = new FlatGridLogicTerrainField(chunks * chunkSize, chunks * chunkSize, chunkSizeCells: chunkSize);
            var counting = new CountingLogicTerrainField(inner);
            var build = new NavBuildConfig(1f, 0.6f, 1);

            NavTriangleSurfaceTileIndex index = LogicTerrainTriangleSurfaceCompiler.Compile(counting, build, haloPaddingCm: 100);
            Assert.That(index.Surface.TriangleCount, Is.EqualTo(8192));
            Assert.That(index.Grid.TileCount, Is.EqualTo(4096));
            Assert.That(counting.GetCellCalls, Is.EqualTo(1));
            Assert.That(
                counting.GetWorldPositionCalls,
                Is.LessThanOrEqualTo(4),
                $"64x64 sparse compile must not enumerate cells; world reads were {counting.GetWorldPositionCalls}.");
            Assert.That(counting.GetCellCalls, Is.LessThan(chunks));

            ReadOnlySpan<int> corner = index.GetTriangleIndices(0, 0);
            ReadOnlySpan<int> mid = index.GetTriangleIndices(31, 31);
            ReadOnlySpan<int> far = index.GetTriangleIndices(63, 63);
            Assert.That(corner.Length, Is.GreaterThan(0));
            Assert.That(mid.Length, Is.GreaterThan(0));
            Assert.That(far.Length, Is.GreaterThan(0));
        }

        [Test]
        public void FlatGridSparseCompile_BlockedUniformField_EmitsValidEmptySurfaceWithExtents()
        {
            var terrain = new FlatGridLogicTerrainField(
                8,
                8,
                chunkSizeCells: 4,
                cell: new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Blocked));
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex index = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, haloPaddingCm: 100);
            Assert.That(index.Surface.TriangleCount, Is.EqualTo(0));
            Assert.That(index.Grid.TileCountX, Is.EqualTo(2));
            Assert.That(index.Grid.TileCountZ, Is.EqualTo(2));
            Assert.That(index.Grid.TileWidthCm, Is.EqualTo(400));
            Assert.That(index.Grid.TileHeightCm, Is.EqualTo(400));
        }

        [Test]
        public void FlatGridLogicTerrainField_ExplicitNegativeOrigin_PropagatesToWorldMetersAndDerivedTileGrid()
        {
            const int originXcm = -12800;
            const int originZcm = -6400;
            var terrain = new FlatGridLogicTerrainField(
                widthCells: 8,
                heightCells: 4,
                cellSizeCm: SpatialScaleDefaults.CellCm,
                chunkSizeCells: 4,
                originXcm: originXcm,
                originZcm: originZcm);

            Assert.That(terrain.OriginXcm, Is.EqualTo(originXcm));
            Assert.That(terrain.OriginZcm, Is.EqualTo(originZcm));
            terrain.GetWorldPositionMeters(0, 0, out float x0, out float z0);
            terrain.GetWorldPositionMeters(3, 1, out float x3, out float z1);
            Assert.That(SpatialScaleDefaults.MetersToCentimeters(x0), Is.EqualTo(originXcm).Within(0.01f));
            Assert.That(SpatialScaleDefaults.MetersToCentimeters(z0), Is.EqualTo(originZcm).Within(0.01f));
            Assert.That(SpatialScaleDefaults.MetersToCentimeters(x3), Is.EqualTo(originXcm + 300).Within(0.01f));
            Assert.That(SpatialScaleDefaults.MetersToCentimeters(z1), Is.EqualTo(originZcm + 100).Within(0.01f));

            NavTriangleSurfaceTileGrid derived = LogicTerrainTriangleSurfaceCompiler.DeriveTileGrid(terrain, haloPaddingCm: 100);
            Assert.That(derived.OriginXcm, Is.EqualTo(originXcm));
            Assert.That(derived.OriginZcm, Is.EqualTo(originZcm));
            Assert.That(derived.TileWidthCm, Is.EqualTo(400));
            Assert.That(derived.TileHeightCm, Is.EqualTo(400));
            Assert.That(derived.TileCountX, Is.EqualTo(2));
            Assert.That(derived.TileCountZ, Is.EqualTo(1));

            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex index = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, haloPaddingCm: 100);
            Assert.That(index.Grid.OriginXcm, Is.EqualTo(originXcm));
            Assert.That(index.Grid.OriginZcm, Is.EqualTo(originZcm));
            Assert.That(index.Surface.TriangleCount, Is.GreaterThan(0));
            int minVertexX = int.MaxValue;
            int minVertexZ = int.MaxValue;
            ReadOnlySpan<int> vx = index.Surface.VertexXcm;
            ReadOnlySpan<int> vz = index.Surface.VertexZcm;
            for (int i = 0; i < vx.Length; i++)
            {
                minVertexX = Math.Min(minVertexX, vx[i]);
                minVertexZ = Math.Min(minVertexZ, vz[i]);
            }

            Assert.That(minVertexX, Is.EqualTo(originXcm));
            Assert.That(minVertexZ, Is.EqualTo(originZcm));
        }

        [Test]
        public void FlatGridLogicTerrainField_LegacyDirectConstructor_RemainsOriginZero()
        {
            var terrain = new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4);
            Assert.That(terrain.OriginXcm, Is.EqualTo(0));
            Assert.That(terrain.OriginZcm, Is.EqualTo(0));
            terrain.GetWorldPositionMeters(0, 0, out float x0, out float z0);
            Assert.That(x0, Is.EqualTo(0f));
            Assert.That(z0, Is.EqualTo(0f));
            NavTriangleSurfaceTileGrid grid = LogicTerrainTriangleSurfaceCompiler.DeriveTileGrid(terrain, haloPaddingCm: 0);
            Assert.That(grid.OriginXcm, Is.EqualTo(0));
            Assert.That(grid.OriginZcm, Is.EqualTo(0));
        }

        [Test]
        public void GridBoardCenteredBounds_MatchFlatGridAndTriangleSurfaceOriginComposition()
        {
            var boardConfig = new BoardConfig
            {
                WidthInMacroTiles = 2,
                HeightInMacroTiles = 2,
                GridCellSizeCm = SpatialScaleDefaults.CellCm,
                ChunkSizeCells = SpatialScaleDefaults.TerrainChunkCells,
                LoadedChunkCapacity = 64
            };
            var board = new GridBoard(
                new BoardId("terrain"),
                "terrain",
                boardConfig);
            Assert.That(board.WorldSize.Bounds.Left, Is.EqualTo(-25600));
            Assert.That(board.WorldSize.Bounds.Top, Is.EqualTo(-25600));

            int widthCells = checked(boardConfig.WidthInMacroTiles * SpatialScaleDefaults.MacroTileCells);
            int heightCells = checked(boardConfig.HeightInMacroTiles * SpatialScaleDefaults.MacroTileCells);
            var terrain = new FlatGridLogicTerrainField(
                widthCells,
                heightCells,
                boardConfig.GridCellSizeCm,
                boardConfig.ChunkSizeCells,
                originXcm: board.WorldSize.Bounds.Left,
                originZcm: board.WorldSize.Bounds.Top);
            NavTriangleSurfaceTileGrid grid = LogicTerrainTriangleSurfaceCompiler.DeriveTileGrid(terrain, haloPaddingCm: 200);
            Assert.That(grid.OriginXcm, Is.EqualTo(board.WorldSize.Bounds.Left));
            Assert.That(grid.OriginZcm, Is.EqualTo(board.WorldSize.Bounds.Top));
            Assert.That(grid.TileCountX, Is.EqualTo(8));
            Assert.That(grid.TileCountZ, Is.EqualTo(8));
            Assert.That(grid.TileWidthCm, Is.EqualTo(6400));
        }

        [Test]
        public void ObstacleHeightFiltering_BlocksOnlyOverlappingWalkCandidates()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 400, 0, 0, 400, 400, 0 },
                vertexYcm: new[] { 0, 0, 0, 0, 300, 300, 300, 300 },
                vertexZcm: new[] { 0, 0, 400, 400, 0, 0, 400, 400 },
                triA: new[] { 0, 0, 4, 4 },
                triB: new[] { 1, 2, 5, 6 },
                triC: new[] { 2, 3, 6, 7 },
                triAreaIds: new byte[] { 0, 0, 0, 0 },
                triStableIds: new[] { 0, 1, 2, 3 },
                triFlags: new[]
                {
                    NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate,
                    NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate,
                    NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate,
                    NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate
                });
            _ = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 1, 1, 100));

            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "low-wall",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(200, 200),
                        RadiusCm = 80,
                        MinYcm = 0,
                        MaxYcm = 150
                    }
                }
            };

            Assert.That(
                NavTriangleObstaclePredicate.IsTriangleBlocked(
                    0, 0, 0, 400, 0, 0, 400, 0, 400,
                    obstacles, GroundLayerId, agentHeightCm: 180, agentRadiusCm: 30),
                Is.True);
            Assert.That(
                NavTriangleObstaclePredicate.IsTriangleBlocked(
                    0, 300, 0, 400, 300, 0, 400, 300, 400,
                    obstacles, GroundLayerId, agentHeightCm: 180, agentRadiusCm: 30),
                Is.False);
        }

        [Test]
        public void ExactPredicates2D_Orient2SignAndExactValues()
        {
            Assert.That(ExactPredicates2D.Orient2Sign(0, 0, 1, 0, 0, 1), Is.EqualTo(1));
            Assert.That(ExactPredicates2D.Orient2Sign(0, 0, 0, 1, 1, 0), Is.EqualTo(-1));
            Assert.That(ExactPredicates2D.Orient2Sign(0, 0, 5, 5, 10, 10), Is.EqualTo(0));

            Int128 v = ExactPredicates2D.Orient2(0, 0, 1_000_000, 0, 0, 1_000_000);
            Assert.That(v, Is.EqualTo((Int128)(1_000_000L * 1_000_000L)));
        }

        [Test]
        public void ExactPredicates2D_InCircleSign_InsideOutsideOnCircle()
        {
            // CCW triangle (0,0),(10,0),(10,10); circumcircle center (5,5) radius sqrt(50).
            int inside = ExactPredicates2D.InCircleSign(0, 0, 10, 0, 10, 10, 5, 5);
            int outside = ExactPredicates2D.InCircleSign(0, 0, 10, 0, 10, 10, 13, 5);
            int onVertex = ExactPredicates2D.InCircleSign(0, 0, 10, 0, 10, 10, 10, 0);
            Assert.That(inside, Is.EqualTo(1));
            Assert.That(outside, Is.EqualTo(-1));
            Assert.That(onVertex, Is.EqualTo(0));
        }

        [Test]
        public void ExactPredicates2D_PointInTriangleStrictAndPointOnSegmentInclusive()
        {
            Assert.That(ExactPredicates2D.PointInTriangleStrict(2, 2, 0, 0, 10, 0, 0, 10), Is.True);
            Assert.That(ExactPredicates2D.PointInTriangleStrict(0, 0, 0, 0, 10, 0, 0, 10), Is.False);
            Assert.That(ExactPredicates2D.PointInTriangleStrict(0, 5, 0, 0, 10, 0, 0, 10), Is.False);
            Assert.That(ExactPredicates2D.PointInTriangleStrict(20, 20, 0, 0, 10, 0, 0, 10), Is.False);

            Assert.That(ExactPredicates2D.PointOnSegmentInclusive(0, 0, 10, 0, 5, 0), Is.True);
            Assert.That(ExactPredicates2D.PointOnSegmentInclusive(0, 0, 10, 0, 10, 0), Is.True);
            Assert.That(ExactPredicates2D.PointOnSegmentInclusive(0, 0, 10, 0, 5, 1), Is.False);
            Assert.That(ExactPredicates2D.PointOnSegmentInclusive(0, 0, 10, 0, 11, 0), Is.False);
        }

        [Test]
        public void ExactPredicates2D_LocalDeltaGuard_RejectsFarCoordinates()
        {
            const long farCm = 1L << 31;
            Assert.Throws<InvalidOperationException>(() =>
                ExactPredicates2D.Orient2(0, 0, unchecked((int)farCm), 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                ExactPredicates2D.InCircleSign(0, 0, 10, 0, 10, 10, 0, unchecked((int)farCm)));
        }

        [Test]
        public void NavSegmentMetrics_LengthCmRounded_IsOverflowProof()
        {
            Assert.That(NavSegmentMetrics.LengthCmRounded(0, 0, 0, 300, 0, 400), Is.EqualTo(500));
            Assert.That(NavSegmentMetrics.LengthCmRounded(0, 0, 0, 0, 300, 0), Is.EqualTo(300));
            Assert.That(NavSegmentMetrics.LengthCmRounded(0, 0, 0, 0, 0, 0), Is.EqualTo(0));
            Assert.That(
                NavSegmentMetrics.RoundEuclideanLengthCm(0, 0, 0, 100_000_000, 0, 0),
                Is.EqualTo(100_000_000));
            Assert.That(
                NavSegmentMetrics.LengthCmRounded(0, 0, 0, 0, 0, 2_000_000_000),
                Is.EqualTo(2_000_000_000));
        }

        [Test]
        public void TriangleSurfaceWalkability_SlopeThreshold_AcceptsFlatAndRejectsSteep()
        {
            // Flat floor: normal (0, n, 0).
            bool flatOk = TriangleSurfaceWalkability.TryAcceptSlope(
                0, 160_000, 0, minWalkableUpDotQ1M: 1_000_000, out bool flatDegenerate);
            Assert.That(flatOk, Is.True);
            Assert.That(flatDegenerate, Is.False);

            // 30-degree plane: normal (0, -866025, 500000), up-dot = cos(30) = 0.8660254.
            bool slope30At30 = TriangleSurfaceWalkability.TryAcceptSlope(
                0, -866_025, 500_000, minWalkableUpDotQ1M: 866_025, out bool deg30);
            Assert.That(slope30At30, Is.True, "30-degree slope must be accepted at the frozen 30-degree threshold.");
            Assert.That(deg30, Is.False);

            bool slope30At29 = TriangleSurfaceWalkability.TryAcceptSlope(
                0, -866_025, 500_000, minWalkableUpDotQ1M: 866_026, out _);
            Assert.That(slope30At29, Is.False, "30-degree slope must be rejected one Q1M above the frozen 30-degree threshold.");

            bool slope30At1 = TriangleSurfaceWalkability.TryAcceptSlope(
                0, -866_025, 500_000, minWalkableUpDotQ1M: 999_848, out _);
            Assert.That(slope30At1, Is.False, "30-degree slope must be rejected at a 1-degree threshold.");

            // 45-degree plane: normal (0, -707107, 707107), up-dot = cos(45).
            // The frozen table stores cos(45) Q1M = 707107 (round-half-away-from-zero), so an exact
            // 45-degree plane is rejected at 707107 and accepted at 707106.
            bool slope45At44 = TriangleSurfaceWalkability.TryAcceptSlope(
                0, -707_107, 707_107, minWalkableUpDotQ1M: 707_106, out bool deg45);
            Assert.That(slope45At44, Is.True, "45-degree slope must be accepted at the rounded-down 44/45 boundary threshold.");
            Assert.That(deg45, Is.False);

            bool slope45At45 = TriangleSurfaceWalkability.TryAcceptSlope(
                0, -707_107, 707_107, minWalkableUpDotQ1M: 707_107, out _);
            Assert.That(slope45At45, Is.False, "Exact 45-degree slope must be rejected at the frozen 45-degree threshold.");

            bool slope45At60 = TriangleSurfaceWalkability.TryAcceptSlope(
                0, -707_107, 707_107, minWalkableUpDotQ1M: 500_000, out _);
            Assert.That(slope45At60, Is.True, "45-degree slope must be accepted at a 60-degree threshold.");

            bool degenerate = TriangleSurfaceWalkability.TryAcceptSlope(
                0, 0, 0, minWalkableUpDotQ1M: 707_107, out bool deg);
            Assert.That(degenerate, Is.False);
            Assert.That(deg, Is.True);
        }

        [Test]
        public void TriangleSurfaceWalkability_VerticalClearance_RequiresLocalCandidateSpan()
        {
            var ex = Assert.Throws<ArgumentException>(() => TriangleSurfaceWalkability.ComputeVerticalClearanceCm(
                walkTopYcm: 0,
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<NavTriangleSurfaceFlags>(),
                walkTriIndex: 0,
                walkMinXcm: 0,
                walkMaxXcm: 400,
                walkMinZcm: 0,
                walkMaxZcm: 400,
                ReadOnlySpan<int>.Empty))!;
            Assert.That(ex.Message, Does.Contain("local candidate"));
            Assert.That(ex.Message, Does.Contain("full-surface scans are forbidden"));
        }

        [Test]
        public void TriangleSurfaceWalkability_IsWalkableTriangleIgnoringObstacles_ClearanceAndFlags()
        {
            NavTriangleSurfaceFlags walk = NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;
            NavTriangleSurfaceFlags solidOnly = NavTriangleSurfaceFlags.Solid;

            // Floor tri 0 at Y=0; ceiling tri 1 at Y=300 (solid-only).
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 0, 0, 400, 0 },
                vertexYcm: new[] { 0, 0, 0, 300, 300, 300 },
                vertexZcm: new[] { 0, 0, 400, 0, 0, 400 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 0, 0 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { walk, solidOnly });

            var candidates = new[] { 0, 1 };
            Assert.That(TriangleSurfaceWalkability.IsWalkableTriangleIgnoringObstacles(
                0,
                surface.VertexXcm,
                surface.VertexYcm,
                surface.VertexZcm,
                surface.TriA,
                surface.TriB,
                surface.TriC,
                surface.TriFlags,
                minWalkableUpDotQ1M: 707_107,
                agentHeightCm: 180,
                candidates), Is.True, "Floor with 300cm clearance must be walkable for a 180cm agent.");

            Assert.That(TriangleSurfaceWalkability.IsWalkableTriangleIgnoringObstacles(
                1,
                surface.VertexXcm,
                surface.VertexYcm,
                surface.VertexZcm,
                surface.TriA,
                surface.TriB,
                surface.TriC,
                surface.TriFlags,
                minWalkableUpDotQ1M: 707_107,
                agentHeightCm: 180,
                candidates), Is.False, "Solid-only ceiling must never be a walk candidate.");

            // Ceiling lowered to Y=100 blocks the 180cm agent by vertical clearance.
            var lowCeiling = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 0, 0, 400, 0 },
                vertexYcm: new[] { 0, 0, 0, 100, 100, 100 },
                vertexZcm: new[] { 0, 0, 400, 0, 0, 400 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 0, 0 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { walk, solidOnly });
            Assert.That(TriangleSurfaceWalkability.IsWalkableTriangleIgnoringObstacles(
                0,
                lowCeiling.VertexXcm,
                lowCeiling.VertexYcm,
                lowCeiling.VertexZcm,
                lowCeiling.TriA,
                lowCeiling.TriB,
                lowCeiling.TriC,
                lowCeiling.TriFlags,
                minWalkableUpDotQ1M: 707_107,
                agentHeightCm: 180,
                candidates), Is.False, "100cm clearance must block a 180cm agent.");
        }

        [Test]
        public void NavBakeCanonicalHash_TriangleSurfaceInputHash_DeterministicAndSensitive()
        {
            NavTriangleSurfaceFlags walk = NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 400, 0 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 400, 400 },
                triA: new[] { 0, 0 },
                triB: new[] { 1, 2 },
                triC: new[] { 2, 3 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 10, 11 },
                triFlags: new[] { walk, walk });
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 2, 1, haloPaddingCm: 100);
            NavTriangleSurfaceTileIndex index = NavTriangleSurfaceTileIndex.Build(surface, grid);

            string first = NavBakeCanonicalHash.ComputeTriangleSurfaceInputHash(index);
            string second = NavBakeCanonicalHash.ComputeTriangleSurfaceInputHash(
                NavTriangleSurfaceTileIndex.Build(surface, grid));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.Length, Is.EqualTo(16));

            var moved = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 500, 500, 0 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 400, 400 },
                triA: new[] { 0, 0 },
                triB: new[] { 1, 2 },
                triC: new[] { 2, 3 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 10, 11 },
                triFlags: new[] { walk, walk });
            NavTriangleSurfaceTileIndex movedIndex = NavTriangleSurfaceTileIndex.Build(moved, grid);
            Assert.That(
                NavBakeCanonicalHash.ComputeTriangleSurfaceInputHash(movedIndex),
                Is.Not.EqualTo(first),
                "Surface input hash must change when triangle geometry changes.");
        }

        private sealed class HexLikeFlatLogicTerrainField : LogicTerrainField
        {
            private readonly LogicTerrainCell _cell = new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None);

            public HexLikeFlatLogicTerrainField(int widthCells, int heightCells, int chunkSizeCells)
                : base(widthCells, heightCells, chunkSizeCells)
            {
            }

            public override LogicTerrainTopology Topology => LogicTerrainTopology.Hex;

            public override int HorizontalStepCm => HexCoordinates.EdgeLengthCm;

            public override int VerticalStepCm => HexCoordinates.EdgeLengthCm;

            public override LogicTerrainCell GetCell(int col, int row)
                => IsInBounds(col, row) ? _cell : default;

            public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
            {
                xMeters = HexCoordinates.HexWidth * (col + 0.5f * (row & 1));
                zMeters = HexCoordinates.RowSpacing * row;
            }
        }

        private sealed class CountingLogicTerrainField : LogicTerrainField
        {
            private readonly LogicTerrainField _inner;

            public CountingLogicTerrainField(LogicTerrainField inner)
                : base(inner.WidthCells, inner.HeightCells, inner.ChunkSizeCells)
            {
                _inner = inner;
            }

            public int GetCellCalls { get; private set; }

            public int GetWorldPositionCalls { get; private set; }

            public override LogicTerrainTopology Topology => _inner.Topology;

            public override int HorizontalStepCm => _inner.HorizontalStepCm;

            public override int VerticalStepCm => _inner.VerticalStepCm;

            public override bool IsUniformFlatGridSurface => _inner.IsUniformFlatGridSurface;

            public void ResetCounters()
            {
                GetCellCalls = 0;
                GetWorldPositionCalls = 0;
            }

            public override LogicTerrainCell GetCell(int col, int row)
            {
                GetCellCalls++;
                return _inner.GetCell(col, row);
            }

            public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
            {
                GetWorldPositionCalls++;
                _inner.GetWorldPositionMeters(col, row, out xMeters, out zMeters);
            }

            public override bool TryGetCliffStraightenEdge(int col, int row, int edgeIndex, out bool value)
            {
                return _inner.TryGetCliffStraightenEdge(col, row, edgeIndex, out value);
            }
        }
    }
}
