using System;
using System.Collections.Generic;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.GraphRuntime
{
    public readonly struct GraphProgramRegistration
    {
        public GraphProgramRegistration(GraphInstruction[] program, GraphKind kind)
            : this(program, kind, Array.Empty<string>())
        {
        }

        public GraphProgramRegistration(GraphInstruction[] program, GraphKind kind, string[]? symbols)
        {
            Program = program ?? Array.Empty<GraphInstruction>();
            Kind = kind;
            Symbols = symbols ?? Array.Empty<string>();
            ContainsYield = ProgramContainsYield(Program);
        }

        public GraphInstruction[] Program { get; }
        public GraphKind Kind { get; }
        public string[] Symbols { get; }
        public bool ContainsYield { get; }

        private static bool ProgramContainsYield(GraphInstruction[] program)
        {
            for (int i = 0; i < program.Length; i++)
            {
                if (program[i].Op == (ushort)GraphNodeOp.Yield)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class GraphProgramRegistry
    {
        private readonly Dictionary<int, GraphProgramRegistration> _programs = new();
        private readonly Dictionary<int, GraphInstructionSourceMap> _sourceMaps = new();

        public void Clear()
        {
            _programs.Clear();
            _sourceMaps.Clear();
        }

        public void Register(int graphId, GraphInstruction[] program, GraphKind kind)
            => Register(graphId, program, kind, GraphInstructionSourceMap.Empty);

        public void Register(int graphId, GraphInstruction[] program, GraphKind kind, GraphInstructionSourceMap sourceMap)
            => Register(graphId, program, kind, sourceMap, Array.Empty<string>());

        public void Register(int graphId, GraphInstruction[] program, GraphKind kind, GraphInstructionSourceMap sourceMap, string[]? symbols)
        {
            if (graphId <= 0) throw new ArgumentOutOfRangeException(nameof(graphId));
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (kind == GraphKind.None || !Enum.IsDefined(typeof(GraphKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph registration requires an explicit supported kind.");
            }

            if (!_programs.TryAdd(graphId, new GraphProgramRegistration(program, kind, symbols)))
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} is already registered; duplicate registration is not allowed.");
            }

            if (sourceMap.HasSources)
            {
                _sourceMaps[graphId] = sourceMap;
            }

            try
            {
                EnsureProgramValid(graphId, program, kind);
                EnsureNoInvokeCycle(graphId);
            }
            catch
            {
                _programs.Remove(graphId);
                _sourceMaps.Remove(graphId);
                throw;
            }
        }

        /// <summary>
        /// Hot-replaces program body for an already-registered graph id.
        /// Kind and id must stay identical; identity remap is forbidden (EngineRestartRequired).
        /// </summary>
        public void ReplaceProgram(int graphId, GraphInstruction[] program, GraphKind kind, GraphInstructionSourceMap sourceMap)
            => ReplaceProgram(graphId, program, kind, sourceMap, Array.Empty<string>());

        public void ReplaceProgram(int graphId, GraphInstruction[] program, GraphKind kind, GraphInstructionSourceMap sourceMap, string[]? symbols)
        {
            if (graphId <= 0) throw new ArgumentOutOfRangeException(nameof(graphId));
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (kind == GraphKind.None || !Enum.IsDefined(typeof(GraphKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph replace requires an explicit supported kind.");
            }

            if (!_programs.TryGetValue(graphId, out GraphProgramRegistration existing))
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} is not registered; cannot ReplaceProgram (new ids require EngineRestart).");
            }

            if (existing.Kind != kind)
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} kind is '{existing.Kind}'; cannot replace with '{kind}' (identity change requires EngineRestart).");
            }

            GraphProgramRegistration previous = existing;
            bool hadPreviousSourceMap = _sourceMaps.TryGetValue(graphId, out GraphInstructionSourceMap previousSourceMap);
            _programs[graphId] = new GraphProgramRegistration(program, kind, symbols);
            if (sourceMap.HasSources)
            {
                _sourceMaps[graphId] = sourceMap;
            }
            else
            {
                _sourceMaps.Remove(graphId);
            }

            try
            {
                EnsureProgramValid(graphId, program, kind);
                EnsureNoInvokeCycle(graphId);
            }
            catch
            {
                _programs[graphId] = previous;
                if (hadPreviousSourceMap)
                {
                    _sourceMaps[graphId] = previousSourceMap;
                }
                else
                {
                    _sourceMaps.Remove(graphId);
                }

                throw;
            }
        }

        private static void EnsureProgramValid(int graphId, GraphInstruction[] program, GraphKind kind)
        {
            GraphKindOperationPolicy.ValidateProgram(
                kind,
                program,
                GasGraphOpHandlerTable.Instance,
                graphId,
                nameof(GraphProgramRegistry));
        }

        public void RequireHostKind(int graphId, GraphKind expected, string hostLabel)
        {
            if (!_programs.TryGetValue(graphId, out GraphProgramRegistration entry))
            {
                throw new InvalidOperationException($"Graph program id {graphId} is not registered.");
            }

            if (entry.Kind != expected)
            {
                throw new InvalidOperationException(
                    $"{GraphKindOperationPolicy.KindMismatchError}: 图 {graphId} 的种类是「{entry.Kind}」，不能挂在{hostLabel}上（这里只接受 {expected}）。");
            }
        }

        private void EnsureNoInvokeCycle(int graphId)
        {
            if (!GraphYieldPurityValidator.TryValidateNoInvokeCycle(
                    this,
                    graphId,
                    GraphYieldPurityTarget.DescribeGraph(graphId),
                    out string diagnostic,
                    allowMissingTargets: true))
            {
                throw new InvalidOperationException($"{GraphYieldPurityValidator.InvokeCycleError}: {diagnostic}");
            }
        }

        public bool TryGetProgram(int graphId, out ReadOnlySpan<GraphInstruction> program)
        {
            if (_programs.TryGetValue(graphId, out GraphProgramRegistration entry))
            {
                program = entry.Program;
                return true;
            }

            program = default;
            return false;
        }

        public bool TryGetKind(int graphId, out GraphKind kind)
        {
            if (_programs.TryGetValue(graphId, out GraphProgramRegistration entry) && entry.Kind != GraphKind.None)
            {
                kind = entry.Kind;
                return true;
            }

            kind = GraphKind.None;
            return false;
        }

        public bool TryGetRegistration(int graphId, out GraphProgramRegistration registration)
            => _programs.TryGetValue(graphId, out registration);

        public GraphKind RequireKind(int graphId, GraphKind expected)
        {
            if (!_programs.TryGetValue(graphId, out GraphProgramRegistration entry))
            {
                throw new InvalidOperationException($"Graph program id {graphId} is not registered.");
            }

            if (entry.Kind == GraphKind.None)
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} has no authored kind; expected '{expected}'.");
            }

            if (entry.Kind != expected)
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} kind is '{entry.Kind}', but '{expected}' is required.");
            }

            return entry.Kind;
        }

        public bool TryGetSourceMap(int graphId, out GraphInstructionSourceMap sourceMap)
            => _sourceMaps.TryGetValue(graphId, out sourceMap);
    }
}
