using System.Collections.Generic;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Physics;
using Ludots.Physics.Broadphase;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace GasTests
{
    [TestFixture]
    public sealed class Physics2DIntegrationTests
    {
        private ShapeDataStorage2D _shapeStorage = null!;
        private Physics2DSolverConfig _solverConfig = null!;
        private const string GroundNavLayerId = "Ground";

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
        public void DynamicCircles_ResolveOverlapAcrossPhysicsSteps()
        {
            using var world = World.Create();

            int shape = _shapeStorage.RegisterCircle(50f);
            var a = world.Create(
                Position2D.FromCm(0, 0),
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = shape }
            );
            var b = world.Create(
                Position2D.FromCm(70, 0),
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = shape }
            );

            var simulation = CreateProductionPhysicsSimulation(world, physicsHz: 60);
            for (int i = 0; i < 12; i++)
            {
                simulation.Update(1f / 60f);
            }

            var posA = world.Get<Position2D>(a).Value;
            var posB = world.Get<Position2D>(b).Value;
            float distanceCm = (posB - posA).Length().ToFloat();
            float clearanceCm = distanceCm - 100f;

            Assert.That(clearanceCm, Is.GreaterThanOrEqualTo(-1f));
        }

        [Test]
        public void DrivenCircleCrowd_MaintainsEffectiveSeparation()
        {
            using var world = World.Create();

            int shape = _shapeStorage.RegisterCircle(46f);
            var entities = new[]
            {
                world.Create(Position2D.FromCm(-70, -70), Velocity2D.Zero, Mass2D.FromFloat(1f, 1f), new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = shape }),
                world.Create(Position2D.FromCm(-70, 70), Velocity2D.Zero, Mass2D.FromFloat(1f, 1f), new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = shape }),
                world.Create(Position2D.FromCm(70, -70), Velocity2D.Zero, Mass2D.FromFloat(1f, 1f), new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = shape }),
                world.Create(Position2D.FromCm(70, 70), Velocity2D.Zero, Mass2D.FromFloat(1f, 1f), new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = shape }),
            };

            var simulation = CreateProductionPhysicsSimulation(world, physicsHz: 60);

            for (int i = 0; i < 120; i++)
            {
                for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
                {
                    var entity = entities[entityIndex];
                    var position = world.Get<Position2D>(entity).Value;
                    world.Set(entity, new Velocity2D
                    {
                        Linear = (Fix64Vec2.Zero - position).Normalized() * Fix64.FromInt(330),
                        Angular = Fix64.Zero
                    });
                }

                simulation.Update(1f / 60f);
            }

            float minimumClearanceCm = float.MaxValue;
            for (int i = 0; i < entities.Length; i++)
            {
                var a = world.Get<Position2D>(entities[i]).Value;
                for (int j = i + 1; j < entities.Length; j++)
                {
                    var b = world.Get<Position2D>(entities[j]).Value;
                    float clearanceCm = (b - a).Length().ToFloat() - 92f;
                    minimumClearanceCm = MathF.Min(minimumClearanceCm, clearanceCm);
                }
            }

            Assert.That(minimumClearanceCm, Is.GreaterThanOrEqualTo(-6f));
        }

        private Physics2DSimulationSystem CreateProductionPhysicsSimulation(World world, int physicsHz)
        {
            var simulation = new Physics2DSimulationSystem(
                world,
                new DiscreteClock(),
                new Physics2DTickPolicy(physicsHz, maxStepsPerFixedTick: 1),
                _solverConfig,
                _shapeStorage);
            simulation.Initialize();
            return simulation;
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

        [Test]
        public void RuntimeNavMeshObstacleDirtySystem_UsesBridgeStateAsStructuralDirtySource()
        {
            using var world = World.Create();
            var engine = CreateRuntimeNavMeshDirtyEngine(
                world,
                out NavObstacleSet obstacles,
                out RuntimeIncrementalNavMeshRebuildQueue queue,
                out NavTileStore store);

            var obstacleEntity = world.Create(
                WorldPositionCm.FromCm(150, 150),
                new RuntimeNavMeshStructuralObstacle(),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkNavigationObstacle = 1,
                    RadiusCm = 45,
                    NavRadiusCm = 45
                });
            var nonStructuralEntity = world.Create(
                WorldPositionCm.FromCm(120, 250),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkNavigationObstacle = 1,
                    RadiusCm = 35,
                    NavRadiusCm = 35
                });

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var dirtySystem = new RuntimeNavMeshObstacleDirtySystem(engine);
            bridge.Update(0f);
            dirtySystem.Update(0f);

            Assert.That(obstacles.Obstacles.Count, Is.EqualTo(1));
            Assert.That(queue.PendingTileCount, Is.EqualTo(0));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile firstTile), Is.True);

            world.Set(nonStructuralEntity, WorldPositionCm.FromCm(170, 250));
            world.Add(nonStructuralEntity, new ManifestationObstacleBridge2DDirty());
            bridge.Update(0f);
            dirtySystem.Update(0f);

            Assert.That(obstacles.Obstacles.Count, Is.EqualTo(1));
            Assert.That(store.Revision, Is.EqualTo(1u));

            world.Set(obstacleEntity, WorldPositionCm.FromCm(250, 150));
            world.Add(obstacleEntity, new ManifestationObstacleBridge2DDirty());
            bridge.Update(0f);
            dirtySystem.Update(0f);

            Assert.That(obstacles.Obstacles.Count, Is.EqualTo(1));
            Assert.That(store.Revision, Is.EqualTo(2u));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile secondTile), Is.True);
            Assert.That(secondTile.Checksum, Is.Not.EqualTo(firstTile.Checksum));
        }

        [Test]
        public void RuntimeNavMeshObstacleDirtySystem_ClearsTrackedStateWhenRuntimeModeStops()
        {
            using var world = World.Create();
            var engine = CreateRuntimeNavMeshDirtyEngine(world, out _, out RuntimeIncrementalNavMeshRebuildQueue queue, out NavTileStore store);
            var obstacleEntity = world.Create(
                WorldPositionCm.FromCm(150, 150),
                new RuntimeNavMeshStructuralObstacle(),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkNavigationObstacle = 1,
                    RadiusCm = 45,
                    NavRadiusCm = 45
                });

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var dirtySystem = new RuntimeNavMeshObstacleDirtySystem(engine);
            bridge.Update(0f);
            dirtySystem.Update(0f);

            Assert.That(queue.PendingTileCount, Is.EqualTo(0));
            Assert.That(store.Revision, Is.EqualTo(1u));

            engine.RemoveService(CoreServiceKeys.NavMeshBakeConfig);
            dirtySystem.Update(0f);

            world.Destroy(obstacleEntity);
            engine.SetService(CoreServiceKeys.NavMeshBakeConfig, CreateRuntimeNavBakeConfig());
            dirtySystem.Update(0f);

            Assert.That(queue.PendingTileCount, Is.EqualTo(0));
            Assert.That(store.Revision, Is.EqualTo(1u));
        }

        private static NavMeshBakeConfig CreateRuntimeNavBakeConfig()
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeRuntimeIncremental,
                Algorithm = NavBakeNames.AlgorithmCdt,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = GroundNavLayerId, Layer = 0 }
                },
                Areas = new List<NavAreaCostConfig>(),
                RuntimeIncremental = new NavRuntimeIncrementalConfig
                {
                    TileBudgetPerFixedTick = 1,
                    IncludeNeighborTiles = false,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1
                }
            };
        }

        private static AgentProfileRegistry CreateNavAgentProfiles()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "Small",
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
        }

        private GameEngine CreateRuntimeNavMeshDirtyEngine(
            World world,
            out NavObstacleSet obstacles,
            out RuntimeIncrementalNavMeshRebuildQueue queue,
            out NavTileStore store)
        {
            var engine = new GameEngine();
            engine.SetService(CoreServiceKeys.World, world);
            engine.SetService(CoreServiceKeys.Physics2DShapeStorage, _shapeStorage);
            typeof(GameEngine).GetProperty(nameof(GameEngine.World))!
                .SetValue(engine, world);

            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavMeshBakeConfig bakeConfig = CreateRuntimeNavBakeConfig();
            AgentProfileRegistry agentProfiles = CreateNavAgentProfiles();
            var navProfiles = new NavMeshProfileRegistry(bakeConfig, agentProfiles);
            obstacles = new NavObstacleSet();
            store = new NavTileStore(_ => throw new InvalidOperationException("Runtime navmesh dirty test publishes before disk load."));
            var registry = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(0, 0)] = store
            });
            queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
                new NavBakeContext
                {
                    MapId = "physics_runtime_navmesh_dirty_contract",
                    SourceUri = "Core:Maps/physics_runtime_navmesh_dirty_contract.runtime-navmesh",
                    Terrain = terrain,
                    Obstacles = obstacles,
                    Config = bakeConfig,
                    AgentProfiles = agentProfiles,
                    Targets = new[] { new NavBakeTileCoord(0, 0) },
                    BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                    TileVersion = 3,
                    Mode = NavBakeMode.RuntimeIncremental,
                    Algorithm = NavBakeAlgorithmKind.Cdt,
                    Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
                },
                registry,
                navProfiles);
            engine.SetService(CoreServiceKeys.NavMeshBakeConfig, bakeConfig);
            engine.SetService(CoreServiceKeys.RuntimeNavMeshObstacles, obstacles);
            engine.SetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, queue);
            return engine;
        }
    }
}
