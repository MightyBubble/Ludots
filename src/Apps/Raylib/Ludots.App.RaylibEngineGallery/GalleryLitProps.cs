using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery
{
    /// <summary>
    /// 画廊共享的带光照道具通道：单一 RaylibLitModel 实例 + 昼夜光照，
    /// 供各场景把手工道具（立方体/球）从 unlit 默认材质切到 GGX + 天空 IBL 绘制。
    /// </summary>
    public sealed class GalleryLitProps : IDisposable
    {
        private RaylibLitModel _lit = null!;
        private RaylibFrameLighting _lighting = null!;
        private Mesh _cube;
        private Mesh _sphere;
        private bool _loaded;

        public GalleryLitProps(float dayPhase01 = 0.55f)
        {
            DayPhase01 = dayPhase01;
        }

        public float DayPhase01 { get; set; }

        public RaylibFrameLighting Lighting => _lighting;

        public RaylibLitModel Lit => _lit;

        public void Load()
        {
            if (_loaded)
            {
                return;
            }

            _lit = new RaylibLitModel();
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: DayPhase01);
            _cube = Rl.GenMeshCube(1f, 1f, 1f);
            _sphere = Rl.GenMeshSphere(0.5f, 24, 16);
            _loaded = true;
        }

        public void BeginFrame(Vector3 viewPos, Ludots.Raylib.Render.RaylibDirectionalShadowMap? shadow = null, float shadowTexelWorld = 0.04f)
        {
            EnsureLoaded();
            _lighting.SetDayPhase(DayPhase01);
            _lit.BeginFrame(_lighting, viewPos, shadow, shadowTexelWorld);
        }

        public void DrawMesh(Mesh mesh, RaylibMatrix transform, Vector4 tint, float roughness = 0.8f, float metallic = 0f)
        {
            EnsureLoaded();
            _lit.DrawMesh(mesh, transform, tint, roughness, metallic);
        }

        public void DrawCube(Vector3 center, Vector3 size, Vector4 tint, float roughness = 0.8f, float metallic = 0f, float rotationYRad = 0f)
        {
            EnsureLoaded();
            Matrix4x4 rowMajor =
                Matrix4x4.CreateRotationY(rotationYRad) *
                Matrix4x4.CreateScale(size) *
                Matrix4x4.CreateTranslation(center);
            _lit.DrawMesh(_cube, RaylibMatrix.FromSystemNumerics(rowMajor), tint, roughness, metallic);
        }

        public void DrawSphere(Vector3 center, float radius, Vector4 tint, float roughness = 0.5f, float metallic = 0f)
        {
            EnsureLoaded();
            Matrix4x4 rowMajor =
                Matrix4x4.CreateScale(radius * 2f) *
                Matrix4x4.CreateTranslation(center);
            _lit.DrawMesh(_sphere, RaylibMatrix.FromSystemNumerics(rowMajor), tint, roughness, metallic);
        }

        public void Dispose()
        {
            if (!_loaded)
            {
                return;
            }

            Rl.UnloadMesh(_cube);
            Rl.UnloadMesh(_sphere);
            _lit.Dispose();
            _loaded = false;
        }

        private void EnsureLoaded()
        {
            if (!_loaded)
            {
                throw new InvalidOperationException($"{nameof(GalleryLitProps)} requires {nameof(Load)} first.");
            }
        }
    }
}
