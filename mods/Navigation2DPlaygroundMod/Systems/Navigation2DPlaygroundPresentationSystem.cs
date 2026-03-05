using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace Navigation2DPlaygroundMod.Systems
{
    public sealed class Navigation2DPlaygroundPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly DebugDrawCommandBuffer _debugDraw;
        private const int DesiredVelocityDrawStride = 8;

        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<NavAgent2D, VisualTransform, NavPlaygroundTeam>();

        private static readonly QueryDescription _desiredVelQuery = new QueryDescription()
            .WithAll<NavAgent2D, VisualTransform, NavDesiredVelocity2D, NavPlaygroundTeam>();

        public Navigation2DPlaygroundPresentationSystem(GameEngine engine, DebugDrawCommandBuffer debugDraw)
        {
            _engine = engine;
            _world = engine.World;
            _debugDraw = debugDraw;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float deltaTime)
        {
            if (!Navigation2DPlaygroundState.Enabled) return;

            _debugDraw.Clear();
            AppendSolidPrimitives();
            AppendDesiredVelocity();
            AppendFlowFieldDebug();
            AppendControlOverlay();
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }

        private void AppendSolidPrimitives()
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationPrimitiveDrawBuffer.Name, out var drawObj)) return;
            if (drawObj is not PrimitiveDrawBuffer draw) return;

            foreach (ref var chunk in _world.Query(in _query))
            {
                var visuals = chunk.GetSpan<VisualTransform>();
                var teams = chunk.GetSpan<NavPlaygroundTeam>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    ref var vt = ref visuals[i];
                    var color = teams[i].Id == 0 ? new Vector4(0.1f, 0.9f, 0.2f, 1f) : new Vector4(0.95f, 0.15f, 0.2f, 1f);

                    draw.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = PrimitiveMeshAssetIds.Sphere,
                        Position = new Vector3(vt.Position.X, 0.25f, vt.Position.Z),
                        Scale = new Vector3(0.6f, 0.6f, 0.6f),
                        Color = color
                    });
                }
            }
        }

        private void AppendDesiredVelocity()
        {
            foreach (ref var chunk in _world.Query(in _desiredVelQuery))
            {
                var visuals = chunk.GetSpan<VisualTransform>();
                var desired = chunk.GetSpan<NavDesiredVelocity2D>();
                var teams = chunk.GetSpan<NavPlaygroundTeam>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (DesiredVelocityDrawStride > 1 && (chunk.Entity(i).Id % DesiredVelocityDrawStride) != 0) continue;

                    ref var vt = ref visuals[i];
                    Vector2 from = new Vector2(vt.Position.X, vt.Position.Z);
                    Vector2 dv = desired[i].ValueCmPerSec.ToVector2() * 0.005f;
                    Vector2 to = from + dv;

                    _debugDraw.Lines.Add(new DebugDrawLine2D
                    {
                        A = from,
                        B = to,
                        Thickness = 1f,
                        Color = teams[i].Id == 0 ? DebugDrawColor.Green : DebugDrawColor.Red
                    });
                }
            }
        }

        private void AppendFlowFieldDebug()
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.Navigation2DRuntime.Name, out var navObj)) return;
            if (navObj is not Navigation2DRuntime nav) return;
            if (!nav.FlowDebugEnabled) return;

            int beforeLines = _debugDraw.Lines.Count;

            int mode = nav.FlowDebugMode;
            int startFlow = mode == 1 ? 0 : (mode == 2 ? 1 : 0);
            int endFlowExclusive = mode == 1 ? 1 : (mode == 2 ? 2 : nav.FlowCount);

            // Fix4: 表现层只做只读采样，不调用 SetGoalPoint/Step
            // 模拟层 (Navigation2DSteeringSystem2D) 已负责驱动 flowfield 计算

            // Fix6: 缩小箭头间距(12→4m)和箭头长度(4→1.5m)，更好匹配agent尺寸
            float halfM = 90f;
            float stepM = 4f;
            float arrowLenM = 1.5f;
            Fix64 maxSpeedCmPerSec = Fix64.FromInt(800);

            for (float y = -halfM; y <= halfM + 0.001f; y += stepM)
            {
                for (float x = -halfM; x <= halfM + 0.001f; x += stepM)
                {
                    for (int flowId = startFlow; flowId < endFlowExclusive; flowId++)
                    {
                        var flow = nav.TryGetFlow(flowId);
                        if (flow == null) continue;

                        Fix64Vec2 posCm = Fix64Vec2.FromFloat(x * 100f, y * 100f);
                        if (!flow.TrySampleDesiredVelocityCm(posCm, maxSpeedCmPerSec, out Fix64Vec2 desiredCmPerSec)) continue;

                        Vector2 dir = desiredCmPerSec.ToVector2();
                        float len = dir.Length();
                        if (len <= 1e-4f) continue;
                        dir /= len;

                        float offsetX = flowId == 1 ? 0.6f : -0.6f;
                        Vector2 from = new Vector2(x + offsetX, y);
                        Vector2 to = from + dir * arrowLenM;

                        AddArrow(from, to, flowId == 0 ? DebugDrawColor.Cyan : DebugDrawColor.Yellow);
                    }
                }
            }

            _engine.SetService(Navigation2DPlaygroundKeys.FlowDebugLines, _debugDraw.Lines.Count - beforeLines);
        }

        private void AddArrow(Vector2 from, Vector2 to, DebugDrawColor color)
        {
            _debugDraw.Lines.Add(new DebugDrawLine2D { A = from, B = to, Thickness = 1f, Color = color });

            Vector2 dir = to - from;
            float len = dir.Length();
            if (len <= 1e-6f) return;
            dir /= len;

            float headLen = 1.0f;
            float a = 0.43633232f;
            float c = MathF.Cos(a);
            float s = MathF.Sin(a);

            Vector2 left = new Vector2(dir.X * c - dir.Y * s, dir.X * s + dir.Y * c);
            Vector2 right = new Vector2(dir.X * c + dir.Y * s, -dir.X * s + dir.Y * c);

            _debugDraw.Lines.Add(new DebugDrawLine2D { A = to, B = to - left * headLen, Thickness = 1f, Color = color });
            _debugDraw.Lines.Add(new DebugDrawLine2D { A = to, B = to - right * headLen, Thickness = 1f, Color = color });
        }

        private void AppendControlOverlay()
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) ||
                overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            bool flowEnabled = false;
            bool flowDebug = false;
            int flowMode = 0;
            int flowIters = 0;
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.Navigation2DRuntime.Name, out var navObj) &&
                navObj is Navigation2DRuntime navRuntime)
            {
                flowEnabled = navRuntime.FlowEnabled;
                flowDebug = navRuntime.FlowDebugEnabled;
                flowMode = navRuntime.FlowDebugMode;
                flowIters = navRuntime.FlowIterationsPerTick;
            }

            int agentsPerTeam = 0;
            int liveTotal = 0;
            int flowDbgLines = 0;
            agentsPerTeam = _engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam);
            liveTotal = _engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal);
            flowDbgLines = _engine.GetService(Navigation2DPlaygroundKeys.FlowDebugLines);

            int x = 16;
            int y = 180;
            int w = 500;
            int h = 150;
            var bg = new Vector4(0.04f, 0.05f, 0.08f, 0.68f);
            var border = new Vector4(0.35f, 0.75f, 1f, 0.5f);
            var title = new Vector4(0.9f, 0.95f, 1f, 1f);
            var text = new Vector4(0.85f, 0.9f, 0.95f, 1f);
            var hint = new Vector4(0.68f, 0.78f, 0.9f, 0.95f);

            overlay.AddRect(x, y, w, h, bg, border);
            overlay.AddText(x + 10, y + 8, "Navigation2D Playground", 16, title);
            overlay.AddText(x + 10, y + 30, $"FlowEnabled={flowEnabled}  FlowDebug={flowDebug}  Mode={flowMode}  Iter={flowIters}", 14, text);
            overlay.AddText(x + 10, y + 50, $"Agents/team={agentsPerTeam}  Live={liveTotal}  FlowDbgLines={flowDbgLines}", 14, text);
            overlay.AddText(x + 10, y + 76, "G ToggleFlow | H ToggleDebug | J CycleMode", 13, hint);
            overlay.AddText(x + 10, y + 94, "U +Iter | Y -Iter | K +500/team | L -500/team", 13, hint);
            overlay.AddText(x + 10, y + 112, "R ResetScenario", 13, hint);
        }
    }
}
