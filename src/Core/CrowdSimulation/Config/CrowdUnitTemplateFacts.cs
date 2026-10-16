using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>编辑器检视器用的字段说明。kind 是 string / number / bool；reference 指向可选值所在的表。</summary>
public readonly record struct CrowdUnitTemplateField(string Name, string Kind, string? Reference);

/// <summary>
/// 单位模板上 CrowdSimulationAgent 的装载事实。速度、个人半径、推挤优先级以模板为准。
/// </summary>
public sealed class CrowdUnitTemplateFacts
{
    public const string ComponentName = "CrowdSimulationAgent";
    public const string ProfileIdField = "profileId";
    public const string SpeedField = "speedCmPerSecond";
    public const string PersonalRadiusField = "personalRadiusCm";
    public const string PushPriorityField = "pushPriority";
    public const string SpecialField = "special";
    public const string AgentProfilesReference = "agentProfiles";

    public static readonly IReadOnlyList<CrowdUnitTemplateField> AuthoredFields =
    [
        new(ProfileIdField, "string", AgentProfilesReference),
        new(SpeedField, "number", null),
        new(PersonalRadiusField, "number", null),
        new(PushPriorityField, "number", null),
        new(SpecialField, "bool", null),
    ];

    public required string TemplateId { get; init; }
    public required string ProfileId { get; init; }
    public required Fix64 SpeedCmPerSecond { get; init; }
    public required Fix64 PersonalRadiusCm { get; init; }
    public required Fix64 PushPriority { get; init; }
    public required bool Special { get; init; }

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

            if (!template.Components.TryGetValue(ComponentName, out var node) || node is not JsonObject obj)
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
        RejectUnknownKeys(obj);
        string Prefix(string field) => $"Entities/templates.json: 模板 \"{templateId}\" 的 {ComponentName}.{field}";
        return new CrowdUnitTemplateFacts
        {
            TemplateId = templateId,
            ProfileId = RequireString(obj, ProfileIdField, Prefix(ProfileIdField)),
            SpeedCmPerSecond = RequirePositive(obj, SpeedField, Prefix(SpeedField)),
            PersonalRadiusCm = RequirePositive(obj, PersonalRadiusField, Prefix(PersonalRadiusField)),
            PushPriority = RequirePriority(obj, PushPriorityField, Prefix(PushPriorityField)),
            Special = RequireBool(obj, SpecialField, Prefix(SpecialField)),
        };
    }

    public static void RejectRetiredKeys(JsonObject obj)
    {
        if (obj.ContainsKey("radiusClassCm") || obj.ContainsKey("radiusCm"))
        {
            throw new InvalidOperationException(
                "CrowdSimulationAgent 已删除 radiusClassCm / radiusCm。半径在 agent profile，模板写 profileId；个人半径写 personalRadiusCm。");
        }

    }

    public static void RejectUnknownKeys(JsonObject obj)
    {
        foreach (var (key, _) in obj)
        {
            bool known = false;
            for (int i = 0; i < AuthoredFields.Count; i++)
            {
                if (string.Equals(AuthoredFields[i].Name, key, StringComparison.Ordinal))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                throw new InvalidOperationException($"{ComponentName} contains unsupported property '{key}'.");
            }
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
