using BenchmarkDotNet.Attributes;
using Ludots.Core.GraphRuntime;

namespace Ludots.Benchmarks.GraphRuntime;

[MemoryDiagnoser]
public class GraphVmBenchmarks
{
    private const int EntityCount = 50_000;
    private const int RegistryLookupIterations = 10_000;
    private const int IntStride = GraphVmRuntimeLimits.MaxIntRegisters;
    private const int BoolStride = GraphVmRuntimeLimits.MaxBoolRegisters;
    private const int CallStackStride = GraphVmRuntimeLimits.MaxCallStackDepth;

    private GraphVmDocument _loopDocument = null!;
    private GraphVmDocument _asyncDocument = null!;
    private GraphInstruction[] _loopProgram = Array.Empty<GraphInstruction>();
    private GraphInstruction[] _asyncProgram = Array.Empty<GraphInstruction>();
    private GraphInstructionSourceMap _asyncSourceMap;
    private GraphProgramRegistry _registry = null!;
    private int[] _ints = Array.Empty<int>();
    private byte[] _bools = Array.Empty<byte>();
    private int[] _callStacks = Array.Empty<int>();
    private GraphVmExecutionCursor[] _cursors = Array.Empty<GraphVmExecutionCursor>();
    private FixedTraceSink _traceSink = null!;
    private int _sink;

    [GlobalSetup]
    public void Setup()
    {
        _loopDocument = GraphVmBenchmarkGraphs.CreateCountedLoopGraph(limit: 32);
        _asyncDocument = GraphVmBenchmarkGraphs.CreateDrinkUntilFullGraph(limit: 3);

        GraphVmCompileResult loopCompiled = GraphVmCompiler.Compile(_loopDocument);
        RequireCompiled(loopCompiled);
        _loopProgram = loopCompiled.Program;

        GraphVmCompileResult asyncCompiled = GraphVmCompiler.Compile(_asyncDocument);
        RequireCompiled(asyncCompiled);
        _asyncProgram = asyncCompiled.Program;
        _asyncSourceMap = asyncCompiled.SourceMap;

        _registry = new GraphProgramRegistry();
        _registry.Register(1, _asyncProgram, GraphKind.Effect, _asyncSourceMap);

        _ints = new int[EntityCount * IntStride];
        _bools = new byte[EntityCount * BoolStride];
        _callStacks = new int[EntityCount * CallStackStride];
        _cursors = new GraphVmExecutionCursor[EntityCount];
        _traceSink = new FixedTraceSink(_asyncSourceMap, 512);
    }

    [Benchmark(Baseline = true)]
    public int ExecuteLoop_OneEntity_NoTrace()
    {
        Span<int> ints = stackalloc int[IntStride];
        Span<byte> bools = stackalloc byte[BoolStride];
        Span<int> callStack = stackalloc int[CallStackStride];
        var cursor = new GraphVmExecutionCursor();

        GraphVmExecutionResult result = GraphVmExecutor.ExecuteSlice(
            _loopProgram,
            ints,
            bools,
            callStack,
            ref cursor,
            GraphVmRuntimeLimits.MaxInstructionsPerExecution);

        _sink = result.ReturnInt;
        return _sink;
    }

    [Benchmark]
    public int ExecuteAsyncFunction_ResumeToHalt_NoTrace()
    {
        Span<int> ints = stackalloc int[IntStride];
        Span<byte> bools = stackalloc byte[BoolStride];
        Span<int> callStack = stackalloc int[CallStackStride];
        var cursor = new GraphVmExecutionCursor();
        GraphVmExecutionResult result;

        do
        {
            result = GraphVmExecutor.ExecuteSlice(
                _asyncProgram,
                ints,
                bools,
                callStack,
                ref cursor,
                GraphVmRuntimeLimits.MaxInstructionsPerExecution);
        }
        while (!result.Halted);

        _sink = result.ReturnInt;
        return _sink;
    }

    [Benchmark]
    public int ExecuteAsyncFunction_ResumeToHalt_WithSourceMapTrace()
    {
        Span<int> ints = stackalloc int[IntStride];
        Span<byte> bools = stackalloc byte[BoolStride];
        Span<int> callStack = stackalloc int[CallStackStride];
        var cursor = new GraphVmExecutionCursor();
        GraphVmExecutionResult result;

        _traceSink.Reset();
        do
        {
            result = GraphVmExecutor.ExecuteSlice(
                _asyncProgram,
                ints,
                bools,
                callStack,
                ref cursor,
                GraphVmRuntimeLimits.MaxInstructionsPerExecution,
                _traceSink);
        }
        while (!result.Halted);

        _sink = result.ReturnInt + _traceSink.Count;
        return _sink;
    }

    [Benchmark]
    public int ExecuteLoop_50000Entities_NoTrace()
    {
        int sum = 0;
        for (int entity = 0; entity < EntityCount; entity++)
        {
            Span<int> ints = _ints.AsSpan(entity * IntStride, IntStride);
            Span<byte> bools = _bools.AsSpan(entity * BoolStride, BoolStride);
            Span<int> callStack = _callStacks.AsSpan(entity * CallStackStride, CallStackStride);

            _cursors[entity].Reset();
            GraphVmExecutionCursor cursor = _cursors[entity];
            GraphVmExecutionResult result = GraphVmExecutor.ExecuteSlice(
                _loopProgram,
                ints,
                bools,
                callStack,
                ref cursor,
                GraphVmRuntimeLimits.MaxInstructionsPerExecution);
            _cursors[entity] = cursor;
            sum += result.ReturnInt;
        }

        _sink = sum;
        return _sink;
    }

    [Benchmark]
    public int ExecuteAsyncFunction_50000Entities_OneTick()
    {
        int yielded = 0;
        for (int entity = 0; entity < EntityCount; entity++)
        {
            Span<int> ints = _ints.AsSpan(entity * IntStride, IntStride);
            Span<byte> bools = _bools.AsSpan(entity * BoolStride, BoolStride);
            Span<int> callStack = _callStacks.AsSpan(entity * CallStackStride, CallStackStride);

            _cursors[entity].Reset();
            GraphVmExecutionCursor cursor = _cursors[entity];
            GraphVmExecutionResult result = GraphVmExecutor.ExecuteSlice(
                _asyncProgram,
                ints,
                bools,
                callStack,
                ref cursor,
                GraphVmRuntimeLimits.MaxInstructionsPerExecution);
            _cursors[entity] = cursor;
            yielded += result.Yielded ? 1 : 0;
        }

        _sink = yielded;
        return _sink;
    }

    [Benchmark]
    public int CompileAsyncControlFlowGraph()
    {
        GraphVmCompileResult result = GraphVmCompiler.Compile(_asyncDocument);
        _sink = result.Program.Length + result.SourceMap.Sources.Length;
        return _sink;
    }

    [Benchmark]
    public int RegistryLookup_ProgramAndSourceMap()
    {
        int count = 0;
        for (int i = 0; i < RegistryLookupIterations; i++)
        {
            if (_registry.TryGetProgram(1, out ReadOnlySpan<GraphInstruction> program))
            {
                count += program.Length;
            }

            if (_registry.TryGetSourceMap(1, out GraphInstructionSourceMap sourceMap))
            {
                count += sourceMap.Sources.Length;
            }
        }

        _sink = count;
        return _sink;
    }

    private static void RequireCompiled(GraphVmCompileResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Select(d =>
            $"{d.Code}:{d.NodeId}:{d.Message}")));
    }

    private sealed class FixedTraceSink : IGraphVmTraceSink
    {
        private readonly GraphInstructionSourceMap _sourceMap;
        private readonly GraphInstructionSource[] _buffer;

        public FixedTraceSink(GraphInstructionSourceMap sourceMap, int capacity)
        {
            _sourceMap = sourceMap;
            _buffer = new GraphInstructionSource[capacity];
        }

        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public void OnInstruction(in GraphVmTraceEvent traceEvent)
        {
            if (Count >= _buffer.Length)
            {
                throw new InvalidOperationException("GraphVM trace benchmark buffer overflow.");
            }

            if (!_sourceMap.TryGetSource(traceEvent.InstructionIndex, out GraphInstructionSource source))
            {
                throw new InvalidOperationException($"GraphVM source map missed instruction {traceEvent.InstructionIndex}.");
            }

            _buffer[Count++] = source;
        }
    }
}
