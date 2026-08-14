using System;

namespace RoadNetworkShowcaseMod
{
    internal static class RoadNetworkShowcaseIds
    {
        public const string MapId = "road_network_showcase_chunked";
        public const string MapTag = "road_network_showcase";
        public const string InstalledKey = "RoadNetworkShowcaseMod.Installed";
        public const string ScenarioServiceKey = "RoadNetworkShowcaseMod.Scenario";
        public const string GraphLoadedChunksServiceKey = "RoadNetworkShowcaseMod.GraphLoadedChunks";
        public const string PathPlannerAgentTypeId = "RoadColumn";
        public const string RoadMoveFollowOrderTypeKey = "roadMoveFollow";
        public const string RoadSurfacePresenterId = "road_surface_chunk";

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
