using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// S3 两点路径覆盖层:起点/终点/悬停标记、折线、NavMesh 线框、HUD。
/// 键鼠由 <see cref="S3PathInputSystem"/> 写入运行时队列,这里只读状态。
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

    internal const int HudPanelX = 16, HudPanelY = 16, HudPanelW = 1568, HudPanelH = 118;

    private readonly S3PathDemoRuntime _runtime;
    private readonly RouteVisualBuffer _routeVisuals;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly string _title;

    private int _wireNavId = -1;
    private readonly List<(Vector2[] Pts, float Thick, Vector4 Color)> _wireLines = new();
    private readonly List<(Vector2 Pos, RouteVisualMarkerShape Shape, float Radius, Vector4 Color)> _wireMarkers = new();

    public S3PathDemoPresentationSystem(
        S3PathDemoRuntime runtime,
        RouteVisualBuffer routeVisuals,
        ScreenOverlayBuffer overlay,
        string title)
    {
        _runtime = runtime;
        _routeVisuals = routeVisuals;
        _overlay = overlay;
        _title = title;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        int n = _runtime.Nav.CellCount;
        float csM = _runtime.Config.NavCellSizeCm / 100f;
        int hoveredCell = _runtime.HoveredCell;

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

        // NavMesh + HPA 线框(视图模式 2)
        if (_runtime.ViewMode == 2)
        {
            EnsureWireframe(n, csM);
            var wireId = new RouteVisualId(100);
            foreach (var (pts, thick, color) in _wireLines) _routeVisuals.AddPolylineShared(wireId, pts, thick, color);
            foreach (var (pos, shape, radius, color) in _wireMarkers) _routeVisuals.AddMarker(wireId, pos, shape, radius, 1f, color);
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
            _title,
            detail,
            $"左键 起点 · 右键 终点 · Q 体型 · T 线程 1/2/4 · G 后台变慢 · V 视图 {S3PathDemoRuntime.ViewModeLabels[_runtime.ViewMode]}");
    }

    private static Vector2 CellCenter(int cell, int n, float csM)
        => new((cell % n + 0.5f) * csM, (cell / n + 0.5f) * csM);

    // NavMesh 多边形线框 + cluster 网格 + HPA 入口 + 跳跃链接,按上下文缓存(线框是静态数据)
    private void EnsureWireframe(int n, float csM)
    {
        var nav = _runtime.Nav;
        if (_wireNavId == nav.Id) return;
        _wireNavId = nav.Id;
        _wireLines.Clear();
        _wireMarkers.Clear();
        int t = _runtime.Config.Hpa.ClusterSize, c = n / t;
        var navmeshColor = new Vector4(0.9f, 0.9f, 0.9f, 0.55f);
        var deckColor = new Vector4(1f, 0.84f, 0.31f, 0.85f);
        for (int ty = 0; ty < c; ty++)
        {
            for (int tx = 0; tx < c; tx++)
            {
                int tileId = ty * c + tx;
                AddEntryWire(nav.Tiles?[tileId], tx * t, ty * t, csM, navmeshColor);
                if (nav.UpperTiles != null && nav.UpperTiles.TryGetValue(tileId, out var up)) AddEntryWire(up.Entry, tx * t, ty * t, csM, deckColor);
            }
        }

        var gridColor = new Vector4(1f, 1f, 1f, 0.22f);
        for (int b = 0; b <= c; b++)
        {
            _wireLines.Add((new[] { new Vector2(b * t * csM, 0f), new Vector2(b * t * csM, n * csM) }, csM * 0.1f, gridColor));
            _wireLines.Add((new[] { new Vector2(0f, b * t * csM), new Vector2(n * csM, b * t * csM) }, csM * 0.1f, gridColor));
        }

        if (nav.Hpa != null)
        {
            int n2 = n * n;
            var entranceColor = new Vector4(0.25f, 0.77f, 1f, 0.9f);
            foreach (var block in nav.Hpa.Blocks)
            {
                foreach (int cell in block.Cells)
                {
                    bool deck = cell >= n2;
                    int cc = deck ? cell - n2 : cell;
                    _wireMarkers.Add((new Vector2((cc % n + 0.5f) * csM, (cc / n + 0.5f) * csM), RouteVisualMarkerShape.Ring, csM * 0.4f, deck ? deckColor : entranceColor));
                }
            }
        }

        if (nav.Links != null)
        {
            for (int e = 0; e < nav.Links.Count; e++)
            {
                int a = nav.Links.From[e], b2 = nav.Links.To[e];
                var col = nav.Links.TwoWay[e] != 0 ? new Vector4(0.36f, 0.42f, 0.75f, 0.9f) : new Vector4(1f, 0.7f, 0f, 0.9f);
                _wireLines.Add((new[]
                {
                    new Vector2((a % n + 0.5f) * csM, (a / n + 0.5f) * csM),
                    new Vector2((b2 % n + 0.5f) * csM, (b2 / n + 0.5f) * csM),
                }, csM * 0.2f, col));
            }
        }
    }

    private void AddEntryWire(NavTileEntry? e, int ox, int oy, float csM, Vector4 color)
    {
        if (e == null) return;
        for (int p = 0; p < e.Count; p++)
        {
            int s = e.PolyStart[p], e2 = e.PolyStart[p + 1], count = e2 - s;
            var pts = new Vector2[count + 1];
            for (int k = 0; k < count; k++)
            {
                int v = e.PolyVerts[s + k];
                pts[k] = new Vector2((ox + e.Vx[v]) * csM, (oy + e.Vy[v]) * csM);
            }

            pts[count] = pts[0];
            _wireLines.Add((pts, csM * 0.12f, color));
        }
    }

    private static void DrawCaption(ScreenOverlayBuffer overlay, string title, string detail, string keys)
    {
        overlay.AddRect(HudPanelX, HudPanelY, HudPanelW, HudPanelH, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(1f, 0.85f, 0.2f, 1f));
        overlay.AddText(32, 24, title, 22, new Vector4(1f, 0.92f, 0.35f, 1f));
        overlay.AddText(32, 56, detail, 18, new Vector4(1f, 1f, 1f, 1f));
        overlay.AddText(32, 84, keys, 18, new Vector4(0.75f, 0.9f, 1f, 1f));
    }
}
