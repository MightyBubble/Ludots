namespace Sango.Content.MapBin;

/// <summary>
/// Port of MapTerrain.OnLoad/OnSave: just the terrain cell partition size; the cells themselves are
/// runtime render objects derived from MapData vertices and never serialized.
/// </summary>
public sealed record MapTerrainSection
{
    public int CellSize { get; init; } = 64;

    public static MapTerrainSection Read(BinaryReader reader) => new() { CellSize = reader.ReadInt32() };

    public void Write(BinaryWriter writer) => writer.Write(CellSize);
}
