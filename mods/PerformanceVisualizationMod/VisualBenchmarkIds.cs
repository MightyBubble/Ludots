using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace PerformanceVisualizationMod
{
    public static class VisualBenchmarkIds
    {
        public const string InstalledKey = "PerformanceVisualizationMod.Installed";
        public const string RuntimeServiceKey = "PerformanceVisualizationMod.Runtime";
        public const string StartupMapId = "entry";
        public const string MapId = "visual_benchmark";
        public const string ScenarioTag = "visual_benchmark_subject";
        public const string ScenarioLabel = "VisualBenchmark.Subject";
        public const string PanelOwnerId = "performance-visualization.showcase";
        public const string DefaultScenarioName = "Performer HUD Sweep";

        public static readonly MapId VisualBenchmarkMap = new(MapId);
        public static readonly EventKey RunScenarioEvent = new("PerformanceVisualization.RunScenario");
        public static readonly EventKey ClearScenarioEvent = new("PerformanceVisualization.ClearScenario");

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
