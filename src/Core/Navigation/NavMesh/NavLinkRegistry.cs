using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// 按地图持有的 Link 注册表。
    ///
    /// 与 <see cref="NavQueryServiceRegistry"/> 同层同职责：持有已编译的 Link 图，
    /// 并提供按 (layer, profile) 的可用性查询。未为某地图声明 Link 时返回 false，
    /// 查询方必须把"该地图没有跨表面连接"当作合法结论，而不是退化为直线。
    /// </summary>
    public sealed class NavLinkRegistry
    {
        private readonly Dictionary<string, NavLinkGraph> _graphsByMap;

        public NavLinkRegistry(Dictionary<string, NavLinkGraph> graphsByMap)
        {
            _graphsByMap = graphsByMap ?? throw new ArgumentNullException(nameof(graphsByMap));
        }

        public int MapCount => _graphsByMap.Count;

        public bool TryGetGraph(string mapId, out NavLinkGraph graph)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                graph = null!;
                return false;
            }

            return _graphsByMap.TryGetValue(mapId, out graph!);
        }

        public NavLinkGraph RequireGraph(string mapId)
        {
            if (!TryGetGraph(mapId, out NavLinkGraph graph))
            {
                throw new InvalidOperationException($"NavLinkRegistry has no link graph for map '{mapId}'.");
            }

            return graph;
        }

        /// <summary>
        /// 按地图构建注册表。只有显式声明了 links 的地图才会得到图；
        /// 未声明地图不出现在注册表中（调用方据此得出"无跨表面连接"）。
        /// </summary>
        public static NavLinkRegistry Build(
            NavMeshBakeConfig config,
            NavMeshProfileRegistry profiles,
            AgentProfileRegistry agentProfiles,
            NavLayerResolver layers,
            string mapId)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            if (agentProfiles == null) throw new ArgumentNullException(nameof(agentProfiles));
            if (layers == null) throw new ArgumentNullException(nameof(layers));
            if (string.IsNullOrWhiteSpace(mapId)) throw new ArgumentException("mapId is required.", nameof(mapId));

            var graphs = new Dictionary<string, NavLinkGraph>(StringComparer.Ordinal);
            if (config.Links != null && config.Links.Count > 0)
            {
                graphs[mapId] = new NavLinkGraph(config.Links, profiles, agentProfiles, layers.RequireLayer);
            }

            return new NavLinkRegistry(graphs);
        }
    }
}
