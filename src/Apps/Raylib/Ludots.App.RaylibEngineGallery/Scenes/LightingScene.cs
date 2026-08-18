using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 光照全效演示：粗糙度 × 金属度梯度球阵（GGX 单灯解析 BRDF）、昼夜环绕太阳、
    /// 天空半球 IBL 随相位流转、平面投影阴影接收。
    /// </summary>
    public sealed class LightingScene : IEngineScene
    {
        private const int RoughnessSteps = 7;
        private const int MetallicLanes = 3;

        private readonly GalleryLitProps _litProps = new(dayPhase01: 0.35f);
        private RaylibPlanarShadows _shadows = null!;
        private Mesh _podium;
        private bool _disposed;

        public string Id => "lighting";
        public string Title => "光照全效";
        public string Summary => "GGX 粗糙度×金属度梯度 + 环绕太阳 + 天空 IBL + 平面阴影";

        public void Load()
        {
            _litProps.Load();
            _shadows = new RaylibPlanarShadows(alpha: 96) { GroundY = 0.26f };
            _podium = Rl.GenMeshCube(RoughnessSteps * 3.4f, 0.24f, MetallicLanes * 3.4f);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 26f);
            camera.target.Y = 1.6f;

            float dayPhase = (float)(totalTimeSeconds * 0.02 % 1.0);
            _litProps.DayPhase01 = dayPhase;
            _litProps.BeginFrame(camera.position);

            Rl.ClearBackground(new Color(12, 14, 20, 255));
            Rl.BeginMode3D(camera);
            Rl.DrawGrid(40, 3f);

            _litProps.DrawMesh(
                _podium,
                RaylibMatrix.FromScaleTranslation(0f, 0.13f, 0f, 1f, 1f, 1f),
                new Vector4(0.24f, 0.26f, 0.32f, 1f),
                roughness: 0.9f);

            Vector3 sun = _litProps.Lighting.SunDirectionToward;
            for (int m = 0; m < MetallicLanes; m++)
            {
                float metallic = m / (float)(MetallicLanes - 1);
                for (int r = 0; r < RoughnessSteps; r++)
                {
                    float roughness = (r + 0.5f) / RoughnessSteps;
                    Vector3 center = new(
                        -((RoughnessSteps - 1) * 1.7f) + (r * 3.4f),
                        0.85f,
                        -((MetallicLanes - 1) * 1.7f) + (m * 3.4f));
                    Vector4 tint = metallic > 0.5f
                        ? new Vector4(0.92f, 0.74f, 0.38f, 1f)
                        : new Vector4(0.62f, 0.66f, 0.72f, 1f);
                    _litProps.DrawSphere(center, 1.25f, tint, roughness, metallic);

                    if ((r % 2) == 0 && (m % 2) == 0)
                    {
                        _shadows.DrawSphereShadow(center, 1.25f, sun);
                    }
                }
            }

            Rl.EndMode3D();

            float elevation = sun.Y;
            GalleryFont.Draw($"day phase {dayPhase:0.00}  sun Y {elevation:0.00}  rows roughness→  lanes metal↑", 12, 28, 20, GalleryColors.RayWhite);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Rl.UnloadMesh(_podium);
            _shadows?.Dispose();
            _litProps.Dispose();
            _disposed = true;
        }
    }
}
