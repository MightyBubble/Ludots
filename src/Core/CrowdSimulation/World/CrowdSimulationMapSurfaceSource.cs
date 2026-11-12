using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// 从地图实体提取 CrowdSimulation 地表输入：方形阻挡足迹与条带足迹，各自带上实体点名的结构模板 id。
/// 模板给形状与尺寸，实例的 Overrides 按字段覆盖模板。字段缺了就报错，不补默认值。
/// </summary>
public static class CrowdSimulationMapSurfaceSource
{
    private const string ObstacleComponent = "ManifestationObstacleIntent2D";
    private const string PositionComponent = "WorldPositionCm";
    private const string BoxShape = "Box";

    public static CrowdSimulationMapSurface Extract(MapConfig map, IReadOnlyDictionary<string, EntityTemplate> templatesById)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(templatesById);

        var blockers = new List<BlockerFootprint>();
        var layeredPaths = new List<LayeredPathRecord>();
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

            string where = $"Maps/{map.Id}.json: 实体 \"{entity.InstanceId}\"（模板 \"{entity.Template}\"）";
            string? structure = EffectiveStructureTemplate(template, entity, where);
            var obstacle = EffectiveObstacle(template, entity, where);
            var span = EffectivePathSpan(template, entity, where);
            bool blocks = obstacle is { SinkNavigationObstacle: true };
            if (blocks && span != null)
            {
                throw new InvalidOperationException($"{where} 同时是导航阻挡和条带，只能二选一。");
            }

            if (blocks)
            {
                if (structure == null)
                {
                    throw new InvalidOperationException($"{where} 是导航阻挡，需要 CrowdSimulationStructure 点名结构模板。");
                }

                var view = obstacle!.Value;
                if (view.HalfWidthCm is not { } half || view.HalfHeightCm != half)
                {
                    throw new InvalidOperationException($"{where} 是导航阻挡，必须是 {BoxShape} 正方形（halfWidthCm = halfHeightCm）。");
                }

                var (x, y) = EntityPosition(template, entity, where);
                blockers.Add(new BlockerFootprint(x, y, half, structure));
                continue;
            }

            if (span != null)
            {
                if (structure == null)
                {
                    throw new InvalidOperationException($"{where} 带 CrowdSimulationPathSpan，需要 CrowdSimulationStructure 点名结构模板。");
                }

                layeredPaths.Add(new LayeredPathRecord(span.Value, structure));
                continue;
            }

            if (structure != null)
            {
                throw new InvalidOperationException(
                    $"{where} 点名了结构模板 \"{structure}\"，但既不是导航阻挡（sinkNavigationObstacle = true），也没有 CrowdSimulationPathSpan。");
            }
        }

        return new CrowdSimulationMapSurface(blockers, layeredPaths);
    }

    internal static string? EffectiveStructureTemplate(EntityTemplate template, EntitySpawnData entity, string where)
    {
        var merged = EffectiveComponent(template, entity, nameof(CrowdSimulationStructure));
        if (merged == null)
        {
            return null;
        }

        return RequireString(merged, "template", nameof(CrowdSimulationStructure), where);
    }

    /// <summary>阻挡组件的有效视图（实例 Overrides 按字段覆盖模板）。</summary>
    internal static EffectiveObstacleView? EffectiveObstacle(EntityTemplate template, EntitySpawnData entity, string where)
    {
        var obj = EffectiveComponent(template, entity, ObstacleComponent);
        if (obj == null)
        {
            return null;
        }

        string shape = RequireString(obj, "shape", ObstacleComponent, where);
        bool box = shape == BoxShape;
        return new EffectiveObstacleView(
            Shape: shape,
            SinkNavigationObstacle: RequireBool(obj, "sinkNavigationObstacle", ObstacleComponent, where),
            HalfWidthCm: box ? RequireInt(obj, "halfWidthCm", ObstacleComponent, where) : null,
            HalfHeightCm: box ? RequireInt(obj, "halfHeightCm", ObstacleComponent, where) : null);
    }

    internal static CrowdSimulationPathSpan? EffectivePathSpan(EntityTemplate template, EntitySpawnData entity, string where)
    {
        var merged = EffectiveComponent(template, entity, nameof(CrowdSimulationPathSpan));
        if (merged == null)
        {
            return null;
        }

        const string c = nameof(CrowdSimulationPathSpan);
        return new CrowdSimulationPathSpan
        {
            FromXCm = RequireInt(merged, "fromXCm", c, where),
            FromYCm = RequireInt(merged, "fromYCm", c, where),
            ToXCm = RequireInt(merged, "toXCm", c, where),
            ToYCm = RequireInt(merged, "toYCm", c, where),
            WidthCm = RequireInt(merged, "widthCm", c, where),
        };
    }

    /// <summary>与生成管线同序：实例 Overrides 盖模板；两边都没有 WorldPositionCm 时才用实例的 PositionXCm / PositionYCm。</summary>
    private static (int X, int Y) EntityPosition(EntityTemplate template, EntitySpawnData entity, string where)
    {
        var position = EffectiveComponent(template, entity, PositionComponent);
        if (position != null)
        {
            if (position["Value"] is not JsonObject value)
            {
                throw new InvalidOperationException($"{where} 的 {PositionComponent} 缺 Value。");
            }

            return (RequireInt(value, "X", PositionComponent, where), RequireInt(value, "Y", PositionComponent, where));
        }

        if (entity.PositionXCm is not { } x || entity.PositionYCm is not { } y)
        {
            throw new InvalidOperationException($"{where} 没有位置：模板和实例都没写 {PositionComponent}，实例也没写 PositionXCm / PositionYCm。");
        }

        return (x, y);
    }

    private static JsonObject? EffectiveComponent(EntityTemplate template, EntitySpawnData entity, string component)
    {
        var baseNode = template.Components.TryGetValue(component, out var t) ? t : null;
        JsonNode? overrideNode = null;
        entity.Overrides?.TryGetValue(component, out overrideNode);
        if (baseNode == null && overrideNode == null)
        {
            return null;
        }

        if (baseNode != null && baseNode is not JsonObject)
        {
            throw new InvalidOperationException($"Entities/templates.json: 模板 \"{template.Id}\" 的 {component} 必须是对象。");
        }

        if (overrideNode != null && overrideNode is not JsonObject)
        {
            throw new InvalidOperationException($"实体 \"{entity.InstanceId}\" 的 Overrides.{component} 必须是对象。");
        }

        var merged = baseNode is JsonObject b ? (JsonObject)b.DeepClone() : new JsonObject();
        if (overrideNode is JsonObject ovl)
        {
            foreach (var kvp in ovl)
            {
                merged[kvp.Key] = kvp.Value?.DeepClone();
            }
        }

        return merged;
    }

    private static string RequireString(JsonObject obj, string key, string component, string where) =>
        obj[key] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s)
            ? s
            : throw new InvalidOperationException($"{where} 的 {component}.{key} 必须是非空字符串。");

    private static int RequireInt(JsonObject obj, string key, string component, string where) =>
        obj[key] is JsonValue v && v.TryGetValue(out int i)
            ? i
            : throw new InvalidOperationException($"{where} 的 {component}.{key} 必须是整数。");

    private static bool RequireBool(JsonObject obj, string key, string component, string where) =>
        obj[key] is JsonValue v && v.TryGetValue(out bool b)
            ? b
            : throw new InvalidOperationException($"{where} 的 {component}.{key} 必须是 true 或 false。");

    /// <summary>半边长只在 Box 形状上读，其他形状为 null。</summary>
    internal readonly record struct EffectiveObstacleView(string Shape, bool SinkNavigationObstacle, int? HalfWidthCm, int? HalfHeightCm);
}

public sealed class CrowdSimulationMapSurface
{
    public CrowdSimulationMapSurface(IReadOnlyList<BlockerFootprint> blockers, IReadOnlyList<LayeredPathRecord> layeredPaths)
    {
        Blockers = blockers;
        LayeredPaths = layeredPaths;
    }

    public IReadOnlyList<BlockerFootprint> Blockers { get; }
    /// <summary>上层条带实体，按地图实体顺序。</summary>
    public IReadOnlyList<LayeredPathRecord> LayeredPaths { get; }
}

/// <summary>上层条带实体记录：几何跨度 + 实体点名的结构模板 id。</summary>
public readonly struct LayeredPathRecord
{
    public LayeredPathRecord(CrowdSimulationPathSpan span, string templateId)
    {
        Span = span;
        TemplateId = templateId;
    }

    public CrowdSimulationPathSpan Span { get; }
    public string TemplateId { get; }
}
