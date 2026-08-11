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

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class GraphLookupTableOpsTests
    {
        [Test]
        public void ResolveThenRead_ReturnsTextTokenAndFloat()
        {
            var tables = CreateRankTable(tokenVeteran: 42);
            int tableId = tables.GetTableId("mod.example.rank_display");
            int tokenField = tables.GetFieldId("mod.example.rank_display", "displayToken");
            int scaleField = tables.GetFieldId("mod.example.rank_display", "powerScale");

            using World world = World.Create();
            Entity entity = world.Create();
            var api = new GasGraphRuntimeApi(world, lookupTables: tables);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 2 },
                new() { Op = (ushort)GraphNodeOp.ResolveTableRow, Dst = 1, A = 0, Imm = tableId },
                new() { Op = (ushort)GraphNodeOp.TableReadInt, Dst = 2, A = 1, Imm = tokenField },
                new() { Op = (ushort)GraphNodeOp.TableReadFloat, Dst = 0, A = 1, Imm = scaleField },
            };

            var (ints, floats) = Execute(world, api, entity, program);
            Assert.That(ints[2], Is.EqualTo(42));
            Assert.That(floats[0], Is.EqualTo(1.2f).Within(0.0001f));
        }

        [Test]
        public void ResolveTableRow_MissingKey_Throws()
        {
            var tables = CreateRankTable(tokenVeteran: 42);
            int tableId = tables.GetTableId("mod.example.rank_display");

            using World world = World.Create();
            Entity entity = world.Create();
            var api = new GasGraphRuntimeApi(world, lookupTables: tables);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 99 },
                new() { Op = (ushort)GraphNodeOp.ResolveTableRow, Dst = 1, A = 0, Imm = tableId },
            };

            Assert.That(
                () => Execute(world, api, entity, program),
                Throws.InvalidOperationException.With.Message.Contains(GraphLookupTableRegistry.RowMissingError));
        }

        [Test]
        public void TableReadInt_WrongKind_Throws()
        {
            var tables = CreateRankTable(tokenVeteran: 42);
            int tableId = tables.GetTableId("mod.example.rank_display");
            int scaleField = tables.GetFieldId("mod.example.rank_display", "powerScale");

            using World world = World.Create();
            Entity entity = world.Create();
            var api = new GasGraphRuntimeApi(world, lookupTables: tables);
            int row = tables.ResolveRow(tableId, 2);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = row },
                new() { Op = (ushort)GraphNodeOp.TableReadInt, Dst = 1, A = 0, Imm = scaleField },
            };

            Assert.That(
                () => Execute(world, api, entity, program),
                Throws.InvalidOperationException.With.Message.Contains(GraphLookupTableRegistry.FieldKindMismatchError));
        }

        [Test]
        public void UnknownTable_Throws()
        {
            var tables = CreateRankTable(tokenVeteran: 7);
            Assert.That(
                () => tables.GetTableId("missing.table"),
                Throws.InvalidOperationException.With.Message.Contains(GraphLookupTableRegistry.UnknownTableError));
        }

        [Test]
        public void CompilerAndPatch_ResolveLookupSymbols()
        {
            var tables = CreateRankTable(tokenVeteran: 11);
            var cfg = new GraphConfig
            {
                Id = "lookup.compile",
                Kind = "Effect",
                Entry = "key",
                Nodes =
                {
                    new GraphNodeConfig { Id = "key", Op = nameof(GraphNodeOp.ConstInt), IntValue = 2, Next = "row" },
                    new GraphNodeConfig
                    {
                        Id = "row",
                        Op = nameof(GraphNodeOp.ResolveTableRow),
                        LookupTable = "mod.example.rank_display",
                        Inputs = { "key" },
                        Next = "token",
                    },
                    new GraphNodeConfig
                    {
                        Id = "token",
                        Op = nameof(GraphNodeOp.TableReadInt),
                        LookupTable = "mod.example.rank_display",
                        LookupField = "displayToken",
                        Inputs = { "row" },
                        Next = "scale",
                    },
                    new GraphNodeConfig
                    {
                        Id = "scale",
                        Op = nameof(GraphNodeOp.TableReadFloat),
                        LookupTable = "mod.example.rank_display",
                        LookupField = "powerScale",
                        Inputs = { "row" },
                    },
                },
            };

            var (package, diagnostics) = GraphCompiler.Compile(cfg);
            Assert.That(diagnostics, Is.Empty);
            Assert.That(package.HasValue, Is.True);

            var resolver = new LookupOnlySymbolResolver(tables);
            GraphProgramSymbolPatcher.Patch(package!.Value.Symbols, package.Value.Program, resolver);

            using World world = World.Create();
            Entity entity = world.Create();
            var api = new GasGraphRuntimeApi(world, lookupTables: tables);
            var (ints, floats) = Execute(world, api, entity, package.Value.Program);
            int tokenReg = FindDst(package.Value.Program, GraphNodeOp.TableReadInt);
            int scaleReg = FindDst(package.Value.Program, GraphNodeOp.TableReadFloat);
            Assert.That(ints[tokenReg], Is.EqualTo(11));
            Assert.That(floats[scaleReg], Is.EqualTo(1.2f).Within(0.0001f));
        }

        [Test]
        public void ControlFlowQuery_FrontDoor_CompilesAndExecutesLookup()
        {
            var tables = CreateRankTable(tokenVeteran: 21);
            GraphControlFlowDocument doc = CreateLookupControlFlowDocument("lookup.cf.query", "Query");

            var (package, _, diagnostics) = GraphControlFlowCompiler.CompileWithOutputs(doc);
            Assert.That(diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error), Is.Empty,
                FormatDiagnostics(diagnostics));
            Assert.That(package.HasValue, Is.True);
            Assert.That(package!.Value.Kind, Is.EqualTo(GraphKind.Query));
            Assert.That(package.Value.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.ResolveTableRow));
            Assert.That(package.Value.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.TableReadInt));
            Assert.That(package.Value.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.TableReadFloat));

            var resolver = new LookupOnlySymbolResolver(tables);
            GraphProgramSymbolPatcher.Patch(package.Value.Symbols, package.Value.Program, resolver);

            using World world = World.Create();
            Entity entity = world.Create();
            var api = new GasGraphRuntimeApi(world, lookupTables: tables);
            var (ints, floats) = Execute(world, api, entity, package.Value.Program);
            int tokenReg = FindDst(package.Value.Program, GraphNodeOp.TableReadInt);
            int scaleReg = FindDst(package.Value.Program, GraphNodeOp.TableReadFloat);
            Assert.That(ints[tokenReg], Is.EqualTo(21));
            Assert.That(floats[scaleReg], Is.EqualTo(1.2f).Within(0.0001f));
        }

        [Test]
        public void ControlFlowLinear_Effect_CompilesAndExecutesLookup()
        {
            var tables = CreateRankTable(tokenVeteran: 33);
            GraphControlFlowDocument doc = CreateLookupControlFlowDocument("lookup.cf.effect", "Effect");

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            Assert.That(compiled.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error), Is.Empty,
                FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);
            Assert.That(compiled.Package!.Value.Kind, Is.EqualTo(GraphKind.Effect));

            GraphProgramPackage package = compiled.Package.Value;
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, new LookupOnlySymbolResolver(tables));

            using World world = World.Create();
            Entity entity = world.Create();
            var api = new GasGraphRuntimeApi(world, lookupTables: tables);
            var (ints, floats) = Execute(world, api, entity, package.Program);
            Assert.That(ints[FindDst(package.Program, GraphNodeOp.TableReadInt)], Is.EqualTo(33));
            Assert.That(floats[FindDst(package.Program, GraphNodeOp.TableReadFloat)], Is.EqualTo(1.2f).Within(0.0001f));
        }

        [Test]
        public void AuthoringFrontDoor_QueryJson_CompilesLookupOps()
        {
            var tables = CreateRankTable(tokenVeteran: 8);
            const string graphId = "lookup.front-door.query";
            var obj = (JsonObject)JsonNode.Parse(LookupControlFlowQueryJson(graphId))!;
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);

            var (package, _, diagnostics) = GraphProgramAuthoringFrontDoor.CompileJsonObject(obj, graphId, options);
            Assert.That(diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error), Is.Empty,
                FormatDiagnostics(diagnostics));
            Assert.That(package.HasValue, Is.True);
            Assert.That(package!.Value.Kind, Is.EqualTo(GraphKind.Query));

            GraphProgramSymbolPatcher.Patch(package.Value.Symbols, package.Value.Program, new LookupOnlySymbolResolver(tables));

            using World world = World.Create();
            Entity entity = world.Create();
            var api = new GasGraphRuntimeApi(world, lookupTables: tables);
            var (ints, floats) = Execute(world, api, entity, package.Value.Program);
            Assert.That(ints[FindDst(package.Value.Program, GraphNodeOp.TableReadInt)], Is.EqualTo(8));
            Assert.That(floats[FindDst(package.Value.Program, GraphNodeOp.TableReadFloat)], Is.EqualTo(1.2f).Within(0.0001f));
        }

        [Test]
        public void ControlFlowQuery_MissingLookupTable_FailsClosed()
        {
            GraphControlFlowDocument doc = CreateLookupControlFlowDocument("lookup.cf.missing-table", "Query");
            doc.Nodes.First(n => n.Id == "row").LookupTable = null;

            var (package, _, diagnostics) = GraphControlFlowCompiler.CompileWithOutputs(doc);
            Assert.That(package.HasValue, Is.False);
            Assert.That(diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                d.NodeId == "row" &&
                d.Message.Contains("lookupTable", StringComparison.Ordinal)));
        }

        [Test]
        public void HotPath_ResolveAndRead_ZeroAllocAfterWarmup()
        {
            var tables = CreateRankTable(tokenVeteran: 9);
            int tableId = tables.GetTableId("mod.example.rank_display");
            int tokenField = tables.GetFieldId("mod.example.rank_display", "displayToken");
            int scaleField = tables.GetFieldId("mod.example.rank_display", "powerScale");

            using World world = World.Create();
            var api = new GasGraphRuntimeApi(world, lookupTables: tables);

            // Measure registry/Api hot path only (VM Execute harness can allocate cursor/state noise).
            int sink = 0;
            float sinkF = 0f;
            for (int i = 0; i < 32; i++)
            {
                int row = api.ResolveTableRow(tableId, 2);
                sink ^= api.TableReadInt(tokenField, row);
                sinkF += api.TableReadFloat(scaleField, row);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                int row = api.ResolveTableRow(tableId, 2);
                sink ^= api.TableReadInt(tokenField, row);
                sinkF += api.TableReadFloat(scaleField, row);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(sink, Is.Not.EqualTo(int.MinValue));
            Assert.That(sinkF, Is.Not.EqualTo(float.MinValue));
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static GraphControlFlowDocument CreateLookupControlFlowDocument(string id, string kind)
        {
            return new GraphControlFlowDocument
            {
                Id = id,
                Kind = kind,
                Entry = "key",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "key", Op = nameof(GraphNodeOp.ConstInt), IntValue = 2 },
                    new()
                    {
                        Id = "row",
                        Op = nameof(GraphNodeOp.ResolveTableRow),
                        LookupTable = "mod.example.rank_display",
                    },
                    new()
                    {
                        Id = "token",
                        Op = nameof(GraphNodeOp.TableReadInt),
                        LookupTable = "mod.example.rank_display",
                        LookupField = "displayToken",
                    },
                    new()
                    {
                        Id = "scale",
                        Op = nameof(GraphNodeOp.TableReadFloat),
                        LookupTable = "mod.example.rank_display",
                        LookupField = "powerScale",
                    },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("key", GraphControlFlowPorts.Next, "row"),
                    new("row", GraphControlFlowPorts.Next, "token"),
                    new("token", GraphControlFlowPorts.Next, "scale"),
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("key", GraphControlFlowPorts.Value, "row", GraphControlFlowPorts.A),
                    new("row", GraphControlFlowPorts.Value, "token", GraphControlFlowPorts.A),
                    new("row", GraphControlFlowPorts.Value, "scale", GraphControlFlowPorts.A),
                },
                Outputs = kind == "Query"
                    ? new List<GraphOutputConfig>
                    {
                        new()
                        {
                            Id = "tokenOut",
                            Destination = nameof(GraphOutputDestinationKind.Summary),
                            Type = nameof(GraphOutputValueKind.Int),
                            Source = "token",
                            Key = "lookup.displayToken",
                        },
                        new()
                        {
                            Id = "scaleOut",
                            Destination = nameof(GraphOutputDestinationKind.Summary),
                            Type = nameof(GraphOutputValueKind.Float),
                            Source = "scale",
                            Key = "lookup.powerScale",
                        },
                    }
                    : new List<GraphOutputConfig>(),
            };
        }

        private static string LookupControlFlowQueryJson(string graphId) => $$"""
{
  "id": "{{graphId}}",
  "kind": "Query",
  "entry": "key",
  "nodes": [
    { "id": "key", "op": "ConstInt", "intValue": 2 },
    { "id": "row", "op": "ResolveTableRow", "lookupTable": "mod.example.rank_display" },
    { "id": "token", "op": "TableReadInt", "lookupTable": "mod.example.rank_display", "lookupField": "displayToken" },
    { "id": "scale", "op": "TableReadFloat", "lookupTable": "mod.example.rank_display", "lookupField": "powerScale" }
  ],
  "controlEdges": [
    { "from": "key", "fromPort": "next", "to": "row" },
    { "from": "row", "fromPort": "next", "to": "token" },
    { "from": "token", "fromPort": "next", "to": "scale" }
  ],
  "valueEdges": [
    { "from": "key", "fromPort": "value", "to": "row", "toPort": "a" },
    { "from": "row", "fromPort": "value", "to": "token", "toPort": "a" },
    { "from": "row", "fromPort": "value", "to": "scale", "toPort": "a" }
  ],
  "outputs": [
    {
      "id": "tokenOut",
      "destination": "Summary",
      "type": "Int",
      "source": "token",
      "key": "lookup.displayToken"
    },
    {
      "id": "scaleOut",
      "destination": "Summary",
      "type": "Float",
      "source": "scale",
      "key": "lookup.powerScale"
    }
  ]
}
""";

        private static string FormatDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
            => string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Code}:{d.NodeId}:{d.Message}"));

        private static GraphLookupTableRegistry CreateRankTable(int tokenVeteran)
        {
            var tables = new GraphLookupTableRegistry();
            tables.RegisterTable(
                "mod.example.rank_display",
                new (string, GraphLookupColumnKind)[]
                {
                    ("displayToken", GraphLookupColumnKind.TextToken),
                    ("powerScale", GraphLookupColumnKind.Float),
                },
                keys: new[] { 2 },
                intValues: new[] { tokenVeteran },
                floatValues: new[] { 1.2f });
            tables.Freeze();
            return tables;
        }

        private static (int[] Ints, float[] Floats) Execute(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            GraphInstruction[] program)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            e[0] = caster;
            e[1] = caster;
            var state = new GraphExecutionState
            {
                World = world,
                Api = api,
                Caster = caster,
                ExplicitTarget = caster,
                F = f,
                I = i,
                E = e,
                B = b,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = new int[GraphVmLimits.MaxCallStackDepth],
                CallStackCount = 0,
            };
            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return (state.I.ToArray(), state.F.ToArray());
        }

        private static byte FindDst(GraphInstruction[] program, GraphNodeOp op)
        {
            for (int i = 0; i < program.Length; i++)
            {
                if (program[i].Op == (ushort)op)
                {
                    return program[i].Dst;
                }
            }

            throw new InvalidOperationException($"Missing op {op}.");
        }

        private sealed class LookupOnlySymbolResolver : IGraphSymbolResolver
        {
            private readonly GraphLookupTableRegistry _tables;

            public LookupOnlySymbolResolver(GraphLookupTableRegistry tables)
            {
                _tables = tables;
            }

            public int ResolveTag(string name) => throw new NotSupportedException();
            public int ResolveAttribute(string name) => throw new NotSupportedException();
            public int ResolveEffectTemplate(string name) => throw new NotSupportedException();
            public int ResolveRelationshipType(string name) => throw new NotSupportedException();
            public int ResolveRelationshipMetric(string name) => throw new NotSupportedException();
            public int ResolveRelationshipFlag(string name) => throw new NotSupportedException();
            public int ResolveRelationshipReason(string name) => throw new NotSupportedException();
            public int ResolveTargetDispatchPreset(string name) => throw new NotSupportedException();
            public int ResolveEntityTemplate(string name) => throw new NotSupportedException();
            public int ResolveGraphLookupTable(string name) => _tables.GetTableId(name);
            public int ResolveGraphLookupField(string name) => _tables.GetFieldId(name);
        }
    }
}
