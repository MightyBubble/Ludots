using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ludots.Core.GraphRuntime;

namespace GraphAiShowcaseCommon;

public sealed class GraphAiHotPathProbe
{
    private readonly GraphInstruction[] _program;
    private readonly int _entityCount;
    private readonly int[] _intRegisters;
    private readonly byte[] _boolRegisters;
    private long _totalGraphExecutions;

    public GraphAiHotPathProbe(GraphInstruction[] program, int entityCount)
    {
        _program = program ?? throw new ArgumentNullException(nameof(program));
        if (_program.Length == 0)
        {
            throw new ArgumentException("Graph AI hot path probe requires a non-empty program.", nameof(program));
        }

        if (entityCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entityCount), entityCount, "Graph AI hot path entity count must be positive.");
        }

        _entityCount = entityCount;
        _intRegisters = new int[checked(entityCount * GraphAiVmLimits.IntRegisters)];
        _boolRegisters = new byte[checked(entityCount * GraphAiVmLimits.BoolRegisters)];
        SeedAll();
        RunHotPath(0);
        Snapshot = GraphAiHotPathSnapshot.Empty;
    }

    public GraphAiHotPathSnapshot Snapshot { get; private set; }

    public void Update(int tick)
    {
        int beforeGen0 = GC.CollectionCount(0);
        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();

        int checksum = RunHotPath(tick);

        long stop = Stopwatch.GetTimestamp();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
        int gen0Collections = GC.CollectionCount(0) - beforeGen0;
        long graphExecutions = _entityCount;
        _totalGraphExecutions += graphExecutions;
        long elapsedMicros = (stop - start) * 1_000_000L / Stopwatch.Frequency;

        Snapshot = new GraphAiHotPathSnapshot(
            _entityCount,
            graphExecutions,
            _totalGraphExecutions,
            elapsedMicros,
            allocatedBytes,
            gen0Collections,
            checksum);
    }

    private int RunHotPath(int tick)
    {
        int checksum = 0;
        for (int index = 0; index < _entityCount; index++)
        {
            int intBase = index * GraphAiVmLimits.IntRegisters;
            int boolBase = index * GraphAiVmLimits.BoolRegisters;
            SeedSenses(_intRegisters, intBase, index);
            _intRegisters[intBase] = tick;

            var state = new GraphAiSoaVmState(_intRegisters, _boolRegisters, intBase, boolBase);
            GraphExecutor.Execute(ref state, _program, GraphAiSoaOpHandlerTable.Instance);

            int nextState = _intRegisters[intBase + 10];
            _intRegisters[intBase + 1] = nextState;
            _intRegisters[intBase + 6] = _intRegisters[intBase + 12];
            checksum = unchecked(checksum + nextState + _intRegisters[intBase + 11] + _intRegisters[intBase + 13]);
        }

        return checksum;
    }

    private void SeedAll()
    {
        for (int index = 0; index < _entityCount; index++)
        {
            int intBase = index * GraphAiVmLimits.IntRegisters;
            SeedSenses(_intRegisters, intBase, index);
            _intRegisters[intBase + 1] = index & 3;
            _intRegisters[intBase + 6] = index & 1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SeedSenses(int[] registers, int baseIndex, int index)
    {
        int profile = index & 3;
        if (profile == 0)
        {
            registers[baseIndex + 2] = 240;
            registers[baseIndex + 3] = 86;
            registers[baseIndex + 4] = 60;
            return;
        }

        if (profile == 1)
        {
            registers[baseIndex + 2] = 760;
            registers[baseIndex + 3] = 92;
            registers[baseIndex + 4] = 94;
            return;
        }

        if (profile == 2)
        {
            registers[baseIndex + 2] = 180;
            registers[baseIndex + 3] = 24;
            registers[baseIndex + 4] = 48;
            return;
        }

        registers[baseIndex + 2] = 900;
        registers[baseIndex + 3] = 88;
        registers[baseIndex + 4] = 52;
    }
}

internal readonly struct GraphAiSoaVmState
{
    public GraphAiSoaVmState(int[] intRegisters, byte[] boolRegisters, int intBase, int boolBase)
    {
        I = intRegisters;
        B = boolRegisters;
        IntBase = intBase;
        BoolBase = boolBase;
    }

    public readonly int[] I;
    public readonly byte[] B;
    public readonly int IntBase;
    public readonly int BoolBase;
}

internal sealed class GraphAiSoaOpHandlerTable : IOpHandlerTable<GraphAiSoaVmState>
{
    public static readonly GraphAiSoaOpHandlerTable Instance = new();

    public GraphOpHandler<GraphAiSoaVmState>[] Handlers { get; }

    private GraphAiSoaOpHandlerTable()
    {
        var handlers = new GraphOpHandler<GraphAiSoaVmState>[GraphAiVmLimits.HandlerTableSize];
        handlers[(ushort)GraphAiOp.ConstInt] = HandleConstInt;
        handlers[(ushort)GraphAiOp.CopyInt] = HandleCopyInt;
        handlers[(ushort)GraphAiOp.AddInt] = HandleAddInt;
        handlers[(ushort)GraphAiOp.DecrementPositive] = HandleDecrementPositive;
        handlers[(ushort)GraphAiOp.CompareLtIntImm] = HandleCompareLtIntImm;
        handlers[(ushort)GraphAiOp.CompareGtIntImm] = HandleCompareGtIntImm;
        handlers[(ushort)GraphAiOp.CompareEqIntImm] = HandleCompareEqIntImm;
        handlers[(ushort)GraphAiOp.Jump] = HandleJump;
        handlers[(ushort)GraphAiOp.JumpIfFalse] = HandleJumpIfFalse;
        Handlers = handlers;
    }

    private static void HandleConstInt(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
    {
        state.I[state.IntBase + ins.Dst] = ins.Imm;
    }

    private static void HandleCopyInt(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
    {
        state.I[state.IntBase + ins.Dst] = state.I[state.IntBase + ins.A];
    }

    private static void HandleAddInt(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
    {
        state.I[state.IntBase + ins.Dst] = state.I[state.IntBase + ins.A] + state.I[state.IntBase + ins.B];
    }

    private static void HandleDecrementPositive(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
    {
        int value = state.I[state.IntBase + ins.A];
        state.I[state.IntBase + ins.Dst] = value > 0 ? value - 1 : 0;
    }

    private static void HandleCompareLtIntImm(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
    {
        state.B[state.BoolBase + ins.Dst] = (byte)(state.I[state.IntBase + ins.A] < ins.Imm ? 1 : 0);
    }

    private static void HandleCompareGtIntImm(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
    {
        state.B[state.BoolBase + ins.Dst] = (byte)(state.I[state.IntBase + ins.A] > ins.Imm ? 1 : 0);
    }

    private static void HandleCompareEqIntImm(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
    {
        state.B[state.BoolBase + ins.Dst] = (byte)(state.I[state.IntBase + ins.A] == ins.Imm ? 1 : 0);
    }

    private static void HandleJump(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
    {
        pc += ins.Imm;
    }

    private static void HandleJumpIfFalse(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
    {
        if (state.B[state.BoolBase + ins.A] == 0)
        {
            pc += ins.Imm;
        }
    }
}
