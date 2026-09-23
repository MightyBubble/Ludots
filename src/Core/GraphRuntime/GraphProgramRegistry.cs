using System;
using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    public sealed class GraphProgramRegistry
    {
        private readonly Dictionary<int, GraphInstruction[]> _programs = new();
        private readonly Dictionary<int, GraphInstructionSourceMap> _sourceMaps = new();

        public void Clear()
        {
            _programs.Clear();
            _sourceMaps.Clear();
        }

        public void Register(int graphId, GraphInstruction[] program)
            => Register(graphId, program, GraphInstructionSourceMap.Empty);

        public void Register(int graphId, GraphInstruction[] program, GraphInstructionSourceMap sourceMap)
        {
            if (graphId <= 0) throw new ArgumentOutOfRangeException(nameof(graphId));
            _programs[graphId] = program ?? Array.Empty<GraphInstruction>();
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
            if (_programs.TryGetValue(graphId, out var arr))
            {
                program = arr;
                return true;
            }

            program = default;
            return false;
        }

        public bool TryGetSourceMap(int graphId, out GraphInstructionSourceMap sourceMap)
            => _sourceMaps.TryGetValue(graphId, out sourceMap);
    }
}
