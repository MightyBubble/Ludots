using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// BT composition sugar (BtSequence/BtSelector/BtDecorator): compile-shape, fail-closed,
    /// and single-run behavior. Judge (a): the tree structure appears in the instruction
    /// stream as plain Call/Return/CompareEqInt/JumpIfFalse — sugar never becomes an opcode.
    /// </summary>
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphBehaviorTreeSugarTests
    {
        [Test]
        public void BtTree_CompilesToPlainOpcodes_NoNewOpcode()
        {
            GraphControlFlowCompileResult compiled = CompileTreeResult(CreateJudgeTreeJson());

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            GraphInstruction[] program = compiled.Package!.Value.Program;

            var ops = program.Select(i => (GraphNodeOp)i.Op).ToList();
            Assert.That(ops, Does.Contain(GraphNodeOp.Call));
            Assert.That(ops, Does.Contain(GraphNodeOp.Return));
            Assert.That(ops, Does.Contain(GraphNodeOp.CompareEqInt));
            Assert.That(ops, Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(ops, Does.Contain(GraphNodeOp.Jump));
            Assert.That(ops, Does.Contain(GraphNodeOp.HaltReturnInt));

            foreach (GraphInstruction instruction in program)
            {
                Assert.That(Enum.IsDefined(typeof(GraphNodeOp), (GraphNodeOp)instruction.Op), Is.True,
                    $"Instruction op {(GraphNodeOp)instruction.Op} is not a known GraphNodeOp; BT sugar must not mint opcodes.");
            }

            Assert.That(program.Select(i => i.Op), Has.None.EqualTo(GraphNodeOp.None));

            TestContext.Progress.WriteLine("judge-a disassembly:");
            TestContext.Progress.WriteLine(Disassemble(program));
        }

        internal static string Disassemble(GraphInstruction[] program)
        {
            var lines = new List<string>(program.Length);
            for (int i = 0; i < program.Length; i++)
            {
                GraphInstruction instruction = program[i];
                GraphNodeOp op = (GraphNodeOp)instruction.Op;
                lines.Add($"{i,3}: {op} Dst={instruction.Dst} A={instruction.A} B={instruction.B} C={instruction.C} Imm={instruction.Imm}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        [Test]
        public void BtSequence_AllChildrenSucceed_ReturnsSuccess()
        {
            GraphInstruction[] program = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "c1", "op": "ConstInt", "intValue": 1 },
                    { "id": "c2", "op": "ConstInt", "intValue": 1 }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "c1" },
                    { "from": "seq", "fromPort": "child:1", "to": "c2" }
                  ],
                  "valueEdges": []
                }
                """);

            Assert.That(RunToHalt(program).ReturnInt, Is.EqualTo(1));
        }

        [Test]
        public void BtSequence_ChildFailure_ShortCircuitsToFailure()
        {
            GraphInstruction[] program = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "c1", "op": "ConstInt", "intValue": 0 },
                    { "id": "c2", "op": "ConstInt", "intValue": 1 }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "c1" },
                    { "from": "seq", "fromPort": "child:1", "to": "c2" }
                  ],
                  "valueEdges": []
                }
                """);

            // c2 succeeding would end the tick with 1; observing 0 proves the short circuit.
            Assert.That(RunToHalt(program).ReturnInt, Is.EqualTo(0));
        }

        [Test]
        public void BtSequence_ChildRunning_EndsTickWithRunning()
        {
            GraphInstruction[] program = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "c1", "op": "ConstInt", "intValue": 1 },
                    { "id": "hold", "op": "ConstInt", "intValue": 2 }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "c1" },
                    { "from": "seq", "fromPort": "child:1", "to": "hold" }
                  ],
                  "valueEdges": []
                }
                """);

            Assert.That(RunToHalt(program).ReturnInt, Is.EqualTo(GraphBtStatusCodes.Running));
        }

        [Test]
        public void BtSelector_FirstChildSucceeds_ShortCircuits()
        {
            GraphInstruction[] program = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "sel",
                  "nodes": [
                    { "id": "sel", "op": "BtSelector" },
                    { "id": "s1", "op": "ConstInt", "intValue": 1 },
                    { "id": "s2", "op": "ConstInt", "intValue": 0 }
                  ],
                  "controlEdges": [
                    { "from": "sel", "fromPort": "child:0", "to": "s1" },
                    { "from": "sel", "fromPort": "child:1", "to": "s2" }
                  ],
                  "valueEdges": []
                }
                """);

            // s2 running after s1 would leave the tick Running(2)/Failure; 1 proves the short circuit.
            Assert.That(RunToHalt(program).ReturnInt, Is.EqualTo(1));
        }

        [Test]
        public void BtSelector_AllChildrenFail_ReturnsFailure()
        {
            GraphInstruction[] program = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "sel",
                  "nodes": [
                    { "id": "sel", "op": "BtSelector" },
                    { "id": "s1", "op": "ConstInt", "intValue": 0 },
                    { "id": "s2", "op": "ConstInt", "intValue": 0 }
                  ],
                  "controlEdges": [
                    { "from": "sel", "fromPort": "child:0", "to": "s1" },
                    { "from": "sel", "fromPort": "child:1", "to": "s2" }
                  ],
                  "valueEdges": []
                }
                """);

            Assert.That(RunToHalt(program).ReturnInt, Is.EqualTo(0));
        }

        [Test]
        public void BtLeaf_BoolTerminal_MapsTrueToSuccessFalseToFailure()
        {
            // CompareLtInt is the canonical Script bool producer: a < b → true.
            GraphInstruction[] succeed = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "a", "op": "ConstInt", "intValue": 1 },
                    { "id": "b", "op": "ConstInt", "intValue": 2 },
                    { "id": "cmp", "op": "CompareLtInt" }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "a" },
                    { "from": "seq", "fromPort": "child:1", "to": "b" },
                    { "from": "seq", "fromPort": "child:2", "to": "cmp" },
                    { "from": "a", "fromPort": "next", "to": "b" },
                    { "from": "b", "fromPort": "next", "to": "cmp" }
                  ],
                  "valueEdges": [
                    { "from": "a", "fromPort": "value", "to": "cmp", "toPort": "a" },
                    { "from": "b", "fromPort": "value", "to": "cmp", "toPort": "b" }
                  ]
                }
                """);

            GraphInstruction[] fail = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "a", "op": "ConstInt", "intValue": 2 },
                    { "id": "b", "op": "ConstInt", "intValue": 1 },
                    { "id": "cmp", "op": "CompareLtInt" }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "a" },
                    { "from": "seq", "fromPort": "child:1", "to": "b" },
                    { "from": "seq", "fromPort": "child:2", "to": "cmp" },
                    { "from": "a", "fromPort": "next", "to": "b" },
                    { "from": "b", "fromPort": "next", "to": "cmp" }
                  ],
                  "valueEdges": [
                    { "from": "a", "fromPort": "value", "to": "cmp", "toPort": "a" },
                    { "from": "b", "fromPort": "value", "to": "cmp", "toPort": "b" }
                  ]
                }
                """);

            Assert.That(RunToHalt(succeed).ReturnInt, Is.EqualTo(1));
            Assert.That(RunToHalt(fail).ReturnInt, Is.EqualTo(0));
        }

        [Test]
        public void BtDecorator_Inverter_FlipsTerminalStatus_PassesRunningThrough()
        {
            GraphInstruction[] flipFail = CompileTreeDecorator("inverter", childValue: 0);
            GraphInstruction[] flipSucc = CompileTreeDecorator("inverter", childValue: 1);
            GraphInstruction[] running = CompileTreeDecorator("inverter", childValue: 2);

            Assert.That(RunToHalt(flipFail).ReturnInt, Is.EqualTo(1));
            Assert.That(RunToHalt(flipSucc).ReturnInt, Is.EqualTo(0));
            Assert.That(RunToHalt(running).ReturnInt, Is.EqualTo(2));
        }

        [Test]
        public void BtDecorator_ForceSuccessAndForceFailure()
        {
            GraphInstruction[] forceOverFail = CompileTreeDecorator("forceSuccess", childValue: 0);
            GraphInstruction[] forceOverSucc = CompileTreeDecorator("forceFailure", childValue: 1);
            GraphInstruction[] forceKeepsRunning = CompileTreeDecorator("forceSuccess", childValue: 2);

            Assert.That(RunToHalt(forceOverFail).ReturnInt, Is.EqualTo(1));
            Assert.That(RunToHalt(forceOverSucc).ReturnInt, Is.EqualTo(0));
            Assert.That(RunToHalt(forceKeepsRunning).ReturnInt, Is.EqualTo(2));
        }

        [Test]
        public void BtDecorator_RootDecorator_HaltsWithChildStatus()
        {
            GraphInstruction[] program = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "inv",
                  "nodes": [
                    { "id": "inv", "op": "BtDecorator", "decoratorKind": "inverter" },
                    { "id": "leaf", "op": "ConstInt", "intValue": 1 }
                  ],
                  "controlEdges": [ { "from": "inv", "fromPort": "child:0", "to": "leaf" } ],
                  "valueEdges": []
                }
                """);

            Assert.That(RunToHalt(program).ReturnInt, Is.EqualTo(0));
        }

        [Test]
        public void BtYieldLeaf_SuspendsInsideLeaf_ResumesAndReturnsToParent()
        {
            GraphInstruction[] program = CompileTree("""
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "c1", "op": "ConstInt", "intValue": 1 },
                    { "id": "wait", "op": "Wait" },
                    { "id": "afterWait", "op": "ConstInt", "intValue": 1 },
                    { "id": "c3", "op": "ConstInt", "intValue": 1 }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "c1" },
                    { "from": "seq", "fromPort": "child:1", "to": "wait" },
                    { "from": "seq", "fromPort": "child:2", "to": "c3" },
                    { "from": "wait", "fromPort": "next", "to": "afterWait" }
                  ],
                  "valueEdges": []
                }
                """);

            var registry = new GraphProgramRegistry();
            registry.Register(1, program, GraphKind.Script);

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();

            GraphSliceResult first = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, program, api: null!, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);
            Assert.That(first.Yielded, Is.True, "The Wait leaf must park inside the leaf body.");
            Assert.That(cursor.CallStackCount, Is.EqualTo(1), "The parent sequence's resume address must stay parked on the call stack.");

            GraphSliceResult second = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, program, api: null!, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);
            Assert.That(second.Halted, Is.True);
            Assert.That(second.ReturnInt, Is.EqualTo(1), "After resume the leaf Returns and the sequence continues to child 3 then succeeds.");
        }

        [Test]
        public void BtSequence_NoChildren_FailsClosed()
        {
            GraphControlFlowDocument doc = CompileDocument(CreateJudgeTreeJson());
            doc.ControlEdges.RemoveAll(e => e.From == "seq");

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge &&
                d.NodeId == "seq" &&
                d.Message.Contains("child:", StringComparison.Ordinal)));
        }

        [Test]
        public void BtComposite_UnknownControlPort_FailsClosed()
        {
            GraphControlFlowDocument doc = CompileDocument(CreateJudgeTreeJson());
            doc.ControlEdges.Add(new GraphControlFlowEdge("seq", GraphControlFlowPorts.Next, "c1"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnexpectedControlEdge && d.NodeId == "seq"));
        }

        [Test]
        public void BtComposite_MalformedChildPort_FailsClosed()
        {
            GraphControlFlowDocument doc = CompileDocument(CreateJudgeTreeJson());
            doc.ControlEdges.Add(new GraphControlFlowEdge("seq", "child:abc", "c1"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnexpectedControlEdge && d.NodeId == "seq"));
        }

        [Test]
        public void BtDecorator_EmptyOrUnknownKind_FailsClosed()
        {
            GraphControlFlowDocument empty = CompileDocument(CreateJudgeTreeJson());
            empty.Nodes.First(n => n.Id == "inv").DecoratorKind = " ";
            Assert.That(GraphControlFlowCompiler.Compile(empty).Succeeded, Is.False);

            GraphControlFlowDocument unknown = CompileDocument(CreateJudgeTreeJson());
            unknown.Nodes.First(n => n.Id == "inv").DecoratorKind = "succeeder";
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(unknown);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.TypeMismatch && d.NodeId == "inv"));
        }

        [Test]
        public void BtDecorator_MissingChild_FailsClosed()
        {
            GraphControlFlowDocument doc = CompileDocument(CreateJudgeTreeJson());
            doc.ControlEdges.RemoveAll(e => e.From == "inv");

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge && d.NodeId == "inv"));
        }

        [Test]
        public void BtSugar_NonScriptKind_FailsClosed()
        {
            GraphControlFlowDocument doc = CompileDocument(CreateJudgeTreeJson());
            doc.Kind = "Effect";

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                d.Message.Contains(GraphAuthoringSugar.BtSequence, StringComparison.Ordinal)));
        }

        [Test]
        public void BtSugar_EntryNotComposite_FailsClosed()
        {
            GraphControlFlowDocument doc = CompileDocument(CreateJudgeTreeJson());
            doc.Entry = "c1";

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingEntry));
        }

        [Test]
        public void BtLeafChain_AuthoredHaltReturnInt_FailsClosed()
        {
            GraphControlFlowDocument doc = CompileDocument(CreateJudgeTreeJson());
            doc.Nodes.Add(new GraphControlFlowNode { Id = "haltLeaf", Op = nameof(GraphNodeOp.HaltReturnInt) });
            doc.ControlEdges.Add(new GraphControlFlowEdge("sel", GraphControlFlowPorts.Child(2), "haltLeaf"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnexpectedControlEdge && d.NodeId == "haltLeaf"));
        }

        [Test]
        public void BtComposite_EnteredByNextEdge_FailsClosed()
        {
            GraphControlFlowDocument doc = CompileDocument(CreateJudgeTreeJson());
            doc.ControlEdges.RemoveAll(e => e.From == "sel" && e.FromPort == GraphControlFlowPorts.Child(0));
            doc.ControlEdges.Add(new GraphControlFlowEdge("c1", GraphControlFlowPorts.Next, "sel"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnexpectedControlEdge && d.NodeId == "sel"));
        }

        [Test]
        public void BtLeafTerminal_FloatOutput_FailsClosed()
        {
            GraphControlFlowDocument doc = CompileDocument(CreateJudgeTreeJson());
            doc.Nodes.Add(new GraphControlFlowNode { Id = "fLeaf", Op = nameof(GraphNodeOp.ConstFloat), FloatValue = 1f });
            doc.ControlEdges.Add(new GraphControlFlowEdge("sel", GraphControlFlowPorts.Child(2), "fLeaf"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.TypeMismatch && d.NodeId == "fLeaf"));
        }

        [Test]
        public void BtCompositeNesting_DeepSeventeen_FailsClosed_SixteenCompiles()
        {
            Assert.That(GraphControlFlowCompiler.Compile(BuildNestedSequences(17)).Succeeded, Is.False);
            Assert.That(GraphControlFlowCompiler.Compile(BuildNestedSequences(16)).Succeeded, Is.True);
        }

        /// <summary>
        /// Judge (a) fixture: BtSequence(3 children) wrapping a BtSelector(2 children) and a
        /// BtDecorator(inverter). c1 succeeds, the selector short-circuits on its second child,
        /// the inverter flips it back to Failure — the tick must halt with 0.
        /// </summary>
        private static string CreateJudgeTreeJson()
            => """
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "c1", "op": "ConstInt", "intValue": 1 },
                    { "id": "sel", "op": "BtSelector" },
                    { "id": "s1", "op": "ConstInt", "intValue": 0 },
                    { "id": "s2", "op": "ConstInt", "intValue": 1 },
                    { "id": "inv", "op": "BtDecorator", "decoratorKind": "inverter" },
                    { "id": "d1", "op": "ConstInt", "intValue": 1 }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "c1" },
                    { "from": "seq", "fromPort": "child:1", "to": "sel" },
                    { "from": "seq", "fromPort": "child:2", "to": "inv" },
                    { "from": "sel", "fromPort": "child:0", "to": "s1" },
                    { "from": "sel", "fromPort": "child:1", "to": "s2" },
                    { "from": "inv", "fromPort": "child:0", "to": "d1" }
                  ],
                  "valueEdges": []
                }
                """;

        private static string CreateDecoratorTreeJson(string kind, int childValue)
            => $$"""
                {
                  "kind": "Script",
                  "entry": "seq",
                  "nodes": [
                    { "id": "seq", "op": "BtSequence" },
                    { "id": "inv", "op": "BtDecorator", "decoratorKind": "{{kind}}" },
                    { "id": "d1", "op": "ConstInt", "intValue": {{childValue}} }
                  ],
                  "controlEdges": [
                    { "from": "seq", "fromPort": "child:0", "to": "inv" },
                    { "from": "inv", "fromPort": "child:0", "to": "d1" }
                  ],
                  "valueEdges": []
                }
                """;

        private static GraphInstruction[] CompileTreeDecorator(string kind, int childValue)
            => CompileTree(CreateDecoratorTreeJson(kind, childValue));

        private static GraphControlFlowDocument BuildNestedSequences(int depth)
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "tests.script.bt-depth",
                Kind = "Script",
                Entry = "seq0",
                Nodes = new List<GraphControlFlowNode>(),
                ControlEdges = new List<GraphControlFlowEdge>()
            };

            for (int i = 0; i < depth; i++)
            {
                doc.Nodes.Add(new GraphControlFlowNode { Id = $"seq{i}", Op = GraphAuthoringSugar.BtSequence });
                if (i > 0)
                {
                    doc.ControlEdges.Add(new GraphControlFlowEdge($"seq{i - 1}", GraphControlFlowPorts.Child(0), $"seq{i}"));
                }
            }

            doc.Nodes.Add(new GraphControlFlowNode { Id = "leaf", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1 });
            doc.ControlEdges.Add(new GraphControlFlowEdge($"seq{depth - 1}", GraphControlFlowPorts.Child(0), "leaf"));
            return doc;
        }

        private static GraphControlFlowCompileResult CompileTreeResult(string json)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, "tests.script.bt", options);
        }

        private static GraphInstruction[] CompileTree(string json)
        {
            GraphControlFlowCompileResult compiled = CompileTreeResult(json);
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            return compiled.Package!.Value.Program;
        }

        private static GraphControlFlowDocument CompileDocument(string json)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            GraphControlFlowDocument doc = obj.Deserialize<GraphControlFlowDocument>(options)!;
            doc.Id = "tests.script.bt";
            return doc;
        }

        private static GraphSliceResult RunToHalt(GraphInstruction[] program)
        {
            var registry = new GraphProgramRegistry();
            registry.Register(1, program, GraphKind.Script);

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();

            GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, program, api: null!, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 256);
            Assert.That(result.Halted, Is.True, $"BT tree tick must halt in one slice (got {result.Status}).");
            return result;
        }
    }
}
