using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.Pathing.Config;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public static class NavMapDeclaration
    {
        public static PathingConfig SelectAgentTypes(PathingConfig catalog, IReadOnlyList<string> agentTypeIds, string mapId)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            if (catalog.AgentTypes == null || catalog.AgentTypes.Count == 0)
            {
                throw new InvalidOperationException("PathingConfig.agentTypes is empty.");
            }

            if (agentTypeIds == null || agentTypeIds.Count == 0)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.agentTypes is required.");
            }

            var byId = new Dictionary<string, PathingAgentTypeConfig>(catalog.AgentTypes.Count, StringComparer.Ordinal);
            for (int i = 0; i < catalog.AgentTypes.Count; i++)
            {
                PathingAgentTypeConfig agent = catalog.AgentTypes[i]
                    ?? throw new InvalidOperationException("PathingConfig.agentTypes contains null.");
                if (string.IsNullOrWhiteSpace(agent.Id) || !byId.TryAdd(agent.Id, agent))
                {
                    throw new InvalidOperationException($"PathingConfig.agentTypes contains a missing or duplicate id '{agent.Id}'.");
                }
            }

            var selected = new List<PathingAgentTypeConfig>(agentTypeIds.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < agentTypeIds.Count; i++)
            {
                string id = agentTypeIds[i];
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.agentTypes[{i}] is missing or duplicate.");
                }

                if (!byId.TryGetValue(id, out PathingAgentTypeConfig agent))
                {
                    throw new InvalidOperationException(
                        $"NavMeshBakeConfig.maps.{mapId}.agentTypes '{id}' is not a PathingConfig.agentTypes id.");
                }

                selected.Add(agent);
            }

            return new PathingConfig { AgentTypes = selected };
        }

        public static List<NavMeshAgentProfileConfig> CapabilityProfiles(
            NavMeshBakeConfig navmesh,
            PathingConfig selectedAgents,
            AgentProfileRegistry bodies)
        {
            ArgumentNullException.ThrowIfNull(navmesh);
            ArgumentNullException.ThrowIfNull(selectedAgents);
            ArgumentNullException.ThrowIfNull(bodies);
            var byId = new Dictionary<string, NavMeshAgentProfileConfig>(navmesh.Profiles.Count, StringComparer.Ordinal);
            for (int i = 0; i < navmesh.Profiles.Count; i++)
            {
                NavMeshAgentProfileConfig profile = navmesh.Profiles[i]
                    ?? throw new InvalidOperationException("NavMeshBakeConfig.profiles contains null.");
                if (!byId.TryAdd(profile.Id, profile))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profiles contains duplicate id '{profile.Id}'.");
                }
            }

            var caps = new List<NavMeshAgentProfileConfig>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < selectedAgents.AgentTypes.Count; i++)
            {
                PathingAgentTypeConfig agent = selectedAgents.AgentTypes[i];
                AgentProfileConfig body = bodies.Require(agent.ProfileId, $"PathingConfig.agentTypes[{agent.Id}]");
                if (!byId.TryGetValue(body.NavProfileId, out NavMeshAgentProfileConfig capability))
                {
                    throw new InvalidOperationException(
                        $"PathingConfig.agentTypes[{agent.Id}] agent profile '{body.Id}' references nav profile '{body.NavProfileId}', which is not in NavMeshBakeConfig.profiles.");
                }

                if (seen.Add(capability.Id))
                {
                    caps.Add(capability);
                }
            }

            if (caps.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.maps agentTypes does not reference any nav profile.");
            }

            NavSchemaOrder.SortByKey(caps, static profile => profile.Id);
            return caps;
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
    /// 群体地图的临时判定只在这一处：某块板声明了 surfaceAsset。
    /// 随后会改成地图上的显式求解器字段。
    /// </summary>
    public static class CrowdMapDeclaration
    {
        public static bool IsCrowdMap(NavMeshBakeConfig config, string mapId)
            => TrySurfaceAsset(config, mapId, out _);

        public static bool TrySurfaceAsset(NavMeshBakeConfig config, string mapId, out string surfaceAsset)
        {
            surfaceAsset = null;
            if (config?.Maps == null || !config.Maps.TryGetValue(mapId, out NavMapNavBoardsConfig map) || map?.Boards == null)
            {
                return false;
            }

            string foundName = null;
            foreach ((string name, NavTileGridConfig board) in map.Boards)
            {
                if (board == null || string.IsNullOrWhiteSpace(board.SurfaceAsset))
                {
                    continue;
                }

                if (foundName != null)
                {
                    throw new InvalidOperationException(
                        $"NavMeshBakeConfig.maps.{mapId}.boards 有多块板声明 surfaceAsset（{foundName}、{name}）。");
                }

                foundName = name;
                surfaceAsset = board.SurfaceAsset;
            }

            return foundName != null;
        }
    }
}
