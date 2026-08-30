using System;
using System.Numerics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Exhaustive contract for NavTileBuilder.ClassifyTriangleEmission — the single semantic
    /// source consumed by both the triangle track (AddFace) and the direct heightfield feed.
    /// Every rule branch is pinned: blocked, water, ramp slope (real 3D normal), flat,
    /// three-level drop, two-level lone-corner identification, and area majority resolve.
    /// </summary>
    [TestFixture]
    public sealed class NavWalkableEmissionClassifierTests
    {
        private static readonly NavBuildConfig Config = new(1.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);

        private static NavTileBuilder.Vtx Vtx(int c, int r, float xM, float zM, byte level, byte water = 0, bool ramp = false, bool blocked = false, byte area = 0)
            => new(c, r, new Vector3(xM, level * Config.HeightScaleMeters, zM), water * Config.HeightScaleMeters, level, water, ramp, blocked, area);

        [Test]
        public void AnyBlockedCorner_Drops()
        {
            for (int blockedIndex = 0; blockedIndex < 3; blockedIndex++)
            {
                NavTileBuilder.Vtx a = Vtx(0, 0, 0, 0, 2);
                NavTileBuilder.Vtx b = Vtx(1, 0, 1, 0, 2);
                NavTileBuilder.Vtx c = Vtx(0, 1, 0, 1, 2);
                if (blockedIndex == 0) a = Vtx(0, 0, 0, 0, 2, blocked: true);
                if (blockedIndex == 1) b = Vtx(1, 0, 1, 0, 2, blocked: true);
                if (blockedIndex == 2) c = Vtx(0, 1, 0, 1, 2, blocked: true);

                var emission = NavTileBuilder.ClassifyTriangleEmission(a, b, c, Config);
                Assert.That(emission.Kind, Is.EqualTo(NavTileBuilder.NavWalkableEmissionKind.Drop), $"blocked corner {blockedIndex}");
            }
        }

        [Test]
        public void WaterAboveGround_OnAnyCorner_Drops()
        {
            for (int waterIndex = 0; waterIndex < 3; waterIndex++)
            {
                NavTileBuilder.Vtx a = Vtx(0, 0, 0, 0, 2);
                NavTileBuilder.Vtx b = Vtx(1, 0, 1, 0, 2);
                NavTileBuilder.Vtx c = Vtx(0, 1, 0, 1, 2);
                if (waterIndex == 0) a = Vtx(0, 0, 0, 0, 2, water: 3);
                if (waterIndex == 1) b = Vtx(1, 0, 1, 0, 2, water: 3);
                if (waterIndex == 2) c = Vtx(0, 1, 0, 1, 2, water: 3);

                var emission = NavTileBuilder.ClassifyTriangleEmission(a, b, c, Config);
                Assert.That(emission.Kind, Is.EqualTo(NavTileBuilder.NavWalkableEmissionKind.Drop), $"water corner {waterIndex}");
            }
        }

        [Test]
        public void WaterAtOrBelowGround_Emits()
        {
            // 水位恰平地面(岸线)不可行走判定:water > ground 才切,等于不切
            NavTileBuilder.Vtx a = Vtx(0, 0, 0, 0, 2, water: 2);
            NavTileBuilder.Vtx b = Vtx(1, 0, 1, 0, 2);
            NavTileBuilder.Vtx c = Vtx(0, 1, 0, 1, 2);
            var emission = NavTileBuilder.ClassifyTriangleEmission(a, b, c, Config);
            Assert.That(emission.Kind, Is.EqualTo(NavTileBuilder.NavWalkableEmissionKind.FlatFloor));
            Assert.That(emission.LowLevel, Is.EqualTo(2));
        }

        [Test]
        public void Ramp_ShallowEnough_EmitsFullRange_Steep_Drops()
        {
            // 1m 跑 1m 高 = 45°,upDot≈0.707 > 0.6 → 发射
            NavTileBuilder.Vtx a = Vtx(0, 0, 0, 0, 0, ramp: true);
            NavTileBuilder.Vtx b = Vtx(1, 0, 1, 0, 0);
            NavTileBuilder.Vtx c = Vtx(0, 1, 0, 1, 1);
            var shallow = NavTileBuilder.ClassifyTriangleEmission(a, b, c, Config);
            Assert.That(shallow.Kind, Is.EqualTo(NavTileBuilder.NavWalkableEmissionKind.RampRange));
            Assert.That(shallow.LowLevel, Is.EqualTo(0));
            Assert.That(shallow.HighLevel, Is.EqualTo(1));

            // 1m 跑 2m 高 ≈ 63°,upDot≈0.447 < 0.6 → 丢
            NavTileBuilder.Vtx steep = Vtx(0, 1, 0, 1, 2);
            var steepEmission = NavTileBuilder.ClassifyTriangleEmission(a, b, steep, Config);
            Assert.That(steepEmission.Kind, Is.EqualTo(NavTileBuilder.NavWalkableEmissionKind.Drop));
        }

        [Test]
        public void ThreeDistinctLevels_Drop()
        {
            NavTileBuilder.Vtx a = Vtx(0, 0, 0, 0, 0);
            NavTileBuilder.Vtx b = Vtx(1, 0, 1, 0, 1);
            NavTileBuilder.Vtx c = Vtx(0, 1, 0, 1, 2);
            var emission = NavTileBuilder.ClassifyTriangleEmission(a, b, c, Config);
            Assert.That(emission.Kind, Is.EqualTo(NavTileBuilder.NavWalkableEmissionKind.Drop));
        }

        [Test]
        public void TwoLevels_LoneCorner_IdentifiedForAllThreePositions()
        {
            // 平面 1×1,高角在 (1,1) 方向:level 由到高角的距离决定
            for (int loneIndex = 0; loneIndex < 3; loneIndex++)
            {
                byte[] levels = { 2, 2, 2 };
                levels[loneIndex] = 5;
                NavTileBuilder.Vtx a = Vtx(0, 0, 0, 0, levels[0]);
                NavTileBuilder.Vtx b = Vtx(1, 0, 1, 0, levels[1]);
                NavTileBuilder.Vtx c = Vtx(0, 1, 0, 1, levels[2]);

                var emission = NavTileBuilder.ClassifyTriangleEmission(a, b, c, Config);
                Assert.That(emission.Kind, Is.EqualTo(NavTileBuilder.NavWalkableEmissionKind.TwoLevelSplit), $"lone {loneIndex}");
                Assert.That(emission.LoneIndex, Is.EqualTo((byte)loneIndex));
                Assert.That(emission.LoneLevel, Is.EqualTo((byte)5));
                Assert.That(emission.PairLevel, Is.EqualTo((byte)2));
            }
        }

        [Test]
        public void AreaMajority_Wins_ThenFirstCornerFallback()
        {
            // 多数:2,2,7 → 2
            var majority = NavTileBuilder.ClassifyTriangleEmission(
                Vtx(0, 0, 0, 0, 2, area: 2), Vtx(1, 0, 1, 0, 2, area: 2), Vtx(0, 1, 0, 1, 2, area: 7), Config);
            Assert.That(majority.AreaId, Is.EqualTo(2));

            // 全异:7,3,9 → 首角 7
            var fallback = NavTileBuilder.ClassifyTriangleEmission(
                Vtx(0, 0, 0, 0, 2, area: 7), Vtx(1, 0, 1, 0, 2, area: 3), Vtx(0, 1, 0, 1, 2, area: 9), Config);
            Assert.That(fallback.AreaId, Is.EqualTo(7));
        }

        [Test]
        public void TriangleTrack_And_DirectTrack_AgreeOnCliffBoundary()
        {
            // 同一座两级断崖台地,两轨对 1200 点全采样一致(非百分比容差)
            const int cells = 96;
            var terrain = new MutableGridLogicTerrainField(cells, cells, 100, SpatialScaleDefaults.TerrainChunkCells);
            for (int r = 0; r < cells; r++)
            {
                for (int c = 0; c < cells; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            for (int r = 40; r < 56; r++)
            {
                for (int c = 40; c < 56; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(8, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            var agent = new AgentProfileConfig { Id = "Small", RadiusCm = 30, HeightCm = 180, ClearanceCm = 40, Mass = 1, Layer = 0 };
            var nav = new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 };
            var legacy = new NavBuildConfig(1.0f, 0.6f, cliffHeightThreshold: 1);
            bool okA = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, agent, nav, 0, "ground", new NavObstacleSet(), out NavTile tri, out _, out NavBakeArtifact artA);
            bool okB = RecastNavTileBaker.TryBake(terrain, 1, 1, 1, legacy, agent, nav, 0, "ground", new NavObstacleSet(), out NavTile dir, out _, out NavBakeArtifact artB, NavTerrainFeedKind.Direct);
            Assert.That(okA && okB, Is.True, $"{artA.Message} / {artB.Message}");

            var random = new Random(20260830);
            int mismatched = 0;
            const int probes = 2500;
            for (int i = 0; i < probes; i++)
            {
                float px = 30f + (float)random.NextDouble() * 36f;
                float pz = 30f + (float)random.NextDouble() * 36f;
                bool onA = IsOnMesh(tri, px, pz, out byte areaA);
                bool onB = IsOnMesh(dir, px, pz, out byte areaB);
                if (onA != onB || (onA && areaA != areaB))
                {
                    mismatched++;
                }
            }

            Assert.That(mismatched, Is.EqualTo(0), $"cliff-window mismatch {mismatched}/{probes} — tracks diverge on the shared boundary");
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
