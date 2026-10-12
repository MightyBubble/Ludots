using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public static class NavMapDeclaration
    {
        /// <summary>
        /// 按 maps.&lt;mapId&gt;.navProfiles 的数组顺序取出能力档。不按键名重排。
        /// </summary>
        public static List<NavMeshAgentProfileConfig> ResolveNavProfiles(
            NavMeshBakeConfig navmesh,
            IReadOnlyList<string> navProfileIds,
            string mapId)
        {
            ArgumentNullException.ThrowIfNull(navmesh);
            if (navmesh.Profiles == null || navmesh.Profiles.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.profiles is empty.");
            }

            if (navProfileIds == null || navProfileIds.Count == 0)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navProfiles is required.");
            }

            var byId = new Dictionary<string, NavMeshAgentProfileConfig>(navmesh.Profiles.Count, StringComparer.Ordinal);
            for (int i = 0; i < navmesh.Profiles.Count; i++)
            {
                NavMeshAgentProfileConfig profile = navmesh.Profiles[i]
                    ?? throw new InvalidOperationException("NavMeshBakeConfig.profiles contains null.");
                if (string.IsNullOrWhiteSpace(profile.Id) || !byId.TryAdd(profile.Id, profile))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profiles contains a missing or duplicate id '{profile.Id}'.");
                }
            }

            var selected = new List<NavMeshAgentProfileConfig>(navProfileIds.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < navProfileIds.Count; i++)
            {
                string id = navProfileIds[i];
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navProfiles[{i}] is missing or duplicate.");
                }

                if (!byId.TryGetValue(id, out NavMeshAgentProfileConfig profile))
                {
                    throw new InvalidOperationException(
                        $"NavMeshBakeConfig.maps.{mapId}.navProfiles '{id}' is not a NavMeshBakeConfig.profiles id.");
                }

                selected.Add(profile);
            }

            return selected;
        }

        public static NavMeshBakeConfig WithProfiles(NavMeshBakeConfig source, List<NavMeshAgentProfileConfig> profiles)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new NavMeshBakeConfig
            {
                Mode = source.Mode,
                Algorithm = source.Algorithm,
                Profiles = profiles,
                Layers = source.Layers,
                Areas = source.Areas,
                Maps = source.Maps,
                RuntimeIncremental = source.RuntimeIncremental,
                TerrainFeed = source.TerrainFeed,
            };
        }
    }

    /// <summary>
    /// 群体地图的临时判定：合并后的 maps.&lt;mapId&gt;.terrainAreas 非空。
    /// 随后会改成地图上的显式求解器字段。
    /// </summary>
    public static class CrowdMapDeclaration
    {
        public static bool IsCrowdMap(JsonObject mergedNavmesh, string mapId)
        {
            if (mergedNavmesh == null || string.IsNullOrWhiteSpace(mapId))
            {
                return false;
            }

            if (mergedNavmesh["maps"] is not JsonObject maps ||
                !maps.TryGetPropertyValue(mapId, out JsonNode mapNode) ||
                mapNode is not JsonObject mapObj)
            {
                return false;
            }

            return mapObj["terrainAreas"] is JsonArray terrainAreas && terrainAreas.Count > 0;
        }
    }
}
