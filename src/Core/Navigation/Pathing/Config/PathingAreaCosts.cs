using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public readonly struct ResolvedPathingAreaCost
    {
        public ResolvedPathingAreaCost(double cost, double speedDivisor, bool passable)
        {
            Cost = cost;
            SpeedDivisor = speedDivisor;
            Passable = passable;
        }

        public double Cost { get; }
        public double SpeedDivisor { get; }
        public bool Passable { get; }
    }

    /// <summary>
    /// cost 与 speedDivisor 的唯一读取处。speedDivisor 缺省等于 cost。
    /// 两者都必须 &gt; 0 且不是 NaN。passable 缺省为 true。
    /// 每个 agent type 必须列出每一个已声明区域；排除写 passable: false，不能省略行。
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

            double speedDivisor = area.SpeedDivisor ?? area.Cost;
            if (speedDivisor <= 0d || double.IsNaN(speedDivisor))
            {
                throw new InvalidOperationException($"Invalid pathing speedDivisor for areaId={area.AreaId}");
            }

            return new ResolvedPathingAreaCost(area.Cost, speedDivisor, area.Passable != false);
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
                return;
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
