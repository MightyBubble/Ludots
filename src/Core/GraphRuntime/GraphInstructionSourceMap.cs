using System;

namespace Ludots.Core.GraphRuntime
{
    public readonly record struct GraphInstructionSource(string GraphName, string NodeId, string Op);

    public readonly struct GraphInstructionSourceMap
    {
        public static readonly GraphInstructionSourceMap Empty = new(string.Empty, Array.Empty<GraphInstructionSource>());

        public GraphInstructionSourceMap(string graphName, GraphInstructionSource[] instructions)
        {
            GraphName = graphName ?? string.Empty;
            Instructions = instructions ?? Array.Empty<GraphInstructionSource>();
        }

        public string GraphName { get; }
        public GraphInstructionSource[] Instructions { get; }
        public bool HasSources => Instructions.Length > 0;

        public bool TryGetSource(int instructionIndex, out GraphInstructionSource source)
        {
            if ((uint)instructionIndex < (uint)Instructions.Length)
            {
                source = Instructions[instructionIndex];
                return !string.IsNullOrWhiteSpace(source.NodeId);
            }

            source = default;
            return false;
        }
    }
}
