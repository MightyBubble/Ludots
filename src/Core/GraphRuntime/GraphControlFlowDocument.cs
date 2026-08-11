using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Authored Script (L1 flow) document with explicit control and value edges.
    /// Compiles to <see cref="GraphInstruction"/> via GASGraph control-flow compiler.
    /// </summary>
    public sealed class GraphControlFlowDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Entry { get; set; } = string.Empty;
        public List<GraphControlFlowNode> Nodes { get; set; } = new();
        public List<GraphControlFlowEdge> ControlEdges { get; set; } = new();
        public List<GraphControlFlowValueEdge> ValueEdges { get; set; } = new();
    }

    public sealed class GraphControlFlowNode
    {
        public string Id { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty;
        public int IntValue { get; set; }
        /// <summary>Optional target Script graph id for InvokeScript (patched or literal).</summary>
        public int GraphId { get; set; }
        /// <summary>
        /// When &gt;= 0, forces this node's int output (or MoveInt source via Imm) onto a fixed int register.
        /// Used for loop-carried values without inventing a second memory space.
        /// </summary>
        public int PinRegister { get; set; } = -1;
    }

    public sealed class GraphControlFlowEdge
    {
        public GraphControlFlowEdge()
        {
        }

        public GraphControlFlowEdge(string from, string fromPort, string to)
        {
            From = from;
            FromPort = fromPort;
            To = to;
        }

        public string From { get; set; } = string.Empty;
        public string FromPort { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    public sealed class GraphControlFlowValueEdge
    {
        public GraphControlFlowValueEdge()
        {
        }

        public GraphControlFlowValueEdge(string from, string fromPort, string to, string toPort)
        {
            From = from;
            FromPort = fromPort;
            To = to;
            ToPort = toPort;
        }

        public string From { get; set; } = string.Empty;
        public string FromPort { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string ToPort { get; set; } = string.Empty;
    }

    public static class GraphControlFlowPorts
    {
        public const string Enter = "enter";
        public const string Next = "next";
        public const string True = "true";
        public const string False = "false";
        public const string Call = "call";
        public const string Target = "target";
        public const string Value = "value";
        public const string A = "a";
        public const string B = "b";
        public const string Condition = "condition";
    }
}
