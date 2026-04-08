using System;
using System.Numerics;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Terrain
{
    public readonly struct VisualGroundHit
    {
        private const float CmToM = 0.01f;

        public VisualGroundHit(float worldXCm, float worldYCm, float heightCm, int layerIndex)
        {
            WorldXCm = worldXCm;
            WorldYCm = worldYCm;
            HeightCm = heightCm;
            LayerIndex = layerIndex;
        }

        public float WorldXCm { get; }

        public float WorldYCm { get; }

        public float HeightCm { get; }

        public int LayerIndex { get; }

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
