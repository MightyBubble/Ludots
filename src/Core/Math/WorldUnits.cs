using System;
using System.Numerics;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Mathematics
{
    public static class WorldUnits
    {
        public const float CmPerMeter = 100f;

        public static float CmToM(float cm) => cm / CmPerMeter;
        public static float CmToM(int cm) => cm / CmPerMeter;

        public static int MToCm(float meters) => (int)MathF.Round(meters * CmPerMeter);

        public static Vector2 CmToM(Vector2 cm) => cm / CmPerMeter;
        public static Vector3 CmToM(Vector3 cm) => cm / CmPerMeter;

        public static Vector2 MToCm(Vector2 meters) => meters * CmPerMeter;
        public static Vector3 MToCm(Vector3 meters) => meters * CmPerMeter;

        public static Vector3 WorldCmToVisualMeters(in WorldCmInt2 worldCm, float yMeters = 0f)
        {
            return new Vector3(CmToM(worldCm.X), yMeters, CmToM(worldCm.Y));
        }

        /// <summary>
        /// Fix64Vec2 厘米 → 浮点米 (渲染边界转换)
        /// </summary>
        public static Vector3 WorldCmToVisualMeters(in Fix64Vec2 worldCmFix64, float yMeters = 0f)
        {
            return new Vector3(worldCmFix64.X.ToFloat() * 0.01f, yMeters, worldCmFix64.Y.ToFloat() * 0.01f);
        }

        public static WorldCmInt2 VisualMetersToWorldCm(in Vector3 visualMeters)
        {
            return new WorldCmInt2(MToCm(visualMeters.X), MToCm(visualMeters.Z));
        }
    }
}
