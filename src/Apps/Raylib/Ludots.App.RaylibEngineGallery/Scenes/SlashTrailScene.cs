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
    /// base/tip 世界坐标，按寿命淘汰并折算 age01，渲染走 Ludots.Raylib.Render 的
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
        private readonly GalleryLitProps _litProps = new();
        private readonly RaylibSkyboxRenderer _skybox = new();
        private readonly TrailMeshSample[] _history = new TrailMeshSample[MaxTrailSamples];
        private readonly float[] _historyTimes = new float[MaxTrailSamples];
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private int _historyCount;
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
            _time += 1f / 60f;
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

            float phase = _time % SwingPeriodSeconds;
            float swingT = Math.Clamp(phase / SwingDurationSeconds, 0f, 1f);
            float angle = StartAngleRad + ((EndAngleRad - StartAngleRad) * EaseOutCubic(swingT));
            Vector3 direction = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
            Vector3 bladeBase = Pivot + (direction * BladeBaseRadius);
            Vector3 bladeTip = Pivot + (direction * BladeTipRadius);

            Rl.DrawLine3D(bladeBase, bladeTip, new Color(235, 245, 255, 255));

            bool emitting = phase < SwingDurationSeconds;
            UpdateTrailHistory(bladeBase, bladeTip, emitting);
            EmitTrailBuffer();
            if (_historyCount > 0 && Math.Abs(_time - 2.0f) < 0.001f)
            {
                Console.WriteLine($"PROBE trails={_trails.Count} hist={_historyCount} s0=({_history[0].Base.X:F2},{_history[0].Base.Y:F2},{_history[0].Base.Z:F2})->({_history[0].Tip.X:F2},{_history[0].Tip.Y:F2},{_history[0].Tip.Z:F2}) age0={_history[0].Age01:F2} ageN={_history[_historyCount - 1].Age01:F2}");
                for (int k = 0; k < _historyCount; k += 5)
                {
                    Console.WriteLine($"PROBE s{k}=({_history[k].Base.X:F3},{_history[k].Base.Z:F3})->({_history[k].Tip.X:F3},{_history[k].Tip.Z:F3}) t={_historyTimes[k]:F3}");
                }

                Span<TrailMeshVertex> probeVerts = stackalloc TrailMeshVertex[TrailMeshGeometry.MaxStripVertices];
                int n = TrailMeshGeometry.WriteTrailStrip(_trails.GetSamples(0), _trails.GetHeadColor(0), _trails.GetTailColor(0), probeVerts);
                Console.WriteLine($"PROBE verts={n} v0=({probeVerts[0].Position.X:F2},{probeVerts[0].Position.Y:F2},{probeVerts[0].Position.Z:F2}) c0=({probeVerts[0].Color.X:F2},{probeVerts[0].Color.Y:F2},{probeVerts[0].Color.Z:F2},{probeVerts[0].Color.W:F2}) nan={float.IsNaN(probeVerts[0].Color.W) || float.IsNaN(probeVerts[0].Position.X)}");
            }

            RaylibTrailMeshRenderer.DrawTrailMeshes(_trails);

            // PROBE2: same strip via high-level DrawTriangle3D in solid magenta.
            if (_trails.Count > 0)
            {
                Span<TrailMeshVertex> p2 = stackalloc TrailMeshVertex[TrailMeshGeometry.MaxStripVertices];
                int n2 = TrailMeshGeometry.WriteTrailStrip(_trails.GetSamples(0), _trails.GetHeadColor(0), _trails.GetTailColor(0), p2);
                var magenta = new Color(255, 0, 255, 255);
                var lift = new Vector3(0f, 1.4f, 0f);
                Rl.rlDisableBackfaceCulling();
                Rl.DrawTriangle3D(new Vector3(-3f, 2.5f, -1f), new Vector3(3f, 2.5f, -1f), new Vector3(0f, 2.5f, 3f), magenta);
                for (int t = 0; t + 2 < n2; t += 3)
                {
                    Rl.DrawTriangle3D(p2[t].Position + lift, p2[t + 1].Position + lift, p2[t + 2].Position + lift, magenta);
                }
                Rl.rlEnableBackfaceCulling();
                Console.WriteLine($"PROBE2 n2={n2}");
            }

            // PROBE: giant opaque red triangle via the same rl* immediate path.
            Rl.rlBegin(0x0004);
            Rl.rlColor4ub(255, 0, 0, 255);
            Rl.rlVertex3f(-2f, 2.5f, 0f);
            Rl.rlColor4ub(255, 0, 0, 255);
            Rl.rlVertex3f(2f, 2.5f, 0f);
            Rl.rlColor4ub(255, 0, 0, 255);
            Rl.rlVertex3f(0f, 4.5f, 0f);
            Rl.rlEnd();

            Rl.EndMode3D();
        }

        private void UpdateTrailHistory(in Vector3 bladeBase, in Vector3 bladeTip, bool emitting)
        {
            if (emitting)
            {
                if (_historyCount >= MaxTrailSamples)
                {
                    _historyCount = MaxTrailSamples - 1;
                }

                if (_historyCount > 0)
                {
                    Array.Copy(_history, 0, _history, 1, _historyCount);
                    Array.Copy(_historyTimes, 0, _historyTimes, 1, _historyCount);
                }

                _history[0] = new TrailMeshSample { Base = bladeBase, Tip = bladeTip, Age01 = 0f };
                _historyTimes[0] = _time;
                _historyCount++;
            }

            while (_historyCount > 0 && _time - _historyTimes[_historyCount - 1] > TrailLifetimeSeconds)
            {
                _historyCount--;
            }
        }

        private void EmitTrailBuffer()
        {
            if (_historyCount == 0)
            {
                _trails.Remove(TrailStableId);
                return;
            }

            for (int i = 0; i < _historyCount; i++)
            {
                _history[i].Age01 = Math.Clamp((_time - _historyTimes[i]) / TrailLifetimeSeconds, 0f, 1f);
            }

            if (!_trails.Upsert(TrailStableId, _history.AsSpan(0, _historyCount), in HeadColor, in TailColor))
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
