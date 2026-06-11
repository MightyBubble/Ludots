using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationHostAssetConfigLoader
    {
        public const string DefaultRelativePath = "Presentation/host_assets.json";

        private readonly ConfigPipeline _configs;
        private readonly PresentationMaterialRegistry _materials;
        private readonly string _backendId;

        public PresentationHostAssetConfigLoader(
            ConfigPipeline configs,
            PresentationMaterialRegistry materials,
            string backendId)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
            if (string.IsNullOrWhiteSpace(backendId))
            {
                throw new ArgumentException("Presentation backend id must not be empty.", nameof(backendId));
            }

            _backendId = backendId;
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, DefaultRelativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                JsonNode node = merged[i].Node;
                string rowId = PresentationMaterialConfigLoader.RequireString(node, "id", "host asset row");
                string backendId = PresentationMaterialConfigLoader.RequireString(node, "backendId", $"host asset '{rowId}'");
                if (!string.Equals(backendId, _backendId, StringComparison.Ordinal))
                {
                    continue;
                }

                string assetKind = PresentationMaterialConfigLoader.RequireString(node, "assetKind", $"host asset '{rowId}'");
                if (!string.Equals(assetKind, "Material", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Presentation host asset '{rowId}' has unsupported assetKind '{assetKind}'.");
                }

                ApplyMaterialHostAsset(node, rowId);
            }
        }

        private void ApplyMaterialHostAsset(JsonNode node, string rowId)
        {
            string assetId = PresentationMaterialConfigLoader.RequireString(node, "assetId", $"host material asset '{rowId}'");
            int materialId = _materials.GetId(assetId);
            if (materialId <= 0 || !_materials.TryGet(materialId, out MaterialAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"Presentation host asset '{rowId}' targets unknown material asset '{assetId}' for backend '{_backendId}'.");
            }

            _materials.Register(assetId, descriptor.Domain, ParseSourceUris(node["sourceUris"], rowId), descriptor.Flags);
        }

        private static string[] ParseSourceUris(JsonNode node, string rowId)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                throw new InvalidOperationException($"Presentation host material asset '{rowId}' requires non-empty array field 'sourceUris'.");
            }

            var uris = new string[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                string uri = arr[i]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(uri))
                {
                    throw new InvalidOperationException($"Presentation host material asset '{rowId}'.sourceUris[{i}] must be non-empty.");
                }

                uris[i] = uri;
            }

            return uris;
        }
    }
}
