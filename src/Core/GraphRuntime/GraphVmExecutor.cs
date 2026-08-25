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

        /// <summary>
        /// Lifetime cumulative instruction count: total instructions executed by
        /// this cursor since the last <see cref="Reset"/>. Strictly monotonic —
        /// it grows across Yield/resume slices and is never reset at a Yield,
        /// so <see cref="GraphVmExecutionResult.Steps"/> and trace Step values
        /// stay cumulative. Cleared only by <see cref="Reset"/>.
        /// </summary>
        public int Steps;

        /// <summary>
        /// Instructions executed in the current contiguous segment — from the
        /// last <see cref="GraphVmOpcode.Yield"/> (or from start/resume) until
        /// the next Yield or Halt. Reset to zero at every Yield so long-lived
        /// yielding coroutines are never budget-capped across their lifetime.
        /// Guarded by <see cref="GraphVmRuntimeLimits.MaxInstructionsBetweenYields"/>:
        /// a segment that reaches the budget without yielding fails closed.
        /// </summary>
        public int StepsSinceYield;
        public int CallStackCount;
        public int ReturnInt;
        public GraphVmExecutionStatus Status;

        public void Reset()
        {
            Pc = 0;
            Steps = 0;
            StepsSinceYield = 0;
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

            if (result.Status == GraphVmExecutionStatus.Yielded)
            {
                throw new InvalidOperationException(
                    "GraphVmExecutor.Execute cannot resume a program that yielded: it uses a stack-local " +
                    "register span and call stack, so a Yielded result is not resumable. " +
                    $"Use {nameof(GraphVmExecutor.ExecuteSlice)} with a persistent cursor, register span, and call stack to resume across yields.");
            }

            return result;
        }

        /// <summary>
        /// Executes up to <paramref name="maxInstructionSteps"/> instructions,
        /// suspending at <see cref="GraphVmOpcode.Yield"/> with
        /// <see cref="GraphVmExecutionStatus.Yielded"/> or completing with
        /// <see cref="GraphVmExecutionStatus.Halted"/>.
        /// <para>
        /// A Yielded (or Running) result must be resumed by calling this method
        /// again with the <em>same</em> <paramref name="ints"/>,
        /// <paramref name="bools"/> and <paramref name="callStack"/> spans that
        /// were passed for the suspended slice. The executor cannot detect span
        /// identity — registers and the call stack live in caller memory, so
        /// passing different spans silently loses state. A Yielded result from a
        /// convenience <see cref="Execute"/> overload is not resumable and is
        /// rejected there instead.
        /// </para>
        /// </summary>
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

            if (cursor.Steps < 0)
            {
                throw new InvalidOperationException($"GraphVM cursor step counter is negative: {cursor.Steps}.");
            }

            if (cursor.StepsSinceYield < 0)
            {
                throw new InvalidOperationException($"GraphVM cursor between-yields step counter is negative: {cursor.StepsSinceYield}.");
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
                // Check the between-yields budget before executing the next
                // instruction: reaching the budget without a Yield means the
                // current segment is a non-terminating loop. Checking before
                // incrementing (instead of after) keeps the counter from ever
                // overflowing past the budget.
                if (cursor.StepsSinceYield >= GraphVmRuntimeLimits.MaxInstructionsBetweenYields)
                {
                    throw new InvalidOperationException(
                        $"GraphVM exceeded instruction budget MaxInstructionsBetweenYields " +
                        $"({GraphVmRuntimeLimits.MaxInstructionsBetweenYields}) between yields; " +
                        "the current segment does not terminate.");
                }

                if ((uint)pc >= (uint)program.Length)
                {
                    throw new InvalidOperationException($"GraphVM PC left program without ReturnInt: pc={pc}, len={program.Length}.");
                }

                int instructionIndex = pc;
                ref readonly GraphInstruction instruction = ref program[instructionIndex];
                pc++;
                cursor.Steps++;
                cursor.StepsSinceYield++;
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

                        // Validate the popped return address like any other
                        // control target: a corrupted call stack slot must fail
                        // closed instead of running with an out-of-bounds pc.
                        pc = RequireInstructionTarget(program, callStack[--cursor.CallStackCount]);
                        break;
                    case GraphVmOpcode.Yield:
                    {
                        cursor.Pc = pc;
                        cursor.Status = GraphVmExecutionStatus.Yielded;
                        // Trace Step and result Steps report the lifetime
                        // cumulative count; only the segment budget counter is
                        // reset, so a long-lived yielding coroutine is never
                        // budget-capped across its lifetime.
                        traceSink?.OnInstruction(new GraphVmTraceEvent(cursor.Steps - 1, instructionIndex, instruction.Op, pc));
                        int yieldedSteps = cursor.Steps;
                        cursor.StepsSinceYield = 0;
                        return new GraphVmExecutionResult(GraphVmExecutionStatus.Yielded, cursor.ReturnInt, yieldedSteps);
                    }
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
