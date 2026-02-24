using System;
using System.IO;
using System.Text;
using Ludots.Core.Map.Hex;

public static class EditorTerrainConverter
{
    public static void ConvertVertexMapBinaryToReactTerrain(Stream vertexMapBinary, Stream outputReactTerrain)
    {
        if (vertexMapBinary == null) throw new ArgumentNullException(nameof(vertexMapBinary));
        if (outputReactTerrain == null) throw new ArgumentNullException(nameof(outputReactTerrain));

        var map = VertexMapBinary.Read(vertexMapBinary);

        using var bw = new BinaryWriter(outputReactTerrain, Encoding.UTF8, leaveOpen: true);
        bw.Write(map.WidthInChunks);
        bw.Write(map.HeightInChunks);
        bw.Write((byte)2);

        for (int cy = 0; cy < map.HeightInChunks; cy++)
        {
            for (int cx = 0; cx < map.WidthInChunks; cx++)
            {
                int q = cx << VertexChunk.ChunkSizeShift;
                int r = cy << VertexChunk.ChunkSizeShift;
                var chunk = map.GetChunk(q, r, createIfMissing: false) ?? new VertexChunk();

                for (int ly = 0; ly < VertexChunk.ChunkSize; ly++)
                {
                    for (int lx = 0; lx < VertexChunk.ChunkSize; lx++)
                    {
                        byte height = (byte)(chunk.GetHeight(lx, ly) & 0x0F);
                        byte water = (byte)(chunk.GetWaterHeight(lx, ly) & 0x0F);
                        byte biome = (byte)(chunk.GetBiome(lx, ly) & 0x0F);
                        byte veg = (byte)(chunk.GetVegetation(lx, ly) & 0x0F);

                        byte b0 = (byte)((height << 4) | water);
                        byte b1 = (byte)((biome << 4) | veg);

                        byte b2 = 0;
                        if (chunk.GetRamp(lx, ly)) b2 |= 0b1000_0000;
                        if (chunk.GetExtraFlag(lx, ly, 0)) b2 |= 0b0100_0000;
                        if (chunk.GetExtraFlag(lx, ly, 1)) b2 |= 0b0010_0000;
                        if (chunk.GetExtraFlag(lx, ly, 2)) b2 |= 0b0001_0000;

                        byte b3 = chunk.GetExtraByte(lx, ly, 0);

                        bw.Write(b0);
                        bw.Write(b1);
                        bw.Write(b2);
                        bw.Write(b3);
                    }
                }
            }
        }
    }
}

