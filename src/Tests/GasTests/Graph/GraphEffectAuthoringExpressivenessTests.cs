using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.TagDisplay;
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

        [Test]
        public void FrontDoor_EffectFloatAndBoolRuntimeOps_CompileAndEmit()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "self",
                  "nodes": [
                    { "id": "self", "op": "LoadCaster" },
                    { "id": "target", "op": "LoadExplicitTarget" },
                    { "id": "boolTrue", "op": "ConstBool", "boolValue": true },
                    { "id": "selectBool", "op": "SelectEntity" },
                    { "id": "ten", "op": "ConstFloat", "floatValue": 10 },
                    { "id": "two", "op": "ConstFloat", "floatValue": 2 },
                    { "id": "div", "op": "DivFloat" },
                    { "id": "three", "op": "ConstFloat", "floatValue": 3 },
                    { "id": "min", "op": "MinFloat" },
                    { "id": "max", "op": "MaxFloat" },
                    { "id": "zero", "op": "ConstFloat", "floatValue": 0 },
                    { "id": "five", "op": "ConstFloat", "floatValue": 5 },
                    { "id": "clamp", "op": "ClampFloat" },
                    { "id": "neg", "op": "NegFloat" },
                    { "id": "abs", "op": "AbsFloat" },
                    { "id": "gt", "op": "CompareGtFloat" },
                    { "id": "selectGt", "op": "SelectEntity" }
                  ],
                  "controlEdges": [
                    { "from": "self", "fromPort": "next", "to": "target" },
                    { "from": "target", "fromPort": "next", "to": "boolTrue" },
                    { "from": "boolTrue", "fromPort": "next", "to": "selectBool" },
                    { "from": "selectBool", "fromPort": "next", "to": "ten" },
                    { "from": "ten", "fromPort": "next", "to": "two" },
                    { "from": "two", "fromPort": "next", "to": "div" },
                    { "from": "div", "fromPort": "next", "to": "three" },
                    { "from": "three", "fromPort": "next", "to": "min" },
                    { "from": "min", "fromPort": "next", "to": "max" },
                    { "from": "max", "fromPort": "next", "to": "zero" },
                    { "from": "zero", "fromPort": "next", "to": "five" },
                    { "from": "five", "fromPort": "next", "to": "clamp" },
                    { "from": "clamp", "fromPort": "next", "to": "neg" },
                    { "from": "neg", "fromPort": "next", "to": "abs" },
                    { "from": "abs", "fromPort": "next", "to": "gt" },
                    { "from": "gt", "fromPort": "next", "to": "selectGt" }
                  ],
                  "valueEdges": [
                    { "from": "boolTrue", "fromPort": "value", "to": "selectBool", "toPort": "condition" },
                    { "from": "target", "fromPort": "value", "to": "selectBool", "toPort": "a" },
                    { "from": "self", "fromPort": "value", "to": "selectBool", "toPort": "b" },
                    { "from": "ten", "fromPort": "value", "to": "div", "toPort": "a" },
                    { "from": "two", "fromPort": "value", "to": "div", "toPort": "b" },
                    { "from": "div", "fromPort": "value", "to": "min", "toPort": "a" },
                    { "from": "three", "fromPort": "value", "to": "min", "toPort": "b" },
                    { "from": "min", "fromPort": "value", "to": "max", "toPort": "a" },
                    { "from": "two", "fromPort": "value", "to": "max", "toPort": "b" },
                    { "from": "max", "fromPort": "value", "to": "clamp", "toPort": "value" },
                    { "from": "zero", "fromPort": "value", "to": "clamp", "toPort": "min" },
                    { "from": "five", "fromPort": "value", "to": "clamp", "toPort": "max" },
                    { "from": "clamp", "fromPort": "value", "to": "neg", "toPort": "value" },
                    { "from": "neg", "fromPort": "value", "to": "abs", "toPort": "value" },
                    { "from": "abs", "fromPort": "value", "to": "gt", "toPort": "a" },
                    { "from": "two", "fromPort": "value", "to": "gt", "toPort": "b" },
                    { "from": "gt", "fromPort": "value", "to": "selectGt", "toPort": "condition" },
                    { "from": "target", "fromPort": "value", "to": "selectGt", "toPort": "a" },
                    { "from": "self", "fromPort": "value", "to": "selectGt", "toPort": "b" }
                  ]
                }
                """,
                "tests.effect.float-bool-runtime-ops");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);

            GraphInstruction[] program = compiled.Package!.Value.Program;
            Assert.Multiple(() =>
            {
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.ConstBool));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.DivFloat));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.MinFloat));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.MaxFloat));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.ClampFloat));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.NegFloat));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.AbsFloat));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.CompareGtFloat));
            });

            GraphInstruction constBool = program.Single(i => i.Op == (ushort)GraphNodeOp.ConstBool);
            Assert.That(constBool.Imm, Is.EqualTo(1));

            GraphInstruction clamp = program.Single(i => i.Op == (ushort)GraphNodeOp.ClampFloat);
            Assert.Multiple(() =>
            {
                Assert.That(clamp.A, Is.EqualTo(5));
                Assert.That(clamp.B, Is.EqualTo(6));
                Assert.That(clamp.C, Is.EqualTo(7));
            });

            GraphInstruction neg = program.Single(i => i.Op == (ushort)GraphNodeOp.NegFloat);
            GraphInstruction abs = program.Single(i => i.Op == (ushort)GraphNodeOp.AbsFloat);
            GraphInstruction compare = program.Single(i => i.Op == (ushort)GraphNodeOp.CompareGtFloat);
            Assert.Multiple(() =>
            {
                Assert.That(neg.A, Is.EqualTo(clamp.Dst));
                Assert.That(abs.A, Is.EqualTo(neg.Dst));
                Assert.That(compare.A, Is.EqualTo(abs.Dst));
                Assert.That(compare.B, Is.EqualTo(1));
            });

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Effect,
                    program,
                    GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void FrontDoor_EffectDynamicAndFanOutEffectOps_CompileAndEmit()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "target",
                  "nodes": [
                    { "id": "target", "op": "LoadExplicitTarget" },
                    { "id": "templateId", "op": "ConstInt", "intValue": 77 },
                    { "id": "fanStatic", "op": "FanOutApplyEffect", "effectTemplate": "effect.fan.static" },
                    { "id": "applyDynamic", "op": "ApplyEffectDynamic" },
                    { "id": "fanDynamic", "op": "FanOutApplyEffectDynamic" },
                    { "id": "dispatchStatic", "op": "FanOutDispatchEffect", "effectTemplate": "effect.dispatch.static", "payloadPreset": "TargetToResolved" },
                    { "id": "dispatchDynamic", "op": "FanOutDispatchEffectDynamic", "payloadPreset": "TargetToResolved" }
                  ],
                  "controlEdges": [
                    { "from": "target", "fromPort": "next", "to": "templateId" },
                    { "from": "templateId", "fromPort": "next", "to": "fanStatic" },
                    { "from": "fanStatic", "fromPort": "next", "to": "applyDynamic" },
                    { "from": "applyDynamic", "fromPort": "next", "to": "fanDynamic" },
                    { "from": "fanDynamic", "fromPort": "next", "to": "dispatchStatic" },
                    { "from": "dispatchStatic", "fromPort": "next", "to": "dispatchDynamic" }
                  ],
                  "valueEdges": [
                    { "from": "target", "fromPort": "value", "to": "applyDynamic", "toPort": "target" },
                    { "from": "templateId", "fromPort": "value", "to": "applyDynamic", "toPort": "value" },
                    { "from": "templateId", "fromPort": "value", "to": "fanDynamic", "toPort": "value" },
                    { "from": "templateId", "fromPort": "value", "to": "dispatchDynamic", "toPort": "value" }
                  ]
                }
                """,
                "tests.effect.dynamic-fanout-effect-ops");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);

            GraphProgramPackage package = compiled.Package!.Value;
            GraphInstruction[] program = package.Program;
            GraphInstruction target = SingleInstruction(program, GraphNodeOp.LoadExplicitTarget);
            GraphInstruction templateId = SingleInstruction(program, GraphNodeOp.ConstInt);
            GraphInstruction fanStatic = SingleInstruction(program, GraphNodeOp.FanOutApplyEffect);
            GraphInstruction applyDynamic = SingleInstruction(program, GraphNodeOp.ApplyEffectDynamic);
            GraphInstruction fanDynamic = SingleInstruction(program, GraphNodeOp.FanOutApplyEffectDynamic);
            GraphInstruction dispatchStatic = SingleInstruction(program, GraphNodeOp.FanOutDispatchEffect);
            GraphInstruction dispatchDynamic = SingleInstruction(program, GraphNodeOp.FanOutDispatchEffectDynamic);

            Assert.Multiple(() =>
            {
                Assert.That(package.Symbols[fanStatic.Imm], Is.EqualTo("effect.fan.static"));
                Assert.That(applyDynamic.A, Is.EqualTo(0));
                Assert.That(applyDynamic.B, Is.EqualTo(0));
                Assert.That(fanDynamic.A, Is.EqualTo(0));
                Assert.That(package.Symbols[dispatchStatic.Imm], Is.EqualTo("effect.dispatch.static"));
                Assert.That(package.Symbols[dispatchStatic.Dst], Is.EqualTo("TargetToResolved"));
                Assert.That(dispatchDynamic.A, Is.EqualTo(0));
                Assert.That(applyDynamic.A, Is.EqualTo(target.Dst));
                Assert.That(applyDynamic.B, Is.EqualTo(templateId.Dst));
                Assert.That(fanDynamic.A, Is.EqualTo(templateId.Dst));
                Assert.That(dispatchDynamic.A, Is.EqualTo(templateId.Dst));
                Assert.That(package.Symbols[dispatchDynamic.Dst], Is.EqualTo("TargetToResolved"));
            });

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Effect,
                    program,
                    GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void FrontDoor_EffectApplyEffectDynamic_RequiresTargetAndValueInputs()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "apply",
                  "nodes": [
                    { "id": "apply", "op": "ApplyEffectDynamic" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.effect.apply-effect-dynamic-missing-inputs");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                    d.Code == GraphDiagnosticCodes.MissingValueInput &&
                    d.NodeId == "apply" &&
                    d.Message.Contains("'target'", StringComparison.Ordinal)));
                Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                    d.Code == GraphDiagnosticCodes.MissingValueInput &&
                    d.NodeId == "apply" &&
                    d.Message.Contains("'value'", StringComparison.Ordinal)));
            });
        }

        [Test]
        public void FrontDoor_EffectFanOutApplyEffect_RequiresEffectTemplate()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "fan",
                  "nodes": [
                    { "id": "fan", "op": "FanOutApplyEffect" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.effect.fanout-apply-effect-missing-template");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                d.NodeId == "fan" &&
                d.Message.Contains("effectTemplate", StringComparison.Ordinal)));
        }

        [Test]
        public void FrontDoor_EffectFanOutApplyEffectDynamic_RequiresValueInput()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "fan",
                  "nodes": [
                    { "id": "fan", "op": "FanOutApplyEffectDynamic" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.effect.fanout-apply-effect-dynamic-missing-value");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingValueInput &&
                d.NodeId == "fan" &&
                d.Message.Contains("'value'", StringComparison.Ordinal)));
        }

        [Test]
        public void FrontDoor_EffectFanOutDispatchEffect_RequiresEffectTemplateAndPayloadPreset()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "dispatch",
                  "nodes": [
                    { "id": "dispatch", "op": "FanOutDispatchEffect" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.effect.fanout-dispatch-effect-missing-fields");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                    d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                    d.NodeId == "dispatch" &&
                    d.Message.Contains("effectTemplate", StringComparison.Ordinal)));
                Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                    d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                    d.NodeId == "dispatch" &&
                    d.Message.Contains("payloadPreset", StringComparison.Ordinal)));
            });
        }

        [Test]
        public void FrontDoor_EffectFanOutDispatchEffectDynamic_RequiresPayloadPreset()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "templateId",
                  "nodes": [
                    { "id": "templateId", "op": "ConstInt", "intValue": 77 },
                    { "id": "dispatch", "op": "FanOutDispatchEffectDynamic" }
                  ],
                  "controlEdges": [
                    { "from": "templateId", "fromPort": "next", "to": "dispatch" }
                  ],
                  "valueEdges": [
                    { "from": "templateId", "fromPort": "value", "to": "dispatch", "toPort": "value" }
                  ]
                }
                """,
                "tests.effect.fanout-dispatch-effect-dynamic-missing-payload-preset");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                d.NodeId == "dispatch" &&
                d.Message.Contains("payloadPreset", StringComparison.Ordinal)));
        }

        [Test]
        public void FrontDoor_EffectTagAndDisplayOps_CompileAndEmit()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "self",
                  "nodes": [
                    { "id": "self", "op": "LoadCaster" },
                    { "id": "target", "op": "LoadExplicitTarget" },
                    { "id": "hasTag", "op": "HasTag", "tag": "State.Moving" },
                    { "id": "eqEntity", "op": "CompareEqEntity" },
                    { "id": "readTag", "op": "ReadGameplayTag", "displayTable": "entity.state.display", "tagSelectPolicy": "RequireOne" },
                    { "id": "lookup", "op": "LookupTagDisplayText", "displayTable": "entity.state.display" }
                  ],
                  "controlEdges": [
                    { "from": "self", "fromPort": "next", "to": "target" },
                    { "from": "target", "fromPort": "next", "to": "hasTag" },
                    { "from": "hasTag", "fromPort": "next", "to": "eqEntity" },
                    { "from": "eqEntity", "fromPort": "next", "to": "readTag" },
                    { "from": "readTag", "fromPort": "next", "to": "lookup" }
                  ],
                  "valueEdges": [
                    { "from": "self", "fromPort": "value", "to": "hasTag", "toPort": "source" },
                    { "from": "self", "fromPort": "value", "to": "eqEntity", "toPort": "a" },
                    { "from": "target", "fromPort": "value", "to": "eqEntity", "toPort": "b" },
                    { "from": "self", "fromPort": "value", "to": "readTag", "toPort": "source" },
                    { "from": "readTag", "fromPort": "value", "to": "lookup", "toPort": "a" }
                  ]
                }
                """,
                "tests.effect.tag-display-ops");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);

            GraphInstruction[] program = compiled.Package!.Value.Program;
            Assert.Multiple(() =>
            {
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.HasTag));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.CompareEqEntity));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.SelectTagInMask));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.LookupTagDisplayToken));
            });

            GraphInstruction hasTag = program.Single(i => i.Op == (ushort)GraphNodeOp.HasTag);
            GraphInstruction compareEq = program.Single(i => i.Op == (ushort)GraphNodeOp.CompareEqEntity);
            GraphInstruction selectTag = program.Single(i => i.Op == (ushort)GraphNodeOp.SelectTagInMask);
            GraphInstruction lookup = program.Single(i => i.Op == (ushort)GraphNodeOp.LookupTagDisplayToken);

            Assert.Multiple(() =>
            {
                Assert.That(hasTag.A, Is.EqualTo(0));
                Assert.That(compareEq.A, Is.EqualTo(0));
                Assert.That(compareEq.B, Is.EqualTo(1));
                Assert.That(selectTag.A, Is.EqualTo(0));
                Assert.That(selectTag.Flags, Is.EqualTo((byte)TagSelectPolicy.RequireOne));
                Assert.That(lookup.A, Is.EqualTo(selectTag.Dst));
            });

            int tagSymbol = compiled.Package.Value.Symbols.ToList().IndexOf("State.Moving");
            int tableSymbol = compiled.Package.Value.Symbols.ToList().IndexOf("entity.state.display");
            Assert.Multiple(() =>
            {
                Assert.That(hasTag.Imm, Is.EqualTo(tagSymbol));
                Assert.That(selectTag.Imm, Is.EqualTo(tableSymbol));
                Assert.That(lookup.Imm, Is.EqualTo(tableSymbol));
            });

            var tables = new TagDisplayTableRegistry();
            int movingTagId = TagRegistry.Register("State.Moving");
            var mask = new GameplayTagContainer();
            mask.AddTag(movingTagId);
            tables.RegisterTable("entity.state.display", in mask, new (int, int)[] { (movingTagId, 42) });
            tables.Freeze();
            var resolver = new GasGraphSymbolResolver(
                new Ludots.Core.Gameplay.Relationships.RelationshipTypeRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipMetricRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipFlagRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipReasonRegistry(),
                new TargetDispatchPresetRegistry(),
                new EntityTemplateKeyRegistry(),
                tagDisplayTables: tables);
            GraphProgramSymbolPatcher.Patch(compiled.Package.Value.Symbols, program, resolver);

            hasTag = program.Single(i => i.Op == (ushort)GraphNodeOp.HasTag);
            selectTag = program.Single(i => i.Op == (ushort)GraphNodeOp.SelectTagInMask);
            lookup = program.Single(i => i.Op == (ushort)GraphNodeOp.LookupTagDisplayToken);

            Assert.Multiple(() =>
            {
                Assert.That(hasTag.Imm, Is.EqualTo(movingTagId));
                Assert.That(selectTag.Imm, Is.EqualTo(tables.GetTableId("entity.state.display")));
                Assert.That(lookup.Imm, Is.EqualTo(tables.GetTableId("entity.state.display")));
            });

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Effect,
                    program,
                    GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void FrontDoor_EffectEventControlKnowledgeOps_CompileAndEmit()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "source",
                  "nodes": [
                    { "id": "source", "op": "LoadContextSource" },
                    { "id": "targetCtx", "op": "LoadContextTargetContext" },
                    { "id": "payloadInt", "op": "LoadEventPayloadInt", "slot": 0 },
                    { "id": "payloadFloat", "op": "LoadEventPayloadFloat", "slot": 3 },
                    { "id": "domain", "op": "ControlDomainResolve" },
                    { "id": "controls", "op": "ControlDomainControls" },
                    { "id": "projection", "op": "KnowledgeHasProjection" },
                    { "id": "magnitude", "op": "ConstFloat", "floatValue": 1.5 },
                    { "id": "send", "op": "SendEvent", "tag": "Event.DamageDealt" }
                  ],
                  "controlEdges": [
                    { "from": "source", "fromPort": "next", "to": "targetCtx" },
                    { "from": "targetCtx", "fromPort": "next", "to": "payloadInt" },
                    { "from": "payloadInt", "fromPort": "next", "to": "payloadFloat" },
                    { "from": "payloadFloat", "fromPort": "next", "to": "domain" },
                    { "from": "domain", "fromPort": "next", "to": "controls" },
                    { "from": "controls", "fromPort": "next", "to": "projection" },
                    { "from": "projection", "fromPort": "next", "to": "magnitude" },
                    { "from": "magnitude", "fromPort": "next", "to": "send" }
                  ],
                  "valueEdges": [
                    { "from": "source", "fromPort": "value", "to": "domain", "toPort": "source" },
                    { "from": "source", "fromPort": "value", "to": "controls", "toPort": "a" },
                    { "from": "targetCtx", "fromPort": "value", "to": "controls", "toPort": "b" },
                    { "from": "source", "fromPort": "value", "to": "projection", "toPort": "a" },
                    { "from": "targetCtx", "fromPort": "value", "to": "projection", "toPort": "b" },
                    { "from": "targetCtx", "fromPort": "value", "to": "send", "toPort": "target" },
                    { "from": "magnitude", "fromPort": "value", "to": "send", "toPort": "value" }
                  ]
                }
                """,
                "tests.effect.event-control-knowledge-ops");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);

            GraphInstruction[] program = compiled.Package!.Value.Program;
            Assert.Multiple(() =>
            {
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.LoadContextSource));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.LoadContextTargetContext));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.LoadEventPayloadInt));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.LoadEventPayloadFloat));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.ControlDomainResolve));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.ControlDomainControls));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.KnowledgeHasProjection));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.SendEvent));
            });

            GraphInstruction payloadInt = program.Single(i => i.Op == (ushort)GraphNodeOp.LoadEventPayloadInt);
            GraphInstruction payloadFloat = program.Single(i => i.Op == (ushort)GraphNodeOp.LoadEventPayloadFloat);
            GraphInstruction domain = program.Single(i => i.Op == (ushort)GraphNodeOp.ControlDomainResolve);
            GraphInstruction controls = program.Single(i => i.Op == (ushort)GraphNodeOp.ControlDomainControls);
            GraphInstruction projection = program.Single(i => i.Op == (ushort)GraphNodeOp.KnowledgeHasProjection);
            GraphInstruction send = program.Single(i => i.Op == (ushort)GraphNodeOp.SendEvent);
            GraphInstruction source = program.Single(i => i.Op == (ushort)GraphNodeOp.LoadContextSource);
            GraphInstruction targetCtx = program.Single(i => i.Op == (ushort)GraphNodeOp.LoadContextTargetContext);
            GraphInstruction magnitude = program.Single(i => i.Op == (ushort)GraphNodeOp.ConstFloat);

            Assert.Multiple(() =>
            {
                Assert.That(payloadInt.Imm, Is.EqualTo(0));
                Assert.That(payloadFloat.Imm, Is.EqualTo(3));
                Assert.That(domain.A, Is.EqualTo(source.Dst));
                Assert.That(controls.A, Is.EqualTo(source.Dst));
                Assert.That(controls.B, Is.EqualTo(targetCtx.Dst));
                Assert.That(projection.A, Is.EqualTo(source.Dst));
                Assert.That(projection.B, Is.EqualTo(targetCtx.Dst));
                Assert.That(send.A, Is.EqualTo(targetCtx.Dst));
                Assert.That(send.B, Is.EqualTo(magnitude.Dst));
                Assert.That(compiled.Package.Value.Symbols[send.Imm], Is.EqualTo("Event.DamageDealt"));
            });

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Effect,
                    program,
                    GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void FrontDoor_EffectSendEvent_RequiresEventTag()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "target",
                  "nodes": [
                    { "id": "target", "op": "LoadExplicitTarget" },
                    { "id": "magnitude", "op": "ConstFloat", "floatValue": 1 },
                    { "id": "send", "op": "SendEvent" }
                  ],
                  "controlEdges": [
                    { "from": "target", "fromPort": "next", "to": "magnitude" },
                    { "from": "magnitude", "fromPort": "next", "to": "send" }
                  ],
                  "valueEdges": [
                    { "from": "target", "fromPort": "value", "to": "send", "toPort": "target" },
                    { "from": "magnitude", "fromPort": "value", "to": "send", "toPort": "value" }
                  ]
                }
                """,
                "tests.effect.send-event-missing-tag");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                d.NodeId == "send" &&
                d.Message.Contains("tag", StringComparison.Ordinal)));
        }

        [Test]
        public void FrontDoor_QueryTagDisplayOps_CompileAndEmit()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Query",
                  "entry": "self",
                  "nodes": [
                    { "id": "self", "op": "LoadCaster" },
                    { "id": "readTag", "op": "ReadGameplayTag", "displayTable": "entity.state.display" },
                    { "id": "lookup", "op": "LookupTagDisplayText", "displayTable": "entity.state.display" }
                  ],
                  "controlEdges": [
                    { "from": "self", "fromPort": "next", "to": "readTag" },
                    { "from": "readTag", "fromPort": "next", "to": "lookup" }
                  ],
                  "valueEdges": [
                    { "from": "self", "fromPort": "value", "to": "readTag", "toPort": "source" },
                    { "from": "readTag", "fromPort": "value", "to": "lookup", "toPort": "a" }
                  ],
                  "outputs": [
                    {
                      "id": "stateToken",
                      "destination": "Summary",
                      "type": "Int",
                      "source": "lookup",
                      "key": "panel.entity_info.curState"
                    }
                  ]
                }
                """,
                "tests.query.tag-display-ops");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);
            Assert.That(compiled.OutputSchema.Bindings.Length, Is.EqualTo(1));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.SelectTagInMask));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.LookupTagDisplayToken));

            GraphInstruction selectTag = compiled.Program.Single(i => i.Op == (ushort)GraphNodeOp.SelectTagInMask);
            Assert.That(selectTag.Flags, Is.EqualTo((byte)TagSelectPolicy.RequireOne));

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Query,
                    compiled.Package!.Value.Program,
                    GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void FrontDoor_EffectQueryListOps_CompileAndEmit()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "radius",
                  "nodes": [
                    { "id": "radius", "op": "QueryRadius", "queryCapacityPolicy": "RequireComplete", "radiusCm": 800 },
                    { "id": "sort", "op": "QuerySortStable" },
                    { "id": "limit", "op": "QueryLimit", "intValue": 1 },
                    { "id": "nearest", "op": "AggMinByDistance" }
                  ],
                  "controlEdges": [
                    { "from": "radius", "fromPort": "next", "to": "sort" },
                    { "from": "sort", "fromPort": "next", "to": "limit" },
                    { "from": "limit", "fromPort": "next", "to": "nearest" }
                  ],
                  "valueEdges": []
                }
                """,
                "tests.effect.query-list-ops");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);

            GraphInstruction[] program = compiled.Package!.Value.Program;
            Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.QueryRadius));
            Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.QuerySortStable));
            Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.QueryLimit));
            Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.AggMinByDistance));

            GraphInstruction radius = program.Single(i => i.Op == (ushort)GraphNodeOp.QueryRadius);
            Assert.That(radius.ImmF, Is.EqualTo(800f));
            Assert.That(radius.Flags, Is.EqualTo((byte)0));

            GraphInstruction limit = program.Single(i => i.Op == (ushort)GraphNodeOp.QueryLimit);
            Assert.That(limit.Imm, Is.EqualTo(1));

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Effect,
                    program,
                    GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void FrontDoor_QueryKind_QueryListOps_CompileAndEmit()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Query",
                  "entry": "radius",
                  "nodes": [
                    { "id": "radius", "op": "QueryRadius", "queryCapacityPolicy": "RequireComplete", "radiusCm": 500 },
                    { "id": "sort", "op": "QuerySortStable" },
                    { "id": "limit", "op": "QueryLimit", "intValue": 3 },
                    { "id": "nearest", "op": "AggMinByDistance" },
                    { "id": "count", "op": "AggCount" }
                  ],
                  "controlEdges": [
                    { "from": "radius", "fromPort": "next", "to": "sort" },
                    { "from": "sort", "fromPort": "next", "to": "limit" },
                    { "from": "limit", "fromPort": "next", "to": "nearest" },
                    { "from": "nearest", "fromPort": "next", "to": "count" }
                  ],
                  "valueEdges": [
                    { "from": "radius", "fromPort": "list", "to": "sort", "toPort": "list" },
                    { "from": "sort", "fromPort": "list", "to": "limit", "toPort": "list" },
                    { "from": "limit", "fromPort": "list", "to": "nearest", "toPort": "list" },
                    { "from": "limit", "fromPort": "list", "to": "count", "toPort": "list" }
                  ],
                  "outputs": [
                    {
                      "id": "nearestEntity",
                      "destination": "Summary",
                      "type": "Entity",
                      "source": "nearest",
                      "key": "panel.nearest"
                    },
                    {
                      "id": "targetCount",
                      "destination": "Summary",
                      "type": "Int",
                      "source": "count",
                      "key": "panel.count"
                    }
                  ]
                }
                """,
                "tests.query.query-list-ops");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);
            Assert.That(compiled.OutputSchema.Bindings.Length, Is.EqualTo(2));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.QueryRadius));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.QuerySortStable));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.QueryLimit));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.AggMinByDistance));

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Query,
                    compiled.Package!.Value.Program,
                    GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void FrontDoor_QueryRadius_RequiresCapacityPolicy()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "radius",
                  "nodes": [
                    { "id": "radius", "op": "QueryRadius", "radiusCm": 250 }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.effect.query-radius-missing-capacity-policy");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.TypeMismatch &&
                d.NodeId == "radius" &&
                d.Message.Contains("queryCapacityPolicy", StringComparison.Ordinal)));
        }

        [Test]
        public void FrontDoor_SelectTagInMask_RequiresDisplayTable()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "self",
                  "nodes": [
                    { "id": "self", "op": "LoadCaster" },
                    { "id": "readTag", "op": "SelectTagInMask" }
                  ],
                  "controlEdges": [
                    { "from": "self", "fromPort": "next", "to": "readTag" }
                  ],
                  "valueEdges": [
                    { "from": "self", "fromPort": "value", "to": "readTag", "toPort": "source" }
                  ]
                }
                """,
                "tests.effect.select-tag-missing-table");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                d.NodeId == "readTag" &&
                d.Message.Contains("displayTable", StringComparison.Ordinal)));
        }

        [Test]
        public void FrontDoor_EffectClampFloat_RequiresValueMinAndMaxInputs()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Effect",
                  "entry": "value",
                  "nodes": [
                    { "id": "value", "op": "ConstFloat", "floatValue": 10 },
                    { "id": "min", "op": "ConstFloat", "floatValue": 0 },
                    { "id": "clamp", "op": "ClampFloat" }
                  ],
                  "controlEdges": [
                    { "from": "value", "fromPort": "next", "to": "min" },
                    { "from": "min", "fromPort": "next", "to": "clamp" }
                  ],
                  "valueEdges": [
                    { "from": "value", "fromPort": "value", "to": "clamp", "toPort": "value" },
                    { "from": "min", "fromPort": "value", "to": "clamp", "toPort": "min" }
                  ]
                }
                """,
                "tests.effect.clamp-float-missing-max");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingValueInput &&
                d.NodeId == "clamp" &&
                d.Message.Contains("'max'", StringComparison.Ordinal)));
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

        [Test]
        public void FrontDoor_ValidationPlacementOps_CompileAndEmit()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Validation",
                  "entry": "loadX",
                  "nodes": [
                    { "id": "loadX", "op": "LoadTargetPosX" },
                    { "id": "loadY", "op": "LoadTargetPosY" },
                    { "id": "caster", "op": "LoadCaster" },
                    { "id": "range", "op": "ConstFloat", "floatValue": 500 },
                    { "id": "clamp", "op": "ClampTargetToRange" },
                    { "id": "circle", "op": "IsPointInCircle" },
                    { "id": "snapRadius", "op": "ConstFloat", "floatValue": 100 },
                    { "id": "snapColl", "op": "SnapToNearestInCollection", "collectionKey": "tests.collection.snap" },
                    { "id": "snapEdge", "op": "SnapToNearestGraphEdge" }
                  ],
                  "controlEdges": [
                    { "from": "loadX", "fromPort": "next", "to": "loadY" },
                    { "from": "loadY", "fromPort": "next", "to": "caster" },
                    { "from": "caster", "fromPort": "next", "to": "range" },
                    { "from": "range", "fromPort": "next", "to": "clamp" },
                    { "from": "clamp", "fromPort": "next", "to": "circle" },
                    { "from": "circle", "fromPort": "next", "to": "snapRadius" },
                    { "from": "snapRadius", "fromPort": "next", "to": "snapColl" },
                    { "from": "snapColl", "fromPort": "next", "to": "snapEdge" }
                  ],
                  "valueEdges": [
                    { "from": "caster", "fromPort": "value", "to": "clamp", "toPort": "a" },
                    { "from": "range", "fromPort": "value", "to": "clamp", "toPort": "b" },
                    { "from": "caster", "fromPort": "value", "to": "circle", "toPort": "a" },
                    { "from": "range", "fromPort": "value", "to": "circle", "toPort": "b" },
                    { "from": "caster", "fromPort": "value", "to": "snapColl", "toPort": "source" },
                    { "from": "snapRadius", "fromPort": "value", "to": "snapColl", "toPort": "value" },
                    { "from": "snapRadius", "fromPort": "value", "to": "snapEdge", "toPort": "value" }
                  ]
                }
                """,
                "tests.validation.placement-ops");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);

            GraphInstruction[] program = compiled.Package!.Value.Program;
            Assert.Multiple(() =>
            {
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.LoadTargetPosX));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.LoadTargetPosY));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.ClampTargetToRange));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.IsPointInCircle));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.SnapToNearestInCollection));
                Assert.That(program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.SnapToNearestGraphEdge));
            });

            GraphInstruction snapColl = SingleInstruction(program, GraphNodeOp.SnapToNearestInCollection);
            Assert.That(snapColl.Flags, Is.EqualTo(byte.MaxValue));
            Assert.That(snapColl.Imm, Is.GreaterThanOrEqualTo(0));

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Validation,
                    program,
                    GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void FrontDoor_ValidationClampTargetToRange_RequiresOriginAndRangeInputs()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Validation",
                  "entry": "clamp",
                  "nodes": [
                    { "id": "clamp", "op": "ClampTargetToRange" }
                  ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """,
                "tests.validation.clamp-missing-inputs");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingValueInput &&
                d.NodeId == "clamp" &&
                d.Message.Contains("'a'", StringComparison.Ordinal)));
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingValueInput &&
                d.NodeId == "clamp" &&
                d.Message.Contains("'b'", StringComparison.Ordinal)));
        }

        [Test]
        public void FrontDoor_ValidationSnapToNearestInCollection_RequiresCollectionKey()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "Validation",
                  "entry": "caster",
                  "nodes": [
                    { "id": "caster", "op": "LoadCaster" },
                    { "id": "distance", "op": "ConstFloat", "floatValue": 100 },
                    { "id": "snap", "op": "SnapToNearestInCollection" }
                  ],
                  "controlEdges": [
                    { "from": "caster", "fromPort": "next", "to": "distance" },
                    { "from": "distance", "fromPort": "next", "to": "snap" }
                  ],
                  "valueEdges": [
                    { "from": "caster", "fromPort": "value", "to": "snap", "toPort": "source" },
                    { "from": "distance", "fromPort": "value", "to": "snap", "toPort": "value" }
                  ]
                }
                """,
                "tests.validation.snap-missing-collection");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Package.HasValue, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                d.NodeId == "snap" &&
                d.Message.Contains("collectionKey", StringComparison.Ordinal)));
        }

        private static GraphControlFlowCompileResult CompileFrontDoor(string json, string graphId)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        }

        private static GraphInstruction SingleInstruction(GraphInstruction[] program, GraphNodeOp op)
            => program.Single(i => i.Op == (ushort)op);

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
