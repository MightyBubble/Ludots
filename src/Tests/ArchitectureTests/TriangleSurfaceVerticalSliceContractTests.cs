using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class TriangleSurfaceVerticalSliceContractTests
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

            // Full RTS 8x8 refs are truncated at the world boundary; the corresponding open-world
            // interior 8x8 keeps full halo on every tile. Matched halo-safe interior refs must match.
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
                "RTS full 8x8 CSR triangle refs must be less than the open-world interior 8x8 " +
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
            var metrics = new HexMetrics(terrain.HorizontalStepCm);
            Assert.That(
                a.Grid.TileWidthCm,
                Is.EqualTo(metrics.HexWidthCm * terrain.ChunkSizeCells));
            Assert.That(
                a.Grid.TileHeightCm,
                Is.EqualTo(metrics.RowSpacingCm * terrain.ChunkSizeCells));
            Assert.That(a.Surface.TriStableIds.ToArray(), Is.EqualTo(b.Surface.TriStableIds.ToArray()));
            for (int i = 0; i < a.Surface.TriangleCount; i++)
            {
                Assert.That(
                    a.Surface.TriFlags[i],
                    Is.EqualTo(NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate));
                }
        }

        [Test]
        public void LogicTerrainCompiler_Hex_ExplicitTileSubdivisionsProducePortalSizedTiles()
        {
            const int chunkSizeCells = 64;
            var terrain = new HexLikeFlatLogicTerrainField(
                widthCells: chunkSizeCells * 2,
                heightCells: chunkSizeCells * 2,
                chunkSizeCells: chunkSizeCells);
            var config = new NavTriangleSurfaceConfig
            {
                HaloPaddingCm = 384,
                TileSubdivisionsX = 2,
                TileSubdivisionsZ = 2
            };

            NavTriangleSurfaceTileGrid grid = LogicTerrainTriangleSurfaceCompiler.DeriveTileGrid(
                terrain,
                config);
            var metrics = new HexMetrics(terrain.HorizontalStepCm);

            Assert.That(grid.TileWidthCm, Is.EqualTo(metrics.HexWidthCm * chunkSizeCells / 2));
            Assert.That(grid.TileHeightCm, Is.EqualTo(metrics.RowSpacingCm * chunkSizeCells / 2));
            Assert.That(grid.TileWidthCm, Is.LessThanOrEqualTo(short.MaxValue));
            Assert.That(grid.TileHeightCm, Is.LessThanOrEqualTo(short.MaxValue));
            Assert.That(grid.TileCountX, Is.EqualTo(4));
            Assert.That(grid.TileCountZ, Is.EqualTo(4));
        }

        [Test]
        public void ProductionAdapters_DeclareTriangleSurfaceOfflineAndRuntimeCapabilities()
        {
            NavBakeAdapterCapabilities expected =
                NavBakeAdapterCapabilities.OfflineTriangleSurface |
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface;

            Assert.That(new RecastNavBakeAlgorithm().Capabilities, Is.EqualTo(expected));
            Assert.That(new CdtNavBakeAlgorithm().Capabilities, Is.EqualTo(expected));
            Assert.That(
                new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(CreateBakeConfig(
                    NavBakeNames.ModeOffline,
                    NavBakeNames.AlgorithmLayeredSpan).LayeredSpan)).Capabilities,
                Is.EqualTo(expected));
            Assert.That(expected.HasFlag(NavBakeAdapterCapabilities.OfflineLogicTerrain), Is.False);
            Assert.That(expected.HasFlag(NavBakeAdapterCapabilities.RuntimeIncrementalLogicTerrain), Is.False);
        }

        [Test]
        public void UnsupportedInput_ThrowsTypedExceptionWithoutFallback()
        {
            var context = new NavBakeContext
            {
                MapId = "nav_triangle_surface_terrain_only",
                SourceUri = "Core:Maps/nav_triangle_surface_terrain_only.vtxm",
                Terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Cdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new NavBakeService(new CdtNavBakeAlgorithm()).Bake(context))!;
            Assert.That(ex.Message, Does.Contain("triangle-surface").Or.Contain("does not support"));
            Assert.That(ex.Message, Does.Not.Contain("fallback"));
        }

        [Test]
        public void LayeredSpan_EmptyTile_IsValidEmptyWithChecksum()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 0, 400 },
                vertexYcm: new[] { 0, 0, 200, 200 },
                vertexZcm: new[] { 0, 0, 400, 400 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { NavTriangleSurfaceFlags.Solid, NavTriangleSurfaceFlags.Solid });
            NavTriangleSurfaceTileIndex index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 1, 1, haloPaddingCm: 200));

            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmLayeredSpan);
            NavBakeResult result = BakeOneResult(index, config, new NavBuildConfig(1f, 0.6f, 1), NavBakeAlgorithmKind.LayeredSpan);
            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
            Assert.That(result.Entries[0].Tile.TriangleCount, Is.EqualTo(0));
            Assert.That(result.Entries[0].Tile.Checksum, Is.Not.EqualTo(0UL));
        }

        [Test]
        public void DirtyTileBake_WithTriangleSurface_PerformsZeroLogicTerrainCellReads()
        {
            var inner = new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4);
            var counting = new CountingLogicTerrainField(inner);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(counting, config, build);
            int compileGets = counting.GetCellCalls;
            int compileWorld = counting.GetWorldPositionCalls;
            Assert.That(compileGets, Is.GreaterThan(0));

            counting.ResetCounters();
            var context = new NavBakeContext
            {
                MapId = "nav_zero_terrain_rescan",
                SourceUri = "Core:Maps/nav_zero_terrain_rescan.vtxm",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) },
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Cdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            NavBakeResult result = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(context);
            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
            Assert.That(counting.GetCellCalls, Is.EqualTo(0));
            Assert.That(counting.GetWorldPositionCalls, Is.EqualTo(0));
            Assert.That(compileWorld, Is.GreaterThan(0));
        }

        [Test]
        public void FlatGridSparseCompile_EightByEightChunks_IsExactlyOneHundredTwentyEightTrianglesAndOChunks()
        {
            const int chunkSize = 4;
            const int chunks = 8;
            var inner = new FlatGridLogicTerrainField(chunks * chunkSize, chunks * chunkSize, chunkSizeCells: chunkSize);
            var counting = new CountingLogicTerrainField(inner);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            var build = new NavBuildConfig(1f, 0.6f, 1);

            NavTriangleSurfaceTileIndex first = LogicTerrainTriangleSurfaceCompiler.Compile(counting, config, build);
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
            NavTriangleSurfaceTileIndex second = LogicTerrainTriangleSurfaceCompiler.Compile(counting, config, build);
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
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            var build = new NavBuildConfig(1f, 0.6f, 1);

            NavTriangleSurfaceTileIndex index = LogicTerrainTriangleSurfaceCompiler.Compile(counting, config, build);
            Assert.That(index.Surface.TriangleCount, Is.EqualTo(8192));
            Assert.That(index.Grid.TileCount, Is.EqualTo(4096));
            Assert.That(counting.GetCellCalls, Is.EqualTo(1));
            Assert.That(
                counting.GetWorldPositionCalls,
                Is.LessThanOrEqualTo(4),
                $"64x64 sparse compile must not enumerate cells; world reads were {counting.GetWorldPositionCalls}.");
            // Per-cell enumeration of a 4096x4096 field would be tens of millions of GetCell calls.
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
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex index = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, build);
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
            var boardConfig = new Ludots.Core.Map.Board.BoardConfig
            {
                WidthInMacroTiles = 2,
                HeightInMacroTiles = 2,
                GridCellSizeCm = SpatialScaleDefaults.CellCm,
                ChunkSizeCells = SpatialScaleDefaults.TerrainChunkCells,
                LoadedChunkCapacity = 64
            };
            var board = new Ludots.Core.Map.Board.GridBoard(
                new Ludots.Core.Map.Board.BoardId("terrain"),
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
        public void AllThreeAlgorithms_FlatTwoTile_ShareReachabilityAndBorderY()
        {
            var terrain = new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, build);
            var targets = new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) };

            NavBakeResult cdtBake = BakeAll(surface, config, build, targets, NavBakeAlgorithmKind.Cdt);
            NavBakeResult recastBake = BakeAll(surface, config, build, targets, NavBakeAlgorithmKind.Recast);
            NavBakeResult spanBake = BakeAll(surface, config, build, targets, NavBakeAlgorithmKind.LayeredSpan);
            NavTile cdt0 = cdtBake.Entries[0].Tile;
            NavTile cdt1 = cdtBake.Entries[1].Tile;
            NavTile recast0 = recastBake.Entries[0].Tile;
            NavTile span0 = spanBake.Entries[0].Tile;
            NavTile span1 = spanBake.Entries[1].Tile;

            Assert.That(cdt0.TriangleCount, Is.GreaterThan(0));
            Assert.That(recast0.TriangleCount, Is.GreaterThan(0));
            Assert.That(span0.TriangleCount, Is.GreaterThan(0));

            Assert.That(TryFirstEastPortalY(cdt0, out int cdtEastY), Is.True);
            Assert.That(TryFirstEastPortalY(span0, out int spanEastY), Is.True);
            Assert.That(spanEastY, Is.EqualTo(cdtEastY));
            Assert.That(HasWestPortalAtY(cdt1, cdtEastY), Is.True);
            Assert.That(HasWestPortalAtY(span1, spanEastY), Is.True);

            NavPathResult recastPath = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                CollectDetourBytes(recastBake),
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 200,
                goalXcm: 750,
                goalZcm: 200,
                maxPortals: 64);
            Assert.That(recastPath.Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(recastPath.PathYcm.Length, Is.EqualTo(recastPath.PathXcm.Length));

            NavPathResult cdtPath = DetourNavQueryEngine.FindPath(
                new[] { cdt0, cdt1 },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: surface.Grid.TileWidthCm,
                tileHeightCm: surface.Grid.TileHeightCm,
                startXcm: 50,
                startZcm: 200,
                goalXcm: 750,
                goalZcm: 200,
                maxPortals: 64);
            Assert.That(cdtPath.Status, Is.EqualTo(NavPathStatus.Ok));
        }

        [Test]
        public void Cdt_FlatEightByEight_NegativeOrigin_CrossTilePathIsReachable()
        {
            // Centered RTS/open-world resident window: 8x8 tiles with negative world origin.
            // Interior tiles expose portals on all four sides; wrong portal clearance mis-labels
            // Detour external-link direction and yields NotReachable across an open flat seam.
            const int chunkSizeCells = 64;
            const int chunks = 8;
            const int cellSizeCm = SpatialScaleDefaults.CellCm;
            const int originXcm = -25600;
            const int originZcm = -25600;
            int tileSizeCm = checked(chunkSizeCells * cellSizeCm);
            var terrain = new FlatGridLogicTerrainField(
                widthCells: chunks * chunkSizeCells,
                heightCells: chunks * chunkSizeCells,
                cellSizeCm: cellSizeCm,
                chunkSizeCells: chunkSizeCells,
                originXcm: originXcm,
                originZcm: originZcm);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, build);
            Assert.That(surface.Grid.OriginXcm, Is.EqualTo(originXcm));
            Assert.That(surface.Grid.OriginZcm, Is.EqualTo(originZcm));
            Assert.That(surface.Grid.TileCountX, Is.EqualTo(chunks));
            Assert.That(surface.Grid.TileCountZ, Is.EqualTo(chunks));
            Assert.That(surface.Grid.TileWidthCm, Is.EqualTo(tileSizeCm));

            // Two adjacent interior tiles that straddle X=0 under the centered origin.
            var targets = new[] { new NavBakeTileCoord(3, 4), new NavBakeTileCoord(4, 4) };
            NavBakeResult bake = BakeAll(surface, config, build, targets, NavBakeAlgorithmKind.Cdt);
            NavTile west = bake.Entries[0].Tile;
            NavTile east = bake.Entries[1].Tile;
            Assert.That(west.OriginXcm, Is.EqualTo(originXcm + (3 * tileSizeCm)));
            Assert.That(east.OriginXcm, Is.EqualTo(originXcm + (4 * tileSizeCm)));
            Assert.That(west.PortalCount, Is.GreaterThan(0), "West tile must emit border portals.");
            Assert.That(east.PortalCount, Is.GreaterThan(0), "East tile must emit border portals.");
            Assert.That(TryFirstEastPortalY(west, out int eastY), Is.True, "West tile must expose an East portal on the shared seam.");
            Assert.That(HasWestPortalAtY(east, eastY), Is.True, "East tile must expose a matching West portal on the shared seam.");

            int startXcm = west.OriginXcm + (tileSizeCm / 4);
            int goalXcm = east.OriginXcm + (tileSizeCm / 4);
            int pathZcm = west.OriginZcm + (tileSizeCm / 2);
            NavPathResult path = DetourNavQueryEngine.FindPath(
                new[] { west, east },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: tileSizeCm,
                tileHeightCm: tileSizeCm,
                startXcm: startXcm,
                startZcm: pathZcm,
                goalXcm: goalXcm,
                goalZcm: pathZcm,
                maxPortals: 64);
            Assert.That(
                path.Status,
                Is.EqualTo(NavPathStatus.Ok),
                $"CDT cross-tile path must be reachable across negative-origin tiles; status={path.Status} " +
                $"start=({startXcm},{pathZcm}) goal=({goalXcm},{pathZcm}) westPortals={west.PortalCount} eastPortals={east.PortalCount}");
            Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Cdt_HexSurface_CrossTilePathIsReachable()
        {
            const int chunkSizeCells = 8;
            var terrain = new HexLikeFlatLogicTerrainField(
                widthCells: chunkSizeCells * 2,
                heightCells: chunkSizeCells,
                chunkSizeCells: chunkSizeCells);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            config.TriangleSurface = new NavTriangleSurfaceConfig
            {
                HaloPaddingCm = 384,
                TileSubdivisionsX = 2,
                TileSubdivisionsZ = 2
            };
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, build);
            var targets = new[]
            {
                new NavBakeTileCoord(1, 0),
                new NavBakeTileCoord(2, 0)
            };
            NavBakeResult bake = BakeAll(surface, config, build, targets, NavBakeAlgorithmKind.Cdt);
            NavTile west = bake.Entries[0].Tile;
            NavTile east = bake.Entries[1].Tile;
            Assert.That(west.PortalCount, Is.GreaterThan(0));
            Assert.That(east.PortalCount, Is.GreaterThan(0));
            Assert.That(TryFirstEastPortalY(west, out int eastY), Is.True);
            Assert.That(HasWestPortalAtY(east, eastY), Is.True);
            Assert.That(CountTriangleConnectedComponents(west), Is.EqualTo(1));
            Assert.That(CountTriangleConnectedComponents(east), Is.EqualTo(1));

            int startXcm = west.OriginXcm + (surface.Grid.TileWidthCm / 4);
            int goalXcm = east.OriginXcm + (surface.Grid.TileWidthCm / 4);
            int pathZcm = west.OriginZcm + (surface.Grid.TileHeightCm / 3);
            NavPathResult path = DetourNavQueryEngine.FindPath(
                new[] { west, east },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: surface.Grid.TileWidthCm,
                tileHeightCm: surface.Grid.TileHeightCm,
                startXcm,
                pathZcm,
                goalXcm,
                pathZcm,
                maxPortals: 64);
            Assert.That(
                path.Status,
                Is.EqualTo(NavPathStatus.Ok),
                $"start=({startXcm},{pathZcm}) goal=({goalXcm},{pathZcm}); " +
                $"west={DescribeTilePortals(west)}; east={DescribeTilePortals(east)}; " +
                $"startCovered={PointInTileTriangles(west, startXcm - west.OriginXcm, pathZcm - west.OriginZcm)}; " +
                $"goalCovered={PointInTileTriangles(east, goalXcm - east.OriginXcm, pathZcm - east.OriginZcm)}.");
        }

        [Test]
        public void LayeredSpan_MixedBaselineAndObstacleTile_NorthMarch_StringPullsAroundBuilding()
        {
            // After placing a building, only the touched tile leaves flat-grid-baseline and becomes
            // dense LayeredSpan. Funnel (FindStraightPath) must still collapse the corridor to a
            // short pulled-string — not a portal-by-portal weave filling open triangles.
            const int chunkSizeCells = 64;
            const int chunks = 8;
            const int cellSizeCm = SpatialScaleDefaults.CellCm;
            const int originXcm = -25600;
            const int originZcm = -25600;
            int tileSizeCm = checked(chunkSizeCells * cellSizeCm);
            var terrain = new FlatGridLogicTerrainField(
                widthCells: chunks * chunkSizeCells,
                heightCells: chunks * chunkSizeCells,
                cellSizeCm: cellSizeCm,
                chunkSizeCells: chunkSizeCells,
                originXcm: originXcm,
                originZcm: originZcm);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmLayeredSpan);
            config.LayeredSpan.ColumnCapacity = 8192;
            config.RuntimeIncremental.OutputVertexCapacity = 16384;
            config.RuntimeIncremental.OutputTriangleCapacity = 32768;
            config.RuntimeIncremental.OutputPortalCapacity = 4096;
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, build);

            var targets = new[]
            {
                new NavBakeTileCoord(3, 3),
                new NavBakeTileCoord(4, 3),
                new NavBakeTileCoord(3, 4),
                new NavBakeTileCoord(4, 4)
            };
            NavBakeResult openBake = BakeAll(surface, config, build, targets, NavBakeAlgorithmKind.LayeredSpan);
            var tiles = new NavTile[openBake.Entries.Count];
            for (int i = 0; i < openBake.Entries.Count; i++)
            {
                Assert.That(openBake.Entries[i].Success, Is.True, openBake.Entries[i].Artifact.Message);
                tiles[i] = openBake.Entries[i].Tile;
            }

            // Building deep inside the NE tile — well clear of the x=100 north march and of tile seams.
            // Mixed baseline/dense linking must still allow a direct pulled-string at x=100.
            const int buildingXcm = 3200;
            const int buildingZcm = 3200;
            const int buildingRadiusCm = 150;
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "building",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(buildingXcm, buildingZcm),
                        RadiusCm = buildingRadiusCm,
                        MinYcm = 0,
                        MaxYcm = 300
                    }
                }
            };

            int obstacleTileIndex = -1;
            for (int i = 0; i < tiles.Length; i++)
            {
                NavTile tile = tiles[i];
                int minX = tile.OriginXcm;
                int minZ = tile.OriginZcm;
                int maxX = checked(minX + tileSizeCm);
                int maxZ = checked(minZ + tileSizeCm);
                if (buildingXcm + buildingRadiusCm < minX ||
                    buildingXcm - buildingRadiusCm > maxX ||
                    buildingZcm + buildingRadiusCm < minZ ||
                    buildingZcm - buildingRadiusCm > maxZ)
                {
                    continue;
                }

                obstacleTileIndex = i;
                NavBakeResult blocked = BakeAllWithObstacles(
                    surface,
                    config,
                    build,
                    new[] { new NavBakeTileCoord(tile.TileId.ChunkX, tile.TileId.ChunkY) },
                    NavBakeAlgorithmKind.LayeredSpan,
                    obstacles);
                Assert.That(blocked.Entries[0].Success, Is.True, blocked.Entries[0].Artifact.Message);
                tiles[i] = blocked.Entries[0].Tile;
                Assert.That(
                    DefaultGridNavTileFactory.MatchesFlatBaselineFootprint(tiles[i], tileSizeCm, tileSizeCm),
                    Is.False,
                    "Obstacle tile must leave flat-grid-baseline and use dense LayeredSpan.");
            }

            Assert.That(obstacleTileIndex, Is.GreaterThanOrEqualTo(0), "Building must dirty at least one of the four tiles.");

            int baselineCount = 0;
            for (int i = 0; i < tiles.Length; i++)
            {
                if (DefaultGridNavTileFactory.MatchesFlatBaselineFootprint(tiles[i], tileSizeCm, tileSizeCm))
                {
                    baselineCount++;
                }
            }

            Assert.That(baselineCount, Is.EqualTo(tiles.Length - 1), "Neighbor tiles must remain flat-grid-baseline.");

            NavTile obstacleTile = tiles[obstacleTileIndex];
            string componentSummary = SummarizeTriangleConnectedComponents(obstacleTile);
            Assert.That(
                CountTriangleConnectedComponents(obstacleTile),
                Is.EqualTo(1),
                "Obstacle LayeredSpan tile must stay a single walkable component outside the building hole. " + componentSummary);
            Assert.That(
                CountInternalNeighborEdges(obstacleTile),
                Is.GreaterThan(obstacleTile.TriangleCount),
                "Dense obstacle tile must retain internal triangle adjacency (not only border portals). " + componentSummary);
            Assert.That(
                PointInTileTriangles(obstacleTile, localXcm: 100, localZcm: 2000),
                Is.True,
                "West corridor sample (100,2000) must stay covered after building bake.");
            Assert.That(
                PointInTileTriangles(obstacleTile, localXcm: 100, localZcm: 5000),
                Is.True,
                "West corridor sample (100,5000) must stay covered after building bake.");
            Assert.That(
                PortalSideCoversAlong(obstacleTile, NavPortalSide.North, alongCm: 100),
                Is.True,
                "Dense tile north border must keep a portal covering x=100 for baseline handoff.");
            Assert.That(
                CountClockwiseTriangles(obstacleTile),
                Is.EqualTo(0),
                "Detour funnel requires consistent CCW triangles; CW faces flip portal left/right and weave.");

            // Reachability + connectivity are the hard gates after the hole-winding fix.
            // Hole-annulus ear-clip can still leave west↔south border fans; FindStraightPath then
            // emits many apexes on that corridor (mesh quality follow-up, not a dead funnel).
            const int startXcm = 100;
            const int startZcm = -2000;
            const int goalXcm = 100;
            const int goalZcm = 3600;
            NavPathResult path = DetourNavQueryEngine.FindPath(
                tiles,
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: tileSizeCm,
                tileHeightCm: tileSizeCm,
                startXcm: startXcm,
                startZcm: startZcm,
                goalXcm: goalXcm,
                goalZcm: goalZcm,
                maxPortals: 256);
            Assert.That(
                path.Status,
                Is.EqualTo(NavPathStatus.Ok),
                $"status={path.Status} points={path.PathXcm.Length} {componentSummary} path={FormatPath(path)}");
            Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(path.PathXcm[0], Is.EqualTo(startXcm));
            Assert.That(path.PathZcm[0], Is.EqualTo(startZcm));
            Assert.That(path.PathXcm[^1], Is.EqualTo(goalXcm));
            Assert.That(path.PathZcm[^1], Is.EqualTo(goalZcm));
            // Must not thread the building disc — that was the east/west component split symptom.
            for (int i = 0; i < path.PathXcm.Length; i++)
            {
                long dx = path.PathXcm[i] - buildingXcm;
                long dz = path.PathZcm[i] - buildingZcm;
                Assert.That(
                    (dx * dx) + (dz * dz),
                    Is.GreaterThan((long)buildingRadiusCm * buildingRadiusCm),
                    $"Waypoint ({path.PathXcm[i]},{path.PathZcm[i]}) must stay outside the building. {FormatPath(path)}");
            }
        }

        [Test]
        public void LayeredSpan_FlatEightByEight_NegativeOrigin_NorthMarch_DoesNotDetourThroughChunkCorners()
        {
            // Obstacle-free LayeredSpan flat floor must emit flat-grid-baseline-v2 NavTiles
            // (same Detour contract as Editor Bridge), so centerline north march stays straight.
            const int chunkSizeCells = 64;
            const int chunks = 8;
            const int cellSizeCm = SpatialScaleDefaults.CellCm;
            const int originXcm = -25600;
            const int originZcm = -25600;
            int tileSizeCm = checked(chunkSizeCells * cellSizeCm);
            var terrain = new FlatGridLogicTerrainField(
                widthCells: chunks * chunkSizeCells,
                heightCells: chunks * chunkSizeCells,
                cellSizeCm: cellSizeCm,
                chunkSizeCells: chunkSizeCells,
                originXcm: originXcm,
                originZcm: originZcm);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmLayeredSpan);
            // 64-cell tiles + halo-2 need (64+4)^2=4624 columns; match RTS showcase budget.
            config.LayeredSpan.ColumnCapacity = 8192;
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, build);

            // Four tiles around the centered z=0 / x=0 seam (world chunk corners at ±6400).
            var targets = new[]
            {
                new NavBakeTileCoord(3, 3),
                new NavBakeTileCoord(4, 3),
                new NavBakeTileCoord(3, 4),
                new NavBakeTileCoord(4, 4)
            };
            NavBakeResult bake = BakeAll(surface, config, build, targets, NavBakeAlgorithmKind.LayeredSpan);
            var tiles = new NavTile[bake.Entries.Count];
            for (int i = 0; i < bake.Entries.Count; i++)
            {
                Assert.That(bake.Entries[i].Success, Is.True, bake.Entries[i].Artifact.Message);
                tiles[i] = bake.Entries[i].Tile;
                Assert.That(
                    DefaultGridNavTileFactory.MatchesFlatBaselineFootprint(tiles[i], tileSizeCm, tileSizeCm),
                    Is.True,
                    $"Flat LayeredSpan tile {tiles[i].TileId} must emit flat-grid-baseline-v2 footprint.");
            }

            // Near the four-tile junction (inset from x=0 poly seam). Editor baseline should
            // string-pull to a near-direct corridor — not hop via (±6400,0)/(0,±6400).
            const int startXcm = 100;
            const int startZcm = -2000;
            const int goalXcm = 100;
            const int goalZcm = 3600;
            NavPathResult path = DetourNavQueryEngine.FindPath(
                tiles,
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: tileSizeCm,
                tileHeightCm: tileSizeCm,
                startXcm: startXcm,
                startZcm: startZcm,
                goalXcm: goalXcm,
                goalZcm: goalZcm,
                maxPortals: 64);
            Assert.That(
                path.Status,
                Is.EqualTo(NavPathStatus.Ok),
                $"LayeredSpan north march must be reachable; status={path.Status} start=({startXcm},{startZcm}) goal=({goalXcm},{goalZcm})");
            Assert.That(path.PathXcm.Length, Is.EqualTo(2), "Flat baseline funnel should be start→goal.");
            Assert.That(path.PathXcm[0], Is.EqualTo(startXcm));
            Assert.That(path.PathZcm[0], Is.EqualTo(startZcm));
            Assert.That(path.PathXcm[1], Is.EqualTo(goalXcm));
            Assert.That(path.PathZcm[1], Is.EqualTo(goalZcm));
        }

        [Test]
        public void Detour_NoPortal_NoCrossTilePath_OnePortal_AllowsPath()
        {
            const int tileSizeCm = 400;
            NavTile leftOpen = DefaultGridNavTileFactory.CreateFlatTile(0, 0, 0, 1, 4, SpatialScaleDefaults.CellCm);
            NavTile rightOpen = DefaultGridNavTileFactory.CreateFlatTile(1, 0, 0, 1, 4, SpatialScaleDefaults.CellCm);

            NavPathResult openPath = DetourNavQueryEngine.FindPath(
                new[] { leftOpen, rightOpen },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: tileSizeCm,
                tileHeightCm: tileSizeCm,
                startXcm: 50,
                startZcm: 150,
                goalXcm: 450,
                goalZcm: 150,
                maxPortals: 64);
            Assert.That(openPath.Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(openPath.PathYcm.Length, Is.EqualTo(openPath.PathXcm.Length));

            NavTile leftClosed = StripPortals(leftOpen);
            NavTile rightClosed = StripPortals(rightOpen);
            NavPathResult closedPath = DetourNavQueryEngine.FindPath(
                new[] { leftClosed, rightClosed },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: tileSizeCm,
                tileHeightCm: tileSizeCm,
                startXcm: 50,
                startZcm: 150,
                goalXcm: 450,
                goalZcm: 150,
                maxPortals: 64);
            Assert.That(closedPath.Status, Is.Not.EqualTo(NavPathStatus.Ok));
        }

        [Test]
        public void Detour_FindPath_NonZeroChunkWithNegativeTileSpaceOrigin_IsQueryable()
        {
            // Open-world seam: tile-space origin at (-204800,-204800), resident chunk (31,32)
            // covers world AABB [-6400,0]-[0,6400]. Detour orig must be the tile-space origin,
            // not the loaded tile's origin, or FindNearestPoly misses the polys.
            const int tileSizeCm = 6400;
            const int gridOriginXcm = -204800;
            const int gridOriginZcm = -204800;
            const int chunkX = 31;
            const int chunkZ = 32;
            int originXcm = checked(gridOriginXcm + (chunkX * tileSizeCm));
            int originZcm = checked(gridOriginZcm + (chunkZ * tileSizeCm));
            Assert.That(originXcm, Is.EqualTo(-6400));
            Assert.That(originZcm, Is.EqualTo(0));

            NavTile factory = DefaultGridNavTileFactory.CreateFlatTile(chunkX, chunkZ, layer: 0, tileVersion: 1, chunkSizeCells: 64, cellSizeCm: 100);
            var tile = new NavTile(
                factory.TileId,
                factory.TileVersion,
                factory.BuildConfigHash,
                factory.Checksum,
                originXcm,
                originZcm,
                factory.VertexXcm,
                factory.VertexYcm,
                factory.VertexZcm,
                factory.TriA,
                factory.TriB,
                factory.TriC,
                factory.N0,
                factory.N1,
                factory.N2,
                factory.TriAreaIds,
                factory.Portals);

            NavPathResult path = DetourNavQueryEngine.FindPath(
                new[] { tile },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: tileSizeCm,
                tileHeightCm: tileSizeCm,
                startXcm: originXcm + 200,
                startZcm: originZcm + 200,
                goalXcm: originXcm + 1200,
                goalZcm: originZcm + 800,
                maxPortals: 64);
            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok), $"status={path.Status} origin=({originXcm},{originZcm}) chunk=({chunkX},{chunkZ})");
            Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void ObstacleHeightFiltering_BlocksOnlyOverlappingWalkCandidates()
        {
            // Two stacked floors at Y=0 and Y=300; obstacle covers only lower volume.
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
            var index = NavTriangleSurfaceTileIndex.Build(
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

            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            var context = new NavBakeContext
            {
                MapId = "nav_obstacle_height",
                SourceUri = "Core:Maps/nav_obstacle_height.vtxm",
                TriangleSurface = index,
                Obstacles = obstacles,
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Cdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
            NavBakeResult result = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(context);
            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
            Assert.That(result.Entries[0].Tile.TriangleCount, Is.GreaterThan(0));
            // Upper floor should survive; lower floor blocked by obstacle volume.
            Assert.That(result.Entries[0].Tile.VertexYcm, Does.Contain(300));
        }

        [Test]
        public void Recast_RejectsSolidOnlyHorizontalDeck_AsTypedUnsupported()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 7 },
                triFlags: new[] { NavTriangleSurfaceFlags.Solid });
            var index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 1, 1, 100));
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            var context = new NavBakeContext
            {
                MapId = "nav_solid_horizontal",
                SourceUri = "Core:Maps/nav_solid_horizontal.vtxm",
                TriangleSurface = index,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            NavBakeUnsupportedInputException ex = Assert.Throws<NavBakeUnsupportedInputException>(
                () => new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context))!;
            Assert.That(ex.Algorithm, Is.EqualTo(NavBakeAlgorithmKind.Recast));
            Assert.That(ex.Message, Does.Contain("Solid-only"));
        }

        private static NavBakeResult BakeAll(
            NavTriangleSurfaceTileIndex surface,
            NavMeshBakeConfig config,
            NavBuildConfig build,
            IReadOnlyList<NavBakeTileCoord> targets,
            NavBakeAlgorithmKind algorithm)
            => BakeAllWithObstacles(surface, config, build, targets, algorithm, new NavObstacleSet());

        private static NavBakeResult BakeAllWithObstacles(
            NavTriangleSurfaceTileIndex surface,
            NavMeshBakeConfig config,
            NavBuildConfig build,
            IReadOnlyList<NavBakeTileCoord> targets,
            NavBakeAlgorithmKind algorithm,
            INavObstacleSource obstacles)
        {
            config.Algorithm = NavBakeNames.FormatAlgorithm(algorithm);
            var context = new NavBakeContext
            {
                MapId = "nav_diff_reachability",
                SourceUri = "Core:Maps/nav_diff_reachability.vtxm",
                TriangleSurface = surface,
                Obstacles = obstacles ?? new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            INavBakeAlgorithm adapter = algorithm switch
            {
                NavBakeAlgorithmKind.Recast => new RecastNavBakeAlgorithm(),
                NavBakeAlgorithmKind.Cdt => new CdtNavBakeAlgorithm(),
                NavBakeAlgorithmKind.LayeredSpan => new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan)),
                _ => throw new InvalidOperationException()
            };
            NavBakeResult result = new NavBakeService(adapter).Bake(context);
            Assert.That(result.FailureCount, Is.EqualTo(0));
            return result;
        }

        private static string FormatPath(NavPathResult path)
        {
            if (path.PathXcm == null || path.PathXcm.Length == 0)
            {
                return "<empty>";
            }

            var parts = new string[path.PathXcm.Length];
            for (int i = 0; i < path.PathXcm.Length; i++)
            {
                parts[i] = $"({path.PathXcm[i]},{path.PathZcm[i]})";
            }

            return string.Join(" -> ", parts);
        }

        private static int CountSharpReverseTurns(NavPathResult path)
        {
            // Consecutive segment directions that oppose (dot < 0) mean the funnel failed to pull.
            int reverses = 0;
            for (int i = 0; i + 2 < path.PathXcm.Length; i++)
            {
                long ax = path.PathXcm[i + 1] - path.PathXcm[i];
                long az = path.PathZcm[i + 1] - path.PathZcm[i];
                long bx = path.PathXcm[i + 2] - path.PathXcm[i + 1];
                long bz = path.PathZcm[i + 2] - path.PathZcm[i + 1];
                if ((ax * ax) + (az * az) == 0 || (bx * bx) + (bz * bz) == 0)
                {
                    continue;
                }

                if ((ax * bx) + (az * bz) < 0)
                {
                    reverses++;
                }
            }

            return reverses;
        }

        private static int CountInternalNeighborEdges(NavTile tile)
        {
            int count = 0;
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                if (tile.N0[i] >= 0) count++;
                if (tile.N1[i] >= 0) count++;
                if (tile.N2[i] >= 0) count++;
            }

            return count;
        }

        private static int CountTriangleConnectedComponents(NavTile tile)
            => SummarizeTriangleConnectedComponents(tile, out _);

        private static string SummarizeTriangleConnectedComponents(NavTile tile)
        {
            SummarizeTriangleConnectedComponents(tile, out string summary);
            return summary;
        }

        private static int SummarizeTriangleConnectedComponents(NavTile tile, out string summary)
        {
            int n = tile.TriangleCount;
            if (n <= 0)
            {
                summary = "components=0";
                return 0;
            }

            var seen = new bool[n];
            int components = 0;
            var stack = new int[n];
            var sizeParts = new List<string>(8);
            for (int seed = 0; seed < n; seed++)
            {
                if (seen[seed])
                {
                    continue;
                }

                components++;
                int top = 0;
                stack[top++] = seed;
                seen[seed] = true;
                int size = 0;
                long sumX = 0;
                long sumZ = 0;
                while (top > 0)
                {
                    int t = stack[--top];
                    size++;
                    int a = tile.TriA[t];
                    int b = tile.TriB[t];
                    int c = tile.TriC[t];
                    sumX += tile.VertexXcm[a] + tile.VertexXcm[b] + tile.VertexXcm[c];
                    sumZ += tile.VertexZcm[a] + tile.VertexZcm[b] + tile.VertexZcm[c];
                    TryPushNeighbor(tile.N0[t], seen, stack, ref top);
                    TryPushNeighbor(tile.N1[t], seen, stack, ref top);
                    TryPushNeighbor(tile.N2[t], seen, stack, ref top);
                }

                int centroidX = (int)(sumX / (size * 3L));
                int centroidZ = (int)(sumZ / (size * 3L));
                sizeParts.Add($"n={size}@({centroidX},{centroidZ})");
            }

            summary = $"components={components} [{string.Join("; ", sizeParts)}] internalEdges={CountInternalNeighborEdges(tile)}";
            return components;
        }

        private static void TryPushNeighbor(int neighbor, bool[] seen, int[] stack, ref int top)
        {
            if (neighbor < 0 || neighbor >= seen.Length || seen[neighbor])
            {
                return;
            }

            seen[neighbor] = true;
            stack[top++] = neighbor;
        }

        private static bool PointInTileTriangles(NavTile tile, int localXcm, int localZcm)
        {
            for (int t = 0; t < tile.TriangleCount; t++)
            {
                int a = tile.TriA[t];
                int b = tile.TriB[t];
                int c = tile.TriC[t];
                if (PointInTriangleInclusive(
                        localXcm,
                        localZcm,
                        tile.VertexXcm[a],
                        tile.VertexZcm[a],
                        tile.VertexXcm[b],
                        tile.VertexZcm[b],
                        tile.VertexXcm[c],
                        tile.VertexZcm[c]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PortalSideCoversAlong(NavTile tile, NavPortalSide side, int alongCm)
        {
            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            for (int i = 0; i < portals.Length; i++)
            {
                NavBorderPortal portal = portals[i];
                if (portal.Side != side)
                {
                    continue;
                }

                int minAlong;
                int maxAlong;
                if (side is NavPortalSide.West or NavPortalSide.East)
                {
                    minAlong = Math.Min(portal.LeftZcm, portal.RightZcm);
                    maxAlong = Math.Max(portal.LeftZcm, portal.RightZcm);
                }
                else
                {
                    minAlong = Math.Min(portal.LeftXcm, portal.RightXcm);
                    maxAlong = Math.Max(portal.LeftXcm, portal.RightXcm);
                }

                if (alongCm >= minAlong && alongCm <= maxAlong)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PointInTriangleInclusive(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            long area = ((long)bx - ax) * ((long)cz - az) - ((long)bz - az) * ((long)cx - ax);
            if (area == 0)
            {
                return false;
            }

            long sign = area > 0 ? 1 : -1;
            long ab = (((long)bx - ax) * ((long)pz - az) - ((long)bz - az) * ((long)px - ax)) * sign;
            long bc = (((long)cx - bx) * ((long)pz - bz) - ((long)cz - bz) * ((long)px - bx)) * sign;
            long ca = (((long)ax - cx) * ((long)pz - cz) - ((long)az - cz) * ((long)px - cx)) * sign;
            return ab >= 0 && bc >= 0 && ca >= 0;
        }

        private static int CountClockwiseTriangles(NavTile tile)
        {
            // Z-up / XZ floor: positive cross (bx-ax)*(cz-az)-(bz-az)*(cx-ax) is CCW in the XZ plane
            // used by LayeredSpanContourBuilder ("mathematical-CCW left for Z-down grids" is the
            // contour convention; baked NavTile tris must stay CCW for Detour portals).
            int clockwise = 0;
            for (int t = 0; t < tile.TriangleCount; t++)
            {
                int a = tile.TriA[t];
                int b = tile.TriB[t];
                int c = tile.TriC[t];
                long area2 =
                    ((long)tile.VertexXcm[b] - tile.VertexXcm[a]) * ((long)tile.VertexZcm[c] - tile.VertexZcm[a])
                    - ((long)tile.VertexZcm[b] - tile.VertexZcm[a]) * ((long)tile.VertexXcm[c] - tile.VertexXcm[a]);
                if (area2 < 0)
                {
                    clockwise++;
                }
            }

            return clockwise;
        }

        private static List<byte[]> CollectDetourBytes(NavBakeResult bake)
        {
            var bytes = new List<byte[]>(bake.Entries.Count);
            for (int i = 0; i < bake.Entries.Count; i++)
            {
                NavBakeResultEntry entry = bake.Entries[i];
                Assert.That(entry.Success, Is.True, entry.Artifact.Message);
                Assert.That(entry.DetourTileBytes.Length, Is.GreaterThan(0));
                bytes.Add(entry.DetourTileBytes);
            }

            return bytes;
        }

        private static bool TryFirstEastPortalY(NavTile tile, out int ycm)
        {
            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i].Side == NavPortalSide.East)
                {
                    ycm = portals[i].LeftYcm;
                    return true;
                }
            }

            ycm = 0;
            return false;
        }

        private static NavTile BakeOne(
            NavTriangleSurfaceTileIndex surface,
            NavMeshBakeConfig config,
            NavBuildConfig build,
            IReadOnlyList<NavBakeTileCoord> targets,
            NavBakeAlgorithmKind algorithm,
            int targetIndex)
        {
            config.Algorithm = NavBakeNames.FormatAlgorithm(algorithm);
            var context = new NavBakeContext
            {
                MapId = "nav_diff_reachability",
                SourceUri = "Core:Maps/nav_diff_reachability.vtxm",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            INavBakeAlgorithm adapter = algorithm switch
            {
                NavBakeAlgorithmKind.Recast => new RecastNavBakeAlgorithm(),
                NavBakeAlgorithmKind.Cdt => new CdtNavBakeAlgorithm(),
                NavBakeAlgorithmKind.LayeredSpan => new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan)),
                _ => throw new InvalidOperationException()
            };
            NavBakeResult result = new NavBakeService(adapter).Bake(context);
            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[targetIndex].Artifact.Message);
            return result.Entries[targetIndex].Tile;
        }

        private static bool HasWestPortalAtY(NavTile tile, int ycm)
        {
            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            for (int i = 0; i < portals.Length; i++)
            {
                NavBorderPortal p = portals[i];
                if (p.Side == NavPortalSide.West &&
                    Math.Abs(p.LeftYcm - ycm) <= 2 &&
                    Math.Abs(p.RightYcm - ycm) <= 2 &&
                    Math.Abs(p.LeftZcm - p.RightZcm) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string DescribeTilePortals(NavTile tile)
        {
            var values = new List<string>(tile.PortalCount);
            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            for (int i = 0; i < portals.Length; i++)
            {
                NavBorderPortal portal = portals[i];
                values.Add(
                    $"{portal.Side}({portal.LeftXcm},{portal.LeftZcm})-({portal.RightXcm},{portal.RightZcm})@{portal.LeftYcm}/{portal.RightYcm}");
            }

            return $"id=({tile.TileId.ChunkX},{tile.TileId.ChunkY}) tris={tile.TriangleCount} portals=[{string.Join(',', values)}]";
        }

        private static NavTile StripPortals(NavTile tile)
        {
            var stripped = new NavTile(
                tile.TileId,
                tile.TileVersion,
                tile.BuildConfigHash,
                checksum: 0,
                tile.OriginXcm,
                tile.OriginZcm,
                tile.VertexXcm,
                tile.VertexYcm,
                tile.VertexZcm,
                tile.TriA,
                tile.TriB,
                tile.TriC,
                tile.N0,
                tile.N1,
                tile.N2,
                tile.TriAreaIds,
                Array.Empty<NavBorderPortal>());
            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, stripped);
            ms.Position = 0;
            return NavTileBinary.Read(ms);
        }

        private static NavBakeResult BakeOneResult(
            NavTriangleSurfaceTileIndex surface,
            NavMeshBakeConfig config,
            NavBuildConfig build,
            NavBakeAlgorithmKind algorithm)
        {
            config.Algorithm = NavBakeNames.FormatAlgorithm(algorithm);
            var context = new NavBakeContext
            {
                MapId = "nav_triangle_surface_slice",
                SourceUri = "Core:Maps/nav_triangle_surface_slice.vtxm",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            INavBakeAlgorithm adapter = algorithm switch
            {
                NavBakeAlgorithmKind.Recast => new RecastNavBakeAlgorithm(),
                NavBakeAlgorithmKind.Cdt => new CdtNavBakeAlgorithm(),
                NavBakeAlgorithmKind.LayeredSpan => new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan)),
                _ => throw new InvalidOperationException()
            };
            return new NavBakeService(adapter).Bake(context);
        }

        private static NavMeshBakeConfig CreateBakeConfig(string mode, string algorithm)
        {
            return new NavMeshBakeConfig
            {
                Mode = mode,
                Algorithm = algorithm,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = GroundLayerId, Layer = 0 }
                },
                Areas = new List<NavAreaCostConfig>(),
                RuntimeIncremental = new NavRuntimeIncrementalConfig
                {
                    TileBudgetPerFixedTick = 1,
                    IncludeNeighborTiles = true,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1,
                    TrackedStructuralEntityCapacity = 32,
                    ObstaclePrimitiveCapacity = 64,
                    PolygonVertexCapacity = 512,
                    DirtyTileCapacity = 64,
                    StagedEntryCapacity = 64,
                    PublishedTileCapacity = 64,
                    StoreGroupCapacity = 8,
                    ResidentTileCapacity = 128,
                    OutputVertexCapacity = 256,
                    OutputTriangleCapacity = 512,
                    OutputPortalCapacity = 64,
                    InitialResidentChunkX = 0,
                    InitialResidentChunkZ = 0,
                    InitialResidentWidthChunks = 1,
                    InitialResidentHeightChunks = 1
                },
                LayeredSpan = new NavLayeredSpanConfig
                {
                    ScratchSlotCount = 2,
                    RasterCellSizeCm = 100,
                    // Halo depth 2: outer clearance seeds sit at the rim; depth-1 alone
                    // leaves border neighbors as outer seeds (clearance 0) and drops portals.
                    RasterHaloCells = 2,
                    SameSurfaceToleranceCm = 5,
                    MaxSimplificationErrorCm = 0,
                    HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                    MaxLawsonFlipCount = 100_000,
                    ColumnCapacity = 4096,
                    SpanCapacity = 16384,
                    ClassifiedSpanCapacity = 16384,
                    WalkableSpanCapacity = 16384,
                    LinkCapacity = 65536,
                    SheetCapacity = 16384,
                    PortalIntervalCapacity = 65536,
                    RegionCapacity = 4096,
                    ChartCapacity = 1024,
                    RingCapacity = 2048,
                    ContourVertexCapacity = 16384,
                    ContourEdgeCapacity = 16384,
                    SeamCapacity = 4096,
                    CanonicalLinkCapacity = 65536,
                    SplitPointCapacity = 4096,
                    TriangulationVertexCapacity = 16384,
                    TriangulationTriangleCapacity = 32768,
                    ConstrainedEdgeCapacity = 32768,
                    BorderPortalCapacity = 4096,
                    PolygonVertexCapacity = 16384,
                    AdjacencyEdgeCapacity = 98304,
                    BridgeCandidateCapacity = 16384,
                    RingWorkCapacity = 2048,
                    TemporaryConstraintFlagCapacity = 32768
                },
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 200 },
                Recast = new NavRecastConfig { RasterCellSizeCm = 10, RasterCellHeightCm = 5 }
            };
        }

        private static AgentProfileRegistry CreateAgentProfiles()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "Small",
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
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
                => _inner.TryGetCliffStraightenEdge(col, row, edgeIndex, out value);
        }
    }
}
