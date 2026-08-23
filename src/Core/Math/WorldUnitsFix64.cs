using System.Numerics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Mathematics
{
    public static class WorldUnitsFix64
    {
        /// Fix64Vec2 厘米 → 浮点米 (渲染边界转换)
        /// </summary>
        public static Vector3 WorldCmToVisualMeters(in Fix64Vec2 worldCmFix64, float yMeters = 0f)
        {
            return new Vector3(WorldUnits.CmToM(worldCmFix64.X.ToFloat()), yMeters, WorldUnits.CmToM(worldCmFix64.Y.ToFloat()));
        }
    }
}
