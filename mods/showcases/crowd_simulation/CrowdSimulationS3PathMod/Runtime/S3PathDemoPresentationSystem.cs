using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace CrowdSimulationS3PathMod.Runtime;

/// <summary>
/// S3 演示覆盖层:当前对的折线(品红)、流场方向箭头(白,抽样)、
/// 起点(绿)/ 终点(红)标记;HUD 显示请求帧 / 生效帧 / 当前帧 / 等待次数 /
/// 后台线程数 / 分支 / 可达性(慢注入时提示仿真暂停等待)。
/// </summary>
public sealed class S3PathDemoPresentationSystem : ISystem<float>
{
    private static readonly DebugDrawColor Magenta = new(255, 64, 200);
    private static readonly DebugDrawColor ArrowWhite = new(220, 235, 245);
    private static readonly DebugDrawColor StartGreen = new(76, 217, 100);
    private static readonly DebugDrawColor GoalRed = new(255, 69, 58);

    private readonly S3PathDemoRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public S3PathDemoPresentationSystem(S3PathDemoRuntime runtime, DebugDrawCommandBuffer debugDraw, ScreenOverlayBuffer overlay)
    {
        _runtime = runtime;
        _debugDraw = debugDraw;
        _overlay = overlay;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _debugDraw.Clear();
        var nav = _runtime.Nav;
        int n = nav.CellCount, n2 = n * n;
        float csM = _runtime.Config.NavCellSizeCm / 100f;

        var current = _runtime.Current;
        if (current != null)
        {
            // 流场方向箭头(每 8 格抽样;桥面格跳过)
            var flow = current.Flow;
            if (flow != null)
            {
                var inf = Ludots.Core.Mathematics.FixedPoint.Fix64.MaxValue / 4;
                for (int cy = 1; cy < n - 1; cy += 8)
                {
                    for (int cx = 1; cx < n - 1; cx += 8)
                    {
                        int u = cy * n + cx;
                        if (flow.Integ[u] >= inf) continue;
                        int w = flow.Wp[u];
                        if (w < 0 || w == u) continue;
                        int wc = w % n2;
                        float x0 = (cx + 0.5f) * csM, y0 = (cy + 0.5f) * csM;
                        float dx = (wc % n - cx) * csM, dy = (wc / n - cy) * csM;
                        float len = MathF.Sqrt(dx * dx + dy * dy);
                        if (len < 1e-3f) continue;
                        float arrow = Math.Min(len, 4 * csM);
                        _debugDraw.Lines.Add(new DebugDrawLine2D
                        {
                            A = new Vector2(x0, y0),
                            B = new Vector2(x0 + dx / len * arrow, y0 + dy / len * arrow),
                            Thickness = 0.6f,
                            Color = ArrowWhite,
                        });
                    }
                }
            }

            // 折线(查询结果)
            if (current.Points != null)
            {
                for (int k = 2; k < current.Points.Length; k += 2)
                {
                    _debugDraw.Lines.Add(new DebugDrawLine2D
                    {
                        A = new Vector2((float)current.Points[k - 2].ToDouble() * csM, (float)current.Points[k - 1].ToDouble() * csM),
                        B = new Vector2((float)current.Points[k].ToDouble() * csM, (float)current.Points[k + 1].ToDouble() * csM),
                        Thickness = 1.6f,
                        Color = Magenta,
                    });
                }
            }

            // 起点 / 终点
            int sc = current.Query.StartCell, gc = current.Query.GoalCell;
            _debugDraw.Circles.Add(new DebugDrawCircle2D { Center = new Vector2((sc % n + 0.5f) * csM, (sc / n + 0.5f) * csM), Radius = 4f, Thickness = 1.5f, Color = StartGreen });
            _debugDraw.Circles.Add(new DebugDrawCircle2D { Center = new Vector2((gc % n + 0.5f) * csM, (gc / n + 0.5f) * csM), Radius = 4f, Thickness = 1.5f, Color = GoalRed });
        }

        string branch = current == null ? "-" : current.Branch switch
        {
            CorridorQuery.Branch.Tile => "NavMesh A*+漏斗",
            CorridorQuery.Branch.Hpa => "HPA* 走廊",
            _ => "不可达",
        };
        string detail =
            $"对 {_runtime.PairIndex + 1}/{_runtime.Pairs.Length}；请求帧 {_runtime.RequestTick}；生效帧 {_runtime.DueTick}；当前帧 {_runtime.TickCounter}" +
            $"；等待次数 {_runtime.TotalWaits}；后台线程 {_runtime.ThreadCount}(1/2/4 巡回,结果不变)；分支 {branch}" +
            (current == null ? "" : $"；可达 {(current.Reachable ? "是" : "否")}；流场到达 {(current.Flow?.Reached ?? 0)} 格") +
            (_runtime.SlowInjected ? "；后台慢注入中——仿真暂停等待,绝不使用半成品" : "") +
            (_runtime.Fault != null ? $"；服务故障 {_runtime.Fault}" : "");
        DrawCaption(_overlay, "CrowdSimulation S3 · 两点路径与固定生效帧", detail);
    }

    private static void DrawCaption(ScreenOverlayBuffer overlay, string title, string detail)
    {
        overlay.AddRect(16, 16, 1568, 92, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(1f, 0.85f, 0.2f, 1f));
        overlay.AddText(32, 24, title, 22, new Vector4(1f, 0.92f, 0.35f, 1f));
        overlay.AddText(32, 56, detail, 18, new Vector4(1f, 1f, 1f, 1f));
    }
}
