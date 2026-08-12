using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphEffectAuthoringExpressivenessTests
    {
        [Test]
        public void FrontDoor_EffectBranchBool_CompilesToJumps()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                BranchBoolGraphJson("Effect"),
                "tests.effect.branch-bool");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Jump));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Not.Contain(GraphNodeOp.Yield));
            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Effect,
                    compiled.Package!.Value.Program,
                    GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void FrontDoor_EffectInvokeScriptFunctionName_CompilesAndPatchesViaFuncLib()
        {
            const int calleeId = 1234;
            var catalog = new GraphFunctionCatalog();
            catalog.Register("demo.seven", calleeId, GraphKind.Script);

            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "invoke",
                  "nodes": [
                    { "id": "invoke", "op": "InvokeScript", "functionName": "demo.seven" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.effect.invoke-func-lib");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);

            GraphInstruction[] program = compiled.Package!.Value.Program;
            GraphProgramSymbolPatcher.PatchFuncLib(compiled.Package.Value.Symbols, program, catalog);

            GraphInstruction invoke = program.Single(i => i.Op == (ushort)GraphNodeOp.InvokeScript);
            Assert.That(invoke.Imm, Is.EqualTo(calleeId));
            Assert.That(invoke.Flags & GraphInstructionFlags.FuncLibName, Is.EqualTo(0));
            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Effect,
                    program,
                    GasGraphOpHandlerTable.Instance));
        }

        [TestCase("Score")]
        [TestCase("Validation")]
        [TestCase("Derived")]
        public void FrontDoor_LinearKindsInvokeScriptFunctionName_Compile(string kind)
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                $$"""
                {
                  "kind": "{{kind}}",
                  "entry": "invoke",
                  "nodes": [
                    { "id": "invoke", "op": "InvokeScript", "functionName": "demo.score" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                $"tests.{kind.ToLowerInvariant()}.invoke-func-lib");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.InvokeScript));
            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    ParseKind(kind),
                    compiled.Package!.Value.Program,
                    GasGraphOpHandlerTable.Instance));
        }

        [TestCase("Score")]
        [TestCase("Validation")]
        [TestCase("Derived")]
        public void FrontDoor_NonEffectLinearKindsRejectBranchBool(string kind)
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                BranchBoolGraphJson(kind),
                $"tests.{kind.ToLowerInvariant()}.branch-bool-forbidden");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                d.NodeId == "branch" &&
                d.Message.Contains(GraphControlFlowCompiler.BranchBoolOp, StringComparison.Ordinal)));
        }

        [TestCase("Wait")]
        [TestCase("Yield")]
        [TestCase("While")]
        [TestCase("Until")]
        [TestCase("SwitchInt")]
        public void FrontDoor_EffectWaitYieldAndLoopSugar_FailClosed(string op)
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                $$"""
                {
                  "kind": "Effect",
                  "entry": "bad",
                  "nodes": [
                    { "id": "bad", "op": "{{op}}" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                $"tests.effect.{op.ToLowerInvariant()}-forbidden");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                d.NodeId == "bad"));
        }

        private static GraphControlFlowCompileResult CompileFrontDoor(string json, string graphId)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        }

        private static string BranchBoolGraphJson(string kind)
            => $$"""
               {
                 "kind": "{{kind}}",
                 "entry": "left",
                 "nodes": [
                   { "id": "left", "op": "ConstInt", "intValue": 1 },
                   { "id": "right", "op": "ConstInt", "intValue": 2 },
                   { "id": "pred", "op": "CompareLtInt" },
                   { "id": "branch", "op": "BranchBool" },
                   { "id": "trueTarget", "op": "LoadCaster" },
                   { "id": "falseTarget", "op": "LoadExplicitTarget" }
                 ],
                 "controlEdges": [
                   { "from": "left", "fromPort": "next", "to": "right" },
                   { "from": "right", "fromPort": "next", "to": "pred" },
                   { "from": "pred", "fromPort": "next", "to": "branch" },
                   { "from": "branch", "fromPort": "true", "to": "trueTarget" },
                   { "from": "branch", "fromPort": "false", "to": "falseTarget" }
                 ],
                 "valueEdges": [
                   { "from": "left", "fromPort": "value", "to": "pred", "toPort": "a" },
                   { "from": "right", "fromPort": "value", "to": "pred", "toPort": "b" },
                   { "from": "pred", "fromPort": "value", "to": "branch", "toPort": "condition" }
                 ]
               }
               """;

        private static GraphKind ParseKind(string kind)
        {
            Assert.That(GraphKindParser.TryParse(kind, out GraphKind parsed), Is.True);
            return parsed;
        }
    }
}
