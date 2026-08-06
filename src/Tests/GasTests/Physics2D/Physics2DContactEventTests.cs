using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Layers;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Authoring;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Ticking;
using NUnit.Framework;
using ComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace GasTests.Physics2D
{
    /// <summary>
    /// Issue #732: contact event export pipeline — opt-in emitters, begin/end edge detection
    /// on CollisionPair.ContactCount transitions, entity-death End synthesis, drain contract,
    /// overflow and layer allowlist contract errors, and deterministic event ordering.
    /// </summary>
    [TestFixture]
    public sealed class Physics2DContactEventTests
    {
        private const string SensorLayerName = "ContactEventTests.Sensor";
        private const string PropLayerName = "ContactEventTests.Prop";

        private ShapeDataStorage2D _shapeStorage = null!;
        private uint _sensorBit;
        private uint _propBit;

        [OneTimeSetUp]
        public void RegisterLayersAndAuthoring()
        {
            _sensorBit = 1u << LayerRegistry.Register(SensorLayerName);
            _propBit = 1u << LayerRegistry.Register(PropLayerName);
            Physics2DTemplateAuthoring.RegisterContactEventEmitter("ContactEventTests.ContactEventEmitter", "gas-tests");
        }

        [SetUp]
        public void SetUp()
        {
            _shapeStorage = new ShapeDataStorage2D();
        }

        [Test]
        public void DeclaredContacts_EmitExactlyNBeginAndNEndPairs_WithDeterministicSequence()
        {
            List<(ContactEventType2D Type, int IdA, int IdB, long NormalX, long NormalY)> RunOnce()
            {
                var shapeStorage = new ShapeDataStorage2D();
                using var world = World.Create();
                var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
                var queue = new ContactEventQueue2D(contactEventQueueCapacity: 64);

                const int stationCount = 3;
                var kinematics = new Entity[stationCount];
                for (int station = 0; station < stationCount; station++)
                {
                    int y = station * 2000;
                    CreateEmitterBox(world, shapeStorage, 0, y, halfCm: 40);
                    kinematics[station] = CreateKinematicCircle(world, shapeStorage, -200, y, radiusCm: 50);
                    world.Add(kinematics[station], new EntityLayer(_propBit, uint.MaxValue));
                }

                var simulation = CreateSimulation(world, shapeStorage, poses, queue);

                var recorded = new List<(ContactEventType2D, int, int, long, long)>();
                void StepAndDrain(int approachStep)
                {
                    for (int station = 0; station < stationCount; station++)
                    {
                        var target = Fix64Vec2.FromInt(approachStep, station * 2000);
                        poses.SetKinematicTargetPose(kinematics[station], target, Fix64.Zero);
                    }

                    simulation.Update(1f / 60f);
                    foreach (ContactEvent2D contactEvent in simulation.ContactEvents.DrainEvents())
                    {
                        recorded.Add((
                            contactEvent.Type,
                            contactEvent.EntityA.Id,
                            contactEvent.EntityB.Id,
                            contactEvent.Normal.X.RawValue,
                            contactEvent.Normal.Y.RawValue));
                    }
                }

                // Approach into contact, then retreat until every contact has ended.
                for (int step = 1; step <= 40; step++)
                {
                    StepAndDrain(-200 + step * 4);
                }
                for (int step = 1; step <= 60; step++)
                {
                    StepAndDrain(-40 - step * 4);
                }

                return recorded;
            }

            var first = RunOnce();

            int beginCount = 0;
            int endCount = 0;
            var openContacts = new HashSet<(int, int)>();
            foreach (var contactEvent in first)
            {
                var pairKey = (contactEvent.IdA, contactEvent.IdB);
                if (contactEvent.Type == ContactEventType2D.Begin)
                {
                    beginCount++;
                    Assert.That(openContacts.Add(pairKey), Is.True, "A pair must not Begin twice without an End in between.");
                }
                else
                {
                    endCount++;
                    Assert.That(openContacts.Remove(pairKey), Is.True, "Every End must match a preceding Begin of the same pair.");
                }
            }

            Assert.That(beginCount, Is.EqualTo(3), "Exactly N=3 declared contacts must produce exactly 3 Begin events.");
            Assert.That(endCount, Is.EqualTo(3), "Exactly N=3 declared contacts must produce exactly 3 End events.");
            Assert.That(openContacts, Is.Empty, "No contact may leak a permanent begin state.");

            var second = RunOnce();
            Assert.That(second, Is.EqualTo(first),
                "Identical inputs must reproduce a bitwise-identical contact event sequence across runs.");
        }

        [Test]
        public void BeginEvent_CarriesEntitiesNormalPenetrationAndBothLayers()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var queue = new ContactEventQueue2D(contactEventQueueCapacity: 64);
            var box = CreateEmitterBox(world, _shapeStorage, 0, 0, halfCm: 40);
            var kinematic = CreateKinematicCircle(world, _shapeStorage, -200, 0, radiusCm: 50);
            world.Add(kinematic, new EntityLayer(_propBit, uint.MaxValue));
            var simulation = CreateSimulation(world, _shapeStorage, poses, queue);

            ContactEvent2D begin = default;
            bool found = false;
            for (int step = 1; step <= 40 && !found; step++)
            {
                poses.SetKinematicTargetPose(kinematic, Fix64Vec2.FromInt(-200 + step * 4, 0), Fix64.Zero);
                simulation.Update(1f / 60f);
                foreach (ContactEvent2D contactEvent in simulation.ContactEvents.DrainEvents())
                {
                    begin = contactEvent;
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True, "Kinematic approach must produce a Begin event.");
            Assert.That(begin.Type, Is.EqualTo(ContactEventType2D.Begin));

            var ids = new[] { begin.EntityA.Id, begin.EntityB.Id };
            Assert.That(ids, Is.EquivalentTo(new[] { box.Id, kinematic.Id }),
                "Begin payload must carry both contact parties.");

            Assert.That(begin.Penetration > Fix64.Zero, Is.True, "Begin payload must carry the first-frame penetration.");

            long lengthSquaredRaw = (begin.Normal.X * begin.Normal.X + begin.Normal.Y * begin.Normal.Y).RawValue;
            Assert.That(Math.Abs(lengthSquaredRaw - Fix64.OneValue.RawValue), Is.LessThanOrEqualTo(Fix64.OneValue.RawValue / 100),
                "Begin payload must carry a unit contact normal.");

            LayerMask boxLayer = begin.EntityA.Id == box.Id ? begin.LayerA : begin.LayerB;
            LayerMask kinematicLayer = begin.EntityA.Id == kinematic.Id ? begin.LayerA : begin.LayerB;
            Assert.That(boxLayer.Category, Is.EqualTo(_sensorBit), "Begin payload must carry the emitter's EntityLayer.");
            Assert.That(kinematicLayer.Category, Is.EqualTo(_propBit), "Begin payload must carry the other party's EntityLayer.");

            Assert.That(simulation.ContactEvents.Count, Is.Zero, "DrainEvents must clear the queue.");
            Assert.That(simulation.ContactEvents.DrainEvents().Length, Is.Zero, "A drained queue must stay empty until physics writes again.");
        }

        [Test]
        public void NonEmitterContacts_ProduceNoEventsAtZeroCost()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var queue = new ContactEventQueue2D(contactEventQueueCapacity: 64);

            // Overlapping dynamic boxes without any ContactEventEmitter2D declaration.
            CreateDynamicBox(world, _shapeStorage, 0, 0, halfCm: 40);
            CreateDynamicBox(world, _shapeStorage, 30, 0, halfCm: 40);
            var simulation = CreateSimulation(world, _shapeStorage, poses, queue);

            for (int step = 0; step < 30; step++)
            {
                simulation.Update(1f / 60f);
            }

            Assert.That(simulation.ContactEvents.Count, Is.Zero,
                "Entities without ContactEventEmitter2D must never produce contact events.");
        }

        [Test]
        public void EntityDestroyedMidContact_SynthesizesEndWithoutLeak()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var queue = new ContactEventQueue2D(contactEventQueueCapacity: 64);
            var box = CreateEmitterBox(world, _shapeStorage, 0, 0, halfCm: 40);
            var kinematic = CreateKinematicCircle(world, _shapeStorage, -60, 0, radiusCm: 50);
            world.Add(kinematic, new EntityLayer(_propBit, uint.MaxValue));
            var simulation = CreateSimulation(world, _shapeStorage, poses, queue);

            // Overlapping from the start: the first steps must produce exactly one Begin.
            int beginCount = 0;
            for (int step = 0; step < 5; step++)
            {
                simulation.Update(1f / 60f);
                foreach (ContactEvent2D contactEvent in simulation.ContactEvents.DrainEvents())
                {
                    Assert.That(contactEvent.Type, Is.EqualTo(ContactEventType2D.Begin));
                    beginCount++;
                }
            }
            Assert.That(beginCount, Is.EqualTo(1), "Overlapping emitter contact must Begin exactly once.");

            // Structural change at the frame boundary (cold phase), then step: the tracked
            // contact's pair disappears and an End must be synthesized from the captured payload.
            world.Destroy(box);
            simulation.Update(1f / 60f);

            var drained = simulation.ContactEvents.DrainEvents();
            Assert.That(drained.Length, Is.EqualTo(1), "Destroying a contact party must synthesize exactly one End event.");
            Assert.That(drained[0].Type, Is.EqualTo(ContactEventType2D.End));
            Assert.That(drained[0].Penetration, Is.EqualTo(Fix64.Zero), "End events carry zero penetration.");
            LayerMask boxLayer = drained[0].EntityA.Id == box.Id ? drained[0].LayerA : drained[0].LayerB;
            Assert.That(boxLayer.Category, Is.EqualTo(_sensorBit),
                "End payload must carry the layers captured at Begin time even though the entity died.");

            for (int step = 0; step < 10; step++)
            {
                simulation.Update(1f / 60f);
            }
            Assert.That(simulation.ContactEvents.Count, Is.Zero, "No events may leak after the synthesized End.");
        }

        [Test]
        public void EntityIdReusedForNewContactParty_EmitsEndForOldGenerationAndBeginForNew()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var queue = new ContactEventQueue2D(contactEventQueueCapacity: 64);
            var box = CreateEmitterBox(world, _shapeStorage, 0, 0, halfCm: 40);
            var kinematic = CreateKinematicCircle(world, _shapeStorage, -60, 0, radiusCm: 50);
            world.Add(kinematic, new EntityLayer(_propBit, uint.MaxValue));
            var simulation = CreateSimulation(world, _shapeStorage, poses, queue);

            for (int step = 0; step < 3; step++)
            {
                simulation.Update(1f / 60f);
            }

            Assert.That(simulation.ContactEvents.DrainEvents().Length, Is.EqualTo(1), "setup: one Begin for the initial contact");

            // 帧边界销毁 kinematic 一方并立即以同形状同位置重建：Arch 回收实体 id，
            // 新实体与旧实体同 id 不同 version——追踪键相同但代际不同。
            world.Destroy(kinematic);
            var reused = CreateKinematicCircle(world, _shapeStorage, -60, 0, radiusCm: 50);
            world.Add(reused, new EntityLayer(_propBit, uint.MaxValue));
            if (reused.Id != kinematic.Id)
            {
                Assert.Ignore("Arch did not recycle the entity id in this runtime; the reuse scenario is not reachable here.");
            }

            Assert.That(reused, Is.Not.EqualTo(kinematic), "the recycled entity must differ by version");

            simulation.Update(1f / 60f);

            ReadOnlySpan<ContactEvent2D> drained = simulation.ContactEvents.DrainEvents();
            Assert.That(drained.Length, Is.EqualTo(2),
                "id reuse across generations must yield exactly one End (old generation) and one Begin (new generation)");
            Assert.That(drained[0].Type, Is.EqualTo(ContactEventType2D.End));
            Assert.That(drained[1].Type, Is.EqualTo(ContactEventType2D.Begin));
            Entity oldParty = drained[0].EntityA.Id == kinematic.Id ? drained[0].EntityA : drained[0].EntityB;
            Entity newParty = drained[1].EntityA.Id == reused.Id ? drained[1].EntityA : drained[1].EntityB;
            Assert.That(oldParty, Is.EqualTo(kinematic), "the End belongs to the dead generation");
            Assert.That(newParty, Is.EqualTo(reused), "the Begin belongs to the live generation");

            for (int step = 0; step < 5; step++)
            {
                simulation.Update(1f / 60f);
            }
            Assert.That(simulation.ContactEvents.Count, Is.Zero, "steady contact must not re-emit after the generation swap");
        }

        [Test]
        public void EventQueueOverflow_ThrowsNamingCapacityItem()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var queue = new ContactEventQueue2D(contactEventQueueCapacity: 1);

            // Two simultaneous emitter contacts, but capacity for only one event.
            CreateEmitterBox(world, _shapeStorage, 0, 0, halfCm: 40);
            var kinematicA = CreateKinematicCircle(world, _shapeStorage, -60, 0, radiusCm: 50);
            world.Add(kinematicA, new EntityLayer(_propBit, uint.MaxValue));
            CreateEmitterBox(world, _shapeStorage, 0, 2000, halfCm: 40);
            var kinematicB = CreateKinematicCircle(world, _shapeStorage, -60, 2000, radiusCm: 50);
            world.Add(kinematicB, new EntityLayer(_propBit, uint.MaxValue));
            var simulation = CreateSimulation(world, _shapeStorage, poses, queue);

            Assert.That(
                () =>
                {
                    for (int step = 0; step < 5; step++)
                    {
                        simulation.Update(1f / 60f);
                    }
                },
                Throws.InvalidOperationException.With.Message.Contains("contactEventQueueCapacity"),
                "Queue overflow must throw and name the capacity config item instead of dropping events.");
        }

        [Test]
        public void EmitterOutsideConfiguredLayerAllowlist_ThrowsContractError()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var queue = new ContactEventQueue2D(contactEventQueueCapacity: 64);

            // The emitter box carries the Prop layer, but only the Sensor layer is allowed to emit.
            int boxShape = _shapeStorage.RegisterBox(40f, 40f);
            world.Create(
                Position2D.FromCm(0, 0),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(0, 0) },
                Rotation2D.Identity,
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = boxShape },
                new ContactEventEmitter2D(),
                new EntityLayer(_propBit, uint.MaxValue));
            var kinematic = CreateKinematicCircle(world, _shapeStorage, -60, 0, radiusCm: 50);
            world.Add(kinematic, new EntityLayer(_propBit, uint.MaxValue));
            var simulation = CreateSimulation(world, _shapeStorage, poses, queue);

            Assert.That(
                () =>
                {
                    for (int step = 0; step < 5; step++)
                    {
                        simulation.Update(1f / 60f);
                    }
                },
                Throws.InvalidOperationException.With.Message.Contains("contactEventEmitterLayers"),
                "An emitter whose layer is outside the allowlist is a configuration contract error.");
        }

        [Test]
        public void EmitterContactPartyWithoutEntityLayer_Throws()
        {
            using var world = World.Create();
            var poses = new KinematicTargetPoseBuffer2D(kinematicBodyCapacity: 8);
            var queue = new ContactEventQueue2D(contactEventQueueCapacity: 64);
            CreateEmitterBox(world, _shapeStorage, 0, 0, halfCm: 40);
            // The kinematic party intentionally has no EntityLayer.
            CreateKinematicCircle(world, _shapeStorage, -60, 0, radiusCm: 50);
            var simulation = CreateSimulation(world, _shapeStorage, poses, queue);

            Assert.That(
                () =>
                {
                    for (int step = 0; step < 5; step++)
                    {
                        simulation.Update(1f / 60f);
                    }
                },
                Throws.InvalidOperationException.With.Message.Contains("EntityLayer"),
                "The event payload contract requires EntityLayer on both contact parties.");
        }

        [Test]
        public void ContactEventEmitterAuthoring_ParsesEmptyObjectAndRejectsProperties()
        {
            using var world = World.Create();

            var entity = world.Create();
            ComponentRegistry.Apply(
                entity,
                "ContactEventTests.ContactEventEmitter",
                JsonNode.Parse("{}")!,
                ComponentAuthoringContext.Empty);
            Assert.That(world.Has<ContactEventEmitter2D>(entity), Is.True);

            Assert.That(
                () => ComponentRegistry.Apply(
                    world.Create(),
                    "ContactEventTests.ContactEventEmitter",
                    JsonNode.Parse("""{ "enabled": true }""")!,
                    ComponentAuthoringContext.Empty),
                Throws.InvalidOperationException.With.Message.Contains("enabled"),
                "The emitter declaration is a strict opt-in marker: any property is unknown.");
        }

        private Physics2DSimulationSystem CreateSimulation(
            World world,
            ShapeDataStorage2D shapeStorage,
            KinematicTargetPoseBuffer2D poses,
            ContactEventQueue2D contactEvents)
        {
            var simulation = new Physics2DSimulationSystem(
                world,
                new DiscreteClock(),
                new Physics2DTickPolicy(60, maxStepsPerFixedTick: 1),
                new Physics2DSolverConfig(),
                shapeStorage,
                poses,
                contactEvents,
                new Physics2DKinematicConfig
                {
                    KinematicBodyCapacity = poses.Capacity,
                    ContactEventQueueCapacity = contactEvents.Capacity,
                    ContactEventEmitterLayers = new List<string> { SensorLayerName }
                });
            simulation.Initialize();
            return simulation;
        }

        private Entity CreateEmitterBox(World world, ShapeDataStorage2D shapeStorage, int xCm, int yCm, float halfCm)
        {
            int shape = shapeStorage.RegisterBox(halfCm, halfCm);
            return world.Create(
                Position2D.FromCm(xCm, yCm),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(xCm, yCm) },
                Rotation2D.Identity,
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape },
                new ContactEventEmitter2D(),
                new EntityLayer(_sensorBit, uint.MaxValue));
        }

        private static Entity CreateKinematicCircle(World world, ShapeDataStorage2D shapeStorage, int xCm, int yCm, float radiusCm)
        {
            int shape = shapeStorage.RegisterCircle(radiusCm);
            return world.Create(
                Position2D.FromCm(xCm, yCm),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(xCm, yCm) },
                Rotation2D.Identity,
                Velocity2D.Zero,
                Mass2D.Kinematic,
                new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = shape });
        }

        private static Entity CreateDynamicBox(World world, ShapeDataStorage2D shapeStorage, int xCm, int yCm, float halfCm)
        {
            int shape = shapeStorage.RegisterBox(halfCm, halfCm);
            return world.Create(
                Position2D.FromCm(xCm, yCm),
                new PreviousPosition2D { Value = Fix64Vec2.FromInt(xCm, yCm) },
                Rotation2D.Identity,
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shape });
        }
    }
}
