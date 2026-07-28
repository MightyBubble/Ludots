using System;
using Ludots.Core.GraphRuntime;

namespace GraphAiShowcaseCommon;

public enum GraphAiOp : ushort
{
    None = 0,
    ConstInt = 1,
    CopyInt = 2,
    AddInt = 3,
    DecrementPositive = 4,
    CompareLtIntImm = 10,
    CompareGtIntImm = 11,
    CompareEqIntImm = 12,
    Jump = 20,
    JumpIfFalse = 21
}

public struct GraphAiVmState
{
    public int[] I;
    public byte[] B;

    public static GraphAiVmState Create() =>
        new()
        {
            I = new int[GraphAiVmLimits.IntRegisters],
            B = new byte[GraphAiVmLimits.BoolRegisters]
        };

    public static GraphAiVmState Create(int[] intRegisters, byte[] boolRegisters)
    {
        if (intRegisters == null || intRegisters.Length < GraphAiVmLimits.IntRegisters)
        {
            throw new ArgumentException($"Graph AI VM requires {GraphAiVmLimits.IntRegisters} int registers.", nameof(intRegisters));
        }

        if (boolRegisters == null || boolRegisters.Length < GraphAiVmLimits.BoolRegisters)
        {
            throw new ArgumentException($"Graph AI VM requires {GraphAiVmLimits.BoolRegisters} bool registers.", nameof(boolRegisters));
        }

        return new GraphAiVmState { I = intRegisters, B = boolRegisters };
    }
}

public static class GraphAiVmLimits
{
    public const int IntRegisters = 24;
    public const int BoolRegisters = 8;
    public const int HandlerTableSize = 64;
}

public sealed class GraphAiOpHandlerTable : IOpHandlerTable<GraphAiVmState>
{
    public static readonly GraphAiOpHandlerTable Instance = new();

    public GraphOpHandler<GraphAiVmState>[] Handlers { get; }

    private GraphAiOpHandlerTable()
    {
        var handlers = new GraphOpHandler<GraphAiVmState>[GraphAiVmLimits.HandlerTableSize];
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

    private static void HandleConstInt(ref GraphAiVmState state, in GraphInstruction ins, ref int pc)
    {
        state.I[ins.Dst] = ins.Imm;
    }

    private static void HandleCopyInt(ref GraphAiVmState state, in GraphInstruction ins, ref int pc)
    {
        state.I[ins.Dst] = state.I[ins.A];
    }

    private static void HandleAddInt(ref GraphAiVmState state, in GraphInstruction ins, ref int pc)
    {
        state.I[ins.Dst] = state.I[ins.A] + state.I[ins.B];
    }

    private static void HandleDecrementPositive(ref GraphAiVmState state, in GraphInstruction ins, ref int pc)
    {
        int value = state.I[ins.A];
        state.I[ins.Dst] = value > 0 ? value - 1 : 0;
    }

    private static void HandleCompareLtIntImm(ref GraphAiVmState state, in GraphInstruction ins, ref int pc)
    {
        state.B[ins.Dst] = (byte)(state.I[ins.A] < ins.Imm ? 1 : 0);
    }

    private static void HandleCompareGtIntImm(ref GraphAiVmState state, in GraphInstruction ins, ref int pc)
    {
        state.B[ins.Dst] = (byte)(state.I[ins.A] > ins.Imm ? 1 : 0);
    }

    private static void HandleCompareEqIntImm(ref GraphAiVmState state, in GraphInstruction ins, ref int pc)
    {
        state.B[ins.Dst] = (byte)(state.I[ins.A] == ins.Imm ? 1 : 0);
    }

    private static void HandleJump(ref GraphAiVmState state, in GraphInstruction ins, ref int pc)
    {
        pc += ins.Imm;
    }

    private static void HandleJumpIfFalse(ref GraphAiVmState state, in GraphInstruction ins, ref int pc)
    {
        if (state.B[ins.A] == 0)
        {
            pc += ins.Imm;
        }
    }
}

public static class GraphAiProgramCompiler
{
    public static GraphInstruction[] Compile(GraphAiProgramConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (string.IsNullOrWhiteSpace(config.Id))
        {
            throw new InvalidOperationException("Graph AI program requires a non-empty id.");
        }

        if (config.Instructions.Count == 0)
        {
            throw new InvalidOperationException($"Graph AI program '{config.Id}' requires at least one instruction.");
        }

        var program = new GraphInstruction[config.Instructions.Count];
        for (int i = 0; i < config.Instructions.Count; i++)
        {
            GraphAiInstructionConfig source = config.Instructions[i];
            if (!Enum.TryParse(source.Op, ignoreCase: false, out GraphAiOp op) || op == GraphAiOp.None)
            {
                throw new InvalidOperationException($"Graph AI program '{config.Id}' instruction[{i}] has unsupported op '{source.Op}'.");
            }

            ValidateInstruction(op, source, config.Id, i, config.Instructions.Count);
            program[i] = new GraphInstruction
            {
                Op = (ushort)op,
                Dst = checked((byte)source.Dst),
                A = checked((byte)source.A),
                B = checked((byte)source.B),
                C = checked((byte)source.C),
                Imm = source.Imm
            };
        }

        return program;
    }

    private static void ValidateInstruction(GraphAiOp op, GraphAiInstructionConfig source, string programId, int index, int instructionCount)
    {
        switch (op)
        {
            case GraphAiOp.ConstInt:
                ValidateIntRegister(source.Dst, nameof(source.Dst), programId, index);
                break;
            case GraphAiOp.CopyInt:
            case GraphAiOp.DecrementPositive:
                ValidateIntRegister(source.Dst, nameof(source.Dst), programId, index);
                ValidateIntRegister(source.A, nameof(source.A), programId, index);
                break;
            case GraphAiOp.AddInt:
                ValidateIntRegister(source.Dst, nameof(source.Dst), programId, index);
                ValidateIntRegister(source.A, nameof(source.A), programId, index);
                ValidateIntRegister(source.B, nameof(source.B), programId, index);
                break;
            case GraphAiOp.CompareLtIntImm:
            case GraphAiOp.CompareGtIntImm:
            case GraphAiOp.CompareEqIntImm:
                ValidateBoolRegister(source.Dst, nameof(source.Dst), programId, index);
                ValidateIntRegister(source.A, nameof(source.A), programId, index);
                break;
            case GraphAiOp.Jump:
                ValidateJump(source.Imm, programId, index, instructionCount);
                break;
            case GraphAiOp.JumpIfFalse:
                ValidateBoolRegister(source.A, nameof(source.A), programId, index);
                ValidateJump(source.Imm, programId, index, instructionCount);
                break;
            default:
                throw new InvalidOperationException($"Graph AI program '{programId}' instruction[{index}] has unsupported op '{op}'.");
        }
    }

    private static void ValidateIntRegister(int value, string field, string programId, int index)
    {
        if (value < 0 || value >= GraphAiVmLimits.IntRegisters)
        {
            throw new InvalidOperationException(
                $"Graph AI program '{programId}' instruction[{index}] {field} register {value} is outside 0..{GraphAiVmLimits.IntRegisters - 1}.");
        }
    }

    private static void ValidateBoolRegister(int value, string field, string programId, int index)
    {
        if (value < 0 || value >= GraphAiVmLimits.BoolRegisters)
        {
            throw new InvalidOperationException(
                $"Graph AI program '{programId}' instruction[{index}] {field} bool register {value} is outside 0..{GraphAiVmLimits.BoolRegisters - 1}.");
        }
    }

    private static void ValidateJump(int relativeOffset, string programId, int index, int instructionCount)
    {
        int target = index + 1 + relativeOffset;
        if (target < 0 || target > instructionCount)
        {
            throw new InvalidOperationException(
                $"Graph AI program '{programId}' instruction[{index}] jump target {target} is outside 0..{instructionCount}.");
        }
    }
}
