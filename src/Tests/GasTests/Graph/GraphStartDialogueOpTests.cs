using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphStartDialogueOpTests
    {
        [Test]
        public void StartDialogue_InvokesBoundStarter_WithPatchedDialogueId()
        {
            using var world = World.Create();
            string? started = null;
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindStartDialogue(id => started = id);

            int keyId = ConfigKeyRegistry.Register("Dialogue.Unit.StartDialogue");
            api.StartDialogue(keyId);
            That(started, Is.EqualTo("Dialogue.Unit.StartDialogue"));

            // Also exercise the opcode path (attribution + handler table).
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.StartDialogue, Imm = keyId },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            started = null;
            Execute(world, api, program);
            That(started, Is.EqualTo("Dialogue.Unit.StartDialogue"));
        }

        [Test]
        public void StartDialogue_WithoutBind_FailsClosed()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);
            int keyId = ConfigKeyRegistry.Register("Dialogue.Unit.MissingBind");
            var ex = Throws<InvalidOperationException>(() => api.StartDialogue(keyId));
            That(ex!.Message, Does.Contain("DialogueRuntimeUnavailable"));
        }

        [Test]
        public void Descriptor_AllowsScriptAndTriggerGraph()
        {
            That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.StartDialogue), Is.True);
            That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.StartDialogue), Is.True);
            That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.StartDialogue), Is.False);
        }

        private static void Execute(World world, GasGraphRuntimeApi api, GraphInstruction[] program)
        {
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var state = new GraphExecutionState
            {
                World = world,
                Api = api,
                Caster = world.Create(),
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack,
            };
            GasGraphOpHandlerTable.Instance.RunToHalt(ref state, program);
        }
    }
}
