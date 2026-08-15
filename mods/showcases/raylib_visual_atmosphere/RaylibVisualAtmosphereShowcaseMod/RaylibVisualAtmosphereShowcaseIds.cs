namespace RaylibVisualAtmosphereShowcaseMod;

internal static class RaylibVisualAtmosphereShowcaseIds
{
    public const string MapId = "raylib_visual_atmosphere_showcase";
    public const string InstalledKey = "RaylibVisualAtmosphereShowcaseMod.Installed";

    public const string TreeTemplateId = "raylib_visual_atmosphere_tree";
    public const string BushTemplateId = "raylib_visual_atmosphere_bush";
    public const string VfxBlendTemplateId = "raylib_visual_atmosphere_vfx_blend";
    public const string VfxAdditiveTemplateId = "raylib_visual_atmosphere_vfx_additive";
    public const string DecalFootprintsTemplateId = "raylib_visual_atmosphere_decal_footprints";
    public const string DecalScorchTemplateId = "raylib_visual_atmosphere_decal_scorch";
    public const string DecalBloodTemplateId = "raylib_visual_atmosphere_decal_blood";
    public const string DecalCracksTemplateId = "raylib_visual_atmosphere_decal_cracks";
    public const string RockTemplateId = "raylib_visual_atmosphere_rock";

    public const string TreePerformerId = "raylib_visual_atmosphere_tree_actor";
    public const string BushPerformerId = "raylib_visual_atmosphere_bush_actor";
    public const string VfxBlendPerformerId = "raylib_visual_atmosphere_vfx_blend_actor";
    public const string VfxAdditivePerformerId = "raylib_visual_atmosphere_vfx_additive_actor";
    public const string DecalFootprintsPerformerId = "raylib_visual_atmosphere_decal_footprints_actor";
    public const string DecalScorchPerformerId = "raylib_visual_atmosphere_decal_scorch_actor";
    public const string DecalBloodPerformerId = "raylib_visual_atmosphere_decal_blood_actor";
    public const string DecalCracksPerformerId = "raylib_visual_atmosphere_decal_cracks_actor";
    public const string RockPerformerId = "raylib_visual_atmosphere_rock_actor";

    public const string TreeMeshKey = "raylib_visual_atmosphere.palm";
    public const string BushMeshKey = "raylib_visual_atmosphere.bush";
    public const string VfxBlendMeshKey = "raylib_visual_atmosphere.vfx_blend";
    public const string VfxAdditiveMeshKey = "raylib_visual_atmosphere.vfx_additive";
    public const string RockMeshKey = "raylib_visual_atmosphere.rock";

    public const string TreeMaterialKey = "raylib_visual_atmosphere.palm_cutout";
    public const string BushMaterialKey = "raylib_visual_atmosphere.bush_cutout";
    public const string VfxBlendMaterialKey = "raylib_visual_atmosphere.vfx_alphablend";
    public const string VfxAdditiveMaterialKey = "raylib_visual_atmosphere.vfx_additive";
    public const string RockMaterialKey = "raylib_visual_atmosphere.rock";

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
