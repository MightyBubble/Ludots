using System;
using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 刀光轨迹：脚本化水平挥砍驱动 TrailMeshBuffer——挥砍窗口内逐帧记录刀刃
    /// base/tip 世界坐标，按寿命淘汰并折算 age01。头插/寿命淘汰/age01 折算复用
    /// 共享纯工具 TrailSampleHistory（与 Core 的 TrailMeshRuntime 同一实现），
    /// 画廊不持有第二套采样语义；渲染走 Ludots.Raylib.Render 的
    /// RaylibTrailMeshRenderer（与宿主同一实现）。场景时间用固定 1/60 步进而非真实
    /// 帧间隔：headless 截图帧（第 120 帧）必须稳定落在挥砍末段，保证验收可复现。
    /// </summary>
    public sealed class SlashTrailScene : IEngineScene
    {
        private const int TrailStableId = 1;
        private const float SwingPeriodSeconds = 1.6f;
        private const float SwingDurationSeconds = 0.45f;
        private const float TrailLifetimeSeconds = 0.35f;
        private const int MaxTrailSamples = 24;
        private const float StartAngleRad = -0.96f;
        private const float EndAngleRad = 1.31f;
        private const float BladeBaseRadius = 0.25f;
        private const float BladeTipRadius = 1.45f;

        private static readonly Vector3 Pivot = new(0f, 1.1f, 0f);
        private static readonly Vector4 HeadColor = new(0.78f, 0.93f, 1.00f, 0.95f);
        private static readonly Vector4 TailColor = new(0.22f, 0.42f, 1.00f, 0.00f);

        private readonly TrailMeshBuffer _trails = new(capacity: 8);
        // 注意：TrailSampleHistory 是含可变状态的 struct，字段必须可写；readonly 字段会让
        // PushHead/EvictOlderThan/AgeTo 落在防御性副本上，Count 永远为 0，画廊什么都画不出来。
        private TrailSampleHistory _history = new(MaxTrailSamples);
        private readonly GalleryLitProps _litProps = new();
        private readonly RaylibSkyboxRenderer _skybox = new();
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private float _time;

        public string Id => "slash_trail";
        public string Title => "刀光轨迹";
        public string Summary => "TrailMeshBuffer 武器弧形 mesh 拖尾 + 顶点色渐隐";

        public void Load()
        {
            _litProps.Load();
            _shadowMap = new RaylibDirectionalShadowMap();
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 9f);
            _litProps.Lighting.SetDayPhase(_litProps.DayPhase01);

            _shadowMap.BeginFrame(_litProps.Lighting.SunDirectionToward, Vector3.Zero, 8f);
            _litProps.DrawCubeShadow(_shadowMap, new Vector3(0f, 0.55f, 0f), new Vector3(0.5f, 1.1f, 0.5f));
            _shadowMap.EndFrame();

            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_litProps.Lighting, sizeMeters: 1300f);
            Rl.ClearBackground(skyConfig.Skybox.ClearColor);
            _litProps.BeginFrame(camera.position, _shadowMap, shadowTexelWorld: 0.02f);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);

            _litProps.DrawCube(new Vector3(0f, -0.08f, 0f), new Vector3(12f, 0.16f, 12f), GalleryColors.ShadowReceiverGray, roughness: 0.9f);
            _litProps.DrawCube(new Vector3(0f, 0.55f, 0f), new Vector3(0.5f, 1.1f, 0.5f), new Vector4(0.30f, 0.32f, 0.38f, 1f), roughness: 0.6f);

            SimulateTrailFrame(out Vector3 bladeBase, out Vector3 bladeTip);
            Rl.DrawLine3D(bladeBase, bladeTip, new Color(235, 245, 255, 255));

            RaylibTrailMeshRenderer.DrawTrailMeshes(_trails);

            Rl.EndMode3D();
        }

        /// <summary>
        /// 推进一帧刀光模拟（纯数据，无任何渲染调用）：固定 1/60 步进计算挥砍相位与刀刃
        /// 世界坐标，挥砍窗口内头插样本、超寿命尾部淘汰、age01 折算并 upsert 进
        /// TrailMeshBuffer。画廊 Draw 每帧调用；headless 回归测试直接驱动本方法验证
        /// 采样语义（PushHead 必须真的改变 Count，且一帧结束缓冲里必须有条带）。
        /// </summary>
        internal void SimulateTrailFrame(out Vector3 bladeBase, out Vector3 bladeTip)
        {
            _time += 1f / 60f;
            float phase = _time % SwingPeriodSeconds;
            float swingT = Math.Clamp(phase / SwingDurationSeconds, 0f, 1f);
            float angle = StartAngleRad + ((EndAngleRad - StartAngleRad) * EaseOutCubic(swingT));
            Vector3 direction = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
            bladeBase = Pivot + (direction * BladeBaseRadius);
            bladeTip = Pivot + (direction * BladeTipRadius);

            if (phase < SwingDurationSeconds)
            {
                _history.PushHead(bladeBase, bladeTip, _time, MaxTrailSamples);
            }

            _history.EvictOlderThan(_time, TrailLifetimeSeconds);
            EmitTrailBuffer();
        }

        /// <summary>画廊的 TrailMeshBuffer 快照出口；headless 回归测试用于断言帧末条带。</summary>
        internal TrailMeshBuffer TrailBuffer => _trails;

        private void EmitTrailBuffer()
        {
            if (_history.Count == 0)
            {
                _trails.Remove(TrailStableId);
                return;
            }

            _history.AgeTo(_time, TrailLifetimeSeconds);
            if (!_trails.Upsert(TrailStableId, _history.Samples, in HeadColor, in TailColor))
            {
                throw new InvalidOperationException($"TrailMeshBuffer overflowed in gallery scene '{Id}'.");
            }
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - Math.Clamp(t, 0f, 1f);
            return 1f - (u * u * u);
        }

        public void Dispose()
        {
            _shadowMap?.Dispose();
            _skybox.Dispose();
            _litProps.Dispose();
        }
    }
}
