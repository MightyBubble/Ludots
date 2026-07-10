using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Quests
{
    public sealed class QuestConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly QuestDefinitionRegistry _registry;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public QuestConfigLoader(ConfigPipeline pipeline, QuestDefinitionRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            _registry.Clear();
            var entry = ConfigPipeline.RequireEntry(catalog, "Quests/quests.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var rows = new List<(string Id, QuestDefinition Definition)>(merged.Count);

            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<QuestDefinition>(merged[i].Node.ToJsonString(), _jsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize quest definition '{merged[i].Id}'.");
                definition.Id = string.IsNullOrWhiteSpace(definition.Id) ? merged[i].Id : definition.Id;
                rows.Add((definition.Id, definition));
            }

            rows.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));
            for (int i = 0; i < rows.Count; i++)
            {
                _registry.Register(rows[i].Id, rows[i].Definition);
            }
        }
    }
}
