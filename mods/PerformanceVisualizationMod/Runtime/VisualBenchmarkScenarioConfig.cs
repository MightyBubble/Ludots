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
        bool AttachHealthAttributes,
        VisualBenchmarkWorkloadKind WorkloadKind)
    {
        public static readonly VisualBenchmarkScenarioConfig Small = new(
            Key: "small",
            Label: "2K Performer HUD",
            EntityCount: 2048,
            Rows: 32,
            Columns: 64,
            SpacingCm: 240,
            CameraDistanceCm: 22000,
            AttachHealthAttributes: true,
            WorkloadKind: VisualBenchmarkWorkloadKind.PerformerWorldHud);

        public static readonly VisualBenchmarkScenarioConfig Medium = new(
            Key: "medium",
            Label: "8K Performer HUD",
            EntityCount: 8192,
            Rows: 64,
            Columns: 128,
            SpacingCm: 210,
            CameraDistanceCm: 34000,
            AttachHealthAttributes: true,
            WorkloadKind: VisualBenchmarkWorkloadKind.PerformerWorldHud);

        public static readonly VisualBenchmarkScenarioConfig Large = new(
            Key: "large",
            Label: "32K Visual Stress",
            EntityCount: 32768,
            Rows: 128,
            Columns: 256,
            SpacingCm: 180,
            CameraDistanceCm: 62000,
            AttachHealthAttributes: false,
            WorkloadKind: VisualBenchmarkWorkloadKind.PerformerWorldHud);

        public static readonly VisualBenchmarkScenarioConfig Hud100k = new(
            Key: "hud100k",
            Label: "HUD 100K Direct",
            EntityCount: 100000,
            Rows: 250,
            Columns: 400,
            SpacingCm: 1,
            CameraDistanceCm: 2000,
            AttachHealthAttributes: false,
            WorkloadKind: VisualBenchmarkWorkloadKind.DirectScreenHud100k);

        public static readonly VisualBenchmarkScenarioConfig SkiaHotpath = new(
            Key: "skia_hotpath",
            Label: "Skia Hotpath 10K",
            EntityCount: 10240,
            Rows: 160,
            Columns: 64,
            SpacingCm: 1,
            CameraDistanceCm: 2000,
            AttachHealthAttributes: false,
            WorkloadKind: VisualBenchmarkWorkloadKind.DirectScreenHudHotpath);

        public static readonly VisualBenchmarkScenarioConfig[] All =
        {
            Small,
            Medium,
            Large,
            Hud100k,
            SkiaHotpath,
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

    public enum VisualBenchmarkWorkloadKind : byte
    {
        PerformerWorldHud = 0,
        DirectScreenHud100k = 1,
        DirectScreenHudHotpath = 2,
    }
}
