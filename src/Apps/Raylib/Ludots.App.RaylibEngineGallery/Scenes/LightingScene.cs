using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 光照全效演示：粗糙度 × 金属度梯度球阵（GGX 单灯解析 BRDF）、昼夜环绕太阳、
    /// split-sum 天空 IBL（预滤波环境立方图随相位重烘）映照金属带、shadow map 深度阴影接收。
    /// 天空太阳圆盘、GGX 主光、阴影投射共用同一 SunDirectionToward——看到的光斑即灯光即阴影源。
    /// 相位弧线限定在相机侧方的白昼区间（0.58–0.68），保证首帧即可看清落地阴影。
    /// </summary>
    public sealed class LightingScene : IEngineScene
    {
        private const int RoughnessSteps = 7;
        private const int MetallicLanes = 3;
        private const float SphereRadius = 1.25f;
        private const float PodiumTopY = 0.25f;
        private const float SphereCenterY = PodiumTopY + SphereRadius;
        private const float DayPhaseBase = 0.58f;
        private const float DayPhaseSpan = 0.10f;

        private readonly GalleryLitProps _litProps = new(dayPhase01: DayPhaseBase);
        private readonly RaylibSkyboxRenderer _skybox = new();
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private Mesh _podium;
        private Mesh _shadowMesh;
        private bool _disposed;

        public string Id => "lighting";
        public string Title => "光照全效";
        public string Summary => "GGX 粗糙度×金属度梯度 + 环绕太阳 + split-sum 天空 IBL + 深度阴影";

        public void Load()
        {
            _litProps.Load();
            _shadowMap = new RaylibDirectionalShadowMap();
            _shadowMesh = Rl.GenMeshSphere(0.5f, 24, 16);
            _podium = Rl.GenMeshCube(RoughnessSteps * 3.4f, 0.24f, MetallicLanes * 3.4f);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 26f);
            camera.target.Y = 1.6f;

            float dayPhase = DayPhaseBase + (float)(totalTimeSeconds * 0.012 % DayPhaseSpan);
            _litProps.DayPhase01 = dayPhase;

            _litProps.Lighting.SetDayPhase(dayPhase);
            Vector3 sun = _litProps.Lighting.SunDirectionToward;
            _shadowMap.BeginFrame(sun, new Vector3(0f, 1.2f, 0f), 16f);
            for (int m = 0; m < MetallicLanes; m++)
            {
                for (int r = 0; r < RoughnessSteps; r++)
                {
                    Vector3 center = new(
                        -((RoughnessSteps - 1) * 1.7f) + (r * 3.4f),
                        SphereCenterY,
                        -((MetallicLanes - 1) * 1.7f) + (m * 3.4f));
                    Matrix4x4 rowMajor = Matrix4x4.CreateScale(2.5f) * Matrix4x4.CreateTranslation(center);
                    _shadowMap.DrawMeshShadow(_shadowMesh, RaylibMatrix.FromSystemNumerics(rowMajor));
                }
            }

            _shadowMap.EndFrame();
            _litProps.BeginFrame(camera.position, _shadowMap, shadowTexelWorld: 0.05f);

            var skyConfig = RaylibRenderEnvironmentConfig.CreateDefault() with
            {
                Skybox = new RaylibSkyboxConfig(
                    Enabled: true,
                    SizeMeters: 1200f,
                    ZenithColor: new Vector3(0.10f, 0.30f, 0.62f),
                    HorizonColor: new Vector3(0.84f, 0.72f, 0.58f),
                    GroundHazeColor: new Vector3(0.46f, 0.42f, 0.38f),
                    ClearColor: new Color(120, 150, 180, 255),
                    DeepClearColor: new Color(6, 10, 16, 255)),
                Lighting = RaylibLightingConfig.CreateDefault() with
                {
                    SunDirection = sun,
                    SunColor = new Vector3(1f, 0.93f, 0.78f),
                },
            };

            Rl.ClearBackground(new Color(12, 14, 20, 255));
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, (float)totalTimeSeconds, skyConfig);

            Rl.DrawGrid(40, 3f);

            _litProps.DrawMesh(
                _podium,
                RaylibMatrix.FromScaleTranslation(0f, 0.13f, 0f, 1f, 1f, 1f),
                new Vector4(0.62f, 0.63f, 0.68f, 1f),
                roughness: 0.9f);

            for (int m = 0; m < MetallicLanes; m++)
            {
                float metallic = m / (float)(MetallicLanes - 1);
                for (int r = 0; r < RoughnessSteps; r++)
                {
                    float roughness = (r + 0.5f) / RoughnessSteps;
                    Vector3 center = new(
                        -((RoughnessSteps - 1) * 1.7f) + (r * 3.4f),
                        SphereCenterY,
                        -((MetallicLanes - 1) * 1.7f) + (m * 3.4f));
                    Vector4 tint = metallic > 0.5f
                        ? new Vector4(0.92f, 0.74f, 0.38f, 1f)
                        : new Vector4(0.62f, 0.66f, 0.72f, 1f);
                    _litProps.DrawSphere(center, SphereRadius, tint, roughness, metallic);
                }
            }

            Rl.EndMode3D();

            float elevationDeg = MathF.Asin(Math.Clamp(sun.Y, -1f, 1f)) * (180f / MathF.PI);
            GalleryFont.Draw(
                $"day phase {dayPhase:0.00}  sun elev {elevationDeg:0}°  阴影随太阳弧线扫过基座  rows roughness→  lanes metal↑",
                12, 28, 20, GalleryColors.RayWhite);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Rl.UnloadMesh(_podium);
            _shadowMap?.Dispose();
            Rl.UnloadMesh(_shadowMesh);
            _skybox.Dispose();
            _litProps.Dispose();
            _disposed = true;
        }
    }
}
