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
            string modeHintPrefKey,
            string[] modeHintPrefValueIds,
            string[] modeHintPrefHintKeys)
        {
            SlotActionIds = slotActionIds;
            ModeHintContextIds = modeHintContextIds;
            ModeHintKeys = modeHintKeys;
            ModeHintPrefKey = modeHintPrefKey;
            ModeHintPrefValueIds = modeHintPrefValueIds;
            ModeHintPrefHintKeys = modeHintPrefHintKeys;
        }

        public IReadOnlyList<string> SlotActionIds { get; }

        /// <summary>Interaction context ids, checked in declaration order; the first active one wins.</summary>
        public IReadOnlyList<string> ModeHintContextIds { get; }

        /// <summary><c>modeHints</c> key used while the context at the same index is active.</summary>
        public IReadOnlyList<string> ModeHintKeys { get; }

        /// <summary>Preference key whose player-level value selects a hint. Empty when the file omits modeHintPrefs.</summary>
        public string ModeHintPrefKey { get; }

        /// <summary>Allowed values of <see cref="ModeHintPrefKey"/>, checked before interaction contexts.</summary>
        public IReadOnlyList<string> ModeHintPrefValueIds { get; }

        /// <summary><c>modeHints</c> key used while the preference value at the same index is the player-level value.</summary>
        public IReadOnlyList<string> ModeHintPrefHintKeys { get; }

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

            string prefKey = string.Empty;
            string[] prefValueIds = Array.Empty<string>();
            string[] prefHintKeys = Array.Empty<string>();
            if (merged.ContainsKey("modeHintPrefs"))
            {
                if (merged["modeHintPrefs"] is not JsonObject modeHintPrefs)
                {
                    throw new InvalidOperationException($"'{RelativePath}' modeHintPrefs must be an object.");
                }

                if (modeHintPrefs["key"] is not JsonValue keyValue ||
                    !keyValue.TryGetValue<string>(out string? keyName) ||
                    string.IsNullOrWhiteSpace(keyName))
                {
                    throw new InvalidOperationException($"'{RelativePath}' modeHintPrefs.key must be a non-empty preference key.");
                }

                if (modeHintPrefs["values"] is not JsonObject prefValues)
                {
                    throw new InvalidOperationException($"'{RelativePath}' modeHintPrefs.values must be an object.");
                }

                prefKey = keyName;
                prefValueIds = new string[prefValues.Count];
                prefHintKeys = new string[prefValues.Count];
                int prefIndex = 0;
                foreach (KeyValuePair<string, JsonNode?> pair in prefValues)
                {
                    string? hintKey = pair.Value?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(hintKey))
                    {
                        throw new InvalidOperationException(
                            $"'{RelativePath}' modeHintPrefs.values['{pair.Key}'] must map a preference value to a non-empty modeHints key.");
                    }

                    prefValueIds[prefIndex] = pair.Key;
                    prefHintKeys[prefIndex] = hintKey;
                    prefIndex++;
                }
            }

            return new EntityCommandPanelSlotActionConfig(actionIds, contextIds, modeKeys, prefKey, prefValueIds, prefHintKeys);
        }
    }
}
