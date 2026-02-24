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
            _pipeline = pipeline;
        }

        public void Load(string relativePath = "GAS/attribute_constraints.json")
        {
            if (_pipeline == null) return;

            var fragments = _pipeline.CollectFragments(relativePath);
            for (int fi = 0; fi < fragments.Count; fi++)
            {
                if (fragments[fi] is not JsonObject root) continue;

                foreach (var kvp in root)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value is not JsonObject obj) continue;

                    bool clampToBase = GetBool(obj, "clampToBase", defaultValue: false);
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
        }

        private static bool GetBool(JsonObject obj, string key, bool defaultValue)
        {
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return defaultValue;
            try
            {
                return node.GetValue<bool>();
            }
            catch
            {
                return defaultValue;
            }
        }

        private static bool TryGetFloat(JsonObject obj, string key, out float value)
        {
            value = 0f;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            try
            {
                value = node.GetValue<float>();
                return true;
            }
            catch
            {
                if (node is JsonValue v && v.TryGetValue<string>(out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    value = f;
                    return true;
                }
                return false;
            }
        }
    }
}

