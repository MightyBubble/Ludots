using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Fields.Config
{
    /// <summary>Authoring payload of <c>Fields/cells/&lt;layerId&gt;.json</c>; strict shape validation happens in <see cref="FieldCellsConfigLoader"/>.</summary>
    public sealed class FieldCellsConfig
    {
        public int SchemaVersion { get; set; }
        public string Layer { get; set; } = string.Empty;
        public List<string> Regions { get; set; } = new List<string>();

        [JsonPropertyName("cells")]
        public JsonNode? Cells { get; set; }
    }
}
