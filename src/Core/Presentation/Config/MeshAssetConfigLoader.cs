using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Config
{
    public sealed class MeshAssetConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly MeshAssetRegistry _meshRegistry;

        public MeshAssetConfigLoader(ConfigPipeline configs, MeshAssetRegistry meshRegistry)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _meshRegistry = meshRegistry ?? throw new ArgumentNullException(nameof(meshRegistry));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            LoadMeshAssets(catalog, report);
        }

        private void LoadMeshAssets(ConfigCatalog catalog, ConfigConflictReport report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Presentation/mesh_assets.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string key = node["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("Presentation/mesh_assets.json entry is missing required 'id'.");
                }

                var desc = ParseDescriptor(node, key);
                if (desc.Type == MeshAssetType.None)
                {
                    throw new InvalidOperationException($"Presentation/mesh_assets.json asset '{key}' resolved to MeshAssetType.None.");
                }

                _meshRegistry.Register(key, in desc);
            }
        }

        private static MeshAssetDescriptor ParseDescriptor(JsonNode node, string key)
        {
            string typeStr = node["type"]?.GetValue<string>();
            if (string.Equals(typeStr, "Prefab", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Presentation/mesh_assets.json asset '{key}' uses type Prefab. Author a Presenter with AssetBinding children instead.");
            }

            if (!Enum.TryParse<MeshAssetType>(typeStr, ignoreCase: false, out var type))
            {
                throw new InvalidOperationException($"Presentation/mesh_assets.json asset '{key}' has invalid or missing type '{typeStr}'.");
            }

            switch (type)
            {
                case MeshAssetType.Primitive:
                {
                    string kindStr = node["primitiveKind"]?.GetValue<string>();
                    if (!Enum.TryParse<PrimitiveMeshKind>(kindStr, ignoreCase: false, out var kind))
                    {
                        throw new InvalidOperationException($"Presentation/mesh_assets.json primitive asset '{key}' has invalid or missing primitiveKind '{kindStr}'.");
                    }

                    return MeshAssetDescriptor.Primitive(0, kind);
                }
                case MeshAssetType.Model:
                case MeshAssetType.Billboard:
                {
                    if (node["sourceUris"] != null)
                    {
                        throw new InvalidOperationException(
                            $"Presentation/mesh_assets.json asset '{key}' declares sourceUris. Platform paths belong in Presentation/host_assets.json.");
                    }

                    return type == MeshAssetType.Billboard
                        ? MeshAssetDescriptor.Billboard(0, Array.Empty<string>())
                        : MeshAssetDescriptor.Model(0, Array.Empty<string>());
                }
                default:
                    throw new InvalidOperationException($"Presentation/mesh_assets.json asset '{key}' uses unsupported mesh asset type '{type}'.");
            }
        }
    }
}
