using System;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Systems
{
    /// <summary>
    /// Config of <c>Input/axis_move.json</c> (RFC-0065 INT-6, DEC-15): the WASD-style axis intent →
    /// throttled move order kernel. Disabled by default — mods/control schemes enable it explicitly
    /// (enabled=false is explicit configuration, not a fallback).
    /// </summary>
    public sealed class AxisMoveConfig
    {
        /// <summary>Master switch; when false the system does zero work per tick.</summary>
        public bool Enabled { get; set; }

        /// <summary>Axis2D input action id sampled from the authoritative input snapshot.</summary>
        public string ActionId { get; set; } = string.Empty;

        /// <summary>Order type key resolved against <c>OrderTypeRegistry</c> (fail fast when enabled).</summary>
        public string OrderTypeKey { get; set; } = string.Empty;

        /// <summary>Simulation ticks between two submitted orders while the axis is held.</summary>
        public int ThrottleTicks { get; set; }

        /// <summary>Distance in world centimeters from the actor's position to the order target.</summary>
        public int StepDistanceCm { get; set; }
    }

    /// <summary>
    /// Loader for <c>Input/axis_move.json</c>: catalog-declared DeepObject merge through the shared
    /// <see cref="ConfigPipeline"/> with structural fail-fast validation.
    /// </summary>
    public sealed class AxisMoveConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public AxisMoveConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged axis move config.</summary>
        public AxisMoveConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/axis_move.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<AxisMoveConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>Structural fail-fast validation; order type resolution happens at system construction.</summary>
        public static void Validate(AxisMoveConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (!config.Enabled)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(config.ActionId))
            {
                throw new InvalidOperationException($"Axis move config '{source}' must define actionId when enabled.");
            }

            if (string.IsNullOrWhiteSpace(config.OrderTypeKey))
            {
                throw new InvalidOperationException($"Axis move config '{source}' must define orderTypeKey when enabled.");
            }

            if (config.ThrottleTicks < 1)
            {
                throw new InvalidOperationException($"Axis move config '{source}' throttleTicks must be >= 1 when enabled.");
            }

            if (config.StepDistanceCm <= 0)
            {
                throw new InvalidOperationException($"Axis move config '{source}' stepDistanceCm must be positive when enabled.");
            }
        }
    }
}
