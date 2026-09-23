using System;

namespace Ludots.Core.GraphRuntime
{
    public readonly struct GraphProgramPackage
    {
        public GraphProgramPackage(string graphName, string[] symbols, GraphInstruction[] program)
            : this(graphName, symbols, program, GraphInstructionSourceMap.Empty)
        {
        }

        public GraphProgramPackage(
            string graphName,
            string[] symbols,
            GraphInstruction[] program,
            GraphInstructionSourceMap sourceMap)
        {
            GraphName = graphName ?? string.Empty;
            Symbols = symbols ?? Array.Empty<string>();
            Program = program ?? Array.Empty<GraphInstruction>();
            SourceMap = sourceMap;
        }

        public string GraphName { get; }
        public string[] Symbols { get; }
        public GraphInstruction[] Program { get; }
        public GraphInstructionSourceMap SourceMap { get; }

        public void Deconstruct(out string graphName, out string[] symbols, out GraphInstruction[] program)
        {
            graphName = GraphName;
            symbols = Symbols;
            program = Program;
        }

        public void Deconstruct(
            out string graphName,
            out string[] symbols,
            out GraphInstruction[] program,
            out GraphInstructionSourceMap sourceMap)
        {
            graphName = GraphName;
            symbols = Symbols;
            program = Program;
            sourceMap = SourceMap;
        }
    }
}
