using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Config
{
    public sealed class AnimationClipConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly AnimationClipRegistry _clips;

        public AnimationClipConfigLoader(
            ConfigPipeline configs,
            AnimationClipRegistry clips)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _clips = clips ?? throw new ArgumentNullException(nameof(clips));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/animation_clips.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string key = node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Animation clip is missing required 'id'.");

                _clips.Register(key, ParseClip(node, key));
            }
        }

        private static AnimationClipDefinition ParseClip(JsonNode node, string key)
        {
            string assetKindText = node["assetKind"]?.GetValue<string>() ?? nameof(AnimationClipAssetKind.Clip);
            if (!Enum.TryParse(assetKindText, ignoreCase: true, out AnimationClipAssetKind assetKind))
            {
                throw new InvalidOperationException($"Animation clip '{key}' has invalid assetKind '{assetKindText}'.");
            }

            if (node["locators"] is not JsonArray locatorsArray || locatorsArray.Count == 0)
            {
                throw new InvalidOperationException($"Animation clip '{key}' must define at least one locator.");
            }

            var locators = new AnimationClipLocatorDefinition[locatorsArray.Count];
            for (int i = 0; i < locatorsArray.Count; i++)
            {
                if (locatorsArray[i] is not JsonObject locatorNode)
                {
                    throw new InvalidOperationException($"Animation clip '{key}' locator[{i}] must be an object.");
                }

                string backendId = locatorNode["backendId"]?.GetValue<string>() ?? string.Empty;
                string assetRef = locatorNode["assetRef"]?.GetValue<string>() ?? string.Empty;
                string variant = locatorNode["variant"]?.GetValue<string>() ?? string.Empty;
                locators[i] = new AnimationClipLocatorDefinition(backendId, assetRef, variant);
            }

            ValidateUniqueBackends(key, locators);

            JsonNode? blendInputsNode = node["blendInputs"];
            AnimationBlendInputSource blendInputX = ParseBlendInput(blendInputsNode?["x"], AnimationBlendInputSource.Scalar0, key, "x");
            AnimationBlendInputSource blendInputY = ParseBlendInput(blendInputsNode?["y"], AnimationBlendInputSource.Scalar1, key, "y");

            return new AnimationClipDefinition
            {
                AssetKind = assetKind,
                BlendInputX = blendInputX,
                BlendInputY = blendInputY,
                Locators = locators,
            };
        }

        private static AnimationBlendInputSource ParseBlendInput(
            JsonNode? node,
            AnimationBlendInputSource fallback,
            string key,
            string axisLabel)
        {
            if (node == null)
            {
                return fallback;
            }

            string value = node.GetValue<string>();
            if (!Enum.TryParse(value, ignoreCase: true, out AnimationBlendInputSource input))
            {
                throw new InvalidOperationException(
                    $"Animation clip '{key}' has invalid blendInputs.{axisLabel} '{value}'.");
            }

            return input;
        }

        private static void ValidateUniqueBackends(string key, AnimationClipLocatorDefinition[] locators)
        {
            for (int i = 0; i < locators.Length; i++)
            {
                for (int j = i + 1; j < locators.Length; j++)
                {
                    if (string.Equals(locators[i].BackendId, locators[j].BackendId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Animation clip '{key}' defines duplicate locator backend '{locators[i].BackendId}'.");
                    }
                }
            }
        }
    }
}
