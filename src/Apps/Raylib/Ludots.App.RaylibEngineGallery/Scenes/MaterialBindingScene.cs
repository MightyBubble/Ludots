using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>材质绑定：RaylibMaterialHostBinder 把同一网格绑到三种宿主材质（不透明棋盘 / 裁切条纹 / 半透明光斑）。</summary>
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

        private RaylibMaterialHostBinder _binder = null!;
        private Mesh _cube;
        private bool _disposed;

        public string Id => "material_binding";
        public string Title => "材质绑定";
        public string Summary => "RaylibMaterialHostBinder 同网格多材质/混合模式";

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
                new[] { "generated/mat_checker.png" },
                MaterialAssetFlags.None));
            _materials.Register("gallery.mat.stripe", new MaterialAssetDescriptor(
                StripeMaterialId,
                MaterialAssetDomain.Surface,
                new[] { "generated/mat_stripes.png" },
                MaterialAssetFlags.Cutout | MaterialAssetFlags.DoubleSided));
            _materials.Register("gallery.mat.glow", new MaterialAssetDescriptor(
                GlowMaterialId,
                MaterialAssetDomain.Surface,
                new[] { "generated/mat_glow.png" },
                MaterialAssetFlags.Transparent | MaterialAssetFlags.DoubleSided));

            _binder = new RaylibMaterialHostBinder(_paths, _materials);
            _cube = Rl.GenMeshCube(3f, 3f, 3f);

            for (int i = 0; i < _slots.Length; i++)
            {
                Material material = Rl.LoadMaterialDefault();
                if (!_binder.TryApplyHostMaps(ref material, _slots[i].MaterialId))
                {
                    Rl.UnloadMaterial(material);
                    throw new InvalidOperationException($"Gallery material '{_slots[i].Label}' has no host albedo binding.");
                }

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

            Rl.ClearBackground(new Color(16, 18, 26, 255));
            Rl.BeginMode3D(camera);
            Rl.DrawGrid(20, 3f);

            for (int i = 0; i < _slots.Length; i++)
            {
                DrawBoundCube(i, new Vector3((i - 1) * 7.5f, 3.4f, 0f), spin, Color.WHITE);
                DrawBoundCube(i, new Vector3((i - 1) * 7.5f, 0.9f, -6.5f), -spin * 0.7f, new Color(150, 190, 255, 255));
                Rl.DrawCube(new Vector3((i - 1) * 7.5f, 0.1f, 0f), 4.6f, 0.2f, 4.6f, _slots[i].Panel);
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
            Material material = _boundMaterials[slotIndex];
            if (material.maps != null)
            {
                material.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO].color = tint;
            }

            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, yawRad) *
                Matrix4x4.CreateTranslation(position));

            if (_blendModes[slotIndex] == MaterialBlendMode.AlphaBlend)
            {
                Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
                Rl.DrawMesh(_cube, material, transform);
                Rl.EndBlendMode();
                return;
            }

            Rl.DrawMesh(_cube, material, transform);
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
                _boundMaterials[i].shader = default;
                Rl.UnloadMaterial(_boundMaterials[i]);
            }

            _binder?.Dispose();
            _binder = null!;
            _disposed = true;
        }
    }
}
