using System;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/interaction_modes.json</c> (#1306). Follows the
    /// <see cref="ControlSchemeConfigLoader"/> mounting pattern: catalog-declared DeepObject merge
    /// through the shared <see cref="ConfigPipeline"/>; context id and priority references resolve
    /// at <see cref="InteractionModeMap.Install"/> (fail fast on undefined contexts).
    /// </summary>
    public sealed class InteractionModeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public InteractionModeConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load the merged interaction mode config.</summary>
        public InteractionModesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/interaction_modes.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            return mergedObject.Deserialize<InteractionModesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
        }
    }
}
