using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>MapTrigger-only dispatch table; replaces <see cref="Entry"/> for that kind and must be empty for every other kind.</summary>
        public List<GraphMapTriggerEntryConfig> Entries { get; set; } = new();
        public List<GraphControlFlowNode> Nodes { get; set; } = new();
        public List<GraphControlFlowEdge> ControlEdges { get; set; } = new();
        public List<GraphControlFlowValueEdge> ValueEdges { get; set; } = new();
        public List<GraphOutputConfig> Outputs { get; set; } = new();
    }

    public sealed class GraphMapTriggerEntryConfig
    {
        public string Label { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public string Start { get; set; } = string.Empty;
        public bool Once { get; set; }
    }

    public sealed class GraphControlFlowNode
    {
        public string Id { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty;
        public int IntValue { get; set; }
        public float FloatValue { get; set; }
        public bool BoolValue { get; set; }
        /// <summary>Optional target Script graph id for InvokeScript (literal; mutually exclusive with FunctionName).</summary>
        public int GraphId { get; set; }
        /// <summary>Func Lib name for InvokeScript; resolved to GraphId at patch time.</summary>
        public string? FunctionName { get; set; }
        public string? Attribute { get; set; }
        public string? Tag { get; set; }
        public string? LookupTable { get; set; }
        public string? LookupField { get; set; }
        public string? Template { get; set; }
        public string? CollectionKey { get; set; }
        public string? EffectTemplate { get; set; }
        public string? PayloadPreset { get; set; }
        public string? BuiltinHandler { get; set; }
        public string? BlackboardKey { get; set; }
        public string? ConfigKey { get; set; }
        /// <summary>Panel type symbol for ShowPanel/HidePanel/CreatePanel/DestroyPanel ops (#1014).</summary>
        public string? PanelType { get; set; }
        /// <summary>Placement anchor symbol for CreatePanel (surface-side region id).</summary>
        public string? PanelAnchor { get; set; }
        public string? RelationshipType { get; set; }
        public string? RelationshipMode { get; set; }
        public string? Metric { get; set; }
        public string? Flag { get; set; }
        /// <summary>Event payload slot index for LoadEventPayloadInt (0..1) / LoadEventPayloadFloat (0..3).</summary>
        public int Slot { get; set; }
        public string? QueryCapacityPolicy { get; set; }
        public string? DroppedOutput { get; set; }
        public string? ValidOutput { get; set; }
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
        /// <summary>Loop body entry for While/Until author sugar.</summary>
        public const string Body = "body";
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
