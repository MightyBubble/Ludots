using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Tool;

public static class TerrainControlMapBaker
{
    public const int LayerCount = 4;
    public const int DefaultOutputColumns = 2048;
    public const int DefaultOutputRows = 1170;
    public const string DefaultRulesRelativePath = "assets/terrain/east_asia_weight_rules.json";
    public const string DefaultVisualHeightmapRelativePath = "assets/terrain/east_asia_continuous.vhtm";
    public const string DefaultManifestRelativePath = "assets/terrain/east_asia_terrain_profile.json";
    public const string ManifestOutputKey = "terrainControlWeights";

    private static readonly string[] LayerNames = { "sand", "grass", "dirt", "rock" };
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static TerrainControlMapBakeSummary Bake(
        string modRoot,
        bool overwrite,
        string? rulesPath = null,
        string? visualHeightmapPath = null,
        string? outputPath = null,
        string? manifestPath = null,
        bool registerInManifest = true)
    {
        if (string.IsNullOrWhiteSpace(modRoot)) throw new ArgumentException("Mod root is required.", nameof(modRoot));
        string fullModRoot = Path.GetFullPath(modRoot);
        string fullRulesPath = Path.GetFullPath(rulesPath ?? Path.Combine(fullModRoot, DefaultRulesRelativePath));
        string fullVisualPath = Path.GetFullPath(visualHeightmapPath ?? Path.Combine(fullModRoot, DefaultVisualHeightmapRelativePath));
        string fullManifestPath = Path.GetFullPath(manifestPath ?? Path.Combine(fullModRoot, DefaultManifestRelativePath));

        TerrainWeightRules rules = TerrainWeightRules.Load(fullRulesPath);
        string fullOutputPath = Path.GetFullPath(outputPath ?? Path.Combine(fullModRoot, rules.OutputFile));
        if (File.Exists(fullOutputPath) && !overwrite)
        {
            throw new IOException($"File exists: {fullOutputPath}");
        }

        VisualHeightmapAsset heightmap;
        using (FileStream stream = File.OpenRead(fullVisualPath))
        {
            heightmap = VisualHeightmapBinary.Read(stream);
        }
        if (heightmap.UsesRawUInt16Samples)
        {
            throw new InvalidDataException($"'{fullVisualPath}' uses scaled UInt16 samples; the baker requires centimeter Int16 samples.");
        }

        byte[] rgba = BakeRgba8(
            rules,
            heightmap.HeightSamplesCm,
            heightmap.SampleColumns,
            heightmap.SampleRows,
            heightmap.Bounds.Width,
            heightmap.Bounds.Height,
            rules.OutputColumns,
            rules.OutputRows);

        string? directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using (FileStream output = File.Create(fullOutputPath))
        {
            WriteRgba8Png(output, rules.OutputColumns, rules.OutputRows, rgba);
        }

        string sha256 = ComputeSha256(fullOutputPath);
        if (registerInManifest)
        {
            string relativeOutput = Path.GetRelativePath(fullModRoot, fullOutputPath).Replace('\\', '/');
            RegisterOutput(fullManifestPath, relativeOutput, sha256);
        }

        return new TerrainControlMapBakeSummary(
            fullRulesPath,
            fullVisualPath,
            fullOutputPath,
            registerInManifest ? fullManifestPath : null,
            rules.OutputColumns,
            rules.OutputRows,
            sha256);
    }

    // Channel order is the shader contract: R=sand, G=grass, B=dirt, A=rock.
    public static byte[] BakeRgba8(
        TerrainWeightRules rules,
        short[] heightsCm,
        int sourceColumns,
        int sourceRows,
        int worldWidthCm,
        int worldHeightCm,
        int outputColumns,
        int outputRows)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        if (heightsCm == null) throw new ArgumentNullException(nameof(heightsCm));
        if (sourceColumns < 2 || sourceRows < 2) throw new ArgumentOutOfRangeException(nameof(sourceColumns));
        if (outputColumns < 2 || outputRows < 2) throw new ArgumentOutOfRangeException(nameof(outputColumns));
        if (worldWidthCm <= 0 || worldHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(worldWidthCm));
        if (heightsCm.Length != checked(sourceColumns * sourceRows))
        {
            throw new ArgumentException("Height sample count does not match sourceColumns*sourceRows.", nameof(heightsCm));
        }

        int pixelCount = checked(outputColumns * outputRows);
        var heights = new float[pixelCount];
        for (int row = 0; row < outputRows; row++)
        {
            double sourceY = row * (sourceRows - 1) / (double)(outputRows - 1);
            for (int col = 0; col < outputColumns; col++)
            {
                double sourceX = col * (sourceColumns - 1) / (double)(outputColumns - 1);
                heights[(row * outputColumns) + col] = SampleBilinear(heightsCm, sourceColumns, sourceRows, sourceX, sourceY);
            }
        }

        float stepXcm = worldWidthCm / (float)(outputColumns - 1);
        float stepYcm = worldHeightCm / (float)(outputRows - 1);
        var weights = new float[LayerCount];
        var rgba = new byte[pixelCount * LayerCount];
        for (int row = 0; row < outputRows; row++)
        {
            for (int col = 0; col < outputColumns; col++)
            {
                int index = (row * outputColumns) + col;
                float heightCm = heights[index] - rules.SeaLevelCm;
                float slope = EstimateSlope01(heights, outputColumns, outputRows, col, row, stepXcm, stepYcm);
                float sum = 0f;
                for (int layer = 0; layer < LayerCount; layer++)
                {
                    TerrainWeightLayerRule layerRule = rules.Layers[layer];
                    float band = SmoothRange(heightCm, layerRule.RiseStartCm, layerRule.RiseEndCm, layerRule.FallStartCm, layerRule.FallEndCm);
                    float slopeScale = Math.Clamp(1f - (slope * layerRule.SlopeFactor), 0f, 4f);
                    float noise = ValueNoise(col * layerRule.NoiseFrequency, row * layerRule.NoiseFrequency);
                    float noiseScale = 1f + (layerRule.NoiseAmplitude * ((noise * 2f) - 1f));
                    float weight = band * slopeScale * noiseScale;
                    weights[layer] = weight;
                    sum += weight;
                }

                if (sum < 1e-5f)
                {
                    throw new InvalidDataException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Weight rules '{rules.Id}' leave pixel ({col}, {row}) at {heightCm}cm with no layer coverage; bands must cover every reachable height."));
                }

                int offset = index * LayerCount;
                for (int layer = 0; layer < LayerCount; layer++)
                {
                    rgba[offset + layer] = ToByte01(weights[layer] / sum);
                }
            }
        }

        return rgba;
    }

    public static void WriteRgba8Png(Stream stream, int width, int height, byte[] rgba)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (rgba.Length != checked(width * height * 4))
        {
            throw new InvalidOperationException("RGBA buffer size does not match width*height*4.");
        }

        int rawStride = 1 + (width * 4);
        var raw = new byte[rawStride * height];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * rawStride;
            raw[rowStart] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, rowStart + 1, width * 4);
        }

        byte[] compressed = CompressZlib(raw);
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        stream.Write(signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(stream, "IHDR"u8, ihdr);
        WriteChunk(stream, "IDAT"u8, compressed);
        WriteChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void RegisterOutput(string manifestPath, string relativeOutputFile, string sha256)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Terrain profile manifest is required for output registration.", manifestPath);
        }

        JsonNode? parsed = JsonNode.Parse(File.ReadAllText(manifestPath));
        if (parsed is not JsonObject root)
        {
            throw new InvalidDataException($"'{manifestPath}' must contain a JSON object at the root.");
        }
        if (root["outputs"] is not JsonObject outputs)
        {
            throw new InvalidDataException($"'{manifestPath}' must contain an 'outputs' object.");
        }

        outputs[ManifestOutputKey] = new JsonObject
        {
            ["file"] = relativeOutputFile,
            ["sha256"] = sha256
        };
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Utf8NoBom);
    }

    private static float SampleBilinear(short[] heights, int columns, int rows, double x, double y)
    {
        x = Math.Clamp(x, 0, columns - 1);
        y = Math.Clamp(y, 0, rows - 1);
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int x1 = Math.Min(x0 + 1, columns - 1);
        int y1 = Math.Min(y0 + 1, rows - 1);
        double fx = x - x0;
        double fy = y - y0;
        double top = heights[(y0 * columns) + x0] + ((heights[(y0 * columns) + x1] - heights[(y0 * columns) + x0]) * fx);
        double bottom = heights[(y1 * columns) + x0] + ((heights[(y1 * columns) + x1] - heights[(y1 * columns) + x0]) * fx);
        return (float)(top + ((bottom - top) * fy));
    }

    private static float EstimateSlope01(float[] heights, int columns, int rows, int col, int row, float stepXcm, float stepYcm)
    {
        int left = Math.Max(0, col - 1);
        int right = Math.Min(columns - 1, col + 1);
        int top = Math.Max(0, row - 1);
        int bottom = Math.Min(rows - 1, row + 1);
        float hLeft = heights[(row * columns) + left];
        float hRight = heights[(row * columns) + right];
        float hTop = heights[(top * columns) + col];
        float hBottom = heights[(bottom * columns) + col];
        float dx = MathF.Max(1f, (right - left) * stepXcm);
        float dz = MathF.Max(1f, (bottom - top) * stepYcm);
        float nx = -(hRight - hLeft) / dx;
        float nz = -(hBottom - hTop) / dz;
        float len = MathF.Sqrt((nx * nx) + 1f + (nz * nz));
        float normalY = len > 1e-5f ? 1f / len : 1f;
        return Math.Clamp(1f - normalY, 0f, 1f);
    }

    private static float SmoothRange(float x, float in0, float in1, float out0, float out1)
    {
        float rise = SmoothStep(in0, in1, x);
        float fall = 1f - SmoothStep(out0, out1, x);
        return Math.Clamp(rise * fall, 0f, 1f);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / MathF.Max(edge1 - edge0, 1e-5f), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float ValueNoise(float x, float y)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        float v00 = Hash01(x0, y0);
        float v10 = Hash01(x0 + 1, y0);
        float v01 = Hash01(x0, y0 + 1);
        float v11 = Hash01(x0 + 1, y0 + 1);
        float ix0 = v00 + ((v10 - v00) * Smooth(fx));
        float ix1 = v01 + ((v11 - v01) * Smooth(fx));
        return ix0 + ((ix1 - ix0) * Smooth(fy));
    }

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    private static float Hash01(int x, int y)
    {
        int n = (x * 374761393) + (y * 668265263);
        n = (n ^ (n >> 13)) * 1274126177;
        n ^= n >> 16;
        return (n & 0x7FFFFFFF) / (float)int.MaxValue;
    }

    private static byte ToByte01(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static byte[] CompressZlib(byte[] raw)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x01);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        uint adler = Adler32(raw);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, adler);
        output.Write(checksum);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);

        uint crc = Crc32(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint ModAdler = 65521;
        uint a = 1;
        uint b = 0;
        for (int i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % ModAdler;
            b = (b + a) % ModAdler;
        }

        return (b << 16) | a;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < type.Length; i++)
        {
            crc = Crc32Update(crc, type[i]);
        }

        for (int i = 0; i < data.Length; i++)
        {
            crc = Crc32Update(crc, data[i]);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint Crc32Update(uint crc, byte value)
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

public sealed class TerrainWeightRules
{
    public const int MinOutputDimension = 2;
    public const int MaxOutputDimension = 8192;
    public const float MaxSlopeFactorMagnitude = 8f;
    public const float MaxNoiseFrequency = 16f;

    public TerrainWeightRules(
        string id,
        int seaLevelCm,
        string outputFile,
        int outputColumns,
        int outputRows,
        TerrainWeightLayerRule[] layers)
    {
        Id = id;
        SeaLevelCm = seaLevelCm;
        OutputFile = outputFile;
        OutputColumns = outputColumns;
        OutputRows = outputRows;
        Layers = layers;
    }

    public string Id { get; }

    public int SeaLevelCm { get; }

    public string OutputFile { get; }

    public int OutputColumns { get; }

    public int OutputRows { get; }

    public TerrainWeightLayerRule[] Layers { get; }

    public static TerrainWeightRules Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Terrain weight rules file is required.", path);
        }

        return Parse(File.ReadAllText(path));
    }

    public static TerrainWeightRules Parse(string json)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Terrain weight rules must be valid JSON. {exception.Message}");
        }
        if (parsed is not JsonObject root)
        {
            throw new InvalidOperationException("Terrain weight rules must be a JSON object.");
        }

        string id = ReadRequiredString(root["id"], "Terrain weight rules field 'id'");
        int seaLevelCm = ReadInteger(root["seaLevelCm"], "Terrain weight rules field 'seaLevelCm'", -30000, 30000);

        if (root["output"] is not JsonObject output)
        {
            throw new InvalidOperationException($"Terrain weight rules '{id}' must declare an 'output' object.");
        }

        string outputFile = ReadRequiredString(output["file"], $"Terrain weight rules '{id}' field 'output.file'");
        if (Path.IsPathRooted(outputFile))
        {
            throw new InvalidOperationException($"Terrain weight rules '{id}' field 'output.file' must be relative to the mod root.");
        }

        int outputColumns = ReadInteger(output["columns"], $"Terrain weight rules '{id}' field 'output.columns'", MinOutputDimension, MaxOutputDimension);
        int outputRows = ReadInteger(output["rows"], $"Terrain weight rules '{id}' field 'output.rows'", MinOutputDimension, MaxOutputDimension);

        if (root["layers"] is not JsonObject layersNode)
        {
            throw new InvalidOperationException($"Terrain weight rules '{id}' must declare a 'layers' object.");
        }

        var layers = new TerrainWeightLayerRule[TerrainControlMapBaker.LayerCount];
        for (int i = 0; i < TerrainControlMapBaker.LayerCount; i++)
        {
            string layerName = LayerName(i);
            if (layersNode[layerName] is not JsonObject layerNode)
            {
                throw new InvalidOperationException($"Terrain weight rules '{id}' layers must declare '{layerName}'.");
            }

            layers[i] = ParseLayer(id, layerName, layerNode);
        }

        foreach (string declaredName in layersNode.Select(pair => pair.Key))
        {
            bool known = false;
            for (int i = 0; i < TerrainControlMapBaker.LayerCount; i++)
            {
                known |= string.Equals(declaredName, LayerName(i), StringComparison.Ordinal);
            }

            if (!known)
            {
                throw new InvalidOperationException($"Terrain weight rules '{id}' declares unknown layer '{declaredName}'.");
            }
        }

        return new TerrainWeightRules(id, seaLevelCm, outputFile, outputColumns, outputRows, layers);
    }

    public static string LayerName(int channel) => channel switch
    {
        0 => "sand",
        1 => "grass",
        2 => "dirt",
        3 => "rock",
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    private static TerrainWeightLayerRule ParseLayer(string rulesId, string layerName, JsonObject layerNode)
    {
        string label = $"Terrain weight rules '{rulesId}' layer '{layerName}'";
        if (layerNode["bandCm"] is not JsonObject band)
        {
            throw new InvalidOperationException($"{label} must declare a 'bandCm' object.");
        }

        int riseStart = ReadInteger(band["riseStart"], $"{label} field 'bandCm.riseStart'", -100000, 1000000);
        int riseEnd = ReadInteger(band["riseEnd"], $"{label} field 'bandCm.riseEnd'", -100000, 1000000);
        int fallStart = ReadInteger(band["fallStart"], $"{label} field 'bandCm.fallStart'", -100000, 1000000);
        int fallEnd = ReadInteger(band["fallEnd"], $"{label} field 'bandCm.fallEnd'", -100000, 1000000);
        if (!(riseStart < riseEnd && riseEnd < fallStart && fallStart < fallEnd))
        {
            throw new InvalidOperationException(
                $"{label} bandCm must satisfy riseStart < riseEnd < fallStart < fallEnd. Actual: {riseStart}/{riseEnd}/{fallStart}/{fallEnd}.");
        }

        float slopeFactor = ReadNumber(layerNode["slopeFactor"], $"{label} field 'slopeFactor'", -MaxSlopeFactorMagnitude, MaxSlopeFactorMagnitude);
        float noiseFrequency = ReadNumber(layerNode["noiseFrequency"], $"{label} field 'noiseFrequency'", 0f, MaxNoiseFrequency);
        float noiseAmplitude = ReadNumber(layerNode["noiseAmplitude"], $"{label} field 'noiseAmplitude'", 0f, 1f);
        return new TerrainWeightLayerRule(riseStart, riseEnd, fallStart, fallEnd, slopeFactor, noiseFrequency, noiseAmplitude);
    }

    private static string ReadRequiredString(JsonNode? node, string label)
    {
        string value = node?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} must be a non-empty string.");
        }
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label} must not include leading or trailing whitespace.");
        }

        return value;
    }

    private static int ReadInteger(JsonNode? node, string label, int min, int max)
    {
        if (node?.GetValueKind() != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"{label} must be a number.");
        }

        double parsed = node.GetValue<double>();
        if (parsed != Math.Floor(parsed) || parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"{label} must be an integer within [{min}, {max}]. Actual: {parsed}.");
        }

        return (int)parsed;
    }

    private static float ReadNumber(JsonNode? node, string label, float min, float max)
    {
        if (node?.GetValueKind() != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"{label} must be a number.");
        }

        float parsed = (float)node.GetValue<double>();
        if (!float.IsFinite(parsed) || parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"{label} must be within [{min}, {max}]. Actual: {parsed}.");
        }

        return parsed;
    }
}

public sealed class TerrainWeightLayerRule
{
    public TerrainWeightLayerRule(
        int riseStartCm,
        int riseEndCm,
        int fallStartCm,
        int fallEndCm,
        float slopeFactor,
        float noiseFrequency,
        float noiseAmplitude)
    {
        RiseStartCm = riseStartCm;
        RiseEndCm = riseEndCm;
        FallStartCm = fallStartCm;
        FallEndCm = fallEndCm;
        SlopeFactor = slopeFactor;
        NoiseFrequency = noiseFrequency;
        NoiseAmplitude = noiseAmplitude;
    }

    public int RiseStartCm { get; }

    public int RiseEndCm { get; }

    public int FallStartCm { get; }

    public int FallEndCm { get; }

    public float SlopeFactor { get; }

    public float NoiseFrequency { get; }

    public float NoiseAmplitude { get; }
}

public readonly record struct TerrainControlMapBakeSummary(
    string RulesPath,
    string VisualHeightmapPath,
    string OutputPath,
    string? ManifestPath,
    int OutputColumns,
    int OutputRows,
    string OutputSha256);
