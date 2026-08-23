using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    internal readonly struct GraphYieldPurityTarget
    {
        public GraphYieldPurityTarget(int graphId, string label)
        {
            GraphId = graphId;
            Label = string.IsNullOrWhiteSpace(label) ? DescribeGraph(graphId) : label;
        }

        public int GraphId { get; }
        public string Label { get; }

        public static string DescribeGraph(int graphId)
        {
            string graphKey = GraphIdRegistry.GetName(graphId);
            return string.IsNullOrWhiteSpace(graphKey)
                ? $"graph id {graphId}"
                : $"graph '{graphKey}' (id {graphId})";
        }
    }

    internal delegate bool TryResolveFuncLibTarget(string functionName, out GraphYieldPurityTarget target);

    internal static class GraphYieldPurityValidator
    {
        public const string InvokeCycleError = "GAS.GRAPH.ERR.InvokeCycle";

        public static bool TryValidateNoReachableYield(
            GraphProgramRegistry programs,
            int rootGraphId,
            string rootLabel,
            TryResolveFuncLibTarget resolveFunction,
            out string diagnostic,
            GraphInstruction[]? rootProgramOverride = null,
            string[]? rootSymbolsOverride = null)
        {
            if (programs == null) throw new ArgumentNullException(nameof(programs));
            if (resolveFunction == null) throw new ArgumentNullException(nameof(resolveFunction));

            return !FindInGraph(
                new WalkState(
                    programs,
                    rootGraphId,
                    rootProgramOverride,
                    rootSymbolsOverride,
                    resolveFunction,
                    findYield: true,
                    allowMissingTargets: false),
                rootGraphId,
                string.IsNullOrWhiteSpace(rootLabel) ? GraphYieldPurityTarget.DescribeGraph(rootGraphId) : rootLabel,
                out diagnostic);
        }

        public static bool TryValidateNoInvokeCycle(
            GraphProgramRegistry programs,
            int rootGraphId,
            string rootLabel,
            out string diagnostic,
            GraphInstruction[]? rootProgramOverride = null,
            string[]? rootSymbolsOverride = null,
            TryResolveFuncLibTarget? resolveFunction = null,
            bool allowMissingTargets = true)
        {
            if (programs == null) throw new ArgumentNullException(nameof(programs));

            return !FindInGraph(
                new WalkState(
                    programs,
                    rootGraphId,
                    rootProgramOverride,
                    rootSymbolsOverride,
                    resolveFunction,
                    findYield: false,
                    allowMissingTargets),
                rootGraphId,
                string.IsNullOrWhiteSpace(rootLabel) ? GraphYieldPurityTarget.DescribeGraph(rootGraphId) : rootLabel,
                out diagnostic);
        }

        private sealed class WalkState
        {
            public WalkState(
                GraphProgramRegistry programs,
                int rootGraphId,
                GraphInstruction[]? rootProgramOverride,
                string[]? rootSymbolsOverride,
                TryResolveFuncLibTarget? resolveFunction,
                bool findYield,
                bool allowMissingTargets)
            {
                Programs = programs;
                RootGraphId = rootGraphId;
                RootProgramOverride = rootProgramOverride;
                RootSymbolsOverride = rootSymbolsOverride;
                ResolveFunction = resolveFunction;
                FindYield = findYield;
                AllowMissingTargets = allowMissingTargets;
                ActiveGraphs = new HashSet<int>();
                Path = new List<string>(8);
            }

            public GraphProgramRegistry Programs { get; }
            public int RootGraphId { get; }
            public GraphInstruction[]? RootProgramOverride { get; }
            public string[]? RootSymbolsOverride { get; }
            public TryResolveFuncLibTarget? ResolveFunction { get; }
            public bool FindYield { get; }
            public bool AllowMissingTargets { get; }
            public HashSet<int> ActiveGraphs { get; }
            public List<string> Path { get; }
        }

        private static bool FindInGraph(
            WalkState walk,
            int graphId,
            string label,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (graphId <= 0)
            {
                return Fail(walk.Path, $"invalid graph id {graphId}", out diagnostic);
            }

            if (!walk.ActiveGraphs.Add(graphId))
            {
                return Fail(walk.Path, InvokeCycleError, out diagnostic);
            }

            try
            {
                GraphInstruction[] program;
                string[] symbols;
                if (graphId == walk.RootGraphId && walk.RootProgramOverride != null)
                {
                    program = walk.RootProgramOverride;
                    symbols = walk.RootSymbolsOverride ?? Array.Empty<string>();
                }
                else
                {
                    if (!walk.Programs.TryGetRegistration(graphId, out GraphProgramRegistration registration))
                    {
                        if (walk.AllowMissingTargets)
                        {
                            return false;
                        }

                        return Fail(walk.Path, $"{GraphYieldPurityTarget.DescribeGraph(graphId)} has no registered program", out diagnostic);
                    }

                    program = registration.Program;
                    symbols = registration.Symbols;
                }

                walk.Path.Add(label);
                var visited = new bool[program.Length];
                bool found = FindInProgram(walk, graphId, program, symbols, 0, visited, out diagnostic);
                walk.Path.RemoveAt(walk.Path.Count - 1);
                return found;
            }
            finally
            {
                walk.ActiveGraphs.Remove(graphId);
            }
        }

        private static bool FindInProgram(
            WalkState walk,
            int graphId,
            GraphInstruction[] program,
            string[] symbols,
            int pc,
            bool[] visited,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if ((uint)pc >= (uint)program.Length)
            {
                return false;
            }

            if (visited[pc])
            {
                return false;
            }

            visited[pc] = true;
            GraphInstruction ins = program[pc];
            var op = (GraphNodeOp)ins.Op;

            switch (op)
            {
                case GraphNodeOp.None:
                    return FindInProgram(walk, graphId, program, symbols, pc + 1, visited, out diagnostic);

                case GraphNodeOp.Yield:
                    if (walk.FindYield)
                    {
                        return Fail(walk.Path, $"Yield@pc={pc}", out diagnostic);
                    }

                    return FindInProgram(walk, graphId, program, symbols, pc + 1, visited, out diagnostic);

                case GraphNodeOp.InvokeScript:
                    if (FindInvokeTarget(walk, symbols, ins, pc, out diagnostic))
                    {
                        return true;
                    }

                    return FindInProgram(walk, graphId, program, symbols, pc + 1, visited, out diagnostic);

                case GraphNodeOp.Call:
                {
                    int target = ins.Imm;
                    if ((uint)target >= (uint)program.Length)
                    {
                        return Fail(walk.Path, $"Call@pc={pc} target {target} is outside {GraphYieldPurityTarget.DescribeGraph(graphId)}", out diagnostic);
                    }

                    walk.Path.Add($"Call@pc={pc}->pc={target}");
                    bool found = FindInProgram(walk, graphId, program, symbols, target, visited, out diagnostic);
                    walk.Path.RemoveAt(walk.Path.Count - 1);
                    if (found)
                    {
                        return true;
                    }

                    return FindInProgram(walk, graphId, program, symbols, pc + 1, visited, out diagnostic);
                }

                case GraphNodeOp.Jump:
                {
                    int target = pc + 1 + ins.Imm;
                    if ((uint)target >= (uint)program.Length)
                    {
                        return Fail(walk.Path, $"Jump@pc={pc} target {target} is outside {GraphYieldPurityTarget.DescribeGraph(graphId)}", out diagnostic);
                    }

                    walk.Path.Add($"Jump@pc={pc}->pc={target}");
                    bool found = FindInProgram(walk, graphId, program, symbols, target, visited, out diagnostic);
                    walk.Path.RemoveAt(walk.Path.Count - 1);
                    return found;
                }

                case GraphNodeOp.JumpIfFalse:
                {
                    int target = pc + 1 + ins.Imm;
                    if ((uint)target >= (uint)program.Length)
                    {
                        return Fail(walk.Path, $"JumpIfFalse@pc={pc} target {target} is outside {GraphYieldPurityTarget.DescribeGraph(graphId)}", out diagnostic);
                    }

                    walk.Path.Add($"JumpIfFalse.false@pc={pc}->pc={target}");
                    bool found = FindInProgram(walk, graphId, program, symbols, target, visited, out diagnostic);
                    walk.Path.RemoveAt(walk.Path.Count - 1);
                    if (found)
                    {
                        return true;
                    }

                    return FindInProgram(walk, graphId, program, symbols, pc + 1, visited, out diagnostic);
                }

                case GraphNodeOp.Return:
                case GraphNodeOp.HaltReturnInt:
                    return false;

                default:
                    if (!Enum.IsDefined(typeof(GraphNodeOp), op))
                    {
                        return Fail(walk.Path, $"unknown graph op {ins.Op}@pc={pc}", out diagnostic);
                    }

                    return FindInProgram(walk, graphId, program, symbols, pc + 1, visited, out diagnostic);
            }
        }

        private static bool FindInvokeTarget(
            WalkState walk,
            string[] symbols,
            in GraphInstruction ins,
            int pc,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if ((ins.Flags & GraphInstructionFlags.FuncLibName) != 0)
            {
                if (!TryResolveSymbol(symbols, ins.Imm, out string functionName))
                {
                    return Fail(walk.Path, $"InvokeScript.functionName@pc={pc} cannot resolve symbol index {ins.Imm}", out diagnostic);
                }

                if (walk.ResolveFunction == null || !walk.ResolveFunction(functionName, out GraphYieldPurityTarget target))
                {
                    if (walk.AllowMissingTargets)
                    {
                        return false;
                    }

                    return Fail(walk.Path, $"InvokeScript.functionName '{functionName}'@pc={pc} is not registered in FuncLib", out diagnostic);
                }

                walk.Path.Add($"InvokeScript.functionName '{functionName}'@pc={pc}");
                bool found = FindInGraph(walk, target.GraphId, target.Label, out diagnostic);
                walk.Path.RemoveAt(walk.Path.Count - 1);
                return found;
            }

            int childGraphId = ins.Imm;
            if (childGraphId <= 0)
            {
                return Fail(walk.Path, $"InvokeScript.graphId@pc={pc} requires a positive graph id", out diagnostic);
            }

            walk.Path.Add($"InvokeScript.graphId={childGraphId}@pc={pc}");
            bool childFound = FindInGraph(
                walk,
                childGraphId,
                GraphYieldPurityTarget.DescribeGraph(childGraphId),
                out diagnostic);
            walk.Path.RemoveAt(walk.Path.Count - 1);
            return childFound;
        }

        private static bool TryResolveSymbol(string[] symbols, int index, out string value)
        {
            if ((uint)index >= (uint)symbols.Length)
            {
                value = string.Empty;
                return false;
            }

            value = symbols[index] ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool Fail(List<string> path, string terminal, out string diagnostic)
        {
            path.Add(terminal);
            diagnostic = string.Join(" -> ", path);
            path.RemoveAt(path.Count - 1);
            return true;
        }
    }
}
