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

            diagnostic = string.Empty;
            var activeGraphs = new HashSet<int>();
            var path = new List<string>(8);
            return !FindInGraph(
                programs,
                rootGraphId,
                string.IsNullOrWhiteSpace(rootLabel) ? GraphYieldPurityTarget.DescribeGraph(rootGraphId) : rootLabel,
                rootGraphId,
                rootProgramOverride,
                rootSymbolsOverride,
                resolveFunction,
                activeGraphs,
                path,
                out diagnostic);
        }

        private static bool FindInGraph(
            GraphProgramRegistry programs,
            int graphId,
            string label,
            int rootGraphId,
            GraphInstruction[]? rootProgramOverride,
            string[]? rootSymbolsOverride,
            TryResolveFuncLibTarget resolveFunction,
            HashSet<int> activeGraphs,
            List<string> path,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (graphId <= 0)
            {
                return Fail(path, $"invalid graph id {graphId}", out diagnostic);
            }

            if (!activeGraphs.Add(graphId))
            {
                return false;
            }

            try
            {
                GraphInstruction[] program;
                string[] symbols;
                if (graphId == rootGraphId && rootProgramOverride != null)
                {
                    program = rootProgramOverride;
                    symbols = rootSymbolsOverride ?? Array.Empty<string>();
                }
                else
                {
                    if (!programs.TryGetRegistration(graphId, out GraphProgramRegistration registration))
                    {
                        return Fail(path, $"{GraphYieldPurityTarget.DescribeGraph(graphId)} has no registered program", out diagnostic);
                    }

                    program = registration.Program;
                    symbols = registration.Symbols;
                }

                path.Add(label);
                var visited = new bool[program.Length];
                bool found = FindInProgram(
                    programs,
                    graphId,
                    program,
                    symbols,
                    0,
                    rootGraphId,
                    rootProgramOverride,
                    rootSymbolsOverride,
                    resolveFunction,
                    activeGraphs,
                    visited,
                    path,
                    out diagnostic);
                path.RemoveAt(path.Count - 1);
                return found;
            }
            finally
            {
                activeGraphs.Remove(graphId);
            }
        }

        private static bool FindInProgram(
            GraphProgramRegistry programs,
            int graphId,
            GraphInstruction[] program,
            string[] symbols,
            int pc,
            int rootGraphId,
            GraphInstruction[]? rootProgramOverride,
            string[]? rootSymbolsOverride,
            TryResolveFuncLibTarget resolveFunction,
            HashSet<int> activeGraphs,
            bool[] visited,
            List<string> path,
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
                    return FindInProgram(programs, graphId, program, symbols, pc + 1, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, visited, path, out diagnostic);

                case GraphNodeOp.Yield:
                    return Fail(path, $"Yield@pc={pc}", out diagnostic);

                case GraphNodeOp.InvokeScript:
                    if (FindInvokeTarget(programs, graphId, symbols, ins, pc, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, path, out diagnostic))
                    {
                        return true;
                    }

                    return FindInProgram(programs, graphId, program, symbols, pc + 1, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, visited, path, out diagnostic);

                case GraphNodeOp.Call:
                {
                    int target = ins.Imm;
                    if ((uint)target >= (uint)program.Length)
                    {
                        return Fail(path, $"Call@pc={pc} target {target} is outside {GraphYieldPurityTarget.DescribeGraph(graphId)}", out diagnostic);
                    }

                    path.Add($"Call@pc={pc}->pc={target}");
                    bool found = FindInProgram(programs, graphId, program, symbols, target, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, visited, path, out diagnostic);
                    path.RemoveAt(path.Count - 1);
                    if (found)
                    {
                        return true;
                    }

                    return FindInProgram(programs, graphId, program, symbols, pc + 1, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, visited, path, out diagnostic);
                }

                case GraphNodeOp.Jump:
                {
                    int target = pc + 1 + ins.Imm;
                    if ((uint)target >= (uint)program.Length)
                    {
                        return Fail(path, $"Jump@pc={pc} target {target} is outside {GraphYieldPurityTarget.DescribeGraph(graphId)}", out diagnostic);
                    }

                    path.Add($"Jump@pc={pc}->pc={target}");
                    bool found = FindInProgram(programs, graphId, program, symbols, target, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, visited, path, out diagnostic);
                    path.RemoveAt(path.Count - 1);
                    return found;
                }

                case GraphNodeOp.JumpIfFalse:
                {
                    int target = pc + 1 + ins.Imm;
                    if ((uint)target >= (uint)program.Length)
                    {
                        return Fail(path, $"JumpIfFalse@pc={pc} target {target} is outside {GraphYieldPurityTarget.DescribeGraph(graphId)}", out diagnostic);
                    }

                    path.Add($"JumpIfFalse.false@pc={pc}->pc={target}");
                    bool found = FindInProgram(programs, graphId, program, symbols, target, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, visited, path, out diagnostic);
                    path.RemoveAt(path.Count - 1);
                    if (found)
                    {
                        return true;
                    }

                    return FindInProgram(programs, graphId, program, symbols, pc + 1, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, visited, path, out diagnostic);
                }

                case GraphNodeOp.Return:
                case GraphNodeOp.HaltReturnInt:
                    return false;

                default:
                    if (!Enum.IsDefined(typeof(GraphNodeOp), op))
                    {
                        return Fail(path, $"unknown graph op {ins.Op}@pc={pc}", out diagnostic);
                    }

                    return FindInProgram(programs, graphId, program, symbols, pc + 1, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, visited, path, out diagnostic);
            }
        }

        private static bool FindInvokeTarget(
            GraphProgramRegistry programs,
            int graphId,
            string[] symbols,
            in GraphInstruction ins,
            int pc,
            int rootGraphId,
            GraphInstruction[]? rootProgramOverride,
            string[]? rootSymbolsOverride,
            TryResolveFuncLibTarget resolveFunction,
            HashSet<int> activeGraphs,
            List<string> path,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if ((ins.Flags & GraphInstructionFlags.FuncLibName) != 0)
            {
                if (!TryResolveSymbol(symbols, ins.Imm, out string functionName))
                {
                    return Fail(path, $"InvokeScript.functionName@pc={pc} cannot resolve symbol index {ins.Imm}", out diagnostic);
                }

                if (!resolveFunction(functionName, out GraphYieldPurityTarget target))
                {
                    return Fail(path, $"InvokeScript.functionName '{functionName}'@pc={pc} is not registered in FuncLib", out diagnostic);
                }

                path.Add($"InvokeScript.functionName '{functionName}'@pc={pc}");
                bool found = FindInGraph(programs, target.GraphId, target.Label, rootGraphId, rootProgramOverride, rootSymbolsOverride, resolveFunction, activeGraphs, path, out diagnostic);
                path.RemoveAt(path.Count - 1);
                return found;
            }

            int childGraphId = ins.Imm;
            if (childGraphId <= 0)
            {
                return Fail(path, $"InvokeScript.graphId@pc={pc} requires a positive graph id", out diagnostic);
            }

            path.Add($"InvokeScript.graphId={childGraphId}@pc={pc}");
            bool childFound = FindInGraph(
                programs,
                childGraphId,
                GraphYieldPurityTarget.DescribeGraph(childGraphId),
                rootGraphId,
                rootProgramOverride,
                rootSymbolsOverride,
                resolveFunction,
                activeGraphs,
                path,
                out diagnostic);
            path.RemoveAt(path.Count - 1);
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
