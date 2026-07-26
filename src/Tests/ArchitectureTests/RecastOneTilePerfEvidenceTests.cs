using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Evidence harness for Recast one-tile 64m wall time. Reports counts/checksum; no absolute timing assert.
    /// </summary>
    [TestFixture]
    public sealed class RecastOneTilePerfEvidenceTests
    {
        private const int TileWidthCm = 6400;
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        [Test]
        public void Recast_OneTile64m_LightAgent_ReportsBakeEvidence()
        {
            // BEFORE fix: radius/3 => ~6.67cm cells (~960 columns). AFTER: explicit 20cm (~320 columns).
            float radiusM = 0.20f;
            float derivedCellSizeM = MathF.Max(0.05f, MathF.Min(0.5f, radiusM / 3f));
            int derivedColumns = (int)MathF.Ceiling(TileWidthCm / (derivedCellSizeM * 100f));

            NavTriangleSurfaceTileIndex surface = CreateFlatFloor(TileWidthCm);
            NavMeshBakeConfig config = CreateBakeConfig();
            var context = new NavBakeContext
            {
                MapId = "recast_one_tile_64m_perf",
                SourceUri = "Core:Maps/recast_one_tile_64m_perf.vtxm",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateLightAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            var sw = Stopwatch.StartNew();
            NavBakeResult bake = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);
            sw.Stop();

            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);
            NavTile tile = bake.Entries[0].Tile;
            Assert.That(tile.TriangleCount, Is.GreaterThan(0));

            int configuredCellCm = config.Recast.RasterCellSizeCm;
            int configuredColumns = (int)MathF.Ceiling(TileWidthCm / (float)configuredCellCm);

            TestContext.WriteLine(
                $"Recast one-tile 64m AFTER evidence: wallMs={sw.ElapsedMilliseconds} " +
                $"tris={tile.TriangleCount} verts={tile.VertexCount} portals={tile.PortalCount} " +
                $"checksum={tile.Checksum:X16} buildHash={tile.BuildConfigHash:X16} " +
                $"priorDerivedCellSizeM={derivedCellSizeM:R} priorDerivedColumns={derivedColumns} " +
                $"configuredCellCm={configuredCellCm} configuredColumns={configuredColumns}");

            Assert.That(configuredCellCm, Is.EqualTo(20));
            Assert.That(configuredColumns, Is.EqualTo(320));
            Assert.That(configuredColumns, Is.LessThan(derivedColumns));
        }

        private static NavTriangleSurfaceTileIndex CreateFlatFloor(int tileWidthCm)
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, tileWidthCm, tileWidthCm, 0 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, tileWidthCm, tileWidthCm },
                triA: new[] { 0, 0 },
                triB: new[] { 1, 2 },
                triC: new[] { 2, 3 },
                triAreaIds: new byte[] { 0, 0 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });
            return NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, tileWidthCm, tileWidthCm, 1, 1, haloPaddingCm: 200));
        }

        private static NavMeshBakeConfig CreateBakeConfig()
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeOffline,
                Algorithm = NavBakeNames.AlgorithmRecast,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "light", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = "Ground", Layer = 0 }
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
                    RasterHaloCells = 2,
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
                },
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 200 },
                Recast = new NavRecastConfig { RasterCellSizeCm = 20, RasterCellHeightCm = 10 }
            };
        }

        private static AgentProfileRegistry CreateLightAgentProfiles()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "light",
                    RadiusCm = 20,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
        }
    }
}
