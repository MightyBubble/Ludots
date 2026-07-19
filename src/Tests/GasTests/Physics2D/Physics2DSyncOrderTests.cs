using NUnit.Framework;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Systems;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;

namespace GasTests
{
    /// <summary>
    /// Verifies that the correct execution order
    ///   SavePrevious → Physics update → Physics2DToWorldSync
    /// produces proper interpolation data (Previous != Current after movement).
    ///
    /// The BUG was: SavePrevious registered twice (once after Physics update),
    /// causing Previous to be overwritten with the new value, making interpolation flat.
    /// </summary>
    [TestFixture]
    public class Physics2DSyncOrderTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        [Test]
        public void CorrectOrder_PreviousRetainsOldValue()
        {
            // Arrange: entity at (100, 200) with physics
            var startPos = Fix64Vec2.FromInt(100, 200);
            var entity = _world.Create(
                new WorldPositionCm { Value = startPos },
                new PreviousWorldPositionCm { Value = startPos },
                new Position2D { Value = startPos }
            );

            // Systems in correct order
            var savePrev = new SavePreviousWorldPositionSystem(_world);
            var physicsSync = new Physics2DToWorldPositionSyncSystem(_world);

            // Step 1: SavePrevious — stores current (100,200) into Previous
            savePrev.Update(0.016f);

            // Step 2: Simulate physics moving entity to (300, 400)
            var newPhysicsPos = Fix64Vec2.FromInt(300, 400);
            _world.Set(entity, new Position2D { Value = newPhysicsPos });

            // Step 3: Physics2DToWorldSync — copies Position2D → WorldPositionCm
            physicsSync.Update(0.016f);

            // Assert: Previous should still be the OLD value (100,200)
            var prev = _world.Get<PreviousWorldPositionCm>(entity);
            var current = _world.Get<WorldPositionCm>(entity);

            Assert.That(prev.Value.X, Is.EqualTo(startPos.X), "Previous X should retain old value");
            Assert.That(prev.Value.Y, Is.EqualTo(startPos.Y), "Previous Y should retain old value");
            Assert.That(current.Value.X, Is.EqualTo(newPhysicsPos.X), "Current X should be updated");
            Assert.That(current.Value.Y, Is.EqualTo(newPhysicsPos.Y), "Current Y should be updated");
        }

        [Test]
        public void DuplicateSavePrevAfterPhysics_BreaksInterpolation()
        {
            // Demonstrates the BUG scenario: calling SavePrevious again after physics sync
            // causes Previous == Current, breaking interpolation.
            var startPos = Fix64Vec2.FromInt(100, 200);
            var entity = _world.Create(
                new WorldPositionCm { Value = startPos },
                new PreviousWorldPositionCm { Value = startPos },
                new Position2D { Value = startPos }
            );

            var savePrev = new SavePreviousWorldPositionSystem(_world);
            var physicsSync = new Physics2DToWorldPositionSyncSystem(_world);

            // Correct: SavePrevious first
            savePrev.Update(0.016f);

            // Physics moves entity
            var newPhysicsPos = Fix64Vec2.FromInt(300, 400);
            _world.Set(entity, new Position2D { Value = newPhysicsPos });

            // BUG: SavePrevious called again BEFORE PhysicsSync
            // (this was the duplicate registration in InputCollection)
            savePrev.Update(0.016f);

            // PhysicsSync copies Position2D → WorldPositionCm
            physicsSync.Update(0.016f);

            // Previous now equals current (the bug) because SavePrevious was called
            // after WorldPositionCm was still the start value, but if we simulate
            // the actual buggy order: SavePrev → Physics → SavePrev(BUG) → Sync
            // Let's re-verify: Previous was set to current WorldPos which is still startPos
            // then sync updates WorldPos to newPhysicsPos.
            // Actually the BUG order was: Physics → SavePrev → Sync
            // which means SavePrev saved the *already updated* WorldPos.
            // Let me re-simulate the actual buggy order.

            // Reset
            _world.Set(entity, new WorldPositionCm { Value = startPos });
            _world.Set(entity, new PreviousWorldPositionCm { Value = startPos });
            _world.Set(entity, new Position2D { Value = startPos });

            // Correct SchemaUpdate phase: SavePrevious
            savePrev.Update(0.016f);

            // InputCollection phase (buggy order):
            // 1. Physics moves entity
            _world.Set(entity, new Position2D { Value = newPhysicsPos });
            // 2. Physics sync runs (Position2D → WorldPositionCm)
            physicsSync.Update(0.016f);
            // 3. BUG: SavePrevious runs AGAIN, overwriting Previous with the NEW value
            savePrev.Update(0.016f);

            var prev = _world.Get<PreviousWorldPositionCm>(entity);
            var current = _world.Get<WorldPositionCm>(entity);

            // Both are now the same — interpolation is broken
            Assert.That(prev.Value.X, Is.EqualTo(current.Value.X),
                "BUG: Previous X equals Current X when SavePrevious runs after physics sync");
            Assert.That(prev.Value.Y, Is.EqualTo(current.Value.Y),
                "BUG: Previous Y equals Current Y when SavePrevious runs after physics sync");
        }

        [Test]
        public void MultipleFrames_PreviousTracksCorrectly()
        {
            var pos0 = Fix64Vec2.FromInt(0, 0);
            var entity = _world.Create(
                new WorldPositionCm { Value = pos0 },
                new PreviousWorldPositionCm { Value = pos0 },
                new Position2D { Value = pos0 }
            );

            var savePrev = new SavePreviousWorldPositionSystem(_world);
            var physicsSync = new Physics2DToWorldPositionSyncSystem(_world);

            // Frame 1: move to (100, 100)
            savePrev.Update(0.016f);
            _world.Set(entity, new Position2D { Value = Fix64Vec2.FromInt(100, 100) });
            physicsSync.Update(0.016f);

            var prev1 = _world.Get<PreviousWorldPositionCm>(entity);
            Assert.That(prev1.Value.X, Is.EqualTo(pos0.X), "Frame 1: Previous should be (0,0)");

            // Frame 2: move to (200, 200)
            savePrev.Update(0.016f);
            _world.Set(entity, new Position2D { Value = Fix64Vec2.FromInt(200, 200) });
            physicsSync.Update(0.016f);

            var prev2 = _world.Get<PreviousWorldPositionCm>(entity);
            var cur2 = _world.Get<WorldPositionCm>(entity);
            Assert.That(prev2.Value.X, Is.EqualTo(Fix64Vec2.FromInt(100, 100).X),
                "Frame 2: Previous should be (100,100)");
            Assert.That(cur2.Value.X, Is.EqualTo(Fix64Vec2.FromInt(200, 200).X),
                "Frame 2: Current should be (200,200)");
        }
    }
}
