using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Story;

namespace Ludots.Core.Gameplay.Sequencer
{
    public sealed class SequencerConfigLoader
    {
        public const string SequencesPath = "Sequencer/sequences.json";

        private readonly ConfigPipeline _pipeline;
        private readonly SequenceDefinitionRegistry _registry;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public SequencerConfigLoader(ConfigPipeline pipeline, SequenceDefinitionRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            LegacyNarrativeConfigGuard.RejectIfPresent(catalog);
            _registry.Clear();
            var entry = ConfigPipeline.RequireEntry(catalog, SequencesPath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                JsonObject node = merged[i].Node;
                if (node.ContainsKey("steps"))
                {
                    throw new InvalidOperationException(
                        $"Sequence entry index {i} uses legacy cinematic 'steps'. Use Sequencer tracks. {LegacyNarrativeConfigGuard.MigrationMessage}");
                }

                var definition = JsonSerializer.Deserialize<SequenceDefinition>(node.ToJsonString(), _jsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize sequence at '{SequencesPath}' index {i}.");
                _registry.Register(definition);
            }
        }
    }
}
