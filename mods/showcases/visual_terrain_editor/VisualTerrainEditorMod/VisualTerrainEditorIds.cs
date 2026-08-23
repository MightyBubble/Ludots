using Ludots.Core.Config;

namespace VisualTerrainEditorMod;

internal static class VisualTerrainEditorIds
{
    public const string MapId = "visual_terrain_editor";
    public const string ChunkMeshPerformerId = "visual_terrain_editor.chunk_mesh";
    public const string EditableMapTag = "visual_terrain_editable";

    public static bool IsEditorMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.Ordinal);
    }

    public static bool IsEditableMap(MapConfig? mapConfig)
    {
        if (mapConfig == null)
        {
            return false;
        }

        if (IsEditorMap(mapConfig.Id))
        {
            return true;
        }

        if (mapConfig.Tags == null)
        {
            return false;
        }

        for (int i = 0; i < mapConfig.Tags.Count; i++)
        {
            if (string.Equals(mapConfig.Tags[i], EditableMapTag, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
