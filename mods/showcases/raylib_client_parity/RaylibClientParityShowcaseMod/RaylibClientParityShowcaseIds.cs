namespace RaylibClientParityShowcaseMod;

internal static class RaylibClientParityShowcaseIds
{
    public const string MapId = "raylib_client_parity_showcase";
    public const string InstalledKey = "RaylibClientParityShowcaseMod.Installed";
    public const string RendererServiceKey = "Platform.RaylibBenchmarkRenderer";

    public const string CrowdTemplateId = "raylib_client_parity_crowd_agent";
    public const string CrowdPresenterId = "raylib_client_parity_crowd_actor";
    public const string MannequinMeshKey = "raylib_client_parity.mannequin";
    public const string AlbedoMaterialKey = "raylib_client_parity.albedo_demo";
    public const string VfxPrefabMeshKey = "raylib_client_parity.vfx_marker";

    public static readonly string[] BlacksmithMeshKeys =
    [
        "blacksmith.building.north.intact",
        "blacksmith.building.south.intact",
        "blacksmith.building.damaged",
        "blacksmith.building.ruined",
        "blacksmith.furnace"
    ];

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
