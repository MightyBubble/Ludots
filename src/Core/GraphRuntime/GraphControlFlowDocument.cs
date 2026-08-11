using System.Collections.Generic;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// L1 authoring SSOT document: explicit control and value edges for every GraphKind.
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
        public string? EffectTemplate { get; set; }
        public string? BuiltinHandler { get; set; }
        public string? BlackboardKey { get; set; }
        public string? ConfigKey { get; set; }
        public string? RelationshipType { get; set; }
        public string? RelationshipMode { get; set; }
        public string? Metric { get; set; }
        public string? Flag { get; set; }
        public string? QueryCapacityPolicy { get; set; }
        public float RadiusCm { get; set; }
        public float RangeCm { get; set; }
        public int DirectionDeg { get; set; }
        public int HalfAngleDeg { get; set; }
        public int LengthCm { get; set; }
        public int HalfWidthCm { get; set; }
        public int HalfHeightCm { get; set; }
        public int RotationDeg { get; set; }
        public int HexRadius { get; set; }
        public uint LayerMask { get; set; }
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
    }
}
