using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class RuntimeNavObstacleSnapshotContractTests
    {
        private const string GroundLayerId = "Ground";

        [Test]
        public void RuntimeNavObstacleSnapshot_CircleAndPolygonAccessPreserveCapturedGeometry()
        {
            var snapshot = new RuntimeNavObstacleSnapshot(
                obstaclePrimitiveCapacity: 8,
                polygonVertexCapacity: 32,
                GroundLayerId);

            snapshot.BeginCapture();
            int circle = snapshot.BeginPrimitive(entityId: 7, pieceIndex: 0, NavObstacleKind.Circle, minYcm: 0, maxYcm: 200);
            snapshot.SetCircle(circle, centerXcm: 100, centerZcm: 200, radiusCm: 35);

            int polygon = snapshot.BeginPrimitive(entityId: 7, pieceIndex: 1, NavObstacleKind.Polygon, minYcm: 10, maxYcm: 50);
            int offset = snapshot.BeginPolygonVertices(polygon, vertexCount: 3);
            snapshot.SetPolygonVertex(offset + 0, 0, 0);
            snapshot.SetPolygonVertex(offset + 1, 40, 0);
            snapshot.SetPolygonVertex(offset + 2, 20, 30);
            snapshot.EndCaptureAndSort();

            Assert.That(snapshot.ObstacleCount, Is.EqualTo(2));
            Assert.That(snapshot.GetKind(0), Is.EqualTo(NavObstacleKind.Circle));
            snapshot.GetCircle(0, out int cx, out int cz, out int radius);
            Assert.That(cx, Is.EqualTo(100));
            Assert.That(cz, Is.EqualTo(200));
            Assert.That(radius, Is.EqualTo(35));

            Assert.That(snapshot.GetKind(1), Is.EqualTo(NavObstacleKind.Polygon));
            Assert.That(snapshot.GetPolygonVertexCount(1), Is.EqualTo(3));
            snapshot.GetPolygonVertex(1, 0, out int x0, out int z0);
            snapshot.GetPolygonVertex(1, 1, out int x1, out int z1);
            snapshot.GetPolygonVertex(1, 2, out int x2, out int z2);
            Assert.That((x0, z0), Is.EqualTo((0, 0)));
            Assert.That((x1, z1), Is.EqualTo((40, 0)));
            Assert.That((x2, z2), Is.EqualTo((20, 30)));
            snapshot.GetVerticalRange(0, out int min0, out int max0);
            snapshot.GetVerticalRange(1, out int min1, out int max1);
            Assert.That((min0, max0), Is.EqualTo((0, 200)));
            Assert.That((min1, max1), Is.EqualTo((10, 50)));
        }

        [Test]
        public void RuntimeNavObstacleSnapshot_BeginPrimitive_RejectsInvertedVerticalExtent()
        {
            var snapshot = new RuntimeNavObstacleSnapshot(2, 4, GroundLayerId);
            snapshot.BeginCapture();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => snapshot.BeginPrimitive(1, 0, NavObstacleKind.Circle, minYcm: 5, maxYcm: 5))!;
            Assert.That(ex.Message, Does.Contain("minYcm"));
        }

        [Test]
        public void RuntimeNavObstacleSnapshot_SortCopyAccess_PreserveVerticalRanges_ZeroAllocAfterWarmup()
        {
            var snapshot = new RuntimeNavObstacleSnapshot(8, 16, GroundLayerId);
            var destination = snapshot.CreateCompatibleEmpty();
            snapshot.BeginCapture();
            AddCircle(snapshot, 9, 1, 900, 1, 10, minYcm: 10, maxYcm: 20);
            AddCircle(snapshot, 3, 0, 301, 0, 13, minYcm: 30, maxYcm: 40);
            snapshot.EndCaptureAndSort();
            snapshot.CopyTo(destination);

            Assert.That(destination.EntityIds.ToArray(), Is.EqualTo(new[] { 3, 9 }));
            destination.GetVerticalRange(0, out int min0, out int max0);
            destination.GetVerticalRange(1, out int min1, out int max1);
            Assert.That((min0, max0), Is.EqualTo((30, 40)));
            Assert.That((min1, max1), Is.EqualTo((10, 20)));

            for (int i = 0; i < 64; i++)
            {
                snapshot.BeginCapture();
                AddCircle(snapshot, 2, 0, 2, 2, 6, minYcm: 7, maxYcm: 9);
                AddCircle(snapshot, 1, 0, 1, 1, 5, minYcm: 0, maxYcm: 3);
                snapshot.EndCaptureAndSort();
                snapshot.CopyTo(destination);
                destination.GetVerticalRange(0, out _, out _);
                destination.GetVerticalRange(1, out _, out _);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                snapshot.BeginCapture();
                AddCircle(snapshot, 2, 0, 2, 2, 6, minYcm: 7, maxYcm: 9);
                AddCircle(snapshot, 1, 0, 1, 1, 5, minYcm: 0, maxYcm: 3);
                snapshot.EndCaptureAndSort();
                snapshot.CopyTo(destination);
                destination.GetVerticalRange(0, out _, out _);
                destination.GetVerticalRange(1, out _, out _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), $"Warmed snapshot capture/sort/copy/access path allocated {allocated} bytes.");
        }

        [Test]
        public void RuntimeNavObstacleSnapshot_EndCaptureAndSort_OrdersByStableKeyDeterministically()
        {
            var snapshot = new RuntimeNavObstacleSnapshot(8, 16, GroundLayerId);
            snapshot.BeginCapture();

            AddCircle(snapshot, entityId: 9, pieceIndex: 1, centerXcm: 900, centerZcm: 1, radiusCm: 10);
            AddCircle(snapshot, entityId: 3, pieceIndex: 2, centerXcm: 300, centerZcm: 2, radiusCm: 11);
            AddCircle(snapshot, entityId: 9, pieceIndex: 0, centerXcm: 901, centerZcm: 0, radiusCm: 12);
            AddCircle(snapshot, entityId: 3, pieceIndex: 0, centerXcm: 301, centerZcm: 0, radiusCm: 13);
            snapshot.EndCaptureAndSort();

            Assert.That(snapshot.EntityIds.ToArray(), Is.EqualTo(new[] { 3, 3, 9, 9 }));
            Assert.That(snapshot.PieceIndices.ToArray(), Is.EqualTo(new[] { 0, 2, 0, 1 }));
            snapshot.GetCircle(0, out int cx0, out _, out _);
            snapshot.GetCircle(1, out int cx1, out _, out _);
            snapshot.GetCircle(2, out int cx2, out _, out _);
            snapshot.GetCircle(3, out int cx3, out _, out _);
            Assert.That(new[] { cx0, cx1, cx2, cx3 }, Is.EqualTo(new[] { 301, 300, 901, 900 }));
        }

        [Test]
        public void RuntimeNavObstacleSnapshot_EndCaptureAndSort_RejectsDuplicateStableKeys()
        {
            var snapshot = new RuntimeNavObstacleSnapshot(4, 8, GroundLayerId);
            snapshot.BeginCapture();
            AddCircle(snapshot, entityId: 4, pieceIndex: 1, centerXcm: 1, centerZcm: 1, radiusCm: 5);
            AddCircle(snapshot, entityId: 4, pieceIndex: 1, centerXcm: 2, centerZcm: 2, radiusCm: 6);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => snapshot.EndCaptureAndSort())!;
            Assert.That(ex.Message, Does.Contain("duplicate stable key"));
            Assert.That(ex.Message, Does.Contain("entityId=4"));
            Assert.That(ex.Message, Does.Contain("pieceIndex=1"));
        }

        [Test]
        public void RuntimeNavObstacleSnapshot_EndCaptureAndSort_RejectsIncompleteCircle()
        {
            var snapshot = new RuntimeNavObstacleSnapshot(2, 4, GroundLayerId);
            snapshot.BeginCapture();
            snapshot.BeginPrimitive(entityId: 1, pieceIndex: 0, NavObstacleKind.Circle, minYcm: 0, maxYcm: 1);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => snapshot.EndCaptureAndSort())!;
            Assert.That(ex.Message, Does.Contain("radiusCm"));
        }

        [Test]
        public void RuntimeNavObstacleSnapshot_BeginPrimitive_OverflowNamesObstaclePrimitiveCapacity()
        {
            var snapshot = new RuntimeNavObstacleSnapshot(1, 8, GroundLayerId);
            snapshot.BeginCapture();
            AddCircle(snapshot, entityId: 1, pieceIndex: 0, centerXcm: 0, centerZcm: 0, radiusCm: 5);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => snapshot.BeginPrimitive(entityId: 2, pieceIndex: 0, NavObstacleKind.Circle, minYcm: 0, maxYcm: 1))!;
            Assert.That(ex.Message, Does.Contain("obstaclePrimitiveCapacity"));
        }

        [Test]
        public void RuntimeNavObstacleSnapshot_BeginPolygonVertices_OverflowNamesPolygonVertexCapacity()
        {
            var snapshot = new RuntimeNavObstacleSnapshot(2, 2, GroundLayerId);
            snapshot.BeginCapture();
            int index = snapshot.BeginPrimitive(entityId: 1, pieceIndex: 0, NavObstacleKind.Polygon, minYcm: 0, maxYcm: 1);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => snapshot.BeginPolygonVertices(index, vertexCount: 3))!;
            Assert.That(ex.Message, Does.Contain("polygonVertexCapacity"));
        }

        [Test]
        public void RuntimeNavObstacleSnapshot_CopyTo_IsolatesDestinationMutation()
        {
            var source = new RuntimeNavObstacleSnapshot(4, 16, GroundLayerId);
            var destination = source.CreateCompatibleEmpty();

            source.BeginCapture();
            int polygon = source.BeginPrimitive(entityId: 2, pieceIndex: 0, NavObstacleKind.Polygon, minYcm: 0, maxYcm: 40);
            int offset = source.BeginPolygonVertices(polygon, 3);
            source.SetPolygonVertex(offset + 0, 1, 2);
            source.SetPolygonVertex(offset + 1, 3, 4);
            source.SetPolygonVertex(offset + 2, 5, 6);
            source.EndCaptureAndSort();
            source.CopyTo(destination);

            destination.BeginCapture();
            AddCircle(destination, entityId: 99, pieceIndex: 0, centerXcm: 50, centerZcm: 60, radiusCm: 7);
            destination.EndCaptureAndSort();

            Assert.That(source.ObstacleCount, Is.EqualTo(1));
            Assert.That(source.GetKind(0), Is.EqualTo(NavObstacleKind.Polygon));
            source.GetPolygonVertex(0, 0, out int x, out int z);
            Assert.That((x, z), Is.EqualTo((1, 2)));
            Assert.That(destination.ObstacleCount, Is.EqualTo(1));
            Assert.That(destination.GetKind(0), Is.EqualTo(NavObstacleKind.Circle));
        }

        [Test]
        public void NavMeshBakeConfigLoader_RejectsMissingRuntimeCapacityFields()
        {
            AssertCapacityRejection(
                """
                {
                  "mode": "offline",
                  "algorithm": "recast",
                  "profiles": [
                    { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 }
                  ],
                  "layers": [
                    { "id": "Ground", "layer": 0 }
                  ],
                  "areas": [],
                  "runtimeIncremental": {
                    "tileBudgetPerFixedTick": 1,
                    "includeNeighborTiles": true,
                    "heightScaleMeters": 1,
                    "minWalkableUpDot": 0.6,
                    "cliffHeightThreshold": 1,
                    "obstaclePrimitiveCapacity": 512,
                    "polygonVertexCapacity": 4096,
                    "dirtyTileCapacity": 64,
                    "stagedEntryCapacity": 64,
                    "publishedTileCapacity": 64,
                    "storeGroupCapacity": 8,
                    "residentTileCapacity": 128,
                    "outputVertexCapacity": 256,
                    "outputTriangleCapacity": 512,
                    "outputPortalCapacity": 64,
                    "initialResidentChunkX": 0,
                    "initialResidentChunkZ": 0,
                    "initialResidentWidthChunks": 1,
                    "initialResidentHeightChunks": 1
                  },
                "layeredSpan": {
                "scratchSlotCount": 2,
                "rasterCellSizeCm": 100,
                "rasterHaloCells": 1,
                "sameSurfaceToleranceCm": 5,
                "maxSimplificationErrorCm": 0,
                "heightRounding": "roundHalfAwayFromZero",
                "maxLawsonFlipCount": 100000,
                "columnCapacity": 64,
                "spanCapacity": 128,
                "classifiedSpanCapacity": 128,
                "walkableSpanCapacity": 128,
                "linkCapacity": 256,
                "sheetCapacity": 128,
                "portalIntervalCapacity": 256,
                "regionCapacity": 64,
                "chartCapacity": 32,
                "ringCapacity": 32,
                "contourVertexCapacity": 256,
                "contourEdgeCapacity": 256,
                "seamCapacity": 64,
                "canonicalLinkCapacity": 256,
                "splitPointCapacity": 64,
                "triangulationVertexCapacity": 256,
                "triangulationTriangleCapacity": 512,
                "constrainedEdgeCapacity": 512,
                "borderPortalCapacity": 64,
                "polygonVertexCapacity": 256,
                "adjacencyEdgeCapacity": 1536,
                "bridgeCandidateCapacity": 256,
                "ringWorkCapacity": 64,
                "temporaryConstraintFlagCapacity": 512
                },
                "triangleSurface": {
                  "haloPaddingCm": 100
                },
                "recast": {
                  "rasterCellSizeCm": 10,
                  "rasterCellHeightCm": 5
                }
                }
                """,
                "trackedStructuralEntityCapacity");
        }

        [Test]
        public void NavMeshBakeConfigLoader_RejectsNonPositiveRuntimeCapacityFields()
        {
            AssertCapacityRejection(
                """
                {
                  "mode": "offline",
                  "algorithm": "recast",
                  "profiles": [
                    { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 }
                  ],
                  "layers": [
                    { "id": "Ground", "layer": 0 }
                  ],
                  "areas": [],
                  "runtimeIncremental": {
                    "tileBudgetPerFixedTick": 1,
                    "includeNeighborTiles": true,
                    "heightScaleMeters": 1,
                    "minWalkableUpDot": 0.6,
                    "cliffHeightThreshold": 1,
                    "trackedStructuralEntityCapacity": 256,
                    "obstaclePrimitiveCapacity": 0,
                    "polygonVertexCapacity": 4096,
                    "dirtyTileCapacity": 64,
                    "stagedEntryCapacity": 64,
                    "publishedTileCapacity": 64,
                    "storeGroupCapacity": 8,
                    "residentTileCapacity": 128,
                    "outputVertexCapacity": 256,
                    "outputTriangleCapacity": 512,
                    "outputPortalCapacity": 64,
                    "initialResidentChunkX": 0,
                    "initialResidentChunkZ": 0,
                    "initialResidentWidthChunks": 1,
                    "initialResidentHeightChunks": 1
                  },
                "layeredSpan": {
                "scratchSlotCount": 2,
                "rasterCellSizeCm": 100,
                "rasterHaloCells": 1,
                "sameSurfaceToleranceCm": 5,
                "maxSimplificationErrorCm": 0,
                "heightRounding": "roundHalfAwayFromZero",
                "maxLawsonFlipCount": 100000,
                "columnCapacity": 64,
                "spanCapacity": 128,
                "classifiedSpanCapacity": 128,
                "walkableSpanCapacity": 128,
                "linkCapacity": 256,
                "sheetCapacity": 128,
                "portalIntervalCapacity": 256,
                "regionCapacity": 64,
                "chartCapacity": 32,
                "ringCapacity": 32,
                "contourVertexCapacity": 256,
                "contourEdgeCapacity": 256,
                "seamCapacity": 64,
                "canonicalLinkCapacity": 256,
                "splitPointCapacity": 64,
                "triangulationVertexCapacity": 256,
                "triangulationTriangleCapacity": 512,
                "constrainedEdgeCapacity": 512,
                "borderPortalCapacity": 64,
                "polygonVertexCapacity": 256,
                "adjacencyEdgeCapacity": 1536,
                "bridgeCandidateCapacity": 256,
                "ringWorkCapacity": 64,
                "temporaryConstraintFlagCapacity": 512
                },
                "triangleSurface": {
                  "haloPaddingCm": 100
                },
                "recast": {
                  "rasterCellSizeCm": 10,
                  "rasterCellHeightCm": 5
                }
                }
                """,
                "obstaclePrimitiveCapacity");

            AssertCapacityRejection(
                """
                {
                  "mode": "offline",
                  "algorithm": "recast",
                  "profiles": [
                    { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 }
                  ],
                  "layers": [
                    { "id": "Ground", "layer": 0 }
                  ],
                  "areas": [],
                  "runtimeIncremental": {
                    "tileBudgetPerFixedTick": 1,
                    "includeNeighborTiles": true,
                    "heightScaleMeters": 1,
                    "minWalkableUpDot": 0.6,
                    "cliffHeightThreshold": 1,
                    "trackedStructuralEntityCapacity": 256,
                    "obstaclePrimitiveCapacity": 512,
                    "polygonVertexCapacity": -1,
                    "dirtyTileCapacity": 64,
                    "stagedEntryCapacity": 64,
                    "publishedTileCapacity": 64,
                    "storeGroupCapacity": 8,
                    "residentTileCapacity": 128,
                    "outputVertexCapacity": 256,
                    "outputTriangleCapacity": 512,
                    "outputPortalCapacity": 64,
                    "initialResidentChunkX": 0,
                    "initialResidentChunkZ": 0,
                    "initialResidentWidthChunks": 1,
                    "initialResidentHeightChunks": 1
                  },
                  "layeredSpan": {
                    "scratchSlotCount": 2,
                    "rasterCellSizeCm": 100,
                    "rasterHaloCells": 1,
                    "sameSurfaceToleranceCm": 5,
                    "maxSimplificationErrorCm": 0,
                    "heightRounding": "roundHalfAwayFromZero",
                    "maxLawsonFlipCount": 100000,
                    "columnCapacity": 64,
                    "spanCapacity": 128,
                    "classifiedSpanCapacity": 128,
                    "walkableSpanCapacity": 128,
                    "linkCapacity": 256,
                    "sheetCapacity": 128,
                    "portalIntervalCapacity": 256,
                    "regionCapacity": 64,
                    "chartCapacity": 32,
                    "ringCapacity": 32,
                    "contourVertexCapacity": 256,
                    "contourEdgeCapacity": 256,
                    "seamCapacity": 64,
                    "canonicalLinkCapacity": 256,
                    "splitPointCapacity": 64,
                    "triangulationVertexCapacity": 256,
                    "triangulationTriangleCapacity": 512,
                    "constrainedEdgeCapacity": 512,
                    "borderPortalCapacity": 64,
                    "polygonVertexCapacity": 256,
                    "adjacencyEdgeCapacity": 1536,
                    "bridgeCandidateCapacity": 256,
                    "ringWorkCapacity": 64,
                    "temporaryConstraintFlagCapacity": 512
                },
                "triangleSurface": {
                  "haloPaddingCm": 100
                },
                "recast": {
                  "rasterCellSizeCm": 10,
                  "rasterCellHeightCm": 5
                }
                }
                """,
                "polygonVertexCapacity");
        }

        private static void AddCircle(
            RuntimeNavObstacleSnapshot snapshot,
            int entityId,
            int pieceIndex,
            int centerXcm,
            int centerZcm,
            int radiusCm,
            int minYcm = 0,
            int maxYcm = 200)
        {
            int index = snapshot.BeginPrimitive(entityId, pieceIndex, NavObstacleKind.Circle, minYcm, maxYcm);
            snapshot.SetCircle(index, centerXcm, centerZcm, radiusCm);
        }

        private static void AssertCapacityRejection(string navmeshJson, string expectedField)
        {
            string root = CreateTempNavConfig(navmeshJson);
            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => LoadTempConfig(root))!;
                Assert.That(ex.Message, Does.Contain(expectedField));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static NavMeshBakeConfig LoadTempConfig(string root)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var agentProfiles = new AgentProfileRegistry(new[]
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
            return new NavMeshBakeConfigLoader(pipeline, agentProfiles).Load(catalog);
        }

        private static string CreateTempNavConfig(string navmeshJson)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-runtime-obstacle-snapshot-" + Guid.NewGuid().ToString("N"));
            string coreConfigs = Path.Combine(tempRoot, "Configs");
            Directory.CreateDirectory(Path.Combine(coreConfigs, "Navigation"));
            File.WriteAllText(Path.Combine(coreConfigs, "config_catalog.json"),
                """
                [
                  { "Path": "Navigation/navmesh.json", "Policy": "DeepObject" }
                ]
                """);
            File.WriteAllText(Path.Combine(coreConfigs, "Navigation", "navmesh.json"), navmeshJson);
            return tempRoot;
        }
    }
}
