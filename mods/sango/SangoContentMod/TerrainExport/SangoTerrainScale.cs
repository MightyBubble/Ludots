using System;
using System.Collections.Generic;
using Sango.Content.MapBin;

namespace Sango.Content.TerrainExport;

/// <summary>
/// Scale and classification constants for the DefaultMap.bin → Ludots terrain export.
/// All source units follow MapRender: 1 height byte = 0.5 world units, quadSize units per quad edge,
/// and the engine stores visual heights in centimeters.
/// </summary>
public static class SangoTerrainScale
{
    /// <summary>1 source height/water unit = 0.5 m = 50 cm (MapData position.y = height * 0.5f).</summary>
    public const int CmPerHeightUnit = 50;

    /// <summary>1 source world unit = 1 m = 100 cm (quadSize 5 units per quad edge = 5 m).</summary>
    public const int CmPerWorldUnit = 100;

    /// <summary>
    /// Sea level in source water units is the dominant water byte of the map (11 on the default map,
    /// i.e. 5.5 m); the renderer profile and water plane must reuse the derived centimeter value.
    /// </summary>
    public static int DeriveSeaLevelUnits(SangoMapBinArchive archive)
    {
        if (archive == null) throw new ArgumentNullException(nameof(archive));

        var histogram = new int[256];
        foreach (MapVertexData vertex in archive.Data.Vertices)
        {
            if (vertex.Water > 0)
                histogram[vertex.Water]++;
        }

        int dominant = 0;
        int dominantCount = 0;
        for (int i = 1; i < histogram.Length; i++)
        {
            if (histogram[i] > dominantCount)
            {
                dominant = i;
                dominantCount = histogram[i];
            }
        }

        return dominant;
    }

    public static int DeriveSeaLevelCm(SangoMapBinArchive archive)
    {
        return DeriveSeaLevelUnits(archive) * CmPerHeightUnit;
    }

    /// <summary>
    /// Lossy visual projection of the TerrainTypes.json terrain byte onto the VertexMap biome nibble
    /// (0..15). The raw terrain byte travels losslessly in the VertexMap extra byte layer 0; this
    /// projection only feeds engine-side fallback hex vertex colors (TerrainVisualRules biomes).
    /// 32 appears in the real map grid for open sea beyond the shipped table range.
    /// </summary>
    public static byte ProjectTerrainTypeToBiome(byte terrainType)
    {
        return terrainType switch
        {
            0 => 0,   // 无
            1 => 3,   // 草地 → engine green
            2 => 1,   // 土 → engine sand
            3 => 1,   // 砂地 → engine sand
            4 => 5,   // 湿地 → engine dark olive
            5 => 5,   // 毒泉 → engine dark olive
            6 => 5,   // 森 → engine dark olive
            7 => 2,   // 川 → engine gray (water)
            8 => 2,   // 河 → engine gray (water)
            9 => 2,   // 海 → engine gray (water)
            10 => 4,  // 荒地 → engine rock gray
            11 => 1,  // 主径 → engine sand (road)
            12 => 4,  // 栈道 → engine rock gray
            13 => 2,  // 渡所 → engine gray (water crossing)
            14 => 2,  // 浅滩 → engine gray (water)
            15 => 2,  // 岸 → engine gray
            16 => 4,  // 崖 → engine rock gray
            17 => 1,  // 都市 → engine sand (settled)
            18 => 2,  // 港 → engine gray (harbor)
            19 => 4,  // 关所 → engine rock gray
            20 => 1,  // 小径 → engine sand (road)
            >= 21 and <= 31 => 0, // 地20..地30 placeholder kinds → base brown
            _ => 2,   // 32+ observed as open sea → engine gray (water)
        };
    }

    /// <summary>Linear 0..255 source byte → 0..15 VertexMap nibble level.</summary>
    public static byte QuantizeToNibbleLevel(byte value)
    {
        int level = (int)MathF.Round(value * 15f / 255f);
        return (byte)Math.Clamp(level, 0, 15);
    }

    public static int WorldWidthCm(SangoMapBinArchive archive)
    {
        return archive.Width * archive.Data.QuadSize * CmPerWorldUnit;
    }

    public static int WorldHeightCm(SangoMapBinArchive archive)
    {
        return archive.Height * archive.Data.QuadSize * CmPerWorldUnit;
    }
}
