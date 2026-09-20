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
    /// 美术动画组件：装载 GLB 模型并按 config 播放内嵌 clip（loop/speed/phaseOffset）。
    /// 覆盖模式叠加在基座组件画面上；不触碰相机。
    /// </summary>
    [EngineSceneComponent("animator")]
    public sealed unsafe class AnimatorComponent : IEngineSceneComponent, IEngineSceneComponentAssets, IEngineSceneComponentConfigurable
    {
        private const int MeshAssetId = 7300;

        private readonly RaylibSkyboxRenderer _skybox = new();

        private RaylibGpuSkinnedModelCache _modelCache = null!;
        private RaylibGpuSkinnedModelCache.Entry _entry;
        private RaylibLitModel _lit = null!;
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private EngineSceneAsset _model = null!;
        private Vector3 _position;
        private float _scale = 1.6f;
        private float _facingDeg;
        private float _speed = 1f;
        private float _phaseOffset;
        private int _clipIndex;
        private float _dayPhase = 0.46f;
        private bool _disposed;

        public void Configure(JsonElement config)
        {
            if (config.TryGetProperty("position", out JsonElement position) && position.ValueKind == JsonValueKind.Array && position.GetArrayLength() == 3)
            {
                _position = new Vector3(position[0].GetSingle(), position[1].GetSingle(), position[2].GetSingle());
            }

            if (config.TryGetProperty("scale", out JsonElement scale) && scale.ValueKind == JsonValueKind.Number)
            {
                _scale = scale.GetSingle();
            }

            if (config.TryGetProperty("facingDeg", out JsonElement facing) && facing.ValueKind == JsonValueKind.Number)
            {
                _facingDeg = facing.GetSingle();
            }

            if (config.TryGetProperty("speed", out JsonElement speed) && speed.ValueKind == JsonValueKind.Number)
            {
                _speed = speed.GetSingle();
            }

            if (config.TryGetProperty("phaseOffset", out JsonElement phase) && phase.ValueKind == JsonValueKind.Number)
            {
                _phaseOffset = phase.GetSingle();
            }

            if (config.TryGetProperty("dayPhase", out JsonElement dayPhase) && dayPhase.ValueKind == JsonValueKind.Number)
            {
                _dayPhase = dayPhase.GetSingle();
            }

            if (config.TryGetProperty("clip", out JsonElement clip) && clip.ValueKind == JsonValueKind.String)
            {
                _clipIndex = -1;
                string needle = clip.GetString()!;
                _clipNeedle = needle;
            }

            if (config.TryGetProperty("castShadows", out JsonElement cast) && cast.ValueKind == JsonValueKind.False)
            {
                _castShadows = false;
            }
        }

        private string _clipNeedle = string.Empty;
        private bool _castShadows = true;

        public void SetAssets(IReadOnlyDictionary<string, EngineSceneAsset> assets)
        {
            EngineSceneAsset model = assets.Values.FirstOrDefault(a => a.Kind == "model");
            if (model == null || string.IsNullOrWhiteSpace(model.Source))
            {
                throw new InvalidDataException("animator requires a model asset in the scene manifest.");
            }

            _model = model;
        }

        public void Load()
        {
            _modelCache = new RaylibGpuSkinnedModelCache(GalleryAssetPaths.Instance);
            MeshAssetDescriptor descriptor = MeshAssetDescriptor.Model(MeshAssetId, _model.Source);
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: _dayPhase);
            _lit = new RaylibLitModel();
            _shadowMap = new RaylibDirectionalShadowMap();
            _entry = _modelCache.GetOrLoad(MeshAssetId, in descriptor);
            _lit.AttachToModel(_entry.Model);

            if (_clipIndex < 0)
            {
                bool found = false;
                for (int i = 0; i < _entry.AnimCount; i++)
                {
                    string name = ReadAnimationName(_entry.Animations[i]);
                    if (name.Contains(_clipNeedle, StringComparison.OrdinalIgnoreCase))
                    {
                        _clipIndex = i;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new InvalidDataException($"animator clip '{_clipNeedle}' not found in model '{_model.Source}'.");
                }
            }
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            _lighting.SetDayPhase(_dayPhase);
            ModelAnimation animation = _entry.Animations[_clipIndex];
            float cycle = (float)((totalTimeSeconds * _speed * 0.55) + _phaseOffset) % 1f;
            int frame = (int)(cycle * animation.frameCount) % animation.frameCount;
            Rl.UpdateModelAnimation(_entry.Model, animation, frame);

            if (_castShadows)
            {
                _shadowMap.BeginFrame(_lighting.SunDirectionToward, _position, _scale * 6f);
                _shadowMap.DrawModelShadow(_entry.Model, _position, _facingDeg, new Vector3(_scale));
                _shadowMap.EndFrame();
            }

            _lit.BeginFrame(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.05f);
            Rl.BeginMode3D(camera);
            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_lighting, sizeMeters: 2200f);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);
            ref Material modelMaterial = ref _entry.Model.materials[0];
            _lit.ApplyDrawUniforms(new Vector4(0.95f, 0.96f, 0.98f, 1f), roughness: 0.55f, metallic: 0.05f);
            _lit.BindShadowToMaterial(ref modelMaterial, _shadowMap);
            _lit.BindIblToMaterial(ref modelMaterial);
            Rl.DrawModelEx(_entry.Model, _position, Vector3.UnitY, _facingDeg, new Vector3(_scale), new Color(240, 240, 245, 255));
            Rl.EndMode3D();
        }

        private static string ReadAnimationName(in ModelAnimation animation)
        {
            fixed (byte* name = animation.name)
            {
                int len = 0;
                while (len < 32 && name[len] != 0)
                {
                    len++;
                }

                return System.Text.Encoding.ASCII.GetString(name, len);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _shadowMap?.Dispose();
            _skybox.Dispose();
            _disposed = true;
        }
    }
}
