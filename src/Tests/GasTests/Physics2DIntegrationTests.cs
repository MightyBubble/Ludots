using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Navigation2D.Spatial;
using Ludots.Physics.Broadphase;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using NUnit.Framework;

namespace GasTests
{
    [TestFixture]
    public sealed class Physics2DIntegrationTests
    {
        [SetUp]
        public void SetUp()
        {
            ShapeDataStorage2D.Clear();
        }

        [Test]
        public void ShapeStorage_FailFast_WhenMissingIndex()
        {
            Assert.Throws<KeyNotFoundException>(() => ShapeDataStorage2D.GetShapeType(123));
        }

        [Test]
        public void CollisionPair_ActivatedAndHasContact_WhenBoxesOverlap()
        {
            using var world = World.Create();

            int shape = ShapeDataStorage2D.RegisterBox(0.5f, 0.5f);
            world.Create(
                new Position2D { Value = Fix64Vec2.FromFloat(0f, 0f) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape }
            );
            world.Create(
                new Position2D { Value = Fix64Vec2.FromFloat(0.25f, 0f) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape }
            );

            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, build, maxCollisionPairs: 32);
            var narrow = new NarrowPhaseSystem2D(world);
            var cleanup = new CleanupSystem2D(world);

            build.Update(0f);
            spatial.Update(0f);
            narrow.Update(0f);

            int activeWithContact = 0;
            var q = new QueryDescription().WithAll<CollisionPair>();
            world.Query(in q, (ref CollisionPair pair) =>
            {
                if (pair.IsActive && pair.ContactCount > 0 && pair.Penetration > Fix64.Zero)
                {
                    activeWithContact++;
                }
            });

            Assert.That(activeWithContact, Is.GreaterThanOrEqualTo(1));

            cleanup.Update(0f);

            int activeAfterCleanup = 0;
            world.Query(in q, (ref CollisionPair pair) =>
            {
                if (pair.IsActive) activeAfterCleanup++;
            });

            Assert.That(activeAfterCleanup, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void CompoundObstaclePieces_ExpandToShapeSlotsAndCollideWithoutChildEntities()
        {
            using var world = World.Create();

            var obstacle = new CompoundObstacle2D
            {
                SinkPhysicsCollider = 1,
                SinkNavigationObstacle = 0
            };
            obstacle.SetPiece(
                0,
                ManifestationObstacleShape2D.Box,
                radiusCm: 0,
                halfWidthCm: 50,
                halfHeightCm: 50,
                localOffsetXCm: -200,
                localOffsetYCm: 0,
                navRadiusCm: 0);
            obstacle.SetPiece(
                1,
                ManifestationObstacleShape2D.Box,
                radiusCm: 0,
                halfWidthCm: 50,
                halfHeightCm: 50,
                localOffsetXCm: 200,
                localOffsetYCm: 0,
                navRadiusCm: 0);

            var compoundEntity = world.Create(
                WorldPositionCm.FromCm(0, 0),
                obstacle);

            var dynamicShape = ShapeDataStorage2D.RegisterBox(Fix64.FromInt(25), Fix64.FromInt(25));
            world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(200, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = dynamicShape });

            var bridge = new ManifestationObstacleBridge2DSystem(world);
            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, build, maxCollisionPairs: 32);
            var narrow = new NarrowPhaseSystem2D(world);

            bridge.Update(0f);
            build.Update(0f);
            spatial.Update(0f);
            narrow.Update(0f);

            Assert.That(world.Has<CompoundObstacle2DState>(compoundEntity), Is.True);
            Assert.That(build.RigidBodyDescriptors.Count, Is.EqualTo(3));
            Assert.That(build.Entities.Count, Is.EqualTo(3));
            Assert.That(build.ShapeSlots.Count, Is.EqualTo(3));

            int activeWithContact = 0;
            var q = new QueryDescription().WithAll<CollisionPair>();
            world.Query(in q, (ref CollisionPair pair) =>
            {
                if (pair.IsActive && pair.ContactCount > 0 && pair.Penetration > Fix64.Zero)
                {
                    activeWithContact++;
                    Assert.That(pair.EntityA == compoundEntity || pair.EntityB == compoundEntity, Is.True);
                    byte compoundSlot = pair.EntityA == compoundEntity ? pair.ShapeSlotA : pair.ShapeSlotB;
                    Assert.That(compoundSlot, Is.EqualTo(1));
                }
            });

            Assert.That(activeWithContact, Is.EqualTo(1));
        }

        [Test]
        public void CompoundObstaclePieces_RotateLocalOffsetsForCollision()
        {
            using var world = World.Create();

            var obstacle = new CompoundObstacle2D
            {
                SinkPhysicsCollider = 1,
                SinkNavigationObstacle = 0
            };
            obstacle.SetPiece(
                0,
                ManifestationObstacleShape2D.Box,
                radiusCm: 0,
                halfWidthCm: 50,
                halfHeightCm: 50,
                localOffsetXCm: 200,
                localOffsetYCm: 0,
                navRadiusCm: 0);

            var compoundEntity = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new FacingDirection { AngleRad = MathF.PI / 2f },
                obstacle);

            var dynamicShape = ShapeDataStorage2D.RegisterBox(Fix64.FromInt(25), Fix64.FromInt(25));
            world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(0, 200) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = dynamicShape });

            var bridge = new ManifestationObstacleBridge2DSystem(world);
            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, build, maxCollisionPairs: 16);
            var narrow = new NarrowPhaseSystem2D(world);

            bridge.Update(0f);
            build.Update(0f);
            spatial.Update(0f);
            narrow.Update(0f);

            int activeWithContact = 0;
            var q = new QueryDescription().WithAll<CollisionPair>();
            world.Query(in q, (ref CollisionPair pair) =>
            {
                if (pair.IsActive && pair.ContactCount > 0 && pair.Penetration > Fix64.Zero)
                {
                    activeWithContact++;
                    Assert.That(pair.EntityA == compoundEntity || pair.EntityB == compoundEntity, Is.True);
                }
            });

            Assert.That(activeWithContact, Is.EqualTo(1));
        }

        [Test]
        public void CompoundObstacleFlowDiscomfortHalo_UsesRotatedPieceCenter()
        {
            using var world = World.Create();

            var obstacle = new CompoundObstacle2D
            {
                SinkPhysicsCollider = 0,
                SinkNavigationObstacle = 1
            };
            obstacle.SetPiece(
                0,
                ManifestationObstacleShape2D.Circle,
                radiusCm: 20,
                halfWidthCm: 0,
                halfHeightCm: 0,
                localOffsetXCm: 200,
                localOffsetYCm: 0,
                navRadiusCm: 20);

            world.Create(
                WorldPositionCm.FromCm(0, 0),
                new FacingDirection { AngleRad = MathF.PI / 2f },
                obstacle);
            world.Create(
                new NavAgent2D(),
                new Position2D { Value = Fix64Vec2.FromInt(1000, 1000) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(100),
                    MaxAccelCmPerSec2 = Fix64.FromInt(100),
                    RadiusCm = Fix64.FromInt(20),
                    NeighborDistCm = Fix64.FromInt(100),
                    TimeHorizonSec = Fix64.OneValue,
                    MaxNeighbors = 0
                });

            var config = new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = 8,
                FlowIterationsPerTick = 0,
                FlowCrowd = new Navigation2DFlowCrowdConfig
                {
                    Enabled = true,
                    Discomfort = new Navigation2DFlowCrowdDiscomfortConfig
                    {
                        Enabled = true,
                        ObstacleHaloRadiusCm = 120,
                        ObstacleHaloValue = 10f,
                        ObstacleHaloEdgeValue = 0f,
                    },
                },
            };
            using var runtime = new Navigation2DRuntime(config, gridCellSizeCm: 100, loadedChunks: null)
            {
                FlowEnabled = true,
            };
            runtime.Surface.GetOrCreateTile(Nav2DKeyPacking.PackInt2(0, 0));

            var bridge = new ManifestationObstacleBridge2DSystem(world);
            using var steering = new Navigation2DSteeringSystem2D(world, runtime);

            bridge.Update(0f);
            steering.Update(0.016f);

            Assert.That(runtime.Surface.TryGetDiscomfortCell(1, 2, out float rotatedPieceHalo), Is.True);
            Assert.That(rotatedPieceHalo, Is.GreaterThan(0f));
        }

        [Test]
        public void SingleObstacleFlowDiscomfortHalo_UsesRotatedShapeCenter()
        {
            using var world = World.Create();

            world.Create(
                WorldPositionCm.FromCm(0, 0),
                new FacingDirection { AngleRad = MathF.PI / 2f },
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkPhysicsCollider = 0,
                    SinkNavigationObstacle = 1,
                    RadiusCm = 20,
                    NavRadiusCm = 20,
                    LocalOffsetXCm = 200,
                    LocalOffsetYCm = 0,
                });
            world.Create(
                new NavAgent2D(),
                new Position2D { Value = Fix64Vec2.FromInt(1000, 1000) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(100),
                    MaxAccelCmPerSec2 = Fix64.FromInt(100),
                    RadiusCm = Fix64.FromInt(20),
                    NeighborDistCm = Fix64.FromInt(100),
                    TimeHorizonSec = Fix64.OneValue,
                    MaxNeighbors = 0
                });

            var config = new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = 8,
                FlowIterationsPerTick = 0,
                FlowCrowd = new Navigation2DFlowCrowdConfig
                {
                    Enabled = true,
                    Discomfort = new Navigation2DFlowCrowdDiscomfortConfig
                    {
                        Enabled = true,
                        ObstacleHaloRadiusCm = 120,
                        ObstacleHaloValue = 10f,
                        ObstacleHaloEdgeValue = 0f,
                    },
                },
            };
            using var runtime = new Navigation2DRuntime(config, gridCellSizeCm: 100, loadedChunks: null)
            {
                FlowEnabled = true,
            };
            runtime.Surface.GetOrCreateTile(Nav2DKeyPacking.PackInt2(0, 0));

            var bridge = new ManifestationObstacleBridge2DSystem(world);
            using var steering = new Navigation2DSteeringSystem2D(world, runtime);

            bridge.Update(0f);
            steering.Update(0.016f);

            Assert.That(runtime.Surface.TryGetDiscomfortCell(1, 2, out float rotatedShapeHalo), Is.True);
            Assert.That(rotatedShapeHalo, Is.GreaterThan(0f));
        }

        [Test]
        public void CompoundObstacle_DisablingPhysicsSink_RemovesPiecesFromBroadphase()
        {
            using var world = World.Create();

            var obstacle = new CompoundObstacle2D
            {
                SinkPhysicsCollider = 1,
                SinkNavigationObstacle = 0
            };
            obstacle.SetPiece(
                0,
                ManifestationObstacleShape2D.Circle,
                radiusCm: 60,
                halfWidthCm: 0,
                halfHeightCm: 0,
                localOffsetXCm: 0,
                localOffsetYCm: 0,
                navRadiusCm: 0);

            var compoundEntity = world.Create(
                WorldPositionCm.FromCm(0, 0),
                obstacle);

            var bridge = new ManifestationObstacleBridge2DSystem(world);
            var build = new BuildPhysicsWorldSystem2D(world);

            bridge.Update(0f);
            build.Update(0f);

            Assert.That(world.Has<Mass2D>(compoundEntity), Is.True);
            Assert.That(world.Get<CompoundObstacle2DState>(compoundEntity).SinkPhysicsCollider, Is.EqualTo(1));
            Assert.That(build.RigidBodyDescriptors.Count, Is.EqualTo(1));

            obstacle.SinkPhysicsCollider = 0;
            world.Set(compoundEntity, obstacle);
            world.Add(compoundEntity, new ManifestationObstacleBridge2DDirty());

            bridge.Update(0f);
            build.Update(0f);

            Assert.That(world.Get<CompoundObstacle2DState>(compoundEntity).SinkPhysicsCollider, Is.EqualTo(0));
            Assert.That(build.RigidBodyDescriptors.Count, Is.EqualTo(0));
        }

        [Test]
        public void CompoundPolygonObstacle_UsesPieceLocalOffsetForCollision()
        {
            using var world = World.Create();

            var obstacle = new CompoundObstacle2D
            {
                SinkPhysicsCollider = 1,
                SinkNavigationObstacle = 0
            };
            obstacle.SetPiece(
                0,
                ManifestationObstacleShape2D.Polygon,
                radiusCm: 0,
                halfWidthCm: 0,
                halfHeightCm: 0,
                localOffsetXCm: 300,
                localOffsetYCm: 0,
                navRadiusCm: 0);
            obstacle.SetPolygonVertexCount(0, 4);
            obstacle.SetVertex(0, 0, new WorldCmInt2(-40, -40));
            obstacle.SetVertex(0, 1, new WorldCmInt2(40, -40));
            obstacle.SetVertex(0, 2, new WorldCmInt2(40, 40));
            obstacle.SetVertex(0, 3, new WorldCmInt2(-40, 40));

            var compoundEntity = world.Create(
                WorldPositionCm.FromCm(0, 0),
                obstacle);

            var dynamicShape = ShapeDataStorage2D.RegisterBox(Fix64.FromInt(15), Fix64.FromInt(15));
            world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(300, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = dynamicShape });

            var bridge = new ManifestationObstacleBridge2DSystem(world);
            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, build, maxCollisionPairs: 16);
            var narrow = new NarrowPhaseSystem2D(world);

            bridge.Update(0f);
            build.Update(0f);
            spatial.Update(0f);
            narrow.Update(0f);

            int activeWithContact = 0;
            var q = new QueryDescription().WithAll<CollisionPair>();
            world.Query(in q, (ref CollisionPair pair) =>
            {
                if (pair.IsActive && pair.ContactCount > 0 && pair.Penetration > Fix64.Zero)
                {
                    activeWithContact++;
                    Assert.That(pair.EntityA == compoundEntity || pair.EntityB == compoundEntity, Is.True);
                }
            });

            Assert.That(activeWithContact, Is.EqualTo(1));
        }

        [Test]
        public void SpatialQueryAabb_ReturnsExpectedBodyIndices()
        {
            using var world = World.Create();

            int shape = ShapeDataStorage2D.RegisterBox(0.5f, 0.5f);
            world.Create(
                new Position2D { Value = Fix64Vec2.FromFloat(0f, 0f) },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape }
            );
            world.Create(
                new Position2D { Value = Fix64Vec2.FromFloat(10f, 0f) },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape }
            );

            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, build, maxCollisionPairs: 8);

            build.Update(0f);
            spatial.Update(0f);

            var results = new List<int>();
            var query = new Aabb
            {
                Min = Fix64Vec2.FromFloat(-1f, -1f),
                Max = Fix64Vec2.FromFloat(1f, 1f)
            };

            spatial.CurrentStrategy.QueryAABB(in query, results);
            Assert.That(results.Count, Is.EqualTo(1));
            var e = build.Entities[results[0]];
            Assert.That(world.TryGet(e, out Position2D pos), Is.True);
            Assert.That(pos.Value.X.ToFloat(), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void StaticBodies_AreCachedAcrossSteadyStateBuilds()
        {
            using var world = World.Create();

            int shape = ShapeDataStorage2D.RegisterBox(100f, 100f);
            var obstacle = world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(500, 0) },
                Mass2D.Static,
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape });

            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, build, maxCollisionPairs: 8);

            build.Update(0f);
            spatial.Update(0f);

            Assert.That(build.StaticRigidBodyDescriptors.Count, Is.EqualTo(1));
            Assert.That(build.DynamicRigidBodyDescriptors.Count, Is.EqualTo(0));
            Assert.That(build.DirtyStaticBodyCountLastUpdate, Is.EqualTo(1));
            int staticVersion = build.StaticBodyVersion;

            var results = new List<int>();
            var query = new Aabb
            {
                Min = Fix64Vec2.FromInt(450, -50),
                Max = Fix64Vec2.FromInt(550, 50)
            };
            spatial.CurrentStrategy.QueryAABB(in query, results);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(build.ResolveBodyEntity(results[0]), Is.EqualTo(obstacle));

            build.Update(0f);
            spatial.Update(0f);

            Assert.That(build.StaticBodyVersion, Is.EqualTo(staticVersion));
            Assert.That(build.DirtyStaticBodyCountLastUpdate, Is.EqualTo(0));
            Assert.That(build.StaticRigidBodyDescriptors.Count, Is.EqualTo(1));
        }

        [Test]
        public void CompoundStaticBodies_AreCachedAcrossSteadyStateBuilds()
        {
            using var world = World.Create();

            var obstacle = new CompoundObstacle2D
            {
                SinkPhysicsCollider = 1,
                SinkNavigationObstacle = 0
            };
            obstacle.SetPiece(
                0,
                ManifestationObstacleShape2D.Box,
                radiusCm: 0,
                halfWidthCm: 40,
                halfHeightCm: 40,
                localOffsetXCm: -120,
                localOffsetYCm: 0,
                navRadiusCm: 0);
            obstacle.SetPiece(
                1,
                ManifestationObstacleShape2D.Box,
                radiusCm: 0,
                halfWidthCm: 40,
                halfHeightCm: 40,
                localOffsetXCm: 120,
                localOffsetYCm: 0,
                navRadiusCm: 0);

            var obstacleEntity = world.Create(
                WorldPositionCm.FromCm(0, 0),
                obstacle);

            var bridge = new ManifestationObstacleBridge2DSystem(world);
            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, build, maxCollisionPairs: 16);

            bridge.Update(0f);
            build.Update(0f);
            spatial.Update(0f);

            Assert.That(build.StaticRigidBodyDescriptors.Count, Is.EqualTo(2));
            Assert.That(build.DynamicRigidBodyDescriptors.Count, Is.EqualTo(0));
            Assert.That(build.DirtyStaticBodyCountLastUpdate, Is.EqualTo(1));
            int staticVersion = build.StaticBodyVersion;

            var results = new List<int>();
            var query = new Aabb
            {
                Min = Fix64Vec2.FromInt(80, -40),
                Max = Fix64Vec2.FromInt(160, 40)
            };
            spatial.CurrentStrategy.QueryAABB(in query, results);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(build.ResolveBodyEntity(results[0]), Is.EqualTo(obstacleEntity));

            build.Update(0f);
            spatial.Update(0f);

            Assert.That(build.StaticBodyVersion, Is.EqualTo(staticVersion));
            Assert.That(build.DirtyStaticBodyCountLastUpdate, Is.EqualTo(0));
            Assert.That(build.StaticRigidBodyDescriptors.Count, Is.EqualTo(2));
        }
    }
}
