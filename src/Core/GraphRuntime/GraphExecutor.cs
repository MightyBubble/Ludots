using System;

namespace Ludots.Core.GraphRuntime
{
    public delegate void GraphOpHandler<TState>(ref TState state, in GraphInstruction ins, ref int pc);

    public interface IOpHandlerTable<TState>
    {
        GraphOpHandler<TState>[] Handlers { get; }
    }

    public static class GraphExecutor
    {
        public static void Execute<TState>(ref TState state, ReadOnlySpan<GraphInstruction> program, IOpHandlerTable<TState> handlers)
        {
            var table = handlers.Handlers;
            int pc = 0;
            while ((uint)pc < (uint)program.Length)
            {
                ref readonly var ins = ref program[pc];
                pc++;

                var handler = table[ins.Op];
                if (handler == null)
                {
                    if (ins.Op != 0)
                    {
                        throw new InvalidOperationException($"No handler registered for graph op {ins.Op}.");
                    }
                    continue;
                }

                handler(ref state, in ins, ref pc);
            }
        }
    }
}

