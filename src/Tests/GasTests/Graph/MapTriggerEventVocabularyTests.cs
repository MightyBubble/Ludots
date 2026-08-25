using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [NonParallelizable]
    public sealed class MapTriggerEventVocabularyTests
    {
        [Test]
        public void PayloadKeys_MatchContractStrings()
        {
            Assert.That(MapTriggerEventPayloadKeys.SourceEntity, Is.EqualTo("MapTrigger.SourceEntity"));
            Assert.That(MapTriggerEventPayloadKeys.SourceTeamId, Is.EqualTo("MapTrigger.SourceTeamId"));
            Assert.That(MapTriggerEventPayloadKeys.RegionId, Is.EqualTo("MapTrigger.RegionId"));
            Assert.That(MapTriggerEventPayloadKeys.Count, Is.EqualTo("MapTrigger.Count"));
            Assert.That(MapTriggerEventPayloadKeys.Delta, Is.EqualTo("MapTrigger.Delta"));
            Assert.That(MapTriggerEventPayloadKeys.VarName, Is.EqualTo("MapTrigger.VarName"));
            Assert.That(MapTriggerEventPayloadKeys.VarValueFloat, Is.EqualTo("MapTrigger.VarValueFloat"));
            Assert.That(MapTriggerEventPayloadKeys.VarValueInt, Is.EqualTo("MapTrigger.VarValueInt"));
            Assert.That(MapTriggerEventPayloadKeys.OldValueInt, Is.EqualTo("MapTrigger.OldValueInt"));
            Assert.That(MapTriggerEventPayloadKeys.OldValueFloat, Is.EqualTo("MapTrigger.OldValueFloat"));
            Assert.That(MapTriggerEventPayloadKeys.HeartbeatIndex, Is.EqualTo("MapTrigger.HeartbeatIndex"));
        }

        [Test]
        public void GameEvents_TriggerGraphEventKeys_UseExactStrings()
        {
            Assert.That(GameEvents.MapHeartbeat.Value, Is.EqualTo("MapHeartbeat"));
            Assert.That(GameEvents.EntitySpawned.Value, Is.EqualTo("EntitySpawned"));
            Assert.That(GameEvents.EntityDied.Value, Is.EqualTo("EntityDied"));
            Assert.That(GameEvents.EntityAliveCountChanged.Value, Is.EqualTo("EntityAliveCountChanged"));
            Assert.That(GameEvents.RegionEntered.Value, Is.EqualTo("RegionEntered"));
            Assert.That(GameEvents.RegionExited.Value, Is.EqualTo("RegionExited"));
            Assert.That(GameEvents.MapVariableChanged.Value, Is.EqualTo("MapVariableChanged"));
        }

        [Test]
        public void GameEvents_Tick_IsRemoved()
        {
            FieldInfo? tickField = typeof(GameEvents).GetField("Tick", BindingFlags.Public | BindingFlags.Static);
            Assert.That(tickField, Is.Null, "GameEvents.Tick must stay deleted; per-frame events are forbidden.");
        }

        [Test]
        public void Filters_Empty_MatchesEverything()
        {
            var context = new ScriptContext();
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, default), Is.True);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, new TriggerGraphEntryFilters(null, null, null, null, null)), Is.True);
        }

        [Test]
        public void Filters_Region_MatchesAndMisses()
        {
            var context = new ScriptContext();
            context.Set(MapTriggerEventPayloadKeys.RegionId, "spawn_zone");
            var filters = new TriggerGraphEntryFilters("spawn_zone", null, null, null, null);

            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in filters), Is.True);

            var miss = new TriggerGraphEntryFilters("boss_zone", null, null, null, null);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in miss), Is.False);
        }

        [Test]
        public void Filters_Region_MissingPayload_FailsClosed()
        {
            var context = new ScriptContext();
            var filters = new TriggerGraphEntryFilters("spawn_zone", null, null, null, null);

            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in filters), Is.False);
        }

        [Test]
        public void Filters_Team_MatchesAndMisses()
        {
            var context = new ScriptContext();
            context.Set(MapTriggerEventPayloadKeys.SourceTeamId, 7);
            var filters = new TriggerGraphEntryFilters(null, null, 7, null, null);

            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in filters), Is.True);

            var miss = new TriggerGraphEntryFilters(null, null, 8, null, null);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in miss), Is.False);

            var missingPayload = new TriggerGraphEntryFilters(null, null, 7, null, null);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(new ScriptContext(), in missingPayload), Is.False);
        }

        [Test]
        public void Filters_ThresholdDirection_CompareCountPayload()
        {
            var above = new TriggerGraphEntryFilters(null, null, null, 5f, TriggerGraphEntryFilterDirection.CrossAbove);
            var below = new TriggerGraphEntryFilters(null, null, null, 5f, TriggerGraphEntryFilterDirection.CrossBelow);

            var context = new ScriptContext();
            context.Set(MapTriggerEventPayloadKeys.Count, 5);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in above), Is.True);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in below), Is.True);

            context.Set(MapTriggerEventPayloadKeys.Count, 6);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in above), Is.True);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in below), Is.False);

            context.Set(MapTriggerEventPayloadKeys.Count, 4);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in above), Is.False);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in below), Is.True);
        }

        [Test]
        public void Filters_Threshold_MissingCountPayload_FailsClosed()
        {
            var context = new ScriptContext();
            context.Set(MapTriggerEventPayloadKeys.SourceTeamId, 1);
            var filters = new TriggerGraphEntryFilters(null, null, null, 5f, TriggerGraphEntryFilterDirection.CrossAbove);

            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in filters), Is.False);
        }

        [Test]
        public void Filters_TagDeclared_NeverMatchesUntilTagBearingEventsExist()
        {
            var context = new ScriptContext();
            context.Set(MapTriggerEventPayloadKeys.RegionId, "anywhere");
            var filters = new TriggerGraphEntryFilters(null, "elite", null, null, null);

            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in filters), Is.False);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(new ScriptContext(), in filters), Is.False);
        }

        [Test]
        public void Filters_Combined_AllDeclaredFiltersMustMatch()
        {
            var context = new ScriptContext();
            context.Set(MapTriggerEventPayloadKeys.RegionId, "spawn_zone");
            context.Set(MapTriggerEventPayloadKeys.SourceTeamId, 3);
            context.Set(MapTriggerEventPayloadKeys.Count, 10);
            var filters = new TriggerGraphEntryFilters("spawn_zone", null, 3, 8f, TriggerGraphEntryFilterDirection.CrossAbove);

            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in filters), Is.True);

            var wrongTeam = new TriggerGraphEntryFilters("spawn_zone", null, 4, 8f, TriggerGraphEntryFilterDirection.CrossAbove);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, in wrongTeam), Is.False);
        }

        [Test]
        public void FrontDoor_FiltersUnknownField_Rejected()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                CompileFrontDoor(
                    """
                    {
                      "kind": "TriggerGraph",
                      "entries": [
                        { "label": "a", "event": "MapLoaded", "start": "a1", "filters": { "zone": "north" } }
                      ],
                      "nodes": [
                        { "id": "a1", "op": "HaltReturnInt" }
                      ],
                      "controlEdges": [],
                      "valueEdges": []
                    }
                    """,
                    "tests.maptrigger.filters-unknown"));

            Assert.That(ex!.Message, Does.Contain("filters has unknown field 'zone'"));
        }

        [Test]
        public void FrontDoor_FiltersNonStringValue_Rejected()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                CompileFrontDoor(
                    """
                    {
                      "kind": "TriggerGraph",
                      "entries": [
                        { "label": "a", "event": "MapLoaded", "start": "a1", "filters": { "region": 5 } }
                      ],
                      "nodes": [
                        { "id": "a1", "op": "HaltReturnInt" }
                      ],
                      "controlEdges": [],
                      "valueEdges": []
                    }
                    """,
                    "tests.maptrigger.filters-type"));

            Assert.That(ex!.Message, Does.Contain("filters field 'region' must be a string"));
        }

        [Test]
        public void Compiler_UnknownDirection_IsRejected()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "a", "event": "EntityAliveCountChanged", "start": "a1",
                      "filters": { "threshold": 5, "direction": "ascending" } }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.maptrigger.filters-direction");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(
                compiled.Diagnostics.Any(d => d.Message.Contains("cross_above")),
                Is.True,
                GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
        }

        [Test]
        public void Compiler_ThresholdWithoutDirection_IsRejected()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "a", "event": "EntityAliveCountChanged", "start": "a1",
                      "filters": { "threshold": 5 } }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.maptrigger.filters-halfpair");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(
                compiled.Diagnostics.Any(d => d.Message.Contains("together")),
                Is.True,
                GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
        }

        [Test]
        public void Compiler_ValidFilters_AreCompiledIntoTheEntry()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "a", "event": "RegionEntered", "start": "a1",
                      "filters": { "region": "spawn_zone", "team": 3, "threshold": 2, "direction": "cross_above" } }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.maptrigger.filters-valid");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            TriggerGraphEntry entry = compiled.Package!.Value.TriggerGraphEntries.Single();
            Assert.That(entry.Filters.Region, Is.EqualTo("spawn_zone"));
            Assert.That(entry.Filters.Tag, Is.Null);
            Assert.That(entry.Filters.Team, Is.EqualTo(3));
            Assert.That(entry.Filters.Threshold, Is.EqualTo(2f));
            Assert.That(entry.Filters.Direction, Is.EqualTo(TriggerGraphEntryFilterDirection.CrossAbove));
        }

        [Test]
        public void Registry_ThresholdDirectionMismatch_Throws()
        {
            GraphIdRegistry.Clear();
            var programs = new GraphProgramRegistry();
            var entry = new TriggerGraphEntry(
                "probe",
                GameEvents.EntityAliveCountChanged.Value,
                0,
                once: false,
                new TriggerGraphEntryFilters(null, null, null, 5f, null));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                programs.Register(
                    GraphIdRegistry.Register("tests.maptrigger.registry-mismatch"),
                    new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 } },
                    GraphKind.TriggerGraph,
                    GraphInstructionSourceMap.Empty,
                    null,
                    new[] { entry }));

            Assert.That(ex!.Message, Does.Contain("together"));
        }

        [Test]
        public void Registry_UnpairedDirection_Throws()
        {
            GraphIdRegistry.Clear();
            var programs = new GraphProgramRegistry();
            var entry = new TriggerGraphEntry(
                "probe",
                GameEvents.EntityAliveCountChanged.Value,
                0,
                once: false,
                new TriggerGraphEntryFilters(null, null, null, null, TriggerGraphEntryFilterDirection.CrossBelow));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                programs.Register(
                    GraphIdRegistry.Register("tests.maptrigger.registry-direction"),
                    new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 } },
                    GraphKind.TriggerGraph,
                    GraphInstructionSourceMap.Empty,
                    null,
                    new[] { entry }));

            Assert.That(ex!.Message, Does.Contain("together"));
        }

        private static GraphControlFlowCompileResult CompileFrontDoor(string json, string graphId)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        }
    }

    [TestFixture]
    [NonParallelizable]
    public sealed class ThinkWaveIntervalParseTests
    {
        [Test]
        public void LoadMap_ValidHeartbeatIntervalTicks_Parses()
        {
            MapConfig? config = LoadMapWithJson("""{ "id": "wave_map", "HeartbeatIntervalTicks": 45 }""");

            Assert.That(config, Is.Not.Null);
            Assert.That(config!.HeartbeatIntervalTicks, Is.EqualTo(45));
        }

        [Test]
        public void LoadMap_MissingHeartbeatIntervalTicks_StaysNull()
        {
            MapConfig? config = LoadMapWithJson("""{ "id": "wave_map" }""");

            Assert.That(config, Is.Not.Null);
            Assert.That(config!.HeartbeatIntervalTicks, Is.Null);
        }

        [Test]
        public void LoadMap_ZeroHeartbeatIntervalTicks_Rejected()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                LoadMapWithJson("""{ "id": "wave_map", "HeartbeatIntervalTicks": 0 }"""));

            Assert.That(ex!.Message, Does.Contain("HeartbeatIntervalTicks"));
        }

        [Test]
        public void LoadMap_FractionalHeartbeatIntervalTicks_Rejected()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                LoadMapWithJson("""{ "id": "wave_map", "HeartbeatIntervalTicks": 2.5 }"""));

            Assert.That(ex!.Message, Does.Contain("HeartbeatIntervalTicks"));
        }

        [Test]
        public void LoadMap_StringHeartbeatIntervalTicks_Rejected()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                LoadMapWithJson("""{ "id": "wave_map", "HeartbeatIntervalTicks": "30" }"""));

            Assert.That(ex!.Message, Does.Contain("HeartbeatIntervalTicks"));
        }

        [Test]
        public void LoadMap_ChildOverridesHeartbeatIntervalTicks()
        {
            string tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """{ "id": "parent", "HeartbeatIntervalTicks": 60 }""");
                WriteMapConfig(tempRoot, "child", """{ "id": "child", "parentId": "parent", "HeartbeatIntervalTicks": 5 }""");

                MapConfig? config = CreateMapManager(tempRoot).LoadMap("child");

                Assert.That(config, Is.Not.Null);
                Assert.That(config!.HeartbeatIntervalTicks, Is.EqualTo(5));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        private static MapConfig? LoadMapWithJson(string json)
        {
            string tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "wave_map", json);
                return CreateMapManager(tempRoot).LoadMap("wave_map");
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        private static MapManager CreateMapManager(string coreRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var trigger = new TriggerManager();
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
            var pipeline = new ConfigPipeline(vfs, modLoader);
            return new MapManager(vfs, trigger, modLoader, pipeline);
        }

        private static void WriteMapConfig(string root, string mapId, string json)
        {
            var mapsDir = Path.Combine(root, "Maps");
            Directory.CreateDirectory(mapsDir);
            File.WriteAllText(Path.Combine(mapsDir, $"{mapId}.json"), json);
        }

        private static string CreateTempDir()
        {
            var path = Path.Combine(Path.GetTempPath(), "ludots_thinkwave_parse_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }

    [TestFixture]
    [NonParallelizable]
    public sealed class MapHeartbeatClockSystemTests
    {
        private const string MapId = "think_wave_probe_map";

        [Test]
        public void MapHeartbeat_FiresAtConfiguredCadence()
        {
            using var harness = new WaveHarness(intervalTicks: 3);

            harness.System.Update(0f);
            harness.System.Update(0f);
            Assert.That(harness.Events.Count, Is.EqualTo(0), "No wave before the interval elapses.");

            harness.System.Update(0f);
            Assert.That(harness.Events.Count, Is.EqualTo(1));
            Assert.That(harness.Events[0].HeartbeatIndex, Is.EqualTo(1));

            harness.System.Update(0f);
            harness.System.Update(0f);
            Assert.That(harness.Events.Count, Is.EqualTo(1), "No second wave before another interval elapses.");

            harness.System.Update(0f);
            Assert.That(harness.Events.Count, Is.EqualTo(2));
            Assert.That(harness.Events[1].HeartbeatIndex, Is.EqualTo(2));
        }

        [Test]
        public void MapHeartbeat_DefaultsToThirtyTicksWhenUndeclared()
        {
            using var harness = new WaveHarness(intervalTicks: null);

            for (int i = 0; i < 29; i++)
            {
                harness.System.Update(0f);
            }

            Assert.That(harness.Events.Count, Is.EqualTo(0));

            harness.System.Update(0f);
            Assert.That(harness.Events.Count, Is.EqualTo(1));
            Assert.That(harness.Events[0].HeartbeatIndex, Is.EqualTo(1));
        }

        [Test]
        public void SuspendedMap_DoesNotAdvance()
        {
            using var harness = new WaveHarness(intervalTicks: 2);

            harness.System.Update(0f);
            harness.Session.State = MapSessionState.Suspended;
            for (int i = 0; i < 5; i++)
            {
                harness.System.Update(0f);
            }

            Assert.That(harness.Events.Count, Is.EqualTo(0), "Suspended maps must not accumulate ticks.");

            harness.Session.State = MapSessionState.Active;
            harness.System.Update(0f);

            Assert.That(harness.Events.Count, Is.EqualTo(1), "Resume continues from the pre-suspend accumulation.");
            Assert.That(harness.Events[0].HeartbeatIndex, Is.EqualTo(1));
        }

        [Test]
        public void EntitySpawned_FiresAtWaveGranularityWithPayload()
        {
            using var harness = new WaveHarness(intervalTicks: 2);

            Entity spawned = harness.CreateMapEntity(teamId: 7);
            harness.System.Update(0f);
            Assert.That(harness.Events.Count, Is.EqualTo(0), "Spawn events wait for the wave flush.");

            harness.System.Update(0f);
            CapturedEvent spawn = harness.Events.Single(e => e.Key == GameEvents.EntitySpawned.Value);
            Assert.That(spawn.SourceEntity, Is.EqualTo(spawned));
            Assert.That(spawn.SourceTeamId, Is.EqualTo(7));
        }

        [Test]
        public void EntitySpawned_EntityDiedWithinSameWave_FiresOnlyDeath()
        {
            using var harness = new WaveHarness(intervalTicks: 2);

            Entity transient = harness.CreateMapEntity(teamId: 5);
            harness.World.Destroy(transient);

            harness.System.Update(0f);
            harness.System.Update(0f);

            Assert.That(
                harness.Events.Count(e => e.Key == GameEvents.EntitySpawned.Value),
                Is.EqualTo(0),
                "Membership-diff spawns are net: an entity that never survived a wave boundary never spawns.");
            CapturedEvent death = harness.Events.Single(e => e.Key == GameEvents.EntityDied.Value);
            Assert.That(death.SourceEntity, Is.EqualTo(transient));
            Assert.That(death.SourceTeamId, Is.EqualTo(5));
        }

        [Test]
        public void EntityDied_FiresWithTeamCapturedAtDestroyTime()
        {
            using var harness = new WaveHarness(intervalTicks: 2);

            Entity victim = harness.CreateMapEntity(teamId: 9);
            harness.System.Update(0f);
            harness.System.Update(0f);

            harness.World.Destroy(victim);
            harness.System.Update(0f);
            Assert.That(harness.Events.Count(e => e.Key == GameEvents.EntityDied.Value), Is.EqualTo(0), "Death events wait for the wave flush.");

            harness.System.Update(0f);
            CapturedEvent death = harness.Events.Single(e => e.Key == GameEvents.EntityDied.Value);
            Assert.That(death.SourceEntity, Is.EqualTo(victim));
            Assert.That(death.SourceTeamId, Is.EqualTo(9), "Team is captured at destroy time so consumers never dereference the dead entity.");
        }

        [Test]
        public void EntityAliveCountChanged_FiresOncePerChangeEdge()
        {
            using var harness = new WaveHarness(intervalTicks: 2);

            Entity[] squad = harness.CreateSquad(teamId: 1, count: 5);
            harness.System.Update(0f);
            harness.System.Update(0f);
            Assert.That(
                harness.Events.Count(e => e.Key == GameEvents.EntityAliveCountChanged.Value),
                Is.EqualTo(0),
                "The first wave records the baseline without firing.");

            for (int i = 0; i < squad.Length; i++)
            {
                harness.World.Destroy(squad[i]);
            }

            harness.System.Update(0f);
            harness.System.Update(0f);
            CapturedEvent[] changeEvents = harness.Events
                .Where(e => e.Key == GameEvents.EntityAliveCountChanged.Value)
                .ToArray();
            Assert.That(changeEvents.Length, Is.EqualTo(1), "5 -> 0 is a single change edge.");
            Assert.That(changeEvents[0].SourceTeamId, Is.EqualTo(1));
            Assert.That(changeEvents[0].Count, Is.EqualTo(0));
            Assert.That(changeEvents[0].Delta, Is.EqualTo(-5));

            harness.System.Update(0f);
            harness.System.Update(0f);
            Assert.That(
                harness.Events.Count(e => e.Key == GameEvents.EntityAliveCountChanged.Value),
                Is.EqualTo(1),
                "0 -> 0 must not fire.");
        }

        [Test]
        public void EntityAliveCountChanged_OnlyCountsEntitiesWithAttributeBuffer()
        {
            using var harness = new WaveHarness(intervalTicks: 2);

            Entity counted = harness.CreateSquad(teamId: 2, count: 1)[0];
            Entity uncounted = harness.CreateMapEntity(teamId: 2);

            harness.System.Update(0f);
            harness.System.Update(0f);
            harness.World.Destroy(counted);
            harness.World.Destroy(uncounted);

            harness.System.Update(0f);
            harness.System.Update(0f);

            CapturedEvent change = harness.Events.Single(e => e.Key == GameEvents.EntityAliveCountChanged.Value);
            Assert.That(change.SourceTeamId, Is.EqualTo(2));
            Assert.That(change.Count, Is.EqualTo(0));
            Assert.That(change.Delta, Is.EqualTo(-1), "The teamless buffer-less entity never counted as alive.");
        }

        [Test]
        public void DeathQueueOverflow_IncrementsLiveDropCounter()
        {
            using var harness = new WaveHarness(intervalTicks: 30);

            const int overflow = 100;
            var victims = new List<Entity>();
            for (int i = 0; i < MapHeartbeatClockSystem.LifecycleQueueCapacity + overflow; i++)
            {
                victims.Add(harness.CreateMapEntity(teamId: 3));
            }

            foreach (Entity victim in victims)
            {
                harness.World.Destroy(victim);
            }

            harness.System.Update(0f);
            Assert.That(
                harness.System.GetDroppedLifecycleEvents(new MapId(MapId)),
                Is.EqualTo(overflow),
                "Dropped deaths must be observable live.");
            Assert.That(harness.System.TotalDroppedLifecycleEvents, Is.EqualTo(overflow));
        }

        [Test]
        public void SpawnDiffOverflow_IncrementsLiveDropCounter()
        {
            using var harness = new WaveHarness(intervalTicks: 1);

            for (int i = 0; i < MapHeartbeatClockSystem.LifecycleQueueCapacity + 10; i++)
            {
                harness.CreateMapEntity(teamId: 1);
            }

            harness.System.Update(0f);

            Assert.That(harness.Events.Count(e => e.Key == GameEvents.EntitySpawned.Value), Is.EqualTo(MapHeartbeatClockSystem.LifecycleQueueCapacity));
            Assert.That(harness.System.GetDroppedLifecycleEvents(new MapId(MapId)), Is.EqualTo(10));
            Assert.That(harness.System.TotalDroppedLifecycleEvents, Is.EqualTo(10));
        }

        private sealed class WaveHarness : IDisposable
        {
            public readonly World World = World.Create();
            public readonly MapSessionManager Sessions = new();
            public readonly TriggerManager Triggers = new();
            public readonly MapHeartbeatClockSystem System;
            public readonly MapSession Session;
            public readonly List<CapturedEvent> Events = new();

            public WaveHarness(int? intervalTicks)
            {
                var config = new MapConfig { Id = MapId };
                if (intervalTicks.HasValue)
                {
                    config.HeartbeatIntervalTicks = intervalTicks;
                }

                Session = Sessions.CreateSession(new MapId(MapId), config);
                Sessions.PushFocused(new MapId(MapId));
                System = new MapHeartbeatClockSystem(() => Sessions, World, Triggers, () => new ScriptContext());
                Register(GameEvents.MapHeartbeat);
                Register(GameEvents.EntitySpawned);
                Register(GameEvents.EntityDied);
                Register(GameEvents.EntityAliveCountChanged);
            }

            public Entity CreateMapEntity(int teamId)
            {
                Arch.Core.World world = World;
                Entity entity = world.Create();
                world.Add(entity, new Team { Id = teamId });
                world.Add(entity, new MapEntity { MapId = new MapId(MapId) });
                return entity;
            }

            public Entity[] CreateSquad(int teamId, int count)
            {
                var squad = new Entity[count];
                Arch.Core.World world = World;
                for (int i = 0; i < count; i++)
                {
                    Entity entity = world.Create();
                    world.Add(entity, new Team { Id = teamId });
                    world.Add(entity, new AttributeBuffer());
                    world.Add(entity, new MapEntity { MapId = new MapId(MapId) });
                    squad[i] = entity;
                }

                return squad;
            }

            private void Register(EventKey key)
            {
                Triggers.RegisterEventHandler(key, context =>
                {
                    Events.Add(new CapturedEvent(
                        key.Value,
                        TryGet(context, MapTriggerEventPayloadKeys.SourceEntity, out Entity source) ? source : null,
                        TryGet(context, MapTriggerEventPayloadKeys.SourceTeamId, out int teamId) ? teamId : null,
                        TryGet(context, MapTriggerEventPayloadKeys.Count, out int count) ? count : null,
                        TryGet(context, MapTriggerEventPayloadKeys.Delta, out int delta) ? delta : null,
                        TryGet(context, MapTriggerEventPayloadKeys.HeartbeatIndex, out int waveIndex) ? waveIndex : null));
                    return Task.CompletedTask;
                });
            }

            private static bool TryGet<T>(ScriptContext context, string key, out T value)
            {
                if (context.Contains(key) && context.Get<object>(key) is T boxed)
                {
                    value = boxed;
                    return true;
                }

                value = default!;
                return false;
            }

            public void Dispose()
            {
                World.Dispose();
            }
        }

        private sealed record CapturedEvent(
            string Key,
            Entity? SourceEntity,
            int? SourceTeamId,
            int? Count,
            int? Delta,
            int? HeartbeatIndex);
    }
}
