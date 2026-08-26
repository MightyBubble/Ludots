using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// FsmState 糖（FSM-1a）：编译期展开为 ReadMapVarInt + SwitchInt 式臂链，零新 opcode。
    /// 消融对照 = 手写 ReadMapVarInt→SwitchInt 链逐指令元组全等。
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphFsmSugarTests
    {
        private const string StateVarName = "fsm.phase";

        [Test]
        public void FsmState_CompilesIdenticalToHandwrittenReadSwitch()
        {
            GraphControlFlowDocument sugarGraph = CreateFsmGraph(bindEnum: true);
            GraphControlFlowDocument handwrittenGraph = CreateHandwrittenReadSwitchGraph();
            Ludots.Core.Scripting.EnumCatalog enums = CombatStateCatalog();

            GraphControlFlowCompileResult sugarCompiled = GraphControlFlowCompiler.Compile(sugarGraph, null, enums);
            GraphControlFlowCompileResult handwrittenCompiled = GraphControlFlowCompiler.Compile(handwrittenGraph, null, enums);

            Assert.That(sugarCompiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(sugarCompiled.Diagnostics));
            Assert.That(handwrittenCompiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(handwrittenCompiled.Diagnostics));
            Assert.That(sugarCompiled.Program.Length, Is.EqualTo(handwrittenCompiled.Program.Length));
            for (int i = 0; i < sugarCompiled.Program.Length; i++)
            {
                GraphInstruction a = sugarCompiled.Program[i];
                GraphInstruction b = handwrittenCompiled.Program[i];
                Assert.That(
                    (a.Op, a.Dst, a.A, a.B, a.C, a.Flags, a.Imm, a.ImmF),
                    Is.EqualTo((b.Op, b.Dst, b.A, b.B, b.C, b.Flags, b.Imm, b.ImmF)),
                    $"instruction {i} must match between FsmState and handwritten ReadMapVarInt+SwitchInt lowering");
            }

            Assert.That(sugarCompiled.Package.Value.Symbols, Is.EqualTo(handwrittenCompiled.Package.Value.Symbols),
                "stateVar and var must intern to the same symbol table");

            // 形状断言：展开只由既有 op 组成，无 None 槽位残留。
            var ops = sugarCompiled.Program.Select(i => (GraphNodeOp)i.Op).ToArray();
            Assert.That(ops, Does.Contain(GraphNodeOp.ReadMapVarInt));
            Assert.That(ops, Does.Contain(GraphNodeOp.ConstInt));
            Assert.That(ops, Does.Contain(GraphNodeOp.CompareEqInt));
            Assert.That(ops, Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(ops, Does.Contain(GraphNodeOp.Jump));
            Assert.That(ops, Does.Not.Contain(GraphNodeOp.None));
            Assert.That(ops.All(o => Enum.IsDefined(o)), Is.True);
        }

        [Test]
        public void FsmState_SourceMap_KeepsAuthoredMemberSpelling()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateFsmGraph(bindEnum: true), null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            string[] fsmPorts = Enumerable.Range(0, compiled.Program.Length)
                .Where(i => compiled.SourceMap.TryGetSource(i, out GraphInstructionSource src) && src.NodeId == "fsm")
                .Select(i => compiled.SourceMap.Sources[i].ControlPort)
                .ToArray();
            Assert.That(fsmPorts, Does.Contain("case:Idle"));
            Assert.That(fsmPorts, Does.Contain("case:Combat"));
            Assert.That(fsmPorts, Does.Contain("case:Retreat"));
            Assert.That(fsmPorts, Does.Contain(GraphControlFlowPorts.Default));
            Assert.That(fsmPorts, Does.Contain("stateVar"));
        }

        [Test]
        public void FsmState_DispatchesToMatchingArm()
        {
            Assert.That(RunFsmWithState(phase: 1), Is.EqualTo(101), "phase=Combat hits case:Combat");
            Assert.That(RunFsmWithState(phase: 0), Is.EqualTo(100), "phase=Idle hits case:Idle");
            Assert.That(RunFsmWithState(phase: 2), Is.EqualTo(102), "phase=Retreat hits case:Retreat");
        }

        [Test]
        public void FsmState_FallsToDefaultOnUnmatchedState()
        {
            Assert.That(RunFsmWithState(phase: 7), Is.EqualTo(900), "unregistered phase value falls to default arm");
        }

        [Test]
        public void FsmState_ArmCanTransitionStateVar()
        {
            // Combat 臂先 WriteMapVarInt(fsm.phase, 2) 再 HaltReturnInt(101)：
            // 迁移 = 臂内向同名 map 变量写新相位，与手写图行为一致。
            GraphControlFlowDocument graph = CreateFsmGraph(bindEnum: true);
            graph.Nodes.Insert(4, new GraphControlFlowNode { Id = "retreatPhase", Op = nameof(GraphNodeOp.ConstInt), IntValue = 2 });
            graph.Nodes.Insert(6, new GraphControlFlowNode { Id = "toRetreat", Op = nameof(GraphNodeOp.WriteMapVarInt), Var = StateVarName });
            graph.ControlEdges.RemoveAll(e => e.From == "retVD");
            graph.ControlEdges.Add(new GraphControlFlowEdge("retVD", GraphControlFlowPorts.Next, "retreatPhase"));
            graph.ControlEdges.Add(new GraphControlFlowEdge("retreatPhase", GraphControlFlowPorts.Next, "fsm"));
            graph.ControlEdges.RemoveAll(e => e.From == "fsm" && e.FromPort == "case:Combat");
            graph.ControlEdges.Add(new GraphControlFlowEdge("fsm", "case:Combat", "toRetreat"));
            graph.ControlEdges.Add(new GraphControlFlowEdge("toRetreat", GraphControlFlowPorts.Next, "ret1"));
            graph.ValueEdges.Add(new GraphControlFlowValueEdge("retreatPhase", GraphControlFlowPorts.Value, "toRetreat", GraphControlFlowPorts.Value));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            MapVariableStore store = CreateStore(phase: 1);
            int result = ExecuteWithStore(compiled, store);
            Assert.That(result, Is.EqualTo(101));
            Assert.That(store.ReadInt(StateVarName), Is.EqualTo(2), "Combat arm transitions fsm.phase to Retreat");
        }

        [Test]
        public void FsmState_MissingEnumType_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateFsmGraph(bindEnum: false);

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.TypeMismatch &&
                d.NodeId == "fsm" &&
                d.Message.Contains("enumType", StringComparison.Ordinal)));
        }

        [Test]
        public void FsmState_UnregisteredEnumType_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateFsmGraph(bindEnum: true);
            graph.Nodes.First(n => n.Id == "fsm").EnumType = "Mod.NoSuchEnum";

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.TypeMismatch &&
                d.NodeId == "fsm" &&
                d.Message.Contains("Mod.NoSuchEnum", StringComparison.Ordinal)));
        }

        [Test]
        public void FsmState_WithoutCatalog_FailsClosed()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateFsmGraph(bindEnum: true), null, null);
            Assert.That(compiled.Succeeded, Is.False, "authored enumType with no catalog in scope must fail closed");
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.NodeId == "fsm" && d.Message.Contains("not registered", StringComparison.Ordinal)));
        }

        [Test]
        public void FsmState_MissingStateVar_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateFsmGraph(bindEnum: true);
            graph.Nodes.First(n => n.Id == "fsm").StateVar = " ";

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                d.NodeId == "fsm" &&
                d.Message.Contains("stateVar", StringComparison.Ordinal)));
        }

        [Test]
        public void FsmState_MissingDefault_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateFsmGraph(bindEnum: true);
            graph.ControlEdges.RemoveAll(e => e.From == "fsm" && e.FromPort == GraphControlFlowPorts.Default);

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge && d.NodeId == "fsm"));
        }

        [Test]
        public void FsmState_NoCaseArms_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateFsmGraph(bindEnum: true);
            graph.ControlEdges.RemoveAll(e => e.From == "fsm" && e.FromPort.StartsWith("case:", StringComparison.Ordinal));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge && d.NodeId == "fsm"));
        }

        [Test]
        public void FsmState_ValueInput_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateFsmGraph(bindEnum: true);
            graph.ValueEdges.Add(new GraphControlFlowValueEdge("retV0", GraphControlFlowPorts.Value, "fsm", GraphControlFlowPorts.Selector));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.NodeId == "fsm" && d.Message.Contains("Unexpected value input", StringComparison.Ordinal)));
        }

        [Test]
        public void FsmState_QueryKind_RejectsSugar()
        {
            GraphControlFlowDocument graph = CreateFsmGraph(bindEnum: true);
            graph.Kind = "Query";

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                d.NodeId == "fsm" &&
                d.Message.Contains(GraphAuthoringSugar.FsmState, StringComparison.Ordinal)));
        }

        private static Ludots.Core.Scripting.EnumCatalog CombatStateCatalog()
        {
            var builder = new Ludots.Core.Scripting.EnumCatalog.Builder();
            builder.AddOrAppend(
                (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(
                    @"{ ""id"": ""Mod.CombatState"", ""members"": [""Idle"", ""Combat"", ""Retreat""] }")!,
                "test enum");
            return builder.ToCatalog();
        }

        /// <summary>
        /// FsmState 版：节点序 [4 个臂值常量, fsm, 4 个 HaltReturnInt]，入口沿常量链走到 fsm。
        /// </summary>
        private static GraphControlFlowDocument CreateFsmGraph(bool bindEnum)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.fsm-state",
                Kind = "Script",
                Entry = "retV0",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "retV0", Op = nameof(GraphNodeOp.ConstInt), IntValue = 100 },
                    new() { Id = "retV1", Op = nameof(GraphNodeOp.ConstInt), IntValue = 101 },
                    new() { Id = "retV2", Op = nameof(GraphNodeOp.ConstInt), IntValue = 102 },
                    new() { Id = "retVD", Op = nameof(GraphNodeOp.ConstInt), IntValue = 900 },
                    new()
                    {
                        Id = "fsm",
                        Op = GraphAuthoringSugar.FsmState,
                        EnumType = bindEnum ? "Mod.CombatState" : null,
                        StateVar = StateVarName
                    },
                    new() { Id = "ret0", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "ret1", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "ret2", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "retDefault", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("retV0", GraphControlFlowPorts.Next, "retV1"),
                    new("retV1", GraphControlFlowPorts.Next, "retV2"),
                    new("retV2", GraphControlFlowPorts.Next, "retVD"),
                    new("retVD", GraphControlFlowPorts.Next, "fsm"),
                    new("fsm", "case:Idle", "ret0"),
                    new("fsm", "case:Combat", "ret1"),
                    new("fsm", "case:Retreat", "ret2"),
                    new("fsm", GraphControlFlowPorts.Default, "retDefault")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("retV0", GraphControlFlowPorts.Value, "ret0", GraphControlFlowPorts.Value),
                    new("retV1", GraphControlFlowPorts.Value, "ret1", GraphControlFlowPorts.Value),
                    new("retV2", GraphControlFlowPorts.Value, "ret2", GraphControlFlowPorts.Value),
                    new("retVD", GraphControlFlowPorts.Value, "retDefault", GraphControlFlowPorts.Value)
                }
            };
        }

        /// <summary>
        /// 消融对照：手写 ReadMapVarInt(var=fsm.phase) → SwitchInt(enumType 绑定，selector←read.value)。
        /// 节点序与 FsmState 版对齐，使寄存器分配与程序布局逐位可比。
        /// </summary>
        private static GraphControlFlowDocument CreateHandwrittenReadSwitchGraph()
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.fsm-state-handwritten",
                Kind = "Script",
                Entry = "retV0",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "retV0", Op = nameof(GraphNodeOp.ConstInt), IntValue = 100 },
                    new() { Id = "retV1", Op = nameof(GraphNodeOp.ConstInt), IntValue = 101 },
                    new() { Id = "retV2", Op = nameof(GraphNodeOp.ConstInt), IntValue = 102 },
                    new() { Id = "retVD", Op = nameof(GraphNodeOp.ConstInt), IntValue = 900 },
                    new() { Id = "read", Op = nameof(GraphNodeOp.ReadMapVarInt), Var = StateVarName },
                    new() { Id = "sw", Op = GraphControlFlowCompiler.SwitchIntOp, EnumType = "Mod.CombatState" },
                    new() { Id = "ret0", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "ret1", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "ret2", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "retDefault", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("retV0", GraphControlFlowPorts.Next, "retV1"),
                    new("retV1", GraphControlFlowPorts.Next, "retV2"),
                    new("retV2", GraphControlFlowPorts.Next, "retVD"),
                    new("retVD", GraphControlFlowPorts.Next, "read"),
                    new("read", GraphControlFlowPorts.Next, "sw"),
                    new("sw", "case:Idle", "ret0"),
                    new("sw", "case:Combat", "ret1"),
                    new("sw", "case:Retreat", "ret2"),
                    new("sw", GraphControlFlowPorts.Default, "retDefault")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("read", GraphControlFlowPorts.Value, "sw", GraphControlFlowPorts.Selector),
                    new("retV0", GraphControlFlowPorts.Value, "ret0", GraphControlFlowPorts.Value),
                    new("retV1", GraphControlFlowPorts.Value, "ret1", GraphControlFlowPorts.Value),
                    new("retV2", GraphControlFlowPorts.Value, "ret2", GraphControlFlowPorts.Value),
                    new("retVD", GraphControlFlowPorts.Value, "retDefault", GraphControlFlowPorts.Value)
                }
            };
        }

        private static int RunFsmWithState(int phase)
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateFsmGraph(bindEnum: true), null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            return ExecuteWithStore(compiled, CreateStore(phase));
        }

        private static MapVariableStore CreateStore(int phase)
        {
            return MapVariableStore.Create(
                new MapId("tests.map.fsm"),
                new List<MapVariableDeclaration>
                {
                    new() { Name = StateVarName, Type = MapVariableType.Int, Initial = phase }
                });
        }

        private static int ExecuteWithStore(GraphControlFlowCompileResult compiled, MapVariableStore store)
        {
            var program = compiled.Program.ToArray();
            GraphProgramSymbolPatcher.Patch(compiled.Package.Value.Symbols, program, new ThrowingSymbolResolver());

            var registry = new GraphProgramRegistry();
            registry.Register(1, program, GraphKind.Script);

            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world);
            api.BindMapVariableStoreResolver(_ => store);

            Entity caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();

            GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, program, api, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor,
                budgetSteps: 256, mapScope: store.MapId);

            Assert.That(result.Halted, Is.True);
            return result.ReturnInt;
        }

        /// <summary>
        /// FsmState 测试图只含 ConfigKeyRegistry 直补符号（ReadMapVarInt/WriteMapVarInt），
        /// 其余解析通道一律不该被触达。
        /// </summary>
        private sealed class ThrowingSymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => throw new InvalidOperationException(name);
            public int ResolveAttribute(string name) => throw new InvalidOperationException(name);
            public int ResolveEffectTemplate(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipType(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipMetric(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipFlag(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipReason(string name) => throw new InvalidOperationException(name);
            public int ResolveTargetDispatchPreset(string name) => throw new InvalidOperationException(name);
            public int ResolveEntityTemplate(string name) => throw new InvalidOperationException(name);
        }

        // ── 端到端：TriggerGraph entry 直挂 FsmState，MapVariableChanged 两跳链 ──

        private const string EngineMapIdValue = "map_fsm_sugar_probe";
        private const string EngineGraphName = "Graph.Fsm.Probe";
        private const string EngineTemplateId = "fsm_sugar_probe_entity";
        private const string EngineScopeInstanceId = "fsm_probe_scope";

        [Test]
        public void SentryGraphs_DeHollowed_AndFsmHostDrivesPhaseCycle()
        {
            // 4 张旧壳图去空心后的形状锁：不再是 ConstInt→HaltReturnInt 两指令立即 halt 壳。
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out _);
            foreach (var (idx, ins) in programs.RequireProgramArray(GraphIdRegistry.GetId("Graph.FSM.Sentry"), GraphKind.Script, "dump").Select((x, i) => (i, x)))
            {
                TestContext.WriteLine($"{idx,3}: {(GraphNodeOp)ins.Op} dst={ins.Dst} a={ins.A} b={ins.B} imm={ins.Imm}");
            }
            foreach (string name in new[]
            {
                "Graph.HFSM.Cond.AlwaysTrue",
                "Graph.HFSM.Combat.OnEnter",
                "Graph.HFSM.Combat.OnTick",
                "Graph.HFSM.Combat.OnExit"
            })
            {
                GraphInstruction[] program = programs.RequireProgramArray(
                    GraphIdRegistry.GetId(name), GraphKind.Script, "de-hollow check");
                Assert.That(program.Length, Is.GreaterThan(2), $"{name} must not be a ConstInt→HaltReturnInt shell");
                var ops = program.Select(i => (GraphNodeOp)i.Op).ToArray();
                Assert.That(ops, Does.Contain(GraphNodeOp.CompareLtInt), $"{name} must contain real branch logic");
                Assert.That(ops, Does.Contain(GraphNodeOp.JumpIfFalse), $"{name} must lower BranchBool");
            }

            // Graph.FSM.Sentry 经 GraphFsmHost 的相位环：近距离 100cm 逐波 Idle→Alert→Combat（保持），
            // 远距离 Retreat→Idle。
            using var host = new Ludots.Core.Gameplay.AI.Fsm.GraphFsmHost(
                programs, GraphIdRegistry.GetId("Graph.FSM.Sentry"), capacity: 1, "sentry.phase");
            int agent = host.AddAgent();
            var feed = new StaticDistanceFeed(100);

            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(1), "Idle arm: dist 100cm < 500cm alerts");
            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(2), "Alert arm: Always transitions to Combat");
            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(2), "Combat arm holds while dist < 200cm");

            feed.DistanceCm = 99999;
            host.ThinkWave(128, feed);
            TestContext.WriteLine($"wave4 return={host.LastReturns[agent]} phase={host.PhaseOf(agent)}");
            Assert.That(host.PhaseOf(agent), Is.EqualTo(3), "Combat arm retreats when the intruder is out of range");
            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(0), "Retreat arm: Always transitions back to Idle");
            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(0), "Idle arm holds with no intruder in range");
        }

        private sealed class StaticDistanceFeed : Ludots.Core.Gameplay.AI.BehaviorTree.IBehaviorTreeSensorFeed
        {
            public StaticDistanceFeed(int distanceCm)
            {
                DistanceCm = distanceCm;
            }

            public int DistanceCm { get; set; }

            public void WriteSensors(int agentIndex, int graphId, Span<int> ints, Span<byte> bools)
            {
                ints[0] = DistanceCm;
            }
        }

        [Test]
        public void FsmState_TriggerGraphEntry_TwoHopTransitionChain()
        {
            using var fixture = FsmEngineFixture.Create();
            using GameEngine engine = fixture.CreateEngine();

            GraphControlFlowCompileResult compiled = CompileFrontDoor(FsmTriggerGraphJson);
            Assert.That(compiled.Succeeded, Is.True, FormatDiagnostics(compiled));
            GraphProgramPackage package = compiled.Package!.Value;
            var resolver = new GasGraphSymbolResolver(
                new RelationshipTypeRegistry(),
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipReasonRegistry(),
                new TargetDispatchPresetRegistry(),
                new EntityTemplateKeyRegistry());
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver);

            RegisterTriggerGraph(engine, package);
            engine.LoadMap(EngineMapIdValue);

            MapVariableStore store = engine.CurrentMapSession!.Variables!;
            Assert.That(store.ReadInt("fsm.phase"), Is.EqualTo(99), "boots in the unmatched trap state");

            // 第一跳：外部写 99→0(Idle)，同步派发 MapVariableChanged → entry(varName=fsm.phase) 命中。
            store.WriteInt("fsm.phase", 0);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                "entry chain must execute without trigger errors");
            Assert.That(store.ReadInt("fsm.observedOld"), Is.EqualTo(99),
                "Idle arm reads MapTrigger.OldValueInt from the entry payload");
            Assert.That(store.ReadInt("fsm.observedNew"), Is.EqualTo(0),
                "Idle arm reads MapTrigger.VarValueInt from the entry payload");
            Assert.That(store.ReadInt("fsm.phase"), Is.EqualTo(1),
                "Idle arm transitions fsm.phase to Combat via WriteMapVarInt");
            Assert.That(store.ReadInt("fsm.last"), Is.EqualTo(99),
                "第二跳：写 fsm.phase 同步再派发，重入同一图走 case:Combat 臂写 fsm.last");
            Assert.That(store.GetRevision("fsm.phase"), Is.EqualTo(2u),
                "exactly two phase writes: the external kick and the in-graph transition");

            // 过滤器负例：写其他变量不得触发本 entry（否则 Combat 臂会把 fsm.last 重新写回 99）。
            store.WriteInt("fsm.last", 1234);
            Assert.That(store.ReadInt("fsm.last"), Is.EqualTo(1234),
                "filters.varName must keep writes to other variables from re-entering the graph");
        }

        /// <summary>
        /// The engine freezes GraphIdRegistry during init, so re-register the engine's own
        /// mappings and then claim this fixture's graph name (mirrors MapVariableStoreTests).
        /// </summary>
        private static int RegisterTriggerGraph(GameEngine engine, GraphProgramPackage package)
        {
            RegistryMapping[] mappings = GraphIdRegistry.SnapshotMappings();
            GraphIdRegistry.Clear();
            Array.Sort(mappings, (a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < mappings.Length; i++)
            {
                GraphIdRegistry.Register(mappings[i].Name);
            }

            int graphId = GraphIdRegistry.Register(EngineGraphName);
            engine.GetService(CoreServiceKeys.GraphProgramRegistry)
                .Register(graphId, package.Program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, package.Symbols, package.TriggerGraphEntries);
            return graphId;
        }

        private static string FormatDiagnostics(GraphControlFlowCompileResult compiled)
            => string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

        private static GraphControlFlowCompileResult CompileFrontDoor(string json)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, EngineGraphName, options, eventSchemas: null, CombatStateCatalog());
        }

        /// <summary>
        /// Idle 臂：记录 entry payload 的 old/new → WriteMapVarInt(fsm.phase, Combat) → 立即 Halt
        /// （写变量会同步再派发并重入同一 mount，写后立即 halt 规避共享寄存器被重入重置后继续使用）。
        /// Combat 臂：写 fsm.last=99 作为第二跳见证。default 臂：写 fsm.last=-99 陷阱。
        /// </summary>
        private static string FsmTriggerGraphJson => """
            {
              "kind": "TriggerGraph",
              "entries": [
                { "label": "on_phase_changed", "event": "MapVariableChanged", "start": "fsm", "filters": { "varName": "fsm.phase" } }
              ],
              "nodes": [
                { "id": "fsm", "op": "FsmState", "enumType": "Mod.CombatState", "stateVar": "fsm.phase" },
                { "id": "ldOld", "op": "LoadEntryPayloadInt", "payloadKey": "MapTrigger.OldValueInt" },
                { "id": "wrOld", "op": "WriteMapVarInt", "var": "fsm.observedOld" },
                { "id": "ldNew", "op": "LoadEntryPayloadInt", "payloadKey": "MapTrigger.VarValueInt" },
                { "id": "wrNew", "op": "WriteMapVarInt", "var": "fsm.observedNew" },
                { "id": "combatCode", "op": "ConstInt", "intValue": 1 },
                { "id": "toCombat", "op": "WriteMapVarInt", "var": "fsm.phase" },
                { "id": "idleDone", "op": "HaltReturnInt" },
                { "id": "combatMark", "op": "ConstInt", "intValue": 99 },
                { "id": "wrLast", "op": "WriteMapVarInt", "var": "fsm.last" },
                { "id": "combatDone", "op": "HaltReturnInt" },
                { "id": "trapCode", "op": "ConstInt", "intValue": -99 },
                { "id": "wrTrap", "op": "WriteMapVarInt", "var": "fsm.last" },
                { "id": "trapDone", "op": "HaltReturnInt" }
              ],
              "controlEdges": [
                { "from": "fsm", "fromPort": "case:Idle", "to": "ldOld" },
                { "from": "ldOld", "fromPort": "next", "to": "wrOld" },
                { "from": "wrOld", "fromPort": "next", "to": "ldNew" },
                { "from": "ldNew", "fromPort": "next", "to": "wrNew" },
                { "from": "wrNew", "fromPort": "next", "to": "combatCode" },
                { "from": "combatCode", "fromPort": "next", "to": "toCombat" },
                { "from": "toCombat", "fromPort": "next", "to": "idleDone" },
                { "from": "fsm", "fromPort": "case:Combat", "to": "combatMark" },
                { "from": "combatMark", "fromPort": "next", "to": "wrLast" },
                { "from": "wrLast", "fromPort": "next", "to": "combatDone" },
                { "from": "fsm", "fromPort": "default", "to": "trapCode" },
                { "from": "trapCode", "fromPort": "next", "to": "wrTrap" },
                { "from": "wrTrap", "fromPort": "next", "to": "trapDone" }
              ],
              "valueEdges": [
                { "from": "ldOld", "fromPort": "value", "to": "wrOld", "toPort": "value" },
                { "from": "ldNew", "fromPort": "value", "to": "wrNew", "toPort": "value" },
                { "from": "combatCode", "fromPort": "value", "to": "toCombat", "toPort": "value" },
                { "from": "combatMark", "fromPort": "value", "to": "wrLast", "toPort": "value" },
                { "from": "trapCode", "fromPort": "value", "to": "wrTrap", "toPort": "value" }
              ]
            }
            """;

        private sealed class FsmEngineFixture : IDisposable
        {
            private const string ModId = "FsmSugarFixtureMod";

            private FsmEngineFixture(string root)
            {
                Root = root;
            }

            public string Root { get; }

            public static FsmEngineFixture Create()
            {
                string root = Path.Combine(Path.GetTempPath(), "Ludots_GraphFsmSugarTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Entities"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Maps"));

                File.WriteAllText(
                    Path.Combine(root, ModId, "mod.json"),
                    $$"""
                    {
                      "name": "{{ModId}}",
                      "version": "1.0.0",
                      "description": "Asset-only FsmState sugar fixture",
                      "priority": 0,
                      "dependencies": {}
                    }
                    """);
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "game.json"),
                    """
                    {
                      "startupMapId": "map_fsm_sugar_probe",
                      "startupInputContexts": [],
                      "presentation": {
                        "presenterInstanceCapacity": 16,
                        "gasPresentationEventCapacity": 16,
                        "presentationEventStreamCapacity": 16,
                        "presentationOwnerChangeCapacity": 16,
                        "presenterCommandCapacity": 16,
                        "presenterTimerCapacity": 16,
                        "primitiveDrawBufferCapacity": 16,
                        "visualSnapshotBufferCapacity": 16,
                        "visualProxyBufferCapacity": 16,
                        "skinnedVisualBatchCapacity": 16,
                        "presentationRequestCapacity": 16,
                        "instancedBatchRequestCapacity": 16,
                        "instancedBatchOperationCapacity": 16,
                        "groundOverlayCapacity": 16,
                        "splineRibbonCapacity": 16,
                        "worldHudCapacity": 16,
                        "screenHudCapacity": 16,
                        "minimapMarkerCapacity": 16,
                        "runtimeEntitySpawnQueueCapacity": 16,
                        "runtimeEntitySpawnReceiptQueueCapacity": 16,
                        "cameraCulling": {
                          "highLodDistanceCm": 1000.0,
                          "mediumLodDistanceCm": 2000.0,
                          "lowLodDistanceCm": 3000.0
                        },
                        "minimap": {
                          "initialZoomNormalized": 1.0,
                          "wheelZoomNormalizedStep": 0.1,
                          "buttonZoomNormalizedStep": 0.2,
                          "zoomSliderEnabled": true,
                          "modeToggleEnabled": true,
                          "rotateToggleEnabled": true,
                          "debugMarkerSampleCapacity": 0,
                          "minZoomExtentMode": "OneChunk",
                          "maxZoomExtentMode": "FullMap",
                          "minZoomExplicitHalfExtentCm": 0.0,
                          "maxZoomExplicitHalfExtentCm": 0.0
                        }
                      },
                      "constants": {
                        "orderTypeIds": {
                          "castAbility": 100,
                          "moveTo": 101,
                          "attackTarget": 102,
                          "stop": 103
                        },
                        "responseChainOrderTypeIds": {
                          "chainPass": 1,
                          "chainNegate": 2,
                          "chainActivateEffect": 3
                        },
                        "attributes": {
                          "health": "Health"
                        }
                      }
                    }
                    """);
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "Entities", "templates.json"),
                    $$"""
                    [
                      {
                        "id": "{{EngineTemplateId}}",
                        "components": {
                          "Name": { "Value": "Fsm Sugar Probe Entity" }
                        }
                      }
                    ]
                    """);
                string mapJson = $$"""
                    {
                      "Id": "{{EngineMapIdValue}}",
                      "Tags": [ "camera.skip_default_on_load" ],
                      "Entities": [
                        { "InstanceId": "{{EngineScopeInstanceId}}", "Template": "{{EngineTemplateId}}" }
                      ],
                      "Variables": [
                        { "name": "fsm.phase", "type": "int", "initial": 99 },
                        { "name": "fsm.observedOld", "type": "int", "initial": -1 },
                        { "name": "fsm.observedNew", "type": "int", "initial": -1 },
                        { "name": "fsm.last", "type": "int", "initial": -1 }
                      ],
                      "TriggerGraphs": [ { "graph": "{{EngineGraphName}}", "scopeInstanceId": "{{EngineScopeInstanceId}}" } ]
                    }
                    """;
                File.WriteAllText(Path.Combine(root, ModId, "assets", "Maps", $"{EngineMapIdValue}.json"), mapJson);
                return new FsmEngineFixture(root);
            }

            public GameEngine CreateEngine()
            {
                string repoRoot = FindRepoRoot();
                var engine = new GameEngine();
                engine.InitializeWithConfigPipeline(
                    new List<string>
                    {
                        Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                        Path.Combine(Root, ModId),
                    },
                    Path.Combine(repoRoot, "assets"));
                return engine;
            }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }

            private static string FindRepoRoot()
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 10 && dir != null; i++)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "assets")) &&
                        Directory.Exists(Path.Combine(dir.FullName, "src")))
                    {
                        return dir.FullName;
                    }

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
            }
        }
    }
}
