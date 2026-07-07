using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/control_schemes.json</c> (RFC-0065 INT-5, §5.11). Follows the
    /// <c>CastCommitProfileConfigLoader</c> mounting pattern: catalog-declared DeepObject merge
    /// through the shared <see cref="ConfigPipeline"/>. Command intent and axis move order type
    /// references resolve at <see cref="ControlSchemeRuntime.Install"/> (fail fast on uninstalled
    /// command intent profiles, cast dispatch profiles, or unknown order type keys).
    /// </summary>
    public sealed class ControlSchemeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public ControlSchemeConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged control scheme config.</summary>
        public ControlSchemesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/control_schemes.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<ControlSchemesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>Structural fail-fast validation; intent/context id resolution happens at install.</summary>
        public static void Validate(ControlSchemesConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Schemes == null)
            {
                throw new InvalidOperationException($"Control scheme config '{source}' must explicitly define schemes.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Schemes.Count; i++)
            {
                ControlSchemeDefinition scheme = config.Schemes[i]
                    ?? throw new InvalidOperationException($"Control scheme config '{source}' schemes[{i}] must be an object.");
                string path = $"{source}.schemes[{i}]";
                RequireTrimmedNonEmpty(scheme.Id, $"{path}.id");
                if (!ids.Add(scheme.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates control scheme '{scheme.Id}'.");
                }

                if (scheme.InputContexts == null)
                {
                    throw new InvalidOperationException($"{path}.inputContexts must be explicitly declared (empty array allowed).");
                }

                for (int c = 0; c < scheme.InputContexts.Count; c++)
                {
                    RequireTrimmedNonEmpty(scheme.InputContexts[c], $"{path}.inputContexts[{c}]");
                }

                if (scheme.Defaults == null)
                {
                    throw new InvalidOperationException($"{path}.defaults must be explicitly declared.");
                }

                RequireTrimmedNonEmpty(scheme.Defaults.CommandIntentId, $"{path}.defaults.commandIntentId");
                RequireTrimmedNonEmpty(scheme.Defaults.CastDispatchProfileId, $"{path}.defaults.castDispatchProfileId");

                if (scheme.AxisMove != null)
                {
                    RequireTrimmedNonEmpty(scheme.AxisMove.ActionId, $"{path}.axisMove.actionId");
                    RequireTrimmedNonEmpty(scheme.AxisMove.OrderTypeKey, $"{path}.axisMove.orderTypeKey");
                    if (scheme.AxisMove.ThrottleTicks < 1)
                    {
                        throw new InvalidOperationException($"{path}.axisMove.throttleTicks must be >= 1.");
                    }

                    if (scheme.AxisMove.StepDistanceCm <= 0)
                    {
                        throw new InvalidOperationException($"{path}.axisMove.stepDistanceCm must be positive.");
                    }
                }
            }

            if (config.AllowedSchemes != null)
            {
                for (int i = 0; i < config.AllowedSchemes.Count; i++)
                {
                    RequireTrimmedNonEmpty(config.AllowedSchemes[i], $"{source}.allowedSchemes[{i}]");
                    if (!ids.Contains(config.AllowedSchemes[i]))
                    {
                        throw new InvalidOperationException(
                            $"{source}.allowedSchemes[{i}] references undeclared scheme '{config.AllowedSchemes[i]}'.");
                    }
                }
            }
        }

        private static void RequireTrimmedNonEmpty(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{path} must be a non-empty string.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} must not contain leading or trailing whitespace.");
            }
        }
    }
}
