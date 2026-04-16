namespace PerformanceVisualizationMod.Runtime
{
    public readonly record struct VisualBenchmarkScenarioConfig(
        string Key,
        string Label,
        int EntityCount,
        int Rows,
        int Columns,
        int SpacingCm,
        int CameraDistanceCm,
        bool AttachHealthAttributes)
    {
        public static readonly VisualBenchmarkScenarioConfig Small = new(
            Key: "small",
            Label: "2K Performer HUD",
            EntityCount: 2048,
            Rows: 32,
            Columns: 64,
            SpacingCm: 240,
            CameraDistanceCm: 22000,
            AttachHealthAttributes: true);

        public static readonly VisualBenchmarkScenarioConfig Medium = new(
            Key: "medium",
            Label: "8K Performer HUD",
            EntityCount: 8192,
            Rows: 64,
            Columns: 128,
            SpacingCm: 210,
            CameraDistanceCm: 34000,
            AttachHealthAttributes: true);

        public static readonly VisualBenchmarkScenarioConfig Large = new(
            Key: "large",
            Label: "32K Visual Stress",
            EntityCount: 32768,
            Rows: 128,
            Columns: 256,
            SpacingCm: 180,
            CameraDistanceCm: 62000,
            AttachHealthAttributes: false);

        public static readonly VisualBenchmarkScenarioConfig[] All =
        {
            Small,
            Medium,
            Large,
        };

        public static VisualBenchmarkScenarioConfig FromKey(string? key)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Key, key, System.StringComparison.OrdinalIgnoreCase))
                {
                    return All[i];
                }
            }

            return Small;
        }
    }
}
