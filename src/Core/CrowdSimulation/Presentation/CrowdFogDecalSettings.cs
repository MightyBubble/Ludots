using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 群体迷雾贴花的四档颜色。有单位就有迷雾场，渲染端画雾时只认这份合并结果。
/// </summary>
public static class CrowdFogDecalSettings
{
    public const string RelativePath = "Presentation/crowd_fog_decal.json";

    public static FogDecalPalette LoadMerged(
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

    public static FogDecalPalette Load(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        CrowdFogDecalColors? colors;
        try
        {
            colors = root.Deserialize<CrowdFogDecalColors>(StrictJsonOptions.CreateCamelCase());
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{RelativePath}: {ex.Message}", ex);
        }

        if (colors == null)
        {
            throw new InvalidOperationException($"{RelativePath}: 反序列化为空。");
        }

        return new FogDecalPalette(
            Color(colors.Unseen, "unseen"),
            Color(colors.Explored, "explored"),
            Color(colors.Visible, "visible"),
            Color(colors.Denied, "denied"));
    }

    private static FogDecalColor Color(int[]? rgba, string field)
    {
        if (rgba == null || rgba.Length != 4)
        {
            throw new InvalidOperationException($"{RelativePath}: {field} 需要 4 个 0..255 的整数。");
        }

        for (int i = 0; i < 4; i++)
        {
            if ((uint)rgba[i] > 255)
            {
                throw new InvalidOperationException($"{RelativePath}: {field}[{i}] 超出 0..255。");
            }
        }

        return new FogDecalColor((byte)rgba[0], (byte)rgba[1], (byte)rgba[2], (byte)rgba[3]);
    }

    private sealed class CrowdFogDecalColors
    {
        [JsonRequired] public int[] Unseen { get; set; } = Array.Empty<int>();
        [JsonRequired] public int[] Explored { get; set; } = Array.Empty<int>();
        [JsonRequired] public int[] Visible { get; set; } = Array.Empty<int>();
        [JsonRequired] public int[] Denied { get; set; } = Array.Empty<int>();
    }
}
