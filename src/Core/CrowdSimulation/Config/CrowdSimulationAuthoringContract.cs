using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Navigation.AgentProfiles;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 模板级装载契约（LU-19 中需要实体模板数据的部分；配置内部的检查在
/// <see cref="CrowdSimulationConfigValidator"/>）。进入地图时与配置装载一并执行。
/// </summary>
public static class CrowdSimulationAuthoringContract
{
    public static void Validate(
        IEnumerable<EntityTemplate> templates,
        CrowdSimulationConfig config,
        MapConfig map,
        AgentProfileRegistry profiles)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profiles);

        var byId = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
        foreach (var t in templates)
        {
            if (t?.Id != null) byId[t.Id] = t;
        }

        foreach (var t in byId.Values)
        {
            bool hasCrowd = t.Components.ContainsKey("CrowdSimulationAgent");
            bool hasMass = t.Components.ContainsKey("MassNavigationAgent");
            if (hasCrowd && hasMass)
            {
                throw new InvalidOperationException(
                    $"Entities/templates.json: 模板 \"{t.Id}\" 同时带 CrowdSimulationAgent 与 MassNavigationAgent；同一模板只能使用一种求解器。");
            }

            if (hasCrowd)
            {
                string profileId = t.Components["CrowdSimulationAgent"]?["profileId"]?.GetValue<string>()
                    ?? throw new InvalidOperationException($"Entities/templates.json: 模板 \"{t.Id}\" 的 CrowdSimulationAgent 缺 profileId。");
                if (!profiles.TryGet(profileId, out _))
                {
                    throw new InvalidOperationException(
                        $"Entities/templates.json: 模板 \"{t.Id}\" 的 CrowdSimulationAgent.profileId = \"{profileId}\" 不存在于 Navigation/agent_profiles.json。");
                }
            }
        }

        // 地图阻挡物：sinkNavigationObstacle 的实体只能 Box 且为正方形（实例 Overrides 覆盖模板字段）。
        foreach (var entity in map.Entities)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.Template))
            {
                continue;
            }

            if (!byId.TryGetValue(entity.Template, out var template))
            {
                throw new InvalidOperationException($"Maps/{config.MapId}.json: 实体 \"{entity.InstanceId}\" 引用未知模板 \"{entity.Template}\"。");
            }

            var obstacle = EffectiveObstacle(template, entity);
            if (obstacle == null)
            {
                continue;
            }

            (string shape, bool sink, int halfWidthCm, int halfHeightCm) = obstacle.Value;
            if (!sink)
            {
                continue;
            }

            if (!string.Equals(shape, "Box", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Maps/{config.MapId}.json: 阻挡物 \"{entity.InstanceId}\"（模板 \"{entity.Template}\"）的 shape = \"{shape}\"，阻挡物只能是 Box（不禁止圆形非阻挡区域实体）。");
            }

            if (halfWidthCm != halfHeightCm)
            {
                throw new InvalidOperationException(
                    $"Maps/{config.MapId}.json: 阻挡物 \"{entity.InstanceId}\"（模板 \"{entity.Template}\"）必须是正方形（halfWidthCm {halfWidthCm} ≠ halfHeightCm {halfHeightCm}）。");
            }
        }

        // 部署清单：模板必须存在且带 CrowdSimulationAgent。
        foreach (var templateId in config.Deploy.Templates)
        {
            if (!byId.TryGetValue(templateId, out var template))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: deploy.templates 引用未知实体模板 \"{templateId}\"。");
            }

            if (!template.Components.ContainsKey("CrowdSimulationAgent"))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: deploy.templates 的模板 \"{templateId}\" 必须带 CrowdSimulationAgent 组件。");
            }
        }
    }

    private static (string shape, bool sink, int halfWidthCm, int halfHeightCm)? EffectiveObstacle(EntityTemplate template, EntitySpawnData entity)
    {
        var node = template.Components.TryGetValue("ManifestationObstacleIntent2D", out var t) ? t : null;
        if (entity.Overrides != null && entity.Overrides.TryGetValue("ManifestationObstacleIntent2D", out var o) && o is JsonObject ovl)
        {
            var merged = node as JsonObject;
            var copy = merged != null ? (JsonObject)merged.DeepClone() : new JsonObject();
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

        string shape = obj["shape"]?.GetValue<string>() ?? "Box";
        bool sink = obj["sinkNavigationObstacle"]?.GetValue<bool>() ?? false;
        int halfWidthCm = obj["halfWidthCm"]?.GetValue<int>() ?? 0;
        int halfHeightCm = obj["halfHeightCm"]?.GetValue<int>() ?? 0;
        return (shape, sink, halfWidthCm, halfHeightCm);
    }
}
