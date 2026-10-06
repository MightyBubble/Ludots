using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// 从地图实体提取 CrowdSimulation 地表输入：阻挡足迹（Box 正方形）与桥面跨度。
/// 模板给形状与尺寸，实例的 Overrides 给位置 / 跨度；装载契约
/// （CrowdSimulationAuthoringContract）已保证阻挡物是正方形 Box。
/// </summary>
public static class CrowdSimulationMapSurfaceSource
{
    public static CrowdSimulationMapSurface Extract(MapConfig map, IReadOnlyDictionary<string, EntityTemplate> templatesById)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(templatesById);

        var blockers = new List<BlockerFootprint>();
        var bridges = new List<CrowdSimulationBridgeSpan>();
        foreach (var entity in map.Entities)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.Template))
            {
                continue;
            }

            if (!templatesById.TryGetValue(entity.Template, out var template))
            {
                throw new InvalidOperationException($"Maps/{map.Id}.json: 实体 \"{entity.InstanceId}\" 引用未知模板 \"{entity.Template}\"。");
            }

            var obstacle = EffectiveObstacle(template, entity);
            if (obstacle is { SinkNavigationObstacle: true } blocker)
            {
                var (x, y) = EntityPosition(entity);
                blockers.Add(new BlockerFootprint(x, y, blocker.HalfWidthCm));
                continue;
            }

            var span = EffectiveBridgeSpan(template, entity);
            if (span != null)
            {
                bridges.Add(span.Value);
            }
        }

        return new CrowdSimulationMapSurface(blockers, bridges);
    }

    /// <summary>阻挡组件的有效视图（实例 Overrides 按字段覆盖模板）。</summary>
    internal static EffectiveObstacleView? EffectiveObstacle(EntityTemplate template, EntitySpawnData entity)
    {
        var node = template.Components.TryGetValue("ManifestationObstacleIntent2D", out var t) ? t : null;
        if (entity.Overrides != null && entity.Overrides.TryGetValue("ManifestationObstacleIntent2D", out var o) && o is JsonObject ovl)
        {
            var copy = node is JsonObject baseObj ? (JsonObject)baseObj.DeepClone() : new JsonObject();
            foreach (var kvp in ovl)
            {
                copy[kvp.Key] = kvp.Value?.DeepClone();
            }

            node = copy;
        }

        if (node is not JsonObject obj)
        {
            return null;
        }

        return new EffectiveObstacleView(
            Shape: obj["shape"]?.GetValue<string>() ?? "Box",
            SinkNavigationObstacle: obj["sinkNavigationObstacle"]?.GetValue<bool>() ?? false,
            HalfWidthCm: obj["halfWidthCm"]?.GetValue<int>() ?? 0,
            HalfHeightCm: obj["halfHeightCm"]?.GetValue<int>() ?? 0);
    }

    internal static CrowdSimulationBridgeSpan? EffectiveBridgeSpan(EntityTemplate template, EntitySpawnData entity)
    {
        bool hasSpan = template.Components.ContainsKey(nameof(CrowdSimulationBridgeSpan))
            || (entity.Overrides?.ContainsKey(nameof(CrowdSimulationBridgeSpan)) ?? false);
        if (!hasSpan)
        {
            return null;
        }

        JsonObject? merged = template.Components.TryGetValue(nameof(CrowdSimulationBridgeSpan), out var t) && t is JsonObject baseObj
            ? (JsonObject)baseObj.DeepClone()
            : new JsonObject();
        if (entity.Overrides != null && entity.Overrides.TryGetValue(nameof(CrowdSimulationBridgeSpan), out var o) && o is JsonObject ovl)
        {
            foreach (var kvp in ovl)
            {
                merged[kvp.Key] = kvp.Value?.DeepClone();
            }
        }

        return new CrowdSimulationBridgeSpan
        {
            X0Cm = merged["x0Cm"]?.GetValue<int>() ?? 0,
            Y0Cm = merged["y0Cm"]?.GetValue<int>() ?? 0,
            X1Cm = merged["x1Cm"]?.GetValue<int>() ?? 0,
            Y1Cm = merged["y1Cm"]?.GetValue<int>() ?? 0,
            WidthCm = merged["widthCm"]?.GetValue<int>() ?? 0,
        };
    }

    private static (int X, int Y) EntityPosition(EntitySpawnData entity)
    {
        var value = entity.Overrides?["WorldPositionCm"]?["Value"];
        if (value != null)
        {
            return (value["X"]?.GetValue<int>() ?? 0, value["Y"]?.GetValue<int>() ?? 0);
        }

        return (entity.PositionXCm ?? 0, entity.PositionYCm ?? 0);
    }

    internal readonly record struct EffectiveObstacleView(string Shape, bool SinkNavigationObstacle, int HalfWidthCm, int HalfHeightCm);
}

public sealed class CrowdSimulationMapSurface
{
    public CrowdSimulationMapSurface(IReadOnlyList<BlockerFootprint> blockers, IReadOnlyList<CrowdSimulationBridgeSpan> bridges)
    {
        Blockers = blockers;
        Bridges = bridges;
    }

    public IReadOnlyList<BlockerFootprint> Blockers { get; }
    public IReadOnlyList<CrowdSimulationBridgeSpan> Bridges { get; }
}
