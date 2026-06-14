using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
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
        [SetUp]
        public void SetUp()
        {
            ShapeDataStorage2D.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ShapeDataStorage2D.Clear();
        }

        [Test]
        public void ManifestationObstacleBridge2D_BoxIntent_CreatesPhysicsAndNavigationObstacle()
        {
            using var world = World.Create();
            var system = new ManifestationObstacleBridge2DSystem(world);
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
            That(ShapeDataStorage2D.TryGetBox(collider.ShapeDataIndex, out var box), Is.True);
            That(box.HalfWidth, Is.EqualTo(Fix64.FromInt(240)));
            That(box.HalfHeight, Is.EqualTo(Fix64.FromInt(30)));

            That(world.Has<Mass2D>(entity), Is.True);
            That(world.Get<Mass2D>(entity).IsStatic, Is.True);
            That(world.Has<Velocity2D>(entity), Is.True);
            That(world.Get<Velocity2D>(entity).Linear, Is.EqualTo(Fix64Vec2.Zero));

            var obstacle = world.Get<NavObstacle2D>(entity);
            That(obstacle.Shape, Is.EqualTo(NavObstacleShape2D.Box));
            That(obstacle.ShapeDataIndex, Is.EqualTo(collider.ShapeDataIndex));

            var nav = world.Get<NavKinematics2D>(entity);
            Fix64 expectedRadius = Fix64Math.Sqrt(
                Fix64.FromInt(240 * 240) +
                Fix64.FromInt(30 * 30));
            That(nav.RadiusCm, Is.EqualTo(expectedRadius));
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
                """)!);
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
                """)!);

            var system = new ManifestationObstacleBridge2DSystem(world);
            system.Update(0f);

            That(world.Has<ManifestationObstaclePolygon2D>(entity), Is.True);

            var collider = world.Get<Collider2D>(entity);
            That(collider.Type, Is.EqualTo(ColliderType2D.Polygon));
            That(ShapeDataStorage2D.TryGetPolygon(collider.ShapeDataIndex, out var polygon), Is.True);
            That(polygon.VertexCount, Is.EqualTo(3));

            var obstacle = world.Get<NavObstacle2D>(entity);
            That(obstacle.Shape, Is.EqualTo(NavObstacleShape2D.Polygon));
            That(obstacle.ShapeDataIndex, Is.EqualTo(collider.ShapeDataIndex));
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
                    JsonNode.Parse("""{ "kind": "footprint2d" }""")!))!;
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

        private struct RuntimeManifestationBridgeTestTag { }
    }
}
