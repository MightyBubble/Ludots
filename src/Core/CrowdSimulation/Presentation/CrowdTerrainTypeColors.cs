using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 按 terrainId 的地形类型颜色。游戏内着色和编辑器叠层读同一份合并结果。
/// </summary>
public static class CrowdTerrainTypeColors
{
    public const string RelativePath = "Presentation/terrain_type_colors.json";

    public static CrowdTerrainTypeColorTable LoadMerged(
        ConfigPipeline pipeline,
        ConfigCatalog catalog,
        ConfigConflictReport report)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(catalog, RelativePath, ConfigMergePolicy.DeepObject);
        JsonObject merged = pipeline.MergeDeepObjectFromCatalog(in entry, report);
        if (merged == null || merged.Count == 0)
        {
            throw new InvalidOperationException($"{RelativePath}: 存在目录登记但合并为空。");
        }

        return Load(merged);
    }

    public static CrowdTerrainTypeColorTable Load(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        CrowdTerrainTypeColorsFile? file;
        try
        {
            file = root.Deserialize<CrowdTerrainTypeColorsFile>(StrictJsonOptions.CreateCamelCase());
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{RelativePath}: {ex.Message}", ex);
        }

        if (file?.Colors == null || file.Colors.Count == 0)
        {
            throw new InvalidOperationException($"{RelativePath}: colors 为空。");
        }

        foreach (KeyValuePair<string, CrowdTerrainTypeColor> pair in file.Colors)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new InvalidOperationException($"{RelativePath}: colors 含空 terrainId。");
            }

            pair.Value.Validate($"{RelativePath}: colors.{pair.Key}");
        }

        return new CrowdTerrainTypeColorTable(file.Colors);
    }
}

public sealed class CrowdTerrainTypeColorTable
{
    public CrowdTerrainTypeColorTable(IReadOnlyDictionary<string, CrowdTerrainTypeColor> colors)
    {
        Colors = colors ?? throw new ArgumentNullException(nameof(colors));
    }

    public IReadOnlyDictionary<string, CrowdTerrainTypeColor> Colors { get; }

    public void RequireExact(IReadOnlyList<string> terrainIds, string mapId)
    {
        ArgumentNullException.ThrowIfNull(terrainIds);
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new ArgumentException("地图 id 不能为空。", nameof(mapId));
        }

        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < terrainIds.Count; i++)
        {
            string id = terrainIds[i];
            if (!expected.Add(id))
            {
                throw new InvalidOperationException(
                    $"{CrowdTerrainTypeColors.RelativePath}: 地图 \"{mapId}\" 的 terrainId \"{id}\" 重复。");
            }

            if (!Colors.ContainsKey(id))
            {
                throw new InvalidOperationException(
                    $"{CrowdTerrainTypeColors.RelativePath}: 地图 \"{mapId}\" 的 terrainId \"{id}\" 没有颜色。");
            }
        }

        foreach (string key in Colors.Keys)
        {
            if (!expected.Contains(key))
            {
                throw new InvalidOperationException(
                    $"{CrowdTerrainTypeColors.RelativePath}: 颜色 \"{key}\" 不在地图 \"{mapId}\" 的 terrainAreas 里。");
            }
        }
    }
}

public sealed class CrowdTerrainTypeColor
{
    [JsonRequired]
    public int Red { get; init; }

    [JsonRequired]
    public int Green { get; init; }

    [JsonRequired]
    public int Blue { get; init; }

    [JsonRequired]
    public int Alpha { get; init; }

    public void Validate(string path)
    {
        Channel(path, nameof(Red), Red);
        Channel(path, nameof(Green), Green);
        Channel(path, nameof(Blue), Blue);
        Channel(path, nameof(Alpha), Alpha);
    }

    private static void Channel(string path, string field, int value)
    {
        if (value < 0 || value > 255)
        {
            throw new InvalidOperationException($"{path}.{field} 需为 0..255 的整数。");
        }
    }
}

internal sealed class CrowdTerrainTypeColorsFile
{
    [JsonRequired]
    public Dictionary<string, CrowdTerrainTypeColor> Colors { get; init; } = null!;
}
