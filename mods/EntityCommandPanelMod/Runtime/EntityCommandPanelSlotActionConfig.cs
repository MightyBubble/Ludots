using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace EntityCommandPanelMod.Runtime
{
    /// <summary>
    /// Slot hint inputs a game declares next to the interaction contexts that actually bind its keys:
    /// which input action sits on each ability slot, and which active interaction context selects
    /// which ability <c>modeHints</c> entry.
    /// </summary>
    public sealed class EntityCommandPanelSlotActionConfig
    {
        public const string RelativePath = "UI/entity_command_panel_slot_actions.json";

        private EntityCommandPanelSlotActionConfig(
            string[] slotActionIds,
            string[] modeHintContextIds,
            string[] modeHintKeys,
            string[] modeHintCastModeIds,
            string[] modeHintCastModeKeys)
        {
            SlotActionIds = slotActionIds;
            ModeHintContextIds = modeHintContextIds;
            ModeHintKeys = modeHintKeys;
            ModeHintCastModeIds = modeHintCastModeIds;
            ModeHintCastModeKeys = modeHintCastModeKeys;
        }

        public IReadOnlyList<string> SlotActionIds { get; }

        /// <summary>Interaction context ids, checked in declaration order; the first active one wins.</summary>
        public IReadOnlyList<string> ModeHintContextIds { get; }

        /// <summary><c>modeHints</c> key used while the context at the same index is active.</summary>
        public IReadOnlyList<string> ModeHintKeys { get; }

        /// <summary>Cast mode ids, checked before interaction contexts. Absent when the file omits the object.</summary>
        public IReadOnlyList<string> ModeHintCastModeIds { get; }

        /// <summary><c>modeHints</c> key used while the cast mode at the same index is the player default.</summary>
        public IReadOnlyList<string> ModeHintCastModeKeys { get; }

        public static EntityCommandPanelSlotActionConfig Load(ConfigPipeline pipeline, ConfigCatalog? catalog, ConfigConflictReport? report)
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

            if (merged["modeHintContexts"] is not JsonObject modeHintContexts)
            {
                throw new InvalidOperationException($"'{RelativePath}' must declare a modeHintContexts object.");
            }

            var contextIds = new string[modeHintContexts.Count];
            var modeKeys = new string[modeHintContexts.Count];
            int index = 0;
            foreach (KeyValuePair<string, JsonNode?> pair in modeHintContexts)
            {
                string? modeKey = pair.Value?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(modeKey))
                {
                    throw new InvalidOperationException(
                        $"'{RelativePath}' modeHintContexts['{pair.Key}'] must map a context id to a non-empty modeHints key.");
                }

                contextIds[index] = pair.Key;
                modeKeys[index] = modeKey;
                index++;
            }

            string[] castModeIds = Array.Empty<string>();
            string[] castModeKeys = Array.Empty<string>();
            if (merged.ContainsKey("modeHintCastModes"))
            {
                if (merged["modeHintCastModes"] is not JsonObject modeHintCastModes)
                {
                    throw new InvalidOperationException($"'{RelativePath}' modeHintCastModes must be an object.");
                }

                castModeIds = new string[modeHintCastModes.Count];
                castModeKeys = new string[modeHintCastModes.Count];
                int castIndex = 0;
                foreach (KeyValuePair<string, JsonNode?> pair in modeHintCastModes)
                {
                    string? modeKey = pair.Value?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(modeKey))
                    {
                        throw new InvalidOperationException(
                            $"'{RelativePath}' modeHintCastModes['{pair.Key}'] must map a cast mode id to a non-empty modeHints key.");
                    }

                    castModeIds[castIndex] = pair.Key;
                    castModeKeys[castIndex] = modeKey;
                    castIndex++;
                }
            }

            return new EntityCommandPanelSlotActionConfig(actionIds, contextIds, modeKeys, castModeIds, castModeKeys);
        }
    }
}
