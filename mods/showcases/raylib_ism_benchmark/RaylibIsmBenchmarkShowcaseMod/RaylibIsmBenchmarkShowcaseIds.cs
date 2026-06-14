namespace RaylibIsmBenchmarkShowcaseMod;

internal static class RaylibIsmBenchmarkShowcaseIds
{
    public const string MapId = "raylib_ism_benchmark_showcase";
    public const string InstalledKey = "RaylibIsmBenchmarkShowcaseMod.Installed";
    public const string RendererServiceKey = "Platform.RaylibBenchmarkRenderer";

    public static readonly string[] BlacksmithMeshKeys =
    [
        "blacksmith.building.north.intact",
        "blacksmith.building.south.intact",
        "blacksmith.building.damaged",
        "blacksmith.building.ruined",
        "blacksmith.furnace",
        "blacksmith.worker.knight"
    ];

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
