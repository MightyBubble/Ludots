using System.Collections.Generic;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Navigation2D.Spatial;
using Ludots.Core.Physics;
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
        private ShapeDataStorage2D _shapeStorage = null!;
        private Physics2DSolverConfig _solverConfig = null!;

        [SetUp]
        public void SetUp()
        {
            _shapeStorage = new ShapeDataStorage2D();
            _solverConfig = new Physics2DSolverConfig();
        }

        [Test]
        public void ShapeStorage_FailFast_WhenMissingIndex()
        {
            Assert.Throws<KeyNotFoundException>(() => _shapeStorage.GetShapeType(123));
        }

        [Test]
        public void NavToPhysicsVelocitySync_RespectsMovementSuppression()
        {
            using var world = World.Create();
            var system = new NavToPhysicsVelocitySyncSystem(world);

            var freeActor = world.Create(
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(120, 0) });
            var suppressedActor = world.Create(
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(240, 0) },
                new MovementSuppressed2D());

            system.Update(0f);

            Assert.That(world.Get<Velocity2D>(freeActor).Linear.X.ToFloat(), Is.EqualTo(120f).Within(0.01f));
            Assert.That(world.Get<Velocity2D>(suppressedActor).Linear, Is.EqualTo(Fix64Vec2.Zero));
            Assert.That(world.Get<NavDesiredVelocity2D>(suppressedActor).ValueCmPerSec.X.ToFloat(), Is.EqualTo(240f).Within(0.01f));
        }

        [Test]
        public void NavToPhysicsVelocitySync_ClearsResidualLocomotionVelocityBeforeIntegration_WhenMovementSuppressed()
        {
            using var world = World.Create();
            var sync = new NavToPhysicsVelocitySyncSystem(world);
            var integration = new IntegrationSystem2D(world, _solverConfig);
            var actor = world.Create(
                Position2D.Zero,
                new PreviousPosition2D { Value = Fix64Vec2.Zero },
                new Velocity2D { Linear = Fix64Vec2.FromInt(120, 0), Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(240, 0) },
                new MovementSuppressed2D());

            sync.Update(0f);
            integration.Update(1f);

            Assert.That(world.Get<Velocity2D>(actor).Linear, Is.EqualTo(Fix64Vec2.Zero));
            Assert.That(world.Get<Position2D>(actor).Value, Is.EqualTo(Fix64Vec2.Zero));
            Assert.That(world.Get<NavDesiredVelocity2D>(actor).ValueCmPerSec.X.ToFloat(), Is.EqualTo(240f).Within(0.01f));
        }

        [Test]
        public void MovementSuppressedBody_StillReceivesCollisionCorrectionWithoutLocomotionDrift()
        {
            using var world = World.Create();
            int shape = _shapeStorage.RegisterBox(50f, 50f);
            var sync = new NavToPhysicsVelocitySyncSystem(world);
            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, WithPairLimit(16));
            var narrow = new NarrowPhaseSystem2D(world, _shapeStorage);
            var solver = new SolverSystem2D(world, _solverConfig);
            var impulses = new ApplyImpulsesSystem2D(world);
            var correction = new PositionCorrectionSystem2D(world, _solverConfig);
            var integration = new IntegrationSystem2D(world, _solverConfig);

            var actor = world.Create(
                Position2D.FromCm(40, 0),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(40, 0) },
                new Velocity2D { Linear = Fix64Vec2.FromInt(120, 0), Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(240, 0) },
                new MovementSuppressed2D());
            world.Create(
                Position2D.FromCm(100, 0),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(100, 0) },
                Velocity2D.Zero,
                Mass2D.Static,
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape });

            sync.Update(0f);
            build.Update(0f);
            spatial.Update(0f);
            narrow.Update(0f);
            solver.Update(0f);
            impulses.Update(0f);
            correction.Update(0f);
            Fix64 correctedX = world.Get<Position2D>(actor).Value.X;
            integration.Update(1f);

            Assert.That(world.Get<Velocity2D>(actor).Linear, Is.EqualTo(Fix64Vec2.Zero),
                "Movement suppression clears locomotion velocity but does not disable Physics2D collision correction.");
            Assert.That(world.Get<Position2D>(actor).Value.X, Is.EqualTo(correctedX),
                "The integration step must not add residual locomotion drift after collision correction.");
            Assert.That(correctedX, Is.LessThan(Fix64.FromInt(40)),
                "The suppressed dynamic body should still be pushed out of the static wall by the solver/correction path.");
        }

        [Test]
        public void NavToPhysicsVelocitySync_RestoresDesiredVelocityAfterMovementSuppressionClears()
        {
            using var world = World.Create();
            var sync = new NavToPhysicsVelocitySyncSystem(world);
            var actor = world.Create(
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(240, 0) },
                new MovementSuppressed2D());

            sync.Update(0f);
            Assert.That(world.Get<Velocity2D>(actor).Linear, Is.EqualTo(Fix64Vec2.Zero));

            world.Remove<MovementSuppressed2D>(actor);
            sync.Update(0f);

            Assert.That(world.Get<Velocity2D>(actor).Linear.X.ToFloat(), Is.EqualTo(240f).Within(0.01f),
                "Velocity should be committed from NavDesiredVelocity2D on the first sync after CC clears.");
        }

        [Test]
        public void IntegrationSystem2D_ThroughputSmoke_Integrates2kBodiesWithinBudget()
        {
            using var world = World.Create();
            var system = new IntegrationSystem2D(world, _solverConfig);
            const int bodyCount = 2_000;
            const int measuredSteps = 30;

            for (int i = 0; i < bodyCount; i++)
            {
                world.Create(
                    new Position2D { Value = Fix64Vec2.FromInt(i, i & 7) },
                    new PreviousPosition2D { Value = Fix64Vec2.FromInt(i, i & 7) },
                    new Velocity2D { Linear = Fix64Vec2.FromInt(120, 0), Angular = Fix64.Zero },
                    Mass2D.FromFloat(1f, 1f),
                    new ForceInput2D { Force = Fix64Vec2.FromInt(1, 0) },
                    new PhysicsMaterial2D
                    {
                        BaseDamping = Fix64.OneValue,
                        Friction = Fix64.Zero,
                        Restitution = Fix64.Zero
                    },
                    new AppliedDamping { TotalFieldDamping = Fix64.OneValue },
                    Rotation2D.Identity);
            }

            system.Update(1f / 60f);

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < measuredSteps; i++)
            {
                system.Update(1f / 60f);
            }
            stopwatch.Stop();

            double averageMs = stopwatch.Elapsed.TotalMilliseconds / measuredSteps;
            TestContext.WriteLine($"Integration throughput smoke: bodies={bodyCount}, avgStepMs={averageMs:F4}. 0Alloc tests are blind to TryGet/Set throughput regressions.");
            Assert.That(averageMs, Is.LessThan(10.0d));
        }

        [Test]
        public void CollisionPair_ActivatedAndHasContact_WhenBoxesOverlap()
        {
            using var world = World.Create();

            int shape = _shapeStorage.RegisterBox(0.5f, 0.5f);
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

            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, WithPairLimit(32));
            var narrow = new NarrowPhaseSystem2D(world, _shapeStorage);
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

            var dynamicShape = _shapeStorage.RegisterBox(Fix64.FromInt(25), Fix64.FromInt(25));
            world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(200, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = dynamicShape });

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, WithPairLimit(32));
            var narrow = new NarrowPhaseSystem2D(world, _shapeStorage);

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

            var dynamicShape = _shapeStorage.RegisterBox(Fix64.FromInt(25), Fix64.FromInt(25));
            world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(0, 200) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = dynamicShape });

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, WithPairLimit(16));
            var narrow = new NarrowPhaseSystem2D(world, _shapeStorage);

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

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            using var steering = new Navigation2DSteeringSystem2D(world, runtime, _shapeStorage);

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

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            using var steering = new Navigation2DSteeringSystem2D(world, runtime, _shapeStorage);

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

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);

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

            var dynamicShape = _shapeStorage.RegisterBox(Fix64.FromInt(15), Fix64.FromInt(15));
            world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(300, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = dynamicShape });

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, WithPairLimit(16));
            var narrow = new NarrowPhaseSystem2D(world, _shapeStorage);

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

            int shape = _shapeStorage.RegisterBox(0.5f, 0.5f);
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

            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, WithPairLimit(8));

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

            int shape = _shapeStorage.RegisterBox(100f, 100f);
            var obstacle = world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(500, 0) },
                Mass2D.Static,
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape });

            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, WithPairLimit(8));

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

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);
            var spatial = new AdaptiveSpatialSystem2D(world, build, WithPairLimit(16));

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
