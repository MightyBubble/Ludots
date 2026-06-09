using System;

namespace Ludots.Tool
{
    internal readonly struct MountainRiverFixtureSample
    {
        public MountainRiverFixtureSample(int heightCm, int waterHeightCm, byte areaId, bool blocked)
        {
            HeightCm = heightCm;
            WaterHeightCm = waterHeightCm;
            AreaId = areaId;
            Blocked = blocked;
        }

        public int HeightCm { get; }
        public int WaterHeightCm { get; }
        public byte AreaId { get; }
        public bool Blocked { get; }
    }

    internal static class MountainRiverFixtureTerrain
    {
        private const byte DefaultArea = 0;
        private const byte RoadArea = 1;
        private const byte ForestArea = 2;
        private const byte MountainSlopeArea = 3;
        private const byte ShallowWaterArea = 4;
        private const byte DeepWaterArea = 5;
        private const byte NoFlyArea = 6;

        public static MountainRiverFixtureSample Sample(int width, int height, int xCell, int yCell)
        {
            float x = width <= 1 ? 0f : xCell / (float)(width - 1);
            float y = height <= 1 ? 0f : yCell / (float)(height - 1);
            float riverCenter = ResolveRiverCenter(y);
            float riverDistance = MathF.Abs(x - riverCenter);

            if (IsGroundCorridor(x, y, riverDistance))
            {
                int roadWave = (((xCell / 96) + (yCell / 96)) & 1) == 0 ? 0 : 18;
                return new MountainRiverFixtureSample(580 + roadWave, 0, RoadArea, blocked: false);
            }

            if (riverDistance < 0.035f)
            {
                return new MountainRiverFixtureSample(160, 360, DeepWaterArea, blocked: false);
            }

            if (riverDistance < 0.070f)
            {
                return new MountainRiverFixtureSample(360, 300, ShallowWaterArea, blocked: false);
            }

            float left = RidgeValue(x, y, 0.20f, 0.42f, 0.11f, 0.34f);
            float right = RidgeValue(x, y, 0.78f, 0.55f, 0.12f, 0.42f);
            float peak = MathF.Max(left, right);
            int baseCm = 520 + ((((xCell / 40) + (yCell / 52)) & 1) == 0 ? 0 : 70);
            int heightCm = baseCm + (int)MathF.Round(peak * 1150f);

            if (peak > 0.92f)
            {
                return new MountainRiverFixtureSample(heightCm, 0, NoFlyArea, blocked: true);
            }

            if (peak > 0.56f)
            {
                return new MountainRiverFixtureSample(heightCm, 0, MountainSlopeArea, blocked: false);
            }

            if (peak > 0.36f)
            {
                return new MountainRiverFixtureSample(heightCm, 0, ForestArea, blocked: false);
            }

            return new MountainRiverFixtureSample(heightCm, 0, DefaultArea, blocked: false);
        }

        public static byte ToVertexHeightByte(int heightCm)
        {
            return (byte)Math.Clamp((heightCm + 50) / 100, 0, 15);
        }

        public static byte ToVertexWaterByte(int waterHeightCm)
        {
            return (byte)Math.Clamp((waterHeightCm + 50) / 100, 0, 15);
        }

        private static float ResolveRiverCenter(float y)
        {
            return 0.48f + 0.18f * MathF.Sin((y * 3.6f + 0.15f) * MathF.PI);
        }

        private static bool IsGroundCorridor(float x, float y, float riverDistance)
        {
            return IsBand(y, 0.10f, 0.022f) ||
                IsBand(y, 0.24f, 0.022f) ||
                IsBand(y, 0.38f, 0.022f) ||
                IsBand(y, 0.52f, 0.022f) ||
                IsBand(y, 0.66f, 0.022f) ||
                IsBand(y, 0.80f, 0.022f) ||
                IsBand(y, 0.92f, 0.022f) ||
                IsBand(x, 0.08f, 0.018f) ||
                IsBand(x, 0.24f, 0.018f) ||
                IsBand(x, 0.40f, 0.018f) ||
                IsBand(x, 0.56f, 0.018f) ||
                IsBand(x, 0.72f, 0.018f) ||
                IsBand(x, 0.88f, 0.018f) ||
                (riverDistance < 0.085f && (
                    IsBand(y, 0.16f, 0.018f) ||
                    IsBand(y, 0.34f, 0.018f) ||
                    IsBand(y, 0.70f, 0.018f) ||
                    IsBand(y, 0.86f, 0.018f)));
        }

        private static bool IsBand(float value, float center, float halfWidth)
        {
            return MathF.Abs(value - center) <= halfWidth;
        }

        private static float RidgeValue(float x, float y, float centerX, float centerY, float radiusX, float radiusY)
        {
            float dx = (x - centerX) / radiusX;
            float dy = (y - centerY) / radiusY;
            float d2 = dx * dx + dy * dy;
            return MathF.Max(0f, 1f - d2);
        }
    }
}
