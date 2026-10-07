using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace CrowdSimulationS3PathMod.Runtime;

/// <summary>
/// S3 演示覆盖层(gameplay 通道,不是调试绘制):当前对的折线写路线视觉缓冲
/// (品红折线 + 绿起点 / 红终点标记,渲染端地形跟随);方向图由流场投影器经
/// 全场视觉缓冲呈现(本系统不管);HUD 显示请求帧 / 生效帧 / 当前帧 / 等待次数 /
/// 后台线程数 / 分支 / 可达性(慢注入时提示仿真暂停等待)。
/// </summary>
public sealed class S3PathDemoPresentationSystem : ISystem<float>
{
    private static readonly Vector4 Magenta = new(1f, 0.25f, 0.78f, 1f);
    private static readonly Vector4 StartGreen = new(0.3f, 0.85f, 0.4f, 1f);
    private static readonly Vector4 GoalRed = new(1f, 0.27f, 0.22f, 1f);

    private static readonly RouteVisualId RouteLine = new(1);
    private static readonly RouteVisualId RouteStart = new(2);
    private static readonly RouteVisualId RouteGoal = new(3);

    private readonly S3PathDemoRuntime _runtime;
    private readonly RouteVisualBuffer _routeVisuals;
    private readonly ScreenOverlayBuffer _overlay;

    public S3PathDemoPresentationSystem(S3PathDemoRuntime runtime, RouteVisualBuffer routeVisuals, ScreenOverlayBuffer overlay)
    {
        _runtime = runtime;
        _routeVisuals = routeVisuals;
        _overlay = overlay;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _routeVisuals.BeginFrame();
        int n = _runtime.Nav.CellCount;
        float csM = _runtime.Config.NavCellSizeCm / 100f;

        var current = _runtime.Current;
        if (current?.Points != null)
        {
            int pointCount = current.Points.Length / 2;
            Span<Vector2> pts = pointCount <= 256 ? stackalloc Vector2[pointCount] : new Vector2[pointCount];
            for (int k = 0; k < pointCount; k++)
            {
                pts[k] = new Vector2(
                    (float)current.Points[k * 2].ToDouble() * csM,
                    (float)current.Points[k * 2 + 1].ToDouble() * csM);
            }

            // 视觉尺寸跟导航格走(本图 62.5 m/格),不写死米数
            _routeVisuals.AddPolyline(RouteLine, pts, csM * 0.35f, Magenta);

            int sc = current.Query.StartCell, gc = current.Query.GoalCell;
            float markerRadius = csM * 1.2f;
            _routeVisuals.AddMarker(RouteStart, new Vector2((sc % n + 0.5f) * csM, (sc / n + 0.5f) * csM), RouteVisualMarkerShape.Ring, markerRadius, 1.5f, StartGreen);
            _routeVisuals.AddMarker(RouteGoal, new Vector2((gc % n + 0.5f) * csM, (gc / n + 0.5f) * csM), RouteVisualMarkerShape.Diamond, markerRadius, 1.5f, GoalRed);
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
