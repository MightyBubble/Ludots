using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
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
            obstacle.SetVertex(0, 0, new Ludots.Core.Mathematics.WorldCmInt2(-40, -40));
            obstacle.SetVertex(0, 1, new Ludots.Core.Mathematics.WorldCmInt2(40, -40));
            obstacle.SetVertex(0, 2, new Ludots.Core.Mathematics.WorldCmInt2(40, 40));
            obstacle.SetVertex(0, 3, new Ludots.Core.Mathematics.WorldCmInt2(-40, 40));

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
    }
}
