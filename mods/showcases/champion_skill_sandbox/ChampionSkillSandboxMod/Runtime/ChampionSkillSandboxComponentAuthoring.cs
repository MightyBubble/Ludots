using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace ChampionSkillSandboxMod.Runtime
{
    /// <summary>
    /// Sandbox-local authoring. Collider2D 的作者面归 Core（#1480 Physics2DComponentAuthoring，
    /// 扁平 shape schema），模板数据直接用它；这里只登记 Core 没有的 PhysicsMaterial2D。
    /// </summary>
    internal static class ChampionSkillSandboxComponentAuthoring
    {
        public static void Register(string modId)
        {
            Ludots.Core.Config.ComponentRegistry.Register<PhysicsMaterial2D>("PhysicsMaterial2D", SetPhysicsMaterial2D, modId);
        }

        private static void SetPhysicsMaterial2D(Entity entity, JsonNode data)
        {
            JsonObject obj = RequireObject(data, "PhysicsMaterial2D");
            ValidateProperties(obj, "PhysicsMaterial2D", "friction", "restitution", "baseDamping");

            entity.Add(new PhysicsMaterial2D
            {
                Friction = Fix64.FromFloat(ReadRequiredFloat(obj, "friction", "PhysicsMaterial2D")),
                Restitution = Fix64.FromFloat(ReadRequiredFloat(obj, "restitution", "PhysicsMaterial2D")),
                BaseDamping = Fix64.FromFloat(ReadRequiredFloat(obj, "baseDamping", "PhysicsMaterial2D"))
            });
        }

        private static JsonObject RequireObject(JsonNode node, string name)
        {
            if (node is JsonObject obj)
            {
                return obj;
            }

            throw new InvalidOperationException($"{name} requires an object payload.");
        }

        private static float ReadRequiredFloat(JsonObject obj, string name, string context)
        {
            if (!obj.TryGetPropertyValue(name, out JsonNode? node) || node is null || node.GetValueKind() != JsonValueKind.Number)
            {
                throw new InvalidOperationException($"{context}.{name} requires a numeric value.");
            }

            return node.GetValue<float>();
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
    }
}
