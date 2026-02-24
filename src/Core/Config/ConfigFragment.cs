using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public readonly struct ConfigFragment
    {
        public readonly JsonNode Node;
        public readonly string SourceUri;

        public ConfigFragment(JsonNode node, string sourceUri)
        {
            Node = node;
            SourceUri = sourceUri;
        }
    }
}

