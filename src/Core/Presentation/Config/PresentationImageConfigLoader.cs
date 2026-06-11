using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationImageConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly PresentationImageRegistry _images;

        public PresentationImageConfigLoader(
            ConfigPipeline configs,
            PresentationImageRegistry images)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _images = images ?? throw new ArgumentNullException(nameof(images));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/image_assets.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                JsonObject node = merged[i].Node;
                string key = node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("Presentation image asset is missing required 'id'.");
                }

                _images.Register(key, ParseDefinition(node, key));
            }
        }

        private static PresentationImageDefinition ParseDefinition(JsonObject node, string key)
        {
            string assetKindText = node["assetKind"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(assetKindText))
            {
                throw new InvalidOperationException($"Presentation image asset '{key}' is missing required 'assetKind'.");
            }

            if (!Enum.TryParse(assetKindText, ignoreCase: false, out PresentationImageAssetKind assetKind))
            {
                throw new InvalidOperationException($"Presentation image asset '{key}' has invalid assetKind '{assetKindText}'. Enum values are case-sensitive.");
            }

            if (node["locators"] is not JsonArray locatorsArray || locatorsArray.Count == 0)
            {
                throw new InvalidOperationException($"Presentation image asset '{key}' must define at least one locator.");
            }

            var locators = new PresentationImageLocatorDefinition[locatorsArray.Count];
            for (int i = 0; i < locatorsArray.Count; i++)
            {
                if (locatorsArray[i] is not JsonObject locatorNode)
                {
                    throw new InvalidOperationException($"Presentation image asset '{key}' locator[{i}] must be an object.");
                }

                string backendId = locatorNode["backendId"]?.GetValue<string>() ?? string.Empty;
                string assetRef = locatorNode["assetRef"]?.GetValue<string>() ?? string.Empty;
                locators[i] = new PresentationImageLocatorDefinition(backendId, assetRef);
            }

            ValidateUniqueBackends(key, locators);

            RejectRemovedGeneratedImageField(node, key, "fallbackGlyph");
            RejectRemovedGeneratedImageField(node, key, "fallbackAccentColorHex");
            RejectRemovedGeneratedImageField(node, key, "fallbackSurfaceColorHex");

            return new PresentationImageDefinition
            {
                AssetKind = assetKind,
                Locators = locators,
            };
        }

        private static string? ReadOptionalString(JsonObject node, string propertyName)
        {
            string value = node[propertyName]?.GetValue<string>() ?? string.Empty;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void RejectRemovedGeneratedImageField(JsonObject node, string key, string propertyName)
        {
            if (node[propertyName] is not null)
            {
                throw new InvalidOperationException(
                    $"Presentation image asset '{key}' uses removed field '{propertyName}'. Define only backend locators.");
            }
        }

        private static void ValidateUniqueBackends(string key, PresentationImageLocatorDefinition[] locators)
        {
            for (int i = 0; i < locators.Length; i++)
            {
                for (int j = i + 1; j < locators.Length; j++)
                {
                    if (string.Equals(locators[i].BackendId, locators[j].BackendId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Presentation image asset '{key}' defines duplicate locator backend '{locators[i].BackendId}'.");
                    }
                }
            }
        }
    }
}
