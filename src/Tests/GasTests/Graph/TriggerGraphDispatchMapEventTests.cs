using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// #1115 DispatchMapEvent: compile-time schema gating (event resolves, scope agrees,
    /// payload ports align with EventParamType, String ports rejected), runtime assembly
    /// of the ScriptContext from the StoreArg* staging, map-scoped delivery through
    /// TriggerManager, and fail-closed fires (missing required param, staged type
    /// mismatch, unknown event at run time).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TriggerGraphDispatchMapEventTests
    {
        private const string EventName = "Probe.Dispatch.Fired";
        private const string AmountKey = "Probe.Dispatch.Amount";
        private const string RatioKey = "Probe.Dispatch.Ratio";

        private sealed class RecordingTrigger : Trigger
        {
            public ScriptContext? Seen { get; private set; }

            public override Task ExecuteAsync(ScriptContext context)
            {
                Seen = context;
                return Task.CompletedTask;
            }
        }

        private static EventSchemaRegistry BuildRegistry()
        {
            var registry = new EventSchemaRegistry();
            registry.RegisterCustom(new EventSchema(
                EventName,
                EventScope.Map,
                new EventParamSchema[]
                {
                    new("amount", EventParamType.Int, AmountKey),
                    new("ratio", EventParamType.Float, RatioKey, Optional: true),
                    new("note", EventParamType.String, "Probe.Dispatch.Note", Optional: true),
                }));
            return registry;
        }

        // ── Compile: schema gating ──

        [Test]
        public void Compile_ValidEvent_WiresPayloadPortsToStoreArgOps()
        {
            var doc = DispatchDocument(nodes => nodes, edges => edges);
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, BuildRegistry());
            Assert.That(result.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error).ToList(), Is.Empty,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));

            GraphInstruction[] program = result.Package!.Value.Program;
            GraphInstruction store = program.Single(i => i.Op == (ushort)GraphNodeOp.StoreArgInt);
            Assert.That(result.Package.Value.Symbols[store.Imm], Is.EqualTo(AmountKey),
                "the wired amount port must compile to a StoreArgInt keyed by the schema payload key");
            GraphInstruction dispatch = program.Single(i => i.Op == (ushort)GraphNodeOp.DispatchMapEvent);
            Assert.That(dispatch.Flags & 1, Is.EqualTo(0), "authored scope defaults to the map domain");
            Assert.That(result.Package.Value.Symbols[dispatch.Imm], Is.EqualTo(EventName),
                "the fire instruction interns the event name symbol");
        }

        [Test]
        public void Compile_PortTypeMismatch_FailsClosed()
        {
            var doc = DispatchDocument(nodes => nodes, edges => edges);
            // amount_const is a ConstFloat node; the schema declares 'amount' as Int.
            doc.Nodes.First(n => n.Id == "amount_const").Op = "ConstFloat";

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, BuildRegistry());
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error), Is.True,
                "a Float edge into an Int schema parameter must fail compile");
        }

        [Test]
        public void Compile_StringParamPort_IsRejected()
        {
            var doc = DispatchDocument(nodes => nodes, edges => edges);
            doc.Nodes.Add(new GraphControlFlowNode { Id = "note_const", Op = "ConstInt", IntValue = 3 });
            doc.ValueEdges.Add(new GraphControlFlowValueEdge("note_const", "value", "fire", "note"));

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, BuildRegistry());
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("String")), Is.True,
                "String parameters have no register port");
        }

        [Test]
        public void Compile_UnknownParamPort_IsRejected()
        {
            var doc = DispatchDocument(nodes => nodes, edges => edges);
            doc.Nodes.Add(new GraphControlFlowNode { Id = "bogus_const", Op = "ConstInt", IntValue = 3 });
            doc.ValueEdges.Add(new GraphControlFlowValueEdge("bogus_const", "value", "fire", "bogus"));

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, BuildRegistry());
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("bogus")), Is.True,
                "ports the event schema does not declare must fail compile");
        }

        [Test]
        public void Compile_UnregisteredEvent_IsRejected()
        {
            var doc = DispatchDocument(nodes => nodes, edges => edges);
            doc.Nodes.First(n => n.Id == "fire").Event = "Probe.Dispatch.NeverDeclared";

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, BuildRegistry());
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("no registered schema")), Is.True);
        }

        [Test]
        public void Compile_WithoutSchemaRegistry_FailsClosed()
        {
            var doc = DispatchDocument(nodes => nodes, edges => edges);
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("EventSchemaRegistry")), Is.True,
                "hosts that compile DispatchMapEvent without the schema SSOT must fail closed");
        }

        [TestCase("global")]
        [TestCase("galaxy")]
        [TestCase("self")]
        public void Compile_ScopeConflicts_AreRejected(string scope)
        {
            var doc = DispatchDocument(nodes => nodes, edges => edges);
            doc.Nodes.First(n => n.Id == "fire").Scope = scope;

            var registry = BuildRegistry();
            registry.RegisterCustom(new EventSchema(
                "Probe.Dispatch.GlobalEvent",
                EventScope.Global,
                new EventParamSchema[] { new("amount", EventParamType.Int, AmountKey) }));

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, registry);
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("scope")), Is.True,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void Compile_SelfScope_OnMapEvent_IsRejected()
        {
            var doc = DispatchDocument(nodes => nodes, edges => edges);
            doc.Nodes.First(n => n.Id == "fire").Scope = "self";

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, BuildRegistry());
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("scope")), Is.True,
                "self dispatch requires an Entity-scope schema; the probe event declares Map scope");
        }

        // ── Runtime: fire through TriggerManager ──

        [Test]
        public void DispatchMapEvent_StagedPayload_ReachesMapScopedListener()
        {
            var mapId = new MapId("dispatch_probe_map");
            using var world = World.Create();
            Entity caster = world.Create();

            var manager = new TriggerManager { EventSchemas = BuildRegistry() };
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);
            var listener = new RecordingTrigger { EventKey = new EventKey(EventName) };
            manager.RegisterMapTriggers(mapId, new Trigger[] { listener });

            int eventKeyId = ConfigKeyRegistry.Register(EventName);
            int amountKeyId = ConfigKeyRegistry.Register(AmountKey);
            int graphId = GraphIdRegistry.Register("Graph.Probe.Dispatch.Runtime");
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = 42 },
                new() { Op = (ushort)GraphNodeOp.StoreArgInt, A = 1, Imm = amountKeyId },
                new() { Op = (ushort)GraphNodeOp.DispatchMapEvent, Imm = eventKeyId },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(
                graphId,
                program,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                new[] { EventName, AmountKey },
                new[] { new TriggerGraphEntry("main", "MapLoaded", 0, once: false) });

            ExecuteGraph(programs, graphId, world, caster, api, mapId);

            Assert.That(listener.Seen, Is.Not.Null, "the map-scoped listener must run");
            Assert.That(listener.Seen!.Get<int>(AmountKey), Is.EqualTo(42), "the staged argument must arrive as the schema payload key");
            Assert.That(listener.Seen.Get<MapId>(ContextKeys.MapId).Value, Is.EqualTo(mapId.Value));
        }

        [Test]
        public void DispatchMapEvent_MissingRequiredParam_ThrowsAtRuntime()
        {
            var mapId = new MapId("dispatch_probe_missing");
            using var world = World.Create();
            Entity caster = world.Create();

            var manager = new TriggerManager { EventSchemas = BuildRegistry() };
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            int eventKeyId = ConfigKeyRegistry.Register(EventName);
            int graphId = GraphIdRegistry.Register("Graph.Probe.Dispatch.Runtime.Missing");
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.DispatchMapEvent, Imm = eventKeyId },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(
                graphId,
                program,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                new[] { EventName },
                new[] { new TriggerGraphEntry("main", "MapLoaded", 0, once: false) });

            var ex = Assert.Throws<InvalidOperationException>(() => ExecuteGraph(programs, graphId, world, caster, api, mapId));
            Assert.That(ex!.Message, Does.Contain("EVENT.SCHEMA.MissingParam"),
                "fire-time ValidateFirePayload is the backstop for unstaged required parameters");
        }

        [Test]
        public void DispatchMapEvent_StagedTypeMismatch_ThrowsAtRuntime()
        {
            var mapId = new MapId("dispatch_probe_mismatch");
            using var world = World.Create();
            Entity caster = world.Create();

            var manager = new TriggerManager { EventSchemas = BuildRegistry() };
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            int eventKeyId = ConfigKeyRegistry.Register(EventName);
            int amountKeyId = ConfigKeyRegistry.Register(AmountKey);
            int graphId = GraphIdRegistry.Register("Graph.Probe.Dispatch.Runtime.Mismatch");
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 1.5f },
                new() { Op = (ushort)GraphNodeOp.StoreArgFloat, A = 1, Imm = amountKeyId },
                new() { Op = (ushort)GraphNodeOp.DispatchMapEvent, Imm = eventKeyId },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(
                graphId,
                program,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                new[] { EventName, AmountKey },
                new[] { new TriggerGraphEntry("main", "MapLoaded", 0, once: false) });

            var ex = Assert.Throws<InvalidOperationException>(() => ExecuteGraph(programs, graphId, world, caster, api, mapId));
            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.EntryPayloadTypeMismatch"),
                "staging a float where the schema declares int must throw, not silently coerce");
        }

        [Test]
        public void DispatchMapEvent_UnknownEventAtRuntime_Throws()
        {
            var mapId = new MapId("dispatch_probe_unknown");
            using var world = World.Create();
            Entity caster = world.Create();

            var manager = new TriggerManager { EventSchemas = BuildRegistry() };
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            int eventKeyId = ConfigKeyRegistry.Register("Probe.Dispatch.Undeclared");
            int graphId = GraphIdRegistry.Register("Graph.Probe.Dispatch.Runtime.Unknown");
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.DispatchMapEvent, Imm = eventKeyId },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(
                graphId,
                program,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                new[] { "Probe.Dispatch.Undeclared" },
                new[] { new TriggerGraphEntry("main", "MapLoaded", 0, once: false) });

            var ex = Assert.Throws<InvalidOperationException>(() => ExecuteGraph(programs, graphId, world, caster, api, mapId));
            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.EventSchemaUnknown"));
        }

        [Test]
        public void DispatchMapEvent_NoMapScope_Throws()
        {
            using var world = World.Create();
            Entity caster = world.Create();

            var manager = new TriggerManager { EventSchemas = BuildRegistry() };
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            int eventKeyId = ConfigKeyRegistry.Register(EventName);
            int graphId = GraphIdRegistry.Register("Graph.Probe.Dispatch.Runtime.NoMap");
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.DispatchMapEvent, Imm = eventKeyId },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(
                graphId,
                program,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                new[] { EventName },
                new[] { new TriggerGraphEntry("main", "MapLoaded", 0, once: false) });

            var ex = Assert.Throws<InvalidOperationException>(() => ExecuteGraph(programs, graphId, world, caster, api, mapScope: null));
            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.DispatchMapEventNoMapScope"));
        }

        // ── Helpers ──

        private static GraphControlFlowDocument DispatchDocument(
            Func<List<GraphControlFlowNode>, List<GraphControlFlowNode>> nodes,
            Func<List<GraphControlFlowValueEdge>, List<GraphControlFlowValueEdge>> edges)
        {
            return new GraphControlFlowDocument
            {
                Id = "Graph.Probe.Dispatch.Compile",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "on_map_loaded", Event = "MapLoaded", Start = "amount_const" },
                },
                Nodes = nodes(new List<GraphControlFlowNode>
                {
                    new() { Id = "amount_const", Op = "ConstInt", IntValue = 42 },
                    new() { Id = "fire", Op = "DispatchMapEvent", Event = EventName },
                    new() { Id = "done", Op = "HaltReturnInt" },
                }),
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("amount_const", "next", "fire"),
                    new("fire", "next", "done"),
                },
                ValueEdges = edges(new List<GraphControlFlowValueEdge>
                {
                    new("amount_const", "value", "fire", "amount"),
                }),
            };
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
