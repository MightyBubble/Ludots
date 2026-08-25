using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;
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
    /// #1123 cross-map / global event dispatch: global fire reaches only global
    /// subscribers (map tables untouched), priority order holds across suspend /
    /// resume, cross-map fire touches only the target map and carries
    /// MapTrigger.SourceMapId, scope mismatches fail closed at compile and mount
    /// time, and parameterless custom events keep their authored scope.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TriggerGraphGlobalEventTests
    {
        private const string GlobalEventName = "Probe.Global.Fired";
        private const string MapEventName = "Probe.Global.MapEvent";
        private const string AmountKey = "Probe.Global.Amount";

        private sealed class RecordingTrigger : Trigger
        {
            public ScriptContext? Seen { get; private set; }

            public RecordingTrigger(EventKey eventKey, int priority = 0)
            {
                EventKey = eventKey;
                Priority = priority;
            }

            public override Task ExecuteAsync(ScriptContext context)
            {
                Seen = context;
                return Task.CompletedTask;
            }
        }

        private sealed class OrderProbeTrigger : Trigger
        {
            public int Order = -1;

            public OrderProbeTrigger(EventKey eventKey, int priority)
            {
                EventKey = eventKey;
                Priority = priority;
            }

            public override Task ExecuteAsync(ScriptContext context)
            {
                if (Order < 0)
                {
                    Order = NextOrder++;
                }

                return Task.CompletedTask;
            }

            public static int NextOrder { get; set; }
        }

        private static EventSchemaRegistry BuildRegistry()
        {
            var registry = new EventSchemaRegistry();
            registry.RegisterCustom(new EventSchema(
                GlobalEventName,
                EventScope.Global,
                new EventParamSchema[]
                {
                    new("amount", EventParamType.Int, AmountKey, Optional: true),
                }));
            registry.RegisterCustom(new EventSchema(
                MapEventName,
                EventScope.Map,
                Array.Empty<EventParamSchema>()));
            return registry;
        }

        // ── TriggerManager: global table semantics ──

        [Test]
        public void GlobalFire_ReachesOnlyGlobalSubscribers()
        {
            EventKey eventKey = new(GlobalEventName);
            var manager = new TriggerManager { EventSchemas = BuildRegistry() };
            MapId ownerA = new("global_probe_owner_a");
            MapId ownerB = new("global_probe_owner_b");

            var globalA = new RecordingTrigger(eventKey);
            var globalB = new RecordingTrigger(eventKey);
            manager.RegisterGlobalTriggers(ownerA, new Trigger[] { globalA });
            manager.RegisterGlobalTriggers(ownerB, new Trigger[] { globalB });

            var mapListener = new RecordingTrigger(eventKey);
            manager.RegisterMapTriggers(ownerA, new Trigger[] { mapListener });

            var context = new ScriptContext();
            manager.FireGlobalEvent(eventKey, context);

            Assert.That(globalA.Seen, Is.SameAs(context), "global subscriber of map A must receive the fire");
            Assert.That(globalB.Seen, Is.SameAs(context), "global subscriber of map B must receive the fire");
            Assert.That(mapListener.Seen, Is.Null, "map-table triggers must not be touched by a global fire");

            manager.UnregisterGlobalTriggers(ownerA);
            manager.FireGlobalEvent(eventKey, context);
            Assert.That(globalB.Seen, Is.SameAs(context));
        }

        [Test]
        public void GlobalFire_DispatchesInPriorityOrder_AcrossMaps()
        {
            EventKey eventKey = new(GlobalEventName);
            var manager = new TriggerManager();
            OrderProbeTrigger.NextOrder = 0;

            var late = new OrderProbeTrigger(eventKey, 200);
            var early = new OrderProbeTrigger(eventKey, -50);
            var mid = new OrderProbeTrigger(eventKey, 0);
            // Register out of order, across three owning maps.
            manager.RegisterGlobalTriggers(new MapId("priority_owner_late"), new Trigger[] { late });
            manager.RegisterGlobalTriggers(new MapId("priority_owner_early"), new Trigger[] { early });
            manager.RegisterGlobalTriggers(new MapId("priority_owner_mid"), new Trigger[] { mid });

            manager.FireGlobalEvent(eventKey, new ScriptContext());

            Assert.That(early.Order, Is.EqualTo(0), "negative priority runs first (earlier)");
            Assert.That(mid.Order, Is.EqualTo(1));
            Assert.That(late.Order, Is.EqualTo(2));
        }

        [Test]
        public void SuspendedMap_DoesNotReceiveGlobals_ResumeRestoresPriorityOrder()
        {
            EventKey eventKey = new(GlobalEventName);
            var manager = new TriggerManager();
            MapId owner = new("suspend_probe_owner");

            OrderProbeTrigger.NextOrder = 0;
            var early = new OrderProbeTrigger(eventKey, 0);
            var late = new OrderProbeTrigger(eventKey, 100);
            manager.RegisterGlobalTriggers(owner, new Trigger[] { late, early });

            manager.SetGlobalTriggersSuspended(owner, suspended: true);
            manager.FireGlobalEvent(eventKey, new ScriptContext());
            Assert.That(early.Order, Is.EqualTo(-1), "a suspended map's global subscriptions must not dispatch");
            Assert.That(late.Order, Is.EqualTo(-1));

            manager.SetGlobalTriggersSuspended(owner, suspended: false);
            manager.FireGlobalEvent(eventKey, new ScriptContext());
            Assert.That(early.Order, Is.EqualTo(0), "resume must reattach in priority order, not registration order");
            Assert.That(late.Order, Is.EqualTo(1));

            // Registration while suspended parks in the reverse index only.
            MapId loading = new("suspend_probe_loading");
            var parked = new OrderProbeTrigger(eventKey, 0) { Order = -1 };
            manager.SetGlobalTriggersSuspended(loading, suspended: true);
            manager.RegisterGlobalTriggers(loading, new Trigger[] { parked });
            manager.FireGlobalEvent(eventKey, new ScriptContext());
            Assert.That(parked.Order, Is.EqualTo(-1), "a map registered mid-load only goes live on resume");
            manager.SetGlobalTriggersSuspended(loading, suspended: false);
            manager.FireGlobalEvent(eventKey, new ScriptContext());
            Assert.That(parked.Order, Is.Not.EqualTo(-1));
        }

        // ── TriggerManager: cross-map point-to-point ──

        [Test]
        public void CrossMapFire_TargetsOnlyTargetMap_AndCarriesSourceMapId()
        {
            EventKey eventKey = new(MapEventName);
            var sessions = new MapSessionManager();
            MapId source = new("cross_probe_source");
            MapId target = new("cross_probe_target");
            MapId bystander = new("cross_probe_bystander");
            sessions.CreateSession(target, new Ludots.Core.Config.MapConfig { Id = target.Value });

            var manager = new TriggerManager { EventSchemas = BuildRegistry(), MapSessions = sessions };
            var targetListener = new RecordingTrigger(eventKey);
            var bystanderListener = new RecordingTrigger(eventKey);
            var globalListener = new RecordingTrigger(eventKey);
            manager.RegisterMapTriggers(target, new Trigger[] { targetListener });
            manager.RegisterMapTriggers(bystander, new Trigger[] { bystanderListener });
            manager.RegisterGlobalTriggers(source, new Trigger[] { globalListener });

            var context = new ScriptContext();
            manager.FireCrossMapEvent(source, target, eventKey, context);

            Assert.That(targetListener.Seen, Is.SameAs(context), "the target map's table is the only map table touched");
            Assert.That(bystanderListener.Seen, Is.Null, "other maps must not see a point-to-point fire");
            Assert.That(globalListener.Seen, Is.Null, "global subscriptions must not see a point-to-point fire");
            Assert.That(targetListener.Seen!.Get<MapId>(MapTriggerEventPayloadKeys.SourceMapId), Is.EqualTo(source),
                "MapTrigger.SourceMapId must ride the context for the receiving entry");
        }

        [Test]
        public void CrossMapFire_ZeroSubscribersOnLiveTarget_IsNoOp()
        {
            var sessions = new MapSessionManager();
            MapId target = new("cross_probe_empty_target");
            sessions.CreateSession(target, new Ludots.Core.Config.MapConfig { Id = target.Value });

            var manager = new TriggerManager { EventSchemas = BuildRegistry(), MapSessions = sessions };
            Assert.DoesNotThrow(() =>
                manager.FireCrossMapEvent(new MapId("cross_probe_src"), target, new EventKey(MapEventName), new ScriptContext()));
        }

        [Test]
        public void CrossMapFire_MissingTargetSession_FailsClosedNamingMap()
        {
            var manager = new TriggerManager { EventSchemas = BuildRegistry(), MapSessions = new MapSessionManager() };
            MapId ghost = new("cross_probe_ghost_map");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                manager.FireCrossMapEvent(new MapId("cross_probe_src"), ghost, new EventKey(MapEventName), new ScriptContext()));
            Assert.That(ex!.Message, Does.Contain(ghost.Value), "the diagnostic must name the missing map id");
            Assert.That(ex.Message, Does.Contain("no loaded map session"));
        }

        [Test]
        public void CrossMapFire_GlobalScopeEvent_FailsClosed()
        {
            var sessions = new MapSessionManager();
            MapId target = new("cross_probe_global_target");
            sessions.CreateSession(target, new Ludots.Core.Config.MapConfig { Id = target.Value });

            var manager = new TriggerManager { EventSchemas = BuildRegistry(), MapSessions = sessions };
            var ex = Assert.Throws<InvalidOperationException>(() =>
                manager.FireCrossMapEvent(new MapId("cross_probe_src"), target, new EventKey(GlobalEventName), new ScriptContext()));
            Assert.That(ex!.Message, Does.Contain("Global scope"));
        }

        [Test]
        public void UnregisterMapTriggers_RemovesOwnedGlobalSubscriptions()
        {
            EventKey eventKey = new(GlobalEventName);
            var manager = new TriggerManager();
            MapId owner = new("unload_probe_owner");

            var listener = new RecordingTrigger(eventKey);
            manager.RegisterGlobalTriggers(owner, new Trigger[] { listener });
            manager.UnregisterMapTriggers(owner, new ScriptContext());

            manager.FireGlobalEvent(eventKey, new ScriptContext());
            Assert.That(listener.Seen, Is.Null, "map unload must detach its global subscriptions wholesale");
        }

        // ── Schema parser: parameterless entries keep their scope ──

        [Test]
        public void Parse_ParameterlessEntry_KeepsAuthoredScope()
        {
            System.Text.Json.Nodes.JsonObject node =
                (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(
                    $$"""{ "id": "{{GlobalEventName}}", "description": "probe", "scope": "global" }""")!;

            EventSchema? schema = CustomEventSchemaParser.TryParse(node, GlobalEventName, "test entry");
            Assert.That(schema, Is.Not.Null, "a parameterless entry must still produce a schema");
            Assert.That(schema!.Scope, Is.EqualTo(EventScope.Global), "the authored scope must survive without params");
            Assert.That(schema.Params, Is.Empty);
        }

        // ── Compile: scope self-consistency with global dispatch ──

        [Test]
        public void Compile_GlobalScopeDispatch_OnGlobalEvent_WiresGlobalFlag()
        {
            var doc = GlobalDispatchDocument(scope: "global");
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, BuildRegistry());
            Assert.That(result.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error).ToList(), Is.Empty,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));

            GraphInstruction dispatch = result.Package!.Value.Program.Single(i => i.Op == (ushort)GraphNodeOp.DispatchMapEvent);
            Assert.That(dispatch.Flags & 2, Is.EqualTo(2), "scope=global compiles to the global dispatch flag");
        }

        [TestCase("map")]
        [TestCase("self")]
        public void Compile_GlobalScopeEvent_WithNonGlobalDispatch_Rejected(string scope)
        {
            var doc = GlobalDispatchDocument(scope);
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, BuildRegistry());
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("declared scope")), Is.True,
                $"global-scope events cannot dispatch with scope '{scope}'");
        }

        [Test]
        public void Compile_GlobalDispatchScope_OnMapScopeEvent_Rejected()
        {
            var doc = GlobalDispatchDocument(scope: "global");
            doc.Nodes.First(n => n.Id == "fire").Event = MapEventName;
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc, BuildRegistry());
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("declared scope")), Is.True,
                "scope=global on a Map-scope event must fail compile");
        }

        // ── Runtime: DispatchMapEvent Flags=2 walks FireGlobalEvent ──

        [Test]
        public void DispatchMapEvent_GlobalScopeRuntime_ReachesGlobalListenerOnly()
        {
            MapId mapId = new("global_runtime_probe_map");
            using var world = World.Create();
            Entity caster = world.Create();

            var manager = new TriggerManager { EventSchemas = BuildRegistry() };
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            var globalListener = new RecordingTrigger(new EventKey(GlobalEventName));
            manager.RegisterGlobalTriggers(mapId, new Trigger[] { globalListener });
            var mapListener = new RecordingTrigger(new EventKey(GlobalEventName));
            manager.RegisterMapTriggers(mapId, new Trigger[] { mapListener });

            int eventKeyId = ConfigKeyRegistry.Register(GlobalEventName);
            int amountKeyId = ConfigKeyRegistry.Register(AmountKey);
            int graphId = GraphIdRegistry.Register("Graph.Probe.Global.Runtime");
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = 7 },
                new() { Op = (ushort)GraphNodeOp.StoreArgInt, A = 1, Imm = amountKeyId },
                new() { Op = (ushort)GraphNodeOp.DispatchMapEvent, Imm = eventKeyId, Flags = 2 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(
                graphId,
                program,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                new[] { GlobalEventName, AmountKey },
                new[] { new TriggerGraphEntry("main", "MapLoaded", 0, once: false) });

            ExecuteGraph(programs, graphId, world, caster, api, mapId);

            Assert.That(globalListener.Seen, Is.Not.Null, "the global subscription must run");
            Assert.That(globalListener.Seen!.Get<int>(AmountKey), Is.EqualTo(7), "staged schema params must arrive");
            Assert.That(globalListener.Seen.Get<MapId>(MapTriggerEventPayloadKeys.SourceMapId), Is.EqualTo(mapId),
                "the origin map rides MapTrigger.SourceMapId as transport metadata");
            Assert.That(mapListener.Seen, Is.Null, "map tables must not see a global dispatch");
        }

        // ── Mount routing: schema scope decides the subscription table ──

        [Test]
        public void Mount_EntryOnGlobalScopeEvent_RoutesSubscriptionToGlobalTable()
        {
            using var world = World.Create();
            var config = new Ludots.Core.Config.MapConfig { Id = "global_mount_probe_map" };
            config.TriggerGraphs = System.Text.Json.Nodes.JsonNode.Parse(
                """[ { "graph": "Graph.Probe.Global.Mount" } ]""");
            var session = new MapSession(new MapId("global_mount_probe_map"), config);

            var programs = new GraphProgramRegistry();
            int graphId = GraphIdRegistry.Register("Graph.Probe.Global.Mount");
            programs.Register(
                graphId,
                new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                },
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                symbols: null,
                new[]
                {
                    new TriggerGraphEntry("on_map_event", MapEventName, 0, once: false),
                    new TriggerGraphEntry("on_global_event", GlobalEventName, 0, once: false),
                });

            List<Trigger> triggers = TriggerGraphMounting.BuildTriggers(
                session, programs, entityMounts: null, customEvents: null, eventSchemas: BuildRegistry());

            TriggerGraphMountTrigger mapEntry = triggers.OfType<TriggerGraphMountTrigger>()
                .Single(t => t.EntryLabel == "on_map_event");
            TriggerGraphMountTrigger globalEntry = triggers.OfType<TriggerGraphMountTrigger>()
                .Single(t => t.EntryLabel == "on_global_event");

            Assert.That(mapEntry.SubscriptionScope, Is.EqualTo(EventScope.Map));
            Assert.That(globalEntry.SubscriptionScope, Is.EqualTo(EventScope.Global),
                "the schema's Global scope must route the entry to the global subscription table");
        }

        [Test]
        public void Mount_EntityDomainGlobalSubscription_FailsClosed()
        {
            var programs = new GraphProgramRegistry();
            int graphId = GraphIdRegistry.Register("Graph.Probe.Global.EntityMount");
            programs.Register(
                graphId,
                new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                },
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                symbols: null,
                new[]
                {
                    new TriggerGraphEntry("on_global_event", GlobalEventName, 0, once: false),
                });

            using var world = World.Create();
            Entity scope = world.Create();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphMounting.BuildEntityMountTriggers(
                    programs, scope, "Graph.Probe.Global.EntityMount", "test entity mount",
                    entityIndex: null, eventSchemas: BuildRegistry()));
            Assert.That(ex!.Message, Does.Contain("entity-domain global subscriptions"));
        }

        // ── Helpers ──

        private static GraphControlFlowDocument GlobalDispatchDocument(string scope)
        {
            return new GraphControlFlowDocument
            {
                Id = "Graph.Probe.Global.Compile",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "on_map_loaded", Event = "MapLoaded", Start = "fire" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "fire", Op = "DispatchMapEvent", Event = GlobalEventName, Scope = scope },
                    new() { Id = "done", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("fire", "next", "done"),
                },
                ValueEdges = new List<GraphControlFlowValueEdge>(),
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
