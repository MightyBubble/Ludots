using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Systems;
using Ludots.Core.Physics2D.Components;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    [NonParallelizable]
    public sealed class NavFacingFromVelocitySystemTests
    {
        [Test]
        public void Update_UsesActualVelocity_WhenBodyIsMoving()
        {
            using var world = World.Create();
            var system = new NavFacingFromVelocitySystem(world);

            Entity entity = world.Create(
                new NavAgent2D(),
                new Velocity2D { Linear = Fix64Vec2.FromInt(0, 120), Angular = Fix64.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(120, 0) },
                new FacingDirection { AngleRad = 0f });

            system.Update(1f / 60f);

            Assert.That(world.Get<FacingDirection>(entity).AngleRad, Is.EqualTo(MathF.PI * 0.5f).Within(0.01f));
        }

        [Test]
        public void Update_FallsBackToDesiredVelocity_WhenBodyIsNearlyStill()
        {
            using var world = World.Create();
            var system = new NavFacingFromVelocitySystem(world);

            Entity entity = world.Create(
                new NavAgent2D(),
                Velocity2D.Zero,
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(-100, 0) });

            system.Update(1f / 60f);

            Assert.That(world.Has<FacingDirection>(entity), Is.True);
            Assert.That(MathF.Abs(world.Get<FacingDirection>(entity).AngleRad - MathF.PI), Is.LessThan(0.01f));
        }

        [Test]
        public void Update_DoesNotOverwriteFacing_WhenNoMeaningfulMotionExists()
        {
            using var world = World.Create();
            var system = new NavFacingFromVelocitySystem(world);

            Entity entity = world.Create(
                new NavAgent2D(),
                Velocity2D.Zero,
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(1, 1) },
                new FacingDirection { AngleRad = 0.75f });

            system.Update(1f / 60f);

            Assert.That(world.Get<FacingDirection>(entity).AngleRad, Is.EqualTo(0.75f).Within(0.0001f));
        }
    }
}
