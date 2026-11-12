using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>玩家视角与调试视图共用的基础色（plan、ink、paper 等）。</summary>
internal static class CrowdPresentationPalette
{
    public const string RelativePath = "CrowdSimulationPresentationPalette.json";

    public static JsonObject LoadMerged(
        ConfigPipeline pipeline,
        ConfigCatalog catalog,
        ConfigConflictReport report)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
            throw new InvalidOperationException($"{RelativePath}: 未在配置目录登记。");
        JsonObject? merged = pipeline.MergeDeepObjectFromCatalog(in entry, report);
        if (merged == null || merged.Count == 0)
            throw new InvalidOperationException($"{RelativePath}: 存在目录登记但合并为空。");
        return merged;
    }

    public static JsonObject MergeInto(JsonObject viewRoot, JsonObject palette, string viewPath)
    {
        ArgumentNullException.ThrowIfNull(viewRoot);
        ArgumentNullException.ThrowIfNull(palette);
        if (viewRoot["colors"] is not JsonObject colors)
            throw new InvalidOperationException($"{viewPath}: colors 必填。");
        foreach (var pair in palette)
        {
            if (colors.ContainsKey(pair.Key))
            {
                throw new InvalidOperationException(
                    $"{viewPath}: colors.{pair.Key} 与 {RelativePath} 重复，共用色只写在 {RelativePath}。");
            }

            colors[pair.Key] = pair.Value?.DeepClone();
        }

        return viewRoot;
    }
}
