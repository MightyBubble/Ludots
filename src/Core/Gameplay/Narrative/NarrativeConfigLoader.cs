using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Narrative
{
    public sealed class NarrativeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly NarrativeDefinitionRegistry _registry;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public NarrativeConfigLoader(ConfigPipeline pipeline, NarrativeDefinitionRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            _registry.Clear();
            LoadVariables(catalog, report);
            LoadDialogues(catalog, report);
            LoadCinematics(catalog, report);
        }

        private void LoadVariables(ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Narrative/variables.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<NarrativeVariableDefinition>(merged[i].Node.ToJsonString(), _jsonOptions);
                if (definition != null)
                {
                    _registry.Register(definition);
                }
            }
        }

        private void LoadDialogues(ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Narrative/dialogues.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<NarrativeDialogueDefinition>(merged[i].Node.ToJsonString(), _jsonOptions);
                if (definition != null)
                {
                    _registry.Register(definition);
                }
            }
        }

        private void LoadCinematics(ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Narrative/cinematics.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<NarrativeCinematicDefinition>(merged[i].Node.ToJsonString(), _jsonOptions);
                if (definition != null)
                {
                    _registry.Register(definition);
                }
            }
        }
    }
}
