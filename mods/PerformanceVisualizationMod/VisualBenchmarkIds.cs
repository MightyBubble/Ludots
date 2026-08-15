using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace PerformanceVisualizationMod
{
    public static class VisualBenchmarkIds
    {
        public const string BenchmarkCubePresenterId = "performance_visualization.benchmark_cube";
        public const string InstalledKey = "PerformanceVisualizationMod.Installed";
        public const string RuntimeServiceKey = "PerformanceVisualizationMod.Runtime";
        public const string StartupMapId = "entry";
        public const string MapId = "visual_benchmark";
        public const string Hud100kMapId = "visual_benchmark_hud_100k";
        public const string SkiaHotpathMapId = "visual_benchmark_skia_hotpath";
        public const string ScenarioTag = "visual_benchmark_subject";
        public const string ScenarioLabel = "VisualBenchmark.Subject";
        public const string PanelOwnerId = "performance-visualization.showcase";
        public const string DefaultScenarioName = "Presenter HUD Sweep";

        public static readonly MapId VisualBenchmarkMap = new(MapId);
        public static readonly MapId VisualBenchmarkHud100kMap = new(Hud100kMapId);
        public static readonly MapId VisualBenchmarkSkiaHotpathMap = new(SkiaHotpathMapId);
        public static readonly EventKey RunScenarioEvent = new("PerformanceVisualization.RunScenario");
        public static readonly EventKey ClearScenarioEvent = new("PerformanceVisualization.ClearScenario");

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mapId, Hud100kMapId, System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mapId, SkiaHotpathMapId, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
