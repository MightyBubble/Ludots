using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Tool;

public static class EastAsiaTerrainAssetGenerator
{
    public const double LongitudeMin = 70.0;
    public const double LongitudeMax = 150.0;
    public const double LatitudeMin = 8.0;
    public const double LatitudeMax = 55.0;
    public const int DefaultGridWidthChunks = 112;
    public const int DefaultGridHeightChunks = 64;
    public const int DefaultHexWidthChunks = 112;
    public const int DefaultHexHeightChunks = 64;
    public const int DefaultVisualSampleColumns = 7169;
    public const int DefaultVisualSampleRows = 4097;
    public const int DefaultCellSizeCm = 125_649;
    public const int DefaultWorldWidthCm = DefaultGridWidthChunks * ReactChunkSize * DefaultCellSizeCm;
    public const int DefaultWorldHeightCm = DefaultGridHeightChunks * ReactChunkSize * DefaultCellSizeCm;
    public const string DefaultSourceMetadataFileName = "east_asia_strategy_editor_imports_metadata.json";

    private const int ReactChunkSize = 64;
    private const int ReactCellStride = 4;
    private const int ReactChunkBytes = ReactChunkSize * ReactChunkSize * ReactCellStride;
    private const byte SeaWaterLevel = 1;
    private const byte DryLandMinHeightLevel = 1;
    private const short VisualSeaFloorCm = -1200;
    private const short VisualDryLandMinCm = 40;
    private const short VisualMaxLandCm = 4200;
    // Synthetic continuous bathymetry: sea depth ramps from 0 at the coastline down to
    // -VisualMaxOceanDepthCm far offshore, so land/sea joins are a slope instead of a cliff.
    // Kept the same order of magnitude as land relief (land max 42m) for visual coherence,
    // since the source asset compresses real elevation heavily.
    private const short VisualMaxOceanDepthCm = 6000;
    // Offshore distance (in visual samples) at which depth reaches ~63% of max (1 - 1/e).
    // Larger = gentler near-shore shelf so the coastline meets land almost flush.
    private const double VisualOceanShelfSamples = 130.0;
    private const float VisualDefaultHeight01 = 0.30f;
    private const double VisualHeightGamma = 0.55;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static EastAsiaTerrainGenerationSummary GeneratePlayableSet(
        string sourceRoot,
        string outputRoot,
        bool overwrite,
        int gridWidthChunks = DefaultGridWidthChunks,
        int gridHeightChunks = DefaultGridHeightChunks,
        int hexWidthChunks = DefaultHexWidthChunks,
        int hexHeightChunks = DefaultHexHeightChunks,
        int visualSampleColumns = DefaultVisualSampleColumns,
        int visualSampleRows = DefaultVisualSampleRows,
        int worldWidthCm = DefaultWorldWidthCm,
        int worldHeightCm = DefaultWorldHeightCm)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot)) throw new ArgumentException("Source root is required.", nameof(sourceRoot));
        if (string.IsNullOrWhiteSpace(outputRoot)) throw new ArgumentException("Output root is required.", nameof(outputRoot));
        ValidateChunks(gridWidthChunks, gridHeightChunks, nameof(gridWidthChunks));
        ValidateChunks(hexWidthChunks, hexHeightChunks, nameof(hexWidthChunks));
        ValidateVisualSamples(visualSampleColumns, visualSampleRows);
        if (worldWidthCm <= 0 || worldHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(worldWidthCm));

        string fullRoot = Path.GetFullPath(outputRoot);
        string dataDir = Path.Combine(fullRoot, "assets", "Data", "Maps");
        string terrainDir = Path.Combine(fullRoot, "assets", "terrain");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(terrainDir);

        string gridPath = Path.Combine(dataDir, "east_asia_grid_map_data.bin");
        string hexReactPath = Path.Combine(dataDir, "east_asia_hex_source_map_data.bin");
        string hexPath = Path.Combine(dataDir, "east_asia_hex.vtxm");
        string visualPath = Path.Combine(terrainDir, "east_asia_continuous.vhtm");
        string manifestPath = Path.Combine(terrainDir, "east_asia_terrain_profile.json");

        EastAsiaHeightSource source = EastAsiaHeightSource.Load(sourceRoot);
        using EastAsiaHeightCanvas canvas = EastAsiaHeightCanvas.Load(source);

        EastAsiaEditorImport gridImport = source.RequireEditorImport(gridWidthChunks, gridHeightChunks);
        EastAsiaEditorImport hexImport = source.RequireEditorImport(hexWidthChunks, hexHeightChunks);
        CopyExportedReactMapDataBin(gridImport, gridPath, overwrite);
        CopyExportedReactMapDataBin(hexImport, hexReactPath, overwrite);
        if (File.Exists(hexPath) && !overwrite) throw new IOException($"File exists: {hexPath}");
        ReactMapDataBinConverter.ConvertToVertexMapBinary(hexReactPath, hexPath);
        GenerateVisualHeightmap(canvas, visualPath, visualSampleColumns, visualSampleRows, worldWidthCm, worldHeightCm, overwrite);
        WriteManifest(
            source,
            manifestPath,
            overwrite,
            gridWidthChunks,
            gridHeightChunks,
            hexWidthChunks,
            hexHeightChunks,
            visualSampleColumns,
            visualSampleRows,
            worldWidthCm,
            worldHeightCm,
            gridPath,
            hexReactPath,
            hexPath,
            visualPath);

        return new EastAsiaTerrainGenerationSummary(
            gridPath,
            hexPath,
            hexReactPath,
            visualPath,
            manifestPath);
    }

    public static bool TryProjectLonLatToSourceUv(
        EastAsiaProjectionSpec projection,
        EastAsiaProjectedExtent extent,
        double lonDeg,
        double latDeg,
        out double u,
        out double v)
    {
        double phi1 = DegreesToRadians(projection.StandardParallel1Deg);
        double phi2 = DegreesToRadians(projection.StandardParallel2Deg);
        double phi0 = DegreesToRadians(projection.LatitudeOfOriginDeg);
        double lambda0 = DegreesToRadians(projection.CentralMeridianDeg);
        double phi = DegreesToRadians(latDeg);
        double lambda = DegreesToRadians(lonDeg);

        double n = 0.5 * (Math.Sin(phi1) + Math.Sin(phi2));
        if (Math.Abs(n) < 1e-9)
        {
            u = 0;
            v = 0;
            return false;
        }

        double c = (Math.Cos(phi1) * Math.Cos(phi1)) + (2.0 * n * Math.Sin(phi1));
        double rhoTerm = c - (2.0 * n * Math.Sin(phi));
        double rho0Term = c - (2.0 * n * Math.Sin(phi0));
        if (rhoTerm < 0 || rho0Term < 0)
        {
            u = 0;
            v = 0;
            return false;
        }

        double rho = projection.EarthRadiusM * Math.Sqrt(rhoTerm) / n;
        double rho0 = projection.EarthRadiusM * Math.Sqrt(rho0Term) / n;
        double theta = n * (lambda - lambda0);
        double x = rho * Math.Sin(theta);
        double y = rho0 - (rho * Math.Cos(theta));

        u = (x - extent.MinXM) / (extent.MaxXM - extent.MinXM);
        v = (extent.MaxYM - y) / (extent.MaxYM - extent.MinYM);
        return double.IsFinite(u) && double.IsFinite(v);
    }

    private static void GenerateReactMapDataBin(
        EastAsiaHeightCanvas canvas,
        string outFile,
        int widthChunks,
        int heightChunks,
        bool overwrite)
    {
        PrepareOutput(outFile, overwrite);

        int mapColumns = checked(widthChunks * ReactChunkSize);
        int mapRows = checked(heightChunks * ReactChunkSize);
        using var stream = File.Create(outFile);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(widthChunks);
        writer.Write(heightChunks);
        writer.Write((byte)ReactCellStride);

        var chunk = new byte[ReactChunkBytes];
        for (int chunkY = 0; chunkY < heightChunks; chunkY++)
        {
            for (int chunkX = 0; chunkX < widthChunks; chunkX++)
            {
                Array.Clear(chunk, 0, chunk.Length);
                for (int localY = 0; localY < ReactChunkSize; localY++)
                {
                    for (int localX = 0; localX < ReactChunkSize; localX++)
                    {
                        int column = (chunkX * ReactChunkSize) + localX;
                        int row = (chunkY * ReactChunkSize) + localY;
                        ushort raw = canvas.SampleCellCenter(column, row, mapColumns, mapRows);
                        bool sea = raw == 0;
                        byte height = sea
                            ? (byte)0
                            : (byte)Math.Max(DryLandMinHeightLevel, (int)Math.Round(raw / 65535.0 * 15.0, MidpointRounding.ToEven));
                        byte water = sea ? SeaWaterLevel : (byte)0;

                        int offset = ((localY * ReactChunkSize) + localX) * ReactCellStride;
                        chunk[offset + 0] = (byte)(((height & 0x0F) << 4) | (water & 0x0F));
                        chunk[offset + 1] = 0;
                        chunk[offset + 2] = 0;
                        chunk[offset + 3] = 0;
                    }
                }

                writer.Write(chunk);
            }
        }
    }

    private static void CopyExportedReactMapDataBin(EastAsiaEditorImport sourceImport, string outFile, bool overwrite)
    {
        PrepareOutput(outFile, overwrite);
        File.Copy(sourceImport.FullPath, outFile, overwrite: true);
        string copiedHash = ComputeSha256(outFile);
        if (!string.Equals(copiedHash, sourceImport.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Copied React terrain hash mismatch for '{outFile}'. Expected {sourceImport.Sha256}, got {copiedHash}.");
        }
    }

    private static void GenerateVisualHeightmap(
        EastAsiaHeightCanvas canvas,
        string outFile,
        int sampleColumns,
        int sampleRows,
        int worldWidthCm,
        int worldHeightCm,
        bool overwrite)
    {
        ValidateVisualSamples(sampleColumns, sampleRows);
        PrepareOutput(outFile, overwrite);

        var samples = new short[checked(sampleColumns * sampleRows)];
        var rawGrid = new ushort[samples.Length];
        for (int row = 0; row < sampleRows; row++)
        {
            for (int column = 0; column < sampleColumns; column++)
            {
                rawGrid[(row * sampleColumns) + column] = canvas.SampleEndpoint(column, row, sampleColumns, sampleRows);
            }
        }

        // Continuous bathymetry: distance transform of the sea region to the nearest coast,
        // then map distance -> smooth depth. Land keeps its gamma-lifted elevation.
        float[] seaDistance = ComputeSeaDistanceToCoast(rawGrid, sampleColumns, sampleRows);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = rawGrid[i] == 0
                ? ScaleSeaDepthCm(seaDistance[i])
                : ScaleVisualHeightCm(rawGrid[i]);
        }

        var asset = VisualHeightmapAsset.CreateSingleLayer(
            new WorldAabbCm(-(worldWidthCm / 2), -(worldHeightCm / 2), worldWidthCm, worldHeightCm),
            sampleColumns,
            sampleRows,
            samples,
            layerName: "east_asia",
            interpolationMode: VisualHeightmapInterpolationMode.TriangleHeightfield);
        using var stream = File.Create(outFile);
        VisualHeightmapBinary.Write(stream, asset);
    }

    private static short ScaleVisualHeightCm(ushort raw)
    {
        if (raw == 0)
        {
            return VisualSeaFloorCm;
        }

        double normalized = raw / 65535.0;
        double lifted = Math.Pow(normalized, VisualHeightGamma);
        double cm = VisualDryLandMinCm + (lifted * (VisualMaxLandCm - VisualDryLandMinCm));
        return (short)Math.Clamp((int)Math.Round(cm, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
    }

    // Maps offshore distance (in samples) to a continuous negative depth.
    // Exponential approach to the abyssal floor gives a steep-ish shelf near the coast
    // that flattens far offshore, and crucially depth -> 0 exactly at the coastline
    // so land and sea meet without a vertical step.
    private static short ScaleSeaDepthCm(float distanceSamples)
    {
        if (distanceSamples <= 0f)
        {
            return 0;
        }

        double falloff = 1.0 - Math.Exp(-distanceSamples / VisualOceanShelfSamples);
        double cm = -(falloff * VisualMaxOceanDepthCm);
        return (short)Math.Clamp((int)Math.Round(cm, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
    }

    // Two-pass chamfer distance transform: for every sea sample (raw==0), the Euclidean-ish
    // distance (in sample units) to the nearest land/coast sample. Land samples get 0.
    private static float[] ComputeSeaDistanceToCoast(ushort[] rawGrid, int columns, int rows)
    {
        const float diag = 1.41421356f;
        var dist = new float[rawGrid.Length];
        float far = (columns + rows) * 2f;
        for (int i = 0; i < dist.Length; i++)
        {
            dist[i] = rawGrid[i] == 0 ? far : 0f;
        }

        // Forward pass (top-left -> bottom-right).
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int idx = (y * columns) + x;
                if (dist[idx] == 0f) continue;
                float best = dist[idx];
                if (x > 0) best = Math.Min(best, dist[idx - 1] + 1f);
                if (y > 0) best = Math.Min(best, dist[idx - columns] + 1f);
                if (x > 0 && y > 0) best = Math.Min(best, dist[idx - columns - 1] + diag);
                if (x < columns - 1 && y > 0) best = Math.Min(best, dist[idx - columns + 1] + diag);
                dist[idx] = best;
            }
        }

        // Backward pass (bottom-right -> top-left).
        for (int y = rows - 1; y >= 0; y--)
        {
            for (int x = columns - 1; x >= 0; x--)
            {
                int idx = (y * columns) + x;
                if (dist[idx] == 0f) continue;
                float best = dist[idx];
                if (x < columns - 1) best = Math.Min(best, dist[idx + 1] + 1f);
                if (y < rows - 1) best = Math.Min(best, dist[idx + columns] + 1f);
                if (x < columns - 1 && y < rows - 1) best = Math.Min(best, dist[idx + columns + 1] + diag);
                if (x > 0 && y < rows - 1) best = Math.Min(best, dist[idx + columns - 1] + diag);
                dist[idx] = best;
            }
        }

        return dist;
    }

    private static void ValidateChunks(int widthChunks, int heightChunks, string name)
    {
        if (widthChunks <= 0 || heightChunks <= 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateVisualSamples(int sampleColumns, int sampleRows)
    {
        if (sampleColumns < 2 || sampleRows < 2) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
        if (((sampleColumns - 1) % 256) != 0 || ((sampleRows - 1) % 256) != 0)
        {
            throw new ArgumentException("East Asia visual heightmap samples must be 256*n+1 so the visual terrain editor can import it.");
        }
    }

    private static void PrepareOutput(string outFile, bool overwrite)
    {
        string fullPath = Path.GetFullPath(outFile);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        if (File.Exists(fullPath) && !overwrite)
        {
            throw new IOException($"File exists: {fullPath}");
        }
    }

    private static void WriteManifest(
        EastAsiaHeightSource source,
        string outFile,
        bool overwrite,
        int gridWidthChunks,
        int gridHeightChunks,
        int hexWidthChunks,
        int hexHeightChunks,
        int visualSampleColumns,
        int visualSampleRows,
        int worldWidthCm,
        int worldHeightCm,
        string gridPath,
        string hexReactPath,
        string hexPath,
        string visualPath)
    {
        PrepareOutput(outFile, overwrite);
        var sourceTiles = new List<object>(source.Tiles.Count);
        foreach (EastAsiaHeightTile tile in source.Tiles)
        {
            sourceTiles.Add(new
            {
                file = tile.File,
                sha256 = tile.Sha256,
                tile_x = tile.TileX,
                tile_y = tile.TileY,
                pixel_x = tile.PixelX,
                pixel_y = tile.PixelY,
                width_px = tile.WidthPx,
                height_px = tile.HeightPx
            });
        }
        var editorImports = new List<object>(source.EditorImports.Count);
        foreach (EastAsiaEditorImport editorImport in source.EditorImports)
        {
            editorImports.Add(new
            {
                file = editorImport.File,
                sha256 = editorImport.Sha256,
                width_chunks = editorImport.WidthChunks,
                height_chunks = editorImport.HeightChunks,
                width_cells = editorImport.WidthCells,
                height_cells = editorImport.HeightCells,
                bytes = editorImport.Bytes
            });
        }

        var profile = new
        {
            id = "east_asia_playable_terrain_v1",
            source = new
            {
                metadataFile = DefaultSourceMetadataFileName,
                metadataSha256 = source.MetadataSha256,
                artifact = source.Artifact,
                purpose = source.Purpose,
                tiles = sourceTiles,
                editorImports,
                heightEncoding = new
                {
                    zero = "sea and flattened low water",
                    maxValue = 65535,
                    sourceScaleTopStylizedMeters = source.ScaleTopMeters
                }
            },
            projection = new
            {
                kind = source.Projection.Name,
                centralMeridianDeg = source.Projection.CentralMeridianDeg,
                latitudeOfOriginDeg = source.Projection.LatitudeOfOriginDeg,
                standardParallel1Deg = source.Projection.StandardParallel1Deg,
                standardParallel2Deg = source.Projection.StandardParallel2Deg,
                earthRadiusM = source.Projection.EarthRadiusM,
                note = "Grid, HexGrid, and VisualHeightmap sample the projected Albers canvas directly. They are not stretched lon/lat rectangles."
            },
            sourceRaster = new
            {
                widthPx = source.WidthPx,
                heightPx = source.HeightPx,
                metersPerPixel = source.MetersPerPixel,
                projectedExtentM = new
                {
                    minX = source.Extent.MinXM,
                    maxX = source.Extent.MaxXM,
                    minY = source.Extent.MinYM,
                    maxY = source.Extent.MaxYM
                }
            },
            grid = new
            {
                widthChunks = gridWidthChunks,
                heightChunks = gridHeightChunks,
                widthCells = gridWidthChunks * ReactChunkSize,
                heightCells = gridHeightChunks * ReactChunkSize,
                chunkSizeCells = ReactChunkSize,
                sourcePixelsPerCellX = source.WidthPx / (double)(gridWidthChunks * ReactChunkSize),
                sourcePixelsPerCellY = source.HeightPx / (double)(gridHeightChunks * ReactChunkSize)
            },
            hex = new
            {
                widthChunks = hexWidthChunks,
                heightChunks = hexHeightChunks,
                widthCells = hexWidthChunks * ReactChunkSize,
                heightCells = hexHeightChunks * ReactChunkSize,
                chunkSizeCells = ReactChunkSize,
                sourcePixelsPerCellX = source.WidthPx / (double)(hexWidthChunks * ReactChunkSize),
                sourcePixelsPerCellY = source.HeightPx / (double)(hexHeightChunks * ReactChunkSize)
            },
            visualHeightmap = new
            {
                sampleColumns = visualSampleColumns,
                sampleRows = visualSampleRows,
                worldWidthCm,
                worldHeightCm,
                seaLevelCm = 0,
                seaFloorCm = VisualSeaFloorCm,
                bathymetry = "continuous_distance_to_coast",
                maxOceanDepthCm = VisualMaxOceanDepthCm,
                oceanShelfSamples = VisualOceanShelfSamples,
                dryLandMinCm = VisualDryLandMinCm,
                maxLandCm = VisualMaxLandCm,
                landGamma = VisualHeightGamma,
                editorDefaultHeight01 = VisualDefaultHeight01,
                editorHeightAmplitudeCm = 6000
            },
            outputs = new
            {
                gridReactMapData = new
                {
                    file = "assets/Data/Maps/east_asia_grid_map_data.bin",
                    sha256 = ComputeSha256(gridPath)
                },
                hexReactSourceMapData = new
                {
                    file = "assets/Data/Maps/east_asia_hex_source_map_data.bin",
                    sha256 = ComputeSha256(hexReactPath)
                },
                hexVertexMap = new
                {
                    file = "assets/Data/Maps/east_asia_hex.vtxm",
                    sha256 = ComputeSha256(hexPath)
                },
                visualHeightmap = new
                {
                    file = "assets/terrain/east_asia_continuous.vhtm",
                    sha256 = ComputeSha256(visualPath)
                }
            }
        };
        string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outFile, json, Utf8NoBom);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private sealed class EastAsiaHeightCanvas : IDisposable
    {
        private readonly ushort[] _samples;

        private EastAsiaHeightCanvas(int width, int height, ushort[] samples)
        {
            Width = width;
            Height = height;
            _samples = samples;
        }

        public int Width { get; }

        public int Height { get; }

        public static EastAsiaHeightCanvas Load(EastAsiaHeightSource source)
        {
            var samples = new ushort[checked(source.WidthPx * source.HeightPx)];
            foreach (EastAsiaHeightTile tile in source.Tiles)
            {
                PngU16GrayImage image = PngU16GrayImage.Load(tile.FullPath);
                if (image.Width != tile.WidthPx || image.Height != tile.HeightPx)
                {
                    throw new InvalidDataException(
                        $"Tile '{tile.FullPath}' decoded as {image.Width}x{image.Height}; metadata declares {tile.WidthPx}x{tile.HeightPx}.");
                }

                for (int row = 0; row < image.Height; row++)
                {
                    int sourceOffset = row * image.Width;
                    int targetOffset = ((tile.PixelY + row) * source.WidthPx) + tile.PixelX;
                    Array.Copy(image.Samples, sourceOffset, samples, targetOffset, image.Width);
                }
            }

            return new EastAsiaHeightCanvas(source.WidthPx, source.HeightPx, samples);
        }

        public ushort SampleCellCenter(int column, int row, int columns, int rows)
        {
            int sourceX = Math.Clamp((int)Math.Floor((column + 0.5) * Width / columns), 0, Width - 1);
            int sourceY = Math.Clamp((int)Math.Floor((row + 0.5) * Height / rows), 0, Height - 1);
            return _samples[(sourceY * Width) + sourceX];
        }

        public ushort SampleEndpoint(int column, int row, int columns, int rows)
        {
            int sourceX = columns <= 1 ? 0 : (int)Math.Round(column * (Width - 1) / (double)(columns - 1), MidpointRounding.AwayFromZero);
            int sourceY = rows <= 1 ? 0 : (int)Math.Round(row * (Height - 1) / (double)(rows - 1), MidpointRounding.AwayFromZero);
            sourceX = Math.Clamp(sourceX, 0, Width - 1);
            sourceY = Math.Clamp(sourceY, 0, Height - 1);
            return _samples[(sourceY * Width) + sourceX];
        }

        public void Dispose()
        {
        }
    }

    public sealed record EastAsiaProjectionSpec(
        string Name,
        double CentralMeridianDeg,
        double LatitudeOfOriginDeg,
        double StandardParallel1Deg,
        double StandardParallel2Deg,
        double EarthRadiusM);

    public sealed record EastAsiaProjectedExtent(
        double MinXM,
        double MaxXM,
        double MinYM,
        double MaxYM);

    private sealed record EastAsiaHeightTile(
        string File,
        string FullPath,
        string Sha256,
        int TileX,
        int TileY,
        int PixelX,
        int PixelY,
        int WidthPx,
        int HeightPx);

    private sealed record EastAsiaEditorImport(
        string File,
        string FullPath,
        string Sha256,
        int WidthChunks,
        int HeightChunks,
        int WidthCells,
        int HeightCells,
        long Bytes);

    private sealed class EastAsiaHeightSource
    {
        private EastAsiaHeightSource(
            string artifact,
            string purpose,
            string metadataSha256,
            int widthPx,
            int heightPx,
            double metersPerPixel,
            double scaleTopMeters,
            EastAsiaProjectionSpec projection,
            EastAsiaProjectedExtent extent,
            IReadOnlyList<EastAsiaHeightTile> tiles,
            IReadOnlyList<EastAsiaEditorImport> editorImports)
        {
            Artifact = artifact;
            Purpose = purpose;
            MetadataSha256 = metadataSha256;
            WidthPx = widthPx;
            HeightPx = heightPx;
            MetersPerPixel = metersPerPixel;
            ScaleTopMeters = scaleTopMeters;
            Projection = projection;
            Extent = extent;
            Tiles = tiles;
            EditorImports = editorImports;
        }

        public string Artifact { get; }

        public string Purpose { get; }

        public string MetadataSha256 { get; }

        public int WidthPx { get; }

        public int HeightPx { get; }

        public double MetersPerPixel { get; }

        public double ScaleTopMeters { get; }

        public EastAsiaProjectionSpec Projection { get; }

        public EastAsiaProjectedExtent Extent { get; }

        public IReadOnlyList<EastAsiaHeightTile> Tiles { get; }

        public IReadOnlyList<EastAsiaEditorImport> EditorImports { get; }

        public EastAsiaEditorImport RequireEditorImport(int widthChunks, int heightChunks)
        {
            foreach (EastAsiaEditorImport editorImport in EditorImports)
            {
                if (editorImport.WidthChunks == widthChunks && editorImport.HeightChunks == heightChunks)
                {
                    return editorImport;
                }
            }

            throw new InvalidDataException(
                $"East Asia exported editor import for {widthChunks}x{heightChunks} chunks was not found in '{DefaultSourceMetadataFileName}'.");
        }

        public static EastAsiaHeightSource Load(string sourceRoot)
        {
            string fullSourceRoot = Path.GetFullPath(sourceRoot);
            string metadataPath = Path.Combine(fullSourceRoot, DefaultSourceMetadataFileName);
            if (!File.Exists(metadataPath))
            {
                throw new FileNotFoundException("East Asia heightmap metadata file is required.", metadataPath);
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            JsonElement root = document.RootElement;
            JsonElement outputRaster = root.GetProperty("output_raster");
            JsonElement projection = root.GetProperty("output_projection");
            JsonElement extent = outputRaster.GetProperty("projected_extent_m");
            JsonElement stats = root.GetProperty("stats");

            int widthPx = outputRaster.GetProperty("width_px").GetInt32();
            int heightPx = outputRaster.GetProperty("height_px").GetInt32();
            var tiles = new List<EastAsiaHeightTile>();
            foreach (JsonElement tile in root.GetProperty("tiles").EnumerateArray())
            {
                string file = tile.GetProperty("file").GetString() ?? throw new InvalidDataException("Tile file is required.");
                string fullPath = Path.Combine(fullSourceRoot, file);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("East Asia heightmap tile file is required.", fullPath);
                }

                tiles.Add(new EastAsiaHeightTile(
                    file,
                    fullPath,
                    ComputeSha256(fullPath),
                    tile.GetProperty("tile_x").GetInt32(),
                    tile.GetProperty("tile_y").GetInt32(),
                    tile.GetProperty("pixel_x").GetInt32(),
                    tile.GetProperty("pixel_y").GetInt32(),
                    tile.GetProperty("width_px").GetInt32(),
                    tile.GetProperty("height_px").GetInt32()));
            }
            if (!root.TryGetProperty("editor_imports", out JsonElement importsElement) ||
                importsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    $"East Asia source metadata '{metadataPath}' must contain editor_imports produced by build_editor_map_data.py.");
            }

            var editorImports = new List<EastAsiaEditorImport>();
            foreach (JsonElement editorImport in importsElement.EnumerateArray())
            {
                string file = editorImport.GetProperty("file").GetString() ??
                    throw new InvalidDataException("Editor import file is required.");
                string fullPath = Path.Combine(fullSourceRoot, file);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("East Asia editor map_data.bin file is required.", fullPath);
                }

                long bytes = editorImport.GetProperty("bytes").GetInt64();
                long actualBytes = new FileInfo(fullPath).Length;
                if (actualBytes != bytes)
                {
                    throw new InvalidDataException(
                        $"Editor import '{fullPath}' has {actualBytes} bytes; metadata declares {bytes}.");
                }

                editorImports.Add(new EastAsiaEditorImport(
                    file,
                    fullPath,
                    ComputeSha256(fullPath),
                    editorImport.GetProperty("width_chunks").GetInt32(),
                    editorImport.GetProperty("height_chunks").GetInt32(),
                    editorImport.GetProperty("width_cells").GetInt32(),
                    editorImport.GetProperty("height_cells").GetInt32(),
                    bytes));
            }

            return new EastAsiaHeightSource(
                root.GetProperty("artifact").GetString() ?? string.Empty,
                root.GetProperty("purpose").GetString() ?? string.Empty,
                ComputeSha256(metadataPath),
                widthPx,
                heightPx,
                extent.GetProperty("meters_per_pixel").GetDouble(),
                stats.GetProperty("stylized_scale_top_m").GetDouble(),
                new EastAsiaProjectionSpec(
                    projection.GetProperty("name").GetString() ?? string.Empty,
                    projection.GetProperty("central_meridian_deg").GetDouble(),
                    projection.GetProperty("latitude_of_origin_deg").GetDouble(),
                    projection.GetProperty("standard_parallel_1_deg").GetDouble(),
                    projection.GetProperty("standard_parallel_2_deg").GetDouble(),
                    projection.GetProperty("earth_radius_m").GetDouble()),
                new EastAsiaProjectedExtent(
                    extent.GetProperty("min_x_m").GetDouble(),
                    extent.GetProperty("max_x_m").GetDouble(),
                    extent.GetProperty("min_y_m").GetDouble(),
                    extent.GetProperty("max_y_m").GetDouble()),
                tiles,
                editorImports);
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }
    }

    private sealed class PngU16GrayImage
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        private PngU16GrayImage(int width, int height, ushort[] samples)
        {
            Width = width;
            Height = height;
            Samples = samples;
        }

        public int Width { get; }

        public int Height { get; }

        public ushort[] Samples { get; }

        public static PngU16GrayImage Load(string path)
        {
            using var input = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[8];
            ReadExact(input, signature);
            if (!signature.SequenceEqual(Signature))
            {
                throw new InvalidDataException($"'{path}' is not a PNG file.");
            }

            int width = 0;
            int height = 0;
            int bitDepth = 0;
            int colorType = 0;
            int compression = 0;
            int filter = 0;
            int interlace = 0;
            using var idat = new MemoryStream();
            Span<byte> lengthBuffer = stackalloc byte[4];
            Span<byte> typeBuffer = stackalloc byte[4];
            while (true)
            {
                ReadExact(input, lengthBuffer);
                int length = ReadBigEndianInt32(lengthBuffer);
                ReadExact(input, typeBuffer);
                string type = Encoding.ASCII.GetString(typeBuffer);
                byte[] payload = new byte[length];
                ReadExact(input, payload);
                input.Position += 4;

                if (type == "IHDR")
                {
                    width = ReadBigEndianInt32(payload.AsSpan(0, 4));
                    height = ReadBigEndianInt32(payload.AsSpan(4, 4));
                    bitDepth = payload[8];
                    colorType = payload[9];
                    compression = payload[10];
                    filter = payload[11];
                    interlace = payload[12];
                }
                else if (type == "IDAT")
                {
                    idat.Write(payload, 0, payload.Length);
                }
                else if (type == "IEND")
                {
                    break;
                }
            }

            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException($"'{path}' does not contain a valid IHDR chunk.");
            }

            if (bitDepth != 16 || colorType != 0 || compression != 0 || filter != 0 || interlace != 0)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{path}' must be non-interlaced 16-bit grayscale PNG. Actual bitDepth={bitDepth}, colorType={colorType}, compression={compression}, filter={filter}, interlace={interlace}."));
            }

            idat.Position = 0;
            using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
            int rowBytes = checked(width * 2);
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];
            var filtered = new byte[rowBytes];
            var samples = new ushort[checked(width * height)];

            for (int row = 0; row < height; row++)
            {
                int filterType = zlib.ReadByte();
                if (filterType < 0)
                {
                    throw new EndOfStreamException($"Unexpected EOF before PNG scanline {row} in '{path}'.");
                }

                ReadExact(zlib, filtered);
                ReconstructScanline(filterType, filtered, previous, current, bytesPerPixel: 2);
                for (int column = 0; column < width; column++)
                {
                    int offset = column * 2;
                    samples[(row * width) + column] = (ushort)((current[offset] << 8) | current[offset + 1]);
                }

                (previous, current) = (current, previous);
            }

            return new PngU16GrayImage(width, height, samples);
        }

        private static void ReconstructScanline(int filterType, byte[] filtered, byte[] previous, byte[] output, int bytesPerPixel)
        {
            switch (filterType)
            {
                case 0:
                    Buffer.BlockCopy(filtered, 0, output, 0, filtered.Length);
                    return;

                case 1:
                    for (int i = 0; i < filtered.Length; i++)
                    {
                        int left = i >= bytesPerPixel ? output[i - bytesPerPixel] : 0;
                        output[i] = unchecked((byte)(filtered[i] + left));
                    }

                    return;

                case 2:
                    for (int i = 0; i < filtered.Length; i++)
                    {
                        output[i] = unchecked((byte)(filtered[i] + previous[i]));
                    }

                    return;

                case 3:
                    for (int i = 0; i < filtered.Length; i++)
                    {
                        int left = i >= bytesPerPixel ? output[i - bytesPerPixel] : 0;
                        int up = previous[i];
                        output[i] = unchecked((byte)(filtered[i] + ((left + up) >> 1)));
                    }

                    return;

                case 4:
                    for (int i = 0; i < filtered.Length; i++)
                    {
                        int left = i >= bytesPerPixel ? output[i - bytesPerPixel] : 0;
                        int up = previous[i];
                        int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                        output[i] = unchecked((byte)(filtered[i] + Paeth(left, up, upLeft)));
                    }

                    return;

                default:
                    throw new InvalidDataException($"Unsupported PNG scanline filter {filterType}.");
            }
        }

        private static int Paeth(int left, int up, int upLeft)
        {
            int p = left + up - upLeft;
            int pa = Math.Abs(p - left);
            int pb = Math.Abs(p - up);
            int pc = Math.Abs(p - upLeft);
            if (pa <= pb && pa <= pc) return left;
            return pb <= pc ? up : upLeft;
        }

        private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes)
            => (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

        private static void ReadExact(Stream stream, Span<byte> buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer[offset..]);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }
        }
    }
}

public readonly record struct EastAsiaTerrainGenerationSummary(
    string GridMapDataPath,
    string HexVertexMapPath,
    string HexSourceReactMapDataPath,
    string VisualHeightmapPath,
    string ManifestPath);
