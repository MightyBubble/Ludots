using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphRegisterDescriptorTests
    {
        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
            TagRegistry.Clear();
        }

        [Test]
        public void 目标列表读取与吸附的有效位不得落在同一格子()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "caster",
                  "nodes": [
                    { "id": "caster", "op": "LoadCaster" },
                    { "id": "idx", "op": "ConstInt", "intValue": 0 },
                    { "id": "get", "op": "TargetListGet" },
                    { "id": "dist", "op": "ConstFloat", "floatValue": 100 },
                    { "id": "snap", "op": "SnapToNearestInCollection", "collectionKey": "tests.collection.snap", "validOutput": "snapValid" }
                  ],
                  "controlEdges": [
                    { "from": "caster", "fromPort": "next", "to": "idx" },
                    { "from": "idx", "fromPort": "next", "to": "get" },
                    { "from": "get", "fromPort": "next", "to": "dist" },
                    { "from": "dist", "fromPort": "next", "to": "snap" }
                  ],
                  "valueEdges": [
                    { "from": "idx", "fromPort": "value", "to": "get", "toPort": "value" },
                    { "from": "caster", "fromPort": "value", "to": "snap", "toPort": "source" },
                    { "from": "dist", "fromPort": "value", "to": "snap", "toPort": "value" }
                  ]
                }
                """,
                "tests.s12.two-scratches");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            GraphInstruction get = compiled.Program.Single(i => i.Op == (ushort)GraphNodeOp.TargetListGet);
            GraphInstruction snap = compiled.Program.Single(i => i.Op == (ushort)GraphNodeOp.SnapToNearestInCollection);
            Assert.That(get.Flags, Is.Not.EqualTo(byte.MaxValue));
            Assert.That(snap.Flags, Is.Not.EqualTo(byte.MaxValue));
            Assert.That(get.Flags, Is.Not.EqualTo(snap.Flags),
                "TargetListGet and SnapToNearestInCollection valid bits must come from AllocScratch and must not alias.");
        }

        [Test]
        public void PinRegister与已分配格子冲突时编译失败()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "first",
                  "nodes": [
                    { "id": "first", "op": "ConstInt", "intValue": 1 },
                    { "id": "pinned", "op": "ConstInt", "intValue": 2, "pinRegister": 0 }
                  ],
                  "controlEdges": [
                    { "from": "first", "fromPort": "next", "to": "pinned" }
                  ],
                  "valueEdges": []
                }
                """,
                "tests.s12.pin-conflict");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.RegisterAliasConflict &&
                d.NodeId == "pinned" &&
                d.Message.Contains("conflicts", StringComparison.Ordinal)));
        }

        [Test]
        public void 前门允许的节点必须是策略允许的子集()
        {
            GasGraphOpHandlerTable handlers = GasGraphOpHandlerTable.Instance;
            var failures = new List<string>();
            GraphKind[] kinds =
            {
                GraphKind.Effect, GraphKind.Query, GraphKind.Score,
                GraphKind.Validation, GraphKind.Derived, GraphKind.Script
            };

            foreach (GraphKind kind in kinds)
            {
                foreach (GraphNodeOp op in GraphOpDescriptorTable.EnumerateAuthorable(kind))
                {
                    Assert.That(handlers.TryGetOperationMetadata(op, out EffectOperationMetadata metadata), Is.True, op.ToString());
                    if (!GraphOpDescriptorTable.IsPolicyAllowed(kind, op, in metadata))
                    {
                        failures.Add($"{kind} 前门允许 {op}，但策略拒绝。");
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void 符号补丁跑两次结果相同()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "caster",
                  "nodes": [
                    { "id": "caster", "op": "LoadCaster" },
                    { "id": "has", "op": "HasTag", "tag": "State.Moving" }
                  ],
                  "controlEdges": [
                    { "from": "caster", "fromPort": "next", "to": "has" }
                  ],
                  "valueEdges": [
                    { "from": "caster", "fromPort": "value", "to": "has", "toPort": "source" }
                  ]
                }
                """,
                "tests.s12.patch-idempotent");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            GraphInstruction[] program = compiled.Package!.Value.Program;
            string[] symbols = compiled.Package.Value.Symbols;
            int movingTagId = TagRegistry.Register("State.Moving");
            var resolver = new GasGraphSymbolResolver(
                new RelationshipTypeRegistry(),
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipReasonRegistry(),
                new TargetDispatchPresetRegistry(),
                new EntityTemplateKeyRegistry());

            GraphProgramSymbolPatcher.Patch(symbols, program, resolver);
            GraphInstruction[] first = Clone(program);
            GraphProgramSymbolPatcher.Patch(symbols, program, resolver);

            Assert.That(program.Select(InstructionKey), Is.EqualTo(first.Select(InstructionKey)));
            GraphInstruction has = program.Single(i => i.Op == (ushort)GraphNodeOp.HasTag);
            Assert.That(has.Imm, Is.EqualTo(movingTagId));
        }

        private static GraphControlFlowCompileResult CompileFrontDoor(string json, string graphId)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        }

        private static GraphInstruction[] Clone(GraphInstruction[] program)
        {
            var copy = new GraphInstruction[program.Length];
            Array.Copy(program, copy, program.Length);
            return copy;
        }

        private static string InstructionKey(GraphInstruction ins)
            => $"{ins.Op}:{ins.Dst}:{ins.A}:{ins.B}:{ins.C}:{ins.Flags}:{ins.Imm}:{ins.ImmF}";
    }
}
