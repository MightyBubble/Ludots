using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 样条带覆盖层：GroundOverlayBuffer + SplineRibbonBuffer 手工填充，
    /// 绘制统一走 Ludots.Raylib.Render 的 RaylibWorldOverlayRenderer（与宿主同一实现）。
    /// </summary>
    public sealed class RibbonOverlayScene : IEngineScene
    {
        private readonly GroundOverlayBuffer _overlays = new(capacity: 64);
        private readonly SplineRibbonBuffer _ribbons = new(capacity: 16);
        private readonly GalleryLitProps _litProps = new();
        private readonly RaylibSkyboxRenderer _skybox = new();
        private RaylibDirectionalShadowMap _shadowMap = null!;

        public string Id => "ribbon_overlay";
        public string Title => "样条带覆盖层";
        public string Summary => "GroundOverlayBuffer + SplineRibbonBuffer 世界覆盖层";

        public void Load()
        {
            _litProps.Load();
            _shadowMap = new RaylibDirectionalShadowMap();
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 60f);
            float t = (float)totalTimeSeconds;
            _litProps.Lighting.SetDayPhase(_litProps.DayPhase01);

            _shadowMap.BeginFrame(_litProps.Lighting.SunDirectionToward, Vector3.Zero, 42f);
            DrawPropShadows();
            _shadowMap.EndFrame();

            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_litProps.Lighting, sizeMeters: 1300f);
            Rl.ClearBackground(skyConfig.Skybox.ClearColor);
            _litProps.BeginFrame(camera.position, _shadowMap, shadowTexelWorld: 0.08f);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);
            DrawProps();

            FillOverlays(t);
            RaylibWorldOverlayRenderer.DrawGroundOverlays(_overlays);

            FillRibbons(t);
            RaylibWorldOverlayRenderer.DrawSplineRibbons(_ribbons);

            Rl.EndMode3D();
        }

        private void DrawProps()
        {
            _litProps.DrawCube(new Vector3(0f, -0.08f, 0f), new Vector3(70f, 0.16f, 70f), GalleryColors.ShadowReceiverGray, roughness: 0.9f);
            _litProps.DrawCube(new Vector3(-12f, 0.9f, 6f), new Vector3(2.4f, 1.8f, 2.4f), new Vector4(0.29f, 0.43f, 0.66f, 1f));
            _litProps.DrawCube(new Vector3(13f, 0.9f, -8f), new Vector3(2.4f, 1.8f, 2.4f), new Vector4(0.66f, 0.38f, 0.29f, 1f));
        }

        private void DrawPropShadows()
        {
            _litProps.DrawCubeShadow(_shadowMap, new Vector3(-12f, 0.9f, 6f), new Vector3(2.4f, 1.8f, 2.4f));
            _litProps.DrawCubeShadow(_shadowMap, new Vector3(13f, 0.9f, -8f), new Vector3(2.4f, 1.8f, 2.4f));
        }

        private void FillOverlays(float t)
        {
            _overlays.ClearTransient();
            _overlays.Upsert(new GroundOverlayItem
            {
                StableId = 1,
                Shape = GroundOverlayShape.Ring,
                Center = new Vector3(-12f, 0.06f, 6f),
                Radius = 6.5f + (MathF.Sin(t * 1.4f) * 1.1f),
                InnerRadius = 4.2f + (MathF.Sin(t * 1.4f) * 0.8f),
                FillColor = new Vector4(0.30f, 0.62f, 1.00f, 0.34f),
                BorderColor = new Vector4(0.45f, 0.78f, 1.00f, 0.85f),
                BorderWidth = 0.12f,
            });
            _overlays.Upsert(new GroundOverlayItem
            {
                StableId = 2,
                Shape = GroundOverlayShape.Cone,
                Center = new Vector3(-12f, 0.06f, 6f),
                Radius = 12f,
                Angle = 0.42f,
                Rotation = t * 0.9f,
                FillColor = new Vector4(1.00f, 0.72f, 0.25f, 0.22f),
                BorderColor = new Vector4(1.00f, 0.80f, 0.35f, 0.8f),
                BorderWidth = 0.1f,
            });
            _overlays.Upsert(new GroundOverlayItem
            {
                StableId = 3,
                Shape = GroundOverlayShape.Line,
                Center = new Vector3(-12f, 0.06f, 6f),
                Rotation = MathF.Atan2(-8f - 6f, 13f - -12f),
                Length = Vector3.Distance(new Vector3(-12f, 0f, 6f), new Vector3(13f, 0f, -8f)),
                Width = 1.1f,
                FillColor = new Vector4(0.85f, 0.35f, 0.35f, 0.25f),
                BorderColor = new Vector4(0.95f, 0.45f, 0.45f, 0.8f),
                BorderWidth = 0.1f,
            });
            _overlays.Upsert(new GroundOverlayItem
            {
                StableId = 4,
                Shape = GroundOverlayShape.Circle,
                Center = new Vector3(13f, 0.06f, -8f),
                Radius = 5.2f + (MathF.Sin(t * 2f + 1f) * 0.5f),
                FillColor = new Vector4(1.00f, 0.45f, 0.55f, 0.20f),
                BorderColor = new Vector4(1.00f, 0.55f, 0.62f, 0.85f),
                BorderWidth = 0.12f,
            });
        }

        private void FillRibbons(float t)
        {
            _ribbons.Clear();
            _ribbons.TryAdd(
                1,
                new Vector3(-12f, 0.1f, 6f),
                new Vector3(-6f + MathF.Sin(t * 0.8f) * 3f, 0.1f, 4f),
                new Vector3(4f, 0.1f, -1f + MathF.Cos(t * 0.7f) * 3f),
                new Vector3(13f, 0.1f, -8f),
                1.4f,
                new Vector4(0.35f, 0.95f, 0.65f, 0.75f),
                new Vector4(0.6f, 1f, 0.8f, 0.9f),
                0.14f);
            _ribbons.TryAdd(
                2,
                new Vector3(-18f, 0.1f, -12f),
                new Vector3(-8f, 0.1f, -16f + MathF.Sin(t * 1.1f) * 2.5f),
                new Vector3(4f, 0.1f, -16f - MathF.Sin(t * 0.9f) * 2.5f),
                new Vector3(16f, 0.1f, -12f),
                0.8f,
                new Vector4(0.55f, 0.65f, 1.00f, 0.7f),
                new Vector4(0.75f, 0.82f, 1f, 0.9f),
                0.1f);
            _ribbons.TryAdd(
                3,
                new Vector3(-16f, 0.1f, 14f),
                new Vector3(-4f + MathF.Sin(t * 1.3f) * 2f, 0.1f, 17f),
                new Vector3(8f, 0.1f, 16f + MathF.Cos(t * 1.1f) * 2f),
                new Vector3(17f, 0.1f, 13f),
                0.55f,
                new Vector4(1.00f, 0.85f, 0.35f, 0.7f),
                new Vector4(1f, 0.92f, 0.55f, 0.9f),
                0.08f);
        }

        public void Dispose()
        {
            _shadowMap?.Dispose();
            _skybox.Dispose();
            _litProps.Dispose();
        }
    }
}
