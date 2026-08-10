using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Providers;

namespace Ludots.Core.Gameplay.Activities
{
    public sealed class ActivityConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly ActivityDefinitionRegistry _registry;
        private readonly ProviderDefinitionValidator? _validator;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        public ActivityConfigLoader(
            ConfigPipeline pipeline,
            ActivityDefinitionRegistry registry,
            ProviderDefinitionValidator? validator = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _validator = validator;
        }

        public void Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            _registry.Clear();
            if (catalog == null || !catalog.TryGet("Activities/activities.json", out _))
            {
                return;
            }

            var entry = ConfigPipeline.RequireEntry(
                catalog,
                "Activities/activities.json",
                ConfigMergePolicy.ArrayById,
                "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var rows = new List<(string Id, ActivityDefinition Definition)>(merged.Count);

            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<ActivityDefinition>(
                        merged[i].Node.ToJsonString(),
                        _jsonOptions)
                    ?? throw new InvalidOperationException(
                        $"Failed to deserialize activity definition '{merged[i].Id}'.");
                definition.Id = string.IsNullOrWhiteSpace(definition.Id) ? merged[i].Id : definition.Id;
                ValidateProviderKeys(definition, merged[i].Node.ToJsonString());
                rows.Add((definition.Id, definition));
            }

            rows.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));
            for (int i = 0; i < rows.Count; i++)
            {
                _registry.Register(rows[i].Id, rows[i].Definition);
            }
        }

        private void ValidateProviderKeys(ActivityDefinition definition, string json)
        {
            if (_validator == null)
            {
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            IReadOnlyList<ProviderDefinitionReference> refs =
                ProviderDefinitionValidator.CollectFromJsonDocument(definition.Id, doc.RootElement);
            _validator.ValidateAndThrow(refs);
        }
    }
}
