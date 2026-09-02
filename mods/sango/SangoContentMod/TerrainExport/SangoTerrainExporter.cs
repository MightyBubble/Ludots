using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;
using Sango.Content.MapBin;

namespace Sango.Content.TerrainExport;

/// <summary>
/// Summary of one export run; the authored map JSON (SangoTerrainMod assets/Maps/sango_default.json)
/// hardcodes the same numbers, and the headless tests re-derive them from the real bin.
/// </summary>
public sealed record SangoTerrainExportStats(
    int SampleColumns,
    int SampleRows,
    int WorldWidthCm,
    int WorldHeightCm,
    int SeaLevelCm,
    int PeakSpanCm,
    int HexWidthInChunks,
    int HexHeightInChunks,
    int HexCellsX,
    int HexCellsY,
    long WaterSamples);

/// <summary>
/// Offline converter from a <see cref="SangoMapBinArchive"/> (DefaultMap.bin) to Ludots terrain assets:
/// a CHTM visual heightmap (.height) and a HexGridBoard VertexMap data file (.hex). Both outputs are
/// derived binaries of a copyright-restricted source and stay untracked (see SangoTerrainMod/.gitignore
/// entries in the repo root); the real bin path only ever enters as a tool/test argument.
/// </summary>
public static class SangoTerrainExporter
{
    public const string HeightmapFileName = "sango_default.height";
    public const string HexDataFileName = "sango_default_hex.hex";

    /// <summary>
    /// Axis mapping (keeps geographic orientation: north up): source vertex/grid x is the north axis
    /// and maps to Ludots worldZ (heightmap row), source y is the east axis and maps to worldX
    /// (heightmap column) — mirroring MapData.VertexPosition (y * quadSize, h * 0.5f, x * quadSize).
    /// </summary>
    public static SangoTerrainExportStats ExportToDirectory(SangoMapBinArchive archive, string outputDirectory)
    {
        if (archive == null) throw new ArgumentNullException(nameof(archive));
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);

        SangoTerrainExportStats stats = ExportHeightmap(archive, Path.Combine(outputDirectory, HeightmapFileName));
        SangoTerrainExportStats hexStats = ExportHexGrid(archive, Path.Combine(outputDirectory, HexDataFileName));
        return stats with
        {
            HexWidthInChunks = hexStats.HexWidthInChunks,
            HexHeightInChunks = hexStats.HexHeightInChunks,
            HexCellsX = hexStats.HexCellsX,
            HexCellsY = hexStats.HexCellsY,
        };
    }

    /// <summary>
    /// Produces the single-layer int16-centimeter CHTM heightmap. Dry vertices keep height*0.5m;
    /// water vertices are clamped to at most sea level so nothing submerged pokes through the ocean
    /// plane, while the sea floor stays below sea level for the renderer's depth-tinted flat water.
    /// </summary>
    public static SangoTerrainExportStats ExportHeightmap(SangoMapBinArchive archive, string outputPath)
    {
        MapDataSection data = archive.Data;
        int seaLevelCm = SangoTerrainScale.DeriveSeaLevelCm(archive);
        int worldWidthCm = SangoTerrainScale.WorldWidthCm(archive);
        int worldHeightCm = SangoTerrainScale.WorldHeightCm(archive);

        int sampleColumns = data.VertexCountY; // east axis (source y)
        int sampleRows = data.VertexCountX;    // north axis (source x)

        int maxLandCm = 0;
        long waterSamples = 0;
        var samples = new short[sampleColumns * sampleRows];
        for (int row = 0; row < sampleRows; row++)
        {
            int vertexX = row; // north index into the source vertex grid
            for (int column = 0; column < sampleColumns; column++)
            {
                int vertexY = column; // east index into the source vertex grid
                MapVertexData vertex = data.VertexAt(vertexX, vertexY);
                int heightCm = vertex.Height * SangoTerrainScale.CmPerHeightUnit;
                if (vertex.Water > 0)
                {
                    waterSamples++;
                    heightCm = Math.Min(heightCm, seaLevelCm);
                }
                else
                {
                    maxLandCm = Math.Max(maxLandCm, heightCm);
                }

                samples[row * sampleColumns + column] = (short)heightCm;
            }
        }

        var bounds = new WorldAabbCm(
            x: -worldWidthCm / 2,
            y: -worldHeightCm / 2,
            width: worldWidthCm,
            height: worldHeightCm);
        ContinuousHeightmapAsset asset = ContinuousHeightmapAsset.CreateSingleLayer(
            bounds,
            sampleColumns,
            sampleRows,
            samples,
            layerName: "sango_base");

        using (var stream = File.Create(outputPath))
        {
            ContinuousHeightmapBinary.Write(stream, asset);
        }

        int peakSpanCm = Math.Max(1, maxLandCm - seaLevelCm);
        return new SangoTerrainExportStats(
            SampleColumns: sampleColumns,
            SampleRows: sampleRows,
            WorldWidthCm: worldWidthCm,
            WorldHeightCm: worldHeightCm,
            SeaLevelCm: seaLevelCm,
            PeakSpanCm: peakSpanCm,
            HexWidthInChunks: 0,
            HexHeightInChunks: 0,
            HexCellsX: 0,
            HexCellsY: 0,
            WaterSamples: waterSamples);
    }

    /// <summary>
    /// Produces the HexGridBoard DataFile. Each source hex cell (256x256 on the default map) becomes
    /// one VertexMap cell: quantized terrain height and water level in the 0..15 nibbles, the lossy
    /// visual biome projection in the biome nibble, and the raw TerrainTypes.json byte in extra byte
    /// layer 0 (read back via VertexMap.GetExtraByte) so M1 gameplay layers keep the full table index.
    /// </summary>
    public static SangoTerrainExportStats ExportHexGrid(SangoMapBinArchive archive, string outputPath)
    {
        MapGridSection grid = archive.Grid;
        MapDataSection data = archive.Data;
        int gridVertexCount = Math.Max(1, grid.GridVertexCount);

        int cellsX = grid.BoundsX; // north axis cells
        int cellsY = grid.BoundsY; // east axis cells
        if (cellsX <= 0 || cellsY <= 0)
        {
            throw new InvalidOperationException($"Map grid has non-positive bounds {cellsX}x{cellsY}; nothing to export.");
        }

        int widthInChunks = (cellsY + VertexChunk.ChunkSize - 1) / VertexChunk.ChunkSize;
        int heightInChunks = (cellsX + VertexChunk.ChunkSize - 1) / VertexChunk.ChunkSize;

        var map = new VertexMap();
        map.Initialize(widthInChunks, heightInChunks);

        for (int gridX = 0; gridX < cellsX; gridX++)
        {
            for (int gridY = 0; gridY < cellsY; gridY++)
            {
                MapGridCell cell = grid.CellAt(gridX, gridY);
                MapVertexData center = SampleCellCenterVertex(data, gridVertexCount, gridX, gridY);

                int column = gridY; // VertexMap col = east axis
                int row = gridX;    // VertexMap row = north axis
                map.SetHeight(column, row, SangoTerrainScale.QuantizeToNibbleLevel(center.Height));
                map.SetWaterHeight(column, row, SangoTerrainScale.QuantizeToNibbleLevel(center.Water));
                map.SetBiome(column, row, SangoTerrainScale.ProjectTerrainTypeToBiome(cell.TerrainType));
                // VertexMap only exposes nibble-level setters; the raw terrain byte needs the chunk cell directly.
                map.GetChunk(column, row, createIfMissing: true)
                    .SetExtraByte(column & VertexChunk.ChunkSizeMask, row & VertexChunk.ChunkSizeMask, 0, cell.TerrainType);
            }
        }

        using (var stream = File.Create(outputPath))
        {
            VertexMapBinary.Write(stream, map);
        }

        return new SangoTerrainExportStats(
            SampleColumns: 0,
            SampleRows: 0,
            WorldWidthCm: 0,
            WorldHeightCm: 0,
            SeaLevelCm: 0,
            PeakSpanCm: 0,
            HexWidthInChunks: widthInChunks,
            HexHeightInChunks: heightInChunks,
            HexCellsX: cellsX,
            HexCellsY: cellsY,
            WaterSamples: 0);
    }

    private static MapVertexData SampleCellCenterVertex(MapDataSection data, int gridVertexCount, int gridX, int gridY)
    {
        int vertexX = Math.Min(data.VertexCountX - 1, gridX * gridVertexCount + gridVertexCount / 2);
        int vertexY = Math.Min(data.VertexCountY - 1, gridY * gridVertexCount + gridVertexCount / 2);
        return data.VertexAt(vertexX, vertexY);
    }
}
