using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
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
                  "sinkNavigationObstacle": true
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
                JsonNode.Parse("""{ "kind": "Footprint2D" }""")!);

            That(world.Get<SpatialBounds>(entity).Kind, Is.EqualTo(SpatialBoundsKind.Footprint2D));

            Entity invalid = world.Create();
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(
                    invalid,
                    "SpatialBounds",
                    JsonNode.Parse("""{ "kind": "footprint2d" }""")!))!;
            That(ex.Message, Does.Contain("Unsupported SpatialBounds kind"));
        }
    }
}
