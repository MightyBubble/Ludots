using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.Pathing.Config;

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
    /// 群体地图的判定：合并后的 maps.&lt;mapId&gt;.terrainAreas 非空。
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

    /// <summary>
    /// 地图会话的网格查询范围。未指定（default）表示调用方不是地图会话，查询仍按请求现建。
    /// 地图装载总是指定范围：没有 nav 标签时列表为空，不建查询。
    /// </summary>
    public readonly struct NavMapQueryScope
    {
        public NavMapQueryScope(string mapId, IReadOnlyCollection<string> navProfileIds)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new ArgumentException("Map id is required.", nameof(mapId));
            }

            MapId = mapId;
            NavProfileIds = navProfileIds ?? throw new ArgumentNullException(nameof(navProfileIds));
            IsSpecified = true;
        }

        public string MapId { get; }
        public IReadOnlyCollection<string> NavProfileIds { get; }
        public bool IsSpecified { get; }

        public static string MissingQueryMessage(string mapId, string agentTypeId, string capabilityProfileId)
        {
            if (string.IsNullOrWhiteSpace(capabilityProfileId))
            {
                return $"地图 {mapId} 没有为移动类型 \"{agentTypeId}\" 建立网格查询。";
            }

            return $"地图 {mapId} 没有为移动类型 \"{agentTypeId}\"（能力档 \"{capabilityProfileId}\"）建立网格查询。";
        }
    }

    /// <summary>
    /// 一张地图绑定的移动类型，以及这些类型要列全的 navAreas。
    /// 绑定集 = 体型 navProfileId 落在 maps.&lt;map&gt;.navProfiles 里的 pathing 类型，加上本图群体单位模板点名的 agentTypeId。
    /// 网格查询更窄：地图有 nav 标签，且选择是 PreferMesh / AutoCheapest，且体型 navProfileId 在 navProfiles 里。
    /// </summary>
    public sealed class NavMapAgentBinding
    {
        private readonly HashSet<string> _boundIds;
        private readonly HashSet<string> _navAreaIds;

        private NavMapAgentBinding(
            string mapId,
            bool requiresMeshQueries,
            IReadOnlyList<string> meshQueryProfileIds,
            List<PathingAgentTypeConfig> boundAgents,
            HashSet<string> boundIds,
            List<NavMeshAreaConfig> navAreas,
            HashSet<string> navAreaIds)
        {
            MapId = mapId;
            RequiresMeshQueries = requiresMeshQueries;
            MeshQueryProfileIds = meshQueryProfileIds;
            BoundAgents = boundAgents;
            _boundIds = boundIds;
            NavAreas = navAreas;
            _navAreaIds = navAreaIds;
        }

        public string MapId { get; }
        public bool RequiresMeshQueries { get; }
        public IReadOnlyList<string> MeshQueryProfileIds { get; }
        public IReadOnlyList<PathingAgentTypeConfig> BoundAgents { get; }
        public IReadOnlyList<NavMeshAreaConfig> NavAreas { get; }

        public bool ContainsAgentType(string agentTypeId)
            => !string.IsNullOrWhiteSpace(agentTypeId) && _boundIds.Contains(agentTypeId);

        public bool ContainsNavArea(string areaId)
            => !string.IsNullOrWhiteSpace(areaId) && _navAreaIds.Contains(areaId);

        public void RequireMassNavigationTemplate(EntityTemplate template)
        {
            if (template?.Components == null ||
                !template.Components.TryGetValue("MassNavigationAgent", out JsonNode node) ||
                node is not JsonObject obj)
            {
                return;
            }

            string agentTypeId = null;
            if (obj["agentTypeId"] is JsonValue value && value.TryGetValue<string>(out string parsed))
            {
                agentTypeId = parsed;
            }

            if (ContainsAgentType(agentTypeId))
            {
                return;
            }

            throw new InvalidOperationException(
                $"模板 \"{template.Id}\" 的 agentTypeId \"{agentTypeId}\" 不在地图 {MapId} 绑定的移动类型里。");
        }

        public static NavMapAgentBinding Create(
            string mapId,
            bool navTagged,
            NavMeshBakeConfig navmesh,
            PathingConfig pathing,
            AgentProfileRegistry agentProfiles,
            IReadOnlyCollection<string> crowdAgentTypeIds)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new InvalidOperationException("Nav map binding requires a map id.");
            }

            ArgumentNullException.ThrowIfNull(navmesh);
            ArgumentNullException.ThrowIfNull(pathing);
            ArgumentNullException.ThrowIfNull(agentProfiles);

            NavMapNavBoardsConfig mapNav = null;
            navmesh.Maps?.TryGetValue(mapId, out mapNav);
            IReadOnlyList<string> authoredProfiles = mapNav?.NavProfiles ?? (IReadOnlyList<string>)Array.Empty<string>();
            var navProfileIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < authoredProfiles.Count; i++)
            {
                string id = authoredProfiles[i];
                if (string.IsNullOrWhiteSpace(id) || !navProfileIds.Add(id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navProfiles[{i}] is missing or duplicate.");
                }
            }

            bool hasTerrain = mapNav?.TerrainAreas != null && mapNav.TerrainAreas.Count > 0;
            bool hasCrowd = crowdAgentTypeIds != null && crowdAgentTypeIds.Count > 0;
            bool mustDeclareAreas = navProfileIds.Count > 0 || hasTerrain || hasCrowd;
            List<NavMeshAreaConfig> navAreas = ResolveNavAreas(navmesh, mapNav, mapId, mustDeclareAreas);
            var navAreaIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < navAreas.Count; i++)
            {
                navAreaIds.Add(navAreas[i].Id);
            }

            if (hasTerrain)
            {
                for (int i = 0; i < mapNav.TerrainAreas.Count; i++)
                {
                    NavTerrainAreaBinding terrain = mapNav.TerrainAreas[i];
                    if (terrain == null || !navAreaIds.Contains(terrain.AreaId))
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.maps.{mapId}.terrainAreas[{i}] 的 areaId \"{terrain?.AreaId}\" 不在 maps.{mapId}.navAreas 里。");
                    }
                }
            }

            var pathingById = new Dictionary<string, PathingAgentTypeConfig>(StringComparer.Ordinal);
            if (pathing.AgentTypes != null)
            {
                for (int i = 0; i < pathing.AgentTypes.Count; i++)
                {
                    PathingAgentTypeConfig agent = pathing.AgentTypes[i];
                    if (agent == null || string.IsNullOrWhiteSpace(agent.Id) || !pathingById.TryAdd(agent.Id, agent))
                    {
                        continue;
                    }
                }
            }

            var boundAgents = new List<PathingAgentTypeConfig>();
            var boundIds = new HashSet<string>(StringComparer.Ordinal);
            bool requiresMeshQueries = false;
            foreach (PathingAgentTypeConfig agent in pathingById.Values)
            {
                AgentProfileConfig body = agentProfiles.Require(agent.ProfileId, $"PathingConfig.agentTypes[{agent.Id}]");
                if (!navProfileIds.Contains(body.NavProfileId))
                {
                    continue;
                }

                boundIds.Add(agent.Id);
                boundAgents.Add(agent);
                if (navTagged && SelectsMesh(agent))
                {
                    requiresMeshQueries = true;
                }
            }

            if (crowdAgentTypeIds != null)
            {
                foreach (string crowdId in crowdAgentTypeIds)
                {
                    if (string.IsNullOrWhiteSpace(crowdId) || !boundIds.Add(crowdId))
                    {
                        continue;
                    }

                    if (!pathingById.TryGetValue(crowdId, out PathingAgentTypeConfig agent))
                    {
                        throw new InvalidOperationException(
                            $"地图 {mapId} 的群体单位引用了移动类型 \"{crowdId}\"，但它不在 Navigation/pathing.json 的 agentTypes 里。");
                    }

                    boundAgents.Add(agent);
                }
            }

            IReadOnlyList<string> meshProfiles = navTagged
                ? authoredProfiles
                : Array.Empty<string>();
            return new NavMapAgentBinding(
                mapId,
                requiresMeshQueries,
                meshProfiles,
                boundAgents,
                boundIds,
                navAreas,
                navAreaIds);
        }

        public static bool SelectsMesh(PathingAgentTypeConfig agent)
        {
            PathSelectionMode? mode = agent?.Selection?.Mode;
            return mode == PathSelectionMode.PreferMesh || mode == PathSelectionMode.AutoCheapest;
        }

        private static List<NavMeshAreaConfig> ResolveNavAreas(
            NavMeshBakeConfig navmesh,
            NavMapNavBoardsConfig mapNav,
            string mapId,
            bool required)
        {
            IReadOnlyList<string> authored = mapNav?.NavAreas;
            if (!required && (authored == null || authored.Count == 0))
            {
                return new List<NavMeshAreaConfig>();
            }

            if (authored == null || authored.Count == 0)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navAreas 必须是非空的区域 id 数组。");
            }

            var byId = new Dictionary<string, NavMeshAreaConfig>(StringComparer.Ordinal);
            if (navmesh.Areas != null)
            {
                for (int i = 0; i < navmesh.Areas.Count; i++)
                {
                    NavMeshAreaConfig area = navmesh.Areas[i]
                        ?? throw new InvalidOperationException("NavMeshBakeConfig.areas contains null.");
                    if (string.IsNullOrWhiteSpace(area.Id) || !byId.TryAdd(area.Id, area))
                    {
                        throw new InvalidOperationException($"NavMeshBakeConfig.areas contains a missing or duplicate id '{area.Id}'.");
                    }
                }
            }

            var selected = new List<NavMeshAreaConfig>(authored.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < authored.Count; i++)
            {
                string id = authored[i];
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navAreas[{i}] is missing or duplicate.");
                }

                if (!byId.TryGetValue(id, out NavMeshAreaConfig area))
                {
                    throw new InvalidOperationException(
                        $"NavMeshBakeConfig.maps.{mapId}.navAreas '{id}' is not a NavMeshBakeConfig.areas id.");
                }

                selected.Add(area);
            }

            return selected;
        }
    }

    public static class NavSessionBinding
    {
        private static NavMapAgentBinding _current;

        public static void Set(NavMapAgentBinding binding) => _current = binding;

        public static void Clear() => _current = null;

        public static void RequireMassNavigationTemplate(EntityTemplate template)
        {
            _current?.RequireMassNavigationTemplate(template);
        }
    }
}
