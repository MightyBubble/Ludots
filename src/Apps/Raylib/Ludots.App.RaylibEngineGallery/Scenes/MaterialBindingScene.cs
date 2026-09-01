using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 材质绑定：RaylibMaterialLibrary 同网格多材质（不透明棋盘 / 裁切条纹 / 半透明光斑）；
    /// 实例行演示材质实例链（同父异贴图/异参数）与 shaderKey=emissive 自定义着色行为。
    /// </summary>
    [EngineSceneComponent("material_binding")]
    public sealed unsafe class MaterialBindingScene : IEngineSceneComponent
    {
        private const int CheckerMaterialId = 621;
        private const int StripeMaterialId = 622;
        private const int GlowMaterialId = 623;
        private const int IronBaseMaterialId = 624;
        private const int IronRustyMaterialId = 625;
        private const int EmissiveMaterialId = 626;
        private const int EmissiveHotMaterialId = 627;
        private const int DemoCubeAssetId = 630;

        private readonly GalleryAssetPaths _paths = GalleryAssetPaths.Instance;
        private readonly GalleryMaterialAssets _materials = new();
        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private readonly (int MaterialId, string Label, Color Panel)[] _slots;
        private readonly Material[] _boundMaterials = new Material[3];
        private readonly MaterialBlendMode[] _blendModes = new MaterialBlendMode[3];
        private readonly GalleryLitProps _litProps = new();
        private readonly RaylibSkyboxRenderer _skybox = new();

        private RaylibMaterialLibrary _binder = null!;
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibLaneShader _emissiveLane = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private Mesh _cube;
        private bool _disposed;

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
            GalleryTextureFactory.WritePng("mat_rusty.png", 128, 128, (x, y) =>
            {
                float n = GalleryTextureFactory.SmoothNoise(x, y, 11);
                return new Color((byte)(90 + (n * 110f)), (byte)(44 + (n * 52f)), (byte)(22 + (n * 30f)), 255);
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

            // 实例链演示：rusty 继承 base 的 metallic=1 并覆盖 albedo/roughness；hot 覆盖 emissive 命名参数。
            _materials.Register("gallery.mat.iron", new MaterialAssetDescriptor(
                IronBaseMaterialId,
                MaterialAssetDomain.Surface,
                MaterialAssetFlags.None,
                floatParams: new Dictionary<string, float>
                {
                    [MaterialParameterNames.Roughness] = 0.35f,
                    [MaterialParameterNames.Metallic] = 1.0f,
                }),
                new Dictionary<string, string> { [MaterialTextureSlots.Albedo] = "generated/mat_checker.png" });
            _materials.Register("gallery.mat.iron.rusty", new MaterialAssetDescriptor(
                IronRustyMaterialId,
                MaterialAssetDomain.Surface,
                MaterialAssetFlags.None,
                parentKey: "gallery.mat.iron",
                floatParams: new Dictionary<string, float> { [MaterialParameterNames.Roughness] = 0.95f }),
                new Dictionary<string, string> { [MaterialTextureSlots.Albedo] = "generated/mat_rusty.png" });
            _materials.Register("gallery.mat.emissive", new MaterialAssetDescriptor(
                EmissiveMaterialId,
                MaterialAssetDomain.Surface,
                MaterialAssetFlags.None,
                shaderKey: "emissive",
                floatParams: new Dictionary<string, float>
                {
                    [MaterialParameterNames.Roughness] = 0.6f,
                    ["uEmissiveStrength"] = 1.5f,
                },
                colorParams: new Dictionary<string, Vector4> { ["uEmissiveColor"] = new Vector4(0.2f, 0.9f, 1.0f, 1f) }),
                new Dictionary<string, string> { [MaterialTextureSlots.Albedo] = "generated/mat_checker.png" });
            _materials.Register("gallery.mat.emissive.hot", new MaterialAssetDescriptor(
                EmissiveHotMaterialId,
                MaterialAssetDomain.Surface,
                MaterialAssetFlags.None,
                parentKey: "gallery.mat.emissive",
                floatParams: new Dictionary<string, float> { ["uEmissiveStrength"] = 3.0f },
                colorParams: new Dictionary<string, Vector4> { ["uEmissiveColor"] = new Vector4(1.0f, 0.35f, 0.15f, 1f) }));

            _meshes.Register("gallery.demo_cube", MeshAssetDescriptor.Primitive(DemoCubeAssetId, PrimitiveMeshKind.Cube));

            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Instanced,
                _paths,
                _materials,
                channelRegistrar: GalleryAnimationChannels.Register);
            _emissiveLane = RaylibLaneShader.LoadInstancing(AppContext.BaseDirectory, "mat_emissive.vs", "mat_emissive.fs", "mat_emissive");
            _primitives.RegisterInstancingShader("emissive", _emissiveLane);

            _binder = new RaylibMaterialLibrary(_paths, _materials);
            _cube = RaylibNativeResources.GenMeshCube(3f, 3f, 3f);

            for (int i = 0; i < _slots.Length; i++)
            {
                Material material = RaylibNativeResources.LoadMaterialDefault();
                if (!_binder.TryApplyMaps(ref material, _slots[i].MaterialId))
                {
                    RaylibNativeResources.UnloadMaterial(material);
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

            _snapshot.BeginFrame();
            _snapshot.Add(GalleryItems.Mesh(DemoCubeAssetId, 9001, new Vector3(-11.25f, 1.4f, -13f), new Vector3(2.8f, 2.8f, 2.8f), Vector4.One, IronBaseMaterialId));
            _snapshot.Add(GalleryItems.Mesh(DemoCubeAssetId, 9002, new Vector3(-3.75f, 1.4f, -13f), new Vector3(2.8f, 2.8f, 2.8f), Vector4.One, IronRustyMaterialId));
            _snapshot.Add(GalleryItems.Mesh(DemoCubeAssetId, 9003, new Vector3(3.75f, 1.4f, -13f), new Vector3(2.8f, 2.8f, 2.8f), Vector4.One, EmissiveMaterialId));
            _snapshot.Add(GalleryItems.Mesh(DemoCubeAssetId, 9004, new Vector3(11.25f, 1.4f, -13f), new Vector3(2.8f, 2.8f, 2.8f), Vector4.One, EmissiveHotMaterialId));
            _primitives.ApplyFrameLighting(_litProps.Lighting, camera.position, _shadowMap, shadowTexelWorld: 0.05f);
            _primitives.Draw(_snapshot, camera, _meshes);

            Rl.EndMode3D();

            int x = 16;
            for (int i = 0; i < _slots.Length; i++)
            {
                Rl.DrawRectangle(x - 6, 26, 4, 18, _slots[i].Panel);
                GalleryFont.Draw($"[{i + 1}] {_slots[i].Label} ({_blendModes[i]})", x + 4, 26, 18, GalleryColors.RayWhite);
                x += 320;
            }

            GalleryFont.Draw("instance row: [iron] base metallic | [rusty] albedo+roughness override | [emissive] shaderKey=emissive | [hot] instance param override", 16, 52, 18, GalleryColors.RayWhite);
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
                RaylibNativeResources.UnloadMesh(_cube);
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
                RaylibNativeResources.UnloadMaterial(_boundMaterials[i]);
            }

            _binder?.Dispose();
            _primitives?.Dispose();
            if (_emissiveLane != null)
            {
                RaylibNativeResources.UnloadShader(_emissiveLane.Shader);
                _emissiveLane = null!;
            }

            _shadowMap?.Dispose();
            _skybox.Dispose();
            _litProps.Dispose();
            _binder = null!;
            _disposed = true;
        }
    }
}
