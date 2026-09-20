using System;
using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public readonly struct VisualGroundHit
    {
        private const float CmToM = 0.01f;

        public VisualGroundHit(float worldXCm, float worldYCm, float heightCm, int layerIndex)
            : this(worldXCm, worldYCm, heightCm, layerIndex, distanceMeters: 0f, Vector3.UnitY)
        {
        }

        public VisualGroundHit(float worldXCm, float worldYCm, float heightCm, int layerIndex, float distanceMeters, Vector3 normal)
        {
            WorldXCm = worldXCm;
            WorldYCm = worldYCm;
            HeightCm = heightCm;
            LayerIndex = layerIndex;
            DistanceMeters = distanceMeters;
            Normal = normal;
        }

        public float WorldXCm { get; }

        public float WorldYCm { get; }

        public float HeightCm { get; }

        public int LayerIndex { get; }

        public float DistanceMeters { get; }

        public Vector3 Normal { get; }

        public Vector3 ToVisualMeters()
        {
            return new Vector3(WorldXCm * CmToM, HeightCm * CmToM, WorldYCm * CmToM);
        }

        public WorldCmInt2 ToWorldCmInt2()
        {
            return new WorldCmInt2(
                (int)MathF.Round(WorldXCm, MidpointRounding.AwayFromZero),
                (int)MathF.Round(WorldYCm, MidpointRounding.AwayFromZero));
        }
    }
}
