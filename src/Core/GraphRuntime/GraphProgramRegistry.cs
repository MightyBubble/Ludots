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

        public void Clear() => _programs.Clear();

        /// <summary>
        /// Registers a programmatically constructed graph without an authored kind.
        /// Authored graphs must use <see cref="Register(int, GraphInstruction[], GraphKind)"/>.
        /// </summary>
        public void Register(int graphId, GraphInstruction[] program)
        {
            Register(graphId, program, GraphKind.None);
        }

        public void Register(int graphId, GraphInstruction[] program, GraphKind kind)
        {
            if (graphId <= 0) throw new ArgumentOutOfRangeException(nameof(graphId));
            if (!_programs.TryAdd(graphId, new GraphProgramRegistration(program ?? Array.Empty<GraphInstruction>(), kind)))
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} is already registered; duplicate registration is not allowed.");
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
    }
}
