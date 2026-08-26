using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Physics2D.Navigation;
using Ludots.Core.TransportNetwork;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class TransportNavObstacleSinkTests
    {
        [Test]
        public void Corridor_ExpandsEqualWidthPolygon_FromWidthCmOnly()
        {
            var asset = new TransportNetworkAsset
            {
                Id = "sink.corridor",
                SampleStepCm = 100,
                DefaultVisualWidthMeters = 2f,
                Nodes =
                {
                    new TransportNetworkNode { Id = "a", Xcm = 0, Ycm = 0, Tags = new List<string>() },
                    new TransportNetworkNode { Id = "b", Xcm = 1000, Ycm = 0, Tags = new List<string>() }
                },
                Segments =
                {
                    new TransportNetworkSegment
                    {
                        Id = "river",
                        Points =
                        {
                            TransportNetworkPoint.FromNode("a"),
                            TransportNetworkPoint.FromNode("b")
                        },
                        Tags = { "Transport.Area.River", "Nav.CarveGround" },
                        WidthCm = 200,
                        VisualWidthMeters = 99f
                    }
                }
            };

            var config = new TransportNavObstacleSinkConfig
            {
                Rules =
                {
                    new TransportNavObstacleSinkRule
                    {
                        Id = "carve_rivers",
                        RequiredTagsAll = { "Transport.Area.River", "Nav.CarveGround" },
                        ForbiddenTagsAny = { },
                        LayerId = "Ground",
                        WidthSource = "widthCm",
                        MinWidthCm = 1,
                        SampleStepCm = 100,
                        CapEnds = false,
                        Geometry = TransportNavObstacleGeometryKind.Corridor
                    }
                }
            };

            NavObstacleSet set = TransportNavObstacleSink.Build(asset, config);
            Assert.That(set.Obstacles, Has.Count.EqualTo(1));
            NavObstacle obstacle = set.Obstacles[0];
            Assert.That(obstacle.Id, Is.EqualTo("transport:sink.corridor:river:carve_rivers"));
            Assert.That(obstacle.Kind, Is.EqualTo(NavObstacleKind.Polygon));
            Assert.That(obstacle.LayerId, Is.EqualTo("Ground"));
            Assert.That(obstacle.Points.Count, Is.GreaterThanOrEqualTo(4));

            int minZ = obstacle.Points.Min(p => p.Zcm);
            int maxZ = obstacle.Points.Max(p => p.Zcm);
            Assert.That(maxZ - minZ, Is.EqualTo(200));
            Assert.That(
                NavObstacleGeometry.IsTriangleBlockedByObstacles(500, 0, 520, 10, 480, -10, set, "Ground"),
                Is.True);
            Assert.That(
                NavObstacleGeometry.IsTriangleBlockedByObstacles(500, 500, 520, 510, 480, 490, set, "Ground"),
                Is.False);
        }

        [Test]
        public void FilledRing_UsesAuthoredPolygon_AndIgnoresZeroWidth()
        {
            var asset = new TransportNetworkAsset
            {
                Id = "sink.lake",
                SampleStepCm = 100,
                DefaultVisualWidthMeters = 2f,
                Nodes =
                {
                    new TransportNetworkNode { Id = "n0", Xcm = 0, Ycm = 0, Tags = new List<string>() },
                    new TransportNetworkNode { Id = "n1", Xcm = 400, Ycm = 0, Tags = new List<string>() },
                    new TransportNetworkNode { Id = "n2", Xcm = 400, Ycm = 300, Tags = new List<string>() },
                    new TransportNetworkNode { Id = "n3", Xcm = 0, Ycm = 300, Tags = new List<string>() },
                    new TransportNetworkNode { Id = "n4", Xcm = 0, Ycm = 0, Tags = new List<string>() }
                },
                Segments =
                {
                    new TransportNetworkSegment
                    {
                        Id = "lake",
                        Points =
                        {
                            TransportNetworkPoint.FromNode("n0"),
                            TransportNetworkPoint.FromNode("n1"),
                            TransportNetworkPoint.FromNode("n2"),
                            TransportNetworkPoint.FromNode("n3"),
                            TransportNetworkPoint.FromNode("n4")
                        },
                        Tags = { "Transport.Area.Lake", "Nav.CarveGround" },
                        WidthCm = 0
                    }
                }
            };

            var config = new TransportNavObstacleSinkConfig
            {
                Rules =
                {
                    new TransportNavObstacleSinkRule
                    {
                        Id = "carve_lakes",
                        RequiredTagsAll = { "Transport.Area.Lake", "Nav.CarveGround" },
                        ForbiddenTagsAny = { },
                        LayerId = "Ground",
                        WidthSource = "widthCm",
                        MinWidthCm = 0,
                        SampleStepCm = 0,
                        CapEnds = false,
                        Geometry = TransportNavObstacleGeometryKind.FilledRing
                    }
                }
            };

            NavObstacleSet set = TransportNavObstacleSink.Build(asset, config);
            Assert.That(set.Obstacles, Has.Count.EqualTo(1));
            Assert.That(set.Obstacles[0].Points, Has.Count.EqualTo(4));
            Assert.That(
                NavObstacleGeometry.IsTriangleBlockedByObstacles(200, 150, 210, 160, 190, 140, set, "Ground"),
                Is.True);
        }

        [Test]
        public void WidthSource_RejectsVisualWidthMeters()
        {
            var rule = new TransportNavObstacleSinkRule
            {
                Id = "bad",
                RequiredTagsAll = { },
                ForbiddenTagsAny = { },
                LayerId = "Ground",
                WidthSource = "visualWidthMeters",
                MinWidthCm = 0,
                SampleStepCm = 100,
                Geometry = TransportNavObstacleGeometryKind.Corridor
            };

            Assert.Throws<InvalidOperationException>(() => rule.Validate(0));
        }

        [Test]
        public void EastAsia_CatalogMerge_CoversYangtzeYellowTaihu()
        {
            string root = FindRepoRoot();
            NavObstacleSet obstacles = NavObstacleAuthoringCatalog.BuildForMap(
                root,
                "east_asia_visual_heightmap",
                "EastAsiaNavMeshDebugMod");

            string[] expectedIds =
            {
                "transport:east_asia.waterways:yangtze:carve_rivers",
                "transport:east_asia.waterways:yellow_river:carve_rivers",
                "transport:east_asia.waterways:taihu:carve_lakes"
            };

            Assert.That(obstacles.Obstacles.Select(o => o.Id), Is.SupersetOf(expectedIds));
            foreach (string id in expectedIds)
            {
                NavObstacle obstacle = obstacles.Obstacles.Single(o => o.Id == id);
                Assert.That(obstacle.Enabled, Is.True);
                Assert.That(obstacle.Kind, Is.EqualTo(NavObstacleKind.Polygon));
                Assert.That(obstacle.LayerId, Is.EqualTo("Ground"));
                Assert.That(obstacle.Points.Count, Is.GreaterThanOrEqualTo(3));
            }

            Assert.That(
                NavObstacleGeometry.IsTriangleBlockedByObstacles(
                    95021051, -38092782,
                    95031051, -38082782,
                    95011051, -38102782,
                    obstacles,
                    "Ground"),
                Is.True);

            Assert.That(
                NavObstacleGeometry.IsTriangleBlockedByObstacles(
                    40629961, -49399974,
                    40639961, -49389974,
                    40619961, -49409974,
                    obstacles,
                    "Ground"),
                Is.True);
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "showcase.registry.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }
    }
}
