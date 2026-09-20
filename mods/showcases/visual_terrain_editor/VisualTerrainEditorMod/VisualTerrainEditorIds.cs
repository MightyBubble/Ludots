namespace VisualTerrainEditorMod;

internal static class VisualTerrainEditorIds
{
    public const string MapId = "visual_terrain_editor";
    public const string ChunkMeshPresenterId = "visual_terrain_editor.chunk_mesh";

    public static bool IsEditorMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.Ordinal);
    }
}
