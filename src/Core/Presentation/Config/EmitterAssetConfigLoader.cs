using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Config
{
    public sealed class EmitterAssetConfigLoader
    {
        public const string DefaultRelativePath = "Presentation/emitter_assets.json";

        private readonly ConfigPipeline _configs;
        private readonly EmitterAssetRegistry _emitters;

        public EmitterAssetConfigLoader(ConfigPipeline configs, EmitterAssetRegistry emitters)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _emitters = emitters ?? throw new ArgumentNullException(nameof(emitters));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                catalog,
                DefaultRelativePath,
                ConfigMergePolicy.ArrayById,
                "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"{DefaultRelativePath} entry '{merged[i].Id}' must merge to a JSON object.");
                }

                ValidateFields(obj, merged[i].Id);
                string key = RequireCanonicalString(obj["id"], $"{DefaultRelativePath} entry id");
                AssetKind assetKind = ParseEmitterKind(obj["assetKind"], key);
                int runtimeFormatVersion = ParseRuntimeFormatVersion(obj["runtimeFormatVersion"], key);
                string sha256 = ParseSha256(obj["sha256"], key);
                _emitters.Register(key, assetKind, runtimeFormatVersion, sha256);
            }
        }

        private static int ParseRuntimeFormatVersion(JsonNode? node, string key)
        {
            if (node is not JsonValue value || !value.TryGetValue(out int runtimeFormatVersion))
            {
                throw new InvalidOperationException(
                    $"Emitter asset '{key}' field 'runtimeFormatVersion' must be integer {EmitterAssetDescriptor.RequiredRuntimeFormatVersion}.");
            }

            if (runtimeFormatVersion != EmitterAssetDescriptor.RequiredRuntimeFormatVersion)
            {
                throw new InvalidOperationException(
                    $"Emitter asset '{key}' has runtimeFormatVersion {runtimeFormatVersion}; required version is {EmitterAssetDescriptor.RequiredRuntimeFormatVersion}.");
            }

            return runtimeFormatVersion;
        }

        private static string ParseSha256(JsonNode? node, string key)
        {
            string value = RequireCanonicalString(node, $"Emitter asset '{key}' field 'sha256'");
            try
            {
                return EmitterAssetDescriptor.NormalizeSha256(value);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Emitter asset '{key}' field 'sha256' is invalid: {ex.Message}",
                    ex);
            }
        }

        private static AssetKind ParseEmitterKind(JsonNode? node, string key)
        {
            string value = RequireCanonicalString(
                node,
                $"Emitter asset '{key}' field 'assetKind'");
            AssetKind assetKind = value switch
            {
                nameof(AssetKind.SpriteEmitter) => AssetKind.SpriteEmitter,
                nameof(AssetKind.RibbonEmitter) => AssetKind.RibbonEmitter,
                nameof(AssetKind.ModelEmitter) => AssetKind.ModelEmitter,
                nameof(AssetKind.TrackEmitter) => AssetKind.TrackEmitter,
                nameof(AssetKind.RingEmitter) => AssetKind.RingEmitter,
                _ => throw new InvalidOperationException(
                    $"Emitter asset '{key}' has invalid concrete emitter assetKind '{value}'."),
            };

            return assetKind;
        }

        private static string RequireCanonicalString(JsonNode? node, string label)
        {
            string value = node?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{label} must be a non-empty string.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} must not include leading or trailing whitespace.");
            }

            return value;
        }

        private static void ValidateFields(JsonObject obj, string rowId)
        {
            foreach ((string field, _) in obj)
            {
                if (field is not ("id" or "assetKind" or "runtimeFormatVersion" or "sha256"))
                {
                    throw new InvalidOperationException(
                        $"Emitter asset '{rowId}' uses unsupported field '{field}'. Expected exact fields id, assetKind, runtimeFormatVersion, and sha256.");
                }
            }
        }
    }
}
