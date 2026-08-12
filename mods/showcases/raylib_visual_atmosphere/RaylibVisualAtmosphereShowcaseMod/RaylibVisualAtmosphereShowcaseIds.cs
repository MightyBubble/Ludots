namespace RaylibVisualAtmosphereShowcaseMod;

internal static class RaylibVisualAtmosphereShowcaseIds
{
    public const string MapId = "raylib_visual_atmosphere_showcase";
    public const string InstalledKey = "RaylibVisualAtmosphereShowcaseMod.Installed";

    public const string TreeTemplateId = "raylib_visual_atmosphere_tree";
    public const string BushTemplateId = "raylib_visual_atmosphere_bush";
    public const string VfxBlendTemplateId = "raylib_visual_atmosphere_vfx_blend";
    public const string VfxAdditiveTemplateId = "raylib_visual_atmosphere_vfx_additive";
    public const string BeachPathMarkTemplateId = "raylib_visual_atmosphere_beach_path_mark";
    public const string SandScarTemplateId = "raylib_visual_atmosphere_sand_scar";

    public const string TreePerformerId = "raylib_visual_atmosphere_tree_actor";
    public const string BushPerformerId = "raylib_visual_atmosphere_bush_actor";
    public const string VfxBlendPerformerId = "raylib_visual_atmosphere_vfx_blend_actor";
    public const string VfxAdditivePerformerId = "raylib_visual_atmosphere_vfx_additive_actor";
    public const string BeachPathMarkPerformerId = "raylib_visual_atmosphere_beach_path_mark_actor";
    public const string SandScarPerformerId = "raylib_visual_atmosphere_sand_scar_actor";

    public const string TreeMeshKey = "raylib_visual_atmosphere.palm";
    public const string BushMeshKey = "raylib_visual_atmosphere.bush";
    public const string VfxBlendMeshKey = "raylib_visual_atmosphere.vfx_blend";
    public const string VfxAdditiveMeshKey = "raylib_visual_atmosphere.vfx_additive";

    public const string TreeMaterialKey = "raylib_visual_atmosphere.palm_cutout";
    public const string BushMaterialKey = "raylib_visual_atmosphere.bush_cutout";
    public const string VfxBlendMaterialKey = "raylib_visual_atmosphere.vfx_alphablend";
    public const string VfxAdditiveMaterialKey = "raylib_visual_atmosphere.vfx_additive";

    public const string SkyEnvironmentId = "raylib_visual_atmosphere.sky";
    public const string WaterEnvironmentId = "raylib_visual_atmosphere.water";

    // Hex edge 400cm, 256×256 map center (q=r=128).
    public const float IslandCenterXCm = 133_022f;
    public const float IslandCenterYCm = 76_800f;
    public const float WaterPlaneYMeters = 12f;

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
