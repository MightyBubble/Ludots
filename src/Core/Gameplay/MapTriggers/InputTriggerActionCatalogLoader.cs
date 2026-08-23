using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Loads Input/trigger_actions.json (ArrayById by id): the authored set of input
    /// actions bridged into map-scoped InputActionFired trigger events. An absent
    /// catalog yields an empty list — no actions bridged.
    /// </summary>
    public sealed class InputTriggerActionCatalogLoader
    {
        public const string ConfigPath = "Input/trigger_actions.json";

        private readonly ConfigPipeline _configs;

        public InputTriggerActionCatalogLoader(ConfigPipeline configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public IReadOnlyList<InputTriggerAction> Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            var actions = new List<InputTriggerAction>();
            if (catalog == null || !catalog.TryGet(ConfigPath, out var entry))
            {
                return actions;
            }

            IReadOnlyList<MergedConfigEntry> merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject node ||
                    node["id"]?.GetValue<string>() is not { } id)
                {
                    throw new InvalidOperationException(
                        $"{ConfigPath} entry #{i} must be an object with a non-empty 'id'.");
                }

                var action = new InputTriggerAction { Id = id.Trim() };
                if (node["pickRadiusCm"] is JsonValue radius && radius.TryGetValue<int>(out int radiusCm) && radiusCm > 0)
                {
                    action.PickRadiusCm = radiusCm;
                }

                actions.Add(action);
            }

            return actions;
        }
    }
}
