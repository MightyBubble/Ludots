using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ludots.Core.Modding
{
    public class ModManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("main")]
        public string Main { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 0;

        [JsonPropertyName("dependencies")]
        public Dictionary<string, string> Dependencies { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("changelog")]
        public string Changelog { get; set; }

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; }
    }
}
