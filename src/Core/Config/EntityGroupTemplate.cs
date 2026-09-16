using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ludots.Core.Config
{
    public class EntityGroupTemplate : IIdentifiable
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("slots")]
        public List<EntityGroupSlot>? Slots { get; set; }
    }

    public sealed class EntityGroupSlot
    {
        [JsonPropertyName("localId")]
        public string LocalId { get; set; }

        [JsonPropertyName("template")]
        public string? Template { get; set; }

        [JsonPropertyName("group")]
        public string? Group { get; set; }

        [JsonPropertyName("localPose")]
        public EntityTemplateLocalPose? LocalPose { get; set; }

        [JsonPropertyName("componentOverrides")]
        public Dictionary<string, JsonNode>? ComponentOverrides { get; set; }
    }
}
