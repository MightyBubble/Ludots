using System;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Map-owned presentation profile for visual heightmap rendering.
    /// Height samples remain the gameplay truth; this profile controls how adapters display them.
    /// </summary>
    public sealed class ContinuousHeightmapRenderProfile
    {
        public const float DefaultSeaLevelCm = 0f;
        public const float DefaultDisplayHeightScale = 1f;
        public const float DefaultColorContrast = 1f;
        public const float DefaultAbsoluteColorPeakSpanCm = 3600f;
        public const float MinDisplayHeightScale = 0.01f;
        public const float MaxDisplayHeightScale = 5000f;
        public const float MinColorContrast = 0.01f;
        public const float MaxColorContrast = 16f;
        public const float MinAbsoluteColorPeakSpanCm = 1f;
        public const float MaxAbsoluteColorPeakSpanCm = 1_000_000f;
        public const float DefaultOverviewSwitchChunkSpans = 2.5f;
        public const int DefaultOverviewVertexLimit = 65536;
        public const float MinOverviewSwitchChunkSpans = 1f;
        public const float MaxOverviewSwitchChunkSpans = 64f;
        public const int MinOverviewVertexLimit = 4;
        public const int MaxOverviewVertexLimit = 65536;

        public bool WaterEnabled { get; set; }

        public float SeaLevelCm { get; set; } = DefaultSeaLevelCm;

        public float DisplayHeightScale { get; set; } = DefaultDisplayHeightScale;

        public float ColorContrast { get; set; } = DefaultColorContrast;

        public float AbsoluteColorPeakSpanCm { get; set; } = DefaultAbsoluteColorPeakSpanCm;

        /// <summary>
        /// When true, visual-heightmap adapters must zero distance-fog params for this map
        /// (board-scale cameras sit far past authored fog end and would otherwise wash albedo).
        /// </summary>
        public bool DisableDistanceFog { get; set; }

        /// <summary>
        /// When the camera footprint exceeds detail radius times this multiplier,
        /// adapters must draw the overview mesh instead of the near-chunk window.
        /// </summary>
        public float OverviewSwitchChunkSpans { get; set; } = DefaultOverviewSwitchChunkSpans;

        /// <summary>Max vertices for the continental overview mesh (Raylib ushort index limit).</summary>
        public int OverviewVertexLimit { get; set; } = DefaultOverviewVertexLimit;

        public static ContinuousHeightmapRenderProfile CreateDefault()
        {
            return new ContinuousHeightmapRenderProfile();
        }

        public ContinuousHeightmapRenderProfile Clone()
        {
            return new ContinuousHeightmapRenderProfile
            {
                WaterEnabled = WaterEnabled,
                SeaLevelCm = SeaLevelCm,
                DisplayHeightScale = DisplayHeightScale,
                ColorContrast = ColorContrast,
                AbsoluteColorPeakSpanCm = AbsoluteColorPeakSpanCm,
                DisableDistanceFog = DisableDistanceFog,
                OverviewSwitchChunkSpans = OverviewSwitchChunkSpans,
                OverviewVertexLimit = OverviewVertexLimit,
            };
        }

        public ContinuousHeightmapRenderProfile NormalizeAndValidate()
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
            RequireRange(
                AbsoluteColorPeakSpanCm,
                MinAbsoluteColorPeakSpanCm,
                MaxAbsoluteColorPeakSpanCm,
                nameof(AbsoluteColorPeakSpanCm));
            RequireRange(
                OverviewSwitchChunkSpans,
                MinOverviewSwitchChunkSpans,
                MaxOverviewSwitchChunkSpans,
                nameof(OverviewSwitchChunkSpans));
            if (OverviewVertexLimit < MinOverviewVertexLimit || OverviewVertexLimit > MaxOverviewVertexLimit)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(OverviewVertexLimit),
                    $"{nameof(OverviewVertexLimit)} must be between {MinOverviewVertexLimit} and {MaxOverviewVertexLimit}.");
            }

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
