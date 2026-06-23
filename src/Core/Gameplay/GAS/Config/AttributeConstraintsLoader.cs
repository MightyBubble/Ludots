using System;
using System.Globalization;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class AttributeConstraintsLoader
    {
        private readonly ConfigPipeline _pipeline;

        public AttributeConstraintsLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/attribute_constraints.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            foreach (var kvp in mergedObject)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    throw new InvalidOperationException($"Config '{relativePath}' contains an empty attribute constraint key.");
                }

                if (kvp.Value is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Config '{relativePath}' attribute constraint '{kvp.Key}' must be an object.");
                }

                bool clampToBase = GetBool(obj, "clampToBase", attributeName: kvp.Key, relativePath);
                bool hasMin = TryGetFloat(obj, "min", out var min);
                bool hasMax = TryGetFloat(obj, "max", out var max);

                var constraints = AttributeRegistry.AttributeConstraints.Create(
                    clampToBase: clampToBase,
                    hasMin: hasMin,
                    min: min,
                    hasMax: hasMax,
                    max: max
                );

                AttributeRegistry.SetConstraints(kvp.Key, constraints);
            }
        }

        private static bool GetBool(JsonObject obj, string key, string attributeName, string relativePath)
        {
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            return node.GetValue<bool>();
        }

        private static bool TryGetFloat(JsonObject obj, string key, out float value)
        {
            value = 0f;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            if (node is JsonValue v)
            {
                if (v.TryGetValue<float>(out value))
                {
                    return true;
                }

                if (v.TryGetValue<string>(out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    value = f;
                    return true;
                }
            }

            throw new InvalidOperationException($"Attribute constraint field '{key}' must be a number.");
        }
    }
}

