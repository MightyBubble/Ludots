using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 地形类型叠层的开关、动作和阻挡色。三张 S1 种子和 S4 读同一份，缺文件或缺键在进图时失败。
/// </summary>
public sealed class CrowdTerrainTypeOverlaySettings
{
    public const string RelativePath = "Presentation/crowd_terrain_type_overlay.json";

    private CrowdTerrainTypeOverlaySettings(bool enabled, string action, CrowdTerrainTypeColor blocked)
    {
        Enabled = enabled;
        Action = action;
        Blocked = blocked;
    }

    public bool Enabled { get; }

    public string Action { get; }

    public CrowdTerrainTypeColor Blocked { get; }

    public static CrowdTerrainTypeOverlaySettings LoadMerged(
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

    public static CrowdTerrainTypeOverlaySettings Load(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        CrowdTerrainTypeOverlayFile? file;
        try
        {
            file = root.Deserialize<CrowdTerrainTypeOverlayFile>(StrictJsonOptions.CreateCamelCase());
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{RelativePath}: {ex.Message}", ex);
        }

        if (file == null)
        {
            throw new InvalidOperationException($"{RelativePath}: 合并为空。");
        }

        if (string.IsNullOrWhiteSpace(file.Action))
        {
            throw new InvalidOperationException($"{RelativePath}: action 不能为空。");
        }

        if (file.Blocked == null)
        {
            throw new InvalidOperationException($"{RelativePath}: blocked 不能为空。");
        }

        file.Blocked.Validate($"{RelativePath}: blocked");
        return new CrowdTerrainTypeOverlaySettings(file.Enabled, file.Action, file.Blocked);
    }
}

internal sealed class CrowdTerrainTypeOverlayFile
{
    [JsonRequired]
    public bool Enabled { get; init; }

    [JsonRequired]
    public string Action { get; init; } = "";

    [JsonRequired]
    public CrowdTerrainTypeColor Blocked { get; init; } = null!;
}
