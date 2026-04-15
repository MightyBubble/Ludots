using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationBehaviorConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly PresentationBehaviorRegistry _behaviors;
        private readonly MeshAssetRegistry _meshes;

        public PresentationBehaviorConfigLoader(
            ConfigPipeline configs,
            PresentationBehaviorRegistry behaviors,
            MeshAssetRegistry meshes)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _behaviors = behaviors ?? throw new ArgumentNullException(nameof(behaviors));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/presentation_behaviors.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                JsonNode node = merged[i].Node;
                string key = node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("Presentation behavior is missing required 'id'.");
                }

                _behaviors.Register(key, ParseBehavior(key, node));
            }
        }

        private PresentationBehaviorDefinition ParseBehavior(string key, JsonNode node)
        {
            if (node["states"] is not JsonArray statesArray || statesArray.Count == 0)
            {
                throw new InvalidOperationException($"Presentation behavior '{key}' must define at least one state.");
            }

            var states = new PresentationBehaviorStateDefinition[statesArray.Count];
            for (int i = 0; i < statesArray.Count; i++)
            {
                if (statesArray[i] is not JsonObject stateNode)
                {
                    throw new InvalidOperationException($"Presentation behavior '{key}' state[{i}] must be an object.");
                }

                string stateId = stateNode["stateId"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(stateId))
                {
                    throw new InvalidOperationException($"Presentation behavior '{key}' state[{i}] must declare stateId.");
                }

                string prefabKey = stateNode["prefabAssetId"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(prefabKey))
                {
                    throw new InvalidOperationException($"Presentation behavior '{key}' state '{stateId}' must declare prefabAssetId.");
                }

                int prefabAssetId = _meshes.GetId(prefabKey);
                if (prefabAssetId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Presentation behavior '{key}' state '{stateId}' references unknown prefabAssetId '{prefabKey}'.");
                }

                states[i] = new PresentationBehaviorStateDefinition(stateId, prefabAssetId);
            }

            return new PresentationBehaviorDefinition
            {
                States = states,
            };
        }
    }
}
