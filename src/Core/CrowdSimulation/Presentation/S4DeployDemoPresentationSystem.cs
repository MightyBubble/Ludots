using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// S4 部署演示覆盖层:分层视图线框、建造足迹预览、重烘脏 tile 闪烁、HUD。
/// 键鼠由 <see cref="S4DeployInputSystem"/> 写入演示态与群体指令队列,这里只读状态。
/// </summary>
public sealed class S4DeployDemoPresentationSystem : ISystem<float>
{
    private static readonly Vector4 GoalCyan = new(0.25f, 0.85f, 1f, 1f);
    private static readonly Vector4 HoverWhite = new(1f, 1f, 1f, 0.7f);
    private static readonly Vector4 PlaceGreen = new(0.35f, 0.9f, 0.45f, 0.95f);
    private static readonly Vector4 PlaceRed = new(1f, 0.3f, 0.25f, 0.95f);
    private static readonly Vector4 RemoveOrange = new(1f, 0.62f, 0.15f, 0.95f);
    private static readonly Vector4 FlashYellow = new(1f, 0.9f, 0.2f, 1f);

    private static readonly RouteVisualId GoalMarker = new(1);
    private static readonly RouteVisualId HoverMarker = new(2);
    private static readonly RouteVisualId PreviewOutline = new(3);
    private static readonly RouteVisualId RemoveOutline = new(4);
    private static readonly RouteVisualId WireframeId = new(100);
    private static readonly RouteVisualId FlashBase = new(200);

    private const long FlashMs = 2500; // 脏 tile 闪烁时长(呈现口径,与仿真无关)

    private readonly S4DeployDemoRuntime _demo;
    private readonly RouteVisualBuffer _routeVisuals;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly Func<CrowdSimulationRuntime?> _runtimeSource;
    private readonly Func<CrowdSimSession?> _sessionSource;
    private readonly CrowdPresentationConfig _presentation;

    private readonly S4DeployNavWireframe _wire = new();
    private int _flashReportTick = -1;
    private long _flashStartMs;

    internal S4DeployDemoPresentationSystem(
        S4DeployDemoRuntime demo,
        RouteVisualBuffer routeVisuals,
        ScreenOverlayBuffer overlay,
        Func<CrowdSimulationRuntime?> runtimeSource,
        Func<CrowdSimSession?> sessionSource,
        CrowdPresentationConfig presentation)
    {
        _demo = demo;
        _routeVisuals = routeVisuals;
        _overlay = overlay;
        _runtimeSource = runtimeSource;
        _sessionSource = sessionSource;
        _presentation = presentation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        var session = _sessionSource();
        var runtime = _runtimeSource();
        var ground = _demo.HoverCm;

        // ── 路线通道(gameplay 视觉) ─────────────────────────────
        _routeVisuals.BeginFrame();
        if (session == null)
        {
            DrawHud(null, null, null);
            return;
        }

        float csM = session.Config.NavCellSizeCm / 100f;
        if (ground is { } hover)
        {
            _routeVisuals.AddMarker(HoverMarker, new Vector2((float)(hover.X / 100.0), (float)(hover.Y / 100.0)), RouteVisualMarkerShape.Ring, csM * 0.8f, 1f, HoverWhite);
        }

        // 路线视图:主力组目标标记(流场贴花由视图投影器写场)
        if (_demo.ViewMode == 0 && S4DeployDemoViewProjector.DominantGroup(session) is { Goal: >= 0 } group)
        {
            int n = session.Config.NavCellCount;
            _routeVisuals.AddMarker(GoalMarker, new Vector2((group.Goal % n + 0.5f) * csM, (group.Goal / n + 0.5f) * csM), RouteVisualMarkerShape.Diamond, csM * 1.2f, 1.5f, GoalCyan);
        }

        // NavMesh + HPA 线框(视图模式 2)。变体未物化时本帧不提交,缓存键留给下一帧。
        if (_demo.ViewMode == 2 && _wire.TryProject(session, out _, out _))
        {
            foreach (var (pts, thick, color) in _wire.Lines) _routeVisuals.AddPolylineShared(WireframeId, pts, thick, color);
            foreach (var (pos, shape, radius, color) in _wire.Markers) _routeVisuals.AddMarker(WireframeId, pos, shape, radius, 1f, color);
        }

        // 建造预览(建造模式):足迹轮廓绿=可放/红=不可放;X 拆除目标高亮
        bool previewValid = false, previewShown = false;
        if (_demo.BuildMode && ground is { } p && session.Structures is { } store)
        {
            previewShown = true;
            int px = (int)Math.Floor(p.X), py = (int)Math.Floor(p.Y);
            previewValid = CanPlace(store, _demo.Template, px, py);
            // 足迹/拆除轮廓线宽 = 0.5 格长(31m):细线角标在丘陵远景下实测可视性不足,加粗一档
            AddRectOutline(PreviewOutline, FootprintMeters(_demo.Template, px, py), csM * 0.5f, previewValid ? PlaceGreen : PlaceRed);
            int hoverId = store.EntityAt(Fix64.FromInt(px), Fix64.FromInt(py));
            if (hoverId >= 0)
            {
                var fp = store.FootprintOf(hoverId).Bbox();
                AddRectOutline(RemoveOutline, (
                    (double)fp.MinX.ToInt() / 100, (double)fp.MinY.ToInt() / 100,
                    (double)fp.MaxX.ToInt() / 100, (double)fp.MaxY.ToInt() / 100), csM * 0.5f, RemoveOrange);
            }
        }

        // 重烘脏 tile 闪烁:最近一笔报告发布后短暂高亮其受影响 tile 并集
        CurrentFlash(session, csM);

        // ── HUD(全部读内核现成状态) ─────────────────────────────
        DrawHud(session, runtime, (previewShown, previewValid));
    }

    /// <summary>放置合法性(演示口径,只读结构仓现成数据):光标处无既有结构,且足迹包围格内
    /// 无阻挡格(rect/path 轴对齐时包围盒即足迹;近似对演示预览足够,正式语义在指令执行侧)。</summary>
    private static bool CanPlace(CrowdStructuresStore store, S4DeployDemoRuntime.StructureChoice tpl, int xCm, int yCm)
    {
        if (store.EntityAt(Fix64.FromInt(xCm), Fix64.FromInt(yCm)) >= 0) return false;
        var (minX, minY, maxX, maxY) = FootprintMeters(tpl, xCm, yCm);
        int n = store.CellCount, cs = store.CellSizeCm;
        int cx0 = Math.Clamp((int)Math.Floor(minX * 100 / cs), 0, n - 1), cy0 = Math.Clamp((int)Math.Floor(minY * 100 / cs), 0, n - 1);
        int cx1 = Math.Clamp((int)Math.Floor(maxX * 100 / cs), 0, n - 1), cy1 = Math.Clamp((int)Math.Floor(maxY * 100 / cs), 0, n - 1);
        for (int cy = cy0; cy <= cy1; cy++)
        {
            int row = cy * n;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                if (store.Blocked[row + cx] != 0) return false;
            }
        }

        return true;
    }

    /// <summary>足迹包围盒(米)。rect = 中心 ± size/2;道路 = 光标向 +X 的条带(宽 size,长 RoadLength)。</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY) FootprintMeters(S4DeployDemoRuntime.StructureChoice tpl, int xCm, int yCm)
    {
        if (tpl.IsRoad)
        {
            return (xCm / 100.0, (yCm - tpl.SizeCm / 2) / 100.0, (xCm + tpl.RoadLengthCm) / 100.0, (yCm + tpl.SizeCm / 2) / 100.0);
        }

        double hx = tpl.SizeCm / 200.0, hy = tpl.SizeCm / 200.0;
        return (xCm / 100.0 - hx, yCm / 100.0 - hy, xCm / 100.0 + hx, yCm / 100.0 + hy);
    }

    private void AddRectOutline(RouteVisualId id, (double MinX, double MinY, double MaxX, double MaxY) rect, float thick, Vector4 color)
    {
        Span<Vector2> pts = stackalloc Vector2[5];
        pts[0] = new((float)rect.MinX, (float)rect.MinY);
        pts[1] = new((float)rect.MaxX, (float)rect.MinY);
        pts[2] = new((float)rect.MaxX, (float)rect.MaxY);
        pts[3] = new((float)rect.MinX, (float)rect.MaxY);
        pts[4] = pts[0];
        _routeVisuals.AddPolyline(id, pts, thick, color);
    }

    /// <summary>脏 tile 闪烁:报告 ReportTick 变化即起闪,FlashMs 内黄色轮廓线性淡出。
    /// tile 轮廓按导航格逐格细分:路线渲染器只对折点采样地面高度,1km tile 的四角直边
    /// 在丘陵地形会整段埋进山体(视野内又见不到角点),细分后贴地才可见。</summary>
    private void CurrentFlash(CrowdSimSession session, float csM)
    {
        var report = session.LastRebakeReport;
        if (report == null) return;
        if (report.ReportTick != _flashReportTick)
        {
            _flashReportTick = report.ReportTick;
            _flashStartMs = Environment.TickCount64;
        }

        long elapsed = Environment.TickCount64 - _flashStartMs;
        if (elapsed >= FlashMs) return;
        float alpha = 1f - elapsed / (float)FlashMs;
        int n = session.Config.NavCellCount, tiles = session.Config.Hpa.ClusterSize;
        int perRow = Math.Max(1, n / tiles);
        float tileM = tiles * csM;
        var color = FlashYellow with { W = alpha };
        int shown = 0;
        foreach (int tile in report.UnionTiles)
        {
            var id = new RouteVisualId(200 + shown);
            float ox = tile % perRow * tileM, oy = tile / perRow * tileM;
            // 黄线宽 = 0.6 格长(37.5m):细线随地形起伏时实测可视性不足,加粗一档
            AddTileOutline(id, ox, oy, tileM, csM, csM * 0.6f, color);
            shown++;
        }
    }

    private void AddTileOutline(RouteVisualId id, float ox, float oy, float tileM, float stepM, float thick, Vector4 color)
    {
        int seg = Math.Max(1, (int)Math.Ceiling(tileM / stepM));
        var pts = new Vector2[seg * 4 + 1];
        int p = 0;
        for (int k = 0; k < seg; k++) pts[p++] = new(ox + tileM * k / seg, oy);
        for (int k = 0; k < seg; k++) pts[p++] = new(ox + tileM, oy + tileM * k / seg);
        for (int k = 0; k < seg; k++) pts[p++] = new(ox + tileM - tileM * k / seg, oy + tileM);
        for (int k = 0; k < seg; k++) pts[p++] = new(ox, oy + tileM - tileM * k / seg);
        pts[p] = pts[0];
        _routeVisuals.AddPolyline(id, pts, thick, color);
    }

    internal static bool NavVariantPending(CrowdSimSession session, int viewMode)
    {
        if (viewMode != 1 && viewMode != 2) return false;
        var group = S4DeployDemoViewProjector.DominantGroup(session);
        if (group == null || group.NavId < CrowdBeliefNavs.BeliefStride) return false;
        return !session.TryGetMaterializedNav(group.NavId, out _);
    }

    private void DrawHud(CrowdSimSession? session, CrowdSimulationRuntime? runtime, (bool Shown, bool Valid)? preview)
    {
        string status, rebake, replay;
        if (session == null)
        {
            status = "会话未激活";
            rebake = "-";
            replay = "-";
        }
        else
        {
            int idle = 0, moving = 0, arrived = 0, unreachable = 0, jumping = 0;
            for (int i = 0; i < session.Units.Count; i++)
            {
                switch (session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(i)).State)
                {
                    case (byte)CrowdUnitState.Idle: idle++; break;
                    case (byte)CrowdUnitState.Moving: moving++; break;
                    case (byte)CrowdUnitState.Arrived: arrived++; break;
                    case (byte)CrowdUnitState.Unreachable: unreachable++; break;
                    case (byte)CrowdUnitState.Jump: jumping++; break;
                }
            }

            status = $"tick {session.TickCount};单位 {session.Units.Count}(闲 {idle}/行 {moving}/达 {arrived}" +
                     (unreachable + jumping > 0 ? $"/不可达 {unreachable}/跳跃 {jumping}" : "") + $");选中 {session.SelectedCount}";
            var report = session.LastRebakeReport;
            rebake = report == null
                ? "无结构变更"
                : $"tick {report.ExecTick}→{report.ReportTick} {(report.Kind == CrowdRebakeReport.KindPlace ? "建造" : "拆除")}" +
                  $" · 脏 tile {report.Tiles} · 缓存命中 {report.Hits}/{report.Hits + report.Misses}" +
                  $" · 重规划 {report.Orders} · 挤离 {report.Evicted} · 无处安放 {report.Stuck}";
            replay = _demo.InteractiveMode
                ? "回放已停用(交互模式)"
                : runtime == null
                    ? "回放:-"
                    : runtime.ReplayStatus switch
                    {
                        1 => "回放:逐位一致",
                        2 => $"回放:分歧 @tick {runtime.ReplayDivergenceTick}",
                        _ => "回放:待触发",
                    };
        }

        CrowdDeployPresentation deploy = _presentation.Deploy
            ?? throw new InvalidOperationException("部署演示 HUD 缺少 deploy 文案。");
        string placeHint = preview == null
            ? ""
            : preview.Value.Shown
                ? (preview.Value.Valid ? deploy.PlaceOk : deploy.PlaceBlocked)
                : deploy.PlaceOffMap;
        string activity = _demo.BuildMode
            ? deploy.BuildActivity
                .Replace("{label}", _demo.Template.Label, StringComparison.Ordinal)
                .Replace("{placeHint}", placeHint, StringComparison.Ordinal)
            : deploy.ObserveActivity;
        string pending = session != null && NavVariantPending(session, _demo.ViewMode) ? deploy.PendingNav : "";
        string keys = _presentation.Hud.Keys
            .Replace("{view}", _demo.ViewLabel, StringComparison.Ordinal)
            .Replace("{templateIndex}", (_demo.TemplateIndex + 1).ToString(), StringComparison.Ordinal)
            .Replace("{activity}", activity, StringComparison.Ordinal)
            .Replace("{pending}", pending, StringComparison.Ordinal);
        CrowdHudPanel hud = _presentation.Hud;
        _overlay.AddRect(hud.X, hud.Y, hud.W, hud.H, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(1f, 0.85f, 0.2f, 1f));
        _overlay.AddText(32, 24, _presentation.Title, 22, new Vector4(1f, 0.92f, 0.35f, 1f));
        _overlay.AddText(32, 54, status, 18, new Vector4(1f, 1f, 1f, 1f));
        _overlay.AddText(32, 78, $"最近重烘:{rebake}", 18, new Vector4(1f, 1f, 1f, 1f));
        _overlay.AddText(32, 102, replay, 18, _demo.InteractiveMode ? new Vector4(1f, 0.55f, 0.4f, 1f) : new Vector4(0.8f, 1f, 0.8f, 1f));
        _overlay.AddText(32, 126, keys, 18, new Vector4(0.75f, 0.9f, 1f, 1f));
    }
}
