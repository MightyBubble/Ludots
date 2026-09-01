using System.Numerics;
using System.Text.Json;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Ludots.Raylib.SceneKit;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Content.EngineGallery.Components
{
    /// <summary>
    /// 静态网格多实例组件：同一 mesh 的实例阵（TRS + 逐实例颜色/材质覆盖）经
    /// PrimitiveDrawItem 进实例化合批车道；材质走工程 .mat.json 资产（parent 实例链）。
    /// 覆盖模式：不清屏、不画天，叠加在基座组件（island_terrain 等）的画面上。
    /// </summary>
    [EngineSceneComponent("static_mesh")]
    public sealed class StaticMeshComponent : IEngineSceneComponent, IEngineSceneComponentAssets, IEngineSceneComponentConfigurable
    {
        private const int MeshAssetId = 7100;
        private const int MaterialAssetIdBase = 7200;
        private const int FirstStableId = 720000;

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryMaterialAssets _materials = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private readonly List<InstanceSpec> _instances = [];
        private readonly Dictionary<string, int> _materialIdByAssetKey = new(StringComparer.Ordinal);

        private PrimitiveMeshKind _meshKind = PrimitiveMeshKind.Cube;
        private string _defaultMaterialAssetKey = string.Empty;
        private float _dayPhase = 0.46f;
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private bool _disposed;

        public void Configure(JsonElement config)
        {
            _meshKind = config.TryGetProperty("primitive", out JsonElement primitive) && primitive.ValueKind == JsonValueKind.String
                ? ParseMeshKind(primitive.GetString()!)
                : PrimitiveMeshKind.Cube;
            if (config.TryGetProperty("material", out JsonElement material) && material.ValueKind == JsonValueKind.String)
            {
                _defaultMaterialAssetKey = material.GetString()!;
            }

            if (config.TryGetProperty("dayPhase", out JsonElement phase) && phase.ValueKind == JsonValueKind.Number)
            {
                _dayPhase = phase.GetSingle();
            }

            if (!config.TryGetProperty("instances", out JsonElement instances) || instances.ValueKind != JsonValueKind.Array || instances.GetArrayLength() == 0)
            {
                throw new InvalidDataException("static_mesh requires a non-empty instances array.");
            }

            foreach (JsonElement item in instances.EnumerateArray())
            {
                _instances.Add(new InstanceSpec(
                    ReadVector3(item, "position"),
                    ReadScale(item),
                    ReadColor(item),
                    item.TryGetProperty("material", out JsonElement m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : string.Empty));
            }
        }

        public void SetAssets(IReadOnlyDictionary<string, EngineSceneAsset> assets)
        {
            int nextMaterialId = MaterialAssetIdBase;
            foreach (EngineSceneAsset asset in assets.Values)
            {
                if (asset.ResolvedPath == null)
                {
                    throw new InvalidDataException($"static_mesh asset '{asset.Id}' has no resolved project path.");
                }

                _materialIdByAssetKey[asset.Id] = MaterialAssetReader.Register(_materials, asset.ResolvedPath, nextMaterialId);
                nextMaterialId++;
            }
        }

        public void Load()
        {
            _meshes.Register("project.static_mesh", MeshAssetDescriptor.Primitive(MeshAssetId, _meshKind));
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: _dayPhase);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Instanced,
                GalleryAssetPaths.Instance,
                _materials,
                channelRegistrar: GalleryAnimationChannels.Register);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            _lighting.SetDayPhase(_dayPhase);
            _snapshot.BeginFrame();
            for (int i = 0; i < _instances.Count; i++)
            {
                InstanceSpec instance = _instances[i];
                string materialKey = instance.MaterialAssetKey.Length > 0 ? instance.MaterialAssetKey : _defaultMaterialAssetKey;
                _snapshot.Add(GalleryItems.Mesh(
                    MeshAssetId,
                    FirstStableId + i,
                    instance.Position,
                    instance.Scale,
                    instance.Color,
                    materialKey.Length > 0 && _materialIdByAssetKey.TryGetValue(materialKey, out int materialId) ? materialId : 0));
            }

            Rl.BeginMode3D(camera);
            _primitives.ApplyFrameLighting(_lighting, camera.position, shadow: null);
            _primitives.Draw(_snapshot, camera, _meshes);
            Rl.EndMode3D();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _primitives?.Dispose();
            _primitives = null!;
            _disposed = true;
        }

        private static PrimitiveMeshKind ParseMeshKind(string kind)
        {
            return kind switch
            {
                "cube" => PrimitiveMeshKind.Cube,
                "sphere" => PrimitiveMeshKind.Sphere,
                _ => throw new InvalidDataException($"static_mesh uses unknown primitive '{kind}'."),
            };
        }

        private static Vector3 ReadVector3(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3)
            {
                throw new InvalidDataException($"static_mesh instance field {name} must be a 3D vector.");
            }

            return new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
        }

        private static Vector3 ReadScale(JsonElement element)
        {
            if (!element.TryGetProperty("scale", out JsonElement value))
            {
                return Vector3.One;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return new Vector3(value.GetSingle());
            }

            return ReadVector3(element, "scale");
        }

        private static Vector4 ReadColor(JsonElement element)
        {
            if (!element.TryGetProperty("color", out JsonElement value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 4)
            {
                return Vector4.One;
            }

            return new Vector4(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle(), value[3].GetSingle());
        }

        private readonly record struct InstanceSpec(Vector3 Position, Vector3 Scale, Vector4 Color, string MaterialAssetKey);
    }
}
