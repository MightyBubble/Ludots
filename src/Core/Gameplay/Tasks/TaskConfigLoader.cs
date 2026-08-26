using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Story;

namespace Ludots.Core.Gameplay.Tasks
{
    public sealed class TaskConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly TaskDefinitionRegistry _registry;
        private readonly ProviderDefinitionValidator _validator;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        public TaskConfigLoader(
            ConfigPipeline pipeline,
            TaskDefinitionRegistry registry,
            ProviderDefinitionValidator validator)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public void Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            _registry.Clear();
            if (catalog == null || !catalog.TryGet("Tasks/tasks.json", out _))
            {
                return;
            }

            var entry = ConfigPipeline.RequireEntry(
                catalog,
                "Tasks/tasks.json",
                ConfigMergePolicy.ArrayById,
                "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var rows = new List<(string Id, TaskDefinition Definition)>(merged.Count);

            for (int i = 0; i < merged.Count; i++)
            {
                JsonObject node = merged[i].Node;
                if (node.ContainsKey("on_enter_cinematic_id"))
                {
                    throw new InvalidOperationException(
                        $"Task entry index {i} uses retired field 'on_enter_cinematic_id'. Use 'on_enter_sequence_id'. {LegacyNarrativeConfigGuard.MigrationMessage}");
                }

                var definition = JsonSerializer.Deserialize<TaskDefinition>(
                        node.ToJsonString(),
                        _jsonOptions)
                    ?? throw new InvalidOperationException(
                        $"Failed to deserialize task definition '{merged[i].Id}'.");
                definition.Id = string.IsNullOrWhiteSpace(definition.Id) ? merged[i].Id : definition.Id;
                using (JsonDocument doc = JsonDocument.Parse(node.ToJsonString()))
                {
                    IReadOnlyList<ProviderDefinitionReference> refs =
                        ProviderDefinitionValidator.CollectFromJsonDocument(definition.Id, doc.RootElement);
                    _validator.ValidateAndThrow(refs);
                }

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
