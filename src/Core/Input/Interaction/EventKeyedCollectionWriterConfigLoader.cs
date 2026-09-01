using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loads Input/collection_event_writers.json (ArrayById): each entry's <c>id</c> is the
    /// event key the <see cref="EventKeyedCollectionWriter"/> consumes. An absent catalog
    /// yields an empty list — no pass-through events are received.
    /// </summary>
    public sealed class EventKeyedCollectionWriterConfigLoader
    {
        public const string ConfigPath = "Input/collection_event_writers.json";

        private readonly ConfigPipeline _configs;

        public EventKeyedCollectionWriterConfigLoader(ConfigPipeline configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public IReadOnlyList<string> Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            var eventKeys = new List<string>();
            if (catalog == null || !catalog.TryGet(ConfigPath, out var entry))
            {
                return eventKeys;
            }

            IReadOnlyList<MergedConfigEntry> merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                string? eventKey = merged[i].Node?["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(eventKey) || !string.Equals(eventKey, eventKey.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{ConfigPath} entry #{i} must declare a trimmed non-empty 'id' (the consumed event key).");
                }

                eventKeys.Add(eventKey);
            }

            return eventKeys;
        }
    }
}
