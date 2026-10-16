using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public readonly struct ResolvedPathingAreaCost
    {
        public ResolvedPathingAreaCost(double cost, bool passable)
        {
            Cost = cost;
            Passable = passable;
        }

        public double Cost { get; }
        public bool Passable { get; }
    }

    /// <summary>
    /// cost 与 speedDivisor 的唯一读取处。speedDivisor 缺省等于 cost。
    /// 两者都必须 &gt; 0 且不是 NaN。passable 缺省为 true。
    /// 调用方传入的区域表就是合同（地图的 navAreas，不是全局目录）。
    /// 绑定的移动类型必须列出其中每一个区域；排除写 passable: false，不能省略行。
    /// </summary>
    public static class PathingAreaCosts
    {
        public static ResolvedPathingAreaCost Resolve(PathingAreaCostConfig area)
        {
            ArgumentNullException.ThrowIfNull(area);
            if (area.Cost <= 0d || double.IsNaN(area.Cost))
            {
                throw new InvalidOperationException($"Invalid pathing cost for areaId={area.AreaId}");
            }

            return new ResolvedPathingAreaCost(area.Cost, area.Passable);
        }

        public static void RequireComplete(PathingAgentTypeConfig agent, IReadOnlyList<NavMeshAreaConfig> areas)
        {
            ArgumentNullException.ThrowIfNull(agent);
            if (string.IsNullOrWhiteSpace(agent.Id))
            {
                throw new InvalidOperationException("PathingConfig.agentTypes 缺少 id。");
            }

            var listed = new HashSet<int>();
            IReadOnlyList<PathingAreaCostConfig> costs = agent.NavMesh?.AreaCosts;
            if (costs != null)
            {
                for (int i = 0; i < costs.Count; i++)
                {
                    if (costs[i] != null)
                    {
                        listed.Add(costs[i].AreaId);
                    }
                }
            }

            if (areas == null)
            {
                throw new InvalidOperationException(
                    $"PathingConfig.agentTypes[{agent.Id}] 的区域表为 null。绑定的移动类型必须列出地图 navAreas 里的每一个区域；排除写 passable: false。");
            }

            for (int i = 0; i < areas.Count; i++)
            {
                NavMeshAreaConfig area = areas[i];
                if (area == null || listed.Contains(area.AreaId))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"PathingConfig.agentTypes[{agent.Id}] 缺少 areaId {area.AreaId}（{area.Id}）。排除区域写 passable: false，不能省略行。");
            }
        }
    }
}
