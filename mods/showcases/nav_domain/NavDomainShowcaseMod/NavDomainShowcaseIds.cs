namespace NavDomainShowcaseMod;

internal static class NavDomainShowcaseIds
{
    public const string MapId = "nav_domain_editor";
    public const string MeshPerformerId = "nav_domain.mesh";
    public const string MeshAssetParamKey = "nav_domain.mesh.asset";
    public const string MeshMaterialParamKey = "nav_domain.mesh.material";
    public const string TerrainMeshKeyPrefix = "nav_domain.terrain";
    public const string NavTileMeshKeyPrefix = "nav_domain.navtile";
    public const string TerrainPerformerKeyPrefix = "nav_domain.terrain_performer";
    public const string NavTilePerformerKeyPrefix = "nav_domain.navtile_performer";
    public const string SourceUri = "NavDomainShowcaseMod:editor/logic_terrain";

    public static bool IsEditorMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.Ordinal);
    }
}
