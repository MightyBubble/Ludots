using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Gameplay.Spawning;
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
        public void GeometryProfileObstacle_ProducesPieceLevelCollisionPairs()
        {
            using var world = World.Create();

            var profile = new ObstacleGeometryProfile2D();
            profile.SetBox(0, 40, 40, -220, 0);
            profile.SetBox(1, 40, 40, -110, 0);
            profile.SetBox(2, 40, 40, 0, 0);
            profile.SetBox(3, 40, 40, 110, 0);
            profile.SetBox(4, 40, 40, 220, 0);

            var obstacle = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.GeometryProfile,
                    SinkPhysicsCollider = 1,
                    SinkNavigationObstacle = 0,
                    NavRadiusCm = 0,
                    LocalOffsetXCm = 0,
                    LocalOffsetYCm = 0
                },
                profile);

            var bridge = new ManifestationObstacleBridge2DSystem(world);
            bridge.Update(0f);

            int dynamicShape = ShapeDataStorage2D.RegisterBox(320f, 120f);
            world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = dynamicShape });

            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, build, maxCollisionPairs: 32);
            var narrow = new NarrowPhaseSystem2D(world);

            build.Update(0f);
            spatial.Update(0f);
            narrow.Update(0f);

            int activeWithContact = 0;
            var q = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            world.Query(in q, (ref CollisionPair pair) =>
            {
                if (pair.ContactCount > 0)
                {
                    activeWithContact++;
                }
            });

            Assert.That(activeWithContact, Is.EqualTo(5));
            Assert.That(world.Has<CompoundCollider2D>(obstacle), Is.True);
            Assert.That(world.Has<Collider2D>(obstacle), Is.False);
        }
    }
}
