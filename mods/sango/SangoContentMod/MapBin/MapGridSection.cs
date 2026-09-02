using System.Numerics;

namespace Sango.Content.MapBin;

public enum MapGridState
{
    None = 0,
    Defence = 1,
    Interior = 2,
    Thief = 3,
}

/// <summary>
/// Port of MapGrid.San11GridData: the Romance of the Three Kingdoms XI per-cell payload. Only read
/// from DefaultMap.bin streams with version &lt; 5, where it is immediately folded into
/// <see cref="MapGridCell.TerrainType"/>/<see cref="MapGridCell.AreaId"/>/<see cref="MapGridCell.TerrainState"/>.
/// </summary>
public sealed record San11GridData
{
    public byte TType { get; init; }
    public byte AreaId { get; init; }
    public byte LpB { get; init; }
    public byte Trap { get; init; }
    public byte Dir { get; init; }
    public byte Interior { get; init; }
    public byte Defence { get; init; }
    public byte Thief { get; init; }
    public byte Flood { get; init; }
    public byte Fire { get; init; }
    public byte Ruins { get; init; }

    public static San11GridData Read(BinaryReader reader) => new()
    {
        TType = reader.ReadByte(),
        AreaId = reader.ReadByte(),
        LpB = reader.ReadByte(),
        Trap = reader.ReadByte(),
        Dir = reader.ReadByte(),
        Interior = reader.ReadByte(),
        Defence = reader.ReadByte(),
        Thief = reader.ReadByte(),
        Flood = reader.ReadByte(),
        Fire = reader.ReadByte(),
        Ruins = reader.ReadByte(),
    };

    public void Write(BinaryWriter writer)
    {
        writer.Write(TType);
        writer.Write(AreaId);
        writer.Write(LpB);
        writer.Write(Trap);
        writer.Write(Dir);
        writer.Write(Interior);
        writer.Write(Defence);
        writer.Write(Thief);
        writer.Write(Flood);
        writer.Write(Fire);
        writer.Write(Ruins);
    }
}

/// <summary>
/// Port of MapGrid.GridData, persisted fields only: hex cell terrain (index into
/// TerrainTypes.json), a bit mask over <see cref="MapGridState"/>, and the region id.
/// </summary>
public readonly record struct MapGridCell(byte TerrainType, int TerrainState, ushort AreaId);

/// <summary>
/// Port of MapGrid.OnLoad/OnSave. Cells are stored x-major: index = x * <see cref="BoundsY"/> + y,
/// iterating x then y exactly like the source's gridDatas[x][y] loops (odd-q hex layout).
/// </summary>
public sealed record MapGridSection
{
    public int GridSize { get; init; }
    public int GridVertexCount { get; init; }
    public int BoundsX { get; init; }
    public int BoundsY { get; init; }
    public string GridTextureName { get; init; } = "grid";
    public MapGridCell[] Cells { get; init; } = Array.Empty<MapGridCell>();

    public MapGridCell CellAt(int x, int y) => Cells[x * BoundsY + y];

    /// <summary>
    /// quadSize is the MapData quad size visible to the grid at load time: for version &gt; 5 the
    /// grid loads before MapData has read its quadSize, so the source derives grid bounds from the
    /// C# field default 5; for version &le; 5 the grid loads last and uses the loaded value.
    /// </summary>
    public static MapGridSection Read(int version, BinaryReader reader, int width, int height, int quadSize)
    {
        string? gridTextureName = null;
        if (version < 6)
            gridTextureName = reader.ReadString();

        // MapGrid.Create stores max(size, quadSize) back into gridSize, and OnSave rewrites that field.
        int gridSize = Math.Max(reader.ReadInt32(), quadSize);
        int gridVertexCount = gridSize / quadSize;
        int boundsX = width * quadSize / gridSize;
        int boundsY = height * quadSize / gridSize;

        var cells = new MapGridCell[boundsX * boundsY];
        int index = 0;
        for (int x = 0; x < boundsX; x++)
        {
            for (int y = 0; y < boundsY; y++)
            {
                byte terrainType = reader.ReadByte();
                int terrainState = version >= 7 ? reader.ReadInt32() : 0;
                ushort areaId = (ushort)(version >= 8 ? reader.ReadUInt16() : 0);

                if (version < 5)
                {
                    _ = reader.ReadInt32();
                    if (version > 0)
                    {
                        San11GridData san11 = San11GridData.Read(reader);
                        terrainType = (byte)(san11.TType + 1);
                        areaId = (ushort)(san11.AreaId + 1);
                        if (san11.Defence > 0)
                            terrainState |= 1 << (int)MapGridState.Defence;
                        if (san11.Thief > 0)
                            terrainState |= 1 << (int)MapGridState.Thief;
                        if (san11.Interior > 0)
                            terrainState |= 1 << (int)MapGridState.Interior;
                    }
                }

                cells[index++] = new MapGridCell(terrainType, terrainState, areaId);
            }
        }

        if (version >= 6)
            gridTextureName = reader.ReadString();

        return new MapGridSection
        {
            GridSize = gridSize,
            GridVertexCount = gridVertexCount,
            BoundsX = boundsX,
            BoundsY = boundsY,
            GridTextureName = string.IsNullOrEmpty(gridTextureName) ? "grid" : gridTextureName,
            Cells = cells,
        };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(GridSize);
        for (int x = 0; x < BoundsX; x++)
        {
            for (int y = 0; y < BoundsY; y++)
            {
                MapGridCell cell = Cells[x * BoundsY + y];
                writer.Write(cell.TerrainType);
                writer.Write(cell.TerrainState);
                writer.Write(cell.AreaId);
            }
        }
        if (string.IsNullOrEmpty(GridTextureName))
            writer.Write("");
        else
            writer.Write(GridTextureName);
    }
}
