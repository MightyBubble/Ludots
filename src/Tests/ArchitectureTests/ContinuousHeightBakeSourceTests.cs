using System;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class ContinuousHeightBakeSourceTests
    {
        [Test]
        public void Policy_RejectsContinuousHeightWithoutAsset()
        {
            var board = new BoardConfig
            {
                Name = "default",
                NavBakePolicy = new NavBakePolicy { HeightSource = NavBakeHeightSources.ContinuousHeightmap }
            };

            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
                () => NavBakePolicyRules.Validate(board, mapDeclaresContinuousHeightmap: false));
            Assert.That(ex!.Message, Does.Contain("continuous-heightmap"));
        }

        [Test]
        public void Policy_RejectsUnknownHeightSource()
        {
            var board = new BoardConfig
            {
                Name = "default",
                NavBakePolicy = new NavBakePolicy { HeightSource = "visual-height" }
            };

            Assert.Throws<InvalidOperationException>(
                () => NavBakePolicyRules.Validate(board, mapDeclaresContinuousHeightmap: true));
        }

        [Test]
        public void ContinuousSample_KeepsReliefFinerThanSixteenLevels()
        {
            const int extentCm = 6400;
            var terrain = new MutableGridLogicTerrainField(64, 64, 100, 64);
            for (int r = 0; r < 64; r++)
            {
                for (int c = 0; c < 64; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Ramp));
                }
            }

            var heightmap = new RampHeightmap();
            var legacy = new NavBuildConfig(1f, 0.7071f, cliffHeightThreshold: 1);
            var agent = new AgentProfileConfig
            {
                Id = "tw.agent",
                RadiusCm = 30,
                HeightCm = 200,
                ClearanceCm = 50,
                Mass = 2,
                Layer = 0
            };
            var nav = new NavMeshAgentProfileConfig
            {
                Id = "tw.agent",
                MaxClimbCm = 40,
                MaxSlopeDeg = 45,
                CellSizeCm = 50
            };
            var request = new ContinuousHeightBakeRequest
            {
                Heightmap = heightmap,
                Bounds = new WorldAabbCm(0, 0, extentCm, extentCm),
                BlockedAtOrBelowHeightCm = -100000
            };

            bool ok = RecastNavTileBaker.TryBake(
                terrain, 0, 0, 1, legacy, agent, nav, 0, "Ground", new NavObstacleSet(),
                out NavTile tile, out _, out NavBakeArtifact artifact,
                NavTerrainFeedKind.Direct, request);

            Assert.That(ok, Is.True, $"{artifact.ErrorCode}: {artifact.Message}");
            Assert.That(tile.TriangleCount, Is.GreaterThan(0));
            Assert.That(tile.Portals.Length, Is.GreaterThan(0));

            int minY = int.MaxValue;
            int maxY = int.MinValue;
            for (int i = 0; i < tile.VertexCount; i++)
            {
                int y = tile.VertexYcm[i];
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            Assert.That(maxY, Is.LessThan(1500), "the steep band must stay out of the walkable mesh");
            Assert.That(maxY - minY, Is.GreaterThan(400));
            Assert.That(TrySurfaceHeightCm(tile, 2000, 3200, out int surfaceY), Is.True, "gentle ramp must stay walkable");
            Assert.That(Math.Abs(surfaceY - 400), Is.LessThan(120), $"surface {surfaceY}cm left the 400cm sample");
        }

        private static bool TrySurfaceHeightCm(NavTile tile, int xcm, int zcm, out int heightCm)
        {
            heightCm = 0;
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int ia = tile.TriA[i];
                int ib = tile.TriB[i];
                int ic = tile.TriC[i];
                int ax = tile.VertexXcm[ia];
                int az = tile.VertexZcm[ia];
                int bx = tile.VertexXcm[ib];
                int bz = tile.VertexZcm[ib];
                int cx = tile.VertexXcm[ic];
                int cz = tile.VertexZcm[ic];
                float v0x = cx - ax;
                float v0z = cz - az;
                float v1x = bx - ax;
                float v1z = bz - az;
                float v2x = xcm - ax;
                float v2z = zcm - az;
                float dot00 = v0x * v0x + v0z * v0z;
                float dot01 = v0x * v1x + v0z * v1z;
                float dot02 = v0x * v2x + v0z * v2z;
                float dot11 = v1x * v1x + v1z * v1z;
                float dot12 = v1x * v2x + v1z * v2z;
                float denom = dot00 * dot11 - dot01 * dot01;
                if (MathF.Abs(denom) <= 1e-3f)
                {
                    continue;
                }

                float inv = 1f / denom;
                float u = (dot11 * dot02 - dot01 * dot12) * inv;
                float v = (dot00 * dot12 - dot01 * dot02) * inv;
                if (u < -0.02f || v < -0.02f || u + v > 1.02f)
                {
                    continue;
                }

                float y = tile.VertexYcm[ia] + u * (tile.VertexYcm[ic] - tile.VertexYcm[ia]) + v * (tile.VertexYcm[ib] - tile.VertexYcm[ia]);
                heightCm = (int)MathF.Round(y);
                return true;
            }

            return false;
        }

        private sealed class RampHeightmap : IContinuousHeightmap
        {
            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
            {
                heightCm = worldXCm * 0.2f;
                if (worldXCm > 4000f)
                {
                    heightCm += (worldXCm - 4000f) * 3f;
                }

                return true;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
            {
                for (int i = 0; i < worldXCm.Length; i++)
                {
                    TrySampleHeightCm(worldXCm[i], worldYCm[i], out outHeightCm[i], layerIndex);
                }

                return true;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
                => throw new NotSupportedException();

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = -1)
                => throw new NotSupportedException();
        }
    }
}
