using System;
using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    public readonly struct GraphProgramRegistration
    {
        public GraphProgramRegistration(GraphInstruction[] program, GraphKind kind)
        {
            Program = program ?? Array.Empty<GraphInstruction>();
            Kind = kind;
        }

        public GraphInstruction[] Program { get; }
        public GraphKind Kind { get; }
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
        {
            if (graphId <= 0) throw new ArgumentOutOfRangeException(nameof(graphId));
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (kind == GraphKind.None || !Enum.IsDefined(typeof(GraphKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph registration requires an explicit supported kind.");
            }

            if (!_programs.TryAdd(graphId, new GraphProgramRegistration(program, kind)))
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} is already registered; duplicate registration is not allowed.");
            }

            if (sourceMap.HasSources)
            {
                _sourceMaps[graphId] = sourceMap;
            }
        }

        public void Replace(int graphId, GraphInstruction[] program, GraphKind kind)
            => Replace(graphId, program, kind, GraphInstructionSourceMap.Empty);

        public void Replace(int graphId, GraphInstruction[] program, GraphKind kind, GraphInstructionSourceMap sourceMap)
        {
            if (graphId <= 0) throw new ArgumentOutOfRangeException(nameof(graphId));
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (kind == GraphKind.None || !Enum.IsDefined(typeof(GraphKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph replacement requires an explicit supported kind.");
            }

            if (!_programs.ContainsKey(graphId))
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} is not registered; Replace cannot introduce a new graph id after freeze.");
            }

            _programs[graphId] = new GraphProgramRegistration(program, kind);
            if (sourceMap.HasSources)
            {
                _sourceMaps[graphId] = sourceMap;
            }
            else
            {
                _sourceMaps.Remove(graphId);
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
