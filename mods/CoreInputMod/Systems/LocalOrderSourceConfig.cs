using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace CoreInputMod.Systems
{
    /// <summary>
    /// Per-mod declaration consumed by CoreInputMod's game-start installer: a mod that ships
    /// assets/Input/local_order_source.json gets the standard local order source installed for
    /// its own assets/Input/input_order_mappings.json, with no mod-side wrapper system.
    /// </summary>
    public sealed class LocalOrderSourceConfig
    {
        public static readonly JsonSerializerOptions JsonOptions = StrictJsonOptions.CreateCamelCase();

        /// <summary>"InputCollection" (default) or "LocalInput"; mirrors the two install sites used by showcases.</summary>
        public string SystemGroup { get; set; } = "InputCollection";

        /// <summary>When set, wires the queue-modifier provider reading this input action.</summary>
        public string? QueueModifierActionId { get; set; }

        /// <summary>When set, written to SkillBarOverlaySystem.SkillBarKeyLabelsKey once the mapping exists.</summary>
        public string[]? SkillBarKeyLabels { get; set; }

        /// <summary>When set, written to SkillBarOverlaySystem.SkillBarEnabledKey once the mapping exists.</summary>
        public bool? SkillBarEnabled { get; set; }

        /// <summary>Global-context key that receives per-frame install/bind state for acceptance diagnostics.</summary>
        public string? DiagnosticsKey { get; set; }

        /// <summary>Fail fast when the mapping asset or input reader is unavailable instead of skipping.</summary>
        public bool RequireMapping { get; set; }

        public static LocalOrderSourceConfig LoadFromStream(System.IO.Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            var config = JsonSerializer.Deserialize<LocalOrderSourceConfig>(stream, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize assets/Input/local_order_source.json.");
            if (!string.Equals(config.SystemGroup, "InputCollection", StringComparison.Ordinal) &&
                !string.Equals(config.SystemGroup, "LocalInput", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"assets/Input/local_order_source.json systemGroup must be 'InputCollection' or 'LocalInput', got '{config.SystemGroup}'.");
            }

            return config;
        }
    }
}
