using System;

namespace Ludots.Core.GraphRuntime
{
    public readonly record struct GraphVmExecutionResult
    {
        public GraphVmExecutionResult(bool halted, int returnInt, int steps)
            : this(halted ? GraphVmExecutionStatus.Halted : GraphVmExecutionStatus.Running, returnInt, steps)
        {
        }

        public GraphVmExecutionResult(GraphVmExecutionStatus status, int returnInt, int steps)
        {
            Status = status;
            ReturnInt = returnInt;
            Steps = steps;
        }

        public GraphVmExecutionStatus Status { get; }
        public bool Halted => Status == GraphVmExecutionStatus.Halted;
        public bool Yielded => Status == GraphVmExecutionStatus.Yielded;
        public int ReturnInt { get; }
        public int Steps { get; }
    }

    public struct GraphVmExecutionCursor
    {
        public int Pc;
        public int Steps;
        public int CallStackCount;
        public int ReturnInt;
        public GraphVmExecutionStatus Status;

        public void Reset()
        {
            Pc = 0;
            Steps = 0;
            CallStackCount = 0;
            ReturnInt = 0;
            Status = GraphVmExecutionStatus.Running;
        }
    }

    public readonly record struct GraphVmTraceEvent
    {
        public GraphVmTraceEvent(int step, int instructionIndex, ushort op)
            : this(step, instructionIndex, op, instructionIndex + 1)
        {
        }

        public GraphVmTraceEvent(int step, int instructionIndex, ushort op, int nextInstructionIndex)
        {
            Step = step;
            InstructionIndex = instructionIndex;
            Op = op;
            NextInstructionIndex = nextInstructionIndex;
        }

        public int Step { get; }
        public int InstructionIndex { get; }
        public ushort Op { get; }
        public int NextInstructionIndex { get; }
    }

    public interface IGraphVmTraceSink
    {
        void OnInstruction(in GraphVmTraceEvent traceEvent);
    }

    public static class GraphVmExecutor
    {
        public static GraphVmExecutionResult Execute(
            ReadOnlySpan<GraphInstruction> program,
            IGraphVmTraceSink? traceSink = null)
        {
            Span<int> ints = stackalloc int[GraphVmRuntimeLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmRuntimeLimits.MaxBoolRegisters];
            Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
            return Execute(program, ints, bools, callStack, traceSink);
        }

        public static GraphVmExecutionResult Execute(
            ReadOnlySpan<GraphInstruction> program,
            Span<int> ints,
            Span<byte> bools,
            IGraphVmTraceSink? traceSink = null)
        {
            Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
            return Execute(program, ints, bools, callStack, traceSink);
        }

        public static GraphVmExecutionResult Execute(
            ReadOnlySpan<GraphInstruction> program,
            Span<int> ints,
            Span<byte> bools,
            Span<int> callStack,
            IGraphVmTraceSink? traceSink = null)
        {
            var cursor = new GraphVmExecutionCursor();
            GraphVmExecutionResult result = ExecuteSlice(
                program,
                ints,
                bools,
                callStack,
                ref cursor,
                GraphVmRuntimeLimits.MaxInstructionsPerExecution,
                traceSink);

            if (result.Status == GraphVmExecutionStatus.Running)
            {
                throw new InvalidOperationException(
                    $"GraphVM exceeded MaxInstructionsPerExecution ({GraphVmRuntimeLimits.MaxInstructionsPerExecution}).");
            }

            return result;
        }

        public static GraphVmExecutionResult ExecuteSlice(
            ReadOnlySpan<GraphInstruction> program,
            Span<int> ints,
            Span<byte> bools,
            Span<int> callStack,
            ref GraphVmExecutionCursor cursor,
            int maxInstructionSteps,
            IGraphVmTraceSink? traceSink = null)
        {
            if (program.Length == 0)
            {
                throw new InvalidOperationException("GraphVM cannot execute an empty program.");
            }

            if (ints.Length < GraphVmRuntimeLimits.MaxIntRegisters)
            {
                throw new ArgumentException("GraphVM int register span is smaller than the runtime contract.", nameof(ints));
            }

            if (bools.Length < GraphVmRuntimeLimits.MaxBoolRegisters)
            {
                throw new ArgumentException("GraphVM bool register span is smaller than the runtime contract.", nameof(bools));
            }

            if (callStack.Length < GraphVmRuntimeLimits.MaxCallStackDepth)
            {
                throw new ArgumentException("GraphVM call stack span is smaller than the runtime contract.", nameof(callStack));
            }

            if (maxInstructionSteps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxInstructionSteps));
            }

            if (cursor.Status == GraphVmExecutionStatus.Halted)
            {
                return new GraphVmExecutionResult(GraphVmExecutionStatus.Halted, cursor.ReturnInt, cursor.Steps);
            }

            if (cursor.CallStackCount < 0 || cursor.CallStackCount > callStack.Length)
            {
                throw new InvalidOperationException($"GraphVM call stack count out of range: {cursor.CallStackCount}.");
            }

            cursor.Status = GraphVmExecutionStatus.Running;

            int pc = cursor.Pc;
            int stepsThisSlice = 0;

            while (stepsThisSlice < maxInstructionSteps)
            {
                if ((uint)pc >= (uint)program.Length)
                {
                    throw new InvalidOperationException($"GraphVM PC left program without ReturnInt: pc={pc}, len={program.Length}.");
                }

                int instructionIndex = pc;
                ref readonly GraphInstruction instruction = ref program[instructionIndex];
                pc++;
                cursor.Steps++;
                stepsThisSlice++;

                switch ((GraphVmOpcode)instruction.Op)
                {
                    case GraphVmOpcode.Nop:
                        break;
                    case GraphVmOpcode.ConstInt:
                        ints[RequireIntRegister(instruction.Dst)] = instruction.Imm;
                        break;
                    case GraphVmOpcode.LoadInt:
                        ints[RequireIntRegister(instruction.Dst)] = ints[RequireIntRegister(instruction.A)];
                        break;
                    case GraphVmOpcode.StoreInt:
                        ints[RequireIntRegister(instruction.Dst)] = ints[RequireIntRegister(instruction.A)];
                        break;
                    case GraphVmOpcode.AddInt:
                        ints[RequireIntRegister(instruction.Dst)] =
                            ints[RequireIntRegister(instruction.A)] + ints[RequireIntRegister(instruction.B)];
                        break;
                    case GraphVmOpcode.LessThanInt:
                        bools[RequireBoolRegister(instruction.Dst)] =
                            (byte)(ints[RequireIntRegister(instruction.A)] < ints[RequireIntRegister(instruction.B)] ? 1 : 0);
                        break;
                    case GraphVmOpcode.Jump:
                        pc = RequireInstructionTarget(program, instruction.Imm);
                        break;
                    case GraphVmOpcode.JumpIfFalse:
                        if (bools[RequireBoolRegister(instruction.A)] == 0)
                        {
                            pc = RequireInstructionTarget(program, instruction.Imm);
                        }
                        break;
                    case GraphVmOpcode.BranchBool:
                        throw new InvalidOperationException("GraphVM BranchBool must be lowered by GraphVmCompiler before execution.");
                    case GraphVmOpcode.Call:
                        if (cursor.CallStackCount >= GraphVmRuntimeLimits.MaxCallStackDepth)
                        {
                            throw new InvalidOperationException(
                                $"GraphVM call stack exceeded MaxCallStackDepth ({GraphVmRuntimeLimits.MaxCallStackDepth}).");
                        }

                        callStack[cursor.CallStackCount++] = pc;
                        pc = RequireInstructionTarget(program, instruction.Imm);
                        break;
                    case GraphVmOpcode.Return:
                        if (cursor.CallStackCount == 0)
                        {
                            throw new InvalidOperationException("GraphVM Return executed with an empty call stack.");
                        }

                        pc = callStack[--cursor.CallStackCount];
                        break;
                    case GraphVmOpcode.Yield:
                        cursor.Pc = pc;
                        cursor.Status = GraphVmExecutionStatus.Yielded;
                        traceSink?.OnInstruction(new GraphVmTraceEvent(cursor.Steps - 1, instructionIndex, instruction.Op, pc));
                        return new GraphVmExecutionResult(GraphVmExecutionStatus.Yielded, cursor.ReturnInt, cursor.Steps);
                    case GraphVmOpcode.ReturnInt:
                        cursor.Pc = pc;
                        cursor.ReturnInt = ints[RequireIntRegister(instruction.A)];
                        cursor.Status = GraphVmExecutionStatus.Halted;
                        traceSink?.OnInstruction(new GraphVmTraceEvent(cursor.Steps - 1, instructionIndex, instruction.Op, pc));
                        return new GraphVmExecutionResult(GraphVmExecutionStatus.Halted, cursor.ReturnInt, cursor.Steps);
                    default:
                        throw new InvalidOperationException($"GraphVM op {instruction.Op} is not registered.");
                }

                traceSink?.OnInstruction(new GraphVmTraceEvent(cursor.Steps - 1, instructionIndex, instruction.Op, pc));
            }

            cursor.Pc = pc;
            cursor.Status = GraphVmExecutionStatus.Running;
            return new GraphVmExecutionResult(GraphVmExecutionStatus.Running, cursor.ReturnInt, cursor.Steps);
        }

        private static int RequireIntRegister(byte register)
        {
            if (register >= GraphVmRuntimeLimits.MaxIntRegisters)
            {
                throw new InvalidOperationException($"GraphVM int register out of range: {register}.");
            }

            return register;
        }

        private static int RequireBoolRegister(byte register)
        {
            if (register >= GraphVmRuntimeLimits.MaxBoolRegisters)
            {
                throw new InvalidOperationException($"GraphVM bool register out of range: {register}.");
            }

            return register;
        }

        private static int RequireInstructionTarget(ReadOnlySpan<GraphInstruction> program, int target)
        {
            if ((uint)target >= (uint)program.Length)
            {
                throw new InvalidOperationException($"GraphVM jump target out of range: {target}.");
            }

            return target;
        }
    }
}
