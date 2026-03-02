using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class DisplacementPresetTests
    {
        [Test]
        public void Displacement_AwayFromSource_MovesTarget()
        {
            // Knockback: target at (1000,0), source at (0,0), push away
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) });

            int totalDist = 500;
            int totalTicks = 10;

            world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.AwayFromSource,
                TotalDistanceCm = totalDist,
                RemainingDistanceCm = Fix64.FromInt(totalDist),
                TotalDurationTicks = totalTicks,
                RemainingTicks = totalTicks,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);

            // Run 10 ticks
            for (int i = 0; i < totalTicks; i++)
                system.Update(0f);

            var finalPos = world.Get<WorldPositionCm>(target).Value;

            // Target should have moved ~500cm away from source (in +X direction)
            float finalX = finalPos.X.ToFloat();
            That(finalX, Is.EqualTo(1500f).Within(5f),
                "Target should be pushed 500cm away from source");
        }

        [Test]
        public void Displacement_TowardSource_PullsTarget()
        {
            // Pull: target at (2000,0), source at (0,0), pull toward source
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(2000, 0) });

            int totalDist = 600;
            int totalTicks = 10;

            world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.TowardSource,
                TotalDistanceCm = totalDist,
                RemainingDistanceCm = Fix64.FromInt(totalDist),
                TotalDurationTicks = totalTicks,
                RemainingTicks = totalTicks,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);

            for (int i = 0; i < totalTicks; i++)
                system.Update(0f);

            var finalPos = world.Get<WorldPositionCm>(target).Value;

            // Target should have moved 600cm toward source
            float finalX = finalPos.X.ToFloat();
            That(finalX, Is.EqualTo(1400f).Within(5f),
                "Target should be pulled 600cm toward source");
        }

        [Test]
        public void Displacement_FixedDirection_MovesStraight()
        {
            // Blink/dash in fixed 90-degree direction (straight up / +Y)
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(500, 500) });

            int totalDist = 300;
            int totalTicks = 5;

            world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.Fixed,
                FixedDirectionRad = Fix64.FromInt(90) * Fix64.Deg2Rad, // 90 degrees = +Y
                TotalDistanceCm = totalDist,
                RemainingDistanceCm = Fix64.FromInt(totalDist),
                TotalDurationTicks = totalTicks,
                RemainingTicks = totalTicks,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);

            for (int i = 0; i < totalTicks; i++)
                system.Update(0f);

            var finalPos = world.Get<WorldPositionCm>(target).Value;

            // Should move ~300cm in +Y direction, X unchanged
            float finalX = finalPos.X.ToFloat();
            float finalY = finalPos.Y.ToFloat();
            That(finalX, Is.EqualTo(500f).Within(5f), "X should be unchanged");
            That(finalY, Is.EqualTo(800f).Within(5f), "Y should increase by ~300");
        }

        [Test]
        public void Displacement_Completes_EntityDestroyed()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) });

            var dispEntity = world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.AwayFromSource,
                TotalDistanceCm = 100,
                RemainingDistanceCm = Fix64.FromInt(100),
                TotalDurationTicks = 5,
                RemainingTicks = 5,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);

            // After 5 ticks, displacement should be done
            for (int i = 0; i < 5; i++)
                system.Update(0f);

            That(world.IsAlive(dispEntity), Is.False,
                "Displacement entity should be destroyed after completion");
            That(world.IsAlive(target), Is.True,
                "Target entity should survive displacement");
        }

        [Test]
        public void Displacement_DeadTarget_ImmediateCleanup()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) });

            var dispEntity = world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.AwayFromSource,
                TotalDistanceCm = 500,
                RemainingDistanceCm = Fix64.FromInt(500),
                TotalDurationTicks = 20,
                RemainingTicks = 20,
                OverrideNavigation = true,
            });

            // Kill the target
            world.Destroy(target);

            var system = new DisplacementRuntimeSystem(world);
            system.Update(0f);

            That(world.IsAlive(dispEntity), Is.False,
                "Displacement should be cleaned up when target dies");
        }
    }
}
