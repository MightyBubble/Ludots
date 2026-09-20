using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Fields.Config
{
    /// <summary>Authoring payload of <c>Fields/cells/&lt;layerId&gt;.json</c>; strict shape validation happens in <see cref="FieldCellsConfigLoader"/>.</summary>
    public sealed class FieldCellsConfig
    {
        public string Layer { get; set; } = string.Empty;
        public List<string> Regions { get; set; } = new List<string>();

        /// <summary>Array of inclusive [x0, y0, x1, y1, regionId].</summary>
        [JsonPropertyName("rects")]
        public JsonNode? Rects { get; set; }

        /// <summary>Optional array of [x, y, regionId] sparse leftovers that are not worth a rect.</summary>
        [JsonPropertyName("points")]
        public JsonNode? Points { get; set; }
    }
}
