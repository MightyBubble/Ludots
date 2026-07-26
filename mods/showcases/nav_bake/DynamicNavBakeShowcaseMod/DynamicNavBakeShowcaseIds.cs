namespace DynamicNavBakeShowcaseMod;

public static class DynamicNavBakeShowcaseIds
{
    public const string RuntimeServiceKey = "DynamicNavBakeShowcase.Runtime";
    public const string RtsMapId = "nav_bake_dynamic_rts";
    public const string OpenWorldMapId = "nav_bake_open_world_64x64";

    public const string PanelElementId = "dynamic-nav-bake-panel";
    public const string StatusTextElementId = "dynamic-nav-bake-status";

    /// <summary>Player-facing construction entry / cancel toggle.</summary>
    public const string BuildBuildingButtonElementId = "dynamic-nav-bake-build-building";
    public const string NavMeshVisibilityButtonElementId = "dynamic-nav-bake-navmesh-visibility";

    // Retained ids for non-player harness / auto-timeline contracts that still call action APIs.
    public const string HotspotTextElementId = "dynamic-nav-bake-hotspot";
    public const string ResidencyTextElementId = "dynamic-nav-bake-residency";
    public const string PerformanceTextElementId = "dynamic-nav-bake-performance";
    public const string RecastButtonElementId = "dynamic-nav-bake-algorithm-recast";
    public const string CdtButtonElementId = "dynamic-nav-bake-algorithm-cdt";
    public const string LayeredSpanButtonElementId = "dynamic-nav-bake-algorithm-layered-span";
    public const string RtsMapButtonElementId = "dynamic-nav-bake-map-rts";
    public const string OpenWorldMapButtonElementId = "dynamic-nav-bake-map-open-world";
    public const string BuildingToolButtonElementId = "dynamic-nav-bake-tool-building";
    public const string TerrainToolButtonElementId = "dynamic-nav-bake-tool-terrain";
    public const string PlaceEditButtonElementId = "dynamic-nav-bake-place-edit";
    public const string BakeButtonElementId = "dynamic-nav-bake-bake";
    public const string RestoreButtonElementId = "dynamic-nav-bake-restore";
    public const string DeploySquadButtonElementId = "dynamic-nav-bake-deploy-squad";
    public const string MoveToGoalButtonElementId = "dynamic-nav-bake-move-to-goal";
    public const string NextHotspotButtonElementId = "dynamic-nav-bake-next-hotspot";
    public const string ReturnButtonElementId = "dynamic-nav-bake-return";

    public const string AutoTimelineEnvKey = "LUDOTS_DYNAMIC_NAV_BAKE_AUTO_TIMELINE";

    /// <summary>
    /// Locked Orbit camera for Raylib auto-timeline capture (pan None, no user input).
    /// Interactive play keeps map DefaultCamera <c>Camera.Profile.Tactical</c>.
    /// </summary>
    public const string AutoCaptureCameraId = "DynamicNavBake.Camera.AutoCapture";

    public static bool IsAutoTimelineEnabled()
    {
        string? raw = Environment.GetEnvironmentVariable(AutoTimelineEnvKey);
        return !string.IsNullOrEmpty(raw);
    }

    public static bool IsShowcaseMap(string? mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return false;
        }

        return string.Equals(mapId, RtsMapId, StringComparison.Ordinal)
            || string.Equals(mapId, OpenWorldMapId, StringComparison.Ordinal);
    }
}
