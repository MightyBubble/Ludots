using System;
using System.IO;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Tool
{
    public static class VisualHeightmapFixtureGenerator
    {
        public static void Generate(string outFile, int widthChunks, int heightChunks, MapVtxmGenerator.Preset preset, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(outFile)) throw new ArgumentException("Output file is required.", nameof(outFile));
            if (widthChunks <= 0) throw new ArgumentOutOfRangeException(nameof(widthChunks));
            if (heightChunks <= 0) throw new ArgumentOutOfRangeException(nameof(heightChunks));

            outFile = Path.GetFullPath(outFile);
            string? outDir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            if (File.Exists(outFile) && !overwrite)
            {
                throw new IOException($"File already exists: {outFile} (pass --overwrite to replace)");
            }

            int sampleColumns = checked(widthChunks * 64);
            int sampleRows = checked(heightChunks * 64);
            int sampleCount = checked(sampleColumns * sampleRows);
            var heights = new short[sampleCount];
            for (int y = 0; y < sampleRows; y++)
            {
                for (int x = 0; x < sampleColumns; x++)
                {
                    heights[y * sampleColumns + x] = (short)HeightCmAt(preset, sampleColumns, sampleRows, x, y);
                }
            }

            var asset = VisualHeightmapAsset.CreateSingleLayer(
                new WorldAabbCm(0, 0, sampleColumns * 100, sampleRows * 100),
                sampleColumns,
                sampleRows,
                heights,
                layerName: "base",
                interpolationMode: VisualHeightmapInterpolationMode.TriangleHeightfield);

            using var fs = File.Create(outFile);
            VisualHeightmapBinary.Write(fs, asset);
        }

        private static int HeightCmAt(MapVtxmGenerator.Preset preset, int width, int height, int x, int y)
        {
            return preset switch
            {
                MapVtxmGenerator.Preset.Flat => 600,
                MapVtxmGenerator.Preset.Lake => 600 + ((y / 32) % 4) * 120,
                MapVtxmGenerator.Preset.Cliffs => ((x / 16) & 1) == 0 ? 200 : 1200,
                MapVtxmGenerator.Preset.MountainRiver => MountainRiverFixtureTerrain.Sample(width, height, x, y).HeightCm,
                _ => 450 + (((x / 16) % 12) + (((y / 128) & 1) * 3)) * 80
            };
        }
    }
}
