using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// terrainFeed=direct(高度场列直灌)与 terrainFeed=triangles(每格三角化)的双轨对拍:
    /// 同一合成地形(平原+台地断崖+水湾+阻挡条)按 #1344 判据锁拓扑等价——
    /// 采样点可行域一致率、可行三角数量级一致、台地区域 area 一致。
    /// </summary>
    [TestFixture]
    public sealed class NavTerrainFeedDualTrackTests
    {
        private const int Cells = 192; // 3×3 chunks @ 64 cells
        private const int ChunkCells = SpatialScaleDefaults.TerrainChunkCells;

        private static readonly AgentProfileConfig _agent = new()
        {
            Id = "Small",
            RadiusCm = 30,
            HeightCm = 180,
            ClearanceCm = 40,
            Mass = 1,
            Layer = 0
        };

        private static readonly NavMeshAgentProfileConfig _nav = new() { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 };

        [Test]
        public void DirectFeed_MatchesTriangleFeed_OnReliefTerrain()
        {
            MutableGridLogicTerrainField terrain = BuildReliefTerrain();
            var legacy = new NavBuildConfig(1.0f, 0.6f, cliffHeightThreshold: 1);

            bool okA = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, _agent, _nav, 0, "ground", new NavObstacleSet(), out NavTile tileTriangles, out _, out NavBakeArtifact artifactTriangles);
            bool okB = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, _agent, _nav, 0, "ground", new NavObstacleSet(), out NavTile tileDirect, out _, out NavBakeArtifact artifactDirect, NavTerrainFeedKind.Direct);

            Assert.That(okA && okB, Is.True, $"triangles={okA}({artifactTriangles.ErrorCode}:{artifactTriangles.Message}) direct={okB}({artifactDirect.ErrorCode}:{artifactDirect.Message})");

            // 数量级一致:可行域面积近似 → 三角数量在 2× 因子内
            Assert.That(tileDirect.TriangleCount, Is.InRange(tileTriangles.TriangleCount / 2, tileTriangles.TriangleCount * 2),
                $"triangle counts diverged: triangles={tileTriangles.TriangleCount} direct={tileDirect.TriangleCount}");

            // 采样一致率:中心瓦片足迹内 1200 随机点的"在可行 navmesh 上"判定
            var random = new Random(20260829);
            terrain.GetWorldPositionMeters(ChunkCells, ChunkCells, out float x0, out float z0);
            terrain.GetWorldPositionMeters(ChunkCells * 2, ChunkCells * 2, out float x1, out float z1);
            int samples = 0;
            int mismatches = 0;
            for (int i = 0; i < 1200; i++)
            {
                float px = x0 + (float)random.NextDouble() * (x1 - x0);
                float pz = z0 + (float)random.NextDouble() * (z1 - z0);
                bool onA = IsOnMesh(tileTriangles, px, pz, out byte areaA);
                bool onB = IsOnMesh(tileDirect, px, pz, out byte areaB);
                samples++;
                if (onA != onB)
                {
                    mismatches++;
                    continue;
                }

                if (onA && areaA != areaB)
                {
                    mismatches++;
                }
            }

            double agreement = 1.0 - mismatches / (double)samples;
            Assert.That(agreement, Is.GreaterThanOrEqualTo(0.9),
                $"point sampling agreement {agreement:P1} below 90% (mismatches {mismatches}/{samples})");
        }

        [Test]
        public void DirectFeed_FlatPlain_ProducesWalkableMesh()
        {
            var terrain = new MutableGridLogicTerrainField(Cells, Cells, 100, ChunkCells);
            for (int r = 0; r < Cells; r++)
            {
                for (int c = 0; c < Cells; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            var legacy = new NavBuildConfig(1.0f, 0.6f, cliffHeightThreshold: 1);
            bool ok = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, _agent, _nav, 0, "ground", new NavObstacleSet(), out NavTile tile, out _, out NavBakeArtifact artifact, NavTerrainFeedKind.Direct);
            Assert.That(ok, Is.True, $"{artifact.ErrorCode}: {artifact.Message}");
            Assert.That(tile.TriangleCount, Is.GreaterThan(0));
            Assert.That(tile.Portals.Length, Is.GreaterThan(0), "flat plain tile must carry portals");
        }

        /// <summary>#1368 终态:粗格战略瓦片(体素=地形格)是合法档位,两轨都必须干净烤成。</summary>
        [Test]
        public void CoarseStrategicTile_BakesCleanlyOnBothFeeds()
        {
            int coarseCellCm = 125_649; // 源采样分辨率,64 格瓦片 ≈ 80km
            var terrain = new MutableGridLogicTerrainField(Cells, Cells, coarseCellCm, ChunkCells);
            for (int r = 0; r < Cells; r++)
            {
                for (int c = 0; c < Cells; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            var legacy = new NavBuildConfig(1.0f, 0.6f, cliffHeightThreshold: 1);
            foreach (NavTerrainFeedKind feed in new[] { NavTerrainFeedKind.Triangles, NavTerrainFeedKind.Direct })
            {
                bool ok = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, _agent, _nav, 0, "ground", new NavObstacleSet(), out NavTile tile, out _, out NavBakeArtifact artifact, feed);
                Assert.That(ok, Is.True, $"{feed}: {artifact.ErrorCode} {artifact.Message}");
                Assert.That(tile.TriangleCount, Is.GreaterThan(0), $"{feed} coarse strategic tile must carry a mesh");
            }
        }

        [Test]
        public void BakeConfigLoader_AcceptsTerrainFeedDirect()
        {
            var config = new NavMeshBakeConfig { TerrainFeed = NavBakeNames.TerrainFeedDirect };
            Assert.That(config.ParsedTerrainFeed, Is.EqualTo(NavTerrainFeedKind.Direct));

            config.TerrainFeed = "bogus";
            Assert.That(() => _ = config.ParsedTerrainFeed, Throws.InvalidOperationException);
        }

        [Test]
        public void DirectFeed_WaterCorner_CutsAtTriangleDiagonal()
        {
            var terrain = FlatTerrain();
            for (int r = 65; r <= 67; r++)
            {
                for (int c = 65; c <= 67; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(2, 3, LogicTerrainSurfaceFlags.Water));
                }
            }

            AssertFeatureAgreement(terrain, 64f, 68f, 64f, 68f, probes: 17);
        }

        /// <summary>Blocked 切发射是两轨共同语义(NavTileBuilder.AddFace 对 blocked 角早退,
        /// 共享分类器同判)——直灌轨在被阻挡区必须成洞。(审计更正:此前据混叠测量误断
        /// "Blocked 不参与发射"并写反了测试,现已以共享分类器为单一语义源。)</summary>
        [Test]
        public void DirectFeed_BlockedCells_CutEmission()
        {
            var terrain = FlatTerrain();
            for (int r = 65; r <= 67; r++)
            {
                for (int c = 65; c <= 67; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.Blocked));
                }
            }

            var legacy = new NavBuildConfig(1.0f, 0.6f, cliffHeightThreshold: 1);
            bool ok = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, _agent, _nav, 0, "ground", new NavObstacleSet(), out NavTile tile, out _, out NavBakeArtifact artifact, NavTerrainFeedKind.Direct);
            Assert.That(ok, Is.True, artifact.Message);
            Assert.That(IsOnMesh(tile, 66f, 66f, out _), Is.False, "direct: blocked patch center must be a hole");
            AssertProbeOn(tile, 100f, 100f, "direct far plain stays walkable");
        }

        [Test]
        public void DirectFeed_CliffSplit_PreservesBothFloors()
        {
            var terrain = FlatTerrain();
            for (int r = 80; r < 120; r++)
            {
                for (int c = 80; c < 120; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(8, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            AssertFeatureAgreement(terrain, 74f, 126f, 74f, 126f, probes: 21);
            BakeBoth(terrain, out NavTile tri, out NavTile dir);
            AssertProbeOn(tri, 100f, 100f, "triangles plateau top");
            AssertProbeOn(dir, 100f, 100f, "direct plateau top");
            AssertProbeOn(tri, 70f, 70f, "triangles plain");
            AssertProbeOn(dir, 70f, 70f, "direct plain");
        }

        [Test]
        public void DirectFeed_ObstacleAtQuadCorner_CutsIntersectingTriangles()
        {
            var terrain = FlatTerrain();
            var obstacles = new NavObstacleSet();
            obstacles.Obstacles.Add(new NavObstacle
            {
                Kind = NavObstacleKind.Circle,
                LayerId = "ground",
                Center = new NavPointCm(6600, 6600),
                RadiusCm = 90,
                Enabled = true
            });

            BakeBoth(terrain, out NavTile tri, out NavTile dir, obstacles);
            Assert.That(IsOnMesh(tri, 66f, 66f, out _), Is.False, "triangles: obstacle center must be a hole");
            Assert.That(IsOnMesh(dir, 66f, 66f, out _), Is.False, "direct: obstacle center must be a hole");
            AssertProbeOn(tri, 100f, 100f, "triangles far from obstacle");
            AssertProbeOn(dir, 100f, 100f, "direct far from obstacle");
        }

        [Test]
        public void DirectFeed_AuthoredArea63_FailsFast()
        {
            var terrain = FlatTerrain();
            terrain.SetCell(70, 70, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.None, areaId: 63));
            var legacy = new NavBuildConfig(1.0f, 0.6f, cliffHeightThreshold: 1);
            bool ok = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, _agent, _nav, 0, "ground", new NavObstacleSet(), out _, out _, out NavBakeArtifact artifact, NavTerrainFeedKind.Direct);
            Assert.That(ok, Is.False);
            Assert.That(artifact.Message, Does.Contain("reserved"));
        }

        [Test]
        public void Loader_TerrainFeed_MissingDirectNullAndBogus()
        {
            Assert.That(LoadTempNavConfig(null), Is.EqualTo(NavBakeNames.TerrainFeedTriangles));
            Assert.That(LoadTempNavConfig("\"direct\""), Is.EqualTo(NavBakeNames.TerrainFeedDirect));
            Assert.Throws<InvalidOperationException>(() => LoadTempNavConfig("null"));
            Assert.Throws<InvalidOperationException>(() => LoadTempNavConfig("\"bogus\""));
        }

        [Test]
        public void Loader_AuthoredArea63_IsRejected()
        {
            Assert.Throws<InvalidOperationException>(
                () => LoadTempNavConfig(null, areasOverride: "\"areas\": [ { \"id\": \"Bad\", \"areaId\": 63, \"cost\": 1 } ]"));
        }

        private static MutableGridLogicTerrainField FlatTerrain()
        {
            var terrain = new MutableGridLogicTerrainField(Cells, Cells, 100, ChunkCells);
            for (int r = 0; r < Cells; r++)
            {
                for (int c = 0; c < Cells; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            return terrain;
        }

        private static void BakeBoth(MutableGridLogicTerrainField terrain, out NavTile tileTriangles, out NavTile tileDirect, NavObstacleSet? obstacles = null)
        {
            var legacy = new NavBuildConfig(1.0f, 0.6f, cliffHeightThreshold: 1);
            obstacles ??= new NavObstacleSet();
            bool okA = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, _agent, _nav, 0, "ground", obstacles, out tileTriangles, out _, out NavBakeArtifact artifactA);
            bool okB = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, _agent, _nav, 0, "ground", obstacles, out tileDirect, out _, out NavBakeArtifact artifactB, NavTerrainFeedKind.Direct);
            Assert.That(okA && okB, Is.True, $"triangles={okA}({artifactA.Message}) direct={okB}({artifactB.Message})");
        }

        private static void AssertProbeOn(NavTile tile, float xMeters, float zMeters, string label)
            => Assert.That(IsOnMesh(tile, xMeters, zMeters, out _), Is.True, $"{label}: probe ({xMeters},{zMeters}) must be walkable");

        private void AssertFeatureAgreement(MutableGridLogicTerrainField terrain, float x0, float x1, float z0, float z1, int probes)
        {
            BakeBoth(terrain, out NavTile tri, out NavTile dir);
            var random = new Random(20260829);
            int mismatched = 0;
            int total = probes * probes;
            for (int i = 0; i < total; i++)
            {
                float px = x0 + (float)random.NextDouble() * (x1 - x0);
                float pz = z0 + (float)random.NextDouble() * (z1 - z0);
                bool onA = IsOnMesh(tri, px, pz, out byte areaA);
                bool onB = IsOnMesh(dir, px, pz, out byte areaB);
                if (onA != onB || (onA && areaA != areaB))
                {
                    mismatched++;
                }
            }

            Assert.That(mismatched, Is.LessThanOrEqualTo(total / 10), $"feature-window disagreement {mismatched}/{total} exceeds 10%");
        }

        private static string LoadTempNavConfig(string? terrainFeedValue, string? areasOverride = null)
        {
            string terrainLine = terrainFeedValue == null ? string.Empty : $"  \"terrainFeed\": {terrainFeedValue},\n";
            string areasLine = areasOverride ?? "\"areas\": []";
            string json = "{\n" +
                "  \"mode\": \"offline\",\n" +
                "  \"algorithm\": \"recast\",\n" +
                terrainLine +
                "  \"profiles\": [ { \"id\": \"Small\", \"maxClimbCm\": 40, \"maxSlopeDeg\": 45 } ],\n" +
                "  \"layers\": [ { \"id\": \"Ground\", \"layer\": 0 } ],\n" +
                "  " + areasLine + ",\n" +
                "  \"runtimeIncremental\": {\n" +
                "    \"tileBudgetPerFixedTick\": 1,\n" +
                "    \"includeNeighborTiles\": true,\n" +
                "    \"heightScaleMeters\": 1,\n" +
                "    \"minWalkableUpDot\": 0.6,\n" +
                "    \"cliffHeightThreshold\": 1\n" +
                "  }\n" +
                "}";
            string root = NavBakeConfigLoaderTestHelpers.CreateTempNavConfig(json);
            try
            {
                return NavBakeConfigLoaderTestHelpers.Load(root).TerrainFeed;
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static MutableGridLogicTerrainField BuildReliefTerrain()
        {
            var terrain = new MutableGridLogicTerrainField(Cells, Cells, 100, ChunkCells);
            for (int r = 0; r < Cells; r++)
            {
                for (int c = 0; c < Cells; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            // 台地:内部 level 8、area 2;与平原(2)的 6 级落差 > cliff 阈值 → 边缘环不可行
            for (int r = 100; r < 140; r++)
            {
                for (int c = 100; c < 140; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(8, 0, LogicTerrainSurfaceFlags.None, areaId: 2));
                }
            }

            // 水湾:水面高于地面 → 不可行
            for (int r = 120; r < 170; r++)
            {
                for (int c = 10; c < 50; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(1, 3, LogicTerrainSurfaceFlags.Water));
                }
            }

            // 阻挡条
            for (int r = 30; r < 34; r++)
            {
                for (int c = 20; c < 160; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.Blocked));
                }
            }

            return terrain;
        }

        private static bool IsOnMesh(NavTile tile, float pxMeters, float pzMeters, out byte areaId)
        {
            float px = pxMeters * 100f - tile.OriginXcm;
            float pz = pzMeters * 100f - tile.OriginZcm;
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int a = tile.TriA[i];
                int b = tile.TriB[i];
                int c = tile.TriC[i];
                if (PointInTriangle(
                    px, pz,
                    tile.VertexXcm[a], tile.VertexZcm[a],
                    tile.VertexXcm[b], tile.VertexZcm[b],
                    tile.VertexXcm[c], tile.VertexZcm[c]))
                {
                    areaId = tile.TriAreaIds[i];
                    return true;
                }
            }

            areaId = 0;
            return false;
        }

        private static bool PointInTriangle(
            float px, float pz,
            float ax, float az,
            float bx, float bz,
            float cx, float cz)
        {
            float d1 = Sign(px, pz, ax, az, bx, bz);
            float d2 = Sign(px, pz, bx, bz, cx, cz);
            float d3 = Sign(px, pz, cx, cz, ax, az);
            bool hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNegative && hasPositive);
        }

        private static float Sign(float x1, float z1, float x2, float z2, float x3, float z3)
            => (x1 - x3) * (z2 - z3) - (x2 - x3) * (z1 - z3);
    }
}
