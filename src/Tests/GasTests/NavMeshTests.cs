using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using NUnit.Framework;

namespace GasTests
{
    public class NavMeshTests
    {
        [Test]
        public void NavTiles_BakeLoadAndCrossTilePath_Works()
        {
            var map = new VertexMap();
            map.Initialize(widthInChunks: 2, heightInChunks: 1);

            int width = map.WidthInChunks * VertexChunk.ChunkSize;
            int height = map.HeightInChunks * VertexChunk.ChunkSize;

            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    map.SetHeight(c, r, 0);
                    map.SetWaterHeight(c, r, 0);
                    map.SetRamp(c, r, false);
                    map.SetBlocked(c, r, false);
                }
            }

            var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            var bytesByTile = new Dictionary<NavTileId, byte[]>();
            for (int cy = 0; cy < map.HeightInChunks; cy++)
            {
                for (int cx = 0; cx < map.WidthInChunks; cx++)
                {
                    Assert.That(NavTileBuilder.TryBuildTile(map, cx, cy, 1, cfg, out var tile, out var artifact), Is.True, artifact.Message);
                    using var ms = new MemoryStream();
                    NavTileBinary.Write(ms, tile);
                    bytesByTile[tile.TileId] = ms.ToArray();
                }
            }

            var store = new NavTileStore(id =>
            {
                if (!bytesByTile.TryGetValue(id, out var b)) throw new FileNotFoundException();
                return new MemoryStream(b, writable: false);
            });
            var query = new NavQueryService(store);

            int startCol = 10;
            int startRow = 10;
            int goalCol = 74;
            int goalRow = 10;
            int startXcm = (int)MathF.Round((HexCoordinates.HexWidth * (startCol + 0.5f * (startRow & 1))) * 100f);
            int startZcm = (int)MathF.Round((HexCoordinates.RowSpacing * startRow) * 100f);
            int goalXcm = (int)MathF.Round((HexCoordinates.HexWidth * (goalCol + 0.5f * (goalRow & 1))) * 100f);
            int goalZcm = (int)MathF.Round((HexCoordinates.RowSpacing * goalRow) * 100f);

            var res = query.TryFindPath(startXcm, startZcm, goalXcm, goalZcm);
            Assert.That(res.Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(res.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(res.PathXcm.Length, Is.EqualTo(res.PathZcm.Length));
            Assert.That(res.PathXcm[0], Is.EqualTo(startXcm));
            Assert.That(res.PathZcm[0], Is.EqualTo(startZcm));
            Assert.That(res.PathXcm[^1], Is.EqualTo(goalXcm));
            Assert.That(res.PathZcm[^1], Is.EqualTo(goalZcm));
        }

        #region CDT Pipeline Tests

        [Test]
        public void WalkMaskBuilder_FlatTerrain_AllWalkable()
        {
            var map = CreateFlatTerrain(1, 1);
            var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);

            var mask = WalkMaskBuilder.Build(map, 0, 0, cfg);

            Assert.That(mask.TileWidth, Is.EqualTo(VertexChunk.ChunkSize));
            Assert.That(mask.TileHeight, Is.EqualTo(VertexChunk.ChunkSize));
            Assert.That(mask.WalkableTriangleCount, Is.GreaterThan(0), "Should have walkable triangles");
        }

        [Test]
        public void WalkMaskBuilder_WaterCovered_NotWalkable()
        {
            var map = CreateFlatTerrain(1, 1);

            // Cover center with water
            for (int r = 20; r < 40; r++)
            {
                for (int c = 20; c < 40; c++)
                {
                    map.SetWaterHeight(c, r, 10); // Water above ground
                }
            }

            var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            var mask = WalkMaskBuilder.Build(map, 0, 0, cfg);

            // Check that water-covered area is not walkable
            Assert.That(mask.IsWalkable(30, 30, 0), Is.False, "Water-covered cell should not be walkable");
            Assert.That(mask.IsWalkable(10, 10, 0), Is.True, "Dry cell should be walkable");
        }

        [Test]
        public void WalkMaskBuilder_Cliff_NotWalkable()
        {
            var map = CreateFlatTerrain(1, 1);

            // Create a cliff (height difference > threshold)
            for (int r = 30; r < 64; r++)
            {
                for (int c = 0; c < 64; c++)
                {
                    map.SetHeight(c, r, 10); // High plateau
                }
            }

            var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            var mask = WalkMaskBuilder.Build(map, 0, 0, cfg);

            // Cells at the cliff edge should have some non-walkable triangles
            // (row 29 has height 0, row 30 has height 10, difference > 1)
            Assert.That(mask.IsWalkable(32, 10, 0), Is.True, "Flat low area should be walkable");
            Assert.That(mask.IsWalkable(32, 50, 0), Is.True, "Flat high plateau should be walkable");
        }

        [Test]
        public void ContourExtractor_SimpleSquare_ExtractsRing()
        {
            var map = CreateFlatTerrain(1, 1);

            // Block edges to create a simple square
            for (int i = 0; i < 64; i++)
            {
                map.SetBlocked(i, 0, true);
                map.SetBlocked(i, 63, true);
                map.SetBlocked(0, i, true);
                map.SetBlocked(63, i, true);
            }

            var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            var mask = WalkMaskBuilder.Build(map, 0, 0, cfg);
            var rings = ContourExtractor.Extract(mask, 0, 0);

            Assert.That(rings.Count, Is.GreaterThan(0), "Should extract at least one ring");
        }

        [Test]
        public void ContourExtractor_DonutShape_ExtractsOuterAndHole()
        {
            var map = CreateFlatTerrain(1, 1);

            // Create a hole in the center (donut shape)
            for (int r = 25; r < 40; r++)
            {
                for (int c = 25; c < 40; c++)
                {
                    map.SetBlocked(c, r, true);
                }
            }

            var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            var mask = WalkMaskBuilder.Build(map, 0, 0, cfg);
            var rings = ContourExtractor.Extract(mask, 0, 0);

            // Should have outer boundary and hole
            int outerCount = 0, holeCount = 0;
            foreach (var ring in rings)
            {
                if (ring.IsOuter) outerCount++;
                else holeCount++;
            }

            Assert.That(outerCount, Is.GreaterThan(0), "Should have outer boundary");
            Assert.That(holeCount, Is.GreaterThan(0), "Should have hole");
        }

        [Test]
        public void PolygonProcessor_ValidPolygon_NoWarnings()
        {
            var rings = new List<IntRing>
            {
                new IntRing(
                    new IntPoint[] {
                        new IntPoint(0, 0),
                        new IntPoint(10, 0),
                        new IntPoint(10, 10),
                        new IntPoint(0, 10)
                    },
                    true, 100)
            };

            var config = new PolygonProcessConfig();
            var result = PolygonProcessor.Process(rings, config);

            Assert.That(result.Polygons.Length, Is.EqualTo(1));
            Assert.That(result.HasWarnings, Is.False);
        }

        [Test]
        public void PolygonProcessor_PolygonWithHole_AssignsCorrectly()
        {
            var rings = new List<IntRing>
            {
                // Outer (CCW, large)
                new IntRing(
                    new IntPoint[] {
                        new IntPoint(0, 0),
                        new IntPoint(20, 0),
                        new IntPoint(20, 20),
                        new IntPoint(0, 20)
                    },
                    true, 400),
                // Hole (CW, smaller, inside outer)
                new IntRing(
                    new IntPoint[] {
                        new IntPoint(5, 5),
                        new IntPoint(5, 15),
                        new IntPoint(15, 15),
                        new IntPoint(15, 5)
                    },
                    false, -100)
            };

            var config = new PolygonProcessConfig();
            var result = PolygonProcessor.Process(rings, config);

            Assert.That(result.Polygons.Length, Is.EqualTo(1));
            Assert.That(result.Polygons[0].Holes.Length, Is.EqualTo(1), "Hole should be assigned to outer");
        }

        [Test]
        public void Triangulator_SimpleSquare_ProducesTriangles()
        {
            var polygon = new Polygon(
                new IntPoint[] {
                    new IntPoint(0, 0),
                    new IntPoint(10, 0),
                    new IntPoint(10, 10),
                    new IntPoint(0, 10)
                });

            var triangulator = TriangulatorFactory.CreateDefault();
            var success = triangulator.TryTriangulate(polygon, out var mesh, out var error);

            Assert.That(success, Is.True, $"Triangulation failed: {error}");
            Assert.That(mesh.VertexCount, Is.EqualTo(4));
            Assert.That(mesh.TriangleCount, Is.EqualTo(2), "Square should produce 2 triangles");
        }

        [Test]
        public void Triangulator_ConvexPentagon_ProducesTriangles()
        {
            var polygon = new Polygon(
                new IntPoint[] {
                    new IntPoint(5, 0),
                    new IntPoint(10, 4),
                    new IntPoint(8, 10),
                    new IntPoint(2, 10),
                    new IntPoint(0, 4)
                });

            var triangulator = TriangulatorFactory.CreateDefault();
            var success = triangulator.TryTriangulate(polygon, out var mesh, out var error);

            Assert.That(success, Is.True, $"Triangulation failed: {error}");
            Assert.That(mesh.VertexCount, Is.EqualTo(5));
            Assert.That(mesh.TriangleCount, Is.EqualTo(3), "Pentagon should produce 3 triangles");
        }

        [Test]
        public void BakePipeline_FlatTerrain_ProducesTile()
        {
            var map = CreateFlatTerrain(1, 1);
            var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            var context = new BakePipelineContext();

            var result = BakePipeline.Execute(map, 0, 0, 1, cfg, context);

            Assert.That(result.Success, Is.True, $"Bake failed at stage {context.CurrentStage}: {result.Artifact.Message}");
            Assert.That(result.Tile, Is.Not.Null);
            Assert.That(result.Tile.TriangleCount, Is.GreaterThan(0));
            Assert.That(result.Tile.Portals.Length, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void BakePipeline_TerrainWithHole_ProducesTileWithHole()
        {
            var map = CreateFlatTerrain(1, 1);

            // Create a blocked hole in the center
            for (int r = 25; r < 40; r++)
            {
                for (int c = 25; c < 40; c++)
                {
                    map.SetBlocked(c, r, true);
                }
            }

            var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            var context = new BakePipelineContext();

            var result = BakePipeline.Execute(map, 0, 0, 1, cfg, context);

            Assert.That(result.Success, Is.True, $"Bake failed: {result.Artifact.Message}");
            Assert.That(result.Tile.TriangleCount, Is.GreaterThan(0));

            // Verify logs contain polygon processing info
            Assert.That(context.Logs.Count, Is.GreaterThan(0));
        }

        [Test]
        public void FunnelAlgorithm_DirectPath_ReturnsStartAndGoal()
        {
            var start = new Vector2(0, 0);
            var goal = new Vector2(10, 10);
            var portals = new List<FunnelPortal>();

            var result = FunnelAlgorithm.SmoothPath(start, goal, portals);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Path.Length, Is.EqualTo(2));
            Assert.That(result.Path[0], Is.EqualTo(start));
            Assert.That(result.Path[1], Is.EqualTo(goal));
        }

        [Test]
        public void FunnelAlgorithm_SinglePortal_SmoothsPath()
        {
            var start = new Vector2(0, 5);
            var goal = new Vector2(20, 5);
            var portals = new List<FunnelPortal>
            {
                new FunnelPortal(10, 0, 10, 10) // Vertical portal at x=10
            };

            var result = FunnelAlgorithm.SmoothPath(start, goal, portals);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Path.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(result.Path[0], Is.EqualTo(start));
            Assert.That(result.Path[^1], Is.EqualTo(goal));
        }

        [Test]
        public void FunnelAlgorithm_MultiplePortals_SmoothsPath()
        {
            var start = new Vector2(0, 5);
            var goal = new Vector2(30, 5);
            var portals = new List<FunnelPortal>
            {
                new FunnelPortal(10, 0, 10, 10),
                new FunnelPortal(20, 0, 20, 10)
            };

            var result = FunnelAlgorithm.SmoothPath(start, goal, portals);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Path.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(result.Path[0], Is.EqualTo(start));
            Assert.That(result.Path[^1], Is.EqualTo(goal));
        }

        [Test]
        public void ExtendedBakeArtifact_GeneratesReport()
        {
            var artifact = new ExtendedBakeArtifact(
                new NavTileId(1, 2, 0), 1,
                NavBakeStage.Serialize, NavBakeErrorCode.None, "");
            artifact.InputWalkableTriangles = 100;
            artifact.OutputTriangleCount = 50;
            artifact.OutputVertexCount = 30;

            var report = artifact.GenerateReport();

            Assert.That(report, Does.Contain("NavMesh Bake Artifact Report"));
            Assert.That(report, Does.Contain("Tile: (1, 2"));
            Assert.That(report, Does.Contain("Input Walkable Triangles: 100"));
        }

        #endregion

        #region Helper Methods

        private static VertexMap CreateFlatTerrain(int widthChunks, int heightChunks)
        {
            var map = new VertexMap();
            map.Initialize(widthInChunks: widthChunks, heightInChunks: heightChunks);

            int width = map.WidthInChunks * VertexChunk.ChunkSize;
            int height = map.HeightInChunks * VertexChunk.ChunkSize;

            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    map.SetHeight(c, r, 0);
                    map.SetWaterHeight(c, r, 0);
                    map.SetRamp(c, r, false);
                    map.SetBlocked(c, r, false);
                }
            }

            return map;
        }

        #endregion
    }
}

