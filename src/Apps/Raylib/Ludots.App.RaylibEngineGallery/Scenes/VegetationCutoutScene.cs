using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>植被透贴：程序化草丛/树 billboard 纹理经 vegetation_cutout shader 透贴渲染，双面 + alpha 裁切。</summary>
    public sealed class VegetationCutoutScene : IEngineScene
    {
        private const int GrassAssetId = 401;
        private const int TreeAssetId = 402;
        private const int GrassMaterialId = 611;
        private const int TreeMaterialId = 612;

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryMaterialAssets _materials = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private readonly (Vector3 Position, float Scale, int StableId)[] _tufts = BuildTufts();

        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private bool _disposed;

        public string Id => "vegetation_cutout";
        public string Title => "植被透贴";
        public string Summary => "vegetation_cutout shader 程序化草丛 billboard";

        public void Load()
        {
            GalleryTextureFactory.WritePng("grass_billboard.png", 96, 128, (x, y) =>
            {
                float v = y / 127f;
                var random = new Random(x * 131 + y * 17);
                int blades = 9;
                for (int blade = 0; blade < blades; blade++)
                {
                    float center = (blade + 0.5f) / blades;
                    float sway = MathF.Sin(v * 4f + blade) * 0.035f;
                    float halfWidth = (0.55f / blades) * (0.25f + (1f - v) * 0.75f);
                    if (MathF.Abs((x / 95f) - (center + sway)) < halfWidth && v > 0.25f + (blade % 3) * 0.06f)
                    {
                        byte shade = (byte)(96 + ((1f - v) * 120f) + (random.Next(0, 28)));
                        return new Color((byte)(28 + (shade / 4)), shade, (byte)(34 + (shade / 5)), 255);
                    }
                }

                return new Color(0, 0, 0, 0);
            });
            GalleryTextureFactory.WritePng("tree_billboard.png", 128, 192, (x, y) =>
            {
                float u = (x - 64f) / 56f;
                float v = (y - 32f) / 152f;
                bool trunk = MathF.Abs(u) < 0.09f && v > 0.72f;
                bool canopy = MathF.Sqrt((u * u) + ((v - 0.38f) * 1.5f * (v - 0.38f))) < 0.42f - (v * 0.1f);
                if (trunk)
                {
                    return new Color(88, 62, 40, 255);
                }

                if (canopy)
                {
                    float dither = GalleryTextureFactory.HashNoise(x, y, 5);
                    byte green = (byte)(110 + (dither * 90f));
                    return new Color((byte)(24 + (dither * 30f)), green, (byte)(40 + (dither * 26f)), 255);
                }

                return new Color(0, 0, 0, 0);
            });

            _meshes.Register(
                "gallery.grass",
                MeshAssetDescriptor.Billboard(GrassAssetId, "generated/grass_billboard.png"));
            _meshes.Register(
                "gallery.tree",
                MeshAssetDescriptor.Billboard(TreeAssetId, "generated/tree_billboard.png"));
            _materials.Register("gallery.vegetation.grass", new MaterialAssetDescriptor(
                GrassMaterialId,
                MaterialAssetDomain.Surface,
                new[] { "generated/grass_billboard.png" },
                MaterialAssetFlags.Cutout | MaterialAssetFlags.DoubleSided));
            _materials.Register("gallery.vegetation.tree", new MaterialAssetDescriptor(
                TreeMaterialId,
                MaterialAssetDomain.Surface,
                new[] { "generated/tree_billboard.png" },
                MaterialAssetFlags.Cutout | MaterialAssetFlags.DoubleSided));

            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.5f);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Immediate,
                GalleryAssetPaths.Instance,
                _materials,
                channelRegistrar: GalleryAnimationChannels.Register);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 46f);
            camera.target.Y = 3f;

            _lighting.SetDayPhase(0.5f);

            Rl.ClearBackground(new Color(120, 158, 190, 255));
            Rl.BeginMode3D(camera);
            Rl.DrawGrid(30, 3f);
            Rl.DrawCube(new Vector3(0f, -0.1f, 0f), 90f, 0.2f, 90f, new Color(64, 84, 52, 255));

            _primitives.ApplyFrameLighting(_lighting, camera.position);
            _snapshot.BeginFrame();
            foreach ((Vector3 position, float scale, int stableId) in _tufts)
            {
                _snapshot.Add(GalleryItems.Mesh(
                    GrassAssetId,
                    stableId,
                    position,
                    new Vector3(scale * 0.9f, scale, scale * 0.9f),
                    new Vector4(1f, 1f, 1f, 1f),
                    GrassMaterialId));
            }

            for (int i = 0; i < 6; i++)
            {
                float angle = i * MathF.Tau / 6f;
                _snapshot.Add(GalleryItems.Mesh(
                    TreeAssetId,
                    900 + i,
                    new Vector3(MathF.Cos(angle) * 26f, 0f, MathF.Sin(angle) * 26f),
                    new Vector3(7f, 10.5f, 7f),
                    new Vector4(1f, 1f, 1f, 1f),
                    TreeMaterialId));
            }

            _primitives.Draw(_snapshot, camera, _meshes, timeSeconds: totalTimeSeconds);
            Rl.EndMode3D();

            Rl.DrawText($"cutout billboards {_snapshot.Count} (tufts {_tufts.Length} + trees)", 12, 28, 20, GalleryColors.RayWhite);
        }

        private static (Vector3, float, int)[] BuildTufts()
        {
            var tufts = new (Vector3, float, int)[420];
            var random = new Random(77);
            for (int i = 0; i < tufts.Length; i++)
            {
                float angle = random.NextSingle() * MathF.Tau;
                float radius = MathF.Sqrt(random.NextSingle()) * 38f;
                float scale = 0.9f + (random.NextSingle() * 1.1f);
                tufts[i] = (new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius), scale, i + 1);
            }

            return tufts;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _primitives?.Dispose();
            _disposed = true;
        }
    }
}
