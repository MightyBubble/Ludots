using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Rendering
{
    public static class TerrainVisualLayers
    {
        public const int Flag0 = 0;
        public const int Flag1 = 1;
        public const int Flag2 = 2;

        public const int Byte0 = 0;
    }

    public static class TerrainVisualRules
    {
        public static Vector4 GetVertexColor(
            byte height,
            byte biome,
            bool extraFlag0,
            bool extraFlag1,
            bool extraFlag2,
            byte extraByte0)
        {
            Vector3 rgb = GetBiomeBaseColorRgb(biome);

            float h = 0f;
            float s = 0f;
            float l = 0f;
            RgbToHsl(rgb, ref h, ref s, ref l);

            float t = height / 15f;
            l = MathF.Min(1f, l + t * 0.3f);
            s = MathF.Max(0f, s - t * 0.2f);

            rgb = HslToRgb(h, s, l);

            if (extraFlag0)
            {
                rgb = Vector3.Lerp(rgb, new Vector3(1f, 1f, 1f), 0.7f);
            }

            if (extraFlag2)
            {
                rgb = Vector3.Lerp(rgb, HexToRgb(0xA5F2F3), 0.6f);
            }

            if (extraFlag1)
            {
                rgb = Vector3.Lerp(rgb, HexToRgb(0x3E2723), 0.6f);
            }

            if (extraByte0 > 0)
            {
                float hue = (extraByte0 * 137.508f) % 360f;
                Vector3 tCol = HslToRgb(hue / 360f, 0.7f, 0.5f);
                rgb = Vector3.Lerp(rgb, tCol, 0.3f);
            }

            return new Vector4(rgb, 1f);
        }

        private static Vector3 GetBiomeBaseColorRgb(byte biome)
        {
            return biome switch
            {
                0 => HexToRgb(0x8B4513),
                1 => HexToRgb(0xF4A460),
                2 => HexToRgb(0x808080),
                3 => HexToRgb(0x3D6C2E),
                4 => HexToRgb(0x696969),
                5 => HexToRgb(0x556B2F),
                _ => HexToRgb(0x8B4513)
            };
        }

        private static Vector3 HexToRgb(int hex)
        {
            float r = ((hex >> 16) & 0xFF) / 255f;
            float g = ((hex >> 8) & 0xFF) / 255f;
            float b = (hex & 0xFF) / 255f;
            return new Vector3(r, g, b);
        }

        private static void RgbToHsl(Vector3 rgb, ref float h, ref float s, ref float l)
        {
            float r = rgb.X;
            float g = rgb.Y;
            float b = rgb.Z;

            float max = MathF.Max(r, MathF.Max(g, b));
            float min = MathF.Min(r, MathF.Min(g, b));
            float delta = max - min;

            l = (max + min) * 0.5f;

            if (delta < 1e-6f)
            {
                h = 0f;
                s = 0f;
                return;
            }

            s = l > 0.5f ? (delta / (2f - max - min)) : (delta / (max + min));

            if (max == r)
            {
                h = (g - b) / delta + (g < b ? 6f : 0f);
            }
            else if (max == g)
            {
                h = (b - r) / delta + 2f;
            }
            else
            {
                h = (r - g) / delta + 4f;
            }

            h /= 6f;
        }

        private static Vector3 HslToRgb(float h, float s, float l)
        {
            if (s < 1e-6f)
            {
                return new Vector3(l, l, l);
            }

            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;

            float r = HueToRgb(p, q, h + 1f / 3f);
            float g = HueToRgb(p, q, h);
            float b = HueToRgb(p, q, h - 1f / 3f);
            return new Vector3(r, g, b);
        }

        private static float HueToRgb(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }
    }
}

