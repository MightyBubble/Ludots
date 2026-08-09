using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Engine.Physics2D
{
    /// <summary>
    /// Explicit budgets for the kinematic body and contact event pipeline.
    /// Strict data contract: the config file must exist and every field must be explicit;
    /// missing, unknown, or illegal values fail startup. No default injection.
    /// </summary>
    public sealed class Physics2DKinematicConfig
    {
        public int KinematicBodyCapacity { get; set; }
        public int ContactEventQueueCapacity { get; set; }
        public List<string> ContactEventEmitterLayers { get; set; }
    }

    public sealed class Physics2DKinematicConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public Physics2DKinematicConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public Physics2DKinematicConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Physics2D/kinematic.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);

            if (mergedObject == null)
            {
                throw new InvalidOperationException(
                    $"Physics2D kinematic config '{relativePath}' is required and no source provided it. " +
                    "kinematicBodyCapacity, contactEventQueueCapacity, and contactEventEmitterLayers must be explicit (no default injection).");
            }

            var options = StrictJsonOptions.CreateCamelCase();
            var config = mergedObject.Deserialize<Physics2DKinematicConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize Physics2DKinematicConfig.");
            }

            Validate(config, relativePath);
            return config;
        }

        private static void Validate(Physics2DKinematicConfig config, string relativePath)
        {
            if (config.KinematicBodyCapacity < 1)
            {
                throw new InvalidOperationException(
                    $"'{relativePath}' kinematicBodyCapacity must be an explicit integer > 0, got {config.KinematicBodyCapacity}.");
            }

            if (config.ContactEventQueueCapacity < 1)
            {
                throw new InvalidOperationException(
                    $"'{relativePath}' contactEventQueueCapacity must be an explicit integer > 0, got {config.ContactEventQueueCapacity}.");
            }

            if (config.ContactEventEmitterLayers == null)
            {
                throw new InvalidOperationException(
                    $"'{relativePath}' requires explicit 'contactEventEmitterLayers' (list of layer names allowed to emit contact events; may be empty).");
            }

            for (int i = 0; i < config.ContactEventEmitterLayers.Count; i++)
            {
                string layerName = config.ContactEventEmitterLayers[i];
                if (string.IsNullOrWhiteSpace(layerName))
                {
                    throw new InvalidOperationException(
                        $"'{relativePath}' contactEventEmitterLayers[{i}] must be a non-empty layer name.");
                }

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(config.ContactEventEmitterLayers[j], layerName, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"'{relativePath}' contactEventEmitterLayers contains duplicate layer name '{layerName}'.");
                    }
                }
            }
        }
    }
}
