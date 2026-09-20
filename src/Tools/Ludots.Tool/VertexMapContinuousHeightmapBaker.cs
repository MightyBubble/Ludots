using Ludots.Platform.Abstractions;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Tool;

public static class VertexMapContinuousHeightmapBaker
{
    public static ContinuousHeightmapAsset Bake(
        VertexMap map,
        int heightStepCm,
        int hexEdgeLengthCm)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (heightStepCm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightStepCm));
        }
        if (hexEdgeLengthCm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hexEdgeLengthCm));
        }

        int vertexColumns = checked(map.WidthInChunks * VertexChunk.ChunkSize);
        int vertexRows = checked(map.HeightInChunks * VertexChunk.ChunkSize);
        if (vertexColumns < 2 || vertexRows < 2)
        {
            throw new InvalidOperationException("VertexMap visual height bake requires at least two rows and columns.");
        }

        int sampleColumns = checked(vertexColumns * 2);
        int sampleRows = vertexRows;
        var samples = new short[checked(sampleColumns * sampleRows)];
        for (int row = 0; row < sampleRows; row++)
        {
            int rowParity = row & 1;
            int rowOffset = row * sampleColumns;
            for (int halfColumn = 0; halfColumn < sampleColumns; halfColumn++)
            {
                int heightLevelTwice;
                if ((halfColumn & 1) == rowParity)
                {
                    int sourceColumn = (halfColumn - rowParity) / 2;
                    heightLevelTwice = checked(map.GetHeight(sourceColumn, row) * 2);
                }
                else
                {
                    int rightColumn = rowParity == 0
                        ? (halfColumn + 1) / 2
                        : halfColumn / 2;
                    int leftColumn = rightColumn - 1;
                    leftColumn = Math.Clamp(leftColumn, 0, vertexColumns - 1);
                    rightColumn = Math.Clamp(rightColumn, 0, vertexColumns - 1);
                    heightLevelTwice = checked(
                        map.GetHeight(leftColumn, row) + map.GetHeight(rightColumn, row));
                }

                int sampleHeightCm = checked((heightLevelTwice * heightStepCm) / 2);
                samples[rowOffset + halfColumn] = checked((short)sampleHeightCm);
            }
        }

        double hexWidthCm = Math.Sqrt(3d) * hexEdgeLengthCm;
        double halfHexWidthCm = hexWidthCm * 0.5d;
        double rowSpacingCm = hexEdgeLengthCm * 1.5d;
        int widthCm = checked((int)Math.Round(
            (sampleColumns - 1) * halfHexWidthCm,
            MidpointRounding.AwayFromZero));
        int heightCm = checked((int)Math.Round(
            (sampleRows - 1) * rowSpacingCm,
            MidpointRounding.AwayFromZero));

        return ContinuousHeightmapAsset.CreateSingleLayer(
            new WorldAabbCm(0, 0, widthCm, heightCm),
            sampleColumns,
            sampleRows,
            samples,
            layerName: "vertex-map-ground",
            interpolationMode: ContinuousHeightmapInterpolationMode.TriangleHeightfield);
    }

    public static ContinuousHeightmapAsset BakeFile(
        string inputPath,
        string outputPath,
        int heightStepCm,
        int hexEdgeLengthCm,
        bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            throw new FileNotFoundException("VertexMap input file was not found.", inputPath);
        }
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Visual heightmap output path is required.", nameof(outputPath));
        }

        string fullOutputPath = Path.GetFullPath(outputPath);
        if (!overwrite && File.Exists(fullOutputPath))
        {
            throw new IOException($"Visual heightmap output already exists: {fullOutputPath}");
        }

        VertexMap map;
        using (FileStream input = File.OpenRead(inputPath))
        {
            map = VertexMapBinary.Read(input);
        }

        ContinuousHeightmapAsset asset = Bake(map, heightStepCm, hexEdgeLengthCm);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
        using (FileStream output = File.Create(fullOutputPath))
        {
            ContinuousHeightmapBinary.Write(output, asset);
        }

        return asset;
    }
}
