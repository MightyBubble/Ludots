using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>材质绑定：RaylibMaterialLibrary 把同一网格绑到三种宿主材质（不透明棋盘 / 裁切条纹 / 半透明光斑）。</summary>
    public sealed unsafe class MaterialBindingScene : IEngineScene
    {
        private const int CheckerMaterialId = 621;
        private const int StripeMaterialId = 622;
        private const int GlowMaterialId = 623;

        private readonly GalleryAssetPaths _paths = GalleryAssetPaths.Instance;
        private readonly GalleryMaterialAssets _materials = new();
        private readonly (int MaterialId, string Label, Color Panel)[] _slots;
        private readonly Material[] _boundMaterials = new Material[3];
        private readonly MaterialBlendMode[] _blendModes = new MaterialBlendMode[3];
        private readonly GalleryLitProps _litProps = new();
        private readonly RaylibSkyboxRenderer _skybox = new();

        private RaylibMaterialLibrary _binder = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private Mesh _cube;
        private bool _disposed;

        public string Id => "material_binding";
        public string Title => "材质绑定";
        public string Summary => "RaylibMaterialLibrary 同网格多材质/混合模式";

        public MaterialBindingScene()
        {
            _slots = new[]
            {
                (CheckerMaterialId, "opaque checker", new Color(70, 96, 140, 255)),
                (StripeMaterialId, "cutout stripes", new Color(140, 96, 70, 255)),
                (GlowMaterialId, "alpha glow", new Color(90, 130, 90, 255)),
            };
        }

        public void Load()
        {
            _litProps.Load();
            _shadowMap = new RaylibDirectionalShadowMap();
            GalleryTextureFactory.WritePng("mat_checker.png", 128, 128, (x, y) =>
            {
                bool lightCell = ((x / 16) + (y / 16)) % 2 == 0;
                return lightCell ? new Color(228, 224, 214, 255) : new Color(158, 60, 54, 255);
            });
            GalleryTextureFactory.WritePng("mat_stripes.png", 128, 128, (x, y) =>
            {
                float wave = MathF.Sin((x * 0.35f) + (y * 0.06f));
                return wave > 0.15f ? new Color(240, 196, 86, 255) : new Color(0, 0, 0, 0);
            });
            GalleryTextureFactory.WritePng("mat_glow.png", 128, 128, (x, y) =>
            {
                float u = (x - 64f) / 60f;
                float v = (y - 64f) / 60f;
                float radial = Math.Clamp(1f - MathF.Sqrt((u * u) + (v * v)), 0f, 1f);
                byte alpha = (byte)(MathF.Pow(radial, 1.3f) * 235f);
                return new Color(120, 240, 170, alpha);
            });

            _materials.Register("gallery.mat.checker", new MaterialAssetDescriptor(
                CheckerMaterialId,
                MaterialAssetDomain.Surface,
                MaterialAssetFlags.None),
                new Dictionary<string, string> { [MaterialTextureSlots.Albedo] = "generated/mat_checker.png" });
            _materials.Register("gallery.mat.stripe", new MaterialAssetDescriptor(
                StripeMaterialId,
                MaterialAssetDomain.Surface,
                MaterialAssetFlags.Cutout | MaterialAssetFlags.DoubleSided),
                new Dictionary<string, string> { [MaterialTextureSlots.Albedo] = "generated/mat_stripes.png" });
            _materials.Register("gallery.mat.glow", new MaterialAssetDescriptor(
                GlowMaterialId,
                MaterialAssetDomain.Surface,
                MaterialAssetFlags.Transparent | MaterialAssetFlags.DoubleSided),
                new Dictionary<string, string> { [MaterialTextureSlots.Albedo] = "generated/mat_glow.png" });

            _binder = new RaylibMaterialLibrary(_paths, _materials);
            _cube = Rl.GenMeshCube(3f, 3f, 3f);

            for (int i = 0; i < _slots.Length; i++)
            {
                Material material = Rl.LoadMaterialDefault();
                if (!_binder.TryApplyMaps(ref material, _slots[i].MaterialId))
                {
                    Rl.UnloadMaterial(material);
                    throw new InvalidOperationException($"Gallery material '{_slots[i].Label}' has no host albedo binding.");
                }

                material.shader = _litProps.Lit.Shader;
                _boundMaterials[i] = material;
                _blendModes[i] = MaterialBlendModeResolver.Resolve(
                    _materials.TryGet(_slots[i].MaterialId, out MaterialAssetDescriptor descriptor)
                        ? descriptor.Flags
                        : throw new InvalidOperationException($"Gallery material '{_slots[i].MaterialId}' is not registered."));
            }
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 26f);
            float spin = (float)totalTimeSeconds * 0.5f;
            _litProps.Lighting.SetDayPhase(_litProps.DayPhase01);

            _shadowMap.BeginFrame(_litProps.Lighting.SunDirectionToward, new Vector3(0f, 1.6f, -2.5f), 16f);
            for (int i = 0; i < _slots.Length; i++)
            {
                DrawBoundCubeShadow(new Vector3((i - 1) * 7.5f, 3.4f, 0f), spin);
                DrawBoundCubeShadow(new Vector3((i - 1) * 7.5f, 0.9f, -6.5f), -spin * 0.7f);
            }

            _shadowMap.EndFrame();

            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_litProps.Lighting, sizeMeters: 1200f);
            Rl.ClearBackground(skyConfig.Skybox.ClearColor);
            _litProps.BeginFrame(camera.position, _shadowMap, shadowTexelWorld: 0.05f);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);
            Rl.DrawGrid(20, 3f);

            for (int i = 0; i < _slots.Length; i++)
            {
                DrawBoundCube(i, new Vector3((i - 1) * 7.5f, 3.4f, 0f), spin, Color.WHITE);
                DrawBoundCube(i, new Vector3((i - 1) * 7.5f, 0.9f, -6.5f), -spin * 0.7f, new Color(150, 190, 255, 255));
                Color panel = _slots[i].Panel;
                _litProps.DrawCube(
                    new Vector3((i - 1) * 7.5f, 0.1f, 0f),
                    new Vector3(4.6f, 0.2f, 4.6f),
                    new Vector4(panel.r / 255f, panel.g / 255f, panel.b / 255f, 1f));
            }

            Rl.EndMode3D();

            int x = 16;
            for (int i = 0; i < _slots.Length; i++)
            {
                Rl.DrawRectangle(x - 6, 26, 4, 18, _slots[i].Panel);
                GalleryFont.Draw($"[{i + 1}] {_slots[i].Label} ({_blendModes[i]})", x + 4, 26, 18, GalleryColors.RayWhite);
                x += 320;
            }
        }

        private unsafe void DrawBoundCube(int slotIndex, Vector3 position, float yawRad, Color tint)
        {
            ref Material material = ref _boundMaterials[slotIndex];
            if (material.maps != null)
            {
                material.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO].color = tint;
            }

            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, yawRad) *
                Matrix4x4.CreateTranslation(position));
            _litProps.Lit.ApplyDrawUniforms(ToVector4(tint), roughness: 0.65f, metallic: slotIndex == 2 ? 0.25f : 0f);
            _litProps.Lit.BindShadowToMaterial(ref material, _shadowMap);
            _litProps.Lit.BindIblToMaterial(ref material);

            if (_blendModes[slotIndex] == MaterialBlendMode.AlphaBlend)
            {
                Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
                Rl.DrawMesh(_cube, material, transform);
                Rl.EndBlendMode();
                return;
            }

            Rl.DrawMesh(_cube, material, transform);
        }

        private void DrawBoundCubeShadow(Vector3 position, float yawRad)
        {
            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, yawRad) *
                Matrix4x4.CreateTranslation(position));
            _shadowMap.DrawMeshShadow(_cube, transform);
        }

        private static Vector4 ToVector4(Color color)
        {
            return new Vector4(color.r / 255f, color.g / 255f, color.b / 255f, color.a / 255f);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_cube.vertexCount > 0)
            {
                Rl.UnloadMesh(_cube);
            }

            for (int i = 0; i < _boundMaterials.Length; i++)
            {
                if (_boundMaterials[i].maps == null)
                {
                    continue;
                }

                _binder?.DetachOwnedMaps(ref _boundMaterials[i]);
                _boundMaterials[i].maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_EMISSION].texture = default;
                _boundMaterials[i].maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP].texture = default;
                _boundMaterials[i].maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_BRDF].texture = default;
                _boundMaterials[i].shader = default;
                Rl.UnloadMaterial(_boundMaterials[i]);
            }

            _binder?.Dispose();
            _shadowMap?.Dispose();
            _skybox.Dispose();
            _litProps.Dispose();
            _binder = null!;
            _disposed = true;
        }
    }
}
