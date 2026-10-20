using System;
using System.IO;
using System.Text.Json;

namespace Sango.Content.TerrainExport;

/// <summary>
/// TerrainTypes 字节 → VertexMap biome 半字节(0..15)的数据驱动投影。数据源
/// sango_terrain_weight_rules.json:terrainTypeWeights 逐行 biome 字段(0..32)+
/// 顶层 terrainTypeBiomeDefault 兜表外 id(33..255 的开放海域)。失败关闭:表行缺
/// biome、biome 越界、默认值缺失一律即抛,没有代码侧兜底映射。
/// </summary>
public sealed class SangoTerrainBiomeProjection
{
    public const string RulesFileName = "sango_terrain_weight_rules.json";

    private readonly int[] _biomeByTerrainType;
    private readonly byte _defaultBiome;

    private SangoTerrainBiomeProjection(byte defaultBiome)
    {
        _biomeByTerrainType = new int[256];
        Array.Fill(_biomeByTerrainType, -1);
        _defaultBiome = defaultBiome;
    }

    public byte DefaultBiome => _defaultBiome;

    public static SangoTerrainBiomeProjection Load(Stream jsonStream)
    {
        if (jsonStream == null) throw new ArgumentNullException(nameof(jsonStream));
        using JsonDocument document = JsonDocument.Parse(jsonStream);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("terrainTypeBiomeDefault", out JsonElement defaultElement) ||
            defaultElement.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException(
                $"{RulesFileName} lacks numeric terrainTypeBiomeDefault (out-of-table terrain ids have no fallback otherwise)");
        }

        if (!root.TryGetProperty("terrainTypeWeights", out JsonElement weights) ||
            weights.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{RulesFileName} lacks the terrainTypeWeights object");
        }

        var projection = new SangoTerrainBiomeProjection(ValidateBiome(defaultElement.GetByte()));
        foreach (JsonProperty entry in weights.EnumerateObject())
        {
            if (!byte.TryParse(entry.Name, out byte terrainType))
            {
                throw new InvalidDataException($"terrainTypeWeights key '{entry.Name}' is not a terrain byte");
            }

            if (!entry.Value.TryGetProperty("biome", out JsonElement biomeElement) ||
                biomeElement.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidDataException($"terrainTypeWeights['{entry.Name}'] lacks a numeric biome");
            }

            projection._biomeByTerrainType[terrainType] = ValidateBiome(biomeElement.GetByte());
        }

        return projection;
    }

    static byte ValidateBiome(byte biome)
    {
        return biome <= 15 ? biome : throw new InvalidDataException($"biome {biome} is outside the nibble range 0..15");
    }

    public byte Project(byte terrainType)
    {
        int biome = _biomeByTerrainType[terrainType];
        return biome >= 0 ? (byte)biome : _defaultBiome;
    }
}
