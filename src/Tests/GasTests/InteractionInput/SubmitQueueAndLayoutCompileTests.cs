using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Scripting;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS.InteractionInput
{
    [TestFixture]
    public sealed class SubmitQueueAndLayoutCompileTests
    {
        [TestCase(null, SubmitQueueFlags.Immediate)]
        [TestCase("onQueueModifier", SubmitQueueFlags.OnQueueModifier)]
        [TestCase("always", SubmitQueueFlags.Always)]
        public void SubmitCommandIntent_EncodesQueueIntoFlags(string? queue, byte expected)
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(CommandDocument(queue));

            That(result.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error), Is.Empty,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
            GraphInstruction submit = result.Package!.Value.Program.Single(i => i.Op == (ushort)GraphNodeOp.SubmitCommandIntent);
            That(submit.Flags, Is.EqualTo(expected));
        }

        [Test]
        public void SubmitCommandIntent_UnknownQueue_FailsWithInvalidSubmitQueue()
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(CommandDocument("sometimes"));

            That(result.Diagnostics.Any(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.InvalidSubmitQueue &&
                d.Message.Contains("sometimes")), Is.True);
        }

        [Test]
        public void Resolve_OnQueueModifier_FollowsTheFiringActionsQueueModifier()
        {
            var held = new GraphEntryPayloadTable();
            held.SetInt(MapTriggerEventPayloadKeys.Modifiers, InputActionFiredModifiers.Queue);
            var released = new GraphEntryPayloadTable();
            released.SetInt(MapTriggerEventPayloadKeys.Modifiers, 0);

            That(SubmitQueueFlags.Resolve(SubmitQueueFlags.OnQueueModifier, held, "probe"), Is.EqualTo(OrderSubmitMode.Queued));
            That(SubmitQueueFlags.Resolve(SubmitQueueFlags.OnQueueModifier, released, "probe"), Is.EqualTo(OrderSubmitMode.Immediate));
            That(SubmitQueueFlags.Resolve(SubmitQueueFlags.Always, null, "probe"), Is.EqualTo(OrderSubmitMode.Queued));
            That(SubmitQueueFlags.Resolve(SubmitQueueFlags.Immediate, null, "probe"), Is.EqualTo(OrderSubmitMode.Immediate));
        }

        [Test]
        public void Resolve_OnQueueModifierOutsideAnInputAction_Throws()
        {
            var error = Throws<InvalidOperationException>(() =>
                SubmitQueueFlags.Resolve(SubmitQueueFlags.OnQueueModifier, new GraphEntryPayloadTable(), "SubmitCommandIntent"));

            That(error!.Message, Does.Contain("SubmitQueueModifierUnavailable").And.Contain("SubmitCommandIntent"));
        }

        [TestCase("preserveRelative", GroundLayoutAssignment.PreserveRelative)]
        [TestCase("actorOrder", GroundLayoutAssignment.ActorOrder)]
        public void SubmitCommandIntent_EncodesLayoutIntoTheInstruction(string layout, GroundLayoutAssignment expected)
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(CommandDocument(null, layout, 180));

            That(result.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error), Is.Empty,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
            GraphInstruction submit = result.Package!.Value.Program.Single(i => i.Op == (ushort)GraphNodeOp.SubmitCommandIntent);
            That(SubmitGroundLayout.Decode(submit.C, submit.Imm), Is.EqualTo(new GroundLayout(expected, 180)));
        }

        [Test]
        public void SubmitCommandIntent_WithoutLayout_StacksOnTheGroundPoint()
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(CommandDocument(null));

            GraphInstruction submit = result.Package!.Value.Program.Single(i => i.Op == (ushort)GraphNodeOp.SubmitCommandIntent);
            That(SubmitGroundLayout.Decode(submit.C, submit.Imm).Assignment, Is.EqualTo(GroundLayoutAssignment.None));
        }

        [TestCase("wedge", 180)]
        [TestCase("preserveRelative", 0)]
        [TestCase(null, 180)]
        public void SubmitCommandIntent_InvalidLayout_FailsWithInvalidSubmitGroundLayout(string? layout, int spacingCm)
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(CommandDocument(null, layout, spacingCm));

            That(result.Diagnostics.Any(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.InvalidSubmitGroundLayout), Is.True);
        }

        private static GraphControlFlowDocument CommandDocument(string? queue, string? layout = null, int layoutSpacingCm = 0)
        {
            return new GraphControlFlowDocument
            {
                Id = "Graph.Probe.SubmitQueue",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "on_command", Event = "MapLoaded", Start = "px" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "px", Op = "LoadPointerScreenX" },
                    new() { Id = "py", Op = "LoadPointerScreenY" },
                    new() { Id = "gnd", Op = "ScreenPointToGround" },
                    new() { Id = "submit", Op = "SubmitCommandIntent", Queue = queue, Layout = layout, LayoutSpacingCm = layoutSpacingCm },
                    new() { Id = "done", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("px", "next", "py"),
                    new("py", "next", "gnd"),
                    new("gnd", "next", "submit"),
                    new("submit", "next", "done"),
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("px", "value", "gnd", "a"),
                    new("py", "value", "gnd", "b"),
                    new("gnd", "value", "submit", "condition"),
                },
            };
        }
    }
}
