using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Collision;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using NUnit.Framework;

namespace GasTests.Physics2D
{
    [TestFixture]
    public sealed class Physics2DFeatureTests
    {
        [SetUp]
        public void SetUp()
        {
            ShapeDataStorage2D.Clear();
        }

        [Test]
        public void CircleCircle_Overlaps_ReturnsCollision()
        {
            int aIndex = ShapeDataStorage2D.RegisterCircle(radius: 10f);
            int bIndex = ShapeDataStorage2D.RegisterCircle(radius: 10f);

            var colliderA = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = aIndex };
            var colliderB = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = bIndex };

            bool hit = CollisionAlgorithms2D.Detect(
                posA: Fix64Vec2.FromInt(0, 0),
                rotA: Rotation2D.Identity,
                colliderA: colliderA,
                posB: Fix64Vec2.FromInt(15, 0),
                rotB: Rotation2D.Identity,
                colliderB: colliderB,
                out _,
                out Fix64 penetration,
                out _);

            Assert.That(hit, Is.True);
            Assert.That(penetration > Fix64.Zero, Is.True);
        }

        [Test]
        public void BoxBox_WithRotation_Overlaps_ReturnsCollision()
        {
            int boxIndex = ShapeDataStorage2D.RegisterBox(halfWidth: 10f, halfHeight: 10f);

            var colliderA = new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = boxIndex };
            var colliderB = new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = boxIndex };

            bool hit = CollisionAlgorithms2D.Detect(
                posA: Fix64Vec2.FromInt(0, 0),
                rotA: Rotation2D.FromDegrees(45f),
                colliderA: colliderA,
                posB: Fix64Vec2.FromInt(15, 0),
                rotB: Rotation2D.FromDegrees(-15f),
                colliderB: colliderB,
                out _,
                out Fix64 penetration,
                out _);

            Assert.That(hit, Is.True);
            Assert.That(penetration > Fix64.Zero, Is.True);
        }

        [Test]
        public void PolygonCircle_CircleInside_ReturnsCollision()
        {
            Fix64Vec2[] tri =
            {
                Fix64Vec2.FromInt(0, 0),
                Fix64Vec2.FromInt(20, 0),
                Fix64Vec2.FromInt(0, 20)
            };
            int polyIndex = ShapeDataStorage2D.RegisterPolygon(tri);
            int circleIndex = ShapeDataStorage2D.RegisterCircle(radius: 3f);

            var colliderPoly = new Collider2D { Type = ColliderType2D.Polygon, ShapeDataIndex = polyIndex };
            var colliderCircle = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = circleIndex };

            bool hit = CollisionAlgorithms2D.Detect(
                posA: Fix64Vec2.FromInt(0, 0),
                rotA: Rotation2D.Identity,
                colliderA: colliderPoly,
                posB: Fix64Vec2.FromInt(5, 5),
                rotB: Rotation2D.Identity,
                colliderB: colliderCircle,
                out _,
                out Fix64 penetration,
                out _);

            Assert.That(hit, Is.True);
            Assert.That(penetration > Fix64.Zero, Is.True);
        }

        [Test]
        public void CleanupSystem_DoesNotResetActivePairs()
        {
            using var world = World.Create();
            var pairEntity = world.Create(
                new CollisionPair
                {
                    IsActive = true,
                    ContactCount = 1,
                    AccumulatedNormalImpulse0 = Fix64.FromFloat(1f),
                    AccumulatedTangentImpulse0 = Fix64.FromFloat(2f)
                },
                new ActiveCollisionPairTag()
            );

            var cleanup = new CleanupSystem2D(world);
            cleanup.Update(0.016f);

            ref var pair = ref pairEntity.Get<CollisionPair>();
            Assert.That(pair.AccumulatedNormalImpulse0, Is.EqualTo(Fix64.FromFloat(1f)));
            Assert.That(pair.AccumulatedTangentImpulse0, Is.EqualTo(Fix64.FromFloat(2f)));
        }

        [Test]
        public void AdaptiveSpatial_DropPolicyCountsDroppedPairs()
        {
            using var world = World.Create();

            int circleIndex = ShapeDataStorage2D.RegisterCircle(radius: 10f);
            var collider = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = circleIndex };
            var mass = Mass2D.FromFloat(inverseMass: 1f, inverseInertia: 0f);

            world.Create(new Position2D { Value = Fix64Vec2.FromInt(0, 0) }, collider, mass);
            world.Create(new Position2D { Value = Fix64Vec2.FromInt(5, 0) }, collider, mass);

            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, buildPhysicsWorld: build, maxCollisionPairs: 0)
            {
                OverflowPolicy = CollisionPairOverflowPolicy2D.Drop
            };

            build.Update(0.016f);
            spatial.Update(0.016f);

            Assert.That(spatial.DroppedPairsLastUpdate, Is.EqualTo(1));
        }
    }
}
