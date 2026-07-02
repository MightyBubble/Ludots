using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class RuntimeManifestationBridgeTests
    {
        private ShapeDataStorage2D _shapeStorage = null!;
        private ComponentAuthoringContext _authoringContext = null!;

        [SetUp]
        public void SetUp()
        {
            _shapeStorage = new ShapeDataStorage2D();
            _authoringContext = new ComponentAuthoringContext();
            _authoringContext.Set(ComponentAuthoringServiceKeys.Physics2DShapeStorage, _shapeStorage);
        }

        [TearDown]
        public void TearDown()
        {
            Ludots.Core.Config.ComponentRegistry.UnregisterSource("RuntimeManifestationBridgeTests.ModA");
            Ludots.Core.Config.ComponentRegistry.UnregisterSource("RuntimeManifestationBridgeTests.ModB");
        }

        [Test]
        public void ManifestationObstacleBridge2D_BoxIntent_CreatesPhysicsAndMassNavigationFlowProjection()
        {
            using var world = World.Create();
            var system = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var entity = world.Create(
                WorldPositionCm.FromCm(1200, 3400),
                new FacingDirection { AngleRad = MathF.PI / 2f },
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Box,
                    SinkPhysicsCollider = 1,
                    SinkNavigationObstacle = 1,
                    HalfWidthCm = 240,
                    HalfHeightCm = 30,
                });

            system.Update(0f);

            That(world.Has<Position2D>(entity), Is.True);
            That(world.Get<Position2D>(entity).Value, Is.EqualTo(Fix64Vec2.FromInt(1200, 3400)));
            That(world.Has<Rotation2D>(entity), Is.True);
            That(world.Get<Rotation2D>(entity).Value.ToFloat(), Is.EqualTo(MathF.PI / 2f).Within(0.0001f));

            var collider = world.Get<Collider2D>(entity);
            That(collider.Type, Is.EqualTo(ColliderType2D.Box));
            That(_shapeStorage.TryGetBox(collider.ShapeDataIndex, out var box), Is.True);
            That(box.HalfWidth, Is.EqualTo(Fix64.FromInt(240)));
            That(box.HalfHeight, Is.EqualTo(Fix64.FromInt(30)));

            That(world.Has<Mass2D>(entity), Is.True);
            That(world.Get<Mass2D>(entity).IsStatic, Is.True);
            That(world.Has<Velocity2D>(entity), Is.True);
            That(world.Get<Velocity2D>(entity).Linear, Is.EqualTo(Fix64Vec2.Zero));

            Fix64 expectedRadius = Fix64Math.Sqrt(
                Fix64.FromInt(240 * 240) +
                Fix64.FromInt(30 * 30));

            That(world.Has<MassNavigationFlowObstacleProjection>(entity), Is.True);
            var projection = world.Get<MassNavigationFlowObstacleProjection>(entity);
            That(projection.PieceCount, Is.EqualTo(1));
            That(projection.GetShape(0), Is.EqualTo(ManifestationObstacleShape2D.Box));
            That(projection.GetRadiusCm(0), Is.EqualTo(expectedRadius.RoundToInt()));
        }

        [Test]
        public void ManifestationObstacleBridge2D_SingleSinkTogglesRemoveDerivedState()
        {
            using var world = World.Create();
            var system = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var entity = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkPhysicsCollider = 1,
                    SinkNavigationObstacle = 1,
                    RadiusCm = 50,
                    NavRadiusCm = 50,
                });

            system.Update(0f);

            That(world.Has<Collider2D>(entity), Is.True);
            That(world.Has<Mass2D>(entity), Is.True);
            That(world.Has<Velocity2D>(entity), Is.True);
            That(world.Has<MassNavigationFlowObstacleProjection>(entity), Is.True);

            var intent = world.Get<ManifestationObstacleIntent2D>(entity);
            intent.SinkPhysicsCollider = 0;
            intent.SinkNavigationObstacle = 0;
            world.Set(entity, intent);
            world.Add(entity, new ManifestationObstacleBridge2DDirty());

            system.Update(0f);

            That(world.Has<Collider2D>(entity), Is.False);
            That(world.Has<Mass2D>(entity), Is.False);
            That(world.Has<Velocity2D>(entity), Is.False);
            That(world.Has<MassNavigationFlowObstacleProjection>(entity), Is.False);
        }

        [Test]
        public void ManifestationObstacleBridge2D_DisabledInitialSinksDoNotRemoveAuthoredRuntimeComponents()
        {
            using var world = World.Create();
            int authoredShapeIndex = _shapeStorage.RegisterBox(Fix64.FromInt(10), Fix64.FromInt(20));
            var authoredVelocity = Velocity2D.FromCmPerSec(7f, 8f, 0.5f);
            var authoredMass = Mass2D.FromFloat(1f, 2f);
            var authoredProjection = new MassNavigationFlowObstacleProjection();
            authoredProjection.SetPiece(0, ManifestationObstacleShape2D.Box, 12, 34, 56);

            var entity = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = authoredShapeIndex },
                authoredMass,
                authoredVelocity,
                authoredProjection,
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkPhysicsCollider = 0,
                    SinkNavigationObstacle = 0,
                    RadiusCm = 50,
                    NavRadiusCm = 50,
                });

            var system = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            system.Update(0f);

            That(world.Get<Collider2D>(entity).ShapeDataIndex, Is.EqualTo(authoredShapeIndex));
            That(world.Get<Mass2D>(entity).InverseMass, Is.EqualTo(authoredMass.InverseMass));
            That(world.Get<Mass2D>(entity).InverseInertia, Is.EqualTo(authoredMass.InverseInertia));
            That(world.Get<Velocity2D>(entity).Linear, Is.EqualTo(authoredVelocity.Linear));
            That(world.Get<Velocity2D>(entity).Angular, Is.EqualTo(authoredVelocity.Angular));
            That(world.Has<MassNavigationFlowObstacleProjection>(entity), Is.False);
        }

        [Test]
        public void ComponentRegistry_ParsesPolygonManifestationObstacle_AndBridgeCreatesPolygonObstacle()
        {
            using var world = World.Create();
            var entity = world.Create(WorldPositionCm.FromCm(600, 900));

            Ludots.Core.Config.ComponentRegistry.Apply(
                entity,
                "ManifestationObstacleIntent2D",
                JsonNode.Parse("""
                {
                  "shape": "Polygon",
                  "sinkPhysicsCollider": true,
                  "sinkNavigationObstacle": true,
                  "navRadiusCm": 160,
                  "localOffsetCm": { "x": 0, "y": 0 }
                }
                """)!,
                _authoringContext);
            Ludots.Core.Config.ComponentRegistry.Apply(
                entity,
                "ManifestationObstaclePolygon2D",
                JsonNode.Parse("""
                {
                  "vertices": [
                    { "x": -120, "y": -80 },
                    { "x": 140, "y": -20 },
                    { "x": 40, "y": 160 }
                  ]
                }
                """)!,
                _authoringContext);

            var system = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            system.Update(0f);

            That(world.Has<ManifestationObstaclePolygon2D>(entity), Is.True);

            var collider = world.Get<Collider2D>(entity);
            That(collider.Type, Is.EqualTo(ColliderType2D.Polygon));
            That(_shapeStorage.TryGetPolygon(collider.ShapeDataIndex, out var polygon), Is.True);
            That(polygon.VertexCount, Is.EqualTo(3));

            var projection = world.Get<MassNavigationFlowObstacleProjection>(entity);
            That(projection.PieceCount, Is.EqualTo(1));
            That(projection.GetShape(0), Is.EqualTo(ManifestationObstacleShape2D.Polygon));
            That(projection.GetRadiusCm(0), Is.EqualTo(160));
        }

        [Test]
        public void ComponentRegistry_ParsesCompoundObstacle_AndBridgeCreatesCompoundStateOnSameEntity()
        {
            using var world = World.Create();
            var entity = world.Create(WorldPositionCm.FromCm(1000, 2000));

            Ludots.Core.Config.ComponentRegistry.Apply(
                entity,
                "CompoundObstacle2D",
                JsonNode.Parse("""
                {
                  "sinkPhysicsCollider": true,
                  "sinkNavigationObstacle": true,
                  "pieces": [
                    {
                      "shape": "Box",
                      "navRadiusCm": 120,
                      "halfWidthCm": 100,
                      "halfHeightCm": 40,
                      "localOffsetCm": { "x": -120, "y": 0 }
                    },
                    {
                      "shape": "Polygon",
                      "navRadiusCm": 80,
                      "localOffsetCm": { "x": 160, "y": 20 },
                      "vertices": [
                        { "x": -40, "y": -30 },
                        { "x": 50, "y": -20 },
                        { "x": 20, "y": 60 }
                      ]
                    }
                  ]
                }
                """)!,
                _authoringContext);

            var system = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            system.Update(0f);

            That(world.Has<CompoundObstacle2D>(entity), Is.True);
            That(world.Has<CompoundObstacle2DState>(entity), Is.True);
            That(world.Has<Collider2D>(entity), Is.False, "Compound obstacles should not collapse into the single-collider component.");
            That(world.Has<Mass2D>(entity), Is.True);
            That(world.Has<Velocity2D>(entity), Is.True);
            That(world.Has<MassNavigationFlowObstacleProjection>(entity), Is.True);

            var state = world.Get<CompoundObstacle2DState>(entity);
            That(state.PieceCount, Is.EqualTo(2));
            That(state.GetShape(0), Is.EqualTo(ManifestationObstacleShape2D.Box));
            That(state.GetShape(1), Is.EqualTo(ManifestationObstacleShape2D.Polygon));
            That(_shapeStorage.TryGetBox(state.GetShapeDataIndex(0), out var box), Is.True);
            That(box.HalfWidth, Is.EqualTo(Fix64.FromInt(100)));
            That(box.HalfHeight, Is.EqualTo(Fix64.FromInt(40)));
            That(_shapeStorage.TryGetPolygon(state.GetShapeDataIndex(1), out var polygon), Is.True);
            That(polygon.VertexCount, Is.EqualTo(3));
            That(polygon.LocalOffset, Is.EqualTo(Fix64Vec2.FromInt(160, 20)));

            var projection = world.Get<MassNavigationFlowObstacleProjection>(entity);
            That(projection.PieceCount, Is.EqualTo(2));
            That(projection.GetShape(0), Is.EqualTo(ManifestationObstacleShape2D.Box));
            That(projection.GetShape(1), Is.EqualTo(ManifestationObstacleShape2D.Polygon));
            That(projection.GetRadiusCm(0), Is.EqualTo(120));
            That(projection.GetRadiusCm(1), Is.EqualTo(80));
        }

        [Test]
        public void ManifestationObstacleBridge2D_DisablingPhysicsSink_DirtiesAndRemovesStaticBody()
        {
            using var world = World.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(1200, 900),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Box,
                    SinkPhysicsCollider = 1,
                    SinkNavigationObstacle = 0,
                    HalfWidthCm = 90,
                    HalfHeightCm = 20
                });

            var bridge = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);
            var build = new BuildPhysicsWorldSystem2D(world, _shapeStorage);

            bridge.Update(0f);
            build.Update(0f);

            That(world.Has<Physics2DStaticBodyState>(entity), Is.True);
            That(world.Has<Physics2DStaticBodyDirty>(entity), Is.False);

            world.Set(entity, new ManifestationObstacleIntent2D
            {
                Shape = ManifestationObstacleShape2D.Box,
                SinkPhysicsCollider = 0,
                SinkNavigationObstacle = 0,
                HalfWidthCm = 90,
                HalfHeightCm = 20
            });
            world.Add(entity, new ManifestationObstacleBridge2DDirty());

            bridge.Update(0f);

            That(world.Has<Collider2D>(entity), Is.False);
            That(world.Has<Physics2DStaticBodyDirty>(entity), Is.True);

            build.Update(0f);

            That(world.Has<Physics2DStaticBodyState>(entity), Is.False);
            That(world.Has<Physics2DStaticBodyDirty>(entity), Is.False);
            That(build.StaticRigidBodyDescriptors.Count, Is.EqualTo(0));
        }

        [Test]
        public void ManifestationObstacleBridge2D_RejectsSingleAndCompoundObstacleOnSameEntity()
        {
            using var world = World.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkPhysicsCollider = 1,
                    SinkNavigationObstacle = 1,
                    RadiusCm = 10,
                    NavRadiusCm = 10
                });

            var compound = new CompoundObstacle2D
            {
                SinkPhysicsCollider = 1,
                SinkNavigationObstacle = 1
            };
            compound.SetPiece(
                0,
                ManifestationObstacleShape2D.Circle,
                radiusCm: 10,
                halfWidthCm: 0,
                halfHeightCm: 0,
                localOffsetXCm: 0,
                localOffsetYCm: 0,
                navRadiusCm: 10);
            world.Add(entity, compound);

            var system = new ManifestationObstacleBridge2DSystem(world, _shapeStorage);

            InvalidOperationException ex = Throws<InvalidOperationException>(() => system.Update(0f))!;
            That(ex.Message, Does.Contain("must not author both ManifestationObstacleIntent2D and CompoundObstacle2D"));
        }

        [Test]
        public void ComponentRegistry_RequiresExactSpatialBoundsKind()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            Ludots.Core.Config.ComponentRegistry.Apply(
                entity,
                "SpatialBounds",
                JsonNode.Parse("""{ "kind": "Footprint2D", "localCenterCm": { "x": 0, "y": 0 }, "localCenterYCm": 0 }""")!);

            That(world.Get<SpatialBounds>(entity).Kind, Is.EqualTo(SpatialBoundsKind.Footprint2D));

            Entity invalid = world.Create();
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(
                    invalid,
                    "SpatialBounds",
                    JsonNode.Parse("""{ "kind": "footprint2d" }""")!,
                    _authoringContext))!;
            That(ex.Message, Does.Contain("Unsupported SpatialBounds kind"));
        }

        [Test]
        public void ComponentRegistry_RejectsCaseAliasesUnknownComponentsAndExtraFields()
        {
            using var world = World.Create();

            AssertRejects(
                world,
                "WorldPositionCm",
                """{ "value": { "X": 1, "Y": 2 } }""",
                "unsupported property 'value'");
            AssertRejects(
                world,
                "WorldPositionCm",
                """{ "Value": { "x": 1, "Y": 2 } }""",
                "unsupported property 'x'");
            AssertRejects(
                world,
                "SpatialBounds",
                """{ "Kind": "Footprint2D" }""",
                "unsupported property 'Kind'");
            AssertRejects(
                world,
                "SpatialFootprint2D",
                """{ "vertices": [ { "X": 0, "y": 0 }, { "x": 1, "y": 0 }, { "x": 0, "y": 1 } ] }""",
                "requires explicit 'x'");
            AssertRejects(
                world,
                "ManifestationObstacleIntent2D",
                """{ "Shape": "Circle", "sinkPhysicsCollider": true, "sinkNavigationObstacle": true, "navRadiusCm": 10 }""",
                "unsupported property 'Shape'");
            AssertRejects(
                world,
                "SelectionSelectableState",
                """{ "isEnabled": true }""",
                "unsupported property 'isEnabled'");
            AssertRejects(
                world,
                "SelectionSelectableState",
                """true""",
                "requires an object payload");
            AssertRejects(
                world,
                "ManifestationObstacleIntent2D",
                """{ "shape": "Circle", "sinkPhysicsCollider": 1, "sinkNavigationObstacle": true, "navRadiusCm": 10 }""",
                "requires a boolean value");
            AssertRejects(
                world,
                "ManifestationObstacleIntent2D",
                """{ "shape": "Circle", "sinkPhysicsCollider": false, "sinkNavigationObstacle": false, "radiusCm": 10, "navRadiusCm": 10, "localOffsetCm": { "x": 0, "y": 0 } }""",
                "requires at least one sink intent");
            AssertRejects(
                world,
                "CompoundObstacle2D",
                """{ "sinkPhysicsCollider": false, "sinkNavigationObstacle": false, "pieces": [ { "shape": "Circle", "radiusCm": 10, "navRadiusCm": 10, "localOffsetCm": { "x": 0, "y": 0 } } ] }""",
                "requires at least one sink intent");
            AssertRejects(
                world,
                "CompoundObstacle2D",
                """{ "sinkPhysicsCollider": true, "sinkNavigationObstacle": true, "pieces": [] }""",
                "pieces count must be between");
            AssertRejects(
                world,
                "CompoundObstacle2D",
                """
                {
                  "sinkPhysicsCollider": true,
                  "sinkNavigationObstacle": true,
                  "pieces": [
                    {
                      "shape": "Polygon",
                      "navRadiusCm": 20,
                      "localOffsetCm": { "x": 0, "y": 0 },
                      "vertices": [ { "x": 0, "y": 0 }, { "x": 10, "y": 0 } ]
                    }
                  ]
                }
                """,
                "vertices count must be between");
            AssertRejects(
                world,
                "CompoundObstacle2D",
                """
                {
                  "sinkPhysicsCollider": true,
                  "sinkNavigationObstacle": true,
                  "pieces": [
                    {
                      "shape": "Box",
                      "navRadiusCm": 20,
                      "halfWidthCm": 10,
                      "halfHeightCm": 10,
                      "localOffsetCm": { "x": 0, "y": 0 },
                      "localOffsetXCm": 0,
                      "localOffsetYCm": 0
                    }
                  ]
                }
                """,
                "must author either localOffsetCm or localOffsetXCm/localOffsetYCm");
            AssertRejects(
                world,
                "AbilityFormSetRef",
                """
                "legacy_form_set"
                """,
                "requires an object payload");
            AssertRejects(
                world,
                "AttributeBuffer",
                """{ "base": null }""",
                "AttributeBuffer.base requires an object payload");
            AssertRejects(
                world,
                "AttributeBuffer",
                """{ "base": { "Health": null } }""",
                "requires a non-null numeric value");
            AssertRejects(
                world,
                "AbilityStateBuffer",
                """{ "abilityIds": [1, 2, 3, 4, 5, 6, 7, 8, 9] }""",
                "accepts at most");
            AssertRejects(
                world,
                "OrderBuffer",
                """{ "ignored": true }""",
                "does not accept authored fields");
            AssertRejects(
                world,
                "UnknownComponent",
                """{}""",
                "Unknown component");
        }

        [Test]
        public void ComponentRegistry_GenericComponentsRequireExactPascalCaseFields()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            Ludots.Core.Config.ComponentRegistry.Apply(
                entity,
                "Team",
                JsonNode.Parse("""{ "Id": 7 }""")!);

            That(world.Get<Team>(entity).Id, Is.EqualTo(7));

            AssertRejects(
                world,
                "Team",
                """{ "id": 7 }""",
                "failed strict deserialization");
        }

        [Test]
        public void ComponentRegistry_UnregisterSource_RemovesOnlyThatModRegistrations()
        {
            const string modId = "RuntimeManifestationBridgeTests.ModAuthoring";
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(modId);

            Ludots.Core.Config.ComponentRegistry.Register<RuntimeManifestationBridgeTestTag>(
                "RuntimeManifestationBridgeTestTag",
                modId);
            That(Ludots.Core.Config.ComponentRegistry.TryGetComponentType(
                "RuntimeManifestationBridgeTestTag",
                out _),
                Is.True);

            That(Ludots.Core.Config.ComponentRegistry.UnregisterSource(modId), Is.EqualTo(1));
            That(Ludots.Core.Config.ComponentRegistry.TryGetComponentType(
                "RuntimeManifestationBridgeTestTag",
                out _),
                Is.False);

            using var world = World.Create();
            Entity entity = world.Create();
            DoesNotThrow(() => Ludots.Core.Config.ComponentRegistry.Apply(
                entity,
                "Team",
                JsonNode.Parse("""{ "Id": 3 }""")!));
            That(world.Get<Team>(entity).Id, Is.EqualTo(3));
        }

        [Test]
        public void ComponentRegistry_DuplicateSameTypedDefinitionAcrossMods_IsNoOp()
        {
            const string modA = "RuntimeManifestationBridgeTests.ModA";
            const string modB = "RuntimeManifestationBridgeTests.ModB";
            const string name = "RuntimeManifestationBridgeTestSameTypedTag";
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(modA);
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(modB);

            Ludots.Core.Config.ComponentRegistry.Register<RuntimeManifestationBridgeTestTag>(name, modA);

            DoesNotThrow(() =>
                Ludots.Core.Config.ComponentRegistry.Register<RuntimeManifestationBridgeTestTag>(name, modB));
            That(Ludots.Core.Config.ComponentRegistry.TryGetComponentType(name, out _), Is.True);
        }

        [Test]
        public void ComponentRegistry_DuplicateSameSetterDefinitionAcrossMods_IsNoOp()
        {
            const string modA = "RuntimeManifestationBridgeTests.ModA";
            const string modB = "RuntimeManifestationBridgeTests.ModB";
            const string name = "RuntimeManifestationBridgeTestSameSetterTag";
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(modA);
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(modB);
            Ludots.Core.Config.ComponentSetter setter = SetRuntimeManifestationBridgeTestTag;

            Ludots.Core.Config.ComponentRegistry.Register(name, setter, modA);

            DoesNotThrow(() =>
                Ludots.Core.Config.ComponentRegistry.Register(name, setter, modB));
        }

        [Test]
        public void ComponentRegistry_DuplicateDifferentTypedDefinitionAcrossMods_Throws()
        {
            const string modA = "RuntimeManifestationBridgeTests.ModA";
            const string modB = "RuntimeManifestationBridgeTests.ModB";
            const string name = "RuntimeManifestationBridgeTestDifferentTypedTag";
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(modA);
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(modB);

            Ludots.Core.Config.ComponentRegistry.Register<RuntimeManifestationBridgeTestTag>(name, modA);

            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Register<RuntimeManifestationBridgeDifferentTestTag>(name, modB))!;
            That(ex.Message, Does.Contain("already registered"));
        }

        [Test]
        public void ComponentRegistry_DuplicateDifferentSetterDefinitionAcrossMods_Throws()
        {
            const string modA = "RuntimeManifestationBridgeTests.ModA";
            const string modB = "RuntimeManifestationBridgeTests.ModB";
            const string name = "RuntimeManifestationBridgeTestDifferentSetterTag";
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(modA);
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(modB);

            Ludots.Core.Config.ComponentRegistry.Register(
                name,
                SetRuntimeManifestationBridgeTestTag,
                modA);

            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Register(
                    name,
                    SetRuntimeManifestationBridgeDifferentTestTag,
                    modB))!;
            That(ex.Message, Does.Contain("already registered"));
        }

        [Test]
        public void EntityBuilder_RejectsUnknownTemplatesAndNullOverrides()
        {
            using var world = World.Create();
            var templates = new System.Collections.Generic.Dictionary<string, EntityTemplate>(StringComparer.Ordinal)
            {
                ["known"] = new EntityTemplate
                {
                    Id = "known",
                    Components = new System.Collections.Generic.Dictionary<string, JsonNode>(StringComparer.Ordinal)
                    {
                        ["Name"] = JsonNode.Parse("""{ "Value": "Known" }""")!
                    }
                }
            };

            var builder = new EntityBuilder(world, templates);
            Assert.That(
                Throws<InvalidOperationException>(() => builder.UseTemplate("KNOWN"))!.Message,
                Does.Contain("Unknown entity template"));
            Assert.That(
                Throws<InvalidOperationException>(() => builder.WithOverride("Name", null!))!.Message,
                Does.Contain("requires non-null data"));
            Assert.That(
                Throws<InvalidOperationException>(() => builder.WithOverride("name", JsonNode.Parse("""{ "Value": "Wrong" }""")!).Build())!.Message,
                Does.Contain("Unknown component"));
        }

        private static void AssertRejects(World world, string componentName, string json, string expectedMessage)
        {
            Entity entity = world.Create();
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(
                    entity,
                    componentName,
                    JsonNode.Parse(json)!))!;
            That(ex.Message, Does.Contain(expectedMessage));
        }

        private static void SetRuntimeManifestationBridgeTestTag(Entity entity, JsonNode data)
        {
            if (!entity.Has<RuntimeManifestationBridgeTestTag>())
            {
                entity.Add(new RuntimeManifestationBridgeTestTag());
            }
        }

        private static void SetRuntimeManifestationBridgeDifferentTestTag(Entity entity, JsonNode data)
        {
            if (!entity.Has<RuntimeManifestationBridgeDifferentTestTag>())
            {
                entity.Add(new RuntimeManifestationBridgeDifferentTestTag());
            }
        }

        private struct RuntimeManifestationBridgeTestTag { }
        private struct RuntimeManifestationBridgeDifferentTestTag { }
    }
}
