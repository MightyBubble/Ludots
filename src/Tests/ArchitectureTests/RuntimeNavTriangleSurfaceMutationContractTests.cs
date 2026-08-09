using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class RuntimeNavTriangleSurfaceMutationContractTests
    {
        [Test]
        public void RuntimeNavTriangleSurfaceService_Publish_BumpsGenerationAndRejectsNull()
        {
            NavTriangleSurfaceTileIndex first = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            var service = new RuntimeNavTriangleSurfaceService(first);
            Assert.That(service.ContentGeneration, Is.EqualTo(1UL));
            Assert.That(service.Published.Surface.TriangleCount, Is.EqualTo(8));

            NavTriangleSurfaceTileIndex second = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            service.Publish(second);
            Assert.That(service.ContentGeneration, Is.EqualTo(2UL));
            Assert.That(service.Published, Is.SameAs(second));

            Assert.Throws<ArgumentNullException>(() => service.Publish(null!));
        }

        [Test]
        public void TerrainBrush_Block_RemovesWalkableCellsAndDirtyAabbCoversAffectedTilesOnly()
        {
            NavTriangleSurfaceTileIndex source = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            Assert.That(source.Surface.TriangleCount, Is.EqualTo(8));

            var spec = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 250,
                centerZcm: 250,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Block,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 2,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            NavTriangleSurfaceTileIndex blocked = NavTriangleSurfaceTerrainBrush.Apply(source, spec, out WorldAabbCm dirty);
            Assert.That(blocked.Surface.TriangleCount, Is.GreaterThan(0));
            Assert.That(blocked.Surface.TriangleCount, Is.Not.EqualTo(source.Surface.TriangleCount));
            Assert.That(dirty.Left, Is.EqualTo(200));
            Assert.That(dirty.Top, Is.EqualTo(200));
            Assert.That(dirty.Right, Is.EqualTo(300));
            Assert.That(dirty.Bottom, Is.EqualTo(300));

            WorldAabbCm restoreDirty = NavTriangleSurfaceTerrainBrush.ComputeChangedTileAabb(blocked, source);
            Assert.That(restoreDirty.Left, Is.EqualTo(0));
            Assert.That(restoreDirty.Top, Is.EqualTo(0));
            Assert.That(restoreDirty.Right, Is.EqualTo(800));
            Assert.That(restoreDirty.Bottom, Is.EqualTo(800));
        }

        [Test]
        public void TerrainBrush_Raise_ChangesVertexHeightInsideBrush()
        {
            NavTriangleSurfaceTileIndex source = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            var spec = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 150,
                centerZcm: 150,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Raise,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 3,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            NavTriangleSurfaceTileIndex raised = NavTriangleSurfaceTerrainBrush.Apply(source, spec, out _);
            bool sawRaised = false;
            ReadOnlySpan<int> vy = raised.Surface.VertexYcm;
            for (int i = 0; i < vy.Length; i++)
            {
                if (vy[i] == 300)
                {
                    sawRaised = true;
                    break;
                }
            }

            Assert.That(sawRaised, Is.True, "Raised brush must emit vertices at height level 3 (300cm).");
        }

        [Test]
        public void TerrainBrush_BlockThenRepublishBeforeImage_RestoresTriangleCount()
        {
            NavTriangleSurfaceTileIndex source = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            var spec = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 250,
                centerZcm: 250,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Block,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 2,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            NavTriangleSurfaceTileIndex blocked = NavTriangleSurfaceTerrainBrush.Apply(source, spec, out _);
            var service = new RuntimeNavTriangleSurfaceService(source);
            service.Publish(blocked);
            Assert.That(service.ContentGeneration, Is.EqualTo(2UL));
            service.Publish(source);
            Assert.That(service.Published.Surface.TriangleCount, Is.EqualTo(source.Surface.TriangleCount));
            Assert.That(service.ContentGeneration, Is.EqualTo(3UL));
        }

        [Test]
        public void TerrainBrush_GroundBandEdit_PreservesStackedBridgeTrianglesExactly()
        {
            NavTriangleSurfaceTileIndex source = CreateGroundWithBridge();
            var spec = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 200,
                centerZcm: 200,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Block,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 2,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            NavTriangleSurfaceTileIndex edited = NavTriangleSurfaceTerrainBrush.Apply(source, spec, out WorldAabbCm dirty);

            Assert.That(dirty, Is.EqualTo(new WorldAabbCm(100, 100, 200, 200)));
            AssertTriangleCoordinatesEqualByStableId(source.Surface, edited.Surface, stableId: 20);
            AssertTriangleCoordinatesEqualByStableId(source.Surface, edited.Surface, stableId: 21);
        }

        [Test]
        public void EditTransaction_StageBrushCommitRestore_PublishesAndCapturesBeforeImage()
        {
            const string layerId = "Ground";
            var terrain = new FlatGridLogicTerrainField(8, 8, chunkSizeCells: 4);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex source = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            int sourceTriangleCount = source.Surface.TriangleCount;

            var config = CreateRuntimeIncrementalBakeConfig(layerId);
            var agentProfiles = CreateAgentProfiles();
            var surfaceContext = new NavBakeContext
            {
                MapId = "nav_edit_transaction_contract",
                SourceUri = "Core:Maps/nav_edit_transaction_contract.tris",
                TriangleSurface = source,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = agentProfiles,
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = NavBakeAlgorithmKind.ExactCdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
            var store = new NavTileStore(
                _ => throw new InvalidOperationException("Runtime incremental test publishes tiles before disk load."),
                config.RuntimeIncremental);
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey(0, 0)] = store
                },
                NavQueryTileSpace.FromGrid(source.Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                surfaceContext,
                registry,
                new NavMeshProfileRegistry(config, agentProfiles));

            // Establish committed residency for tile (0,0) before any terrain edit.
            queue.RequestResidentWindowTransition(new[] { new NavBakeTileCoord(0, 0) });
            DrainQueue(queue);
            Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(1));
            Assert.That(queue.Status, Is.EqualTo(RuntimeNavMeshRebuildStatus.Idle));

            var service = new RuntimeNavTriangleSurfaceService(source);
            NavTriangleSurfaceTileIndex? published = null;
            var transaction = new RuntimeNavTriangleSurfaceEditTransaction(
                service,
                queue,
                surface => published = surface,
                includeNeighborTiles: false);

            var spec = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 250,
                centerZcm: 250,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Block,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 2,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            transaction.StageBrush(in spec);
            Assert.That(transaction.HasStaged, Is.True);
            Assert.That(transaction.StagedBefore.Surface.TriangleCount, Is.EqualTo(sourceTriangleCount));
            NavTriangleSurfaceTileIndex afterImage = transaction.StagedAfter;
            Assert.That(afterImage.Surface.TriangleCount, Is.Not.EqualTo(sourceTriangleCount));
            Assert.That(service.Published, Is.SameAs(source), "Staging must not publish.");

            transaction.Commit();
            Assert.That(service.ContentGeneration, Is.EqualTo(2UL));
            Assert.That(service.Published, Is.SameAs(afterImage));
            Assert.That(published, Is.SameAs(afterImage), "Owned service key must be republished on commit.");
            Assert.That(queue.CurrentTriangleSurface, Is.SameAs(afterImage), "Queue surface SSOT must be replaced on commit.");
            Assert.That(queue.PendingTileCount, Is.GreaterThan(0), "Commit must enqueue dirty tiles.");
            Assert.That(transaction.HasStaged, Is.False);
            Assert.That(transaction.HasRestorableBeforeImage, Is.True);

            DrainQueue(queue);
            transaction.StageExactRestore();
            transaction.Commit();
            Assert.That(service.Published.Surface.TriangleCount, Is.EqualTo(sourceTriangleCount), "Exact restore must republish the captured before-image.");
            Assert.That(service.ContentGeneration, Is.EqualTo(3UL));
            Assert.That(queue.CurrentTriangleSurface, Is.SameAs(service.Published));
        }

        private static void DrainQueue(RuntimeIncrementalNavMeshRebuildQueue queue)
        {
            var published = new RuntimeNavMeshRebuildPublishedTile[256];
            var failures = new NavBakeResultEntry[256];
            while (queue.PendingTileCount > 0 || queue.SealedRemainingCount > 0)
            {
                _ = queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }
        }

        private static NavMeshBakeConfig CreateRuntimeIncrementalBakeConfig(string layerId)
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeRuntimeIncremental,
                Algorithm = NavBakeNames.AlgorithmExactCdt,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = layerId, Layer = 0 }
                },
                Areas = new List<NavAreaCostConfig>(),
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 0 },
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
                    ResidentTileCapacity = 64,
                    OutputVertexCapacity = 256,
                    OutputTriangleCapacity = 512,
                    OutputPortalCapacity = 64,
                    InitialResidentChunkX = 0,
                    InitialResidentChunkZ = 0,
                    InitialResidentWidthChunks = 1,
                    InitialResidentHeightChunks = 1
                }
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

        private static NavTriangleSurfaceTileIndex CompileFlat(int widthCells, int heightCells, int chunkSize, int halo)
        {
            var terrain = new FlatGridLogicTerrainField(widthCells, heightCells, cellSizeCm: 100, chunkSizeCells: chunkSize);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            return LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, halo);
        }

        private static NavTriangleSurfaceTileIndex CreateGroundWithBridge()
        {
            NavTriangleSurfaceFlags walk = NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;
            var snapshot = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 400, 0, 100, 300, 300, 100 },
                vertexYcm: new[] { 0, 0, 0, 0, 500, 500, 500, 500 },
                vertexZcm: new[] { 0, 0, 400, 400, 100, 100, 300, 300 },
                triA: new[] { 0, 0, 4, 4 },
                triB: new[] { 1, 2, 5, 6 },
                triC: new[] { 2, 3, 6, 7 },
                triAreaIds: new byte[] { 0, 0, 0, 0 },
                triStableIds: new[] { 10, 11, 20, 21 },
                triFlags: new[] { walk, walk, walk, walk });
            return NavTriangleSurfaceTileIndex.Build(
                snapshot,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 1, 1, haloPaddingCm: 0));
        }

        private static void AssertTriangleCoordinatesEqualByStableId(
            NavTriangleSurfaceSnapshot expected,
            NavTriangleSurfaceSnapshot actual,
            int stableId)
        {
            int expectedIndex = FindTriangleByStableId(expected, stableId);
            int actualIndex = FindTriangleByStableId(actual, stableId);
            Assert.That(actualIndex, Is.GreaterThanOrEqualTo(0), $"Triangle stable id {stableId} must survive the ground edit.");
            AssertVertexEqual(expected, expected.TriA[expectedIndex], actual, actual.TriA[actualIndex]);
            AssertVertexEqual(expected, expected.TriB[expectedIndex], actual, actual.TriB[actualIndex]);
            AssertVertexEqual(expected, expected.TriC[expectedIndex], actual, actual.TriC[actualIndex]);
        }

        private static int FindTriangleByStableId(NavTriangleSurfaceSnapshot surface, int stableId)
        {
            for (int i = 0; i < surface.TriangleCount; i++)
            {
                if (surface.TriStableIds[i] == stableId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AssertVertexEqual(
            NavTriangleSurfaceSnapshot expected,
            int expectedVertex,
            NavTriangleSurfaceSnapshot actual,
            int actualVertex)
        {
            Assert.That(actual.VertexXcm[actualVertex], Is.EqualTo(expected.VertexXcm[expectedVertex]));
            Assert.That(actual.VertexYcm[actualVertex], Is.EqualTo(expected.VertexYcm[expectedVertex]));
            Assert.That(actual.VertexZcm[actualVertex], Is.EqualTo(expected.VertexZcm[expectedVertex]));
        }
    }
}
