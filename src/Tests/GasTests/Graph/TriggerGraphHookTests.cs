using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// #1124 data-driven mod hooks: entry priority feeds dispatch order (ascending,
    /// negative earlier, cross-graph), the Route A weaver splices hook fragments
    /// before/after anchors and node ids with register isolation, hook entries mount
    /// no dispatch triggers, and every authoring mistake (unknown anchor / node,
    /// A↔B cycles, duplicate anchors, unhookable entry roots, unreachable anchors)
    /// fails closed at load.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TriggerGraphHookTests
    {
        private const string TargetGraphId = "Graph.Hook.Target";
        private const string CallerGraphId = "Graph.Hook.Caller";
        private const string SecondCallerGraphId = "Graph.Hook.SecondCaller";
        private const string TargetBeforeEvent = "Probe.Hook.TargetBefore";
        private const string TargetAfterEvent = "Probe.Hook.TargetAfter";
        private const string HookFiredEvent = "Probe.Hook.FragmentFired";
        private const string AmountKey = "Probe.Hook.Amount";

        private sealed class RunProbe
        {
            public List<string> Log { get; } = new();
            public List<int> HookAmounts { get; } = new();
            public List<int> TargetAfterAmounts { get; } = new();

            public Trigger Build(string label, string eventName, List<int>? amountSink)
            {
                return new ProbeTrigger(this, label, new EventKey(eventName), amountSink);
            }

            private sealed class ProbeTrigger : Trigger
            {
                private readonly RunProbe _owner;
                private readonly string _label;
                private readonly List<int>? _amountSink;

                public ProbeTrigger(RunProbe owner, string label, EventKey eventKey, List<int>? amountSink)
                {
                    _owner = owner;
                    _label = label;
                    _amountSink = amountSink;
                    EventKey = eventKey;
                }

                public override Task ExecuteAsync(ScriptContext context)
                {
                    _owner.Log.Add(_label);
                    if (_amountSink != null && context.Contains(AmountKey))
                    {
                        _amountSink.Add(context.Get<int>(AmountKey));
                    }

                    return Task.CompletedTask;
                }
            }
        }

        private static EventSchemaRegistry BuildRegistry()
        {
            var registry = new EventSchemaRegistry();
            registry.RegisterCustom(new EventSchema(TargetBeforeEvent, EventScope.Map, Array.Empty<EventParamSchema>()));
            registry.RegisterCustom(new EventSchema(TargetAfterEvent, EventScope.Map, new EventParamSchema[]
            {
                new("amount", EventParamType.Int, AmountKey),
            }));
            registry.RegisterCustom(new EventSchema(HookFiredEvent, EventScope.Map, new EventParamSchema[]
            {
                new("amount", EventParamType.Int, AmountKey),
            }));
            return registry;
        }

        /// <summary>
        /// Target flow: mark_before → 5 → 7 → middle(anchor, AddInt 5+7) → mark_after(fires
        /// amount=12) → halt. A "before middle" hook must land between mark_before and
        /// middle; "after middle" between middle and mark_after.
        /// </summary>
        private static GraphControlFlowDocument TargetDocument(bool withAnchor = true)
        {
            return new GraphControlFlowDocument
            {
                Id = TargetGraphId,
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "main", Event = "MapLoaded", Start = "mark_before" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "mark_before", Op = "DispatchMapEvent", Event = TargetBeforeEvent },
                    new() { Id = "five", Op = "ConstInt", IntValue = 5 },
                    new() { Id = "seven", Op = "ConstInt", IntValue = 7 },
                    new() { Id = "middle", Op = "AddInt", Anchor = withAnchor ? "hotspot" : null },
                    new() { Id = "mark_after", Op = "DispatchMapEvent", Event = TargetAfterEvent },
                    new() { Id = "done", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("mark_before", "next", "five"),
                    new("five", "next", "seven"),
                    new("seven", "next", "middle"),
                    new("middle", "next", "mark_after"),
                    new("mark_after", "next", "done"),
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("five", "value", "middle", "a"),
                    new("seven", "value", "middle", "b"),
                    new("middle", "value", "mark_after", "amount"),
                },
            };
        }

        private static GraphControlFlowDocument HookCallerDocument(
            string graphId,
            string mode,
            int priority = 0,
            int hookAmount = 99)
        {
            return new GraphControlFlowDocument
            {
                Id = graphId,
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new()
                    {
                        Label = "on_hook",
                        Event = "MapLoaded",
                        Start = "hook_const",
                        Priority = priority,
                        HookAnchor = mode == "hookAnchor" ? new TriggerGraphHookAnchorConfig
                        {
                            GraphId = TargetGraphId,
                            Anchor = "hotspot",
                            Position = "before",
                        } : null,
                        HookNodeBefore = mode == "hookNodeBefore" ? new TriggerGraphHookNodeConfig
                        {
                            GraphId = TargetGraphId,
                            NodeId = "middle",
                        } : null,
                        HookNodeAfter = mode == "hookNodeAfter" ? new TriggerGraphHookNodeConfig
                        {
                            GraphId = TargetGraphId,
                            NodeId = "middle",
                        } : null,
                    },
                    new() { Label = "main", Event = "MapLoaded", Start = "hook_const" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "hook_const", Op = "ConstInt", IntValue = hookAmount },
                    new() { Id = "hook_fire", Op = "DispatchMapEvent", Event = HookFiredEvent },
                    new() { Id = "hook_done", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("hook_const", "next", "hook_fire"),
                    new("hook_fire", "next", "hook_done"),
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("hook_const", "value", "hook_fire", "amount"),
                },
            };
        }

        private sealed class StubResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => throw new InvalidOperationException($"stub resolver: {name}");
            public int ResolveAttribute(string name) => throw new InvalidOperationException($"stub resolver: {name}");
            public int ResolveEffectTemplate(string name) => throw new InvalidOperationException($"stub resolver: {name}");
            public int ResolveRelationshipType(string name) => throw new InvalidOperationException($"stub resolver: {name}");
            public int ResolveRelationshipMetric(string name) => throw new InvalidOperationException($"stub resolver: {name}");
            public int ResolveRelationshipFlag(string name) => throw new InvalidOperationException($"stub resolver: {name}");
            public int ResolveRelationshipReason(string name) => throw new InvalidOperationException($"stub resolver: {name}");
            public int ResolveTargetDispatchPreset(string name) => throw new InvalidOperationException($"stub resolver: {name}");
            public int ResolveEntityTemplate(string name) => throw new InvalidOperationException($"stub resolver: {name}");
        }

        private static int Register(GraphProgramRegistry registry, GraphControlFlowDocument doc, EventSchemaRegistry schemas)
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc, schemas);
            Assert.That(compiled.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error).ToList(), Is.Empty,
                () => string.Join("\n", compiled.Diagnostics.Select(d => d.Message)));
            GraphProgramPackage package = compiled.Package!.Value;
            int id = GraphIdRegistry.Register(doc.Id);
            registry.Register(id, package.Program, GraphKind.TriggerGraph, compiled.SourceMap, package.Symbols, package.TriggerGraphEntries);
            return id;
        }

        // ── Weaving: before / after / node-id hooks ──

        [TestCase("hookAnchor")]
        [TestCase("hookNodeBefore")]
        public void Weave_BeforeHook_RunsFragmentBeforeAnchor(string mode)
        {
            RunProbe probe = WeaveAndRun(mode, out TriggerManager _);

            Assert.That(probe.Log, Is.EqualTo(new[] { "target:before", "hook", "target:after" }),
                "the fragment must run after the anchor's predecessors and before the anchor itself");
            Assert.That(probe.HookAmounts, Is.EqualTo(new[] { 99 }));
            Assert.That(probe.TargetAfterAmounts, Is.EqualTo(new[] { 12 }),
                "the fragment's own registers must not pollute the target's post-anchor math (5+7)");
        }

        [Test]
        public void Weave_AfterHook_RunsFragmentAfterAnchor()
        {
            RunProbe probe = WeaveAndRun("hookNodeAfter", out TriggerManager _);

            Assert.That(probe.Log, Is.EqualTo(new[] { "target:before", "hook", "target:after" }),
                "'after middle' places the fragment between middle and mark_after");
            Assert.That(probe.HookAmounts, Is.EqualTo(new[] { 99 }));
            Assert.That(probe.TargetAfterAmounts, Is.EqualTo(new[] { 12 }));
        }

        [Test]
        public void Weave_PriorityChainsHooksInAscendingOrder()
        {
            var schemas = BuildRegistry();
            var programs = new GraphProgramRegistry();
            GraphControlFlowDocument target = TargetDocument();
            GraphControlFlowDocument late = HookCallerDocument(SecondCallerGraphId, "hookAnchor", priority: 100, hookAmount: 100);
            GraphControlFlowDocument early = HookCallerDocument(CallerGraphId, "hookAnchor", priority: 50, hookAmount: 50);
            Register(programs, target, schemas);
            Register(programs, late, schemas);
            Register(programs, early, schemas);

            var documents = new List<KeyValuePair<string, GraphControlFlowDocument>>
            {
                new(TargetGraphId, target),
                new(SecondCallerGraphId, late),
                new(CallerGraphId, early),
            };
            TriggerGraphHookWeaver.Weave(programs, documents, new StubResolver(), schemas);

            RunProbe probe = ExecuteWovenTarget(programs, schemas);

            Assert.That(probe.Log, Is.EqualTo(new[] { "target:before", "hook", "hook", "target:after" }),
                "two hooks on one anchor chain; priority 50 runs before priority 100");
            Assert.That(probe.HookAmounts, Is.EqualTo(new[] { 50, 100 }),
                "same-priority ties would fall back to compile order; distinct priorities must order ascending");
            Assert.That(probe.TargetAfterAmounts, Is.EqualTo(new[] { 12 }));
        }

        // ── Fail-closed authoring mistakes ──

        [Test]
        public void Weave_UnknownAnchor_FailsClosed()
        {
            var schemas = BuildRegistry();
            var programs = new GraphProgramRegistry();
            GraphControlFlowDocument target = TargetDocument(withAnchor: false);
            GraphControlFlowDocument caller = HookCallerDocument(CallerGraphId, "hookAnchor");
            Register(programs, target, schemas);
            Register(programs, caller, schemas);

            var documents = new List<KeyValuePair<string, GraphControlFlowDocument>>
            {
                new(TargetGraphId, target),
                new(CallerGraphId, caller),
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphHookWeaver.Weave(programs, documents, new StubResolver(), schemas));
            Assert.That(ex!.Message, Does.Contain("hotspot"), "the diagnostic must name the missing anchor");
        }

        [Test]
        public void Weave_UnknownNodeId_FailsClosed()
        {
            var schemas = BuildRegistry();
            var programs = new GraphProgramRegistry();
            GraphControlFlowDocument target = TargetDocument();
            GraphControlFlowDocument caller = HookCallerDocument(CallerGraphId, "hookNodeBefore");
            caller.Entries[0].HookNodeBefore!.NodeId = "ghost_node";
            Register(programs, target, schemas);
            Register(programs, caller, schemas);

            var documents = new List<KeyValuePair<string, GraphControlFlowDocument>>
            {
                new(TargetGraphId, target),
                new(CallerGraphId, caller),
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphHookWeaver.Weave(programs, documents, new StubResolver(), schemas));
            Assert.That(ex!.Message, Does.Contain("ghost_node"), "the diagnostic must name the missing node id");
        }

        [Test]
        public void Weave_HookCycle_FailsClosed()
        {
            var schemas = BuildRegistry();
            var programs = new GraphProgramRegistry();
            GraphControlFlowDocument target = TargetDocument();
            // Graph A hooks B's node; Graph B hooks A's node back — mutual inlining cycle.
            GraphControlFlowDocument a = HookCallerDocument(CallerGraphId, "hookNodeBefore");
            GraphControlFlowDocument b = HookCallerDocument(SecondCallerGraphId, "hookNodeBefore");
            a.Entries[0].HookNodeBefore!.GraphId = SecondCallerGraphId;
            a.Entries[0].HookNodeBefore!.NodeId = "hook_const";
            b.Entries[0].HookNodeBefore!.GraphId = CallerGraphId;
            b.Entries[0].HookNodeBefore!.NodeId = "hook_const";
            Register(programs, target, schemas);
            Register(programs, a, schemas);
            Register(programs, b, schemas);

            var documents = new List<KeyValuePair<string, GraphControlFlowDocument>>
            {
                new(TargetGraphId, target),
                new(CallerGraphId, a),
                new(SecondCallerGraphId, b),
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphHookWeaver.Weave(programs, documents, new StubResolver(), schemas));
            Assert.That(ex!.Message, Does.Contain(TriggerGraphHookWeaver.HookCycleError));
        }

        [Test]
        public void Weave_DuplicateAnchor_FailsClosed()
        {
            var schemas = BuildRegistry();
            GraphControlFlowDocument target = TargetDocument();
            target.Nodes.Add(new GraphControlFlowNode
            {
                Id = "other",
                Op = "ConstInt",
                IntValue = 1,
                Anchor = "hotspot",
            });
            target.ControlEdges.Add(new GraphControlFlowEdge("mark_before", "next", "other"));

            // The compile-side gate fires first: duplicate anchors are the cross-mod hook
            // contract, so the document never registers.
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(target, schemas);
            Assert.That(compiled.Diagnostics.Any(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Message.Contains("duplicate anchor", StringComparison.Ordinal) &&
                d.Message.Contains("hotspot", StringComparison.Ordinal)), Is.True,
                "duplicate anchor names must fail closed at compile");
        }

        [Test]
        public void Weave_BeforeEntryRoot_NoIncomingEdge_FailsClosed()
        {
            var schemas = BuildRegistry();
            var programs = new GraphProgramRegistry();
            GraphControlFlowDocument target = TargetDocument(withAnchor: false);
            target.Nodes.First(n => n.Id == "mark_before").Anchor = "root_anchor";
            GraphControlFlowDocument caller = HookCallerDocument(CallerGraphId, "hookAnchor");
            caller.Entries[0].HookAnchor!.Anchor = "root_anchor";
            Register(programs, target, schemas);
            Register(programs, caller, schemas);

            var documents = new List<KeyValuePair<string, GraphControlFlowDocument>>
            {
                new(TargetGraphId, target),
                new(CallerGraphId, caller),
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphHookWeaver.Weave(programs, documents, new StubResolver(), schemas));
            Assert.That(ex!.Message, Does.Contain("no incoming control edge"),
                "entry-root anchors cannot be re-rooted and must fail closed");
        }

        [Test]
        public void Weave_UnreachableAnchorNode_FailsClosedAtCompile()
        {
            var schemas = BuildRegistry();
            GraphControlFlowDocument target = TargetDocument(withAnchor: false);
            // Park an anchored node off the entry flow: DetectUnreachable rejects the
            // document at compile, so a hookNode targeting it can never load.
            target.Nodes.Add(new GraphControlFlowNode { Id = "island", Op = "ConstInt", IntValue = 1, Anchor = "island_anchor" });
            target.ControlEdges.Add(new GraphControlFlowEdge("island", "next", "done"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(target, schemas);
            Assert.That(compiled.Diagnostics.Any(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.UnreachableNode &&
                d.NodeId == "island"), Is.True,
                "an anchored node unreachable from any entry must fail closed before hooks are considered");
        }

        // ── Entry priority: compile + mount + dispatch ──

        [Test]
        public void Compile_EntryPriority_FeedsTriggerPriorityAscending()
        {
            var schemas = BuildRegistry();
            var doc = new GraphControlFlowDocument
            {
                Id = "Graph.Hook.PriorityCompile",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "late", Event = "MapLoaded", Start = "a", Priority = 100 },
                    new() { Label = "early", Event = "MapLoaded", Start = "a", Priority = -50 },
                    new() { Label = "mid", Event = "MapLoaded", Start = "a" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "a", Op = "ConstInt", IntValue = 1 },
                    new() { Id = "halt", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge> { new("a", "next", "halt") },
                ValueEdges = new List<GraphControlFlowValueEdge>(),
            };

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc, schemas);
            Assert.That(compiled.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error).ToList(), Is.Empty);
            TriggerGraphEntry[] entries = compiled.Package!.Value.TriggerGraphEntries;
            Assert.That(entries.Single(e => e.Label == "late").Priority, Is.EqualTo(100));
            Assert.That(entries.Single(e => e.Label == "early").Priority, Is.EqualTo(-50));
            Assert.That(entries.Single(e => e.Label == "mid").Priority, Is.EqualTo(0), "priority defaults to 0");
        }

        [Test]
        public void Mount_HookEntry_CreatesNoDispatchTrigger_PriorityEntriesCarryThrough()
        {
            var schemas = BuildRegistry();
            var programs = new GraphProgramRegistry();
            GraphControlFlowDocument caller = HookCallerDocument(CallerGraphId, "hookAnchor", priority: 25);
            caller.Entries[1].Priority = -10;
            Register(programs, caller, schemas);

            var config = new Ludots.Core.Config.MapConfig { Id = "hook_mount_probe_map" };
            config.TriggerGraphs = System.Text.Json.Nodes.JsonNode.Parse(
                """[ { "graph": "Graph.Hook.Caller" } ]""");
            var session = new MapSession(new MapId("hook_mount_probe_map"), config);
            List<Trigger> triggers = TriggerGraphMounting.BuildTriggers(session, programs, entityMounts: null);

            Assert.That(triggers.OfType<TriggerGraphMountTrigger>().Any(t => t.EntryLabel == "on_hook"), Is.False,
                "hook entries must not create dispatch triggers");
            TriggerGraphMountTrigger main = triggers.OfType<TriggerGraphMountTrigger>().Single(t => t.EntryLabel == "main");
            Assert.That(main.Priority, Is.EqualTo(-10), "dispatch entries carry their authored priority into Trigger.Priority");
        }

        // ── Helpers ──

        private static RunProbe WeaveAndRun(string mode, out TriggerManager manager)
        {
            var schemas = BuildRegistry();
            var programs = new GraphProgramRegistry();
            GraphControlFlowDocument target = TargetDocument();
            GraphControlFlowDocument caller = HookCallerDocument(CallerGraphId, mode);
            Register(programs, target, schemas);
            Register(programs, caller, schemas);

            var documents = new List<KeyValuePair<string, GraphControlFlowDocument>>
            {
                new(TargetGraphId, target),
                new(CallerGraphId, caller),
            };
            TriggerGraphHookWeaver.Weave(programs, documents, new StubResolver(), schemas);

            RunProbe probe = ExecuteWovenTarget(programs, schemas, out manager);
            return probe;
        }

        private static RunProbe ExecuteWovenTarget(GraphProgramRegistry programs, EventSchemaRegistry schemas)
        {
            return ExecuteWovenTarget(programs, schemas, out _);
        }

        private static RunProbe ExecuteWovenTarget(GraphProgramRegistry programs, EventSchemaRegistry schemas, out TriggerManager manager)
        {
            var probe = new RunProbe();
            manager = new TriggerManager { EventSchemas = schemas };
            MapId mapId = new("hook_probe_map");
            manager.RegisterMapTriggers(mapId, new[]
            {
                probe.Build("target:before", TargetBeforeEvent, null),
                probe.Build("hook", HookFiredEvent, probe.HookAmounts),
                probe.Build("target:after", TargetAfterEvent, probe.TargetAfterAmounts),
            });

            using var world = World.Create();
            Entity caster = world.Create();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);
            int graphId = GraphIdRegistry.GetId(TargetGraphId);
            ExecuteGraph(programs, graphId, world, caster, api, mapId);
            return probe;
        }

        private static void ExecuteGraph(
            GraphProgramRegistry registry,
            int graphId,
            World world,
            Entity caster,
            Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi api,
            MapId? mapScope)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;

            Assert.That(registry.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> root), Is.True);
            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                Programs = registry,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack,
                MapScope = mapScope
            };

            GasGraphOpHandlerTable.Execute(ref state, root, GasGraphOpHandlerTable.Instance);
        }
    }
}
