using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Authoring;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Physics2D.Ticking;
using NUnit.Framework;
using ComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace GasTests.Physics2D
{
    /// <summary>
    /// Issue #732: kinematic body contract — dynamic-layer broadphase tracking, infinite-mass
    /// solving, pose-driven API with derived velocity, sleeping wake-up, contract errors,
    /// determinism, and steady-state zero allocation.
    /// </summary>
    [TestFixture]
    public sealed class Physics2DKinematicBodyTests
    {
        private ShapeDataStorage2D _shapeStorage = null!;

        [SetUp]
        public void SetUp()
        {
            _shapeStorage = new ShapeDataStorage2D();
        }

        [OneTimeSetUp]
        public void RegisterAuthoring()
        {
            Physics2DTemplateAuthoring.RegisterRigidBody("KinematicBodyTests.RigidBody", "gas-tests");
        }

        [Test]
        public void KinematicBody_PushesDynamicBox_WhileFollowingSubmittedTrajectoryExactly()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var kinematic = CreateKinematicCircle(world, -200, 0, radiusCm: 50);
            var box = CreateDynamicBox(world, 0, 0, halfCm: 40);
            var simulation = CreateSimulation(world, poses);

            var boxStart = world.Get<Position2D>(box).Value;
            const int stepCm = 5;
            for (int step = 1; step <= 90; step++)
            {
                var target = Fix64Vec2.FromInt(-200 + step * stepCm, 0);
                poses.SetKinematicTargetPose(kinematic, target, Fix64.Zero);
                simulation.Update(1f / 60f);

                var kinematicPos = world.Get<Position2D>(kinematic).Value;
                Assert.That(kinematicPos.X.RawValue, Is.EqualTo(target.X.RawValue),
                    $"Kinematic X must match the submitted target exactly at step {step}; its trajectory must not be affected by contacts.");
                Assert.That(kinematicPos.Y.RawValue, Is.EqualTo(target.Y.RawValue),
                    $"Kinematic Y must match the submitted target exactly at step {step}.");
            }

            var kinematicVelocity = world.Get<Velocity2D>(kinematic).Linear;
            Assert.That(kinematicVelocity.X.ToFloat(), Is.EqualTo(300f).Within(0.5f),
                "Kinematic Velocity2D must be derived as Δpose/dt (5cm per 1/60s step = 300 cm/s).");

            var boxEnd = world.Get<Position2D>(box).Value;
            Assert.That((boxEnd.X - boxStart.X).ToFloat(), Is.GreaterThan(60f),
                "Dynamic box must be pushed away by the kinematic body.");

            float kinematicRightEdge = world.Get<Position2D>(kinematic).Value.X.ToFloat() + 50f;
            float boxLeftEdge = boxEnd.X.ToFloat() - 40f;
            Assert.That(boxLeftEdge, Is.GreaterThanOrEqualTo(kinematicRightEdge - 5f),
                "Dynamic box must be resolved out of the kinematic body (small residual penetration allowed).");
        }

        [Test]
        public void DynamicBody_BouncesOffKinematicWall_WhichNeverMoves()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var wall = CreateKinematicBox(world, 0, 0, halfCm: 50);
            int circleShape = _shapeStorage.RegisterCircle(30f);
            var ball = world.Create(
                Position2D.FromCm(-200, 0),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(-200, 0) },
                Rotation2D.Identity,
                new Velocity2D { Linear = Fix64Vec2.FromInt(240, 0), Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = circleShape });
            var simulation = CreateSimulation(world, poses);

            for (int step = 0; step < 120; step++)
            {
                simulation.Update(1f / 60f);
            }

            var wallPos = world.Get<Position2D>(wall).Value;
            Assert.That(wallPos.X.RawValue, Is.Zero, "Kinematic wall must have exactly zero displacement.");
            Assert.That(wallPos.Y.RawValue, Is.Zero, "Kinematic wall must have exactly zero displacement.");

            var ballPos = world.Get<Position2D>(ball).Value;
            Assert.That(ballPos.X.ToFloat() + 30f, Is.LessThanOrEqualTo(-50f + 1f),
                "Dynamic ball must be stopped/bounced by the infinite-mass kinematic wall, not tunnel through it.");

            var ballVelocity = world.Get<Velocity2D>(ball).Linear;
            Assert.That(ballVelocity.X.ToFloat(), Is.LessThanOrEqualTo(0.5f),
                "Dynamic ball must not keep driving into the kinematic wall.");
        }

        [Test]
        public void Broadphase_TracksKinematicInDynamicLayer_AndSkipsKinematicStaticAndKinematicKinematicPairs()
        {
            using var world = World.Create();
            int shape = _shapeStorage.RegisterBox(50f, 50f);

            // Overlapping trio: kinematic × static, kinematic × kinematic — no pairs allowed.
            var kinematicA = CreateKinematicBox(world, 0, 0, halfCm: 50);
            var kinematicB = CreateKinematicBox(world, 30, 0, halfCm: 50);
            world.Create(
                Position2D.FromCm(-30, 0),
                Mass2D.Static,
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape });

            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, WithPairLimit(16));
            build.Update(0f);
            spatial.Update(0f);

            Assert.That(build.DynamicRigidBodyDescriptors.Count, Is.EqualTo(2),
                "Kinematic bodies must be tracked in the dynamic broadphase layer (AABB refreshed every step).");
            Assert.That(build.StaticRigidBodyDescriptors.Count, Is.EqualTo(1),
                "Kinematic bodies must never enter the versioned static cache.");
            Assert.That(CountActivePairs(world), Is.Zero,
                "kinematic×static and kinematic×kinematic must not produce collision pairs.");

            // Adding an overlapping dynamic body produces pairs against both kinematic bodies
            // (and the ordinary dynamic×static pair), but still none among kinematic/static only.
            CreateDynamicBox(world, 15, 0, halfCm: 50);
            build.Update(0f);
            spatial.Update(0f);

            int kinematicDynamicPairs = 0;
            int otherPairs = 0;
            var pairQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            world.Query(in pairQuery, (ref CollisionPair pair) =>
            {
                if (!pair.IsActive)
                {
                    return;
                }

                var massA = world.Get<Mass2D>(pair.EntityA);
                var massB = world.Get<Mass2D>(pair.EntityB);
                Assert.That(massA.IsKinematic && massB.IsKinematic, Is.False,
                    "kinematic×kinematic pairs must never be activated.");
                Assert.That((massA.IsKinematic && massB.IsStatic) || (massA.IsStatic && massB.IsKinematic), Is.False,
                    "kinematic×static pairs must never be activated.");
                if (massA.IsKinematic || massB.IsKinematic)
                {
                    kinematicDynamicPairs++;
                }
                else
                {
                    otherPairs++;
                }
            });

            Assert.That(kinematicDynamicPairs, Is.EqualTo(2),
                "kinematic×dynamic must produce collision pairs.");
            Assert.That(otherPairs, Is.EqualTo(1),
                "The pre-existing dynamic×static pairing semantics must stay unchanged.");
        }

        [Test]
        public void SleepingDynamicBox_IsWokenByTouchingKinematic_WhileUntouchedBoxStaysAsleep()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var solverConfig = new Physics2DSolverConfig { SleepTimeSeconds = 0.05f };
            var kinematic = CreateKinematicCircle(world, -300, 0, radiusCm: 50);
            var nearBox = CreateDynamicBox(world, 0, 0, halfCm: 50);
            var farBox = CreateDynamicBox(world, 1000, 0, halfCm: 50);
            var simulation = CreateSimulation(world, poses, solverConfig);

            for (int step = 0; step < 30; step++)
            {
                simulation.Update(1f / 60f);
            }

            Assert.That(world.Has<SleepingTag>(nearBox), Is.True, "Near box must be asleep before the kinematic arrives.");
            Assert.That(world.Has<SleepingTag>(farBox), Is.True, "Far box must be asleep before the kinematic arrives.");

            var nearBoxStart = world.Get<Position2D>(nearBox).Value;
            for (int step = 1; step <= 80; step++)
            {
                var target = Fix64Vec2.FromInt(-300 + step * 4, 0);
                poses.SetKinematicTargetPose(kinematic, target, Fix64.Zero);
                simulation.Update(1f / 60f);
            }

            Assert.That(world.Has<SleepingTag>(nearBox), Is.False,
                "Sleeping dynamic box touched by the moving kinematic must wake up.");
            Assert.That((world.Get<Position2D>(nearBox).Value.X - nearBoxStart.X).ToFloat(), Is.GreaterThan(10f),
                "Woken box must be pushed by the kinematic body.");
            Assert.That(world.Has<SleepingTag>(farBox), Is.True,
                "Untouched sleeping box must stay asleep.");
        }

        [Test]
        public void ForceInputOnKinematicBody_ThrowsContractError()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var kinematic = CreateKinematicCircle(world, 0, 0, radiusCm: 50);
            world.Add(kinematic, new ForceInput2D { Force = Fix64Vec2.FromInt(10, 0) });
            var simulation = CreateSimulation(world, poses);

            Assert.That(
                () => simulation.Update(1f / 60f),
                Throws.InvalidOperationException.With.Message.Contains("contract error"),
                "Applying ForceInput2D to a kinematic body must throw instead of being silently ignored.");
        }

        [Test]
        public void SetKinematicTargetPose_Twice_PerStep_Throws()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var kinematic = CreateKinematicCircle(world, 0, 0, radiusCm: 50);

            poses.SetKinematicTargetPose(kinematic, Fix64Vec2.FromInt(10, 0), Fix64.Zero);
            Assert.That(
                () => poses.SetKinematicTargetPose(kinematic, Fix64Vec2.FromInt(20, 0), Fix64.Zero),
                Throws.InvalidOperationException.With.Message.Contains("one SetKinematicTargetPose"));
        }

        [Test]
        public void SetKinematicTargetPose_OnDynamicBody_Throws()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var box = CreateDynamicBox(world, 0, 0, halfCm: 50);
            var simulation = CreateSimulation(world, poses);

            poses.SetKinematicTargetPose(box, Fix64Vec2.FromInt(10, 0), Fix64.Zero);
            Assert.That(
                () => simulation.Update(1f / 60f),
                Throws.InvalidOperationException.With.Message.Contains("not a kinematic body"));
        }

        [Test]
        public void KinematicPoseBuffer_OverCapacitySubmission_ThrowsNamingCapacityItem()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 1);
            var kinematicA = CreateKinematicCircle(world, 0, 0, radiusCm: 50);
            var kinematicB = CreateKinematicCircle(world, 200, 0, radiusCm: 50);

            poses.SetKinematicTargetPose(kinematicA, Fix64Vec2.FromInt(10, 0), Fix64.Zero);
            Assert.That(
                () => poses.SetKinematicTargetPose(kinematicB, Fix64Vec2.FromInt(210, 0), Fix64.Zero),
                Throws.InvalidOperationException.With.Message.Contains("kinematicBodyCapacity"));
        }

        [Test]
        public void KinematicBodyCount_ExceedingCapacity_ThrowsNamingCapacityItem()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 1);
            CreateKinematicCircle(world, 0, 0, radiusCm: 50);
            CreateKinematicCircle(world, 500, 0, radiusCm: 50);
            var simulation = CreateSimulation(world, poses);

            Assert.That(
                () => simulation.Update(1f / 60f),
                Throws.InvalidOperationException.With.Message.Contains("kinematicBodyCapacity"));
        }

        [Test]
        public void KinematicPushScenario_IsBitwiseDeterministicAcrossRuns()
        {
            (long boxX, long boxY, long kinX, long kinY) RunOnce()
            {
                var shapeStorage = new ShapeDataStorage2D();
                using var world = World.Create();
                var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
                int circleShape = shapeStorage.RegisterCircle(50f);
                int boxShape = shapeStorage.RegisterBox(40f, 40f);
                var kinematic = world.Create(
                    Position2D.FromCm(-200, 0),
                    new PreviousPosition2D { Value = Fix64Vec2.FromInt(-200, 0) },
                    Rotation2D.Identity,
                    Velocity2D.Zero,
                    Mass2D.Kinematic,
                    new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = circleShape });
                var box = world.Create(
                    Position2D.FromCm(0, 6),
                    new PreviousPosition2D { Value = Fix64Vec2.FromInt(0, 6) },
                    Rotation2D.Identity,
                    Velocity2D.Zero,
                    Mass2D.FromFloat(1f, 1f),
                    new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = boxShape });
                var simulation = new Physics2DSimulationSystem(
                    world,
                    new DiscreteClock(),
                    new Physics2DTickPolicy(60, maxStepsPerFixedTick: 1),
                    new Physics2DSolverConfig(),
                    shapeStorage,
                    poses,
                    new ContactEventQueue2D(contactEventQueueCapacity: 64),
                    new Physics2DKinematicConfig
                    {
                        KinematicBodyCapacity = 8,
                        ContactEventQueueCapacity = 64,
                        ContactEventEmitterLayers = new List<string>()
                    });
                simulation.Initialize();

                for (int step = 1; step <= 120; step++)
                {
                    poses.SetKinematicTargetPose(kinematic, Fix64Vec2.FromInt(-200 + step * 4, 0), Fix64.Zero);
                    simulation.Update(1f / 60f);
                }

                var boxPos = world.Get<Position2D>(box).Value;
                var kinPos = world.Get<Position2D>(kinematic).Value;
                return (boxPos.X.RawValue, boxPos.Y.RawValue, kinPos.X.RawValue, kinPos.Y.RawValue);
            }

            var first = RunOnce();
            var second = RunOnce();
            Assert.That(second, Is.EqualTo(first),
                "Identical inputs must reproduce bitwise-identical Fix64 positions across runs.");
        }

        [Test]
        public void SteadyStateKinematicStepping_DoesNotAllocate()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);

            // Squeeze setup: the dynamic box (width 50) is pinched between the advancing
            // kinematic and a static wall whose final gap (45 cm) is narrower than the box,
            // so at least one collision pair stays continuously active during measurement.
            // Contact break/re-form cycles are intentionally excluded here: re-activating the
            // ActiveCollisionPairTag archetype after it empties re-allocates an Arch chunk,
            // a pre-existing property of the tag mechanism that #641 already ruled will be
            // replaced by fixed slots (identical allocation reproduces with dynamic-only bodies).
            int wallShape = _shapeStorage.RegisterBox(50f, 50f);
            world.Create(
                Position2D.FromCm(100, 0),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(100, 0) },
                Rotation2D.Identity,
                Velocity2D.Zero,
                Mass2D.Static,
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = wallShape });
            CreateDynamicBox(world, 20, 0, halfCm: 25);
            var kinematic = CreateKinematicBox(world, -40, 0, halfCm: 25);

            // Sleep must stay out of the measurement: a sleep transition is a legitimate
            // structural change but not part of the steady-state contract under test.
            var simulation = CreateSimulation(world, poses, new Physics2DSolverConfig { SleepTimeSeconds = 3600f });

            int step = 0;
            void StepOnce()
            {
                step++;
                int targetX = Math.Min(0, -40 + step);
                poses.SetKinematicTargetPose(kinematic, Fix64Vec2.FromInt(targetX, 0), Fix64.Zero);
                simulation.Update(1f / 60f);
                simulation.ContactEvents.DrainEvents();
            }

            for (int i = 0; i < 64; i++)
            {
                StepOnce();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                StepOnce();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero,
                "Steady-state kinematic stepping (pose drive + solve + event scan) must not allocate after warmup.");
        }

        [Test]
        public void RigidBodyAuthoring_ParsesKinematicBodyTypeStrictly()
        {
            using var world = World.Create();
            var authoringContext = CreateAuthoringContext();

            var kinematic = world.Create();
            ComponentRegistry.Apply(
                kinematic,
                "KinematicBodyTests.RigidBody",
                JsonNode.Parse("""
                {
                  "positionCm": { "x": 10, "y": 20 },
                  "bodyType": "Kinematic",
                  "shape": { "type": "Circle", "radiusCm": 50 }
                }
                """)!,
                authoringContext);

            var mass = world.Get<Mass2D>(kinematic);
            Assert.That(mass.IsKinematic, Is.True);
            Assert.That(mass.IsStatic, Is.False);
            Assert.That(mass.IsDynamic, Is.False);
            Assert.That(mass.InverseMass, Is.EqualTo(Fix64.Zero));

            var legacyDynamic = world.Create();
            ComponentRegistry.Apply(
                legacyDynamic,
                "KinematicBodyTests.RigidBody",
                JsonNode.Parse("""
                {
                  "positionCm": { "x": 0, "y": 0 },
                  "inverseMass": 1.0,
                  "shape": { "type": "Circle", "radiusCm": 50 }
                }
                """)!,
                authoringContext);
            Assert.That(world.Get<Mass2D>(legacyDynamic).IsDynamic, Is.True,
                "Templates without bodyType must keep the pre-#732 inverseMass semantics.");
        }

        [Test]
        public void RigidBodyAuthoring_RejectsContradictoryKinematicDeclarations()
        {
            using var world = World.Create();
            var authoringContext = CreateAuthoringContext();

            Assert.That(
                () => ComponentRegistry.Apply(
                    world.Create(),
                    "KinematicBodyTests.RigidBody",
                    JsonNode.Parse("""
                    {
                      "positionCm": { "x": 0, "y": 0 },
                      "bodyType": "Kinematic",
                      "inverseMass": 1.0,
                      "shape": { "type": "Circle", "radiusCm": 50 }
                    }
                    """)!,
                    authoringContext),
                Throws.InvalidOperationException.With.Message.Contains("inverseMass"),
                "Kinematic bodyType with non-zero inverseMass must fail strictly.");

            Assert.That(
                () => ComponentRegistry.Apply(
                    world.Create(),
                    "KinematicBodyTests.RigidBody",
                    JsonNode.Parse("""
                    {
                      "positionCm": { "x": 0, "y": 0 },
                      "bodyType": "Kinematic",
                      "velocityCmPerSec": { "x": 100, "y": 0 },
                      "shape": { "type": "Circle", "radiusCm": 50 }
                    }
                    """)!,
                    authoringContext),
                Throws.InvalidOperationException.With.Message.Contains("derived"),
                "Authored velocity on a kinematic body must fail: velocity is derived from target poses.");

            Assert.That(
                () => ComponentRegistry.Apply(
                    world.Create(),
                    "KinematicBodyTests.RigidBody",
                    JsonNode.Parse("""
                    {
                      "positionCm": { "x": 0, "y": 0 },
                      "bodyType": "Kinematic",
                      "forceCmPerSec2": { "x": 100, "y": 0 },
                      "shape": { "type": "Circle", "radiusCm": 50 }
                    }
                    """)!,
                    authoringContext),
                Throws.InvalidOperationException.With.Message.Contains("contract error"),
                "Authored force on a kinematic body must fail.");

            Assert.That(
                () => ComponentRegistry.Apply(
                    world.Create(),
                    "KinematicBodyTests.RigidBody",
                    JsonNode.Parse("""
                    {
                      "positionCm": { "x": 0, "y": 0 },
                      "bodyType": "Wobbly",
                      "shape": { "type": "Circle", "radiusCm": 50 }
                    }
                    """)!,
                    authoringContext),
                Throws.InvalidOperationException.With.Message.Contains("Wobbly"),
                "Unknown bodyType values must fail strictly.");
        }

        private ComponentAuthoringContext CreateAuthoringContext()
        {
            var authoringContext = new ComponentAuthoringContext();
            authoringContext.Set(ComponentAuthoringServiceKeys.Physics2DShapeStorage, _shapeStorage);
            return authoringContext;
        }

        private Physics2DSimulationSystem CreateSimulation(
            World world,
            KinematicTargetPoseBuffer2D poses,
            Physics2DSolverConfig solverConfig = null,
            ContactEventQueue2D contactEvents = null,
            List<string> allowedEmitterLayers = null)
        {
            var simulation = new Physics2DSimulationSystem(
                world,
                new DiscreteClock(),
                new Physics2DTickPolicy(60, maxStepsPerFixedTick: 1),
                solverConfig ?? new Physics2DSolverConfig(),
                _shapeStorage,
                poses,
                contactEvents ?? new ContactEventQueue2D(contactEventQueueCapacity: 256),
                new Physics2DKinematicConfig
                {
                    KinematicBodyCapacity = poses.Capacity,
                    ContactEventQueueCapacity = (contactEvents ?? new ContactEventQueue2D(256)).Capacity,
                    ContactEventEmitterLayers = allowedEmitterLayers ?? new List<string>()
                });
            simulation.Initialize();
            return simulation;
        }

        private Entity CreateKinematicCircle(World world, int xCm, int yCm, float radiusCm)
        {
            int shape = _shapeStorage.RegisterCircle(radiusCm);
            return world.Create(
                Position2D.FromCm(xCm, yCm),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(xCm, yCm) },
                Rotation2D.Identity,
                Velocity2D.Zero,
                Mass2D.Kinematic,
                new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = shape });
        }

        private Entity CreateKinematicBox(World world, int xCm, int yCm, float halfCm)
        {
            int shape = _shapeStorage.RegisterBox(halfCm, halfCm);
            return world.Create(
                Position2D.FromCm(xCm, yCm),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(xCm, yCm) },
                Rotation2D.Identity,
                Velocity2D.Zero,
                Mass2D.Kinematic,
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape });
        }

        private Entity CreateDynamicBox(World world, int xCm, int yCm, float halfCm)
        {
            int shape = _shapeStorage.RegisterBox(halfCm, halfCm);
            return world.Create(
                Position2D.FromCm(xCm, yCm),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(xCm, yCm) },
                Rotation2D.Identity,
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape });
        }

        private static int CountActivePairs(World world)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            world.Query(in query, (ref CollisionPair pair) =>
            {
                if (pair.IsActive)
                {
                    count++;
                }
            });
            return count;
        }

        private static Physics2DSolverConfig WithPairLimit(int maxCollisionPairs)
        {
            return new Physics2DSolverConfig
            {
                MaxCollisionPairs = maxCollisionPairs,
                CollisionPairInitialCapacity = 0,
                CollisionPairGrowthStep = 1
            };
        }
    }
}
