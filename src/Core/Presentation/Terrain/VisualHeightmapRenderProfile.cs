using System;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Map-owned presentation profile for visual heightmap rendering.
    /// Height samples remain the gameplay truth; this profile controls how adapters display them.
    /// </summary>
    public sealed class VisualHeightmapRenderProfile
    {
        public const float DefaultSeaLevelCm = 0f;
        public const float DefaultDisplayHeightScale = 1f;
        public const float DefaultColorContrast = 1f;
        public const float MinDisplayHeightScale = 0.01f;
        public const float MaxDisplayHeightScale = 5000f;
        public const float MinColorContrast = 0.01f;
        public const float MaxColorContrast = 16f;

        public bool WaterEnabled { get; set; }

        public float SeaLevelCm { get; set; } = DefaultSeaLevelCm;

        public float DisplayHeightScale { get; set; } = DefaultDisplayHeightScale;

        public float ColorContrast { get; set; } = DefaultColorContrast;

        public static VisualHeightmapRenderProfile CreateDefault()
        {
            return new VisualHeightmapRenderProfile();
        }

        public VisualHeightmapRenderProfile Clone()
        {
            return new VisualHeightmapRenderProfile
            {
                WaterEnabled = WaterEnabled,
                SeaLevelCm = SeaLevelCm,
                DisplayHeightScale = DisplayHeightScale,
                ColorContrast = ColorContrast,
            };
        }

        public VisualHeightmapRenderProfile NormalizeAndValidate()
        {
            RequireFinite(SeaLevelCm, nameof(SeaLevelCm));
            RequireRange(
                DisplayHeightScale,
                MinDisplayHeightScale,
                MaxDisplayHeightScale,
                nameof(DisplayHeightScale));
            RequireRange(
                ColorContrast,
                MinColorContrast,
                MaxColorContrast,
                nameof(ColorContrast));

            return Clone();
        }

        private static void RequireFinite(float value, string name)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must be finite.");
            }
        }

        private static void RequireRange(float value, float min, float max, string name)
        {
            RequireFinite(value, name);
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must be between {min} and {max}.");
            }
        }
    }
}
