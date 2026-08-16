using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationHostAssetConfigLoader
    {
        public const string DefaultRelativePath = "Presentation/host_assets.json";

        private readonly ConfigPipeline _configs;
        private readonly MeshAssetRegistry _meshRegistry;
        private readonly PresentationMaterialRegistry _materialRegistry;

        public PresentationHostAssetConfigLoader(
            ConfigPipeline configs,
            MeshAssetRegistry meshRegistry,
            PresentationMaterialRegistry materialRegistry)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _meshRegistry = meshRegistry ?? throw new ArgumentNullException(nameof(meshRegistry));
            _materialRegistry = materialRegistry ?? throw new ArgumentNullException(nameof(materialRegistry));
        }

        public void Apply(string backendId, ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            if (string.IsNullOrWhiteSpace(backendId))
            {
                throw new ArgumentException("Host asset backendId must not be empty.", nameof(backendId));
            }

            RequireNoBoundaryWhitespace(backendId, "Host asset backendId");
            var entry = ConfigPipeline.RequireEntry(catalog, DefaultRelativePath, ConfigMergePolicy.ArrayById, "id");
            var fragments = PresentationAssetConfigIdGuard.CollectUniqueArrayByIdFragments(_configs, in entry);
            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                JsonNode node = merged[i].Node;
                string rowBackendId = RequireString(node, "backendId", "host asset row");
                if (!string.Equals(rowBackendId, backendId, StringComparison.Ordinal))
                {
                    continue;
                }

                string assetKind = RequireString(node, "assetKind", rowBackendId);
                if (string.Equals(assetKind, "Mesh", StringComparison.Ordinal))
                {
                    ApplyMeshHostAsset(node, backendId);
                    continue;
                }

                if (string.Equals(assetKind, "Material", StringComparison.Ordinal))
                {
                    ApplyMaterialHostAsset(node, backendId);
                    continue;
                }

                throw new InvalidOperationException(
                    $"Presentation host asset '{RequireString(node, "id", "host asset row")}' has unsupported assetKind '{assetKind}'.");
            }
        }

        private void ApplyMeshHostAsset(JsonNode node, string backendId)
        {
            string rowId = RequireString(node, "id", "host asset row");
            string assetId = RequireString(node, "assetId", rowId);
            int meshAssetId = _meshRegistry.GetId(assetId);
            if (meshAssetId == 0 || !_meshRegistry.TryGetDescriptor(meshAssetId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"Presentation host asset '{rowId}' targets unknown mesh asset '{assetId}' for backend '{backendId}'.");
            }

            if (descriptor.Type != MeshAssetType.Model && descriptor.Type != MeshAssetType.Billboard)
            {
                throw new InvalidOperationException(
                    $"Presentation host asset '{rowId}' targets mesh asset '{assetId}' with type '{descriptor.Type}'. Only Model and Billboard require host sourceUris.");
            }

            descriptor.SourceUris = ParseSourceUris(node["sourceUris"], rowId);
            _meshRegistry.Register(assetId, in descriptor);
        }

        private void ApplyMaterialHostAsset(JsonNode node, string backendId)
        {
            string rowId = RequireString(node, "id", "host asset row");
            string assetId = RequireString(node, "assetId", rowId);
            int materialAssetId = _materialRegistry.GetId(assetId);
            if (materialAssetId == 0 || !_materialRegistry.TryGet(materialAssetId, out MaterialAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"Presentation host asset '{rowId}' targets unknown material asset '{assetId}' for backend '{backendId}'.");
            }

            _materialRegistry.Register(assetId, descriptor.Domain, ParseSourceUris(node["sourceUris"], rowId), descriptor.Flags);
        }

        private static string[] ParseSourceUris(JsonNode node, string rowId)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Presentation host asset '{rowId}' must declare a non-empty sourceUris array.");
            }

            var uris = new string[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                string uri = arr[i]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(uri))
                {
                    throw new InvalidOperationException(
                        $"Presentation host asset '{rowId}' has an empty sourceUris entry at index {i}.");
                }

                RequireNoBoundaryWhitespace(uri, $"Presentation host asset '{rowId}' sourceUris[{i}]");
                uris[i] = uri;
            }

            return uris;
        }

        private static string RequireString(JsonNode node, string fieldName, string rowLabel)
        {
            string value = node[fieldName]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Presentation host asset '{rowLabel}' must declare '{fieldName}'.");
            }

            RequireNoBoundaryWhitespace(value, $"Presentation host asset '{rowLabel}' field '{fieldName}'");
            return value;
        }

        private static void RequireNoBoundaryWhitespace(string value, string label)
        {
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} must not include leading or trailing whitespace.");
            }
        }
    }
}
