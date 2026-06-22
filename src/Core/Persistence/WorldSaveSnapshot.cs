using System.Text.Json.Nodes;

namespace Ludots.Core.Persistence
{
    public sealed record WorldSaveSnapshot(
        SaveContextHeader Header,
        JsonObject Domains,
        byte[] WorldBytes);
}
