using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// #1108 placed-entity variable reads: compile-side instanceId shape validation,
    /// mount-time fail-closed membership against the mounting map's catalog, the
    /// Entity.Null (not throw) run-time miss contract with the World.IsAlive double
    /// insurance, and the InstanceExposure "declared" load-time stub.
    /// </summary>
    [TestFixture]
    public sealed class TriggerGraphPlacedVariableTests
    {
        private const string MapId = "map_placed_variable_probe";
        private const string GraphName = "Graph.TriggerGraph.PlacedProbe";

        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
        }

        private static GraphControlFlowDocument ProbeDocument(string? instanceId)
        {
            return new GraphControlFlowDocument
            {
                Id = GraphName,
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new()
                    {
                        Label = "probe",
                        Event = "EntityDied",
                        Start = "probe_read",
                    },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "probe_read", Op = "LoadPlacedEntity", InstanceId = instanceId },
                    new() { Id = "probe_halt", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("probe_read", "next", "probe_halt"),
                },
            };
        }

        [Test]
        public void Compile_PlacedInstanceId_MissingFailsClosed()
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(ProbeDocument(null));

            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("instanceId")), Is.True,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void Compile_PlacedInstanceId_AuthoredSymbolCompiles()
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(ProbeDocument("boss_camp"));

            Assert.That(result.Diagnostics.Where(d => d.Message.Contains("instanceId")).ToList(), Is.Empty,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
            Assert.That(result.Package, Is.Not.Null);
        }

        [Test]
        public void Mount_UnknownInstanceId_FailsClosed()
        {
            using var world = World.Create();
            MapSession session = CreateSession(world, registeredInstances: new[] { "probe_hero" });
            var programs = new GraphProgramRegistry();
            RegisterTriggerGraph(programs, instanceId: "ghost_boss");

            string? message = null;
            try
            {
                Ludots.Core.Gameplay.MapTriggers.TriggerGraphMounting.BuildTriggers(session, programs, entityMounts: null);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
            Assert.That(message, Does.Contain("ghost_boss"));
            Assert.That(message, Does.Contain("LoadPlacedEntity"));
        }

        [Test]
        public void Mount_CataloguedInstanceId_BuildsTriggers()
        {
            using var world = World.Create();
            MapSession session = CreateSession(world, registeredInstances: new[] { "probe_hero", "boss_camp" });
            var programs = new GraphProgramRegistry();
            RegisterTriggerGraph(programs, instanceId: "boss_camp");

            List<Trigger> triggers = Ludots.Core.Gameplay.MapTriggers.TriggerGraphMounting.BuildTriggers(
                session, programs, entityMounts: null);

            Assert.That(triggers.OfType<Ludots.Core.Gameplay.MapTriggers.TriggerGraphMountTrigger>().Count(), Is.EqualTo(1),
                "a catalogued instance must mount normally");
        }

        [Test]
        public void Execute_RegisteredLiveInstance_LoadsEntity()
        {
            using var world = World.Create();
            Entity boss = world.Create();
            var api = new GasGraphRuntimeApi(world);
            var index = new MapLoadEntityIndex();
            index.Register(MapId, "boss_camp", boss);
            api.BindPlacedInstanceIndexResolver(mapId => mapId.Value == MapId ? index : null);

            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.LoadPlacedEntity, Dst = 2, Imm = ConfigKeyRegistry.Register("boss_camp") },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            GraphExecutionState state = Execute(api, world, world.Create(), program, new MapId(MapId));

            Assert.That(state.E[2], Is.EqualTo(boss));
        }

        [Test]
        public void Compile_LoadPlacedAnchor_RequiresAnchorInInstanceId()
        {
            GraphControlFlowCompileResult bad = GraphControlFlowCompiler.Compile(new GraphControlFlowDocument
            {
                Id = GraphName,
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "probe", Event = "EntityDied", Start = "read" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "read", Op = "LoadPlacedAnchor", InstanceId = "boss_camp" },
                    new() { Id = "halt", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("read", "next", "halt"),
                },
            });
            Assert.That(bad.Diagnostics.Any(d => d.Message.Contains("anchor")), Is.True);

            GraphControlFlowCompileResult good = GraphControlFlowCompiler.Compile(new GraphControlFlowDocument
            {
                Id = GraphName,
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "probe", Event = "EntityDied", Start = "read" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "read", Op = "LoadPlacedAnchor", InstanceId = "camp_anchor" },
                    new() { Id = "halt", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("read", "next", "halt"),
                },
            });
            Assert.That(good.Diagnostics.Where(d => d.Message.Contains("anchor")).ToList(), Is.Empty);
            Assert.That(good.Package, Is.Not.Null);
        }

        [Test]
        public void Mount_UnknownRegionId_FailsClosed()
        {
            using var world = World.Create();
            MapSession session = CreateSession(world, registeredInstances: Array.Empty<string>(), regionIds: new[] { "yard" });
            var programs = new GraphProgramRegistry();
            RegisterTriggerGraphOp(programs, GraphNodeOp.LoadPlacedRegion, "ghost_yard");

            string? message = null;
            try
            {
                Ludots.Core.Gameplay.MapTriggers.TriggerGraphMounting.BuildTriggers(session, programs, entityMounts: null);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("ghost_yard"));
            Assert.That(message, Does.Contain("LoadPlacedRegion"));
        }

        [Test]
        public void Mount_CataloguedRegionId_BuildsTriggers()
        {
            using var world = World.Create();
            MapSession session = CreateSession(world, registeredInstances: Array.Empty<string>(), regionIds: new[] { "yard" });
            var programs = new GraphProgramRegistry();
            RegisterTriggerGraphOp(programs, GraphNodeOp.LoadPlacedRegion, "yard");

            List<Trigger> triggers = Ludots.Core.Gameplay.MapTriggers.TriggerGraphMounting.BuildTriggers(
                session, programs, entityMounts: null);

            Assert.That(triggers.OfType<Ludots.Core.Gameplay.MapTriggers.TriggerGraphMountTrigger>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void Mount_LoadPlacedAnchor_NonAnchorInstance_FailsClosed()
        {
            using var world = World.Create();
            MapSession session = CreateSession(world, registeredInstances: new[] { "boss_camp" });
            var programs = new GraphProgramRegistry();
            RegisterTriggerGraphOp(programs, GraphNodeOp.LoadPlacedAnchor, "boss_camp");

            string? message = null;
            try
            {
                Ludots.Core.Gameplay.MapTriggers.TriggerGraphMounting.BuildTriggers(session, programs, entityMounts: null);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("LoadPlacedAnchor"));
            Assert.That(message, Does.Contain("anchor"));
        }

        [Test]
        public void Execute_LoadPlacedRegion_WritesPresenceInt()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world);
            var catalog = new HashSet<string>(StringComparer.Ordinal) { "yard" };
            api.BindRegionCatalogResolver(mapId => mapId.Value == MapId ? catalog : null);

            GraphInstruction[] hit =
            {
                new() { Op = (ushort)GraphNodeOp.LoadPlacedRegion, Dst = 3, Imm = ConfigKeyRegistry.Register("yard") },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            GraphInstruction[] miss =
            {
                new() { Op = (ushort)GraphNodeOp.LoadPlacedRegion, Dst = 3, Imm = ConfigKeyRegistry.Register("missing") },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            Assert.That(Execute(api, world, world.Create(), hit, new MapId(MapId)).I[3], Is.EqualTo(1));
            Assert.That(Execute(api, world, world.Create(), miss, new MapId(MapId)).I[3], Is.EqualTo(0));
        }

        [Test]
        public void Execute_LoadPlacedAnchor_LoadsEntity()
        {
            using var world = World.Create();
            Entity anchor = world.Create();
            var api = new GasGraphRuntimeApi(world);
            var index = new MapLoadEntityIndex();
            index.Register(MapId, "camp_anchor", anchor);
            api.BindPlacedInstanceIndexResolver(mapId => mapId.Value == MapId ? index : null);

            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.LoadPlacedAnchor, Dst = 2, Imm = ConfigKeyRegistry.Register("camp_anchor") },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            Assert.That(Execute(api, world, world.Create(), program, new MapId(MapId)).E[2], Is.EqualTo(anchor));
        }

        [Test]
        public void Execute_UnregisteredInstance_WritesEntityNullWithoutThrow()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world);
            var index = new MapLoadEntityIndex();
            api.BindPlacedInstanceIndexResolver(mapId => mapId.Value == MapId ? index : null);

            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.LoadPlacedEntity, Dst = 2, Imm = ConfigKeyRegistry.Register("never_registered") },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            GraphExecutionState state = Execute(api, world, world.Create(), program, new MapId(MapId));

            Assert.That(state.E[2], Is.EqualTo(Entity.Null),
                "an unregistered instance is a readable miss, not a throw");
        }

        [Test]
        public void Execute_DestroyedInstance_WritesEntityNull()
        {
            using var world = World.Create();
            Entity boss = world.Create();
            var api = new GasGraphRuntimeApi(world);
            var index = new MapLoadEntityIndex();
            index.Register(MapId, "boss_camp", boss);
            api.BindPlacedInstanceIndexResolver(mapId => mapId.Value == MapId ? index : null);
            world.Destroy(boss);

            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.LoadPlacedEntity, Dst = 2, Imm = ConfigKeyRegistry.Register("boss_camp") },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            GraphExecutionState state = Execute(api, world, world.Create(), program, new MapId(MapId));

            Assert.That(state.E[2], Is.EqualTo(Entity.Null),
                "the index can hold a stale handle after destroy; World.IsAlive is the double insurance");
        }

        [Test]
        public void TryGetPlacedEntity_UnboundResolver_ThrowsPlacedIndexUnavailable()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world);

            Assert.That(
                () => api.TryGetPlacedEntity(ConfigKeyRegistry.Register("boss_camp"), new MapId(MapId), out _),
                Throws.InvalidOperationException.With.Message.Contains("GAS.GRAPH.ERR.PlacedIndexUnavailable"));
        }

        [Test]
        public void LoadEntitiesAndIndex_DeclaredExposure_FailsClosedAwaitingHitl()
        {
            using var world = World.Create();
            MapLoader loader = CreateBareLoader(world);
            var map = new MapConfig { Id = MapId, InstanceExposure = "declared" };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.LoadEntitiesAndIndex(map))!;

            Assert.That(ex.Message, Does.Contain(MapId));
            Assert.That(ex.Message, Does.Contain("declared"));
            Assert.That(ex.Message, Does.Contain("HITL"));
        }

        [Test]
        public void LoadEntitiesAndIndex_UnknownExposure_FailsClosed()
        {
            using var world = World.Create();
            MapLoader loader = CreateBareLoader(world);
            var map = new MapConfig { Id = MapId, InstanceExposure = "everyone" };

            Assert.That(
                () => loader.LoadEntitiesAndIndex(map),
                Throws.InvalidOperationException.With.Message.Contains("\"all\" or \"declared\""));
        }

        private static MapSession CreateSession(World world, string[] registeredInstances, string[]? regionIds = null)
        {
            var config = new MapConfig { Id = MapId };
            config.TriggerGraphs = JsonNode.Parse($$"""[ { "graph": "{{GraphName}}" } ]""");
            if (regionIds != null && regionIds.Length > 0)
            {
                var regions = new JsonArray();
                for (int i = 0; i < regionIds.Length; i++)
                {
                    regions.Add(JsonNode.Parse($$"""{ "id": "{{regionIds[i]}}", "shape": "circle", "x": 0, "y": 0, "radiusCm": 100 }"""));
                }

                config.Regions = regions;
            }

            var session = new MapSession(new MapId(MapId), config);
            var index = new MapLoadEntityIndex();
            for (int i = 0; i < registeredInstances.Length; i++)
            {
                index.Register(MapId, registeredInstances[i], world.Create());
            }

            session.EntityIndex = index;
            return session;
        }

        private static void RegisterTriggerGraph(GraphProgramRegistry programs, string instanceId)
        {
            RegisterTriggerGraphOp(programs, GraphNodeOp.LoadPlacedEntity, instanceId);
        }

        private static void RegisterTriggerGraphOp(GraphProgramRegistry programs, GraphNodeOp op, string instanceId)
        {
            GraphInstruction[] program =
            {
                new() { Op = (ushort)op, Dst = 2, Imm = ConfigKeyRegistry.Register(instanceId) },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            int id = GraphIdRegistry.Register(GraphName);
            programs.Register(
                id,
                program,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                null,
                new[] { new TriggerGraphEntry("probe", GameEvents.EntityDied.Value, 0, once: false) });
        }

        private static GraphExecutionState Execute(
            GasGraphRuntimeApi api,
            World world,
            Entity caster,
            GraphInstruction[] program,
            MapId mapScope)
        {
            var state = new GraphExecutionState
            {
                World = world,
                Api = api,
                Caster = caster,
                ExplicitTarget = caster,
                F = new float[GraphVmLimits.MaxFloatRegisters],
                I = new int[GraphVmLimits.MaxIntRegisters],
                E = new Entity[GraphVmLimits.MaxEntityRegisters],
                B = new byte[GraphVmLimits.MaxBoolRegisters],
                Targets = new Entity[GraphVmLimits.MaxTargets],
                TargetList = new GraphTargetList(new Entity[GraphVmLimits.MaxTargets]),
                CallStack = new int[GraphVmLimits.MaxCallStackDepth],
                CallStackCount = 0,
                MapScope = mapScope,
            };
            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return state;
        }

        /// <summary>The exposure check precedes every registry read, so the fixture stays bare (no templates).</summary>
        private static MapLoader CreateBareLoader(World world)
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_TriggerGraphPlacedVariableTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
                return new MapLoader(world, new WorldMap(), pipeline);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
