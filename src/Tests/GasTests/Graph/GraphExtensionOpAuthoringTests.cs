using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphExtensionOpAuthoringTests
    {
        private const string ProviderKey = "ProviderMod.QueryThreat";

        [Test]
        public void FrontDoor_ScoreGraph_CompilesRegisteredExtensionOp()
        {
            GasGraphOpRegistry registry = CreateThreatRegistry(out int opCode);
            GraphControlFlowCompileResult compiled = CompileScoreThreat(registry);

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package, Is.Not.Null);
            Assert.That(compiled.Program.Any(i => i.Op == checked((ushort)opCode)), Is.True);
            Assert.That(opCode, Is.GreaterThanOrEqualTo(GasGraphOpRegistry.FirstModOpCode));
        }

        [Test]
        public void FrontDoor_UnknownExtensionKey_FailsClosed()
        {
            GraphControlFlowCompileResult compiled = CompileScoreThreat(opRegistry: null);

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics.Any(d => d.Code == GraphDiagnosticCodes.UnknownNodeOp), Is.True);
        }

        [Test]
        public void FrontDoor_QueryKind_RejectsExtensionOp()
        {
            GasGraphOpRegistry registry = CreateThreatRegistry(out _);
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Query",
                  "entry": "threat",
                  "nodes": [
                    { "id": "threat", "op": "ProviderMod.QueryThreat" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.graph.extension.query",
                registry);

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(
                compiled.Diagnostics.Any(d =>
                    d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                    d.Message.Contains("Query", StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void FrontDoor_MissingValueInput_FailsClosed()
        {
            GasGraphOpRegistry registry = CreateThreatRegistry(out _);
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Score",
                  "entry": "threat",
                  "nodes": [
                    { "id": "threat", "op": "ProviderMod.QueryThreat" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.graph.extension.missing-input",
                registry);

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics.Any(d => d.Code == GraphDiagnosticCodes.MissingValueInput), Is.True);
        }

        [Test]
        public void ExecuteScore_RunsInstalledExtensionHandler()
        {
            var hub = new ModExtensionHub();
            var provider = new ModContext(
                "ProviderMod",
                new VirtualFileSystem(),
                new FunctionRegistry(),
                new TriggerManager(),
                new Ludots.Core.Engine.SystemFactoryRegistry(),
                new TriggerDecoratorRegistry(),
                hub);
            provider.Extensions.Gas.RegisterGraphOp(
                ProviderKey,
                GraphValueType.Float,
                fixedRegister: 0,
                WriteThreatScore,
                GraphValueType.Entity);
            var handlers = new GasGraphOpHandlerTable(hub.Gas.GraphOps);

            GraphControlFlowCompileResult compiled = CompileScoreThreat(hub.Gas.GraphOps);
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Score,
                    compiled.Package!.Value.Program,
                    handlers));

            var programs = new GraphProgramRegistry(handlers);
            Assert.DoesNotThrow(() =>
                programs.Register(1, compiled.Package!.Value.Program, GraphKind.Score));

            using var world = World.Create();
            Entity caster = world.Create();
            Entity target = world.Create();
            var api = new GasGraphRuntimeApi(world);
            float score = GraphExecutor.ExecuteScore(
                world,
                caster,
                target,
                default,
                compiled.Package!.Value.Program,
                api,
                GraphKind.Score,
                handlers);
            Assert.That(score, Is.EqualTo(7.5f).Within(0.001f));
        }

        [Test]
        public void KindPolicy_ExtensionOpWithoutMetadata_FailsClosed()
        {
            var program = new[]
            {
                new GraphInstruction { Op = GasGraphOpRegistry.FirstModOpCode, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Score,
                    program,
                    GasGraphOpHandlerTable.Instance))!;
            Assert.That(ex.Message, Does.Contain(GraphKindOperationPolicy.MissingOperationMetadataError));
        }

        private static GasGraphOpRegistry CreateThreatRegistry(out int opCode)
        {
            var hub = new ModExtensionHub();
            var provider = new ModContext(
                "ProviderMod",
                new VirtualFileSystem(),
                new FunctionRegistry(),
                new TriggerManager(),
                new Ludots.Core.Engine.SystemFactoryRegistry(),
                new TriggerDecoratorRegistry(),
                hub);
            opCode = provider.Extensions.Gas.RegisterGraphOp(
                ProviderKey,
                GraphValueType.Float,
                fixedRegister: 0,
                WriteThreatScore,
                GraphValueType.Entity);
            return hub.Gas.GraphOps;
        }

        private static void WriteThreatScore(ref GraphExecutionState state, in GraphInstruction ins, ref int pc)
        {
            Entity target = state.E[ins.A];
            if (!state.World.IsAlive(target))
            {
                throw new InvalidOperationException("ProviderMod.QueryThreat requires a live target entity.");
            }

            state.F[ins.Dst] = 7.5f;
        }

        private static GraphControlFlowCompileResult CompileScoreThreat(GasGraphOpRegistry? opRegistry)
        {
            return CompileFrontDoor(
                """
                {
                  "kind": "Score",
                  "entry": "target",
                  "nodes": [
                    { "id": "target", "op": "LoadExplicitTarget" },
                    { "id": "threat", "op": "ProviderMod.QueryThreat" }
                  ],
                  "controlEdges": [
                    { "from": "target", "fromPort": "next", "to": "threat" }
                  ],
                  "valueEdges": [
                    { "from": "target", "fromPort": "value", "to": "threat", "toPort": "a" }
                  ]
                }
                """,
                "tests.graph.extension.score-threat",
                opRegistry);
        }

        private static GraphControlFlowCompileResult CompileFrontDoor(
            string json,
            string graphId,
            GasGraphOpRegistry? opRegistry)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(
                obj,
                graphId,
                options,
                eventSchemas: null,
                enums: null,
                opRegistry);
        }
    }
}
