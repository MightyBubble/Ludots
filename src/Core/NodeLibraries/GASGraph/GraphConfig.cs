using System.Collections.Generic;
using Ludots.Core.Config;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public sealed class GraphConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = "Effect";
        public string Entry { get; set; } = string.Empty;
        public List<GraphNodeConfig> Nodes { get; set; } = new();
        public List<GraphOutputConfig> Outputs { get; set; } = new();
    }

    public sealed class GraphNodeConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty;
        public string? Next { get; set; }
        public List<string> Inputs { get; set; } = new();

        public float FloatValue { get; set; }
        public int IntValue { get; set; }
        public bool BoolValue { get; set; }

        public string? Tag { get; set; }
        public string? Attribute { get; set; }
        public string? Template { get; set; }
        public string? CollectionKey { get; set; }
        public string? EffectTemplate { get; set; }
        public string? BlackboardKey { get; set; }
        public string? ConfigKey { get; set; }
        public string? ValidOutput { get; set; }

        public float Radius { get; set; }
        public float RangeCm { get; set; }
        public int DirectionDeg { get; set; }
        public int HalfAngleDeg { get; set; }
        public int LengthCm { get; set; }
        public int HalfWidthCm { get; set; }
        public int HalfHeightCm { get; set; }
        public int RotationDeg { get; set; }
        public int HexRadius { get; set; }
        public uint LayerMask { get; set; }
        public string? RelationshipMode { get; set; }
        public int Limit { get; set; }
        public int TeamId { get; set; }
        public string? Sort { get; set; }
        public string? RelationshipType { get; set; }
        public string? Metric { get; set; }
        public string? Flag { get; set; }
        public string? Reason { get; set; }
        public string? PayloadPreset { get; set; }
        public string? BuiltinHandler { get; set; }
        public bool Descending { get; set; }
        /// <summary>Event payload slot index for LoadEventPayloadInt (0..1) / LoadEventPayloadFloat (0..3).</summary>
        public int Slot { get; set; }
    }

    public sealed class GraphOutputConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Destination { get; set; } = nameof(GraphOutputDestinationKind.Summary);
        public string Type { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string CollectionKey { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }
}
