using System;

namespace Ludots.Core.GraphRuntime
{
    public readonly record struct GraphInstructionSource(string GraphId, string NodeId, string Op, string ControlPort = "");

    public readonly struct GraphInstructionSourceMap
    {
        public GraphInstructionSourceMap(string graphId, GraphInstructionSource[] sources)
        {
            GraphId = graphId ?? string.Empty;
            Sources = sources ?? Array.Empty<GraphInstructionSource>();
        }

        public static GraphInstructionSourceMap Empty => new(string.Empty, Array.Empty<GraphInstructionSource>());

        public string GraphId { get; }
        public GraphInstructionSource[] Sources { get; }
        public bool HasSources => Sources.Length > 0;

        public bool TryGetSource(int instructionIndex, out GraphInstructionSource source)
        {
            if ((uint)instructionIndex < (uint)Sources.Length)
            {
                source = Sources[instructionIndex];
                return !string.IsNullOrWhiteSpace(source.NodeId);
            }

            source = default;
            return false;
        }
    }
}
