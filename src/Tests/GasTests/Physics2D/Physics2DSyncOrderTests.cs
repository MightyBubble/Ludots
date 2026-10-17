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
    /// Verifies that the execution order SavePrevious → Physics update → Physics2DToWorldSync
    /// produces proper interpolation data (Previous != Current after movement).
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
            var startPos = Fix64Vec2.FromInt(100, 200);
            var entity = _world.Create(
                new WorldPositionCm { Value = startPos },
                new PreviousWorldPositionCm { Value = startPos },
                new Position2D { Value = startPos }
            );

            var savePrev = new SavePreviousWorldPositionSystem(_world);
            var physicsSync = new Physics2DToWorldPositionSyncSystem(_world);

            savePrev.Update(0.016f);

            var newPhysicsPos = Fix64Vec2.FromInt(300, 400);
            _world.Set(entity, new Position2D { Value = newPhysicsPos });

            physicsSync.Update(0.016f);

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
            var startPos = Fix64Vec2.FromInt(100, 200);
            var entity = _world.Create(
                new WorldPositionCm { Value = startPos },
                new PreviousWorldPositionCm { Value = startPos },
                new Position2D { Value = startPos }
            );

            var savePrev = new SavePreviousWorldPositionSystem(_world);
            var physicsSync = new Physics2DToWorldPositionSyncSystem(_world);

            savePrev.Update(0.016f);

            var newPhysicsPos = Fix64Vec2.FromInt(300, 400);
            _world.Set(entity, new Position2D { Value = newPhysicsPos });

            savePrev.Update(0.016f);

            physicsSync.Update(0.016f);

            // The sequence above runs the second SavePrevious before the sync, so it saves
            // the start value and does not reproduce the break; reset and replay the actual
            // broken order, where SavePrevious saves the already-synced position.
            _world.Set(entity, new WorldPositionCm { Value = startPos });
            _world.Set(entity, new PreviousWorldPositionCm { Value = startPos });
            _world.Set(entity, new Position2D { Value = startPos });

            // SchemaUpdate phase: SavePrevious.
            savePrev.Update(0.016f);

            // InputCollection phase, replayed in the broken order: Physics → Sync → SavePrevious.
            _world.Set(entity, new Position2D { Value = newPhysicsPos });
            physicsSync.Update(0.016f);
            savePrev.Update(0.016f);

            var prev = _world.Get<PreviousWorldPositionCm>(entity);
            var current = _world.Get<WorldPositionCm>(entity);

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

            savePrev.Update(0.016f);
            _world.Set(entity, new Position2D { Value = Fix64Vec2.FromInt(100, 100) });
            physicsSync.Update(0.016f);

            var prev1 = _world.Get<PreviousWorldPositionCm>(entity);
            Assert.That(prev1.Value.X, Is.EqualTo(pos0.X), "Frame 1: Previous should be (0,0)");

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
