using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [NonParallelizable]
    public sealed class MapTriggerRegionTests
    {
        private const string MapId = "map_region_probe";
        private const string TrackedTagName = "Region.Tracked.Probe";
        private const string UnknownTagName = "Region.NoSuchTag.Probe";

        [Test]
        public void ParseList_CircleWithoutRadiusCm_Rejected()
        {
            JsonNode node = JsonNode.Parse("""[ { "id": "ring", "shape": "circle", "x": 0, "y": 0 } ]""")!;

            string? message = null;
            try
            {
                MapRegionDefinition.ParseList(node, MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("radiusCm"));
        }

        [Test]
        public void ParseList_RectMissingHalfHeightCm_Rejected()
        {
            JsonNode node = JsonNode.Parse("""[ { "id": "yard", "shape": "rect", "x": 0, "y": 0, "halfWidthCm": 50 } ]""")!;

            string? message = null;
            try
            {
                MapRegionDefinition.ParseList(node, MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("halfHeightCm"));
        }

        [Test]
        public void ParseList_UnknownField_Rejected()
        {
            JsonNode node = JsonNode.Parse(
                """[ { "id": "ring", "shape": "circle", "x": 0, "y": 0, "radiusCm": 10, "priority": 1 } ]""")!;

            string? message = null;
            try
            {
                MapRegionDefinition.ParseList(node, MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("priority"));
        }

        [Test]
        public void ParseList_DuplicateRegionId_Rejected()
        {
            JsonNode node = JsonNode.Parse(
                """
                [
                  { "id": "ring", "shape": "circle", "x": 0, "y": 0, "radiusCm": 10 },
                  { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 10 }
                ]
                """)!;

            string? message = null;
            try
            {
                MapRegionDefinition.ParseList(node, MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
            Assert.That(message, Does.Contain("ring"));
        }

        [Test]
        public void ParseList_UnknownShape_Rejected()
        {
            JsonNode node = JsonNode.Parse(
                """[ { "id": "ring", "shape": "polygon", "x": 0, "y": 0, "radiusCm": 10 } ]""")!;

            string? message = null;
            try
            {
                MapRegionDefinition.ParseList(node, MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("polygon"));
        }

        [Test]
        public void ParseList_MissingNode_YieldsNoRegions()
        {
            List<MapRegionDefinition> regions = MapRegionDefinition.ParseList(null, MapId);

            Assert.That(regions.Count, Is.EqualTo(0));
        }

        [Test]
        public void ParseList_CircleAndRectWithEntityTags_Accepted()
        {
            JsonNode node = JsonNode.Parse(
                """
                [
                  { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 50, "entityTags": [ "Region.Tracked.Probe" ] },
                  { "id": "yard", "shape": "rect", "x": 200, "y": 200, "halfWidthCm": 50, "halfHeightCm": 40 }
                ]
                """)!;

            List<MapRegionDefinition> regions = MapRegionDefinition.ParseList(node, MapId);

            Assert.That(regions.Count, Is.EqualTo(2));
            Assert.That(regions[0].Id, Is.EqualTo("ring"));
            Assert.That(regions[0].Shape, Is.EqualTo(MapRegionShape.Circle));
            Assert.That(regions[0].EntityTags, Is.EqualTo(new[] { "Region.Tracked.Probe" }));
            Assert.That(regions[1].Id, Is.EqualTo("yard"));
            Assert.That(regions[1].Shape, Is.EqualTo(MapRegionShape.Rect));
            Assert.That(regions[1].EntityTags.Count, Is.EqualTo(0));
        }

        [Test]
        public void Enter_FiresOnce_WhenEntityCrossesIn()
        {
            using var harness = RegionHarness.Create(
                """[ { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 50 } ]""");
            Entity entity = harness.SpawnPositioned(0, 0);

            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(0), "Outside must not fire.");

            harness.MoveTo(entity, 100, 100);
            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1));
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(entity));
            Assert.That(harness.Entered[0].RegionId, Is.EqualTo("ring"));
            Assert.That(harness.Exited.Count, Is.EqualTo(0));
        }

        [Test]
        public void Enter_DoesNotRefire_WhileEntityStaysInside()
        {
            using var harness = RegionHarness.Create(
                """[ { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 50 } ]""");
            Entity entity = harness.SpawnPositioned(100, 100);

            for (int i = 0; i < 4; i++)
            {
                harness.Tick();
            }

            Assert.That(harness.Entered.Count, Is.EqualTo(1), "Initial occupancy fires enter exactly once.");
            Assert.That(harness.Exited.Count, Is.EqualTo(0));
        }

        [Test]
        public void Exit_Fires_WhenEntityLeaves()
        {
            using var harness = RegionHarness.Create(
                """[ { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 50 } ]""");
            Entity entity = harness.SpawnPositioned(100, 100);
            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(1));

            harness.MoveTo(entity, 1000, 1000);
            harness.Tick();

            Assert.That(harness.Exited.Count, Is.EqualTo(1));
            Assert.That(harness.Exited[0].Entity, Is.EqualTo(entity));
            Assert.That(harness.Exited[0].RegionId, Is.EqualTo("ring"));
        }

        [Test]
        public void DeadEntity_LeavesInsideSetSilently_WithoutExitEvent()
        {
            using var harness = RegionHarness.Create(
                """[ { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 50 } ]""");
            Entity entity = harness.SpawnPositioned(100, 100);
            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(1));

            harness.World.Destroy(entity);
            harness.Tick();

            Assert.That(harness.Exited.Count, Is.EqualTo(0), "Dead entities leave the inside-set without RegionExited.");

            Entity revived = harness.SpawnPositioned(100, 100);
            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(2), "The dead slot must not poison the inside-set for new entities.");
            Assert.That(harness.Entered[1].Entity, Is.EqualTo(revived));
        }

        [Test]
        public void RectContainment_BoundaryCountsAsInside()
        {
            using var harness = RegionHarness.Create(
                """[ { "id": "yard", "shape": "rect", "x": 100, "y": 100, "halfWidthCm": 50, "halfHeightCm": 40 } ]""");
            Entity boundary = harness.SpawnPositioned(150, 140);
            Entity justOutside = harness.SpawnPositioned(151, 140);

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1), "Positions exactly on the rect boundary count as inside.");
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(boundary));

            harness.MoveTo(boundary, 49, 60);
            harness.Tick();

            Assert.That(harness.Exited.Count, Is.EqualTo(1), "Leaving across the boundary fires exit.");
            Assert.That(justOutside, Is.Not.EqualTo(boundary));
        }

        [Test]
        public void TagFilteredRegion_IgnoresUntaggedEntities()
        {
            int tagId = TagRegistry.Register(TrackedTagName);
            using var harness = RegionHarness.Create(
                $$"""[ { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 50, "entityTags": [ "{{TrackedTagName}}" ] } ]""");
            Entity untagged = harness.SpawnPositioned(100, 100);
            Entity tagged = harness.SpawnPositionedTagged(120, 100, tagId);

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1), "Only entities carrying a declared tag are tracked.");
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(tagged));
            Assert.That(harness.Entered[0].Entity, Is.Not.EqualTo(untagged));
        }

        [Test]
        public void UnknownEntityTag_ThrowsNamingMapRegionTag()
        {
            Assert.That(
                TagRegistry.GetId(UnknownTagName),
                Is.EqualTo(TagRegistry.InvalidId),
                "Precondition: the probe tag must not be registered.");

            using var harness = RegionHarness.Create(
                $$"""[ { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 50, "entityTags": [ "{{UnknownTagName}}" ] } ]""");

            string? message = null;
            try
            {
                harness.Tick();
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
            Assert.That(message, Does.Contain("ring"));
            Assert.That(message, Does.Contain(UnknownTagName));
        }

        [Test]
        public void Enter_WaitsForHeartbeatBoundary_DefaultInterval30()
        {
            using var harness = RegionHarness.Create(
                """[ { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 50 } ]""",
                thinkWaveIntervalTicks: MapHeartbeatClockSystem.DefaultIntervalTicks);
            Entity entity = harness.SpawnPositioned(100, 100);

            for (int i = 0; i < MapHeartbeatClockSystem.DefaultIntervalTicks - 1; i++)
            {
                harness.Tick();
            }

            Assert.That(harness.Entered.Count, Is.EqualTo(0), "No evaluation before the think-wave boundary.");

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1), "Evaluation happens on the 30th tick.");
        }

        [Test]
        public void SuspendedSession_NeitherAccumulatesNorEvaluates()
        {
            using var harness = RegionHarness.Create(
                """[ { "id": "ring", "shape": "circle", "x": 100, "y": 100, "radiusCm": 50 } ]""",
                thinkWaveIntervalTicks: MapHeartbeatClockSystem.DefaultIntervalTicks);
            Entity entity = harness.SpawnPositioned(100, 100);
            harness.Session.State = MapSessionState.Suspended;

            for (int i = 0; i < 40; i++)
            {
                harness.Tick();
            }

            Assert.That(harness.Entered.Count, Is.EqualTo(0), "Suspended maps must not evaluate regions.");

            harness.Session.State = MapSessionState.Active;
            const int interval = MapHeartbeatClockSystem.DefaultIntervalTicks;
            for (int i = 0; i < interval - 1; i++)
            {
                harness.Tick();
            }

            Assert.That(harness.Entered.Count, Is.EqualTo(0), "Resume must restart accumulation, not fire a stale wave.");

            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(1));
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(entity));
        }

        private readonly record struct RegionEvent(Entity Entity, string RegionId);

        private sealed class RegionHarness : IDisposable
        {
            private RegionHarness(
                World world,
                MapSession session,
                TriggerManager triggers,
                MapHeartbeatClockSystem pump,
                RegionTriggerSystem system,
                List<RegionEvent> entered,
                List<RegionEvent> exited)
            {
                World = world;
                Session = session;
                Triggers = triggers;
                Pump = pump;
                System = system;
                Entered = entered;
                Exited = exited;
            }

            public World World { get; }
            public MapSession Session { get; }
            public TriggerManager Triggers { get; }
            public MapHeartbeatClockSystem Pump { get; }
            public RegionTriggerSystem System { get; }
            public List<RegionEvent> Entered { get; }
            public List<RegionEvent> Exited { get; }

            public static RegionHarness Create(string regionsJson, int thinkWaveIntervalTicks = 1)
            {
                var world = World.Create();
                var sessions = new MapSessionManager();
                var config = new MapConfig { Id = MapId };
                config.Regions = JsonNode.Parse(regionsJson);
                config.HeartbeatIntervalTicks = thinkWaveIntervalTicks;
                MapSession session = sessions.CreateSession(new MapId(MapId), config);
                var triggers = new TriggerManager();
                var entered = new List<RegionEvent>();
                var exited = new List<RegionEvent>();
                triggers.RegisterEventHandler(GameEvents.RegionEntered, ctx => Capture(entered, ctx));
                triggers.RegisterEventHandler(GameEvents.RegionExited, ctx => Capture(exited, ctx));
                var pump = new MapHeartbeatClockSystem(() => sessions, world, triggers, () => new ScriptContext());
                var system = new RegionTriggerSystem(world, () => sessions, triggers, () => new ScriptContext());
                system.Initialize();
                return new RegionHarness(world, session, triggers, pump, system, entered, exited);
            }

            public Entity SpawnPositioned(int xCm, int yCm)
            {
                return World.Create(
                    new Ludots.Core.Components.MapEntity { MapId = new MapId(MapId) },
                    new Ludots.Core.Components.WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) });
            }

            public Entity SpawnPositionedTagged(int xCm, int yCm, int tagId)
            {
                var tags = new GameplayTagContainer();
                tags.AddTag(tagId);
                return World.Create(
                    new Ludots.Core.Components.MapEntity { MapId = new MapId(MapId) },
                    new Ludots.Core.Components.WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) },
                    tags);
            }

            public void MoveTo(Entity entity, int xCm, int yCm)
            {
                World.Set(entity, new Ludots.Core.Components.WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) });
            }

            public void Tick()
            {
                Pump.Update(1 / 60f);
                System.Update(1 / 60f);
            }

            private static Task Capture(List<RegionEvent> sink, ScriptContext context)
            {
                sink.Add(new RegionEvent(
                    context.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity),
                    context.Get<string>(MapTriggerEventPayloadKeys.RegionId)));
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                World.Dispose();
            }
        }
    }
}
