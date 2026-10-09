using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace EntityCommandPanelMod.Runtime
{
    /// <summary>
    /// Which input action a game binds to each ability slot, used only for the key hint drawn on
    /// a slot. Games declare it next to the interaction context that actually binds those keys.
    /// </summary>
    public static class EntityCommandPanelSlotActionConfig
    {
        public const string RelativePath = "UI/entity_command_panel_slot_actions.json";

        public static IReadOnlyList<string> Load(ConfigPipeline pipeline, ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            var entry = ConfigPipeline.RequireEntry(catalog, RelativePath, ConfigMergePolicy.DeepObject);
            JsonObject merged = pipeline.MergeDeepObjectFromCatalog(in entry, report)
                ?? throw new InvalidOperationException($"Missing required config '{RelativePath}'.");
            if (merged["slotActionIds"] is not JsonArray array)
            {
                throw new InvalidOperationException($"'{RelativePath}' must declare a slotActionIds array.");
            }

            var actionIds = new string[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                string? actionId = array[i]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(actionId))
                {
                    throw new InvalidOperationException($"'{RelativePath}' slotActionIds[{i}] must be a non-empty action id.");
                }

                actionIds[i] = actionId;
            }

            return actionIds;
        }
    }
}
