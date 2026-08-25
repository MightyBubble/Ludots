using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Fields.Config
{
    /// <summary>Per-layer entry of <c>Fields/layers.json</c>; strict shape validation happens in <see cref="FieldLayerConfigLoader"/>.</summary>
    public sealed class FieldLayerConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int? CellSizeCm { get; set; }
        public int? ChunkSizeCells { get; set; }

        [JsonPropertyName("default")]
        public JsonNode? Default { get; set; }

        public int? UpdateHz { get; set; }
        public bool? Persistent { get; set; }
        public string WriterDomain { get; set; } = string.Empty;
        public int? MaxRegionIds { get; set; }
    }
}
