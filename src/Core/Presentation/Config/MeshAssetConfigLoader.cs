using System;
using System.Globalization;
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
                    descriptor.VfxEffectData = ParseVfxEffectData(node["vfx"], key);
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
                    descriptor.VfxEffectData = ParseVfxEffectData(node["vfx"], key);
                    return descriptor;
                }
                case MeshAssetType.Prefab:
                {
                    var parts = ParseParts(node["parts"]);
                    var descriptor = MeshAssetDescriptor.Prefab(0, parts);
                    descriptor.VfxEffectData = ParseVfxEffectData(node["vfx"], key);
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
                    PrefabVisualPartKind.Vfx => PrefabPart.Vfx(
                        ResolveEffectAssetId(p?["effectAssetId"], $"Prefab part at index {j}"),
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
                part.Size = ParseVector2WithDefault(p?["size"], part.Size == Vector2.Zero ? Vector2.One : part.Size);
                part.Tiling = ParseVector2WithDefault(p?["tiling"], part.Tiling == Vector2.Zero ? Vector2.One : part.Tiling);
                part.AlignToSurface = p?["alignToSurface"]?.GetValue<bool>() ?? part.AlignToSurface;
                part.TerrainFacing = p?["terrainFacing"]?.GetValue<bool>() ?? part.TerrainFacing;

                parts[j] = part;
            }
            return parts;
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

            if (!descriptor.VfxEffectData.IsValid)
            {
                throw new InvalidOperationException(
                    $"{partLabel} references VFX effect asset '{effectKey}' without vfx emitter data.");
            }

            return effectAssetId;
        }

        private static VfxEffectAssetData ParseVfxEffectData(JsonNode? node, string key)
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

            ValidateObjectFields(
                obj,
                $"Presentation/mesh_assets.json asset '{key}' vfx",
                "emitter");

            if (obj["emitter"] is not JsonObject emitter)
            {
                throw new InvalidOperationException(
                    $"Presentation/mesh_assets.json asset '{key}' vfx.emitter must be an object.");
            }

            ValidateObjectFields(
                emitter,
                $"Presentation/mesh_assets.json asset '{key}' vfx.emitter",
                "shape",
                "particleCount",
                "ringSegments",
                "radiusScale",
                "coreRadiusScale",
                "particleRadiusScale",
                "lifetimeSeconds",
                "pulseSpeedRadPerSecond",
                "orbitSpeedRadPerSecond");

            string shapeLabel = $"Presentation/mesh_assets.json asset '{key}' vfx.emitter.shape";
            VfxEmitterShape shape = ReadRequiredEnum<VfxEmitterShape>(emitter["shape"], shapeLabel);
            if (shape == VfxEmitterShape.None)
            {
                throw new InvalidOperationException($"{shapeLabel} must not be None.");
            }

            return new VfxEffectAssetData(new VfxEmitterDescriptor(
                shape,
                ReadRequiredPositiveInt(emitter["particleCount"], $"Presentation/mesh_assets.json asset '{key}' vfx.emitter.particleCount"),
                ReadRequiredMinInt(emitter["ringSegments"], 3, $"Presentation/mesh_assets.json asset '{key}' vfx.emitter.ringSegments"),
                ReadRequiredPositiveFloat(emitter["radiusScale"], $"Presentation/mesh_assets.json asset '{key}' vfx.emitter.radiusScale"),
                ReadRequiredPositiveFloat(emitter["coreRadiusScale"], $"Presentation/mesh_assets.json asset '{key}' vfx.emitter.coreRadiusScale"),
                ReadRequiredPositiveFloat(emitter["particleRadiusScale"], $"Presentation/mesh_assets.json asset '{key}' vfx.emitter.particleRadiusScale"),
                ReadRequiredPositiveFloat(emitter["lifetimeSeconds"], $"Presentation/mesh_assets.json asset '{key}' vfx.emitter.lifetimeSeconds"),
                ReadRequiredNonNegativeFloat(emitter["pulseSpeedRadPerSecond"], $"Presentation/mesh_assets.json asset '{key}' vfx.emitter.pulseSpeedRadPerSecond"),
                ReadRequiredNonNegativeFloat(emitter["orbitSpeedRadPerSecond"], $"Presentation/mesh_assets.json asset '{key}' vfx.emitter.orbitSpeedRadPerSecond")));
        }

        private static PrefabVfxSpawnMode ParseSpawnMode(string? spawnModeText)
        {
            string resolved = string.IsNullOrWhiteSpace(spawnModeText)
                ? nameof(PrefabVfxSpawnMode.Once)
                : spawnModeText;
            return ParseRequiredEnumText<PrefabVfxSpawnMode>(
                resolved,
                "Prefab part VFX spawnMode");
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

        private static int ReadRequiredPositiveInt(JsonNode? node, string label)
        {
            return ReadRequiredMinInt(node, 1, label);
        }

        private static int ReadRequiredMinInt(JsonNode? node, int min, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out int value))
            {
                throw new InvalidOperationException($"{label} must be an integer greater than or equal to {min}.");
            }

            if (value < min)
            {
                throw new InvalidOperationException($"{label} must be greater than or equal to {min}.");
            }

            return value;
        }

        private static float ReadRequiredPositiveFloat(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out float value))
            {
                throw new InvalidOperationException($"{label} must be a finite number greater than 0.");
            }

            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new InvalidOperationException($"{label} must be a finite number greater than 0.");
            }

            return value;
        }

        private static float ReadRequiredNonNegativeFloat(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out float value))
            {
                throw new InvalidOperationException($"{label} must be a finite number greater than or equal to 0.");
            }

            if (!float.IsFinite(value) || value < 0f)
            {
                throw new InvalidOperationException($"{label} must be a finite number greater than or equal to 0.");
            }

            return value;
        }

        private static void ValidateObjectFields(JsonObject obj, string context, params string[] allowedFields)
        {
            foreach (var property in obj)
            {
                bool allowed = false;
                for (int i = 0; i < allowedFields.Length; i++)
                {
                    if (string.Equals(property.Key, allowedFields[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new InvalidOperationException(
                        $"{context} uses unsupported field '{property.Key}'.");
                }
            }
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
