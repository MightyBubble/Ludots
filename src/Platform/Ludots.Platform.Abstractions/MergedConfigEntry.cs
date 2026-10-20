using System.Text.Json.Nodes;

namespace Ludots.Platform.Abstractions
{
    public readonly struct MergedConfigEntry
    {
        public readonly string Id;
        public readonly JsonObject Node;
        public readonly string SourceUri;

        public MergedConfigEntry(string id, JsonObject node, string sourceUri = "")
        {
            Id = id;
            Node = node;
            SourceUri = sourceUri ?? "";
        }
    }
}
