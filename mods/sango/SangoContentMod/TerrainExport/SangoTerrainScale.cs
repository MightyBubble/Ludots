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
