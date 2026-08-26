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
        /// <summary>TriggerGraph-only dispatch table; replaces <see cref="Entry"/> for that kind and must be empty for every other kind.</summary>
        public List<TriggerGraphEntryConfig> Entries { get; set; } = new();
        public List<GraphControlFlowNode> Nodes { get; set; } = new();
        public List<GraphControlFlowEdge> ControlEdges { get; set; } = new();
        public List<GraphControlFlowValueEdge> ValueEdges { get; set; } = new();
        public List<GraphOutputConfig> Outputs { get; set; } = new();
    }

    public sealed class TriggerGraphEntryConfig
    {
        public string Label { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public string Start { get; set; } = string.Empty;
        public bool Once { get; set; }
        public string? Refire { get; set; }
        /// <summary>Dispatch priority within one event key (#1124): ascending, negative earlier, default 0.</summary>
        public int Priority { get; set; }
        public TriggerGraphEntryFiltersConfig? Filters { get; set; }
        /// <summary>Authoring shape of <c>hookAnchor: { graphId, anchor, position }</c> (#1124).</summary>
        public TriggerGraphHookAnchorConfig? HookAnchor { get; set; }
        /// <summary>Authoring shape of <c>hookNodeBefore: { graphId, nodeId }</c> (#1124).</summary>
        public TriggerGraphHookNodeConfig? HookNodeBefore { get; set; }
        /// <summary>Authoring shape of <c>hookNodeAfter: { graphId, nodeId }</c> (#1124).</summary>
        public TriggerGraphHookNodeConfig? HookNodeAfter { get; set; }
        /// <summary>Compiled filter struct produced by entry validation; default when no filters are authored.</summary>
        public TriggerGraphEntryFilters ParsedFilters { get; set; }
        /// <summary>Normalized refire policy ("ignore"/"restart"); default "ignore".</summary>
        public string NormalizedRefire { get; set; } = TriggerGraphEntry.RefireIgnore;
        /// <summary>Normalized hook target produced by entry validation; null = plain dispatch entry.</summary>
        public TriggerGraphHookTargetConfig? ParsedHook { get; set; }
    }

    /// <summary>Authoring shape of an entry <c>hookAnchor</c> block: weave before/after a named anchor node.</summary>
    public sealed class TriggerGraphHookAnchorConfig
    {
        public string GraphId { get; set; } = string.Empty;
        public string Anchor { get; set; } = string.Empty;
        public string Position { get; set; } = "before";
    }

    /// <summary>Authoring shape of an entry <c>hookNodeBefore</c>/<c>hookNodeAfter</c> block: weave before/after a node id.</summary>
    public sealed class TriggerGraphHookNodeConfig
    {
        public string GraphId { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Normalized hook target of a TriggerGraph entry (#1124): the entry body is a
    /// fragment woven into another graph at compile time instead of dispatching on
    /// its own event. Exactly one of Anchor / NodeId is set.
    /// </summary>
    public sealed class TriggerGraphHookTargetConfig
    {
        public TriggerGraphHookTargetConfig(string targetGraphId, string targetNodeId, bool before)
        {
            TargetGraphId = targetGraphId;
            TargetNodeId = targetNodeId;
            Before = before;
        }

        public string TargetGraphId { get; }
        public string TargetNodeId { get; }
        public bool Before { get; }

        public static bool TryParseAnchor(
            string targetGraphId,
            string anchor,
            string position,
            string context,
            out TriggerGraphHookTargetConfig? parsed,
            out string? error)
        {
            return TryParse(targetGraphId, anchor, nodeId: null, position, context, out parsed, out error);
        }

        public static bool TryParseNode(
            string targetGraphId,
            string nodeId,
            string position,
            string context,
            out TriggerGraphHookTargetConfig? parsed,
            out string? error)
        {
            return TryParse(targetGraphId, anchor: null, nodeId, position, context, out parsed, out error);
        }

        private static bool TryParse(
            string targetGraphId,
            string? anchor,
            string? nodeId,
            string position,
            string context,
            out TriggerGraphHookTargetConfig? parsed,
            out string? error)
        {
            parsed = null;
            error = null;
            if (string.IsNullOrWhiteSpace(targetGraphId))
            {
                error = $"{context} hook target requires a non-empty 'graphId'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(anchor) && string.IsNullOrWhiteSpace(nodeId))
            {
                error = $"{context} hook target requires an 'anchor' or 'nodeId'.";
                return false;
            }

            string trimmedPosition = (position ?? string.Empty).Trim();
            bool before;
            if (trimmedPosition == "before")
            {
                before = true;
            }
            else if (trimmedPosition == "after")
            {
                before = false;
            }
            else
            {
                error = $"{context} hook 'position' must be \"before\" or \"after\" (got '{position ?? "null"}').";
                return false;
            }

            parsed = new TriggerGraphHookTargetConfig(
                targetGraphId.Trim(),
                (anchor ?? nodeId ?? string.Empty).Trim(),
                before);
            return true;
        }
    }

    /// <summary>
    /// Authoring shape of a TriggerGraph entry filters block; direction is authored as
    /// "cross_above" / "cross_below" and compiled to the typed enum.
    /// </summary>
    public sealed class TriggerGraphEntryFiltersConfig
    {
        public string? Region { get; set; }
        public string? Tag { get; set; }
        public int? Team { get; set; }
        public float? Threshold { get; set; }
        public string? Direction { get; set; }
        public string? Action { get; set; }
        public string? InstanceId { get; set; }
        public string? VarName { get; set; }
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
        public string? Ability { get; set; }
        public string? LookupTable { get; set; }
        public string? LookupField { get; set; }
        /// <summary>Distribution id symbol for WeightedPick; resolved to a key id at patch time.</summary>
        public string? Distribution { get; set; }
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
        /// <summary>Skin id for CreatePanel (Unreal-style creation-time render param; optional).</summary>
        public string? PanelSkin { get; set; }
        /// <summary>Viewport Z-order for CreatePanel; maps to surface lease priority. Default 100.</summary>
        public float? PanelZOrder { get; set; }
        /// <summary>Map variable name symbol for ReadMapVarInt/ReadMapVarFloat/WriteMapVarInt/WriteMapVarFloat.</summary>
        public string? Var { get; set; }
        public string? RelationshipType { get; set; }
        public string? RelationshipMode { get; set; }
        public string? Metric { get; set; }
        public string? Flag { get; set; }
        /// <summary>Event payload slot index for LoadEventPayloadInt (0..1) / LoadEventPayloadFloat (0..3).</summary>
        public int Slot { get; set; }
        /// <summary>Named event payload key (a MapTriggerEventPayloadKeys constant) for LoadEntryPayload* ops.</summary>
        public string? PayloadKey { get; set; }
        /// <summary>Placed InstanceId for LoadPlacedEntity / LoadPlacedRegion / LoadPlacedAnchor (#1108); validated fail-closed against the mounting map's catalog at mount time.</summary>
        public string? InstanceId { get; set; }
        /// <summary>Optional TriggerGraph entry label for InvokeGraph; omitted → target entry table [0].</summary>
        public string? EntryLabel { get; set; }
        /// <summary>Event name for DispatchMapEvent; must resolve in the EventSchemaRegistry.</summary>
        public string? Event { get; set; }
        /// <summary>Dispatch domain for DispatchMapEvent: "map" (default), "self", or "global" (#1123).</summary>
        public string? Scope { get; set; }
        /// <summary>#1126 AwaitCallback catalog name (Imm symbol); required on AwaitCallback nodes.</summary>
        public string? CallbackType { get; set; }
        /// <summary>Literal / FormatText template for formal text ops (ConstText Imm → Symbols; FormatText brace scan).</summary>
        public string? Text { get; set; }
        /// <summary>Presentation surface for SinkPresentationText: "Subtitle" or "Dialogue".</summary>
        public string? PresentationSurface { get; set; }
        /// <summary>InvokeArgs staging key for StoreArgInt/Float/Entity and the InvokeGraph call contract.</summary>
        public string? ArgKey { get; set; }
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
        /// <summary>
        /// BtDecorator sugar kind: "inverter" (0↔1, Running passes through), "forceSuccess",
        /// or "forceFailure". Required on BtDecorator nodes; empty or unknown fails closed.
        /// </summary>
        public string? DecoratorKind { get; set; }
        /// <summary>
        /// Named hook point (#1124): another mod's TriggerGraph entry with a matching
        /// hookAnchor weaves its body before/after this node at compile time. Anchor
        /// names must be unique within one graph; empty means "no anchor".
        /// </summary>
        public string? Anchor { get; set; }
        /// <summary>
        /// Enum type name binding SwitchInt case arms and SelectByEnum candidates to
        /// Enums/enums.json members; case ports are then authored as case:{memberName}
        /// and resolved to declaration-order ints at compile time. Unregistered type
        /// names fail closed.
        /// </summary>
        public string? EnumType { get; set; }
        /// <summary>
        /// Map variable name for FsmState sugar: the FSM's current-state SSOT. Required
        /// on FsmState nodes; the compile lowers it to a ReadMapVarInt symbol read and
        /// transitions happen by WriteMapVarInt on the same name inside arm bodies.
        /// </summary>
        public string? StateVar { get; set; }
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
        /// <summary>Value port prefix for FormatText brace pins; full port is arg:{indexOrName}.</summary>
        public const string ArgPrefix = "arg:";

        public static string Case(int caseValue)
            => CasePrefix + caseValue.ToString(CultureInfo.InvariantCulture);

        public static string Arg(int index)
            => ArgPrefix + index.ToString(CultureInfo.InvariantCulture);

        public static string Arg(string name)
            => ArgPrefix + name;

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

        /// <summary>Control port prefix for BtSequence/BtSelector/BtDecorator children; full port is child:{int}.</summary>
        public const string ChildPrefix = "child:";

        public static string Child(int ordinal)
            => ChildPrefix + ordinal.ToString(CultureInfo.InvariantCulture);

        public static bool TryParseChildPort(string port, out int ordinal)
        {
            ordinal = 0;
            if (string.IsNullOrEmpty(port) ||
                !port.StartsWith(ChildPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(
                port.AsSpan(ChildPrefix.Length),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ordinal);
        }
    }
}
