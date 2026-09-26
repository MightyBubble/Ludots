using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Navigation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavMeshAuthoredMarchContractTests
    {
        private const int CellCm = 100;
        private const int ChunkCells = 64;
        private const int TileCm = ChunkCells * CellCm;
        private const int WaterMinXcm = 5600;
        private const int WaterMaxXcm = 7200;
        private const int WaterMaxZcm = 4000;
        private const int RockXcm = 6400;
        private const int RockZcm = 5200;
        private const int RockRadiusCm = 350;

        [Test]
        public void BakedRelief_PreferMeshDetoursAroundWaterMountainAndRock()
        {
            MutableGridLogicTerrainField terrain = BuildTheatre();
            NavObstacleSet obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "rock",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = "ground",
                        Center = new NavPointCm(RockXcm, RockZcm),
                        RadiusCm = RockRadiusCm
                    }
                }
            };

            var tiles = new Dictionary<NavTileId, byte[]>();
            Bake(terrain, chunkX: 0, chunkY: 0, obstacles, tiles);
            Bake(terrain, chunkX: 1, chunkY: 0, obstacles, tiles);

            AutoPathService service = CreateService(tiles);
            var request = new PathRequest(
                1,
                default(Entity),
                PathDomain.Auto,
                "Land",
                PathEndpoint.FromWorldCm(800, 1500),
                PathEndpoint.FromWorldCm(11000, 1500),
                new PathBudget(0, 64));

            Assert.That(service.TrySolve(in request, out PathResult result), Is.True);
            Assert.That(result.Status, Is.EqualTo(PathStatus.Found), $"path status {result.Status} error {result.ErrorCode}");
            Assert.That(result.ResolvedDomain, Is.EqualTo(PathDomain.NavMesh));

            int[] xcm = new int[64];
            int[] zcm = new int[64];
            Assert.That(service.TryCopyPath(in result.Handle, xcm, zcm, out int count), Is.True);
            Assert.That(count, Is.GreaterThanOrEqualTo(2));

            bool climbedNorth = false;
            for (int i = 0; i < count; i++)
            {
                bool inWater = xcm[i] > WaterMinXcm && xcm[i] < WaterMaxXcm && zcm[i] < WaterMaxZcm;
                Assert.That(inWater, Is.False, $"waypoint {i} ({xcm[i]},{zcm[i]}) sits in the water");
                long dx = xcm[i] - RockXcm;
                long dz = zcm[i] - RockZcm;
                Assert.That(dx * dx + dz * dz, Is.GreaterThan((long)RockRadiusCm * RockRadiusCm),
                    $"waypoint {i} ({xcm[i]},{zcm[i]}) sits inside the rock");
                if (zcm[i] > WaterMaxZcm) climbedNorth = true;
            }

            Assert.That(climbedNorth, Is.True, "the march should leave the straight line and pass north of the water");

            var ontoMountain = new PathRequest(
                2,
                default(Entity),
                PathDomain.Auto,
                "Land",
                PathEndpoint.FromWorldCm(800, 1500),
                PathEndpoint.FromWorldCm(1400, 5400),
                new PathBudget(0, 32));
            Assert.That(service.TrySolve(in ontoMountain, out PathResult mountain), Is.True);
            Assert.That(mountain.Status, Is.EqualTo(PathStatus.NoPath));
        }

        [Test]
        public void BakedRelief_DirectOrderStaysAStraightSegment()
        {
            MutableGridLogicTerrainField terrain = BuildTheatre();
            var tiles = new Dictionary<NavTileId, byte[]>();
            Bake(terrain, 0, 0, new NavObstacleSet(), tiles);
            Bake(terrain, 1, 0, new NavObstacleSet(), tiles);
            AutoPathService service = CreateService(tiles);
            var request = new PathRequest(
                3,
                default(Entity),
                PathDomain.Auto,
                "Ship",
                PathEndpoint.FromWorldCm(800, 1500),
                PathEndpoint.FromWorldCm(11000, 1500),
                new PathBudget(0, 8));

            Assert.That(service.TrySolve(in request, out PathResult result), Is.True);
            Assert.That(result.Status, Is.EqualTo(PathStatus.Found));
            int[] xcm = new int[8];
            int[] zcm = new int[8];
            Assert.That(service.TryCopyPath(in result.Handle, xcm, zcm, out int count), Is.True);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(xcm[0], Is.EqualTo(800));
            Assert.That(zcm[0], Is.EqualTo(1500));
            Assert.That(xcm[1], Is.EqualTo(11000));
            Assert.That(zcm[1], Is.EqualTo(1500));
        }

        [Test]
        public void NavMeshOnlySession_RejectsGraphSelection()
        {
            var tiles = new Dictionary<NavTileId, byte[]>();
            Bake(BuildTheatre(), 0, 0, new NavObstacleSet(), tiles);
            Assert.Throws<InvalidOperationException>(() => CreateService(tiles, PathSelectionMode.PreferGraph));
        }

        private static MutableGridLogicTerrainField BuildTheatre()
        {
            var terrain = new MutableGridLogicTerrainField(ChunkCells * 2, ChunkCells, CellCm, ChunkCells);
            for (int r = 0; r < terrain.HeightCells; r++)
            {
                for (int c = 0; c < terrain.WidthCells; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            for (int r = 0; r < 40; r++)
            {
                for (int c = 56; c < 72; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(1, 3, LogicTerrainSurfaceFlags.Water));
                }
            }

            for (int r = 48; r < 60; r++)
            {
                for (int c = 8; c < 22; c++)
                {
                    terrain.SetCell(c, r, new LogicTerrainCell(8, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            return terrain;
        }

        private static void Bake(
            MutableGridLogicTerrainField terrain,
            int chunkX,
            int chunkY,
            NavObstacleSet obstacles,
            Dictionary<NavTileId, byte[]> tiles)
        {
            var legacy = new NavBuildConfig(1f, 0.6f, cliffHeightThreshold: 1);
            bool ok = RecastNavTileBaker.TryBake(
                terrain,
                chunkX,
                chunkY,
                tileVersion: 1,
                legacy,
                LandAgent(),
                new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 },
                layer: 0,
                layerId: "ground",
                obstacles,
                out NavTile tile,
                out _,
                out NavBakeArtifact artifact);
            Assert.That(ok, Is.True, artifact.Message);
            using var stream = new MemoryStream();
            NavTileBinary.Write(stream, tile);
            tiles[tile.TileId] = stream.ToArray();
        }

        private static AutoPathService CreateService(
            Dictionary<NavTileId, byte[]> tiles,
            PathSelectionMode landMode = PathSelectionMode.PreferMesh)
        {
            var store = new NavTileStore(id =>
            {
                if (!tiles.TryGetValue(id, out byte[] bytes))
                {
                    throw new InvalidOperationException($"Nav tile {id.ChunkX},{id.ChunkY} layer {id.Layer} was not baked.");
                }

                return new MemoryStream(bytes, writable: false);
            });
            var stores = new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(0, 0)] = store,
                [new NavQueryServiceKey(0, 1)] = store
            };
            var registry = new NavQueryServiceRegistry(stores, TileCm, TileCm);
            var agents = new AgentProfileRegistry(new[] { LandAgent(), ShipAgent() });
            var navProfiles = new NavMeshProfileRegistry(
                new NavMeshBakeConfig
                {
                    Profiles = new List<NavMeshAgentProfileConfig>
                    {
                        new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 },
                        new NavMeshAgentProfileConfig { Id = "Medium", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                    }
                },
                agents);
            var pathing = new PathingConfig
            {
                AgentTypes = new List<PathingAgentTypeConfig>
                {
                    new PathingAgentTypeConfig
                    {
                        Id = "Land",
                        ProfileId = "Small",
                        Selection = new PathingSelectionConfig { Mode = landMode }
                    },
                    new PathingAgentTypeConfig
                    {
                        Id = "Ship",
                        ProfileId = "Medium",
                        Selection = new PathingSelectionConfig { Mode = PathSelectionMode.Direct }
                    }
                }
            };
            return new AutoPathService(registry, navProfiles, agents, new PathStore(8, 64), pathing);
        }

        private static AgentProfileConfig LandAgent()
        {
            return new AgentProfileConfig
            {
                Id = "Small",
                RadiusCm = 30,
                HeightCm = 180,
                ClearanceCm = 40,
                Mass = 1,
                Layer = 0
            };
        }

        private static AgentProfileConfig ShipAgent()
        {
            return new AgentProfileConfig
            {
                Id = "Medium",
                RadiusCm = 40,
                HeightCm = 200,
                ClearanceCm = 40,
                Mass = 1,
                Layer = 0
            };
        }
    }
}
