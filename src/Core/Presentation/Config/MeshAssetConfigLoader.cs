using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Config
{
    public sealed class MeshAssetConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly MeshAssetRegistry _meshRegistry;
        private readonly PrefabRegistry _prefabRegistry;

        public MeshAssetConfigLoader(ConfigPipeline configs, MeshAssetRegistry meshRegistry, PrefabRegistry prefabRegistry)
        {
            _configs = configs;
            _meshRegistry = meshRegistry;
            _prefabRegistry = prefabRegistry;
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            LoadMeshAssets(catalog, report);
            LoadPrefabs(catalog, report);
        }

        private void LoadMeshAssets(ConfigCatalog catalog, ConfigConflictReport report)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/mesh_assets.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string key = node["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(key)) continue;

                var desc = ParseDescriptor(node);
                if (desc.Type == MeshAssetType.None) continue;

                _meshRegistry.Register(key, in desc);
            }
        }

        private void LoadPrefabs(ConfigCatalog catalog, ConfigConflictReport report)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/prefabs.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string prefabKey = node["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(prefabKey)) continue;

                string meshRef = node["meshAssetId"]?.GetValue<string>();
                int meshAssetId = string.IsNullOrWhiteSpace(meshRef) ? 0 : _meshRegistry.ResolveIdOrZero(meshRef);

                var parts = ParseParts(node["parts"]);

                if (parts.Length > 0 && meshAssetId == 0 && parts.Length == 1)
                    meshAssetId = parts[0].MeshAssetId;

                int prefabId = _meshRegistry.ResolveIdOrZero(prefabKey);
                if (prefabId == 0)
                {
                    var prefabDesc = parts.Length > 0
                        ? MeshAssetDescriptor.Prefab(0, parts)
                        : MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.None);
                    prefabId = _meshRegistry.Register(prefabKey, in prefabDesc);
                }

                _prefabRegistry.Register(new PrefabDefinition
                {
                    PrefabId = prefabId,
                    MeshAssetId = meshAssetId > 0 ? meshAssetId : prefabId,
                    BaseScale = node["baseScale"]?.GetValue<float>() ?? 1f,
                });
            }
        }

        private MeshAssetDescriptor ParseDescriptor(JsonNode node)
        {
            string typeStr = node["type"]?.GetValue<string>();
            if (!Enum.TryParse<MeshAssetType>(typeStr, ignoreCase: true, out var type))
                return default;

            switch (type)
            {
                case MeshAssetType.Primitive:
                {
                    string kindStr = node["primitiveKind"]?.GetValue<string>();
                    Enum.TryParse<PrimitiveMeshKind>(kindStr, ignoreCase: true, out var kind);
                    return MeshAssetDescriptor.Primitive(0, kind);
                }
                case MeshAssetType.Model:
                {
                    var urisNode = node["sourceUris"];
                    string[] uris;
                    if (urisNode is JsonArray arr)
                    {
                        uris = new string[arr.Count];
                        for (int j = 0; j < arr.Count; j++)
                            uris[j] = arr[j]?.GetValue<string>() ?? string.Empty;
                    }
                    else
                    {
                        string single = urisNode?.GetValue<string>();
                        uris = string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
                    }
                    return MeshAssetDescriptor.Model(0, uris);
                }
                case MeshAssetType.Prefab:
                {
                    var parts = ParseParts(node["parts"]);
                    return MeshAssetDescriptor.Prefab(0, parts);
                }
                default:
                    return default;
            }
        }

        private PrefabPart[] ParseParts(JsonNode partsNode)
        {
            if (partsNode is not JsonArray arr || arr.Count == 0)
                return Array.Empty<PrefabPart>();

            var parts = new PrefabPart[arr.Count];
            for (int j = 0; j < arr.Count; j++)
            {
                var p = arr[j];
                string meshRef = p?["meshAssetId"]?.GetValue<string>();
                int meshId = 0;
                if (!string.IsNullOrWhiteSpace(meshRef))
                    meshId = _meshRegistry.ResolveIdOrZero(meshRef);
                if (meshId == 0 && int.TryParse(meshRef, out int numericId))
                    meshId = numericId;

                parts[j] = new PrefabPart
                {
                    MeshAssetId = meshId,
                    LocalPosition = ParseVector3(p?["localPosition"]),
                    LocalScale = ParseVector3WithDefault(p?["localScale"], Vector3.One),
                    ColorTint = ParseVector4WithDefault(p?["colorTint"], Vector4.One),
                };
            }
            return parts;
        }

        private static Vector3 ParseVector3(JsonNode node)
        {
            if (node is JsonArray arr && arr.Count >= 3)
                return new Vector3(arr[0]?.GetValue<float>() ?? 0f, arr[1]?.GetValue<float>() ?? 0f, arr[2]?.GetValue<float>() ?? 0f);
            return Vector3.Zero;
        }

        private static Vector3 ParseVector3WithDefault(JsonNode node, Vector3 defaultValue)
        {
            if (node is JsonArray arr && arr.Count >= 3)
                return new Vector3(arr[0]?.GetValue<float>() ?? defaultValue.X, arr[1]?.GetValue<float>() ?? defaultValue.Y, arr[2]?.GetValue<float>() ?? defaultValue.Z);
            return defaultValue;
        }

        private static Vector4 ParseVector4WithDefault(JsonNode node, Vector4 defaultValue)
        {
            if (node is JsonArray arr && arr.Count >= 4)
                return new Vector4(arr[0]?.GetValue<float>() ?? defaultValue.X, arr[1]?.GetValue<float>() ?? defaultValue.Y, arr[2]?.GetValue<float>() ?? defaultValue.Z, arr[3]?.GetValue<float>() ?? defaultValue.W);
            return defaultValue;
        }
    }
}
