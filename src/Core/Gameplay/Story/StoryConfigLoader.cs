using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Story
{
    public sealed class StoryConfigLoader
    {
        public const string LinesPath = "Story/lines.json";
        public const string ProfilesPath = "Story/presentation_profiles.json";
        public const string SpeakersPath = "Story/speakers.json";

        private readonly ConfigPipeline _pipeline;
        private readonly StoryDefinitionRegistry _registry;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public StoryConfigLoader(ConfigPipeline pipeline, StoryDefinitionRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            _registry.Clear();
            LoadLines(catalog, report);
            LoadProfiles(catalog, report);
            LoadSpeakers(catalog, report);
        }

        private void LoadLines(ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, LinesPath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            ThrowOnDuplicateIds(report, LinesPath, "Story line");
            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<StoryLineDefinition>(merged[i].Node.ToJsonString(), _jsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize story line at '{LinesPath}' index {i}.");
                _registry.Register(definition);
            }
        }

        private void LoadProfiles(ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, ProfilesPath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            ThrowOnDuplicateIds(report, ProfilesPath, "Story presentation profile");
            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<StoryPresentationProfileDefinition>(merged[i].Node.ToJsonString(), _jsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize story presentation profile at '{ProfilesPath}' index {i}.");
                _registry.Register(definition);
            }
        }

        private void LoadSpeakers(ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, SpeakersPath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            ThrowOnDuplicateIds(report, SpeakersPath, "Story speaker");
            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<StorySpeakerDefinition>(merged[i].Node.ToJsonString(), _jsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize story speaker at '{SpeakersPath}' index {i}.");
                _registry.Register(definition);
            }
        }

        private static void ThrowOnDuplicateIds(ConfigConflictReport? report, string relativePath, string label)
        {
            if (report == null)
            {
                return;
            }

            var duplicates = report.GetDuplicateIds(relativePath);
            if (duplicates.Count == 0)
            {
                return;
            }

            var lines = new List<string>();
            for (int i = 0; i < duplicates.Count; i++)
            {
                lines.Add($"  {label} '{duplicates[i].Id}' defined by both '{duplicates[i].FirstSource}' and '{duplicates[i].SecondSource}'");
            }

            throw new InvalidOperationException(
                $"{label} ids must be unique across mods (namespaced ids, e.g. 'author_kit.*'):{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
        }
    }
}
