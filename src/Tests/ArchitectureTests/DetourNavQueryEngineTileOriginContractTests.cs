using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class DetourNavQueryEngineTileOriginContractTests
    {
        private const string GroundLayerId = "Ground";

        [Test]
        public void FindPath_NonZeroChunksNegativeGridOrigin_ResolvesTilesAcrossTileSpace()
        {
            // Grid origin (-6400,-6400) with 6400cm tiles: absolute chunk ids 3/4 do not equal
            // zero-based tile indices. The query engine must derive the tile-space origin from
            // (Origin - Chunk*extent) so absolute chunk ids resolve to the same relative slot.
            const int chunkSizeCells = 64;
            const int cellSizeCm = SpatialScaleDefaults.CellCm;
            const int gridOriginXcm = -6400;
            const int gridOriginZcm = -6400;
            int tileSizeCm = checked(chunkSizeCells * cellSizeCm);
            var terrain = new FlatGridLogicTerrainField(
                widthCells: 6 * chunkSizeCells,
                heightCells: 6 * chunkSizeCells,
                cellSizeCm: cellSizeCm,
                chunkSizeCells: chunkSizeCells,
                originXcm: gridOriginXcm,
                originZcm: gridOriginZcm);
            NavMeshBakeConfig config = CreateBakeConfig();
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(
                terrain,
                config,
                new NavBuildConfig(1f, 0.6f, 1));

            NavBakeResult bake = BakeAll(
                surface,
                config,
                new[]
                {
                    new NavBakeTileCoord(3, 0),
                    new NavBakeTileCoord(4, 0),
                    new NavBakeTileCoord(3, 1),
                    new NavBakeTileCoord(4, 1)
                });
            var tiles = new NavTile[bake.Entries.Count];
            for (int i = 0; i < bake.Entries.Count; i++)
            {
                Assert.That(bake.Entries[i].Success, Is.True, bake.Entries[i].Artifact.Message);
                tiles[i] = bake.Entries[i].Tile;
                Assert.That(
                    tiles[i].OriginXcm,
                    Is.EqualTo(checked(gridOriginXcm + tiles[i].TileId.ChunkX * tileSizeCm)));
                Assert.That(
                    tiles[i].OriginZcm,
                    Is.EqualTo(checked(gridOriginZcm + tiles[i].TileId.ChunkY * tileSizeCm)));
            }

            // Start inside chunk (3,0); goal inside chunk (4,1) — a diagonal cross-tile march.
            int startXcm = checked(gridOriginXcm + (3 * tileSizeCm) + 100);
            int startZcm = checked(gridOriginZcm + 100);
            int goalXcm = checked(gridOriginXcm + (4 * tileSizeCm) + (tileSizeCm - 100));
            int goalZcm = checked(gridOriginZcm + tileSizeCm + (tileSizeCm - 100));

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
                $"status={path.Status} points={path.PathXcm.Length} path={FormatPath(path)}");
            Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(path.PathXcm[0], Is.EqualTo(startXcm));
            Assert.That(path.PathZcm[0], Is.EqualTo(startZcm));
            Assert.That(path.PathXcm[^1], Is.EqualTo(goalXcm));
            Assert.That(path.PathZcm[^1], Is.EqualTo(goalZcm));
        }

        [Test]
        public void FindPath_MixedDerivedGridOrigins_HardFailsInsteadOfSilentMinOrigin()
        {
            // Two tiles at the same absolute chunk but with different world origins derive
            // different tile-space origins. Loading both must hard-fail: a silent min-origin
            // collapse would misindex the higher chunk and return NotReady paths.
            const int chunkSizeCells = 64;
            const int cellSizeCm = SpatialScaleDefaults.CellCm;
            int tileSizeCm = checked(chunkSizeCells * cellSizeCm);

            NavTile tileA = DefaultGridNavTileFactory.CreateFlatTile(
                chunkX: 3,
                chunkY: 0,
                layer: 0,
                tileVersion: 1,
                chunkSizeCells: chunkSizeCells,
                cellSizeCm: cellSizeCm,
                areaId: 1);
            Assert.That(tileA.OriginXcm, Is.EqualTo(3 * tileSizeCm)); // derived origin 0

            var terrainB = new FlatGridLogicTerrainField(
                widthCells: 6 * chunkSizeCells,
                heightCells: 6 * chunkSizeCells,
                cellSizeCm: cellSizeCm,
                chunkSizeCells: chunkSizeCells,
                originXcm: -6400,
                originZcm: 0);
            NavMeshBakeConfig config = CreateBakeConfig();
            NavTriangleSurfaceTileIndex surfaceB = LogicTerrainTriangleSurfaceCompiler.Compile(
                terrainB,
                config,
                new NavBuildConfig(1f, 0.6f, 1));
            NavBakeResult bakeB = BakeAll(surfaceB, config, new[] { new NavBakeTileCoord(3, 0) });
            Assert.That(bakeB.Entries[0].Success, Is.True, bakeB.Entries[0].Artifact.Message);
            NavTile tileB = bakeB.Entries[0].Tile;
            Assert.That(tileB.OriginXcm, Is.EqualTo(checked(-6400 + 3 * tileSizeCm))); // derived origin -6400

            var mixed = new[] { tileA, tileB };
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => DetourNavQueryEngine.FindPath(
                    mixed,
                    layer: 0,
                    areaCosts: NavAreaCostTable.CreateDefault(),
                    tileWidthCm: tileSizeCm,
                    tileHeightCm: tileSizeCm,
                    startXcm: 100,
                    startZcm: 100,
                    goalXcm: tileSizeCm - 100,
                    goalZcm: 100,
                    maxPortals: 64))!;
            Assert.That(ex.Message, Does.Contain("single tile-space origin"));
            Assert.That(ex.Message, Does.Contain("Tile 3,0,0"));
        }

        private static NavBakeResult BakeAll(
            NavTriangleSurfaceTileIndex surface,
            NavMeshBakeConfig config,
            IReadOnlyList<NavBakeTileCoord> targets)
        {
            var context = new NavBakeContext
            {
                MapId = "tile_origin_regression",
                SourceUri = "Core:Maps/tile_origin_regression.tris",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.LayeredSpan,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            var adapter = new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan));
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

        private static NavMeshBakeConfig CreateBakeConfig()
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeOffline,
                Algorithm = NavBakeNames.AlgorithmLayeredSpan,
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
                    RasterHaloCells = 1,
                    SameSurfaceToleranceCm = 5,
                    MaxSimplificationErrorCm = 0,
                    HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                    MaxLawsonFlipCount = 100_000,
                    ColumnCapacity = 8192,
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
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 100 },
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
    }
}
