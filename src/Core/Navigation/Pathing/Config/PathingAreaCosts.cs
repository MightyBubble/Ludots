using System;

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
    /// cost 与 speedDivisor 的唯一读取处。speedDivisor 缺省等于 cost（schema 默认）。
    /// 两者都必须 &gt; 0 且不是 NaN。passable 缺省为 true；未列出的区域不进这张表，调用方按代价 1、可通行处理。
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
    }
}
