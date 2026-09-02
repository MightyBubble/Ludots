namespace Sango.Content.MapBin;

/// <summary>
/// Port of MapData.VertexData, persisted fields only: per-vertex height byte (world height = Height * 0.5f),
/// terrain texture layer index, and water height byte. Position/UV/normal are Burst-Job derived in
/// Unity and never serialized, so they are not carried here.
/// </summary>
public readonly record struct MapVertexData(byte Height, byte TextureIndex, byte Water);

/// <summary>
/// Port of MapData.OnLoad/OnSave. Vertices are stored x-major: index = x * <see cref="VertexCountY"/> + y,
/// iterating x then y exactly like the source's vertexDatas[x][y] loops. The vertex mesh is
/// (Width+1) x (Height+1); Height/TextureIndex/Water are three bytes per vertex.
/// </summary>
public sealed record MapDataSection
{
    public int QuadSize { get; init; } = 5;
    public int VertexCountX { get; init; }
    public int VertexCountY { get; init; }
    public MapVertexData[] Vertices { get; init; } = Array.Empty<MapVertexData>();

    public MapVertexData VertexAt(int x, int y) => Vertices[x * VertexCountY + y];

    public static MapDataSection Read(int version, BinaryReader reader, int width, int height)
    {
        int quadSize = version <= 2 ? 5 : reader.ReadInt32();
        int vertexCountX = width + 1;
        int vertexCountY = height + 1;

        var vertices = new MapVertexData[vertexCountX * vertexCountY];
        int index = 0;
        for (int x = 0; x < vertexCountX; x++)
        {
            for (int y = 0; y < vertexCountY; y++)
                vertices[index++] = new MapVertexData(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        }

        return new MapDataSection
        {
            QuadSize = quadSize,
            VertexCountX = vertexCountX,
            VertexCountY = vertexCountY,
            Vertices = vertices,
        };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(QuadSize);
        for (int x = 0; x < VertexCountX; x++)
        {
            for (int y = 0; y < VertexCountY; y++)
            {
                MapVertexData vertex = Vertices[x * VertexCountY + y];
                writer.Write(vertex.Height);
                writer.Write(vertex.TextureIndex);
                writer.Write(vertex.Water);
            }
        }
    }
}
