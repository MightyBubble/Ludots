using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using NUnit.Framework;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class ComponentRegistryPositionAuthoringTests
{
    [Test]
    public void WorldPositionCm_InstanceOverrideUpdatesPositionWithoutReplacingPresentationAuthoring()
    {
        using World world = World.Create();
        Entity entity = world.Create();

        CoreComponentRegistry.Apply(entity, "WorldPositionCm", Position(10, 20));
        world.Set(entity, new VisualTransform
        {
            Position = new Vector3(1f, 2f, 3f),
            Rotation = Quaternion.Identity,
            Scale = new Vector3(4f, 5f, 6f),
        });
        world.Set(entity, new CullState
        {
            IsVisible = true,
            LOD = LODLevel.High,
            DistanceToCameraSq = 25f,
            ScreenCoverage01 = 0.5f,
        });

        CoreComponentRegistry.Apply(entity, "WorldPositionCm", Position(300, 400));

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<WorldPositionCm>(entity).Value, Is.EqualTo(Fix64Vec2.FromInt(300, 400)));
            Assert.That(world.Get<PreviousWorldPositionCm>(entity).Value, Is.EqualTo(Fix64Vec2.FromInt(300, 400)));
            Assert.That(world.Get<VisualTransform>(entity).Scale, Is.EqualTo(new Vector3(4f, 5f, 6f)));
            Assert.That(world.Get<CullState>(entity).IsVisible, Is.True);
            Assert.That(world.Get<CullState>(entity).LOD, Is.EqualTo(LODLevel.High));
        });
    }

    private static JsonNode Position(int x, int y) =>
        JsonNode.Parse($$"""
        {
          "Value": {
            "X": {{x}},
            "Y": {{y}}
          }
        }
        """)!;
}
