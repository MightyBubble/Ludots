using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Movement.Physics2DBridge;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Physics
{
    /// <summary>
    /// Sensor-emission contract (#1469): contact begin/end edges whose party
    /// carries RegionVolumeEmissionCm fire through the unified region emission
    /// outlet — counterpart rides MapTrigger.SourceEntity, authored payload rides
    /// per schema, off-map emitters and non-emitters stay silent. Physics itself is
    /// unchanged (CrowdPhysicsArena acceptance covers the producer side).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ContactEmissionTapTests
    {
        private const string MapId = "contact_emission_probe";
        private const string PressedEvent = "demo.plate.pressed";
        private const string ReleasedEvent = "demo.plate.released";

        [Test]
        public void ContactEdges_FireDeclaredContract_WithPayloadAndCounterpart()
        {
            using var harness = TapHarness.Create(withSchema: true);
            Entity plate = harness.SpawnEmitter();
            Entity ball = harness.SpawnCounterpart();

            harness.Enqueue(ContactEventType2D.Begin, plate, ball);
            harness.Tick();
            Assert.That(harness.Pressed.Count, Is.EqualTo(1), "Contact begin must fire the declared enter event.");
            Assert.That(harness.Pressed[0].Source, Is.EqualTo(ball), "The counterpart rides MapTrigger.SourceEntity.");
            Assert.That(harness.Pressed[0].Zone, Is.EqualTo("gate"), "Authored payload rides per schema.");
            Assert.That(harness.Released.Count, Is.EqualTo(0));

            harness.Enqueue(ContactEventType2D.End, plate, ball);
            harness.Tick();
            Assert.That(harness.Released.Count, Is.EqualTo(1), "Contact end must fire the declared exit event.");
            Assert.That(harness.Released[0].Source, Is.EqualTo(ball));
        }

        [Test]
        public void NonEmitterParty_AndOffMapEmitter_StaySilent()
        {
            using var harness = TapHarness.Create(withSchema: true);
            Entity plain = harness.SpawnCounterpart();
            Entity offMap = harness.World.Create(
                new RegionVolumeEmissionCm
                {
                    EnterEvent = new EventKey(PressedEvent),
                    ExitEvent = new EventKey(ReleasedEvent),
                });

            harness.Enqueue(ContactEventType2D.Begin, plain, plain);
            harness.Enqueue(ContactEventType2D.Begin, offMap, plain);
            harness.Tick();

            Assert.That(harness.Pressed.Count, Is.EqualTo(0),
                "A party without emission and an emitter without a map scope fire nothing.");
        }

        [Test]
        public void Bake_ValidatesStandaloneEmitter_FailClosedOnUnknownEvent()
        {
            using var harness = TapHarness.Create(withSchema: false);
            harness.SpawnEmitter();

            string? message = null;
            try
            {
                RegionVolumeBakePass.Bake(harness.World, harness.Session, harness.CustomEvents, harness.Schemas);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null, "An authored emitter with an undeclared event must fail the load-time bake.");
            Assert.That(message, Does.Contain(PressedEvent));
            Assert.That(message, Does.Contain("custom"));
        }

        private readonly record struct Fired(Entity Source, string Zone);

        private sealed class NoOpConsumer : IContactEventConsumer2D
        {
            public void OnContactEvent(in ContactEvent2D contactEvent)
            {
            }
        }

        private sealed class TapHarness : IDisposable
        {
            private TapHarness(
                World world,
                MapSessionManager sessions,
                MapSession session,
                TriggerManager triggers,
                ContactEventQueue2D queue,
                ContactEventRoutingSystem2D routing,
                CustomEventNameRegistry customEvents,
                EventSchemaRegistry schemas,
                List<Fired> pressed,
                List<Fired> released)
            {
                World = world;
                Sessions = sessions;
                Session = session;
                Triggers = triggers;
                Queue = queue;
                Routing = routing;
                CustomEvents = customEvents;
                Schemas = schemas;
                Pressed = pressed;
                Released = released;
            }

            public World World { get; }
            public MapSessionManager Sessions { get; }
            public MapSession Session { get; }
            public TriggerManager Triggers { get; }
            public ContactEventQueue2D Queue { get; }
            public ContactEventRoutingSystem2D Routing { get; }
            public CustomEventNameRegistry CustomEvents { get; }
            public EventSchemaRegistry Schemas { get; }
            public List<Fired> Pressed { get; }
            public List<Fired> Released { get; }
            public Ludots.Core.Gameplay.Components.EntityLayer HarnessLayer { get; set; } = new();

            public static TapHarness Create(bool withSchema)
            {
                var world = World.Create();
                var sessions = new MapSessionManager();
                var session = sessions.CreateSession(new MapId(MapId), new MapConfig { Id = MapId });
                var triggers = new TriggerManager();

                var customEvents = new CustomEventNameRegistry();
                var schemas = new EventSchemaRegistry();
                if (withSchema)
                {
                    customEvents.Register(PressedEvent);
                    customEvents.Register(ReleasedEvent);
                    schemas.RegisterCustom(new EventSchema(
                        PressedEvent,
                        EventScope.Map,
                        new EventParamSchema[] { new("zone", EventParamType.String, "plate.zone") }));
                    schemas.RegisterCustom(new EventSchema(
                        ReleasedEvent,
                        EventScope.Map,
                        new EventParamSchema[] { new("zone", EventParamType.String, "plate.zone", Optional: true) }));
                }

                triggers.EventSchemas = schemas;

                var pressed = new List<Fired>();
                var released = new List<Fired>();
                triggers.RegisterEventHandler(new EventKey(PressedEvent), ctx => Capture(pressed, ctx));
                triggers.RegisterEventHandler(new EventKey(ReleasedEvent), ctx => Capture(released, ctx));

                Ludots.Core.Layers.LayerRegistry.Register("probe.contact.emission");
                uint category = 1u << Ludots.Core.Layers.LayerRegistry.GetIndex("probe.contact.emission");
                var layer = new Ludots.Core.Gameplay.Components.EntityLayer(category, category);

                var queue = new ContactEventQueue2D(contactEventQueueCapacity: 64);
                var router = new ContactEventRouter2D(new[] { "probe.contact.emission" });
                router.RegisterConsumer("probe.contact.emission", new NoOpConsumer());
                var tap = new ContactEmissionTap(world, triggers, () => sessions, () => new ScriptContext());
                var routing = new ContactEventRoutingSystem2D(queue, router, tap);
                var harness = new TapHarness(world, sessions, session, triggers, queue, routing, customEvents, schemas, pressed, released);
                harness.HarnessLayer = layer;
                return harness;
            }

            public Entity SpawnEmitter()
            {
                return World.Create(
                    new MapEntity { MapId = new MapId(MapId) },
                    HarnessLayer,
                    new RegionVolumeEmissionCm
                    {
                        EnterEvent = new EventKey(PressedEvent),
                        ExitEvent = new EventKey(ReleasedEvent),
                        Payload = new[]
                        {
                            new RegionVolumePayloadEntry
                            {
                                Key = "plate.zone",
                                Type = RegionVolumePayloadValueType.String,
                                StringValue = "gate",
                            },
                        },
                    });
            }

            public Entity SpawnCounterpart()
            {
                return World.Create(new MapEntity { MapId = new MapId(MapId) }, HarnessLayer);
            }

            public void Enqueue(ContactEventType2D type, Entity emitter, Entity counterpart)
            {
                Queue.Enqueue(new ContactEvent2D
                {
                    Type = type,
                    EntityA = emitter,
                    EntityB = counterpart,
                    LayerA = HarnessLayer.Value,
                    LayerB = HarnessLayer.Value,
                });
            }

            public void Tick()
            {
                Routing.Update(1 / 60f);
            }

            private static Task Capture(List<Fired> sink, ScriptContext context)
            {
                sink.Add(new Fired(
                    context.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity),
                    context.Get<string>("plate.zone")));
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                World.Dispose();
            }
        }
    }
}
