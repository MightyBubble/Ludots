using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Tool;

public readonly record struct WalkabilityTextureBounds(
    int MinXcm,
    int MinZcm,
    int MaxXcm,
    int MaxZcm)
{
    public int WidthCm => checked(MaxXcm - MinXcm);

    public int HeightCm => checked(MaxZcm - MinZcm);

    public void Validate()
    {
        if (MaxXcm <= MinXcm || MaxZcm <= MinZcm)
        {
            throw new InvalidOperationException(
                $"Walkability texture bounds must have positive area: ({MinXcm},{MinZcm})-({MaxXcm},{MaxZcm}).");
        }
    }
}

public readonly record struct WalkabilityAreaColor(byte R, byte G, byte B);

public sealed record WalkabilityTextureExportResult(
    int TileCount,
    int TriangleCount,
    int Width,
    int Height,
    WalkabilityTextureBounds Bounds,
    string ContentHash,
    string PngPath,
    string SidecarPath);

public static class WalkabilityTextureExporter
{
    public static WalkabilityTextureExportResult ExportDirectory(
        string inputDirectory,
        string outputPngPath,
        int width,
        int height = 0,
        WalkabilityTextureBounds? explicitBounds = null)
    {
        if (string.IsNullOrWhiteSpace(inputDirectory))
        {
            throw new ArgumentException("NavTile input directory is required.", nameof(inputDirectory));
        }

        if (!Directory.Exists(inputDirectory))
        {
            throw new DirectoryNotFoundException($"NavTile input directory not found: {inputDirectory}");
        }

        string[] paths = Directory
            .EnumerateFiles(inputDirectory, "*.ntil", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidOperationException($"NavTile input directory contains no .ntil files: {inputDirectory}");
        }

        var tiles = new NavTile[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            using FileStream stream = File.OpenRead(paths[i]);
            tiles[i] = NavTileBinary.Read(stream);
        }

        WalkabilityTextureBounds bounds = explicitBounds ?? DeriveBounds(tiles);
        bounds.Validate();
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Texture width must be positive.");
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Texture height cannot be negative.");
        }

        if (height == 0)
        {
            height = Math.Max(
                1,
                checked((int)Math.Round(width * (double)bounds.HeightCm / bounds.WidthCm)));
        }

        ValidateBoundsContainTiles(tiles, bounds);
        byte[] rgba = Rasterize(tiles, bounds, width, height);
        byte[] png = RgbaPngEncoder.Encode(width, height, rgba);
        string hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();

        string fullPngPath = Path.GetFullPath(outputPngPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPngPath)!);
        File.WriteAllBytes(fullPngPath, png);

        int triangleCount = 0;
        for (int i = 0; i < tiles.Length; i++)
        {
            triangleCount = checked(triangleCount + tiles[i].TriangleCount);
        }

        string sidecarPath = fullPngPath + ".json";
        var sidecar = new
        {
            schemaVersion = 1,
            boundsCm = new
            {
                minX = bounds.MinXcm,
                minZ = bounds.MinZcm,
                maxX = bounds.MaxXcm,
                maxZ = bounds.MaxZcm,
            },
            width,
            height,
            encoding = new
            {
                format = "RGBA8",
                row0 = "maxZcm",
                alpha = "0=not-covered-or-blocked,255=walkable",
                rgb = "deterministic TriAreaIds palette",
            },
            sourceTileCount = tiles.Length,
            triangleCount,
            contentHash = "sha256:" + hash,
        };
        File.WriteAllText(
            sidecarPath,
            JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        return new WalkabilityTextureExportResult(
            tiles.Length,
            triangleCount,
            width,
            height,
            bounds,
            "sha256:" + hash,
            fullPngPath,
            sidecarPath);
    }

    public static byte[] Rasterize(
        IReadOnlyList<NavTile> tiles,
        WalkabilityTextureBounds bounds,
        int width,
        int height)
    {
        if (tiles == null) throw new ArgumentNullException(nameof(tiles));
        bounds.Validate();
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var rgba = new byte[checked(width * height * 4)];
        for (int tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
        {
            NavTile tile = tiles[tileIndex]
                ?? throw new InvalidDataException($"NavTile[{tileIndex}] is null.");
            ValidateTile(tile);
            for (int triangleIndex = 0; triangleIndex < tile.TriangleCount; triangleIndex++)
            {
                int a = tile.TriA[triangleIndex];
                int b = tile.TriB[triangleIndex];
                int c = tile.TriC[triangleIndex];
                long ax = (long)tile.OriginXcm + tile.VertexXcm[a];
                long az = (long)tile.OriginZcm + tile.VertexZcm[a];
                long bx = (long)tile.OriginXcm + tile.VertexXcm[b];
                long bz = (long)tile.OriginZcm + tile.VertexZcm[b];
                long cx = (long)tile.OriginXcm + tile.VertexXcm[c];
                long cz = (long)tile.OriginZcm + tile.VertexZcm[c];
                RasterizeTriangle(
                    rgba,
                    width,
                    height,
                    bounds,
                    ax,
                    az,
                    bx,
                    bz,
                    cx,
                    cz,
                    tile.TriAreaIds[triangleIndex]);
            }
        }

        return rgba;
    }

    public static WalkabilityAreaColor GetAreaColor(byte areaId)
    {
        return new WalkabilityAreaColor(
            (byte)(48 + (((areaId * 67) + 31) % 176)),
            (byte)(48 + (((areaId * 97) + 89) % 176)),
            (byte)(48 + (((areaId * 131) + 151) % 176)));
    }

    private static WalkabilityTextureBounds DeriveBounds(IReadOnlyList<NavTile> tiles)
    {
        long minX = long.MaxValue;
        long minZ = long.MaxValue;
        long maxX = long.MinValue;
        long maxZ = long.MinValue;
        for (int tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
        {
            NavTile tile = tiles[tileIndex];
            ValidateTile(tile);
            for (int vertexIndex = 0; vertexIndex < tile.VertexCount; vertexIndex++)
            {
                long x = (long)tile.OriginXcm + tile.VertexXcm[vertexIndex];
                long z = (long)tile.OriginZcm + tile.VertexZcm[vertexIndex];
                minX = Math.Min(minX, x);
                minZ = Math.Min(minZ, z);
                maxX = Math.Max(maxX, x);
                maxZ = Math.Max(maxZ, z);
            }
        }

        if (minX == long.MaxValue)
        {
            throw new InvalidOperationException(
                "NavTile set has no walkable vertices; pass explicit world bounds to export a transparent texture.");
        }

        return new WalkabilityTextureBounds(
            checked((int)minX),
            checked((int)minZ),
            checked((int)maxX),
            checked((int)maxZ));
    }

    private static void ValidateBoundsContainTiles(
        IReadOnlyList<NavTile> tiles,
        WalkabilityTextureBounds bounds)
    {
        for (int tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
        {
            NavTile tile = tiles[tileIndex];
            for (int vertexIndex = 0; vertexIndex < tile.VertexCount; vertexIndex++)
            {
                long x = (long)tile.OriginXcm + tile.VertexXcm[vertexIndex];
                long z = (long)tile.OriginZcm + tile.VertexZcm[vertexIndex];
                if (x < bounds.MinXcm ||
                    x > bounds.MaxXcm ||
                    z < bounds.MinZcm ||
                    z > bounds.MaxZcm)
                {
                    throw new InvalidOperationException(
                        $"Explicit bounds do not contain NavTile {tile.TileId} vertex {vertexIndex} at ({x},{z})cm.");
                }
            }
        }
    }

    private static void ValidateTile(NavTile tile)
    {
        if (tile == null) throw new ArgumentNullException(nameof(tile));
        if (tile.VertexYcm.Length != tile.VertexCount ||
            tile.VertexZcm.Length != tile.VertexCount)
        {
            throw new InvalidDataException($"NavTile {tile.TileId} vertex arrays have mismatched lengths.");
        }

        if (tile.TriB.Length != tile.TriangleCount ||
            tile.TriC.Length != tile.TriangleCount ||
            tile.TriAreaIds.Length != tile.TriangleCount)
        {
            throw new InvalidDataException($"NavTile {tile.TileId} triangle arrays have mismatched lengths.");
        }

        for (int triangleIndex = 0; triangleIndex < tile.TriangleCount; triangleIndex++)
        {
            if ((uint)tile.TriA[triangleIndex] >= (uint)tile.VertexCount ||
                (uint)tile.TriB[triangleIndex] >= (uint)tile.VertexCount ||
                (uint)tile.TriC[triangleIndex] >= (uint)tile.VertexCount)
            {
                throw new InvalidDataException(
                    $"NavTile {tile.TileId} triangle {triangleIndex} contains an out-of-range vertex index.");
            }
        }
    }

    private static void RasterizeTriangle(
        byte[] rgba,
        int width,
        int height,
        WalkabilityTextureBounds bounds,
        long ax,
        long az,
        long bx,
        long bz,
        long cx,
        long cz,
        byte areaId)
    {
        long minWorldX = Math.Min(ax, Math.Min(bx, cx));
        long maxWorldX = Math.Max(ax, Math.Max(bx, cx));
        long minWorldZ = Math.Min(az, Math.Min(bz, cz));
        long maxWorldZ = Math.Max(az, Math.Max(bz, cz));
        int minPixelX = Math.Clamp(
            (int)Math.Floor((minWorldX - bounds.MinXcm) * (double)width / bounds.WidthCm),
            0,
            width - 1);
        int maxPixelX = Math.Clamp(
            (int)Math.Floor((maxWorldX - bounds.MinXcm) * (double)width / bounds.WidthCm),
            0,
            width - 1);
        int minPixelY = Math.Clamp(
            (int)Math.Floor((bounds.MaxZcm - maxWorldZ) * (double)height / bounds.HeightCm),
            0,
            height - 1);
        int maxPixelY = Math.Clamp(
            (int)Math.Floor((bounds.MaxZcm - minWorldZ) * (double)height / bounds.HeightCm),
            0,
            height - 1);
        WalkabilityAreaColor color = GetAreaColor(areaId);

        for (int y = minPixelY; y <= maxPixelY; y++)
        {
            double worldZ = bounds.MaxZcm - ((y + 0.5) * bounds.HeightCm / height);
            for (int x = minPixelX; x <= maxPixelX; x++)
            {
                double worldX = bounds.MinXcm + ((x + 0.5) * bounds.WidthCm / width);
                if (!ContainsPoint(worldX, worldZ, ax, az, bx, bz, cx, cz))
                {
                    continue;
                }

                int offset = ((y * width) + x) * 4;
                rgba[offset] = color.R;
                rgba[offset + 1] = color.G;
                rgba[offset + 2] = color.B;
                rgba[offset + 3] = byte.MaxValue;
            }
        }
    }

    private static bool ContainsPoint(
        double px,
        double pz,
        long ax,
        long az,
        long bx,
        long bz,
        long cx,
        long cz)
    {
        double e0 = Edge(ax, az, bx, bz, px, pz);
        double e1 = Edge(bx, bz, cx, cz, px, pz);
        double e2 = Edge(cx, cz, ax, az, px, pz);
        bool hasNegative = e0 < 0d || e1 < 0d || e2 < 0d;
        bool hasPositive = e0 > 0d || e1 > 0d || e2 > 0d;
        return !(hasNegative && hasPositive);
    }

    private static double Edge(long ax, long az, long bx, long bz, double px, double pz)
        => ((bx - ax) * (pz - az)) - ((bz - az) * (px - ax));
}

internal static class RgbaPngEncoder
{
    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (rgba == null) throw new ArgumentNullException(nameof(rgba));
        if (rgba.Length != checked(width * height * 4))
        {
            throw new InvalidOperationException("RGBA buffer size does not match width*height*4.");
        }

        int scanlineLength = checked(1 + (width * 4));
        var scanlines = new byte[checked(scanlineLength * height)];
        for (int y = 0; y < height; y++)
        {
            int destination = y * scanlineLength;
            scanlines[destination] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, scanlines, destination + 1, width * 4);
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(scanlines);
            }

            compressed = compressedStream.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, compressed);
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ComputeCrc32(type, data));
        stream.Write(crcBytes);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        for (int i = 0; i < type.Length; i++)
        {
            crc = UpdateCrc32(crc, type[i]);
        }

        for (int i = 0; i < data.Length; i++)
        {
            crc = UpdateCrc32(crc, data[i]);
        }

        return crc ^ uint.MaxValue;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
        {
            uint mask = (uint)-(int)(crc & 1);
            crc = (crc >> 1) ^ (0xEDB88320u & mask);
        }

        return crc;
    }
}
