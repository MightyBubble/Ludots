using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 单位模板上 CrowdSimulationAgent 的装载事实。速度、个人半径、推挤优先级以模板为准。
/// </summary>
public sealed class CrowdUnitTemplateFacts
{
    public required string TemplateId { get; init; }
    public required string ProfileId { get; init; }
    public required string AgentTypeId { get; init; }
    public required Fix64 SpeedCmPerSecond { get; init; }
    public required Fix64 PersonalRadiusCm { get; init; }
    public required Fix64 PushPriority { get; init; }
    public required bool BulkDeploy { get; init; }

    public static List<CrowdUnitTemplateFacts> FromTemplates(IEnumerable<EntityTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        var list = new List<CrowdUnitTemplateFacts>();
        foreach (var template in templates)
        {
            if (template?.Id == null || template.Components == null)
            {
                continue;
            }

            if (!template.Components.TryGetValue("CrowdSimulationAgent", out var node) || node is not JsonObject obj)
            {
                continue;
            }

            list.Add(Parse(template.Id, obj));
        }

        return list;
    }

    public static CrowdUnitTemplateFacts Parse(string templateId, JsonObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        RejectRetiredKeys(obj);
        string Prefix(string field) => $"Entities/templates.json: 模板 \"{templateId}\" 的 CrowdSimulationAgent.{field}";
        return new CrowdUnitTemplateFacts
        {
            TemplateId = templateId,
            ProfileId = RequireString(obj, "profileId", Prefix("profileId")),
            AgentTypeId = RequireString(obj, "agentTypeId", Prefix("agentTypeId")),
            SpeedCmPerSecond = RequirePositive(obj, "speedCmPerSecond", Prefix("speedCmPerSecond")),
            PersonalRadiusCm = RequirePositive(obj, "personalRadiusCm", Prefix("personalRadiusCm")),
            PushPriority = RequirePriority(obj, "pushPriority", Prefix("pushPriority")),
            BulkDeploy = RequireBool(obj, "bulkDeploy", Prefix("bulkDeploy")),
        };
    }

    public static void RejectRetiredKeys(JsonObject obj)
    {
        if (obj.ContainsKey("agentType"))
        {
            throw new InvalidOperationException("CrowdSimulationAgent 已删除 agentType，改用 agentTypeId。");
        }

        if (obj.ContainsKey("radiusClassCm") || obj.ContainsKey("radiusCm"))
        {
            throw new InvalidOperationException(
                "CrowdSimulationAgent 已删除 radiusClassCm / radiusCm。半径在 agent profile，模板写 profileId；个人半径写 personalRadiusCm。");
        }

        if (obj.ContainsKey("special"))
        {
            throw new InvalidOperationException("CrowdSimulationAgent 已删除 special，改用 bulkDeploy（true 参与批量部署）。");
        }
    }

    private static bool RequireBool(JsonObject obj, string name, string path)
    {
        if (obj[name] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
        {
            throw new InvalidOperationException($"{path} 需为布尔值。");
        }

        return flag;
    }

    private static string RequireString(JsonObject obj, string name, string path)
    {
        if (obj[name] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"{path} 需为非空字符串。");
        }

        return text;
    }

    private static Fix64 RequirePositive(JsonObject obj, string name, string path)
    {
        if (obj[name] is not JsonValue value || !value.TryGetValue<double>(out double number) || !double.IsFinite(number) || number <= 0)
        {
            throw new InvalidOperationException($"{path} 需为 > 0 的数值。");
        }

        return Fix64.FromDouble(number);
    }

    private static Fix64 RequirePriority(JsonObject obj, string name, string path)
    {
        if (obj[name] is not JsonValue value || !value.TryGetValue<double>(out double number) || !double.IsFinite(number) || number < 0)
        {
            throw new InvalidOperationException($"{path} 需为 ≥ 0 的数值。");
        }

        return Fix64.FromDouble(number);
    }
}
