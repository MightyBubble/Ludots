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
        private readonly PrefabRegistry? _prefabRegistry;
        private readonly bool _loadPrefabs;

        public MeshAssetConfigLoader(ConfigPipeline configs, MeshAssetRegistry meshRegistry)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _meshRegistry = meshRegistry ?? throw new ArgumentNullException(nameof(meshRegistry));
            _prefabRegistry = null;
            _loadPrefabs = false;
        }

        public MeshAssetConfigLoader(ConfigPipeline configs, MeshAssetRegistry meshRegistry, PrefabRegistry prefabRegistry)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _meshRegistry = meshRegistry ?? throw new ArgumentNullException(nameof(meshRegistry));
            _prefabRegistry = prefabRegistry ?? throw new ArgumentNullException(nameof(prefabRegistry));
            _loadPrefabs = true;
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            LoadMeshAssets(catalog, report);
            if (_loadPrefabs)
            {
                LoadPrefabs(catalog, report);
            }
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

        private void LoadPrefabs(ConfigCatalog catalog, ConfigConflictReport report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Presentation/prefabs.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string prefabKey = node["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(prefabKey))
                {
                    throw new InvalidOperationException("Presentation/prefabs.json entry is missing required 'id'.");
                }

                string meshRef = node["meshAssetId"]?.GetValue<string>();
                int meshAssetId = string.IsNullOrWhiteSpace(meshRef) ? 0 : _meshRegistry.GetId(meshRef);

                var parts = ParseParts(node["parts"]);

                if (parts.Length > 0 && meshAssetId == 0 && parts.Length == 1)
                    meshAssetId = parts[0].MeshAssetId;

                int prefabId = _meshRegistry.GetId(prefabKey);
                if (prefabId == 0)
                {
                    var prefabDesc = parts.Length > 0
                        ? MeshAssetDescriptor.Prefab(0, parts)
                        : MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.None);
                    prefabId = _meshRegistry.Register(prefabKey, in prefabDesc);
                }

                _prefabRegistry!.Register(prefabKey, new PrefabDefinition
                {
                    MeshAssetId = meshAssetId > 0 ? meshAssetId : prefabId,
                    BaseScale = node["baseScale"]?.GetValue<float>() ?? 1f,
                });
            }
        }

        private MeshAssetDescriptor ParseDescriptor(JsonNode node, string key)
        {
            string typeStr = node["type"]?.GetValue<string>();
            if (!Enum.TryParse<MeshAssetType>(typeStr, ignoreCase: true, out var type))
            {
                throw new InvalidOperationException($"Presentation/mesh_assets.json asset '{key}' has invalid or missing type '{typeStr}'.");
            }

            switch (type)
            {
                case MeshAssetType.Primitive:
                {
                    string kindStr = node["primitiveKind"]?.GetValue<string>();
                    if (!Enum.TryParse<PrimitiveMeshKind>(kindStr, ignoreCase: true, out var kind))
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
                case MeshAssetType.Prefab:
                {
                    var parts = ParseParts(node["parts"]);
                    return MeshAssetDescriptor.Prefab(0, parts);
                }
                default:
                    throw new InvalidOperationException($"Presentation/mesh_assets.json asset '{key}' uses unsupported mesh asset type '{type}'.");
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
                string? kindText = p?["kind"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(kindText))
                {
                    throw new InvalidOperationException($"Prefab part at index {j} must declare an explicit kind.");
                }

                if (!Enum.TryParse(kindText, ignoreCase: true, out PrefabVisualPartKind kind))
                {
                    throw new InvalidOperationException($"Prefab part has invalid kind '{kindText}'.");
                }

                string meshRef = p?["meshAssetId"]?.GetValue<string>();
                int meshId = 0;
                if (!string.IsNullOrWhiteSpace(meshRef))
                    meshId = _meshRegistry.GetId(meshRef);

                PrefabPart part = kind switch
                {
                    PrefabVisualPartKind.Decal => PrefabPart.Decal(
                        p?["materialId"]?.GetValue<int>() ?? 0,
                        ParseVector2WithDefault(p?["size"], Vector2.One)),
                    PrefabVisualPartKind.Vfx => PrefabPart.Vfx(
                        p?["effectAssetId"]?.GetValue<int>() ?? 0,
                        ParseSpawnMode(p?["spawnMode"]?.GetValue<string>())),
                    PrefabVisualPartKind.Surface => PrefabPart.Surface(
                        meshId,
                        p?["materialId"]?.GetValue<int>() ?? 0,
                        ParseVector2WithDefault(p?["tiling"], Vector2.One)),
                    _ => PrefabPart.Default(meshId),
                };

                part.MeshAssetId = meshId;
                part.LocalPosition = ParseVector3(p?["localPosition"]);
                part.LocalRotation = ParseQuaternionWithDefault(p?["localRotation"], Quaternion.Identity);
                part.LocalScale = ParseVector3WithDefault(p?["localScale"], Vector3.One);
                part.ColorTint = ParseVector4WithDefault(p?["colorTint"], Vector4.One);
                part.Grounding = ParseGrounding(p?["grounding"]);
                part.MaterialId = p?["materialId"]?.GetValue<int>() ?? part.MaterialId;
                part.EffectAssetId = p?["effectAssetId"]?.GetValue<int>() ?? part.EffectAssetId;
                part.Size = ParseVector2WithDefault(p?["size"], part.Size == Vector2.Zero ? Vector2.One : part.Size);
                part.Tiling = ParseVector2WithDefault(p?["tiling"], part.Tiling == Vector2.Zero ? Vector2.One : part.Tiling);
                part.AlignToSurface = p?["alignToSurface"]?.GetValue<bool>() ?? part.AlignToSurface;
                part.TerrainFacing = p?["terrainFacing"]?.GetValue<bool>() ?? part.TerrainFacing;

                parts[j] = part;
            }
            return parts;
        }

        private static PrefabVfxSpawnMode ParseSpawnMode(string? spawnModeText)
        {
            string resolved = string.IsNullOrWhiteSpace(spawnModeText)
                ? nameof(PrefabVfxSpawnMode.Once)
                : spawnModeText;
            if (!Enum.TryParse(resolved, ignoreCase: true, out PrefabVfxSpawnMode spawnMode))
            {
                throw new InvalidOperationException($"Prefab part VFX spawnMode has invalid value '{resolved}'.");
            }

            return spawnMode;
        }

        private static PrefabPartGrounding ParseGrounding(JsonNode node)
        {
            if (node == null)
            {
                return PrefabPartGrounding.None;
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Prefab part grounding must be an object.");
            }

            string modeText = obj["mode"]?.GetValue<string>() ?? nameof(PrefabPartGroundingMode.None);
            if (!Enum.TryParse(modeText, ignoreCase: true, out PrefabPartGroundingMode mode))
            {
                throw new InvalidOperationException($"Prefab part grounding has invalid mode '{modeText}'.");
            }

            int layerIndex = obj["layerIndex"]?.GetValue<int>() ?? 0;
            if (layerIndex < 0)
            {
                throw new InvalidOperationException("Prefab part grounding layerIndex cannot be negative.");
            }

            return new PrefabPartGrounding(
                mode,
                obj["verticalOffsetMeters"]?.GetValue<float>() ?? 0f,
                obj["alignToGroundNormal"]?.GetValue<bool>() ?? false,
                layerIndex);
        }

        private static Vector3 ParseVector3(JsonNode node)
        {
            if (node is JsonArray arr && arr.Count >= 3)
                return new Vector3(arr[0]?.GetValue<float>() ?? 0f, arr[1]?.GetValue<float>() ?? 0f, arr[2]?.GetValue<float>() ?? 0f);
            return Vector3.Zero;
        }

        private static Vector2 ParseVector2WithDefault(JsonNode node, Vector2 defaultValue)
        {
            if (node is JsonArray arr && arr.Count >= 2)
            {
                return new Vector2(
                    arr[0]?.GetValue<float>() ?? defaultValue.X,
                    arr[1]?.GetValue<float>() ?? defaultValue.Y);
            }

            return defaultValue;
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

        private static Quaternion ParseQuaternionWithDefault(JsonNode node, Quaternion defaultValue)
        {
            if (node is JsonArray arr && arr.Count >= 4)
            {
                return new Quaternion(
                    arr[0]?.GetValue<float>() ?? defaultValue.X,
                    arr[1]?.GetValue<float>() ?? defaultValue.Y,
                    arr[2]?.GetValue<float>() ?? defaultValue.Z,
                    arr[3]?.GetValue<float>() ?? defaultValue.W);
            }

            if (node is JsonObject obj)
            {
                return new Quaternion(
                    obj["x"]?.GetValue<float>() ?? obj["X"]?.GetValue<float>() ?? defaultValue.X,
                    obj["y"]?.GetValue<float>() ?? obj["Y"]?.GetValue<float>() ?? defaultValue.Y,
                    obj["z"]?.GetValue<float>() ?? obj["Z"]?.GetValue<float>() ?? defaultValue.Z,
                    obj["w"]?.GetValue<float>() ?? obj["W"]?.GetValue<float>() ?? defaultValue.W);
            }

            return defaultValue;
        }
    }
}
