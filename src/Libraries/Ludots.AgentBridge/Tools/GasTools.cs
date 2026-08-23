using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools
{
    public sealed class GasEntityTool : IAgentTool
    {
        public string Name => "ludots.gas.entity";

        public string Description =>
            "Inspect one entity's GAS state: gameplay tags (name-resolved), attributes (base/current, non-zero only), " +
            "active effect container, ability slots. Params: {entityId: int}.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["entityId"] = new JsonObject { ["type"] = "integer" },
            },
            ["required"] = new JsonArray("entityId"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            int entityId = AgentToolContext.RequireInt(args, "entityId");
            Entity entity = context.ResolveEntity(entityId);
            World world = context.Engine.World;

            var result = new JsonObject
            {
                ["entityId"] = entityId,
                ["name"] = world.Has<Name>(entity) ? world.Get<Name>(entity).Value : null,
            };

            if (world.Has<GameplayTagContainer>(entity))
            {
                ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(entity);
                var tagArray = new JsonArray();
                for (int tagId = 1; tagId <= GameplayTagContainer.MAX_TAG_ID; tagId++)
                {
                    if (!tags.HasTag(tagId)) continue;
                    string tagName = TagRegistry.GetName(tagId);
                    tagArray.Add(string.IsNullOrEmpty(tagName) ? $"#{tagId}" : tagName);
                }

                result["tags"] = tagArray;
            }

            if (world.Has<AttributeBuffer>(entity))
            {
                ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(entity);
                var attrArray = new JsonArray();
                for (int attrId = 0; attrId < AttributeBuffer.MAX_ATTRS; attrId++)
                {
                    float current = attributes.GetCurrent(attrId);
                    float baseValue = attributes.GetBase(attrId);
                    if (current == 0f && baseValue == 0f) continue;

                    string attrName = AttributeRegistry.GetName(attrId);
                    attrArray.Add(new JsonObject
                    {
                        ["id"] = attrId,
                        ["name"] = string.IsNullOrEmpty(attrName) ? null : attrName,
                        ["current"] = MathF.Round(current, 3),
                        ["base"] = MathF.Round(baseValue, 3),
                    });
                }

                result["attributes"] = attrArray;
            }

            if (world.Has<ActiveEffectContainer>(entity))
            {
                ref ActiveEffectContainer effects = ref world.Get<ActiveEffectContainer>(entity);
                var effectArray = new JsonArray();
                for (int i = 0; i < effects.Count; i++)
                {
                    effectArray.Add(effects.GetEntity(i).Id);
                }

                result["activeEffects"] = new JsonObject
                {
                    ["count"] = effects.Count,
                    ["effectEntityIds"] = effectArray,
                };
            }

            if (world.Has<AbilityStateBuffer>(entity))
            {
                ref AbilityStateBuffer abilities = ref world.Get<AbilityStateBuffer>(entity);
                var abilityArray = new JsonArray();
                for (int i = 0; i < abilities.Count; i++)
                {
                    AbilitySlotState slot = abilities.Get(i);
                    abilityArray.Add(new JsonObject
                    {
                        ["slot"] = i,
                        ["abilityId"] = slot.AbilityId,
                        ["templateEntityId"] = slot.TemplateEntityId,
                    });
                }

                result["abilities"] = abilityArray;
            }

            return result;
        }
    }

    public sealed class GasDiagnosticsTool : IAgentTool
    {
        public string Name => "ludots.gas.diagnostics";
        public string Description => "Dump the GasDiagnosticEventBuffer for the current frame: system/metric/capacity/count per event. No parameters.";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            var buffer = context.RequireService(CoreServiceKeys.GasDiagnosticEventBuffer);
            var events = new JsonArray();
            for (int i = 0; i < buffer.Count; i++)
            {
                GasDiagnosticEvent e = buffer[i];
                events.Add(new JsonObject
                {
                    ["system"] = e.System.ToString(),
                    ["metric"] = e.Metric.ToString(),
                    ["capacity"] = e.Capacity,
                    ["count"] = e.Count,
                });
            }

            return new JsonObject
            {
                ["frameIndex"] = buffer.FrameIndex,
                ["count"] = buffer.Count,
                ["capacity"] = buffer.Capacity,
                ["events"] = events,
            };
        }
    }
}
