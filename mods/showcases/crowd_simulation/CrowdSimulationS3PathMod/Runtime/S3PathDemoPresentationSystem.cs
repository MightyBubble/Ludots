using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace CrowdSimulationS3PathMod.Runtime;

/// <summary>
/// S3 演示交互与覆盖层(spec 演示契约):
/// 左键 = 点起点(绿环)、右键 = 点终点(红菱),光标悬停格有提示圈;
/// Q 巡回代理体型、T 巡回后台线程 1/2/4、G 注入 / 解除后台变慢;
/// HUD 显示请求帧 / 生效帧 / 当前帧 / 等待次数 / 体型 / 线程数 / 分支 / 可达性。
/// 输入只读边沿(按下沿),世界落点走 ScreenRayProvider + 高度图射线,不碰调试通道。
/// </summary>
public sealed class S3PathDemoPresentationSystem : ISystem<float>
{
    private static readonly Vector4 Magenta = new(1f, 0.25f, 0.78f, 1f);
    private static readonly Vector4 StartGreen = new(0.3f, 0.85f, 0.4f, 1f);
    private static readonly Vector4 GoalRed = new(1f, 0.27f, 0.22f, 1f);
    private static readonly Vector4 HoverWhite = new(1f, 1f, 1f, 0.75f);

    private static readonly RouteVisualId RouteLine = new(1);
    private static readonly RouteVisualId RouteStart = new(2);
    private static readonly RouteVisualId RouteGoal = new(3);
    private static readonly RouteVisualId RouteHover = new(4);

    private const string MouseLeft = "<Mouse>/LeftButton";
    private const string MouseRight = "<Mouse>/RightButton";
    private const string KeyContext = "<Keyboard>/q";
    private const string KeyThreads = "<Keyboard>/t";
    private const string KeySlow = "<Keyboard>/g";
    private const int HudZoneBottomPx = 110; // HUD 面板区的点击不算世界点选

    private readonly S3PathDemoRuntime _runtime;
    private readonly RouteVisualBuffer _routeVisuals;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly IInputBackend _input;
    private readonly IScreenRayProvider _rays;
    private readonly IContinuousHeightmap? _heightmap;

    private bool _prevLeft, _prevRight, _prevContext, _prevThreads, _prevSlow;

    public S3PathDemoPresentationSystem(
        S3PathDemoRuntime runtime,
        RouteVisualBuffer routeVisuals,
        ScreenOverlayBuffer overlay,
        IInputBackend input,
        IScreenRayProvider rays,
        IContinuousHeightmap? heightmap)
    {
        _runtime = runtime;
        _routeVisuals = routeVisuals;
        _overlay = overlay;
        _input = input;
        _rays = rays;
        _heightmap = heightmap;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        int n = _runtime.Nav.CellCount;
        float csM = _runtime.Config.NavCellSizeCm / 100f;
        float csCm = _runtime.Config.NavCellSizeCm;

        // ── 输入边沿 → 运行时队列 ─────────────────────────────
        bool left = _input.GetButton(MouseLeft), right = _input.GetButton(MouseRight);
        bool keyContext = _input.GetButton(KeyContext), keyThreads = _input.GetButton(KeyThreads), keySlow = _input.GetButton(KeySlow);
        Vector2 mouse = _input.GetMousePosition();
        int hoveredCell = -1;
        if (_heightmap != null && mouse.Y >= HudZoneBottomPx)
        {
            var ray = _rays.GetRay(mouse);
            if (_heightmap.TryRaycastGround(in ray, out VisualGroundHit hit))
            {
                int cx = (int)MathF.Floor(hit.WorldXCm / csCm), cy = (int)MathF.Floor(hit.WorldYCm / csCm);
                if (cx >= 0 && cy >= 0 && cx < n && cy < n) hoveredCell = cy * n + cx;
            }
        }

        _runtime.HoveredCell = hoveredCell;
        if (left && !_prevLeft && hoveredCell >= 0) _runtime.EnqueueSetStart(hoveredCell);
        if (right && !_prevRight && hoveredCell >= 0) _runtime.EnqueueSetGoal(hoveredCell);
        if (keyContext && !_prevContext) _runtime.EnqueueCycleContext();
        if (keyThreads && !_prevThreads) _runtime.EnqueueCycleThreads();
        if (keySlow && !_prevSlow) _runtime.EnqueueToggleSlow();
        _prevLeft = left; _prevRight = right; _prevContext = keyContext; _prevThreads = keyThreads; _prevSlow = keySlow;

        // ── 覆盖层(gameplay 通道) ─────────────────────────────
        _routeVisuals.BeginFrame();
        float markerRadius = csM * 1.2f;
        if (hoveredCell >= 0)
        {
            _routeVisuals.AddMarker(RouteHover, CellCenter(hoveredCell, n, csM), RouteVisualMarkerShape.Ring, markerRadius * 0.8f, 1f, HoverWhite);
        }

        if (_runtime.StartCell >= 0)
        {
            _routeVisuals.AddMarker(RouteStart, CellCenter(_runtime.StartCell, n, csM), RouteVisualMarkerShape.Ring, markerRadius, 1.5f, StartGreen);
        }

        if (_runtime.GoalCell >= 0)
        {
            _routeVisuals.AddMarker(RouteGoal, CellCenter(_runtime.GoalCell, n, csM), RouteVisualMarkerShape.Diamond, markerRadius, 1.5f, GoalRed);
        }

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
        }

        // ── HUD ─────────────────────────────
        string branch = current == null ? "-" : current.Branch switch
        {
            CorridorQuery.Branch.Tile => "NavMesh A*+漏斗",
            CorridorQuery.Branch.Hpa => "HPA* 走廊",
            _ => "不可达",
        };
        string detail =
            $"请求帧 {_runtime.RequestTick}；生效帧 {_runtime.DueTick}；当前帧 {_runtime.TickCounter}；等待次数 {_runtime.TotalWaits}" +
            $"；体型 {_runtime.ContextLabel}；后台线程 {_runtime.ThreadCount}；分支 {branch}" +
            (current == null ? "" : $"；可达 {(current.Reachable ? "是" : "否")}；流场到达 {(current.Flow?.Reached ?? 0)} 格") +
            (_runtime.SlowInjected ? "；后台慢注入中——仿真暂停等待,绝不使用半成品" : "") +
            (_runtime.Fault != null ? $"；服务故障 {_runtime.Fault}" : "");
        DrawCaption(_overlay,
            "CrowdSimulation S3 · 两点路径与固定生效帧",
            detail,
            "左键 起点 · 右键 终点 · Q 体型 · T 线程 1/2/4 · G 后台变慢");
    }

    private static Vector2 CellCenter(int cell, int n, float csM)
        => new((cell % n + 0.5f) * csM, (cell / n + 0.5f) * csM);

    private static void DrawCaption(ScreenOverlayBuffer overlay, string title, string detail, string keys)
    {
        overlay.AddRect(16, 16, 1568, 118, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(1f, 0.85f, 0.2f, 1f));
        overlay.AddText(32, 24, title, 22, new Vector4(1f, 0.92f, 0.35f, 1f));
        overlay.AddText(32, 56, detail, 18, new Vector4(1f, 1f, 1f, 1f));
        overlay.AddText(32, 84, keys, 18, new Vector4(0.75f, 0.9f, 1f, 1f));
    }
}
