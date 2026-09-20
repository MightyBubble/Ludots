using System;
using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public static class WorldUnits
    {
        public const float CmPerMeter = 100f;

        public const float MetersPerCm = 1f / CmPerMeter;

        public static float CmToM(float cm) => cm * MetersPerCm;
        public static float CmToM(int cm) => cm * MetersPerCm;

        public static int MToCm(float meters) => (int)MathF.Round(meters * CmPerMeter);

        public static Vector2 CmToM(Vector2 cm) => cm / CmPerMeter;
        public static Vector3 CmToM(Vector3 cm) => cm / CmPerMeter;

        public static Vector2 MToCm(Vector2 meters) => meters * CmPerMeter;
        public static Vector3 MToCm(Vector3 meters) => meters * CmPerMeter;

        public static Vector3 WorldCmToVisualMeters(in WorldCmInt2 worldCm, float yMeters = 0f)
        {
            return new Vector3(CmToM(worldCm.X), yMeters, CmToM(worldCm.Y));
        }

        public static Vector3 WorldCmToVisualMeters(float worldXCm, float worldYCm, float yMeters = 0f)
        {
            return new Vector3(CmToM(worldXCm), yMeters, CmToM(worldYCm));
        }

        /// <summary>


        public static WorldCmInt2 VisualMetersToWorldCm(in Vector3 visualMeters)
        {
            return new WorldCmInt2(MToCm(visualMeters.X), MToCm(visualMeters.Z));
        }

        public static Vector2 VisualMetersToWorldCm2(in Vector3 visualMeters)
        {
            return new Vector2(visualMeters.X * CmPerMeter, visualMeters.Z * CmPerMeter);
        }
    }
}
