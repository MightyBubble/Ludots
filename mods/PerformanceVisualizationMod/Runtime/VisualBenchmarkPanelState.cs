namespace PerformanceVisualizationMod.Runtime
{
    internal readonly record struct VisualBenchmarkPanelState(
        string Title,
        string Status,
        string Scenario,
        string Metrics,
        string Camera,
        string Hint,
        string[] Actions)
    {
        public static readonly VisualBenchmarkPanelState Empty = new(
            Title: "Visual Benchmark Showcase",
            Status: "Waiting for visual_benchmark map focus.",
            Scenario: "Scenario: not loaded",
            Metrics: "Entities: 0 | Visible: 0 | HUD: 0",
            Camera: "Camera: unavailable",
            Hint: "Load the showcase map to begin.",
            Actions: new[]
            {
                "Run 2K performer HUD",
                "Run 8K performer HUD",
                "Run 32K visual stress",
                "Clear scenario",
            });
    }
}
