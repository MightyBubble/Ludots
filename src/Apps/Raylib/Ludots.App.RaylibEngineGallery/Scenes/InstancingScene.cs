using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>GPU 实例化合批：IRaylibBenchmarkRenderer 驱动 30k 纯数据 RaylibBenchmarkInstance 网格阵。</summary>
    public sealed class InstancingScene : IEngineScene
    {
        private const int TargetInstances = 30_000;
        private const int CubeAssetId = 101;
        private const int SphereAssetId = 102;

        private readonly GalleryMeshAssets _meshes = new();
        private readonly RaylibSkyboxRenderer _skybox = new();
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibBenchmarkRenderer _benchmark = null!;
        private RaylibFrameLighting _lighting = null!;
        private RaylibLitModel _lit = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private Mesh _groundMesh;
        private bool _sceneInstalled;
        private bool _disposed;

        public string Id => "instancing";
        public string Title => "GPU 实例化合批";
        public string Summary => "IRaylibBenchmarkRenderer 30k 纯数据实例阵";

        public void Load()
        {
            _meshes.Register("gallery.cube", MeshAssetDescriptor.Primitive(CubeAssetId, PrimitiveMeshKind.Cube));
            _meshes.Register("gallery.sphere", MeshAssetDescriptor.Primitive(SphereAssetId, PrimitiveMeshKind.Sphere));

            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.5f);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Instanced,
                vfs: null,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register);
            _benchmark = new RaylibBenchmarkRenderer(_primitives, _meshes);
            _lit = new RaylibLitModel();
            _shadowMap = new RaylibDirectionalShadowMap();
            _groundMesh = RaylibNativeResources.GenMeshCube(440f, 0.3f, 150f);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            if (!_sceneInstalled)
            {
                _benchmark.SetScene(BuildScene());
                _sceneInstalled = true;
            }

            _lighting.SetDayPhase(0.5f);
            _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(0f, 1f, 0f), 230f);
            _benchmark.DrawShadow(camera, _shadowMap);
            _shadowMap.EndFrame();

            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_lighting, sizeMeters: 2600f);
            Rl.ClearBackground(skyConfig.Skybox.ClearColor);
            _lit.BeginFrame(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.55f);
            _primitives.ApplyFrameLighting(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.55f);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);
            _lit.DrawMesh(
                _groundMesh,
                RaylibMatrix.FromScaleTranslation(0f, -0.15f, 0f, 1f, 1f, 1f),
                GalleryColors.ShadowReceiverGray,
                roughness: 0.88f,
                metallic: 0f);
            _benchmark.Draw(camera);
            Rl.EndMode3D();

            RaylibBenchmarkStats stats = _benchmark.LastStats;
            GalleryFont.Draw(
                $"instances {stats.VisibleCount}  buckets {stats.BucketCount}  draw {stats.CpuDrawMs:0.00}ms",
                12,
                28,
                20,
                GalleryColors.RayWhite);
        }

        private RaylibBenchmarkScene BuildScene()
        {
            var instances = new RaylibBenchmarkInstance[TargetInstances];
            var palette = new RaylibBenchmarkMaterialColor[6];
            var paletteColors = new Vector4[]
            {
                new(0.90f, 0.35f, 0.30f, 1f),
                new(0.95f, 0.70f, 0.28f, 1f),
                new(0.36f, 0.78f, 0.44f, 1f),
                new(0.32f, 0.60f, 0.92f, 1f),
                new(0.76f, 0.48f, 0.90f, 1f),
                new(0.88f, 0.88f, 0.92f, 1f),
            };
            for (int i = 0; i < palette.Length; i++)
            {
                palette[i] = new RaylibBenchmarkMaterialColor(500 + i, paletteColors[i]);
            }

            const int columns = 300;
            const int rows = 100;
            float spacing = 1.4f;
            float offsetX = -(columns - 1) * spacing * 0.5f;
            float offsetZ = -(rows - 1) * spacing * 0.5f;
            for (int i = 0; i < TargetInstances; i++)
            {
                int x = i % columns;
                int z = i / columns;
                bool cube = ((x + z) & 1) == 0;
                float wave = MathF.Sin((x * 0.11f) + (z * 0.17f));
                instances[i] = new RaylibBenchmarkInstance(
                    meshAssetId: cube ? CubeAssetId : SphereAssetId,
                    materialId: 500 + ((x / 50 + z / 50) % 6),
                    position: new Vector3(offsetX + (x * spacing), 0.6f + (wave * 0.8f), offsetZ + (z * spacing)),
                    rotation: Quaternion.Identity,
                    scale: new Vector3(0.9f, 0.9f + (0.5f * (wave + 1f) * 0.5f), 0.9f),
                    color: paletteColors[(x / 50 + z / 50) % 6]);
            }

            return new RaylibBenchmarkScene(
                enabled: true,
                instances: instances,
                initialActiveInstanceCount: TargetInstances,
                palette: new RaylibBenchmarkMaterialPalette(new Vector4(1f, 1f, 1f, 1f), palette),
                camera: new RaylibBenchmarkCamera(
                    position: new Vector3(0f, 120f, 210f),
                    target: new Vector3(0f, 0f, 0f),
                    fovY: 55f),
                label: "Gallery 30k instanced grid");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _primitives?.Dispose();
            _lit?.Dispose();
            _shadowMap?.Dispose();
            _skybox.Dispose();
            RaylibNativeResources.UnloadMesh(_groundMesh);
            _primitives = null!;
            _lit = null!;
            _shadowMap = null!;
            _benchmark = null!;
            _disposed = true;
        }
    }
}
