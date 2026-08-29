using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
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

        private readonly AgentProfileConfig _agent = new()
        {
            Id = "Small",
            RadiusCm = 30,
            HeightCm = 180,
            ClearanceCm = 40,
            Mass = 1,
            Layer = 0
        };

        private readonly NavMeshAgentProfileConfig _nav = new() { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 };

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

        /// <summary>#1368:粗格瓦片的体素爆炸必须在入口 fail-fast,不得溢出异常。</summary>
        [Test]
        public void CoarseTile_ExceedsVoxelBudget_FailsFastWithArtifact()
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
                bool ok = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, _agent, _nav, 0, "ground", new NavObstacleSet(), out _, out _, out NavBakeArtifact artifact, feed);
                Assert.That(ok, Is.False, $"{feed} must fail on coarse tiles");
                Assert.That(artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.VoxelBudgetExceeded), $"{feed}: {artifact.Message}");
                Assert.That(artifact.Message, Does.Contain("voxel columns"));
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
