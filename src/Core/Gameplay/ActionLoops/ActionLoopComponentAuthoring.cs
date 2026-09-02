using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Teams;
using AuthoringRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Core.Gameplay.ActionLoops;

internal static class ActionLoopComponentAuthoring
{
    public static void Register()
    {
        AuthoringRegistry.Register<ResourceTransportProfile>(
            nameof(ResourceTransportProfile),
            SetResourceTransportProfile);
        AuthoringRegistry.Register<ResourceSourceProfile>(
            nameof(ResourceSourceProfile),
            SetResourceSourceProfile);
        AuthoringRegistry.Register<ResourceSinkProfile>(
            nameof(ResourceSinkProfile),
            SetResourceSinkProfile);
        AuthoringRegistry.Register<ResourceTransportState>(nameof(ResourceTransportState));
        AuthoringRegistry.Register<DirectAttackProfile>(nameof(DirectAttackProfile), SetDirectAttackProfile);
        AuthoringRegistry.Register<DirectAttackState>(nameof(DirectAttackState));
    }

    private static void SetResourceTransportProfile(Entity entity, JsonNode data)
    {
        const string context = nameof(ResourceTransportProfile);
        JsonObject obj = RequireObject(data, context);
        ValidateProperties(
            obj,
            context,
            "GatherOrderTypeId",
            "MoveOrderTypeId",
            "ResourceAttribute",
            "CargoAmount",
            "LoadDurationTicks",
            "ArrivalRadiusCm");
        var profile = new ResourceTransportProfile
        {
            GatherOrderTypeId = ReadPositiveInt(obj, "GatherOrderTypeId", context),
            MoveOrderTypeId = ReadPositiveInt(obj, "MoveOrderTypeId", context),
            ResourceAttributeId = RegisterAttribute(obj, context),
            CargoAmount = ReadPositiveFloat(obj, "CargoAmount", context),
            LoadDurationTicks = ReadPositiveInt(obj, "LoadDurationTicks", context),
            ArrivalRadiusCm = ReadPositiveInt(obj, "ArrivalRadiusCm", context),
        };
        ValidateOrderTypeId(profile.GatherOrderTypeId, "GatherOrderTypeId", context);
        ValidateOrderTypeId(profile.MoveOrderTypeId, "MoveOrderTypeId", context);
        entity.Add(profile);
    }

    private static void SetResourceSourceProfile(Entity entity, JsonNode data)
    {
        entity.Add(new ResourceSourceProfile
        {
            ResourceAttributeId = ReadResourceAttributeOnly(data, nameof(ResourceSourceProfile)),
        });
    }

    private static void SetResourceSinkProfile(Entity entity, JsonNode data)
    {
        const string context = nameof(ResourceSinkProfile);
        JsonObject obj = RequireObject(data, context);
        ValidateProperties(obj, context, "ResourceAttribute", "DockOffsetXCm", "DockOffsetYCm");
        entity.Add(new ResourceSinkProfile
        {
            ResourceAttributeId = RegisterAttribute(obj, context),
            DockOffsetXCm = ReadInt(obj, "DockOffsetXCm", context),
            DockOffsetYCm = ReadInt(obj, "DockOffsetYCm", context),
        });
    }

    private static void SetDirectAttackProfile(Entity entity, JsonNode data)
    {
        const string context = nameof(DirectAttackProfile);
        JsonObject obj = RequireObject(data, context);
        ValidateProperties(
            obj,
            context,
            "AttackOrderTypeId",
            "MoveOrderTypeId",
            "EffectTemplate",
            "TargetRelation",
            "RangeCm",
            "CooldownTicks",
            "EngagementStandoffRadiusCm");
        int attackOrderTypeId = ReadPositiveInt(obj, "AttackOrderTypeId", context);
        int moveOrderTypeId = ReadPositiveInt(obj, "MoveOrderTypeId", context);
        ValidateOrderTypeId(attackOrderTypeId, "AttackOrderTypeId", context);
        ValidateOrderTypeId(moveOrderTypeId, "MoveOrderTypeId", context);
        string effectTemplate = ReadRequiredString(obj, "EffectTemplate", context);
        int effectTemplateId = EffectTemplateIdRegistry.GetId(effectTemplate);
        if (effectTemplateId == EffectTemplateIdRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                $"{context}.EffectTemplate references unknown effect '{effectTemplate}'.");
        }

        int rangeCm = ReadPositiveInt(obj, "RangeCm", context);
        int standoffRadiusCm = ReadInt(obj, "EngagementStandoffRadiusCm", context);
        int maximumStandoffRadiusCm = Math.Max(0, rangeCm - DirectAttackProfile.PursuitArrivalSlackCm);
        if ((uint)standoffRadiusCm > (uint)maximumStandoffRadiusCm)
        {
            throw new InvalidOperationException(
                $"{context}.EngagementStandoffRadiusCm must be between 0 and {maximumStandoffRadiusCm} for RangeCm {rangeCm}.");
        }

        entity.Add(new DirectAttackProfile
        {
            AttackOrderTypeId = attackOrderTypeId,
            MoveOrderTypeId = moveOrderTypeId,
            EffectTemplateId = effectTemplateId,
            TargetRelation = RelationshipFilterUtil.Parse(ReadRequiredString(obj, "TargetRelation", context)),
            RangeCm = rangeCm,
            CooldownTicks = ReadPositiveInt(obj, "CooldownTicks", context),
            EngagementStandoffRadiusCm = standoffRadiusCm,
        });
    }

    private static int ReadResourceAttributeOnly(JsonNode data, string context)
    {
        JsonObject obj = RequireObject(data, context);
        ValidateProperties(obj, context, "ResourceAttribute");
        return RegisterAttribute(obj, context);
    }

    private static int RegisterAttribute(JsonObject obj, string context)
    {
        return AttributeRegistry.Register(ReadRequiredString(obj, "ResourceAttribute", context));
    }

    private static JsonObject RequireObject(JsonNode data, string context)
    {
        return data as JsonObject
            ?? throw new InvalidOperationException($"{context} authoring must be an object.");
    }

    private static void ValidateProperties(JsonObject obj, string context, params string[] allowed)
    {
        foreach ((string name, _) in obj)
        {
            bool found = false;
            for (int i = 0; i < allowed.Length; i++)
            {
                if (string.Equals(name, allowed[i], StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException($"{context} contains unknown property '{name}'.");
            }
        }
    }

    private static int ReadPositiveInt(JsonObject obj, string name, string context)
    {
        JsonNode node = RequireProperty(obj, name, context);
        if (node.GetValueKind() != JsonValueKind.Number ||
            !node.AsValue().TryGetValue(out int value) ||
            value <= 0)
        {
            throw new InvalidOperationException($"{context}.{name} must be a positive integer.");
        }

        return value;
    }

    private static int ReadInt(JsonObject obj, string name, string context)
    {
        JsonNode node = RequireProperty(obj, name, context);
        if (node.GetValueKind() != JsonValueKind.Number ||
            !node.AsValue().TryGetValue(out int value))
        {
            throw new InvalidOperationException($"{context}.{name} must be an integer.");
        }

        return value;
    }

    private static float ReadPositiveFloat(JsonObject obj, string name, string context)
    {
        JsonNode node = RequireProperty(obj, name, context);
        if (node.GetValueKind() != JsonValueKind.Number ||
            !node.AsValue().TryGetValue(out float value) ||
            !float.IsFinite(value) ||
            value <= 0f)
        {
            throw new InvalidOperationException($"{context}.{name} must be a positive finite number.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonObject obj, string name, string context)
    {
        JsonNode node = RequireProperty(obj, name, context);
        if (node.GetValueKind() != JsonValueKind.String)
        {
            throw new InvalidOperationException($"{context}.{name} must be a string.");
        }

        string value = node.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{context}.{name} must be a canonical non-empty string.");
        }

        return value;
    }

    private static JsonNode RequireProperty(JsonObject obj, string name, string context)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? node) || node == null)
        {
            throw new InvalidOperationException($"{context} requires explicit '{name}'.");
        }

        return node;
    }

    private static void ValidateOrderTypeId(int orderTypeId, string name, string context)
    {
        if ((uint)orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
        {
            throw new InvalidOperationException(
                $"{context}.{name} must be below {OrderTypeRegistry.MaxOrderTypes}.");
        }
    }
}
