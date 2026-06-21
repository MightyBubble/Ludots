using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Config;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Collision;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Physics.Broadphase;
using Ludots.Physics.Broadphase.Strategies;
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

        [Test]
        public void UniformGrid_MatchesSortAndSweepPotentialPairsAndQueries()
        {
            var dynamicBodies = new[]
            {
                Body(index: 0, x: 0, y: 0, half: 10, isStatic: false),
                Body(index: 1, x: 15, y: 0, half: 10, isStatic: false),
                Body(index: 2, x: 1000, y: 1000, half: 10, isStatic: false)
            };
            var staticBodies = new[]
            {
                Body(index: 0, x: 18, y: 0, half: 8, isStatic: true),
                Body(index: 1, x: -200, y: -200, half: 10, isStatic: true)
            };

            using var sweep = new SortAndSweepStrategy();
            using var grid = new UniformGridStrategy(cellSizeCm: 32);
            sweep.Build(dynamicBodies, staticBodies, rebuildStatic: true);
            grid.Build(dynamicBodies, staticBodies, rebuildStatic: true);

            var sweepPairs = new List<(int, int)>();
            var gridPairs = new List<(int, int)>();
            sweep.QueryPotentialCollisions(sweepPairs);
            grid.QueryPotentialCollisions(gridPairs);

            Assert.That(NormalizePairs(gridPairs), Is.EqualTo(NormalizePairs(sweepPairs)));

            var query = new Aabb
            {
                Min = Fix64Vec2.FromInt(8, -12),
                Max = Fix64Vec2.FromInt(28, 12)
            };
            var sweepResults = new List<int>();
            var gridResults = new List<int>();
            sweep.QueryAABB(in query, sweepResults);
            grid.QueryAABB(in query, gridResults);

            sweepResults.Sort();
            gridResults.Sort();
            Assert.That(gridResults, Is.EqualTo(sweepResults));
        }

        [Test]
        public void AdaptiveSpatial_AppliesBroadphasePolicy()
        {
            using var world = World.Create();

            int circleIndex = ShapeDataStorage2D.RegisterCircle(radius: 10f);
            var collider = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = circleIndex };
            var mass = Mass2D.FromFloat(inverseMass: 1f, inverseInertia: 0f);

            world.Create(new Position2D { Value = Fix64Vec2.FromInt(0, 0) }, collider, mass);
            world.Create(new Position2D { Value = Fix64Vec2.FromInt(5, 0) }, collider, mass);

            var build = new BuildPhysicsWorldSystem2D(world);
            var spatial = new AdaptiveSpatialSystem2D(world, build, maxCollisionPairs: 8);
            var policy = new Physics2DBroadphasePolicy(new Physics2DBroadphaseConfig
            {
                Strategy = Physics2DBroadphaseStrategyKind.UniformGrid,
                CellSizeCm = 64
            });

            build.Update(0.016f);
            spatial.ApplyBroadphasePolicy(policy);
            spatial.Update(0.016f);

            Assert.That(spatial.CurrentStrategy, Is.TypeOf<UniformGridStrategy>());
            Assert.That(spatial.CurrentStrategyKind, Is.EqualTo(Physics2DBroadphaseStrategyKind.UniformGrid));
            Assert.That(spatial.CurrentCellSizeCm, Is.EqualTo(64));
        }

        [Test]
        public void BuildPhysicsWorld_RebuildsStaticCacheAfterTrackedStaticEntityDestroyed()
        {
            using var world = World.Create();

            int shapeIndex = ShapeDataStorage2D.RegisterBox(halfWidth: 10f, halfHeight: 10f);
            Entity staticBody = world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shapeIndex },
                Mass2D.Static);

            var build = new BuildPhysicsWorldSystem2D(world);
            build.Update(0.016f);

            Assert.That(build.StaticRigidBodyDescriptors.Count, Is.EqualTo(1));
            int staticVersion = build.StaticBodyVersion;

            world.Destroy(staticBody);
            build.Update(0.016f);

            Assert.That(build.StaticRigidBodyDescriptors.Count, Is.EqualTo(0));
            Assert.That(build.StaticBodyVersion, Is.Not.EqualTo(staticVersion));
            Assert.That(build.DirtyStaticBodyCountLastUpdate, Is.EqualTo(1));
        }

        private static RigidBodyDesc Body(int index, int x, int y, int half, bool isStatic)
        {
            return new RigidBodyDesc
            {
                Index = index,
                EntityIndex = index + 1,
                IsStatic = isStatic,
                BoundingBox = new Aabb
                {
                    Min = Fix64Vec2.FromInt(x - half, y - half),
                    Max = Fix64Vec2.FromInt(x + half, y + half)
                }
            };
        }

        private static List<(int, int)> NormalizePairs(List<(int, int)> pairs)
        {
            var normalized = new List<(int, int)>(pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                var (a, b) = pairs[i];
                if (b < a)
                {
                    (a, b) = (b, a);
                }

                normalized.Add((a, b));
            }

            normalized.Sort(static (left, right) =>
            {
                int a = left.Item1.CompareTo(right.Item1);
                return a != 0 ? a : left.Item2.CompareTo(right.Item2);
            });
            return normalized;
        }
    }
}
