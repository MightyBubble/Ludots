using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Particles;

namespace Ludots.Core.Presentation.Config
{
    public sealed class MeshAssetConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly MeshAssetRegistry _meshRegistry;
        private readonly ParticleVfxRegistry? _particleVfxRegistry;
        private readonly PrefabRegistry? _prefabRegistry;
        private readonly bool _loadPrefabs;

        public MeshAssetConfigLoader(ConfigPipeline configs, MeshAssetRegistry meshRegistry)
            : this(configs, meshRegistry, prefabRegistry: null, particleVfxRegistry: null, loadPrefabs: false)
        {
        }

        public MeshAssetConfigLoader(
            ConfigPipeline configs,
            MeshAssetRegistry meshRegistry,
            ParticleVfxRegistry particleVfxRegistry)
            : this(configs, meshRegistry, prefabRegistry: null, particleVfxRegistry, loadPrefabs: false)
        {
        }

        public MeshAssetConfigLoader(ConfigPipeline configs, MeshAssetRegistry meshRegistry, PrefabRegistry prefabRegistry)
            : this(configs, meshRegistry, prefabRegistry, particleVfxRegistry: null, loadPrefabs: true)
        {
        }

        public MeshAssetConfigLoader(
            ConfigPipeline configs,
            MeshAssetRegistry meshRegistry,
            PrefabRegistry prefabRegistry,
            ParticleVfxRegistry particleVfxRegistry)
            : this(configs, meshRegistry, prefabRegistry, particleVfxRegistry, loadPrefabs: true)
        {
        }

        private MeshAssetConfigLoader(
            ConfigPipeline configs,
            MeshAssetRegistry meshRegistry,
            PrefabRegistry? prefabRegistry,
            ParticleVfxRegistry? particleVfxRegistry,
            bool loadPrefabs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _meshRegistry = meshRegistry ?? throw new ArgumentNullException(nameof(meshRegistry));
            _prefabRegistry = prefabRegistry;
            _particleVfxRegistry = particleVfxRegistry;
            _loadPrefabs = loadPrefabs;
            if (_loadPrefabs && _prefabRegistry == null)
            {
                throw new ArgumentNullException(nameof(prefabRegistry));
            }
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
            MeshAssetType type = ReadRequiredEnum<MeshAssetType>(
                node["type"],
                $"Presentation/mesh_assets.json asset '{key}' type");
            if (type == MeshAssetType.None)
            {
                throw new InvalidOperationException($"Presentation/mesh_assets.json asset '{key}' type must not be None.");
            }

            switch (type)
            {
                case MeshAssetType.Primitive:
                {
                    PrimitiveMeshKind kind = ReadRequiredEnum<PrimitiveMeshKind>(
                        node["primitiveKind"],
                        $"Presentation/mesh_assets.json primitive asset '{key}' primitiveKind");
                    if (kind == PrimitiveMeshKind.None)
                    {
                        throw new InvalidOperationException(
                            $"Presentation/mesh_assets.json primitive asset '{key}' primitiveKind must not be None.");
                    }

                    var descriptor = MeshAssetDescriptor.Primitive(0, kind);
                    descriptor.VfxData = ParseVfxData(node["vfx"], key);
                    return descriptor;
                }
                case MeshAssetType.Model:
                case MeshAssetType.Billboard:
                {
                    if (node["sourceUris"] != null)
                    {
                        throw new InvalidOperationException(
                            $"Presentation/mesh_assets.json asset '{key}' declares sourceUris. Platform paths belong in Presentation/host_assets.json.");
                    }

                    var descriptor = type == MeshAssetType.Billboard
                        ? MeshAssetDescriptor.Billboard(0, Array.Empty<string>())
                        : MeshAssetDescriptor.Model(0, Array.Empty<string>());
                    descriptor.VfxData = ParseVfxData(node["vfx"], key);
                    return descriptor;
                }
                case MeshAssetType.Prefab:
                {
                    var parts = ParseParts(node["parts"]);
                    var descriptor = MeshAssetDescriptor.Prefab(0, parts);
                    descriptor.VfxData = ParseVfxData(node["vfx"], key);
                    return descriptor;
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

                PrefabVisualPartKind kind = ParseRequiredEnumText<PrefabVisualPartKind>(
                    kindText,
                    $"Prefab part at index {j} kind");

                string meshRef = p?["meshAssetId"]?.GetValue<string>();
                int meshId = 0;
                if (!string.IsNullOrWhiteSpace(meshRef))
                    meshId = _meshRegistry.GetId(meshRef);

                PrefabPart part = kind switch
                {
                    PrefabVisualPartKind.Decal => PrefabPart.Decal(
                        p?["materialId"]?.GetValue<int>() ?? 0,
                        ParseVector2WithDefault(p?["size"], Vector2.One)),
                    PrefabVisualPartKind.Vfx => CreateVfxPart(p, j),
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
                part.Size = ParseVector2WithDefault(p?["size"], part.Size == Vector2.Zero ? Vector2.One : part.Size);
                part.Tiling = ParseVector2WithDefault(p?["tiling"], part.Tiling == Vector2.Zero ? Vector2.One : part.Tiling);
                part.AlignToSurface = p?["alignToSurface"]?.GetValue<bool>() ?? part.AlignToSurface;
                part.TerrainFacing = p?["terrainFacing"]?.GetValue<bool>() ?? part.TerrainFacing;

                parts[j] = part;
            }
            return parts;
        }

        private PrefabPart CreateVfxPart(JsonNode? node, int partIndex)
        {
            string partLabel = $"Prefab part at index {partIndex}";
            int effectAssetId = ResolveEffectAssetId(node?["effectAssetId"], partLabel);
            if (!_meshRegistry.TryGetDescriptor(effectAssetId, out MeshAssetDescriptor descriptor) ||
                !descriptor.VfxData.IsValid ||
                descriptor.VfxData.ParticleSystem is null)
            {
                throw new InvalidOperationException(
                    $"{partLabel} references VFX effect asset id {effectAssetId} without Quarks particle data.");
            }

            PrefabVfxSpawnMode spawnMode = descriptor.VfxData.SpawnMode;
            bool authored = node?["spawnMode"] != null;
            if (authored)
            {
                PrefabVfxSpawnMode authoredSpawnMode = ReadRequiredEnum<PrefabVfxSpawnMode>(
                    node["spawnMode"],
                    $"{partLabel} spawnMode");
                if (authoredSpawnMode != spawnMode)
                {
                    throw new InvalidOperationException(
                        $"{partLabel} declares spawnMode '{authoredSpawnMode}', but effect asset spawnMode is '{spawnMode}'. Author spawnMode only on Presentation/particle_vfx.json.");
                }
            }

            PrefabPart part = PrefabPart.Vfx(effectAssetId, spawnMode);
            part.VfxSpawnModeAuthored = authored;
            return part;
        }

        private int ResolveEffectAssetId(JsonNode? node, string partLabel)
        {
            string effectKey = ReadRequiredString(node, $"{partLabel} effectAssetId");
            int effectAssetId = _meshRegistry.GetId(effectKey);
            if (effectAssetId <= 0 || !_meshRegistry.TryGetDescriptor(effectAssetId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"{partLabel} references unknown VFX effect asset '{effectKey}'.");
            }

            if (!descriptor.VfxData.IsValid)
            {
                throw new InvalidOperationException(
                    $"{partLabel} references VFX effect asset '{effectKey}' without VFX particle data.");
            }

            return effectAssetId;
        }

        private VfxAssetData ParseVfxData(JsonNode? node, string key)
        {
            if (node == null)
            {
                return default;
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Presentation/mesh_assets.json asset '{key}' vfx must be an object.");
            }

            string assetLabel = $"Presentation/mesh_assets.json asset '{key}' vfx";
            foreach (var property in obj)
            {
                if (string.Equals(property.Key, "particleVfxId", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(property.Key, "spawnMode", StringComparison.Ordinal) ||
                    string.Equals(property.Key, "emitter", StringComparison.Ordinal) ||
                    string.Equals(property.Key, "coreColor", StringComparison.Ordinal) ||
                    string.Equals(property.Key, "shellColor", StringComparison.Ordinal) ||
                    string.Equals(property.Key, "particleColor", StringComparison.Ordinal) ||
                    string.Equals(property.Key, "particleSystem", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{assetLabel} field '{property.Key}' is not supported. Author only particleVfxId here; spawnMode and Quarks particle data live in Presentation/particle_vfx.json.");
                }

                throw new InvalidOperationException($"{assetLabel} uses unsupported field '{property.Key}'.");
            }

            if (!obj.ContainsKey("particleVfxId"))
            {
                throw new InvalidOperationException($"{assetLabel} must declare particleVfxId.");
            }

            ParticleVfxAssetData particleSystem = ResolveParticleVfxAsset(
                obj["particleVfxId"],
                assetLabel,
                out int particleVfxAssetId);
            return new VfxAssetData(particleSystem, particleVfxAssetId);
        }

        private ParticleVfxAssetData ResolveParticleVfxAsset(
            JsonNode? node,
            string assetLabel,
            out int particleVfxAssetId)
        {
            if (_particleVfxRegistry == null)
            {
                throw new InvalidOperationException(
                    $"{assetLabel}.particleVfxId requires the Presentation particle VFX registry. Load Presentation/particle_vfx.json before mesh assets.");
            }

            string particleVfxKey = ReadRequiredString(node, $"{assetLabel}.particleVfxId");
            particleVfxAssetId = _particleVfxRegistry.GetId(particleVfxKey);
            if (particleVfxAssetId <= 0 || !_particleVfxRegistry.TryGet(particleVfxAssetId, out ParticleVfxAssetData effect))
            {
                throw new InvalidOperationException(
                    $"{assetLabel} references unknown particle VFX asset '{particleVfxKey}'.");
            }

            return effect;
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
            PrefabPartGroundingMode mode = ParseRequiredEnumText<PrefabPartGroundingMode>(
                modeText,
                "Prefab part grounding mode");

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

        private static string ReadRequiredString(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out string? value))
            {
                throw new InvalidOperationException($"{label} must be a non-empty asset key.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{label} must be a non-empty asset key.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} must not include leading or trailing whitespace.");
            }

            return value;
        }

        private static T ReadRequiredEnum<T>(JsonNode? node, string label)
            where T : struct, Enum
        {
            string value = ReadRequiredString(node, label);
            return ParseRequiredEnumText<T>(value, label);
        }

        private static T ParseRequiredEnumText<T>(string value, string label)
            where T : struct, Enum
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                throw new InvalidOperationException($"{label} has invalid value '{value}'. Use the enum name, not a numeric string.");
            }

            if (!Enum.TryParse(value, ignoreCase: false, out T parsed) ||
                !Enum.IsDefined(typeof(T), parsed))
            {
                throw new InvalidOperationException($"{label} has invalid value '{value}'.");
            }

            return parsed;
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
                foreach (var property in obj)
                {
                    string propertyName = property.Key;
                    if (propertyName != "x" &&
                        propertyName != "y" &&
                        propertyName != "z" &&
                        propertyName != "w")
                    {
                        throw new InvalidOperationException(
                            $"Prefab part localRotation object uses unsupported field '{propertyName}'. Expected exact fields x, y, z, w.");
                    }
                }

                return new Quaternion(
                    obj["x"]?.GetValue<float>() ?? defaultValue.X,
                    obj["y"]?.GetValue<float>() ?? defaultValue.Y,
                    obj["z"]?.GetValue<float>() ?? defaultValue.Z,
                    obj["w"]?.GetValue<float>() ?? defaultValue.W);
            }

            return defaultValue;
        }
    }
}
