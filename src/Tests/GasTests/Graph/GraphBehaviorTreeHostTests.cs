using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// GraphBehaviorTreeHost acceptance (BT-1 judges b-e). Judge (c) is structural: nothing in
    /// this file constructs BehaviorTreeWorld or BehaviorTreeDefinition — the tree semantics live
    /// in the compiled Script instructions and the host only parks/resumes per-agent frames.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class GraphBehaviorTreeHostTests
    {
        [Test]
        public void 消融对照_糖构建与手写等价Script图逐波一致()
        {
            GraphControlFlowCompileResult sugarCompiled = CompileTree(SugarSequenceSelectorTreeJson());
            Assert.That(sugarCompiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(sugarCompiled.Diagnostics));

            ManualTree manual = ManualTree.BuildSequenceSelectorTree();
            GraphControlFlowCompileResult manualCompiled = GraphControlFlowCompiler.Compile(manual.Document);
            Assert.That(manualCompiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(manualCompiled.Diagnostics));

            var programs = new GraphProgramRegistry();
            int sugarId = RegisterProgram(programs, sugarCompiled, 101);
            int manualId = RegisterProgram(programs, manualCompiled, 102);

            var sugarApi = new ScriptedBlackboardApi(sugarCompiled.Package!.Value.Symbols);
            var manualApi = new ScriptedBlackboardApi(manualCompiled.Package!.Value.Symbols);

            using var world = World.Create();
            Entity caster = world.Create();

            var sugarHost = new GraphBehaviorTreeHost(programs, sugarId, capacity: 1);
            var manualHost = new GraphBehaviorTreeHost(programs, manualId, capacity: 1);
            sugarHost.AddAgent();
            manualHost.AddAgent();

            // (a,b,c) blackboard values per wave; a leaf's status IS its raw blackboard value (0/1/2).
            (int A, int B, int C, BehaviorTreeStatus Expected)[] waves =
            {
                (1, 1, 0, BehaviorTreeStatus.Success),   // A succeeds, selector short-circuits on B
                (1, 0, 1, BehaviorTreeStatus.Success),   // B fails, C succeeds
                (1, 0, 0, BehaviorTreeStatus.Failure),   // selector exhausts -> failure
                (0, 1, 1, BehaviorTreeStatus.Failure),   // A fails -> sequence short-circuits
                (1, 2, 0, BehaviorTreeStatus.Running),   // B reports Running -> tick ends Running
                (1, 1, 0, BehaviorTreeStatus.Success),   // Running re-evaluates from the root
            };

            for (int wave = 0; wave < waves.Length; wave++)
            {
                sugarApi.Set("bt.a", waves[wave].A);
                sugarApi.Set("bt.b", waves[wave].B);
                sugarApi.Set("bt.c", waves[wave].C);
                manualApi.Set("bt.a", waves[wave].A);
                manualApi.Set("bt.b", waves[wave].B);
                manualApi.Set("bt.c", waves[wave].C);

                sugarHost.RestartFinishedAgents();
                manualHost.RestartFinishedAgents();
                sugarHost.ThinkWave(256, sensors: null, world, caster, default, sugarApi);
                manualHost.ThinkWave(256, sensors: null, world, caster, default, manualApi);

                Assert.That(sugarHost.StatusOf(0), Is.EqualTo(manualHost.StatusOf(0)),
                    $"Wave {wave}: sugar-built tree must match the hand-written equivalent Script graph.");
                Assert.That(sugarHost.StatusOf(0), Is.EqualTo(waves[wave].Expected),
                    $"Wave {wave}: expected {waves[wave].Expected} (a={waves[wave].A}, b={waves[wave].B}, c={waves[wave].C}).");
                Assert.That(manualHost.StatusOf(0), Is.EqualTo(waves[wave].Expected));
            }
        }

        [Test]
        public void 消融对照_装饰反转糖与手写等价图逐波一致()
        {
            GraphControlFlowCompileResult sugarCompiled = CompileTree(SugarInverterTreeJson());
            ManualTree manual = ManualTree.BuildInverterTree();
            GraphControlFlowCompileResult manualCompiled = GraphControlFlowCompiler.Compile(manual.Document);
            Assert.That(sugarCompiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(sugarCompiled.Diagnostics));
            Assert.That(manualCompiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(manualCompiled.Diagnostics));

            var programs = new GraphProgramRegistry();
            int sugarId = RegisterProgram(programs, sugarCompiled, 111);
            int manualId = RegisterProgram(programs, manualCompiled, 112);
            var sugarApi = new ScriptedBlackboardApi(sugarCompiled.Package!.Value.Symbols);
            var manualApi = new ScriptedBlackboardApi(manualCompiled.Package!.Value.Symbols);

            using var world = World.Create();
            Entity caster = world.Create();
            var sugarHost = new GraphBehaviorTreeHost(programs, sugarId, capacity: 1);
            var manualHost = new GraphBehaviorTreeHost(programs, manualId, capacity: 1);
            sugarHost.AddAgent();
            manualHost.AddAgent();

            (int A, int B, BehaviorTreeStatus Expected)[] waves =
            {
                (1, 0, BehaviorTreeStatus.Success), // child fails -> inverter succeeds -> sequence succeeds
                (1, 1, BehaviorTreeStatus.Failure), // child succeeds -> inverter fails the sequence
                (0, 1, BehaviorTreeStatus.Failure), // leafA fails first
                (1, 2, BehaviorTreeStatus.Running), // child Running passes through the inverter
            };

            for (int wave = 0; wave < waves.Length; wave++)
            {
                sugarApi.Set("bt.a", waves[wave].A);
                sugarApi.Set("bt.b", waves[wave].B);
                manualApi.Set("bt.a", waves[wave].A);
                manualApi.Set("bt.b", waves[wave].B);

                sugarHost.RestartFinishedAgents();
                manualHost.RestartFinishedAgents();
                sugarHost.ThinkWave(256, sensors: null, world, caster, default, sugarApi);
                manualHost.ThinkWave(256, sensors: null, world, caster, default, manualApi);

                Assert.That(sugarHost.StatusOf(0), Is.EqualTo(manualHost.StatusOf(0)), $"Wave {wave} decorator ablation mismatch.");
                Assert.That(sugarHost.StatusOf(0), Is.EqualTo(waves[wave].Expected), $"Wave {wave} expected {waves[wave].Expected}.");
            }
        }

        /// <summary>
        /// Judge (d): a Wait leaf parks pc inside the leaf with the parent's resume address on the
        /// call stack; the next wave resumes, the leaf Returns, and the sequence continues with its
        /// next child (the trailing child proves the parent really resumed mid-sequence).
        /// </summary>
        [Test]
        public void Yield叶跨波恢复_Return弹回父级继续下一子()
        {
            GraphInstruction[] program = CompileTreeProgram("""
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "c1", "op": "ConstInt", "intValue": 1 },
                    { "id": "chase", "op": "Wait" },
                    { "id": "chaseDone", "op": "ConstInt", "intValue": 1 },
                    { "id": "c3", "op": "ConstInt", "intValue": 0 }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "c1" },
                    { "from": "seq", "fromPort": "child:1", "to": "chase" },
                    { "from": "seq", "fromPort": "child:2", "to": "c3" },
                    { "from": "chase", "fromPort": "next", "to": "chaseDone" }
                  ],
                  "valueEdges": []
                }
                """, 121);

            var programs = new GraphProgramRegistry();
            programs.Register(121, program, GraphKind.Script);
            using var world = World.Create();

            var host = new GraphBehaviorTreeHost(programs, 121, capacity: 1);
            host.AddAgent();

            host.ThinkWave(64, sensors: null, world);
            Assert.That(host.StatusOf(0), Is.EqualTo(BehaviorTreeStatus.Running));
            Assert.That(host.IsSuspended(0), Is.True, "The Wait leaf must park the frame for the next wave.");
            GraphExecutionCursor parked = host.CursorOf(0);
            Assert.That(parked.CallStackCount, Is.EqualTo(1), "The parent sequence's resume address stays on the call stack.");

            GraphBehaviorTreeThinkStats second = host.ThinkWave(64, sensors: null, world);
            Assert.That(second.Resumed, Is.EqualTo(1), "Wave two must resume the parked run, not restart it.");
            Assert.That(host.StatusOf(0), Is.EqualTo(BehaviorTreeStatus.Failure),
                "After resume the leaf Returns, the sequence proceeds to child 3 (ConstInt 0) and fails — proving the parent continued at the next child.");
            Assert.That(host.LastReturns[0], Is.EqualTo(GraphBtStatusCodes.Failure));
        }

        /// <summary>Judge (e): slice-budget suspension parks the run; the next wave resumes to completion.</summary>
        [Test]
        public void 步数预算越界挂起_下波续跑完成()
        {
            var chainNodes = new List<GraphControlFlowNode>
            {
                new() { Id = "seq", Op = GraphAuthoringSugar.BtSequence },
                new() { Id = "c1", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1 }
            };
            var chainEdges = new List<GraphControlFlowEdge> { new("seq", GraphControlFlowPorts.Child(0), "c1") };
            for (int i = 0; i < 12; i++)
            {
                string id = $"step{i}";
                chainNodes.Add(new GraphControlFlowNode { Id = id, Op = nameof(GraphNodeOp.ConstInt), IntValue = i });
                chainEdges.Add(new GraphControlFlowEdge(i == 0 ? "seq" : $"step{i - 1}",
                    i == 0 ? GraphControlFlowPorts.Child(1) : GraphControlFlowPorts.Next, id));
            }

            var doc = new GraphControlFlowDocument
            {
                Id = "tests.script.bt-budget",
                Kind = "Script",
                Entry = "seq",
                Nodes = chainNodes,
                ControlEdges = chainEdges,
                ValueEdges = new List<GraphControlFlowValueEdge>()
            };

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            var programs = new GraphProgramRegistry();
            programs.Register(131, compiled.Package!.Value.Program, GraphKind.Script);
            using var world = World.Create();

            var host = new GraphBehaviorTreeHost(programs, 131, capacity: 1);
            host.AddAgent();

            host.ThinkWave(budgetSteps: 2, sensors: null, world);
            Assert.That(host.IsSuspended(0), Is.True, "A two-step budget must suspend the run, not truncate it.");
            Assert.That(host.StatusOf(0), Is.EqualTo(BehaviorTreeStatus.Running));

            GraphBehaviorTreeThinkStats second = host.ThinkWave(budgetSteps: 256, sensors: null, world);
            Assert.That(second.Resumed, Is.EqualTo(1));
            Assert.That(host.StatusOf(0), Is.EqualTo(BehaviorTreeStatus.Success),
                "The last chain step reports 1, the sequence completes, and the root halts Success.");
        }

        /// <summary>Judge (e): call-stack overflow fails closed at execution (compile-time depth is covered in the sugar tests).</summary>
        [Test]
        public void 调用栈超上限_失败关闭()
        {
            var program = new GraphInstruction[19];
            program[0] = new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 };
            for (int i = 1; i <= 17; i++)
            {
                program[i] = new GraphInstruction { Op = (ushort)GraphNodeOp.Call, Imm = i + 1 };
            }

            program[18] = new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 };

            var programs = new GraphProgramRegistry();
            programs.Register(141, program, GraphKind.Script);
            using var world = World.Create();

            var host = new GraphBehaviorTreeHost(programs, 141, capacity: 1);
            host.AddAgent();

            Assert.That(() => host.ThinkWave(64, sensors: null, world), Throws.InvalidOperationException
                .With.Message.Contains(nameof(GraphVmLimits.MaxCallStackDepth)));
        }

        /// <summary>
        /// Judge (c): the graph path needs only the program registry — the tree, its blackboard
        /// leaves, and the tick statuses are all Script instructions. Void action leaves report
        /// Success through the shared epilogue and their side effects go through the runtime API.
        /// </summary>
        [Test]
        public void 树语义在指令里_宿主只需程序登记表()
        {
            GraphControlFlowCompileResult compiled = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "load", "op": "LoadCaster" },
                    { "id": "x", "op": "ConstInt", "intValue": 11 },
                    { "id": "y", "op": "ConstInt", "intValue": 22 },
                    { "id": "move", "op": "SetWorldPosition" }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "load" },
                    { "from": "load", "fromPort": "next", "to": "x" },
                    { "from": "x", "fromPort": "next", "to": "y" },
                    { "from": "y", "fromPort": "next", "to": "move" }
                  ],
                  "valueEdges": [
                    { "from": "load", "fromPort": "value", "to": "move", "toPort": "source" },
                    { "from": "x", "fromPort": "value", "to": "move", "toPort": "a" },
                    { "from": "y", "fromPort": "value", "to": "move", "toPort": "b" }
                  ]
                }
                """);
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            var programs = new GraphProgramRegistry();
            programs.Register(151, compiled.Package!.Value.Program, GraphKind.Script);
            var api = new ScriptedBlackboardApi.RecordingMoveApi();
            using var world = World.Create();
            Entity caster = world.Create();

            var host = new GraphBehaviorTreeHost(programs, 151, capacity: 1);
            host.AddAgent();
            host.ThinkWave(64, sensors: null, world, caster, default, api);

            Assert.That(host.StatusOf(0), Is.EqualTo(BehaviorTreeStatus.Success),
                "A Void action leaf succeeds through the shared terminal epilogue.");
            Assert.That(api.Moves, Has.Count.EqualTo(1));
            Assert.That(api.Moves[0], Is.EqualTo((11, 22)),
                "The leaf's side effect went through the runtime API on the caster.");
        }

        [Test]
        public void 宿主非法状态码_失败关闭()
        {
            // Hand-built tree-shaped program halting with an out-of-contract status code.
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = 9 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 }
            };

            var programs = new GraphProgramRegistry();
            programs.Register(161, program, GraphKind.Script);
            using var world = World.Create();

            var host = new GraphBehaviorTreeHost(programs, 161, capacity: 1);
            host.AddAgent();

            Assert.That(() => host.ThinkWave(64, sensors: null, world), Throws.InvalidOperationException
                .With.Message.Contains(nameof(GraphBtStatusCodes)));
        }

        /// <summary>
        /// De-hollowed leaf pattern (BT-B): glue writes the distance into the blackboard;
        /// the leaf chain owns the threshold comparison and ends in a Bool tail (the BT
        /// epilogue maps it to Success/Failure), while an intent leaf drains through a
        /// pinned register the host reads back with ReadInt.
        /// </summary>
        [Test]
        public void 黑板条件叶_胶水喂距离_阈值判定与意图钉读回()
        {
            GraphControlFlowCompileResult compiled = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "root",
                  "nodes": [
                    { "id": "root", "op": "BtSequence" },
                    { "id": "seLoad", "op": "LoadCaster" },
                    { "id": "seRead", "op": "ReadBlackboardInt", "blackboardKey": "bt.dist" },
                    { "id": "seSight", "op": "ConstInt", "intValue": 551 },
                    { "id": "seCmp", "op": "CompareLtInt" },
                    { "id": "chLoad", "op": "LoadCaster" },
                    { "id": "chRead", "op": "ReadBlackboardInt", "blackboardKey": "bt.dist" },
                    { "id": "chSight", "op": "ConstInt", "intValue": 551 },
                    { "id": "chCmp", "op": "CompareLtInt" },
                    { "id": "intent", "op": "ConstInt", "intValue": 1, "pinRegister": 3 }
                  ],
                  "controlEdges": [
                    { "from": "root", "fromPort": "child:0", "to": "seLoad" },
                    { "from": "seLoad", "fromPort": "next", "to": "seRead" },
                    { "from": "seRead", "fromPort": "next", "to": "seSight" },
                    { "from": "seSight", "fromPort": "next", "to": "seCmp" },
                    { "from": "root", "fromPort": "child:1", "to": "chLoad" },
                    { "from": "chLoad", "fromPort": "next", "to": "chRead" },
                    { "from": "chRead", "fromPort": "next", "to": "chSight" },
                    { "from": "chSight", "fromPort": "next", "to": "chCmp" },
                    { "from": "root", "fromPort": "child:2", "to": "intent" }
                  ],
                  "valueEdges": [
                    { "from": "seLoad", "fromPort": "value", "to": "seRead", "toPort": "source" },
                    { "from": "seRead", "fromPort": "value", "to": "seCmp", "toPort": "a" },
                    { "from": "seSight", "fromPort": "value", "to": "seCmp", "toPort": "b" },
                    { "from": "chLoad", "fromPort": "value", "to": "chRead", "toPort": "source" },
                    { "from": "chRead", "fromPort": "value", "to": "chCmp", "toPort": "a" },
                    { "from": "chSight", "fromPort": "value", "to": "chCmp", "toPort": "b" }
                  ]
                }
                """);
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            var programs = new GraphProgramRegistry();
            int graphId = RegisterProgram(programs, compiled, 171);
            using var world = World.Create();

            var api = new ScriptedBlackboardApi(new[] { "bt.dist" });
            var host = new GraphBehaviorTreeHost(programs, graphId, capacity: 1);
            host.AddAgent();

            api.Set("bt.dist", 500);
            host.ThinkWave(64, sensors: null, world, default, default, api);
            Assert.That(host.StatusOf(0), Is.EqualTo(BehaviorTreeStatus.Success), "500 < 551: seen and chased, intent leaf commits 1.");
            Assert.That(host.ReadInt(0, 3), Is.EqualTo(1), "The intent leaf's pinned register is readable from the host.");

            host.RestartFinishedAgents();
            api.Set("bt.dist", 6_000);
            host.ThinkWave(64, sensors: null, world, default, default, api);
            Assert.That(host.StatusOf(0), Is.EqualTo(BehaviorTreeStatus.Failure), "6000 >= 551: not seen, the sequence fails at child 0.");
            Assert.That(host.ReadInt(0, 3), Is.EqualTo(0), "Restart clears registers, so the intent pin reads zero.");

            host.RestartFinishedAgents();
            api.Set("bt.dist", 551);
            host.ThinkWave(64, sensors: null, world, default, default, api);
            Assert.That(host.StatusOf(0), Is.EqualTo(BehaviorTreeStatus.Failure), "Inclusive bound: 551 is not < 551.");
        }

        private static string SugarSequenceSelectorTreeJson()
            => """
                {
                  "kind": "Script",
                  "entry": "root",
                  "nodes": [
                    { "id": "root", "op": "BtSequence" },
                    { "id": "sel", "op": "BtSelector" },
                    { "id": "aLoad", "op": "LoadCaster" },
                    { "id": "aRead", "op": "ReadBlackboardInt", "blackboardKey": "bt.a" },
                    { "id": "bLoad", "op": "LoadCaster" },
                    { "id": "bRead", "op": "ReadBlackboardInt", "blackboardKey": "bt.b" },
                    { "id": "cLoad", "op": "LoadCaster" },
                    { "id": "cRead", "op": "ReadBlackboardInt", "blackboardKey": "bt.c" }
                  ],
                  "controlEdges": [
                    { "from": "root", "fromPort": "child:0", "to": "aLoad" },
                    { "from": "root", "fromPort": "child:1", "to": "sel" },
                    { "from": "sel", "fromPort": "child:0", "to": "bLoad" },
                    { "from": "sel", "fromPort": "child:1", "to": "cLoad" },
                    { "from": "aLoad", "fromPort": "next", "to": "aRead" },
                    { "from": "bLoad", "fromPort": "next", "to": "bRead" },
                    { "from": "cLoad", "fromPort": "next", "to": "cRead" }
                  ],
                  "valueEdges": [
                    { "from": "aLoad", "fromPort": "value", "to": "aRead", "toPort": "source" },
                    { "from": "bLoad", "fromPort": "value", "to": "bRead", "toPort": "source" },
                    { "from": "cLoad", "fromPort": "value", "to": "cRead", "toPort": "source" }
                  ]
                }
                """;

        private static string SugarInverterTreeJson()
            => """
                {
                  "kind": "Script",
                  "entry": "root",
                  "nodes": [
                    { "id": "root", "op": "BtSequence" },
                    { "id": "inv", "op": "BtDecorator", "decoratorKind": "inverter" },
                    { "id": "aLoad", "op": "LoadCaster" },
                    { "id": "aRead", "op": "ReadBlackboardInt", "blackboardKey": "bt.a" },
                    { "id": "bLoad", "op": "LoadCaster" },
                    { "id": "bRead", "op": "ReadBlackboardInt", "blackboardKey": "bt.b" }
                  ],
                  "controlEdges": [
                    { "from": "root", "fromPort": "child:0", "to": "aLoad" },
                    { "from": "root", "fromPort": "child:1", "to": "inv" },
                    { "from": "inv", "fromPort": "child:0", "to": "bLoad" },
                    { "from": "aLoad", "fromPort": "next", "to": "aRead" },
                    { "from": "bLoad", "fromPort": "next", "to": "bRead" }
                  ],
                  "valueEdges": [
                    { "from": "aLoad", "fromPort": "value", "to": "aRead", "toPort": "source" },
                    { "from": "bLoad", "fromPort": "value", "to": "bRead", "toPort": "source" }
                  ]
                }
                """;

        private static GraphControlFlowCompileResult CompileTree(string json)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, "tests.script.bt.host", options);
        }

        private static GraphInstruction[] CompileTreeProgram(string json, int graphId)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, $"tests.script.bt.{graphId}", options);
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            return compiled.Package!.Value.Program;
        }

        private static int RegisterProgram(GraphProgramRegistry programs, GraphControlFlowCompileResult compiled, int graphId)
        {
            programs.Register(graphId, compiled.Package!.Value.Program, GraphKind.Script,
                GraphInstructionSourceMap.Empty, compiled.Package.Value.Symbols);
            return graphId;
        }

        /// <summary>
        /// Hand-authored equivalent of the sugar lowering: Call/Return composites with
        /// CompareEqInt + JumpIfFalse status checks and pinned status registers. No sugar nodes —
        /// the ablation control arm for judge (b).
        /// </summary>
        private sealed class ManualTree
        {
            // High pins avoid the compiler-allocated int registers (I[0] is the host ABI slot).
            private const int RootStatusPin = 30;
            private const int ChildStatusPin = 31;

            private int _seq;

            public GraphControlFlowDocument Document { get; } = new()
            {
                Id = "tests.script.bt.manual",
                Kind = "Script"
            };

            public static ManualTree BuildSequenceSelectorTree()
            {
                var tree = new ManualTree();
                (string headA, string statusA) = tree.AddBlackboardLeaf("bt.a");
                (string headB, string statusB) = tree.AddBlackboardLeaf("bt.b");
                (string headC, string statusC) = tree.AddBlackboardLeaf("bt.c");
                (string headSel, string statusSel) = tree.AddComposite(
                    isSelector: true,
                    isRoot: false,
                    childHeads: new[] { headB, headC },
                    childStatus: new[] { statusB, statusC },
                    statusPin: ChildStatusPin);
                (string headRoot, _) = tree.AddComposite(
                    isSelector: false,
                    isRoot: true,
                    childHeads: new[] { headA, headSel },
                    childStatus: new[] { statusA, statusSel },
                    statusPin: RootStatusPin);
                tree.Document.Entry = headRoot;
                return tree;
            }

            public static ManualTree BuildInverterTree()
            {
                var tree = new ManualTree();
                (string headA, string statusA) = tree.AddBlackboardLeaf("bt.a");
                (string headB, string statusB) = tree.AddBlackboardLeaf("bt.b");
                (string headInv, string statusInv) = tree.AddInverter(headB, statusB, ChildStatusPin);
                (string headRoot, _) = tree.AddComposite(
                    isSelector: false,
                    isRoot: true,
                    childHeads: new[] { headA, headInv },
                    childStatus: new[] { statusA, statusInv },
                    statusPin: RootStatusPin);
                tree.Document.Entry = headRoot;
                return tree;
            }

            private (string Head, string StatusNode) AddBlackboardLeaf(string blackboardKey)
            {
                string load = AddNode("leaf.load.", nameof(GraphNodeOp.LoadCaster));
                string read = AddNode("leaf.read.", nameof(GraphNodeOp.ReadBlackboardInt), blackboardKey: blackboardKey);
                string ret = AddNode("leaf.ret.", nameof(GraphNodeOp.Return));
                AddControl(load, GraphControlFlowPorts.Next, read);
                AddControl(read, GraphControlFlowPorts.Next, ret);
                AddValue(load, GraphControlFlowPorts.Value, read, GraphControlFlowPorts.Source);
                return (load, read);
            }

            private (string Head, string StatusNode) AddComposite(
                bool isSelector,
                bool isRoot,
                string[] childHeads,
                string[] childStatus,
                int statusPin)
            {
                string succSt = AddPinnedConst("c.succ.", 1, statusPin);
                string failSt = AddPinnedConst("c.fail.", 0, statusPin);
                string runSt = AddPinnedConst("c.run.", 2, statusPin);
                WireExit(succSt, isRoot);
                WireExit(failSt, isRoot);
                WireExit(runSt, isRoot);
                string shortCircuit = isSelector ? succSt : failSt;
                string finalExit = isSelector ? failSt : succSt;

                var calls = new string[childHeads.Length];
                for (int c = 0; c < childHeads.Length; c++)
                {
                    calls[c] = AddNode($"c.call{c}.", nameof(GraphNodeOp.Call));
                }

                for (int i = 0; i < childHeads.Length; i++)
                {
                    AddControl(calls[i], GraphControlFlowPorts.Call, childHeads[i]);

                    string zero = AddNode($"c.z{i}.", nameof(GraphNodeOp.ConstInt), intValue: isSelector ? 1 : 0);
                    string eq = AddNode($"c.eq{i}.", nameof(GraphNodeOp.CompareEqInt));
                    string jif = AddNode($"c.jif{i}.", nameof(GraphNodeOp.JumpIfFalse));
                    string runZero = AddNode($"c.rz{i}.", nameof(GraphNodeOp.ConstInt), intValue: 2);
                    string runEq = AddNode($"c.req{i}.", nameof(GraphNodeOp.CompareEqInt));
                    string runJif = AddNode($"c.rjif{i}.", nameof(GraphNodeOp.JumpIfFalse));

                    AddControl(calls[i], GraphControlFlowPorts.Next, zero);
                    AddControl(zero, GraphControlFlowPorts.Next, eq);
                    AddControl(eq, GraphControlFlowPorts.Next, jif);
                    AddControl(jif, GraphControlFlowPorts.False, runZero);
                    AddControl(jif, GraphControlFlowPorts.True, shortCircuit);
                    AddControl(runZero, GraphControlFlowPorts.Next, runEq);
                    AddControl(runEq, GraphControlFlowPorts.Next, runJif);
                    AddControl(runJif, GraphControlFlowPorts.True, runSt);
                    AddControl(runJif, GraphControlFlowPorts.False,
                        i == childHeads.Length - 1 ? finalExit : calls[i + 1]);

                    AddValue(childStatus[i], GraphControlFlowPorts.Value, eq, GraphControlFlowPorts.A);
                    AddValue(zero, GraphControlFlowPorts.Value, eq, GraphControlFlowPorts.B);
                    AddValue(eq, GraphControlFlowPorts.Value, jif, GraphControlFlowPorts.Condition);
                    AddValue(childStatus[i], GraphControlFlowPorts.Value, runEq, GraphControlFlowPorts.A);
                    AddValue(runZero, GraphControlFlowPorts.Value, runEq, GraphControlFlowPorts.B);
                    AddValue(runEq, GraphControlFlowPorts.Value, runJif, GraphControlFlowPorts.Condition);
                }

                return (calls[0], succSt);
            }

            private (string Head, string StatusNode) AddInverter(string childHead, string childStatus, int statusPin)
            {
                string call = AddNode("inv.call.", nameof(GraphNodeOp.Call));
                AddControl(call, GraphControlFlowPorts.Call, childHead);

                string zero = AddNode("inv.z0.", nameof(GraphNodeOp.ConstInt), intValue: 0);
                string eq = AddNode("inv.eq0.", nameof(GraphNodeOp.CompareEqInt));
                string jif = AddNode("inv.jif0.", nameof(GraphNodeOp.JumpIfFalse));
                string one = AddNode("inv.z1.", nameof(GraphNodeOp.ConstInt), intValue: 1);
                string eq1 = AddNode("inv.eq1.", nameof(GraphNodeOp.CompareEqInt));
                string jif1 = AddNode("inv.jif1.", nameof(GraphNodeOp.JumpIfFalse));
                string succSt = AddPinnedConst("inv.succ.", 1, statusPin);
                string failSt = AddPinnedConst("inv.fail.", 0, statusPin);
                string runThrough = AddPinnedConst("inv.run.", 2, statusPin);
                string ret = AddNode("inv.ret.", nameof(GraphNodeOp.Return));

                AddControl(call, GraphControlFlowPorts.Next, zero);
                AddControl(zero, GraphControlFlowPorts.Next, eq);
                AddControl(eq, GraphControlFlowPorts.Next, jif);
                AddControl(jif, GraphControlFlowPorts.False, one);
                AddControl(jif, GraphControlFlowPorts.True, succSt);
                AddControl(one, GraphControlFlowPorts.Next, eq1);
                AddControl(eq1, GraphControlFlowPorts.Next, jif1);
                AddControl(jif1, GraphControlFlowPorts.False, runThrough);
                AddControl(jif1, GraphControlFlowPorts.True, failSt);
                AddControl(succSt, GraphControlFlowPorts.Next, ret);
                AddControl(failSt, GraphControlFlowPorts.Next, ret);
                AddControl(runThrough, GraphControlFlowPorts.Next, ret);

                AddValue(childStatus, GraphControlFlowPorts.Value, eq, GraphControlFlowPorts.A);
                AddValue(zero, GraphControlFlowPorts.Value, eq, GraphControlFlowPorts.B);
                AddValue(eq, GraphControlFlowPorts.Value, jif, GraphControlFlowPorts.Condition);
                AddValue(childStatus, GraphControlFlowPorts.Value, eq1, GraphControlFlowPorts.A);
                AddValue(one, GraphControlFlowPorts.Value, eq1, GraphControlFlowPorts.B);
                AddValue(eq1, GraphControlFlowPorts.Value, jif1, GraphControlFlowPorts.Condition);
                return (call, succSt);
            }

            private void WireExit(string pinnedConstNode, bool isRoot)
            {
                if (isRoot)
                {
                    string halt = AddNode($"{pinnedConstNode}halt", nameof(GraphNodeOp.HaltReturnInt));
                    AddControl(pinnedConstNode, GraphControlFlowPorts.Next, halt);
                    AddValue(pinnedConstNode, GraphControlFlowPorts.Value, halt, GraphControlFlowPorts.Value);
                    return;
                }

                string ret = AddNode($"{pinnedConstNode}ret", nameof(GraphNodeOp.Return));
                AddControl(pinnedConstNode, GraphControlFlowPorts.Next, ret);
            }

            private string AddNode(string prefix, string op, int intValue = 0, string? blackboardKey = null)
            {
                string id = prefix + _seq;
                _seq++;
                Document.Nodes.Add(new GraphControlFlowNode
                {
                    Id = id,
                    Op = op,
                    IntValue = intValue,
                    BlackboardKey = blackboardKey
                });
                return id;
            }

            private string AddPinnedConst(string prefix, int value, int pin)
            {
                string id = prefix + _seq;
                _seq++;
                Document.Nodes.Add(new GraphControlFlowNode
                {
                    Id = id,
                    Op = nameof(GraphNodeOp.ConstInt),
                    IntValue = value,
                    PinRegister = pin
                });
                return id;
            }

            private void AddControl(string from, string port, string to)
                => Document.ControlEdges.Add(new GraphControlFlowEdge(from, port, to));

            private void AddValue(string fromNode, string fromPort, string toNode, string toPort)
                => Document.ValueEdges.Add(new GraphControlFlowValueEdge(fromNode, fromPort, toNode, toPort));
        }

        /// <summary>Blackboard-only runtime stub: keyed by each document's own symbol indices.</summary>
        private class ScriptedBlackboardApi : IGraphRuntimeApi
        {
            private readonly Dictionary<string, int> _keyIds = new(StringComparer.Ordinal);
            private readonly Dictionary<int, int> _values = new();

            public ScriptedBlackboardApi(string[] symbols)
            {
                for (int i = 0; i < symbols.Length; i++)
                {
                    _keyIds[symbols[i]] = i;
                }
            }

            public Dictionary<int, int> Written { get; } = new();

            public int KeyIdOf(string key) => _keyIds[key];

            public void Set(string key, int value) => _values[_keyIds[key]] = value;

            public void SpawnTemplate(int templateKeyId, Arch.Core.Entity source, float xCm, float yCm, bool hasPosition)
            {
            }

            public virtual void SetWorldPosition(Arch.Core.Entity target, int xCm, int yCm)
            {
            }

            /// <summary>Void-leaf probe: records SetWorldPosition calls on the caster.</summary>
            public sealed class RecordingMoveApi : ScriptedBlackboardApi
            {
                public RecordingMoveApi()
                    : base(Array.Empty<string>())
                {
                }

                public List<(int X, int Y)> Moves { get; } = new();

                public override void SetWorldPosition(Arch.Core.Entity target, int xCm, int yCm)
                    => Moves.Add((xCm, yCm));
            }

            public bool TryGetGridPos(Entity entity, out IntVector2 gridPos)
            {
                gridPos = default;
                return false;
            }

            public bool HasTag(Entity entity, int tagId) => false;

            public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
            {
                value = 0f;
                return false;
            }

            public SpatialQueryResult QueryRadius(IntVector2 centerCm, float radiusCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryCone(IntVector2 originCm, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryRectangle(IntVector2 centerCm, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryLine(IntVector2 originCm, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRange(IntVector2 centerCm, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRing(IntVector2 centerCm, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexNeighbors(IntVector2 centerCm, Span<Entity> buffer) => default;
            public int GetTeamId(Entity entity) => 0;
            public uint GetEntityLayerCategory(Entity entity) => 0;
            public int GetRelationship(int teamA, int teamB) => GraphRelationship.Neutral;
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId) { }
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args) { }
            public void RemoveEffectTemplate(Entity target, int templateId) { }
            public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta) { }
            public void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value) { }
            public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude) { }
            public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value) { value = 0f; return false; }

            public bool TryReadBlackboardInt(Entity entity, int keyId, out int value)
            {
                if (_values.TryGetValue(keyId, out int found))
                {
                    value = found;
                    return true;
                }

                value = 0;
                return false;
            }

            public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value) { value = default; return false; }
            public void WriteBlackboardFloat(Entity entity, int keyId, float value) { }
            public void WriteBlackboardInt(Entity entity, int keyId, int value) => Written[keyId] = value;
            public void WriteBlackboardEntity(Entity entity, int keyId, Entity value) { }
            public bool TryLoadConfigFloat(int keyId, out float value) { value = 0f; return false; }
            public bool TryLoadConfigInt(int keyId, out int value) { value = 0; return false; }
        }
    }
}
