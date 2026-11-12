using System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public static class PathingAreaCostCompiler
    {
        public static NavAreaCostTable Compile(PathingNavMeshConfig cfg)
        {
            // Detour 的区域地址是 0..255。已声明区域必须先经 PathingAreaCosts.RequireComplete，
            // 这里的 1 只填那些没有对应声明的槽，不是“省略行 = 代价 1”。
            var costs = new Fix64[256];
            var impassable = new bool[256];
            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = Fix64.OneValue;
            }

            if (cfg?.AreaCosts != null)
            {
                for (int i = 0; i < cfg.AreaCosts.Count; i++)
                {
                    PathingAreaCostConfig area = cfg.AreaCosts[i];
                    if (area == null)
                    {
                        continue;
                    }

                    if (area.AreaId < 0 || area.AreaId > 255)
                    {
                        throw new InvalidOperationException($"Invalid pathing areaId: {area.AreaId}");
                    }

                    ResolvedPathingAreaCost resolved = PathingAreaCosts.Resolve(area);
                    costs[area.AreaId] = Fix64.FromDouble(resolved.Cost);
                    if (!resolved.Passable)
                    {
                        impassable[area.AreaId] = true;
                    }
                }
            }

            return new NavAreaCostTable(costs, impassable);
        }
    }
}
