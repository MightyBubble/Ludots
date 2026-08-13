using System;
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

        public MeshAssetConfigLoader(ConfigPipeline configs, MeshAssetRegistry meshRegistry)
            : this(configs, meshRegistry, particleVfxRegistry: null)
        {
        }

        public MeshAssetConfigLoader(
            ConfigPipeline configs,
            MeshAssetRegistry meshRegistry,
            ParticleVfxRegistry? particleVfxRegistry)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _meshRegistry = meshRegistry ?? throw new ArgumentNullException(nameof(meshRegistry));
            _particleVfxRegistry = particleVfxRegistry;
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

        private MeshAssetDescriptor ParseDescriptor(JsonNode node, string key)
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

            if (type == MeshAssetType.None)
            {
                throw new InvalidOperationException($"Presentation/mesh_assets.json asset '{key}' type must not be None.");
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
                default:
                    throw new InvalidOperationException($"Presentation/mesh_assets.json asset '{key}' uses unsupported mesh asset type '{type}'.");
            }
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
    }
}
