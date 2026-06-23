using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Collision;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Physics2D.Ticking;
using NUnit.Framework;

namespace GasTests.Physics2D
{
    [TestFixture]
    public sealed class Physics2DFeatureTests
    {
        private ShapeDataStorage2D _shapeStorage = null!;

        [SetUp]
        public void SetUp()
        {
            _shapeStorage = new ShapeDataStorage2D();
        }

        [Test]
        public void CircleCircle_Overlaps_ReturnsCollision()
        {
            int aIndex = _shapeStorage.RegisterCircle(radius: 10f);
            int bIndex = _shapeStorage.RegisterCircle(radius: 10f);

            var colliderA = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = aIndex };
            var colliderB = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = bIndex };

            bool hit = CollisionAlgorithms2D.Detect(
                _shapeStorage,
                posA: Fix64Vec2.FromInt(0, 0),
                rotA: Rotation2D.Identity,
                colliderA: colliderA,
                posB: Fix64Vec2.FromInt(15, 0),
                rotB: Rotation2D.Identity,
                colliderB: colliderB,
                out _,
                out Fix64 penetration);

            Assert.That(hit, Is.True);
            Assert.That(penetration > Fix64.Zero, Is.True);
        }

        [Test]
        public void BoxBox_WithRotation_Overlaps_ReturnsCollision()
        {
            int boxIndex = _shapeStorage.RegisterBox(halfWidth: 10f, halfHeight: 10f);

            var colliderA = new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = boxIndex };
            var colliderB = new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = boxIndex };

            bool hit = CollisionAlgorithms2D.Detect(
                _shapeStorage,
                posA: Fix64Vec2.FromInt(0, 0),
                rotA: Rotation2D.FromDegrees(45f),
                colliderA: colliderA,
                posB: Fix64Vec2.FromInt(15, 0),
                rotB: Rotation2D.FromDegrees(-15f),
                colliderB: colliderB,
                out _,
                out Fix64 penetration);

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
            int polyIndex = _shapeStorage.RegisterPolygon(tri);
            int circleIndex = _shapeStorage.RegisterCircle(radius: 3f);

            var colliderPoly = new Collider2D { Type = ColliderType2D.Polygon, ShapeDataIndex = polyIndex };
            var colliderCircle = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = circleIndex };

            bool hit = CollisionAlgorithms2D.Detect(
                _shapeStorage,
                posA: Fix64Vec2.FromInt(0, 0),
                rotA: Rotation2D.Identity,
                colliderA: colliderPoly,
                posB: Fix64Vec2.FromInt(5, 5),
                rotB: Rotation2D.Identity,
                colliderB: colliderCircle,
                out _,
                out Fix64 penetration);

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

            int circleIndex = _shapeStorage.RegisterCircle(radius: 10f);
            var collider = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = circleIndex };
            var mass = Mass2D.FromFloat(inverseMass: 1f, inverseInertia: 0f);

            world.Create(new Position2D { Value = Fix64Vec2.FromInt(0, 0) }, collider, mass);
            world.Create(new Position2D { Value = Fix64Vec2.FromInt(5, 0) }, collider, mass);

            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(
                world,
                buildPhysicsWorld: build,
                solverConfig: new Physics2DSolverConfig
                {
                    MaxCollisionPairs = 0,
                    CollisionPairInitialCapacity = 0,
                    CollisionPairGrowthStep = 1
                })
            {
                OverflowPolicy = CollisionPairOverflowPolicy2D.Drop
            };

            build.Update(0.016f);
            spatial.Update(0.016f);

            Assert.That(spatial.DroppedPairsLastUpdate, Is.EqualTo(1));
        }

        [Test]
        public void ProductionPipeline_HotPathAllocatesZeroAfterWarmup()
        {
            using var world = World.Create();

            int circleIndex = _shapeStorage.RegisterCircle(radius: 10f);
            var collider = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = circleIndex };
            var mass = Mass2D.FromFloat(inverseMass: 1f, inverseInertia: 0f);

            world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                Velocity2D.Zero,
                mass,
                collider);
            world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(5, 0) },
                Velocity2D.Zero,
                mass,
                collider);

            var solverConfig = new Physics2DSolverConfig
            {
                DefaultBaseDamping = 1f,
                PositionCorrectionPercentage = 0f,
                SleepTimeSeconds = 10_000f,
                CollisionPairInitialCapacity = 4,
                CollisionPairGrowthStep = 4,
                MaxCollisionPairs = 4
            };
            var tickPolicy = new Physics2DTickPolicy(targetHz: 15, maxStepsPerFixedTick: 8);
            var pipeline = Physics2DPipelineFactory.CreateProduction(world, solverConfig, tickPolicy, _shapeStorage);

            for (int i = 0; i < pipeline.Systems.Length; i++)
            {
                pipeline.Systems[i].Initialize();
            }

            for (int i = 0; i < 64; i++)
            {
                StepPipeline(pipeline);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                StepPipeline(pipeline);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.LessThanOrEqualTo(64));
        }

        [Test]
        public void ProductionPipeline_NonZeroForceInputWakesSleepingDynamicBodyBeforeIntegration()
        {
            using var world = World.Create();

            int circleIndex = _shapeStorage.RegisterCircle(radius: 10f);
            var collider = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = circleIndex };
            var mass = Mass2D.FromFloat(inverseMass: 1f, inverseInertia: 0f);
            Entity body = world.Create(
                Position2D.Zero,
                new PreviousPosition2D { Value = Fix64Vec2.Zero },
                Velocity2D.Zero,
                mass,
                collider,
                new ForceInput2D { Force = Fix64Vec2.FromInt(150, 0) },
                new Motion { SleepTimer = 32 },
                new SleepingTag());

            var solverConfig = new Physics2DSolverConfig
            {
                DefaultBaseDamping = 1f,
                SleepTimeSeconds = 10_000f,
                CollisionPairInitialCapacity = 4,
                CollisionPairGrowthStep = 4,
                MaxCollisionPairs = 4
            };
            var tickPolicy = new Physics2DTickPolicy(targetHz: 15, maxStepsPerFixedTick: 8);
            var pipeline = Physics2DPipelineFactory.CreateProduction(world, solverConfig, tickPolicy, _shapeStorage);

            for (int i = 0; i < pipeline.Systems.Length; i++)
            {
                pipeline.Systems[i].Initialize();
            }

            StepPipeline(pipeline);

            Assert.That(world.Has<SleepingTag>(body), Is.False,
                "Non-zero ForceInput2D should wake sleeping dynamic bodies before IntegrationSystem2D consumes the force.");
            Assert.That(world.Get<Velocity2D>(body).Linear.X, Is.GreaterThan(Fix64.Zero));
            Assert.That(world.Get<ForceInput2D>(body).Force, Is.EqualTo(Fix64Vec2.Zero));
            Assert.That(world.Get<Motion>(body).SleepTimer, Is.EqualTo(0));
        }

        private static void StepPipeline(Physics2DPipelineDefinition pipeline)
        {
            for (int i = 0; i < pipeline.Systems.Length; i++)
            {
                pipeline.Systems[i].Update(1f / 15f);
            }
        }
    }
}
