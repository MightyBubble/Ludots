using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using CombatStanceBehaviorMod.Components;

namespace CombatStanceBehaviorMod.Runtime;

internal static class CombatStanceComponentAuthoring
{
    public static void Register(string modId)
    {
        Ludots.Core.Config.ComponentRegistry.Register<CombatStanceState>("CombatStanceState", SetCombatStanceState, modId);
    }

    private static void SetCombatStanceState(Entity entity, JsonNode data)
    {
        if (data is not JsonObject obj)
        {
            throw new InvalidOperationException("CombatStanceState requires an object payload.");
        }

        RejectNumericIdAuthoring(obj, "stanceId");
        RejectNumericIdAuthoring(obj, "Stance");
        ValidateProperties(obj, "CombatStanceState", "stance", "leashRadiusCm", "retaliationTtlSteps");

        int stance = ParseStance(RequireStringProperty(obj, "stance", "CombatStanceState"), "CombatStanceState.stance");
        bool hasLeash = TryReadIntProperty(obj, "leashRadiusCm", out int leashRadiusCm);
        bool hasRetaliationTtl = TryReadIntProperty(obj, "retaliationTtlSteps", out int retaliationTtlSteps);

        if (hasLeash && leashRadiusCm < 0)
        {
            throw new InvalidOperationException("CombatStanceState.leashRadiusCm must be non-negative.");
        }

        if (hasRetaliationTtl && retaliationTtlSteps <= 0)
        {
            throw new InvalidOperationException("CombatStanceState.retaliationTtlSteps must be positive when authored.");
        }

        if (stance != CombatStances.HoldFire && (!hasLeash || leashRadiusCm <= 0))
        {
            throw new InvalidOperationException("CombatStanceState requires positive leashRadiusCm for non-HoldFire stances.");
        }

        if (stance == CombatStances.ReturnFire && !hasRetaliationTtl)
        {
            throw new InvalidOperationException("CombatStanceState ReturnFire requires explicit retaliationTtlSteps.");
        }

        entity.Add<CombatStanceState>(new CombatStanceState
        {
            Stance = stance,
            LeashRadiusCm = leashRadiusCm,
            RetaliationTtlSteps = retaliationTtlSteps,
        });
    }

    private static int ParseStance(string value, string context)
    {
        return value switch
        {
            "HoldFire" => CombatStances.HoldFire,
            "ReturnFire" => CombatStances.ReturnFire,
            "Defend" => CombatStances.Defend,
            "AttackAnything" => CombatStances.AttackAnything,
            _ => throw new InvalidOperationException($"{context} references unknown combat stance '{value}'.")
        };
    }

    private static void ValidateProperties(JsonObject obj, string context, params string[] allowedNames)
    {
        foreach (var kvp in obj)
        {
            bool allowed = false;
            for (int i = 0; i < allowedNames.Length; i++)
            {
                if (string.Equals(kvp.Key, allowedNames[i], StringComparison.Ordinal))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
            {
                throw new InvalidOperationException($"{context} contains unsupported property '{kvp.Key}'.");
            }
        }
    }

    private static string RequireStringProperty(JsonObject obj, string name, string context)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode node) || node == null)
        {
            throw new InvalidOperationException($"{context} requires explicit '{name}'.");
        }

        if (node.GetValueKind() != JsonValueKind.String)
        {
            throw new InvalidOperationException($"{context}.{name} requires a string value.");
        }

        string value = node.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{context}.{name} requires a non-empty string value.");
        }

        return value;
    }

    private static bool TryReadIntProperty(JsonObject obj, string name, out int value)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode node))
        {
            value = 0;
            return false;
        }

        if (node == null || node.GetValueKind() == JsonValueKind.Null)
        {
            throw new InvalidOperationException($"{name} requires a non-null integer value.");
        }

        if (node.GetValueKind() != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"{name} requires an integer value.");
        }

        value = node.GetValue<int>();
        return true;
    }

    private static void RejectNumericIdAuthoring(JsonObject obj, string numericProperty)
    {
        if (obj.ContainsKey(numericProperty))
        {
            throw new InvalidOperationException($"CombatStanceState does not support '{numericProperty}'. Use 'stance' with a string key.");
        }
    }
}
