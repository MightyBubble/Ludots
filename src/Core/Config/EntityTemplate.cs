using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public class EntityTemplate : IIdentifiable
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("onSpawnEffect")]
        public string OnSpawnEffect { get; set; }

        // Map of ComponentName -> JsonObject Data
        [JsonPropertyName("components")]
        public Dictionary<string, JsonNode> Components { get; set; } = new Dictionary<string, JsonNode>();

        /// <summary>
        /// Entity-domain TriggerGraph mounts authored on the template (graph ids).
        /// Strict parsing (non-null array of trimmed non-empty strings) happens at
        /// template load; unknown graph names fail closed at mount time.
        /// </summary>
        [JsonPropertyName("TriggerGraphs")]
        public List<string>? TriggerGraphs { get; set; }
    }
}
