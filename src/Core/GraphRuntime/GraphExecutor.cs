using System;

namespace Ludots.Core.GraphRuntime
{
    public delegate void GraphOpHandler<TState>(ref TState state, in GraphInstruction instruction, ref int pc);

    public interface IOpHandlerTable<TState>
    {
        GraphOpHandler<TState>[] Handlers { get; }
    }

    public static class GraphRuntimeLimits
    {
        public const int MaxInstructionsPerExecution = 4096;
    }

    public static class GraphExecutor
    {
        public static void Execute<TState>(
            ref TState state,
            ReadOnlySpan<GraphInstruction> program,
            IOpHandlerTable<TState> handlers,
            int maxInstructions = GraphRuntimeLimits.MaxInstructionsPerExecution)
        {
            if (handlers == null)
            {
                throw new ArgumentNullException(nameof(handlers));
            }

            Execute(ref state, program, handlers.Handlers, maxInstructions);
        }

        public static void Execute<TState>(
            ref TState state,
            ReadOnlySpan<GraphInstruction> program,
            GraphOpHandler<TState>[] handlers,
            int maxInstructions = GraphRuntimeLimits.MaxInstructionsPerExecution)
        {
            if (handlers == null)
            {
                throw new ArgumentNullException(nameof(handlers));
            }

            if (maxInstructions <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxInstructions), maxInstructions, "Graph VM instruction budget must be positive.");
            }

            int pc = 0;
            int steps = 0;

            while ((uint)pc < (uint)program.Length)
            {
                if (++steps > maxInstructions)
                {
                    throw new InvalidOperationException(
                        $"Graph VM exceeded MaxInstructionsPerExecution ({maxInstructions}). Possible infinite loop.");
                }

                ref readonly GraphInstruction instruction = ref program[pc];
                pc++;

                if (instruction.Op == 0)
                {
                    continue;
                }

                if (instruction.Op >= handlers.Length)
                {
                    throw new InvalidOperationException(
                        $"Graph op {instruction.Op} exceeds handler table capacity ({handlers.Length}).");
                }

                GraphOpHandler<TState>? handler = handlers[instruction.Op];
                if (handler == null)
                {
                    throw new InvalidOperationException(
                        $"No handler registered for graph op {instruction.Op}.");
                }

                handler(ref state, in instruction, ref pc);

                if (pc < 0 || pc > program.Length)
                {
                    throw new InvalidOperationException(
                        $"Graph handler for op {instruction.Op} moved pc to {pc}, outside 0..{program.Length}.");
                }
            }
        }
    }
}
