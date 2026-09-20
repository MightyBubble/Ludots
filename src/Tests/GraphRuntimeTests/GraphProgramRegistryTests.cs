using System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.GraphRuntime
{
    [TestFixture]
    public sealed class GraphProgramRegistryTests
    {
        [Test]
        public void Register_EmptyProgram_ThrowsAtRegistration()
        {
            var registry = new GraphProgramRegistry();

            Assert.That(
                () => registry.Register(1, Array.Empty<GraphInstruction>(), GraphKind.Effect),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Register_ProgramExceedingMaxInstructions_ThrowsAtRegistration()
        {
            var registry = new GraphProgramRegistry();
            var oversized = new GraphInstruction[GraphVmRuntimeLimits.MaxInstructions + 1];

            Assert.That(
                () => registry.Register(1, oversized, GraphKind.Effect),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Register_ProgramAtMaxInstructions_IsAccepted()
        {
            var registry = new GraphProgramRegistry();
            var atLimit = new GraphInstruction[GraphVmRuntimeLimits.MaxInstructions];
            atLimit[^1] = new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt };

            Assert.That(() => registry.Register(1, atLimit, GraphKind.Effect), Throws.Nothing);
            Assert.That(registry.TryGetProgram(1, out ReadOnlySpan<GraphInstruction> program), Is.True);
            Assert.That(program.Length, Is.EqualTo(GraphVmRuntimeLimits.MaxInstructions));
        }
    }
}
