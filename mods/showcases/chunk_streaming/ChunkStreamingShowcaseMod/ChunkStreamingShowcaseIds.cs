using System;

namespace ChunkStreamingShowcaseMod
{
    internal static class ChunkStreamingShowcaseIds
    {
        public const string MapId = "chunk_streaming_showcase";
        public const string InstalledKey = "ChunkStreamingShowcaseMod.Installed";
        public const string ScenarioServiceKey = "ChunkStreamingShowcaseMod.Scenario";

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
