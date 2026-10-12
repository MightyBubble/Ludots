using System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public static class PathingAreaCostCompiler
    {
        public static NavAreaCostTable Compile(PathingNavMeshConfig cfg)
        {
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

                    if (area.Cost <= 0f || float.IsNaN(area.Cost))
                    {
                        throw new InvalidOperationException($"Invalid pathing cost for areaId={area.AreaId}");
                    }

                    float speedDivisor = area.SpeedDivisor ?? area.Cost;
                    if (speedDivisor <= 0f || float.IsNaN(speedDivisor))
                    {
                        throw new InvalidOperationException($"Invalid pathing speedDivisor for areaId={area.AreaId}");
                    }

                    costs[area.AreaId] = Fix64.FromFloat(area.Cost);
                    if (area.Passable == false)
                    {
                        impassable[area.AreaId] = true;
                    }
                }
            }

            return new NavAreaCostTable(costs, impassable);
        }

        public static Fix64 SpeedDivisor(PathingAreaCostConfig area)
        {
            float value = area.SpeedDivisor ?? area.Cost;
            return Fix64.FromFloat(value);
        }
    }
}
