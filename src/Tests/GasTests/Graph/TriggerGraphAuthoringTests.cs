using System;
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
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class TriggerGraphAuthoringTests
    {
        [Test]
        public void TriggerGraph_TwoEntries_CompileRegisterAndDispatchFromEntryPcs()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1" },
                    { "label": "on_status_open", "event": "PanelOpenStatus", "start": "b1", "once": true }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 11 },
                    { "id": "aHalt", "op": "HaltReturnInt" },
                    { "id": "b1", "op": "ConstInt", "intValue": 22 },
                    { "id": "bHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" },
                    { "from": "b1", "fromPort": "next", "to": "bHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" },
                    { "from": "b1", "fromPort": "value", "to": "bHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.two-entries");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            GraphProgramPackage package = compiled.Package!.Value;
            TriggerGraphEntry[] entries = package.TriggerGraphEntries;
            Assert.That(entries.Length, Is.EqualTo(2));
            Assert.That(entries[0].Label, Is.EqualTo("on_map_loaded"));
            Assert.That(entries[0].EventName, Is.EqualTo("MapLoaded"));
            Assert.That(entries[0].Once, Is.False);
            Assert.That(entries[1].Label, Is.EqualTo("on_status_open"));
            Assert.That(entries[1].EventName, Is.EqualTo("PanelOpenStatus"));
            Assert.That(entries[1].Once, Is.True);
            Assert.That(entries[0].StartPc, Is.Not.EqualTo(entries[1].StartPc));
            Assert.That((GraphNodeOp)package.Program[entries[0].StartPc].Op, Is.EqualTo(GraphNodeOp.ConstInt));
            Assert.That(package.Program[entries[0].StartPc].Imm, Is.EqualTo(11));
            Assert.That((GraphNodeOp)package.Program[entries[1].StartPc].Op, Is.EqualTo(GraphNodeOp.ConstInt));
            Assert.That(package.Program[entries[1].StartPc].Imm, Is.EqualTo(22));

            var registry = new GraphProgramRegistry();
            registry.Register(901, package.Program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, package.Symbols, entries);
            Assert.That(registry.TryGetRegistration(901, out GraphProgramRegistration registration), Is.True);
            Assert.That(registration.TriggerGraphEntries.Count, Is.EqualTo(2));

            Assert.That(ExecuteFromPc(registry, package.Program, entries[0].StartPc), Is.EqualTo(11), "first entry dispatch must halt with its own chain value");
            Assert.That(ExecuteFromPc(registry, package.Program, entries[1].StartPc), Is.EqualTo(22), "second entry dispatch must halt with its own chain value");
            Assert.That(ExecuteFromPc(registry, package.Program, 0), Is.EqualTo(11), "default cursor lands on the prefix jump targeting the first entry");
        }

        [Test]
        public void TriggerGraph_BranchBoolAndInvokeScript_AreAuthorable()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "left" }
                  ],
                  "nodes": [
                    { "id": "left", "op": "ConstInt", "intValue": 1 },
                    { "id": "right", "op": "ConstInt", "intValue": 2 },
                    { "id": "pred", "op": "CompareLtInt" },
                    { "id": "branch", "op": "BranchBool" },
                    { "id": "call", "op": "InvokeScript", "functionName": "tests.pure.helper" },
                    { "id": "retTrue", "op": "HaltReturnInt" },
                    { "id": "retFalse", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "left", "fromPort": "next", "to": "right" },
                    { "from": "right", "fromPort": "next", "to": "pred" },
                    { "from": "pred", "fromPort": "next", "to": "branch" },
                    { "from": "branch", "fromPort": "true", "to": "call" },
                    { "from": "branch", "fromPort": "false", "to": "retFalse" },
                    { "from": "call", "fromPort": "next", "to": "retTrue" }
                  ],
                  "valueEdges": [
                    { "from": "left", "fromPort": "value", "to": "pred", "toPort": "a" },
                    { "from": "right", "fromPort": "value", "to": "pred", "toPort": "b" },
                    { "from": "pred", "fromPort": "value", "to": "branch", "toPort": "condition" }
                  ]
                }
                """,
                "tests.maptrigger.branch-invoke");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.InvokeScript));
            Assert.That(compiled.Package!.Value.TriggerGraphEntries.Length, Is.EqualTo(1));
        }

        [Test]
        public void TriggerGraph_UnknownEventString_IsAllowedAsPlainString()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_custom", "event": "Totally.Unknown.Event", "start": "a1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.unknown-event");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package!.Value.TriggerGraphEntries[0].EventName, Is.EqualTo("Totally.Unknown.Event"));
        }

        [Test]
        public void TriggerGraph_MissingEntries_Throws()
        {
            Assert.That(() => CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.missing-entries"),
                Throws.Exception.With.Message.Contains("tests.maptrigger.missing-entries").And.Message.Contains("entries"));
        }

        [Test]
        public void TriggerGraph_EmptyEntriesArray_Throws()
        {
            Assert.That(() => CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.empty-entries"),
                Throws.Exception.With.Message.Contains("non-empty"));
        }

        [Test]
        public void TriggerGraph_TopLevelEntryField_Rejected()
        {
            Assert.That(() => CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entry": "a1",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.top-level-entry"),
                Throws.Exception.With.Message.Contains("tests.maptrigger.top-level-entry").And.Message.Contains("'entry'"));
        }

        [Test]
        public void ScriptKind_WithTopLevelEntries_Rejected()
        {
            Assert.That(() => CompileFrontDoor(
                """
                {
                  "kind": "Script",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1" }
                  ],
                  "entry": "a1",
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.script.with-entries"),
                Throws.Exception.With.Message.Contains("tests.script.with-entries").And.Message.Contains("TriggerGraph-only"));
        }

        [Test]
        public void TriggerGraph_UnknownEntryField_Rejected()
        {
            Assert.That(() => CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1", "priority": 1 }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.unknown-entry-field"),
                Throws.Exception.With.Message.Contains("tests.maptrigger.unknown-entry-field")
                    .And.Message.Contains("priority")
                    .And.Message.Contains("entries[0]"));
        }

        [Test]
        public void TriggerGraph_MissingRequiredEntryField_Rejected()
        {
            Assert.That(() => CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "start": "a1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.missing-event"),
                Throws.Exception.With.Message.Contains("tests.maptrigger.missing-event")
                    .And.Message.Contains("event")
                    .And.Message.Contains("entries[0]"));
        }

        [Test]
        public void TriggerGraph_OnceMustBeBoolean_Rejected()
        {
            Assert.That(() => CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1", "once": "yes" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.once-not-bool"),
                Throws.Exception.With.Message.Contains("once").And.Message.Contains("boolean"));
        }

        [Test]
        public void TriggerGraph_RefireRestart_AcceptedAndCarried()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_pulse", "event": "ManualPulse", "start": "a1", "refire": "restart" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.refire-restart");

            Assert.That(compiled.Package!.Value.TriggerGraphEntries[0].Refire, Is.EqualTo("restart"));
        }

        [Test]
        public void TriggerGraph_RefireUnknownValue_Rejected()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_pulse", "event": "ManualPulse", "start": "a1", "refire": "queue" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.refire-bad");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics), Does.Contain("refire"));
        }

        [Test]
        public void TriggerGraph_EmptyLabel_FailsClosed()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "   ", "event": "MapLoaded", "start": "a1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.empty-label");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingEntry &&
                d.GraphId == "tests.maptrigger.empty-label" &&
                d.Message.Contains("label", StringComparison.Ordinal)));
        }

        [Test]
        public void TriggerGraph_DuplicateLabels_FailsClosed()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1" },
                    { "label": "on_map_loaded", "event": "PanelOpenStatus", "start": "b1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" },
                    { "id": "b1", "op": "ConstInt", "intValue": 2 },
                    { "id": "bHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" },
                    { "from": "b1", "fromPort": "next", "to": "bHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" },
                    { "from": "b1", "fromPort": "value", "to": "bHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.duplicate-labels");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.DuplicateEntryLabel &&
                d.Message.Contains("on_map_loaded", StringComparison.Ordinal)));
        }

        [Test]
        public void TriggerGraph_WhitespaceEvent_FailsClosed()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "   ", "start": "a1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.whitespace-event");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingEntry &&
                d.Message.Contains("event", StringComparison.Ordinal)));
        }

        [Test]
        public void TriggerGraph_StartReferencingMissingNode_FailsClosed()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "doesNotExist" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.missing-start");

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingNodeRef &&
                d.Message.Contains("doesNotExist", StringComparison.Ordinal)));
        }

        [Test]
        public void TriggerGraph_WaitNode_NowLowersToYield()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "pause", "op": "Wait" },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "pause" },
                    { "from": "pause", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.wait-node");

            Assert.That(compiled.Succeeded, Is.True);
            Assert.That(
                compiled.Program.Any(i => i.Op == (ushort)GraphNodeOp.Yield),
                Is.True,
                "Wait must lower to Yield now that the TriggerGraph host resumes yielded slices");
        }

        [Test]
        public void TriggerGraph_YieldNode_NowAccepted()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "pause", "op": "Yield" },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "pause" },
                    { "from": "pause", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.yield-node");

            Assert.That(compiled.Succeeded, Is.True);
            Assert.That(
                compiled.Program.Any(i => i.Op == (ushort)GraphNodeOp.Yield),
                Is.True,
                "Yield is authorable for TriggerGraph now that the host resumes yielded slices");
        }

        [Test]
        public void Register_TriggerGraphKind_WithEmptyEntryTable_Throws()
        {
            GraphControlFlowCompileResult compiled = CompileSmallTriggerGraph();
            GraphInstruction[] program = compiled.Package!.Value.Program;

            var registry = new GraphProgramRegistry();
            Assert.That(() => registry.Register(902, program, GraphKind.TriggerGraph),
                Throws.Exception.With.Message.Contains("TriggerGraph").And.Message.Contains("entry table"));
        }

        [Test]
        public void Register_NonTriggerGraphKind_WithEntryTable_Throws()
        {
            GraphControlFlowCompileResult compiled = CompileSmallTriggerGraph();
            GraphInstruction[] program = compiled.Package!.Value.Program;
            TriggerGraphEntry[] entries = compiled.Package.Value.TriggerGraphEntries;

            var registry = new GraphProgramRegistry();
            Assert.That(
                () => registry.Register(903, program, GraphKind.Script, GraphInstructionSourceMap.Empty, Array.Empty<string>(), entries),
                Throws.Exception.With.Message.Contains("TriggerGraph-only"));
        }

        [Test]
        public void Register_TriggerGraph_StartPcOutOfBounds_Throws()
        {
            GraphControlFlowCompileResult compiled = CompileSmallTriggerGraph();
            GraphInstruction[] program = compiled.Package!.Value.Program;
            var outOfBounds = new[] { new TriggerGraphEntry("on_map_loaded", "MapLoaded", program.Length, once: false) };

            var registry = new GraphProgramRegistry();
            Assert.That(
                () => registry.Register(904, program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, Array.Empty<string>(), outOfBounds),
                Throws.Exception.With.Message.Contains("StartPc"));
        }

        [Test]
        public void Register_TriggerGraph_DuplicateLabels_Throws()
        {
            GraphControlFlowCompileResult compiled = CompileSmallTriggerGraph();
            GraphInstruction[] program = compiled.Package!.Value.Program;
            TriggerGraphEntry entry = compiled.Package.Value.TriggerGraphEntries[0];
            var duplicated = new[] { entry, entry };

            var registry = new GraphProgramRegistry();
            Assert.That(
                () => registry.Register(905, program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, Array.Empty<string>(), duplicated),
                Throws.Exception.With.Message.Contains("duplicate").And.Message.Contains(entry.Label));
        }

        [Test]
        public void ReplaceProgram_TriggerGraph_KeepsAndValidatesEntries()
        {
            GraphControlFlowCompileResult compiled = CompileSmallTriggerGraph();
            GraphInstruction[] program = compiled.Package!.Value.Program;
            TriggerGraphEntry[] entries = compiled.Package.Value.TriggerGraphEntries;

            var registry = new GraphProgramRegistry();
            registry.Register(906, program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, Array.Empty<string>(), entries);

            Assert.That(
                () => registry.ReplaceProgram(906, program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty),
                Throws.Exception.With.Message.Contains("entry table"),
                "hot replace without an entry table must fail closed for TriggerGraph");
            Assert.That(registry.TryGetRegistration(906, out GraphProgramRegistration afterFailedReplace), Is.True);
            Assert.That(afterFailedReplace.TriggerGraphEntries.Count, Is.EqualTo(entries.Length), "failed replace must roll back to the original entry table");

            var replaced = new[]
            {
                new TriggerGraphEntry("on_panel_open", "PanelOpenStatus", entries[0].StartPc, once: true)
            };
            registry.ReplaceProgram(906, program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, Array.Empty<string>(), replaced);
            Assert.That(registry.TryGetRegistration(906, out GraphProgramRegistration afterReplace), Is.True);
            Assert.That(afterReplace.TriggerGraphEntries.Count, Is.EqualTo(1));
            Assert.That(afterReplace.TriggerGraphEntries[0].Label, Is.EqualTo("on_panel_open"));
            Assert.That(afterReplace.TriggerGraphEntries[0].Once, Is.True);

            var duplicateLabels = new[]
            {
                new TriggerGraphEntry("dup", "MapLoaded", entries[0].StartPc, once: false),
                new TriggerGraphEntry("dup", "PanelOpenStatus", entries[0].StartPc, once: false)
            };
            Assert.That(
                () => registry.ReplaceProgram(906, program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, Array.Empty<string>(), duplicateLabels),
                Throws.Exception.With.Message.Contains("duplicate"));
            Assert.That(registry.TryGetRegistration(906, out GraphProgramRegistration afterDuplicateReplace), Is.True);
            Assert.That(afterDuplicateReplace.TriggerGraphEntries[0].Label, Is.EqualTo("on_panel_open"), "invalid replace must roll back");
        }

        [Test]
        public void DescriptorProjections_TriggerGraphMirrorsScriptIncludingYield()
        {
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.Yield), Is.True,
                "Yield is authorable for TriggerGraph now that the host resumes yielded slices");
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.Yield), Is.True);
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.Jump), Is.True);
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.ConstInt), Is.True);
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.InvokeScript), Is.True);
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.MoveInt), Is.True);
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.ReadMapVarInt), Is.True);
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.ReadMapVarInt), Is.True);
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Effect, GraphNodeOp.ReadMapVarInt), Is.False);
            Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.ApplyEffectTemplate), Is.False,
                "effect-transactional ops stay out of the TriggerGraph dialect");

            foreach (GraphNodeOp op in GraphOpDescriptorTable.EnumerateAuthorable(GraphKind.TriggerGraph))
            {
                bool scriptHas = GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, op);
                Assert.That(scriptHas, Is.True, $"TriggerGraph authorable op {op} must come from the Script set");
            }

            Assert.That(
                GraphOpDescriptorTable.ProjectCoverageAuthorableKinds(GraphNodeOp.Yield),
                Is.EqualTo(new[] { "TriggerGraph", "Script" }));
        }

        [Test]
        public void GraphKindParser_AcceptsTriggerGraphExactly()
        {
            Assert.That(GraphKindParser.TryParse("TriggerGraph", out GraphKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(GraphKind.TriggerGraph));
            Assert.That(GraphKindParser.TryParse("maptrigger", out _), Is.False);

            Assert.That(
                () => GraphKindParser.ParseRequired("NotAKind", "tests.parse"),
                Throws.Exception.With.Message.Contains("TriggerGraph"));
        }

        private static GraphControlFlowCompileResult CompileSmallTriggerGraph()
        {
            GraphControlFlowCompileResult compiled = CompileFrontDoor(
                """
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 7 },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """,
                "tests.maptrigger.small");
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            return compiled;
        }

        private static GraphControlFlowCompileResult CompileFrontDoor(string json, string graphId)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        }

        private static int ExecuteFromPc(
            GraphProgramRegistry registry,
            GraphInstruction[] program,
            int startPc)
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor(startPc);

            GraphSliceResult result = GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
                registry,
                program,
                ints,
                bools,
                callStack,
                ref cursor,
                budgetSteps: 64,
                world,
                caster);

            Assert.That(result.Halted, Is.True, $"dispatch from pc={startPc} must run its chain to HaltReturnInt");
            return result.ReturnInt;
        }
    }
}
