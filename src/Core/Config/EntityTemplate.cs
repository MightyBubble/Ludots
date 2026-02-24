using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public class EntityTemplate : IIdentifiable
    {
        public string Id { get; set; }
        
        // Map of ComponentName -> JsonObject Data
        public Dictionary<string, JsonNode> Components { get; set; } = new Dictionary<string, JsonNode>();
    }
}
