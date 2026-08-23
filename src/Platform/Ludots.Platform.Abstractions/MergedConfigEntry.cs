using System.Text.Json.Nodes;

namespace Ludots.Platform.Abstractions
{
    public readonly struct MergedConfigEntry
    {
        public readonly string Id;
        public readonly JsonObject Node;

        public MergedConfigEntry(string id, JsonObject node)
        {
            Id = id;
            Node = node;
        }
    }
}
