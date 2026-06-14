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
    }
}
