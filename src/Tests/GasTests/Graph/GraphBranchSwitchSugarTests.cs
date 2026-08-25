using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphBranchSwitchSugarTests
    {
        [Test]
        public void BranchBool_TruePath_ReturnsTrueArmValue()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateBranchBoolGraph(left: 1, right: 2));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Jump));

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(10));
        }

        [Test]
        public void BranchBool_FalsePath_ReturnsFalseArmValue()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateBranchBoolGraph(left: 2, right: 1));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(20));
        }

        [Test]
        public void SwitchInt_HitsMatchingCaseArm()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateSwitchIntGraph(selectorValue: 1));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.CompareEqInt));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Jump));

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(101));
        }

        [Test]
        public void SwitchInt_HitsDefaultWhenNoCaseMatches()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateSwitchIntGraph(selectorValue: 99));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(900));
        }

        [Test]
        public void SwitchInt_MissingDefault_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue: 0);
            graph.ControlEdges.RemoveAll(e =>
                e.From == "sw" && e.FromPort == GraphControlFlowPorts.Default);

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge && d.NodeId == "sw"));
        }

        [Test]
        public void SwitchInt_NoCaseArms_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue: 0);
            graph.ControlEdges.RemoveAll(e =>
                e.From == "sw" && GraphControlFlowPorts.TryParseCasePort(e.FromPort, out _));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge &&
                d.NodeId == "sw" &&
                d.Message.Contains("case:", StringComparison.Ordinal)));
        }

        [Test]
        public void SwitchInt_MalformedCasePort_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue: 0);
            graph.ControlEdges.Add(new GraphControlFlowEdge("sw", "case:not-int", "ret0"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnexpectedControlEdge && d.NodeId == "sw"));
        }

        [Test]
        public void SwitchInt_DuplicateCaseValue_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue: 0);
            // Distinct port spellings that parse to the same int value.
            graph.ControlEdges.Add(new GraphControlFlowEdge("sw", "case:01", "retDefault"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.DuplicateControlEdge &&
                d.NodeId == "sw" &&
                d.Message.Contains("case value 1", StringComparison.Ordinal)));
        }

        [Test]
        public void SwitchInt_MissingSelector_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue: 0);
            graph.ValueEdges.RemoveAll(e =>
                e.To == "sw" && e.ToPort == GraphControlFlowPorts.Selector);

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingValueInput && d.NodeId == "sw"));
        }

        [Test]
        public void QueryKind_RejectsBranchBoolAndSwitchInt()
        {
            GraphControlFlowDocument branch = CreateBranchBoolGraph(left: 1, right: 2);
            branch.Kind = "Query";
            GraphControlFlowCompileResult branchCompiled = GraphControlFlowCompiler.Compile(branch);
            Assert.That(branchCompiled.Succeeded, Is.False);
            Assert.That(branchCompiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                d.NodeId == "branch" &&
                d.Message.Contains(GraphControlFlowCompiler.BranchBoolOp, StringComparison.Ordinal)));

            GraphControlFlowDocument sw = CreateSwitchIntGraph(selectorValue: 1);
            sw.Kind = "Query";
            GraphControlFlowCompileResult switchCompiled = GraphControlFlowCompiler.Compile(sw);
            Assert.That(switchCompiled.Succeeded, Is.False);
            Assert.That(switchCompiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                d.NodeId == "sw" &&
                d.Message.Contains(GraphControlFlowCompiler.SwitchIntOp, StringComparison.Ordinal)));
        }

        [Test]
        public void Break_LowersToExplicitJumpTarget()
        {
            var graph = new GraphControlFlowDocument
            {
                Id = "tests.script.break",
                Kind = "Script",
                Entry = "value",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "value", Op = nameof(GraphNodeOp.ConstInt), IntValue = 7 },
                    new() { Id = "break", Op = GraphAuthoringSugar.Break },
                    new() { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("value", GraphControlFlowPorts.Next, "break"),
                    new("break", GraphControlFlowPorts.Target, "halt")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("value", GraphControlFlowPorts.Value, "halt", GraphControlFlowPorts.Value)
                }
            };

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Jump));
            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(7));
        }

        [Test]
        public void Break_WithoutTarget_FailsClosed()
        {
            var graph = new GraphControlFlowDocument
            {
                Id = "tests.script.break.missing-target",
                Kind = "Script",
                Entry = "break",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "break", Op = GraphAuthoringSugar.Break }
                }
            };

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge && d.NodeId == "break"));
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

        private static GraphControlFlowDocument CreateSwitchOnEnumGraph(int selectorValue, bool bindEnum)
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue);
            GraphControlFlowNode sw = graph.Nodes.First(n => n.Id == "sw");
            if (bindEnum)
            {
                sw.EnumType = "Mod.CombatState";
                graph.ControlEdges.RemoveAll(e => e.From == "sw" && e.FromPort.StartsWith("case:", StringComparison.Ordinal));
                graph.ControlEdges.AddRange(new List<GraphControlFlowEdge>
                {
                    new("sw", "case:Idle", "ret0"),
                    new("sw", "case:Combat", "ret1"),
                    new("sw", "case:Retreat", "ret2"),
                });
            }

            return graph;
        }

        [Test]
        public void SwitchOnEnum_CompilesIdenticalToHandwrittenSwitchInt()
        {
            GraphControlFlowDocument enumGraph = CreateSwitchOnEnumGraph(selectorValue: 1, bindEnum: true);
            GraphControlFlowDocument literalGraph = CreateSwitchOnEnumGraph(selectorValue: 1, bindEnum: false);
            Ludots.Core.Scripting.EnumCatalog enums = CombatStateCatalog();

            GraphControlFlowCompileResult enumCompiled = GraphControlFlowCompiler.Compile(enumGraph, null, enums);
            GraphControlFlowCompileResult literalCompiled = GraphControlFlowCompiler.Compile(literalGraph, null, enums);

            Assert.That(enumCompiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(enumCompiled.Diagnostics));
            Assert.That(literalCompiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(literalCompiled.Diagnostics));
            Assert.That(enumCompiled.Program.Length, Is.EqualTo(literalCompiled.Program.Length));
            for (int i = 0; i < enumCompiled.Program.Length; i++)
            {
                GraphInstruction a = enumCompiled.Program[i];
                GraphInstruction b = literalCompiled.Program[i];
                Assert.That(
                    (a.Op, a.Dst, a.A, a.B, a.C, a.Flags, a.Imm, a.ImmF),
                    Is.EqualTo((b.Op, b.Dst, b.A, b.B, b.C, b.Flags, b.Imm, b.ImmF)),
                    $"instruction {i} must match between case:Combat and case:1 lowering");
            }

            // Runtime behavior is the same program: selector 1 = Combat hits the case:1 arm.
            Assert.That(ExecuteHaltReturn(enumCompiled.Program), Is.EqualTo(101));

            // Metadata keeps the authored member spelling for reverse lookup (diagnostics channel).
            string[] enumSwitchPorts = Enumerable.Range(0, enumCompiled.Program.Length)
                .Where(i => enumCompiled.SourceMap.TryGetSource(i, out GraphInstructionSource src) && src.NodeId == "sw")
                .Select(i => enumCompiled.SourceMap.Sources[i].ControlPort)
                .ToArray();
            Assert.That(enumSwitchPorts, Does.Contain("case:Combat"));
            Assert.That(enumSwitchPorts, Does.Contain("case:Idle"));
            Assert.That(enumSwitchPorts, Does.Contain("case:Retreat"));
            Assert.That(enumSwitchPorts, Does.Contain(GraphControlFlowPorts.Default));
        }

        [Test]
        public void SwitchOnEnum_UnregisteredEnumType_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchOnEnumGraph(selectorValue: 1, bindEnum: true);
            graph.Nodes.First(n => n.Id == "sw").EnumType = "Mod.NoSuchEnum";

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.TypeMismatch &&
                d.NodeId == "sw" &&
                d.Message.Contains("Mod.NoSuchEnum", StringComparison.Ordinal)));
        }

        [Test]
        public void SwitchOnEnum_WithoutCatalog_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchOnEnumGraph(selectorValue: 1, bindEnum: true);

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, null);
            Assert.That(compiled.Succeeded, Is.False, "authored enumType with no catalog in scope must fail closed");
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.NodeId == "sw" && d.Message.Contains("not registered", StringComparison.Ordinal)));
        }

        [Test]
        public void SwitchOnEnum_CaseNameNotAMember_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchOnEnumGraph(selectorValue: 1, bindEnum: true);
            GraphControlFlowEdge bad = graph.ControlEdges.First(e => e.From == "sw" && e.FromPort == "case:Retreat");
            bad.FromPort = "case:Flank";

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.NodeId == "sw" &&
                d.Message.Contains("'Flank'", StringComparison.Ordinal) &&
                d.Message.Contains("Mod.CombatState", StringComparison.Ordinal)));
        }

        [Test]
        public void SwitchOnEnum_RawIntCasePort_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchOnEnumGraph(selectorValue: 1, bindEnum: true);
            GraphControlFlowEdge bad = graph.ControlEdges.First(e => e.From == "sw" && e.FromPort == "case:Retreat");
            bad.FromPort = "case:2";

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.NodeId == "sw" &&
                d.Message.Contains("must name members", StringComparison.Ordinal)));
        }

        private static GraphControlFlowDocument CreateSelectByEnumGraph(int selectorValue, bool withDefault)
        {
            var graph = new GraphControlFlowDocument
            {
                Id = "tests.script.select-by-enum",
                Kind = "Script",
                Entry = "sel",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "sel", Op = nameof(GraphNodeOp.ConstInt), IntValue = selectorValue },
                    new() { Id = "redValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = 100 },
                    new() { Id = "blueValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = 200 },
                    new() { Id = "defValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = 7 },
                    new() { Id = "pick", Op = GraphAuthoringSugar.SelectByEnum, EnumType = "Mod.CombatState" },
                    new() { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("sel", GraphControlFlowPorts.Next, "redValue"),
                    new("redValue", GraphControlFlowPorts.Next, "blueValue"),
                    new("blueValue", GraphControlFlowPorts.Next, "defValue"),
                    new("defValue", GraphControlFlowPorts.Next, "pick"),
                    new("pick", GraphControlFlowPorts.Next, "halt")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("sel", GraphControlFlowPorts.Value, "pick", GraphControlFlowPorts.Selector),
                    new("redValue", GraphControlFlowPorts.Value, "pick", "case:Idle"),
                    new("blueValue", GraphControlFlowPorts.Value, "pick", "case:Combat"),
                    new("pick", GraphControlFlowPorts.Value, "halt", GraphControlFlowPorts.Value)
                }
            };

            if (withDefault)
            {
                graph.ValueEdges.Add(new GraphControlFlowValueEdge("defValue", GraphControlFlowPorts.Value, "pick", GraphControlFlowPorts.Default));
            }

            return graph;
        }

        [Test]
        public void SelectByEnum_MatchingMember_PicksCandidateValue()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateSelectByEnumGraph(selectorValue: 1, withDefault: true), null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.MoveInt));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Not.Contains(GraphNodeOp.SelectEntity),
                "no SelectInt executor exists; the chain is ConstInt/CompareEqInt/JumpIfFalse/MoveInt/Jump only");

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(200), "team=Combat picks the case:Combat candidate");
        }

        [Test]
        public void SelectByEnum_NoCandidateMatches_FallsToDefault()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateSelectByEnumGraph(selectorValue: 2, withDefault: true), null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(7), "selector 2 (Retreat, unbound) falls to default");
        }

        [Test]
        public void SelectByEnum_WithoutEnumType_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSelectByEnumGraph(selectorValue: 1, withDefault: true);
            graph.Nodes.First(n => n.Id == "pick").EnumType = null;

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.NodeId == "pick" && d.Message.Contains("enumType", StringComparison.Ordinal)));
        }

        [Test]
        public void SelectByEnum_QueryKind_RejectsSugar()
        {
            GraphControlFlowDocument graph = CreateSelectByEnumGraph(selectorValue: 1, withDefault: true);
            graph.Kind = "Query";

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph, null, CombatStateCatalog());
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                d.NodeId == "pick" &&
                d.Message.Contains(GraphAuthoringSugar.SelectByEnum, StringComparison.Ordinal)));
        }

        private static GraphControlFlowDocument CreateBranchBoolGraph(int left, int right)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.branch-bool-paths",
                Entry = "left",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "left", Op = nameof(GraphNodeOp.ConstInt), IntValue = left },
                    new() { Id = "right", Op = nameof(GraphNodeOp.ConstInt), IntValue = right },
                    new() { Id = "trueValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = 10 },
                    new() { Id = "falseValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = 20 },
                    new() { Id = "pred", Op = nameof(GraphNodeOp.CompareLtInt) },
                    new() { Id = "branch", Op = GraphControlFlowCompiler.BranchBoolOp },
                    new() { Id = "retTrue", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "retFalse", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("left", GraphControlFlowPorts.Next, "right"),
                    new("right", GraphControlFlowPorts.Next, "trueValue"),
                    new("trueValue", GraphControlFlowPorts.Next, "falseValue"),
                    new("falseValue", GraphControlFlowPorts.Next, "pred"),
                    new("pred", GraphControlFlowPorts.Next, "branch"),
                    new("branch", GraphControlFlowPorts.True, "retTrue"),
                    new("branch", GraphControlFlowPorts.False, "retFalse")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("left", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.A),
                    new("right", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.B),
                    new("pred", GraphControlFlowPorts.Value, "branch", GraphControlFlowPorts.Condition),
                    new("trueValue", GraphControlFlowPorts.Value, "retTrue", GraphControlFlowPorts.Value),
                    new("falseValue", GraphControlFlowPorts.Value, "retFalse", GraphControlFlowPorts.Value)
                }
            };
        }

        private static GraphControlFlowDocument CreateSwitchIntGraph(int selectorValue)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.switch-int-arms",
                Entry = "retV0",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "retV0", Op = nameof(GraphNodeOp.ConstInt), IntValue = 100 },
                    new() { Id = "retV1", Op = nameof(GraphNodeOp.ConstInt), IntValue = 101 },
                    new() { Id = "retV2", Op = nameof(GraphNodeOp.ConstInt), IntValue = 102 },
                    new() { Id = "retVD", Op = nameof(GraphNodeOp.ConstInt), IntValue = 900 },
                    new() { Id = "sel", Op = nameof(GraphNodeOp.ConstInt), IntValue = selectorValue },
                    new() { Id = "sw", Op = GraphControlFlowCompiler.SwitchIntOp },
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
                    new("retVD", GraphControlFlowPorts.Next, "sel"),
                    new("sel", GraphControlFlowPorts.Next, "sw"),
                    new("sw", GraphControlFlowPorts.Case(0), "ret0"),
                    new("sw", GraphControlFlowPorts.Case(1), "ret1"),
                    new("sw", GraphControlFlowPorts.Case(2), "ret2"),
                    new("sw", GraphControlFlowPorts.Default, "retDefault")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("sel", GraphControlFlowPorts.Value, "sw", GraphControlFlowPorts.Selector),
                    new("retV0", GraphControlFlowPorts.Value, "ret0", GraphControlFlowPorts.Value),
                    new("retV1", GraphControlFlowPorts.Value, "ret1", GraphControlFlowPorts.Value),
                    new("retV2", GraphControlFlowPorts.Value, "ret2", GraphControlFlowPorts.Value),
                    new("retVD", GraphControlFlowPorts.Value, "retDefault", GraphControlFlowPorts.Value)
                }
            };
        }

        private static int ExecuteHaltReturn(GraphInstruction[] program)
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
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);

            Assert.That(result.Halted, Is.True);
            return result.ReturnInt;
        }
    }
}
