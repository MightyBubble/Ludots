using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;

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

                _prefabRegistry.Register(prefabKey, new PrefabDefinition
                {
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
                case MeshAssetType.Billboard:
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
                    return type == MeshAssetType.Billboard
                        ? MeshAssetDescriptor.Billboard(0, uris)
                        : MeshAssetDescriptor.Model(0, uris);
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
                parts[j] = ParsePart(p, j);
            }
            return parts;
        }

        private PrefabPart ParsePart(JsonNode node, int index)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"Prefab part at index {index} must be an object.");
            }

            string kindText = obj["kind"]?.GetValue<string>() ?? nameof(PrefabPartKind.Mesh);
            if (!Enum.TryParse(kindText, ignoreCase: true, out PrefabPartKind kind))
            {
                throw new InvalidOperationException($"Prefab part at index {index} has invalid kind '{kindText}'.");
            }

            var part = new PrefabPart
            {
                Kind = kind,
                LocalPosition = ParseVector3(obj["localPosition"]),
                LocalRotation = ParseQuaternionWithDefault(obj["localRotation"], Quaternion.Identity),
                LocalScale = ParseVector3WithDefault(obj["localScale"], Vector3.One),
                ColorTint = ParseVector4WithDefault(obj["colorTint"], Vector4.One),
                Grounding = ParseGrounding(obj["grounding"]),
                Payload = PresentationPayloadConfigParser.ParsePayload(obj["payload"], $"Prefab part[{index}]"),
                AssetKey = ReadString(obj, "assetKey", "asset"),
                MaterialKey = ReadString(obj, "materialKey", "material"),
                SurfaceLayerKey = ReadString(obj, "surfaceLayerKey", "surfaceLayer"),
            };

            switch (kind)
            {
                case PrefabPartKind.Mesh:
                    string meshRef = obj["meshAssetId"]?.GetValue<string>() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(meshRef))
                    {
                        throw new InvalidOperationException($"Prefab mesh part at index {index} requires a non-empty meshAssetId.");
                    }

                    int meshId = _meshRegistry.GetId(meshRef);
                    if (meshId <= 0)
                    {
                        throw new InvalidOperationException($"Prefab mesh part at index {index} references unknown meshAssetId '{meshRef}'.");
                    }

                    part.MeshAssetId = meshId;
                    break;

                case PrefabPartKind.Decal:
                case PrefabPartKind.Vfx:
                    if (string.IsNullOrWhiteSpace(part.AssetKey))
                    {
                        throw new InvalidOperationException($"Prefab {kind} part at index {index} requires a non-empty assetKey.");
                    }
                    break;

                case PrefabPartKind.Surface:
                    if (string.IsNullOrWhiteSpace(part.SurfaceLayerKey))
                    {
                        part.SurfaceLayerKey = part.AssetKey;
                    }

                    if (string.IsNullOrWhiteSpace(part.SurfaceLayerKey))
                    {
                        throw new InvalidOperationException($"Prefab Surface part at index {index} requires a non-empty surfaceLayerKey.");
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Prefab part at index {index} has unsupported kind '{kind}'.");
            }

            return part;
        }

        private static string ReadString(JsonObject obj, string primary, string alternate)
            => obj[primary]?.GetValue<string>() ?? obj[alternate]?.GetValue<string>() ?? string.Empty;

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
