using System.Diagnostics;
using Ludots.Core.GraphRuntime;

namespace Ludots.Benchmarks.GraphRuntime;

internal static class GraphVmBenchmarkSmoke
{
    public static void Run()
    {
        GraphVmDocument graph = GraphVmBenchmarkGraphs.CreateDrinkUntilFullGraph(limit: 3);
        GraphVmCompileResult compiled = GraphVmCompiler.Compile(graph);
        if (!compiled.Succeeded)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, compiled.Diagnostics.Select(d =>
                $"{d.Code}:{d.NodeId}:{d.Message}")));
        }

        Span<int> ints = stackalloc int[GraphVmRuntimeLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmRuntimeLimits.MaxBoolRegisters];
        Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
        var cursor = new GraphVmExecutionCursor();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long elapsedTicks = Stopwatch.GetTimestamp();
        GraphVmExecutionResult result;
        do
        {
            result = GraphVmExecutor.ExecuteSlice(
                compiled.Program,
                ints,
                bools,
                callStack,
                ref cursor,
                GraphVmRuntimeLimits.MaxInstructionsPerExecution);
        }
        while (!result.Halted);

        elapsedTicks = Stopwatch.GetTimestamp() - elapsedTicks;
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        double elapsedUs = elapsedTicks * 1_000_000.0 / Stopwatch.Frequency;

        Console.WriteLine($"GraphVM smoke: status={result.Status}, return={result.ReturnInt}, steps={result.Steps}, elapsedUs={elapsedUs:F3}, allocatedBytes={allocatedBytes}");
    }
}
