using System;
using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    public sealed class GraphProgramRegistry
    {
        private readonly Dictionary<int, GraphInstruction[]> _programs = new();

        public void Clear() => _programs.Clear();

        public void Register(int graphId, GraphInstruction[] program)
        {
            if (graphId <= 0) throw new ArgumentOutOfRangeException(nameof(graphId));
            _programs[graphId] = program ?? Array.Empty<GraphInstruction>();
        }

        public bool TryGetProgram(int graphId, out ReadOnlySpan<GraphInstruction> program)
        {
            if (_programs.TryGetValue(graphId, out var arr))
            {
                program = arr;
                return true;
            }

            program = default;
            return false;
        }
    }
}

