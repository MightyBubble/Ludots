using System;

namespace Ludots.Platform.Abstractions
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
        public const float DefaultAbsoluteColorPeakSpanCm = 3600f;
        public const float MinDisplayHeightScale = 0.01f;
        public const float MaxDisplayHeightScale = 5000f;
        public const float MinColorContrast = 0.01f;
        public const float MaxColorContrast = 16f;
        public const float MinAbsoluteColorPeakSpanCm = 1f;
        public const float MaxAbsoluteColorPeakSpanCm = 1_000_000f;

        // LOD 政策（作者面）：远距全景切换、块级屏幕误差阈值、全景网格顶点预算、雾关闭开关。
        public const float DefaultOverviewSwitchChunkSpans = 2.5f;
        public const float MinOverviewSwitchChunkSpans = 0.25f;
        public const float MaxOverviewSwitchChunkSpans = 64f;
        public const int DefaultOverviewVertexLimit = 65_536;
        public const int MinOverviewVertexLimit = 256;
        public const int MaxOverviewVertexLimit = 262_144;
        public const float DefaultChunkLodErrorPx = 240f;
        public const float MinChunkLodErrorPx = 32f;
        public const float MaxChunkLodErrorPx = 2048f;

        public bool WaterEnabled { get; set; }

        public float SeaLevelCm { get; set; } = DefaultSeaLevelCm;

        public float DisplayHeightScale { get; set; } = DefaultDisplayHeightScale;

        public float ColorContrast { get; set; } = DefaultColorContrast;

        public float AbsoluteColorPeakSpanCm { get; set; } = DefaultAbsoluteColorPeakSpanCm;

        /// <summary>相机距离超过 chunk 跨度 × 该值时切换到全景粗网格渲染。</summary>
        public float OverviewSwitchChunkSpans { get; set; } = DefaultOverviewSwitchChunkSpans;

        /// <summary>全景粗网格顶点预算（按 chunk 步长抽取样本，约束在索引上限内）。</summary>
        public int OverviewVertexLimit { get; set; } = DefaultOverviewVertexLimit;

        /// <summary>块投影屏幕边长超过该像素数时使用高密度网格，否则逐级降档。</summary>
        public float ChunkLodErrorPx { get; set; } = DefaultChunkLodErrorPx;

        /// <summary>作者声明本图尺度下距离雾不适用（策略尺度地图的米级雾会冲平全图）。</summary>
        public bool DisableDistanceFog { get; set; }

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
                AbsoluteColorPeakSpanCm = AbsoluteColorPeakSpanCm,
                OverviewSwitchChunkSpans = OverviewSwitchChunkSpans,
                OverviewVertexLimit = OverviewVertexLimit,
                ChunkLodErrorPx = ChunkLodErrorPx,
                DisableDistanceFog = DisableDistanceFog,
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
            RequireRange(
                ChunkLodErrorPx,
                MinChunkLodErrorPx,
                MaxChunkLodErrorPx,
                nameof(ChunkLodErrorPx));
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
