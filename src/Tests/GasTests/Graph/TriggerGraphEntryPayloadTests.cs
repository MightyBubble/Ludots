using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// #1106 named entry payload reads: compile-side key validation against
    /// MapTriggerEventPayloadKeys, the zero-allocation capture table, the entry filters
    /// evaluator's tag (TagId) and exact-instance matching, and fail-closed reads of keys
    /// the entry event did not carry.
    /// </summary>
    [TestFixture]
    public sealed class TriggerGraphEntryPayloadTests
    {
        private static GraphControlFlowDocument ProbeDocument(string payloadKey)
        {
            return new GraphControlFlowDocument
            {
                Id = "Graph.Probe.EntryPayload",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new()
                    {
                        Label = "probe",
                        Event = "EntityAliveCountChanged",
                        Start = "probe_read",
                        Filters = new TriggerGraphEntryFiltersConfig { Team = 2 },
                    },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "probe_read", Op = "LoadEntryPayloadInt", PayloadKey = payloadKey },
                    new() { Id = "probe_halt", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("probe_read", "next", "probe_halt"),
                },
            };
        }

        [Test]
        public void Compile_NamedPayloadKey_ValidKeyCompiles()
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(
                ProbeDocument(MapTriggerEventPayloadKeys.Count));
            Assert.That(result.Diagnostics.Where(d => d.Message.Contains("payloadKey")).ToList(), Is.Empty,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        }

        [TestCase("MapTrigger.NotAKey")]
        [TestCase("")]
        public void Compile_NamedPayloadKey_UnknownKeyFailsClosed(string payloadKey)
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(ProbeDocument(payloadKey));
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("payloadKey") &&
                d.Message.Contains("MapTriggerEventPayloadKeys")), Is.True,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void EntryPayloadTable_TypedRoundTripAndMismatch()
        {
            var table = new GraphEntryPayloadTable();
            table.SetInt(MapTriggerEventPayloadKeys.SourceTeamId, 7);
            table.SetFloat(MapTriggerEventPayloadKeys.Magnitude, 1.5f);

            Assert.That(table.TryGetInt(MapTriggerEventPayloadKeys.SourceTeamId, out int intValue), Is.True);
            Assert.That(intValue, Is.EqualTo(7));
            Assert.That(table.TryGetFloat(MapTriggerEventPayloadKeys.Magnitude, out float floatValue), Is.True);
            Assert.That(floatValue, Is.EqualTo(1.5f));
            Assert.That(table.TryGetInt(MapTriggerEventPayloadKeys.Count, out _), Is.False,
                "a key that was never captured must read false");
            Assert.Throws<InvalidOperationException>(() => table.TryGetInt(MapTriggerEventPayloadKeys.Magnitude, out _),
                "reading a float slot as int is a contract violation, not a silent zero");
            Assert.Throws<InvalidOperationException>(() => table.TryGetFloat(MapTriggerEventPayloadKeys.SourceTeamId, out _),
                "reading an int slot as float is a contract violation, not a silent zero");

            table.Clear();
            Assert.That(table.TryGetInt(MapTriggerEventPayloadKeys.SourceTeamId, out _), Is.False);
        }

        [Test]
        public void Evaluator_TagFilter_MatchesTagIdPayload()
        {
            int tagId = TagRegistry.Register("Gameplay.Burning");
            var context = new ScriptContext();
            context.Set(MapTriggerEventPayloadKeys.TagId, tagId + 1);
            var filters = new TriggerGraphEntryFilters(region: null, tag: "Gameplay.Burning", null, null, null, instanceId: null, tagId: tagId);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, filters), Is.False,
                "a different tag id must not match");

            context.Set(MapTriggerEventPayloadKeys.TagId, tagId);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, filters), Is.True);

            var unresolvedTag = new TriggerGraphEntryFilters(null, "Gameplay.NoSuchTag", null, null, null);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, unresolvedTag), Is.False,
                "a tag name that never resolved to an id can never match");
        }

        [Test]
        public void Evaluator_InstanceFilter_RequiresExactSourceInstance()
        {
            var world = World.Create();
            Entity hero = world.Create();
            Entity other = world.Create();
            var index = new Ludots.Core.Systems.MapLoadEntityIndex();
            index.Register("probe_map", "probe_hero", hero);
            index.Register("probe_map", "probe_other", other);

            var session = new MapSession(new MapId("probe_map"), mapConfig: null) { EntityIndex = index };
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.MapSession, session);
            var filters = new TriggerGraphEntryFilters(null, null, null, null, null, instanceId: "probe_hero");

            context.Set(MapTriggerEventPayloadKeys.SourceEntity, other);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, filters), Is.False,
                "an event for a different placed instance must not match");

            context.Set(MapTriggerEventPayloadKeys.SourceEntity, hero);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, filters), Is.True);

            world.Dispose();
        }

        [Test]
        public void Evaluator_VarNameFilter_RequiresExactVariableName()
        {
            var context = new ScriptContext();
            var filters = new TriggerGraphEntryFilters(null, null, null, null, null, varName: "stage");

            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, filters), Is.False,
                "an event without a VarName payload must fail closed");

            context.Set(MapTriggerEventPayloadKeys.VarName, "kill_count");
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, filters), Is.False,
                "an event for a different variable must not match");

            context.Set(MapTriggerEventPayloadKeys.VarName, "stage");
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(context, filters), Is.True);
        }
    }
}
