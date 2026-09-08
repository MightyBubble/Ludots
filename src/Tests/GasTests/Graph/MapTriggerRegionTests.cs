using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
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
    /// <summary>
    /// Region volume authoring through the real placement pipeline: template
    /// components parsed by the ComponentRegistry setters, entities built by
    /// EntityBuilder (whole-component overrides included), semantic validation and
    /// catalog derivation by the post-placement bake pass, evaluation by
    /// RegionVolumeTriggerSystem.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class MapTriggerRegionTests
    {
        private const string MapId = "map_region_probe";
        private const string TrackedTagName = "Region.Tracked.Probe";
        private const string PoisonEnteredEventName = "demo.poison.entered";
        private const string PoisonExitedEventName = "demo.poison.exited";

        [Test]
        public void Template_CircleWithoutRadiusCm_Rejected()
        {
            BuildFailure(
                """{ "RegionVolumeCm": { "volumeKey": "ring", "shape": "circle" } }""",
                message => Assert.That(message, Does.Contain("radiusCm")));
        }

        [Test]
        public void Template_MissingVolumeKey_Rejected()
        {
            BuildFailure(
                """{ "RegionVolumeCm": { "shape": "circle", "radiusCm": 10 } }""",
                message => Assert.That(message, Does.Contain("volumeKey")));
        }

        [Test]
        public void Template_RectMissingHalfHeightCm_Rejected()
        {
            BuildFailure(
                """{ "RegionVolumeCm": { "volumeKey": "yard", "shape": "rect", "halfWidthCm": 50 } }""",
                message => Assert.That(message, Does.Contain("halfHeightCm")));
        }

        [Test]
        public void Template_UnknownField_Rejected()
        {
            BuildFailure(
                """{ "RegionVolumeCm": { "volumeKey": "ring", "shape": "circle", "radiusCm": 10, "priority": 1 } }""",
                message => Assert.That(message, Does.Contain("priority")));
        }

        [Test]
        public void Template_NonConvexPolygon_Rejected()
        {
            BuildFailure(
                """{ "RegionVolumeCm": { "volumeKey": "dart", "shape": "polygon", "points": [[0,0],[200,0],[100,100],[200,200],[0,200]] } }""",
                message => Assert.That(message, Does.Contain("convex")));
        }

        [Test]
        public void Template_PolygonWithTwoPoints_Rejected()
        {
            BuildFailure(
                """{ "RegionVolumeCm": { "volumeKey": "line", "shape": "polygon", "points": [[0,0],[100,0]] } }""",
                message => Assert.That(message, Does.Contain("3 points")));
        }

        [Test]
        public void Template_SegmentWithCoincidentEndpoints_Rejected()
        {
            BuildFailure(
                """{ "RegionVolumeCm": { "volumeKey": "wall", "shape": "segment", "ax": 10, "ay": 10, "bx": 10, "by": 10, "halfThicknessCm": 5 } }""",
                message => Assert.That(message, Does.Contain("coincide")));
        }

        [Test]
        public void Template_EmissionWithUnknownField_Rejected()
        {
            BuildFailure(
                """{ "RegionVolumeEmissionCm": { "once": true } }""",
                message => Assert.That(message, Does.Contain("once")));
        }

        [Test]
        public void Template_EmissionWithoutAnySide_Rejected()
        {
            BuildFailure(
                """{ "RegionVolumeEmissionCm": { } }""",
                message => Assert.That(message, Does.Contain("at least one")));
        }

        [Test]
        public void Template_PayloadWithBooleanValue_Rejected()
        {
            BuildFailure(
                $$"""{ "RegionVolumeEmissionCm": { "enter": "{{PoisonEnteredEventName}}", "payload": { "poison.armed": true } } }""",
                message => Assert.That(message, Does.Contain("poison.armed")));
        }

        [Test]
        public void Template_UnknownEntityTag_AutoRegistersLikeGameplayTagContainer()
        {
            Assert.That(
                TagRegistry.GetId("Region.AutoRegister.Probe"),
                Is.EqualTo(TagRegistry.InvalidId),
                "Precondition: the probe tag must not be registered.");

            // Authoring-time alignment with SetGameplayTagContainer: unknown tags
            // auto-register while the registry is unfrozen; rejection only applies
            // post-freeze (runtime spawn), same contract as every other tag authoring.
            using var harness = RegionHarness.Create(
                """{ "RegionVolumeTagFilterCm": { "tags": [ "Region.AutoRegister.Probe" ] } }""");
            Assert.That(harness.Session.RegionVolumeKeys, Is.Empty,
                "A bare tag filter authoring no volume yields an empty catalog.");
            Assert.That(TagRegistry.GetId("Region.AutoRegister.Probe"), Is.Not.EqualTo(TagRegistry.InvalidId),
                "The tag must be registered by the setter, mirroring GameplayTagContainer.");
        }

        [Test]
        public void Bake_UnknownCustomEvent_RejectedWithVocabulary()
        {
            BakeFailure(
                """{ "RegionVolumeCm": { "volumeKey": "ring", "shape": "circle", "radiusCm": 10 }, "RegionVolumeEmissionCm": { "enter": "demo.nope.entered" } }""",
                message =>
                {
                    Assert.That(message, Does.Contain("demo.nope.entered"));
                    Assert.That(message, Does.Contain("custom"));
                });
        }

        [Test]
        public void Bake_EngineEventWithPayload_Rejected()
        {
            BakeFailure(
                """{ "RegionVolumeCm": { "volumeKey": "ring", "shape": "circle", "radiusCm": 10 }, "RegionVolumeEmissionCm": { "enter": "RegionEntered", "payload": { "poison.zone": "east" } } }""",
                message => Assert.That(message, Does.Contain("requires at least one custom event")),
                withPoisonSchema: true);
        }

        [Test]
        public void Bake_ReservedPayloadKey_Rejected()
        {
            BakeFailure(
                $$"""{ "RegionVolumeCm": { "volumeKey": "ring", "shape": "circle", "radiusCm": 10 }, "RegionVolumeEmissionCm": { "enter": "{{PoisonEnteredEventName}}", "payload": { "MapTrigger.RegionId": "ring" } } }""",
                message => Assert.That(message, Does.Contain("reserved")),
                withPoisonSchema: true);
        }

        [Test]
        public void Bake_PayloadKeyNotDeclaredBySchema_Rejected()
        {
            BakeFailure(
                $$"""{ "RegionVolumeCm": { "volumeKey": "ring", "shape": "circle", "radiusCm": 10 }, "RegionVolumeEmissionCm": { "enter": "{{PoisonEnteredEventName}}", "payload": { "poison.zone": "east", "poison.unknown": 1 } } }""",
                message =>
                {
                    Assert.That(message, Does.Contain("poison.unknown"));
                    Assert.That(message, Does.Contain("not declared"));
                },
                withPoisonSchema: true);
        }

        [Test]
        public void Bake_PayloadTypeMismatch_Rejected()
        {
            BakeFailure(
                $$"""{ "RegionVolumeCm": { "volumeKey": "ring", "shape": "circle", "radiusCm": 10 }, "RegionVolumeEmissionCm": { "enter": "{{PoisonEnteredEventName}}", "payload": { "poison.zone": "east", "poison.dps": "five" } } }""",
                message =>
                {
                    Assert.That(message, Does.Contain("poison.dps"));
                    Assert.That(message, Does.Contain("declares"));
                },
                withPoisonSchema: true);
        }

        [Test]
        public void Bake_RequiredParamMissing_Rejected()
        {
            BakeFailure(
                $$"""{ "RegionVolumeCm": { "volumeKey": "ring", "shape": "circle", "radiusCm": 10 }, "RegionVolumeEmissionCm": { "enter": "{{PoisonEnteredEventName}}", "payload": { "poison.dps": 5 } } }""",
                message =>
                {
                    Assert.That(message, Does.Contain("poison.zone"));
                    Assert.That(message, Does.Contain("missing required"));
                },
                withPoisonSchema: true);
        }

        [Test]
        public void Bake_DuplicateVolumeKey_Rejected()
        {
            string? message = null;
            try
            {
                using var harness = RegionHarness.Create(
                    VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""));
                harness.SpawnVolumeEntity(
                    "ring",
                    new RegionVolumeShape { Kind = RegionVolumeShapeKind.Circle, Radius = Fix64.FromFloat(50f) },
                    Fix64Vec2.FromInt(300, 300));
                RegionVolumeBakePass.Bake(
                    harness.World,
                    harness.Session,
                    new CustomEventNameRegistry(),
                    new EventSchemaRegistry());
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("duplicate"));
            Assert.That(message, Does.Contain("ring"));
        }

        [Test]
        public void Bake_VolumeWithoutWorldPosition_Rejected()
        {
            var world = World.Create();
            try
            {
                var sessions = new MapSessionManager();
                var config = new MapConfig { Id = MapId };
                MapSession session = sessions.CreateSession(new MapId(MapId), config);
                world.Create(
                    new MapEntity { MapId = new MapId(MapId) },
                    new RegionVolumeCm
                    {
                        VolumeKey = "adrift",
                        Shape = new RegionVolumeShape { Kind = RegionVolumeShapeKind.Circle, Radius = Fix64.FromFloat(10f) },
                    });

                string? message = null;
                try
                {
                    RegionVolumeBakePass.Bake(world, session, new CustomEventNameRegistry(), new EventSchemaRegistry());
                }
                catch (InvalidOperationException ex)
                {
                    message = ex.Message;
                }

                Assert.That(message, Is.Not.Null);
                Assert.That(message, Does.Contain("adrift"));
                Assert.That(message, Does.Contain("WorldPositionCm"));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void Enter_FiresOnce_WhenEntityCrossesIn()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""));
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
                VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""));
            harness.SpawnPositioned(100, 100);

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
                VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""));
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
        public void Override_ReplacesShape_WholeComponent()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""),
                overridesJson: """{ "RegionVolumeCm": { "volumeKey": "yard", "shape": "rect", "halfWidthCm": 50, "halfHeightCm": 40 } }""");
            Entity insideRect = harness.SpawnPositioned(130, 130);
            Entity insideCircleOnly = harness.SpawnPositioned(100, 148);

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1), "Only the overridden rect shape decides containment.");
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(insideRect));
            Assert.That(harness.Entered[0].RegionId, Is.EqualTo("yard"), "The override carries its own VolumeKey.");
        }

        [Test]
        public void Catalog_KeyedByVolumeKey_NotInstanceId()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(100, 100, """{ "volumeKey": "raid_circle", "shape": "circle", "radiusCm": 50 }"""));

            Assert.That(harness.Session.RegionVolumeKeys, Does.Contain("raid_circle"));
            Assert.That(harness.Session.RegionVolumeKeys, Does.Not.Contain("volume_probe"), "The placement InstanceId never keys the catalog.");
        }

        [Test]
        public void DeadEntity_LeavesInsideSetSilently_WithoutExitEvent()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""));
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
                VolumeAt(100, 100, """{ "volumeKey": "yard", "shape": "rect", "halfWidthCm": 50, "halfHeightCm": 40 }"""));
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
        public void SegmentContainment_BoundaryCountsAsInside()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(100, 0, """{ "volumeKey": "wall", "shape": "segment", "ax": 0, "ay": 0, "bx": 0, "by": 400, "halfThicknessCm": 10 }"""));
            Entity onWall = harness.SpawnPositioned(110, 200);
            Entity justOutside = harness.SpawnPositioned(111, 200);

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1), "Exactly half-thickness away from the segment axis counts as inside.");
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(onWall));
            Assert.That(harness.Entered[0].Entity, Is.Not.EqualTo(justOutside));
        }

        [Test]
        public void PolygonContainment_EnterAndExit()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(0, 0, """{ "volumeKey": "yard", "shape": "polygon", "points": [[0,0],[400,0],[400,300],[0,300]] }"""));
            Entity inside = harness.SpawnPositioned(200, 150);
            Entity outside = harness.SpawnPositioned(500, 150);

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1));
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(inside));
            Assert.That(harness.Entered[0].Entity, Is.Not.EqualTo(outside));

            harness.MoveTo(inside, 500, 150);
            harness.Tick();

            Assert.That(harness.Exited.Count, Is.EqualTo(1));
            Assert.That(harness.Exited[0].RegionId, Is.EqualTo("yard"));
        }

        [Test]
        public void TagFilteredVolume_IgnoresUntaggedEntities()
        {
            int tagId = TagRegistry.Register(TrackedTagName);
            using var harness = RegionHarness.Create(
                VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }""",
                    extraComponents: $$"""{ "RegionVolumeTagFilterCm": { "tags": [ "{{TrackedTagName}}" ] } }"""));
            Entity untagged = harness.SpawnPositioned(100, 100);
            Entity tagged = harness.SpawnPositionedTagged(120, 100, tagId);

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1), "Only entities carrying a declared tag are tracked.");
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(tagged));
            Assert.That(harness.Entered[0].Entity, Is.Not.EqualTo(untagged));
        }

        [Test]
        public void Enter_EvaluatesOnFirstFixedStep()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""),
                thinkWaveIntervalTicks: 30);
            harness.SpawnPositioned(100, 100);

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1));
        }

        [Test]
        public void SuspendedSession_NeitherAccumulatesNorEvaluates()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""),
                thinkWaveIntervalTicks: 30);
            Entity entity = harness.SpawnPositioned(100, 100);
            harness.Session.State = MapSessionState.Suspended;

            for (int i = 0; i < 40; i++)
            {
                harness.Tick();
            }

            Assert.That(harness.Entered.Count, Is.EqualTo(0), "Suspended maps must not evaluate regions.");

            harness.Session.State = MapSessionState.Active;
            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(1));
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(entity));
        }

        [Test]
        public void CustomEmission_FiresDeclaredEventWithAuthoredPayload()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(500, 500, """{ "volumeKey": "poison", "shape": "rect", "halfWidthCm": 80, "halfHeightCm": 60 }""",
                    extraComponents: $$"""{ "RegionVolumeEmissionCm": { "enter": "{{PoisonEnteredEventName}}", "exit": "{{PoisonExitedEventName}}", "payload": { "poison.zone": "east", "poison.dps": 5 } } }"""),
                withPoisonSchema: true);
            Entity entity = harness.SpawnPositioned(500, 500);

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(0), "Engine RegionEntered is replaced by the custom enter event.");
            Assert.That(harness.PoisonEntered.Count, Is.EqualTo(1));
            Assert.That(harness.PoisonEntered[0].Entity, Is.EqualTo(entity), "The crossing entity rides MapTrigger.SourceEntity.");
            Assert.That(harness.PoisonEntered[0].Zone, Is.EqualTo("east"));
            Assert.That(harness.PoisonEntered[0].Dps, Is.EqualTo(5f), "Authored int literal widens to the float param.");

            harness.MoveTo(entity, 900, 900);
            harness.Tick();

            Assert.That(harness.PoisonExited.Count, Is.EqualTo(1));
            Assert.That(harness.PoisonExited[0].Zone, Is.EqualTo("east"));
        }

        [Test]
        public void VolumeEntityDestroyed_OccupantsReceiveExit()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(100, 100, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""));
            Entity entity = harness.SpawnPositioned(100, 100);
            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(1));

            harness.DestroyVolume("ring");
            harness.Tick();

            Assert.That(harness.Exited.Count, Is.EqualTo(1), "A destroyed volume exits its alive occupants.");
            Assert.That(harness.Exited[0].Entity, Is.EqualTo(entity));
            Assert.That(harness.Exited[0].RegionId, Is.EqualTo("ring"));
        }

        [Test]
        public void RuntimeSpawnedVolume_JoinsOnNextWave()
        {
            using var harness = RegionHarness.Create(null);

            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(0));

            harness.SpawnVolumeEntity(
                "runtime_ring",
                new RegionVolumeShape
                {
                    Kind = RegionVolumeShapeKind.Circle,
                    Radius = Fix64.FromFloat(50f),
                },
                Fix64Vec2.FromInt(100, 100));
            Entity entity = harness.SpawnPositioned(100, 100);

            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1), "A volume spawned at runtime is evaluated on the next wave.");
            Assert.That(harness.Entered[0].RegionId, Is.EqualTo("runtime_ring"));
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(entity));
        }

        [Test]
        public void MovingVolume_ExitsOccupantsLeftBehind()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(100, 100, """{ "volumeKey": "aura", "shape": "circle", "radiusCm": 50 }"""));
            Entity entity = harness.SpawnPositioned(100, 100);
            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(1));

            harness.MoveVolume("aura", 500, 500);
            harness.Tick();

            Assert.That(harness.Exited.Count, Is.EqualTo(1), "Moving the volume anchor away exits occupants left behind.");
            Assert.That(harness.Exited[0].Entity, Is.EqualTo(entity));
        }

        [Test]
        public void FastCrossing_ThroughCircle_FiresEnterExitPairWithoutOccupancy()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(500, 0, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""));
            Entity mover = harness.SpawnPositioned(0, 0);
            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(0), "Precondition: outside.");

            harness.SweepTo(mover, 1000, 0);
            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1),
                "A travel segment crossing the volume between waves must fire enter even though the mover is outside at sample time.");
            Assert.That(harness.Entered[0].Entity, Is.EqualTo(mover));
            Assert.That(harness.Exited.Count, Is.EqualTo(1),
                "The transient crossing pairs its enter with an exit in the same wave.");
            Assert.That(harness.Exited[0].Entity, Is.EqualTo(mover));

            harness.SweepTo(mover, 2000, 0);
            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(1),
                "A path that never crossed the volume fires nothing further.");
        }

        [Test]
        public void FastCrossing_ThroughThinSegment_FiresEnterExitPair()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(0, 0, """{ "volumeKey": "tripwire", "shape": "segment", "ax": -400, "ay": 0, "bx": 400, "by": 0, "halfThicknessCm": 5 }"""));
            Entity mover = harness.SpawnPositioned(0, -600);
            harness.Tick();

            harness.SweepTo(mover, 0, 600);
            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(1),
                "A thin tripwire must catch a sweep straight through it.");
            Assert.That(harness.Exited.Count, Is.EqualTo(1));
        }

        [Test]
        public void Teleport_WithoutPreviousPosition_StaysPointSampled()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(500, 0, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""));
            Entity mover = harness.SpawnPositioned(0, 0);
            harness.Tick();

            harness.MoveTo(mover, 1000, 0);
            harness.Tick();

            Assert.That(harness.Entered.Count, Is.EqualTo(0),
                "Without PreviousWorldPositionCm the legacy point-sampling contract holds: a same-wave crossing is invisible.");
        }

        [Test]
        public void LeavingOccupant_SweepDoesNotDoubleFire()
        {
            using var harness = RegionHarness.Create(
                VolumeAt(500, 0, """{ "volumeKey": "ring", "shape": "circle", "radiusCm": 50 }"""));
            Entity mover = harness.SpawnPositioned(500, 0);
            harness.Tick();
            Assert.That(harness.Entered.Count, Is.EqualTo(1), "Precondition: occupied.");

            harness.SweepTo(mover, 1000, 0);
            harness.Tick();

            Assert.That(harness.Exited.Count, Is.EqualTo(1),
                "An occupant leaving fires exactly one exit; the swept path out must not add an enter+exit pair.");
            Assert.That(harness.Entered.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Builds the template components JSON with the volume anchored at
        /// (x, y) through a WorldPositionCm component, optionally appending extra
        /// component JSON (tag filter / emission) after the volume component.
        /// </summary>
        private static string VolumeAt(int x, int y, string regionVolumeJson, string? extraComponents = null)
        {
            string volumeBody = regionVolumeJson.TrimStart('{').TrimEnd('}').Trim();
            string self = $$"""{ "RegionVolumeCm": { {{volumeBody}} }, "WorldPositionCm": { "Value": { "X": {{x}}, "Y": {{y}} } }""";
            return extraComponents == null
                ? self + " }"
                : self + ", " + extraComponents.TrimStart('{').TrimEnd('}') + " }";
        }

        private static void BuildFailure(string templateComponentsJson, Action<string> assertMessage)
        {
            string? message = null;
            try
            {
                using var harness = RegionHarness.Create(templateComponentsJson);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            assertMessage(message!);
        }

        private static void BakeFailure(string templateComponentsJson, Action<string> assertMessage, bool withPoisonSchema = false)
        {
            string anchored = templateComponentsJson.TrimEnd('}') +
                """, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } }""";
            string? message = null;
            try
            {
                using var harness = RegionHarness.Create(anchored, withPoisonSchema: withPoisonSchema);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            assertMessage(message!);
        }

        private readonly record struct RegionEvent(Entity Entity, string RegionId);

        private readonly record struct PoisonEvent(Entity Entity, string Zone, float Dps);

        private sealed class RegionHarness : IDisposable
        {
            private static readonly QueryDescription VolumeQuery = new QueryDescription()
                .WithAll<MapEntity, RegionVolumeCm>();

            private RegionHarness(
                World world,
                MapSession session,
                TriggerManager triggers,
                RegionVolumeTriggerSystem system,
                List<RegionEvent> entered,
                List<RegionEvent> exited,
                List<PoisonEvent> poisonEntered,
                List<PoisonEvent> poisonExited)
            {
                World = world;
                Session = session;
                Triggers = triggers;
                System = system;
                Entered = entered;
                Exited = exited;
                PoisonEntered = poisonEntered;
                PoisonExited = poisonExited;
            }

            public World World { get; }
            public MapSession Session { get; }
            public TriggerManager Triggers { get; }
            public RegionVolumeTriggerSystem System { get; }
            public List<RegionEvent> Entered { get; }
            public List<RegionEvent> Exited { get; }
            public List<PoisonEvent> PoisonEntered { get; }
            public List<PoisonEvent> PoisonExited { get; }

            public static RegionHarness Create(
                string? templateComponentsJson,
                string? extraComponents = null,
                string? overridesJson = null,
                int anchorXCm = 0,
                int anchorYCm = 0,
                int thinkWaveIntervalTicks = 1,
                bool withPoisonSchema = false)
            {
                var world = World.Create();
                var sessions = new MapSessionManager();
                var config = new MapConfig { Id = MapId };
                config.HeartbeatIntervalTicks = thinkWaveIntervalTicks;
                MapSession session = sessions.CreateSession(new MapId(MapId), config);
                var triggers = new TriggerManager();

                var customEvents = new CustomEventNameRegistry();
                var schemas = new EventSchemaRegistry();
                if (withPoisonSchema)
                {
                    customEvents.Register(PoisonEnteredEventName);
                    customEvents.Register(PoisonExitedEventName);
                    schemas.RegisterCustom(new EventSchema(
                        PoisonEnteredEventName,
                        EventScope.Map,
                        new EventParamSchema[]
                        {
                            new("zone", EventParamType.String, "poison.zone"),
                            new("dps", EventParamType.Float, "poison.dps"),
                        }));
                    schemas.RegisterCustom(new EventSchema(
                        PoisonExitedEventName,
                        EventScope.Map,
                        new EventParamSchema[]
                        {
                            new("zone", EventParamType.String, "poison.zone"),
                            new("dps", EventParamType.Float, "poison.dps", Optional: true),
                        }));
                }

                triggers.EventSchemas = schemas;

                var entered = new List<RegionEvent>();
                var exited = new List<RegionEvent>();
                triggers.RegisterEventHandler(GameEvents.RegionEntered, ctx => Capture(entered, ctx));
                triggers.RegisterEventHandler(GameEvents.RegionExited, ctx => Capture(exited, ctx));
                var poisonEntered = new List<PoisonEvent>();
                var poisonExited = new List<PoisonEvent>();
                triggers.RegisterEventHandler(new EventKey(PoisonEnteredEventName), ctx => CapturePoison(poisonEntered, ctx));
                triggers.RegisterEventHandler(new EventKey(PoisonExitedEventName), ctx => CapturePoison(poisonExited, ctx));

                if (templateComponentsJson != null)
                {
                    string merged = extraComponents == null
                        ? templateComponentsJson
                        : templateComponentsJson.TrimEnd('}') + ", " + extraComponents.TrimStart('{');
                    var template = new EntityTemplate { Id = "volume_probe_template" };
                    JsonObject components = JsonNode.Parse(merged)!.AsObject();
                    foreach (var kvp in components)
                    {
                        template.Components[kvp.Key] = kvp.Value!.DeepClone();
                    }

                    var templates = new Dictionary<string, EntityTemplate> { ["volume_probe_template"] = template };
                    var builder = new EntityBuilder(world, templates);
                    builder.UseTemplate("volume_probe_template")
                        .WithEntityContext($"Map '{MapId}' entity 'volume_probe'");
                    if (overridesJson != null)
                    {
                        JsonObject overrides = JsonNode.Parse(overridesJson)!.AsObject();
                        foreach (var kvp in overrides)
                        {
                            builder.WithOverride(kvp.Key, kvp.Value!.DeepClone());
                        }
                    }

                    Entity entity = builder.Build();
                    world.Add(entity, new MapEntity { MapId = new MapId(MapId) });
                }

                session.RegionVolumeKeys = RegionVolumeBakePass.Bake(world, session, customEvents, schemas);
                var system = new RegionVolumeTriggerSystem(world, () => sessions, triggers, () => new ScriptContext());
                system.Initialize();
                return new RegionHarness(world, session, triggers, system, entered, exited, poisonEntered, poisonExited);
            }

            public Entity SpawnPositioned(int xCm, int yCm)
            {
                return World.Create(
                    new MapEntity { MapId = new MapId(MapId) },
                    new WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) });
            }

            public Entity SpawnPositionedTagged(int xCm, int yCm, int tagId)
            {
                var tags = new GameplayTagContainer();
                tags.AddTag(tagId);
                return World.Create(
                    new MapEntity { MapId = new MapId(MapId) },
                    new WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) },
                    tags);
            }

            public Entity SpawnVolumeEntity(string volumeKey, RegionVolumeShape shape, Fix64Vec2 anchor)
            {
                return World.Create(
                    new MapEntity { MapId = new MapId(MapId) },
                    new WorldPositionCm { Value = anchor },
                    new RegionVolumeCm { VolumeKey = volumeKey, Shape = shape });
            }

            public void MoveTo(Entity entity, int xCm, int yCm)
            {
                World.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) });
            }

            /// <summary>
            /// Teleport with a recorded previous position, mirroring how the movement
            /// pipeline maintains PreviousWorldPositionCm each tick (#1475).
            /// </summary>
            public void SweepTo(Entity entity, int xCm, int yCm)
            {
                Fix64Vec2 current = World.Get<WorldPositionCm>(entity).Value;
                if (World.Has<PreviousWorldPositionCm>(entity))
                {
                    World.Set(entity, new PreviousWorldPositionCm { Value = current });
                }
                else
                {
                    World.Add(entity, new PreviousWorldPositionCm { Value = current });
                }

                World.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) });
            }

            public void DestroyVolume(string volumeKey)
            {
                foreach (Entity entity in CollectVolumes())
                {
                    if (World.Get<RegionVolumeCm>(entity).VolumeKey == volumeKey)
                    {
                        World.Destroy(entity);
                        return;
                    }
                }
            }

            public void MoveVolume(string volumeKey, int xCm, int yCm)
            {
                foreach (Entity entity in CollectVolumes())
                {
                    if (World.Get<RegionVolumeCm>(entity).VolumeKey == volumeKey)
                    {
                        World.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) });
                        return;
                    }
                }
            }

            private List<Entity> CollectVolumes()
            {
                var entities = new List<Entity>();
                World.Query(in VolumeQuery, entity => entities.Add(entity));
                return entities;
            }

            public void Tick()
            {
                System.Update(1 / 60f);
            }

            private static Task Capture(List<RegionEvent> sink, ScriptContext context)
            {
                sink.Add(new RegionEvent(
                    context.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity),
                    context.Get<string>(MapTriggerEventPayloadKeys.RegionId)));
                return Task.CompletedTask;
            }

            private static Task CapturePoison(List<PoisonEvent> sink, ScriptContext context)
            {
                sink.Add(new PoisonEvent(
                    context.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity),
                    context.Get<string>("poison.zone"),
                    context.Get<float>("poison.dps")));
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                World.Dispose();
            }
        }
    }
}
