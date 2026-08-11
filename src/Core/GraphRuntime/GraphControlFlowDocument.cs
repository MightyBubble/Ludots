using System;
using System.Collections.Generic;
using System.Globalization;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Authored Script and Query document with explicit control and value edges.
    /// Compiles to <see cref="GraphInstruction"/> via GASGraph control-flow compiler.
    /// </summary>
    public sealed class GraphControlFlowDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Entry { get; set; } = string.Empty;
        public List<GraphControlFlowNode> Nodes { get; set; } = new();
        public List<GraphControlFlowEdge> ControlEdges { get; set; } = new();
        public List<GraphControlFlowValueEdge> ValueEdges { get; set; } = new();
        public List<GraphOutputConfig> Outputs { get; set; } = new();
    }

    public sealed class GraphControlFlowNode
    {
        public string Id { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty;
        public int IntValue { get; set; }
        public float FloatValue { get; set; }
        public bool BoolValue { get; set; }
        /// <summary>Optional target Script graph id for InvokeScript (patched or literal).</summary>
        public int GraphId { get; set; }
        public string? Attribute { get; set; }
        public string? Tag { get; set; }
        public string? Template { get; set; }
        public string? CollectionKey { get; set; }
        public string? RelationshipType { get; set; }
        public string? Metric { get; set; }
        public string? Flag { get; set; }
        public int TeamId { get; set; }
        public bool Descending { get; set; }
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
        public const string List = "list";
        public const string TeamId = "teamId";
        public const string Source = "source";
        public const string Min = "min";
        public const string Max = "max";
        public const string A = "a";
        public const string B = "b";
        public const string Condition = "condition";
        /// <summary>Int selector input for SwitchInt compile-time sugar.</summary>
        public const string Selector = "selector";
        /// <summary>Default arm control port for SwitchInt compile-time sugar.</summary>
        public const string Default = "default";
        /// <summary>Control port prefix for SwitchInt arms; full port is case:{int}.</summary>
        public const string CasePrefix = "case:";

        public static string Case(int caseValue)
            => CasePrefix + caseValue.ToString(CultureInfo.InvariantCulture);

        public static bool TryParseCasePort(string port, out int caseValue)
        {
            caseValue = 0;
            if (string.IsNullOrEmpty(port) ||
                !port.StartsWith(CasePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(
                port.AsSpan(CasePrefix.Length),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out caseValue);
        }
    }
}
