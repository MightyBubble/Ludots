using System;
using System.Collections.Generic;
using System.Text;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Longest host-path length of one TriggerGraph entry, in the same steps
    /// <see cref="GasGraphOpHandlerTable"/> charges to a slice.
    /// InvokeGraph is one step: the callee runs to halt and its steps land on the
    /// lifetime counter, not on stepsThisSlice. Call/Return stay inside the slice.
    /// A reachable Yield or a cycle cannot finish in the slice that started it.
    /// </summary>
    public static class TriggerGraphSliceBudget
    {
        public const int SingleSliceSteps = 64;

        public static int CountLongest(GraphInstruction[] program, int startPc)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            var walker = new Walker(program);
            return walker.Count(startPc);
        }

        public static void Ensure(int graphId, GraphInstruction[] program, TriggerGraphEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return;
            }

            string graphName = GraphIdRegistry.GetName(graphId);
            if (string.IsNullOrEmpty(graphName))
            {
                graphName = graphId.ToString();
            }

            for (int i = 0; i < entries.Length; i++)
            {
                string budget = entries[i].Budget ?? string.Empty;
                if (budget.Length == 0)
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(entries[i].Label) ? $"[{i}]" : entries[i].Label;
                if (!string.Equals(budget, TriggerGraphEntry.BudgetSingleSlice, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_TRIGGER_GRAPH_SINGLE_SLICE: graph '{graphName}' entry '{label}' budget '{budget}' is not '{TriggerGraphEntry.BudgetSingleSlice}'.");
                }

                int count;
                try
                {
                    count = CountLongest(program, entries[i].StartPc);
                }
                catch (SlicePathException ex)
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_TRIGGER_GRAPH_SINGLE_SLICE: graph '{graphName}' entry '{label}' {ex.Reason}, budget {TriggerGraphEntry.BudgetSingleSlice} allows {SingleSliceSteps} steps.");
                }

                if (count > SingleSliceSteps)
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_TRIGGER_GRAPH_SINGLE_SLICE: graph '{graphName}' entry '{label}' longest path is {count} steps, budget {TriggerGraphEntry.BudgetSingleSlice} allows {SingleSliceSteps}.");
                }
            }
        }

        private sealed class SlicePathException : Exception
        {
            public SlicePathException(string reason) : base(reason)
            {
                Reason = reason;
            }

            public string Reason { get; }
        }

        private sealed class Walker
        {
            private readonly GraphInstruction[] _program;
            private readonly Dictionary<string, int> _memo = new(StringComparer.Ordinal);
            private readonly HashSet<string> _stack = new(StringComparer.Ordinal);
            private readonly int[] _returns = new int[GraphVmLimits.MaxCallStackDepth];
            private readonly StringBuilder _key = new();

            public Walker(GraphInstruction[] program)
            {
                _program = program;
            }

            public int Count(int startPc) => Walk(startPc, 0);

            private int Walk(int pc, int depth)
            {
                if ((uint)pc >= (uint)_program.Length)
                {
                    return 0;
                }

                string key = Key(pc, depth);
                if (_memo.TryGetValue(key, out int cached))
                {
                    return cached;
                }

                if (!_stack.Add(key))
                {
                    throw new SlicePathException($"cycles at pc {pc}");
                }

                GraphInstruction ins = _program[pc];
                var op = (GraphNodeOp)ins.Op;
                int rest = op switch
                {
                    GraphNodeOp.HaltReturnInt or GraphNodeOp.Fail => 0,
                    GraphNodeOp.Jump => Walk(pc + 1 + ins.Imm, depth),
                    GraphNodeOp.JumpIfFalse => Math.Max(Walk(pc + 1, depth), Walk(pc + 1 + ins.Imm, depth)),
                    GraphNodeOp.Call => WalkCall(pc, ins.Imm, depth),
                    GraphNodeOp.Return => WalkReturn(depth),
                    GraphNodeOp.Yield or GraphNodeOp.AwaitCallback =>
                        throw new SlicePathException($"reaches {op}"),
                    // Callee steps are not charged to this slice.
                    GraphNodeOp.InvokeGraph => Walk(pc + 1, depth),
                    _ => Walk(pc + 1, depth),
                };
                int total = 1 + rest;
                _stack.Remove(key);
                _memo[key] = total;
                return total;
            }

            private int WalkCall(int pc, int target, int depth)
            {
                if (depth >= _returns.Length)
                {
                    throw new SlicePathException(
                        $"exceeds the call stack ({GraphVmLimits.MaxCallStackDepth})");
                }

                _returns[depth] = pc + 1;
                return Walk(target, depth + 1);
            }

            private int WalkReturn(int depth)
            {
                if (depth <= 0)
                {
                    throw new SlicePathException("returns with an empty call stack");
                }

                return Walk(_returns[depth - 1], depth - 1);
            }

            private string Key(int pc, int depth)
            {
                _key.Clear();
                _key.Append(pc);
                for (int i = 0; i < depth; i++)
                {
                    _key.Append(',').Append(_returns[i]);
                }

                return _key.ToString();
            }
        }
    }
}
