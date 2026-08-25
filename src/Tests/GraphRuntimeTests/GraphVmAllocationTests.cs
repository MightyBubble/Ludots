using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.GraphRuntime
{
    [TestFixture]
    public sealed class GraphVmAllocationTests
    {
        private const int EntityCount = 50_000;

        [Test]
        public void ExecuteSlice_AsyncFunctionResumeToHalt_DoesNotAllocate()
        {
            GraphVmCompileResult compiled = GraphVmCompiler.Compile(GraphVmTestGraphs.CreateDrinkUntilFullGraph());
            Assert.That(compiled.Succeeded, Is.True, GraphVmTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Span<int> ints = stackalloc int[GraphVmRuntimeLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmRuntimeLimits.MaxBoolRegisters];
            Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
            var cursor = new GraphVmExecutionCursor();

            RunToHalt(compiled.Program, ints, bools, callStack, ref cursor);
            cursor.Reset();

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            GraphVmExecutionResult result = RunToHalt(compiled.Program, ints, bools, callStack, ref cursor);
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(result.ReturnInt, Is.EqualTo(3));
            Assert.That(allocatedBytes, Is.EqualTo(0));
        }

        [Test]
        public void ExecuteSlice_50000EntityYieldResumeHalt_DoesNotAllocate()
        {
            GraphVmCompileResult compiled = GraphVmCompiler.Compile(GraphVmTestGraphs.CreateDrinkUntilFullGraph());
            Assert.That(compiled.Succeeded, Is.True, GraphVmTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            int[] ints = new int[EntityCount * GraphVmRuntimeLimits.MaxIntRegisters];
            byte[] bools = new byte[EntityCount * GraphVmRuntimeLimits.MaxBoolRegisters];
            int[] callStacks = new int[EntityCount * GraphVmRuntimeLimits.MaxCallStackDepth];
            GraphVmExecutionCursor[] cursors = new GraphVmExecutionCursor[EntityCount];

            for (int entity = 0; entity < EntityCount; entity++)
            {
                cursors[entity].Reset();
            }

            RunSlice(compiled.Program, ints, bools, callStacks, cursors, entity: 0, sliceSteps: GraphVmRuntimeLimits.MaxInstructionsPerExecution);

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

            int yielded = 0;
            for (int entity = 0; entity < EntityCount; entity++)
            {
                GraphVmExecutionResult first = RunSlice(compiled.Program, ints, bools, callStacks, cursors, entity, GraphVmRuntimeLimits.MaxInstructionsPerExecution);
                yielded += first.Yielded ? 1 : 0;
            }

            int resumed = 0;
            for (int entity = 0; entity < EntityCount; entity++)
            {
                GraphVmExecutionResult second = RunSlice(compiled.Program, ints, bools, callStacks, cursors, entity, GraphVmRuntimeLimits.MaxInstructionsPerExecution);
                resumed += second.Yielded ? 1 : 0;
            }

            int halted = 0;
            for (int entity = 0; entity < EntityCount; entity++)
            {
                GraphVmExecutionResult result;
                do
                {
                    result = RunSlice(compiled.Program, ints, bools, callStacks, cursors, entity, GraphVmRuntimeLimits.MaxInstructionsPerExecution);
                }
                while (!result.Halted);
                halted++;
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(yielded, Is.EqualTo(EntityCount));
            Assert.That(resumed, Is.EqualTo(EntityCount));
            Assert.That(halted, Is.EqualTo(EntityCount));
            Assert.That(allocatedBytes, Is.EqualTo(0));
        }

        private static GraphVmExecutionResult RunToHalt(
            GraphInstruction[] program,
            Span<int> ints,
            Span<byte> bools,
            Span<int> callStack,
            ref GraphVmExecutionCursor cursor)
        {
            GraphVmExecutionResult result;
            do
            {
                result = GraphVmExecutor.ExecuteSlice(
                    program,
                    ints,
                    bools,
                    callStack,
                    ref cursor,
                    GraphVmRuntimeLimits.MaxInstructionsPerExecution);
            }
            while (!result.Halted);

            return result;
        }

        private static GraphVmExecutionResult RunSlice(
            GraphInstruction[] program,
            int[] ints,
            byte[] bools,
            int[] callStacks,
            GraphVmExecutionCursor[] cursors,
            int entity,
            int sliceSteps)
        {
            int intOffset = entity * GraphVmRuntimeLimits.MaxIntRegisters;
            int boolOffset = entity * GraphVmRuntimeLimits.MaxBoolRegisters;
            int stackOffset = entity * GraphVmRuntimeLimits.MaxCallStackDepth;

            Span<int> entityInts = ints.AsSpan(intOffset, GraphVmRuntimeLimits.MaxIntRegisters);
            Span<byte> entityBools = bools.AsSpan(boolOffset, GraphVmRuntimeLimits.MaxBoolRegisters);
            Span<int> entityCallStack = callStacks.AsSpan(stackOffset, GraphVmRuntimeLimits.MaxCallStackDepth);

            GraphVmExecutionCursor cursor = cursors[entity];
            GraphVmExecutionResult result = GraphVmExecutor.ExecuteSlice(
                program,
                entityInts,
                entityBools,
                entityCallStack,
                ref cursor,
                sliceSteps);
            cursors[entity] = cursor;
            return result;
        }

    }
}
