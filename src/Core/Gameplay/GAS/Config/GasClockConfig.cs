using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class GasClockConfig
    {
        public int StepEveryFixedTicks { get; set; }
        public GasStepMode Mode { get; set; }
    }

    public sealed class GasClockConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public GasClockConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public GasClockConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/clock.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);

            if (mergedObject == null)
            {
                throw new InvalidOperationException($"{relativePath} must provide an explicit GAS clock object.");
            }

            var config = new GasClockConfig
            {
                Mode = ParseMode(RequireCanonicalString(mergedObject["mode"], $"{relativePath}.mode")),
                StepEveryFixedTicks = RequireStepEveryFixedTicks(mergedObject["stepEveryFixedTicks"], relativePath),
            };

            if (config.StepEveryFixedTicks < 1)
            {
                throw new InvalidOperationException($"{relativePath}.stepEveryFixedTicks must be >= 1.");
            }

            return config;
        }

        private static string RequireCanonicalString(JsonNode? node, string context)
        {
            if (node is not JsonValue value ||
                !value.TryGetValue<string>(out string? text) ||
                string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} requires an explicit semantic string.");
            }

            string trimmed = text.Trim();
            if (!string.Equals(text, trimmed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must not include leading or trailing whitespace.");
            }

            return text;
        }

        private static int RequireStepEveryFixedTicks(JsonNode? node, string relativePath)
        {
            if (node is not JsonValue value || !value.TryGetValue<int>(out int ticks))
            {
                throw new InvalidOperationException($"{relativePath}.stepEveryFixedTicks requires an explicit integer field.");
            }

            return ticks;
        }

        private static GasStepMode ParseMode(string mode)
        {
            return mode switch
            {
                "Auto" => GasStepMode.Auto,
                "Manual" => GasStepMode.Manual,
                "Paused" => GasStepMode.Paused,
                _ => throw new InvalidOperationException(
                    $"GAS clock mode '{mode}' is invalid. Supported: Auto, Manual, Paused."),
            };
        }
    }
}
