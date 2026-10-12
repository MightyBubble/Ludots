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
    private static readonly RouteVisualId WireframeId = new(CrowdSimulationLimits.WireframeRouteVisualId);
    private static readonly RouteVisualId FlashBase = new(200);

    private const long FlashMs = 2500; // 脏 tile 闪烁时长(呈现口径,与仿真无关)

    private readonly S4DeployDemoRuntime _demo;
    private readonly RouteVisualBuffer _routeVisuals;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly Func<CrowdSimulationRuntime?> _runtimeSource;
    private readonly Func<CrowdSimSession?> _sessionSource;
    private readonly CrowdPresentationConfig _presentation;

    private readonly S4DeployNavWireframe _wire = new();
    private readonly HudTextCache _hudText = new();
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

        float navCellVisual = WorldUnits.CmToM(session.Config.NavCellSizeCm);
        if (ground is { } hover)
        {
            _routeVisuals.AddMarker(HoverMarker, new Vector2(WorldUnits.CmToM((float)hover.X), WorldUnits.CmToM((float)hover.Y)), RouteVisualMarkerShape.Ring, navCellVisual * 0.8f, 1f, HoverWhite);
        }

        // 路线视图:主力组目标标记(流场贴花由视图投影器写场)
        if (_demo.ViewMode == 0 && S4DeployDemoViewProjector.DominantGroup(session) is { Goal: >= 0 } group)
        {
            int navCellsPerSide = session.Config.NavCellsPerSide;
            _routeVisuals.AddMarker(GoalMarker, new Vector2((group.Goal % navCellsPerSide + 0.5f) * navCellVisual, (group.Goal / navCellsPerSide + 0.5f) * navCellVisual), RouteVisualMarkerShape.Diamond, navCellVisual * 1.2f, 1.5f, GoalCyan);
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
            AddRectOutline(PreviewOutline, FootprintVisual(_demo.Template, px, py), navCellVisual * 0.5f, previewValid ? PlaceGreen : PlaceRed);
            int hoverId = store.EntityAt(Fix64.FromInt(px), Fix64.FromInt(py));
            if (hoverId >= 0)
            {
                var fp = store.FootprintOf(hoverId).Bbox();
                AddRectOutline(RemoveOutline, (
                    WorldUnits.CmToM(fp.MinX.ToInt()), WorldUnits.CmToM(fp.MinY.ToInt()),
                    WorldUnits.CmToM(fp.MaxX.ToInt()), WorldUnits.CmToM(fp.MaxY.ToInt())), navCellVisual * 0.5f, RemoveOrange);
            }
        }

        // 重烘脏 tile 闪烁:最近一笔报告发布后短暂高亮其受影响 tile 并集
        CurrentFlash(session, navCellVisual);

        // ── HUD(全部读内核现成状态) ─────────────────────────────
        DrawHud(session, runtime, (previewShown, previewValid));
    }

    /// <summary>放置合法性(演示口径,只读结构仓现成数据):光标处无既有结构,且足迹包围格内
    /// 无阻挡格(rect/path 轴对齐时包围盒即足迹;近似对演示预览足够,正式语义在指令执行侧)。</summary>
    private static bool CanPlace(CrowdStructuresStore store, S4DeployDemoRuntime.StructureChoice tpl, int xCm, int yCm)
    {
        if (store.EntityAt(Fix64.FromInt(xCm), Fix64.FromInt(yCm)) >= 0) return false;
        var (minX, minY, maxX, maxY) = FootprintVisual(tpl, xCm, yCm);
        int navCellsPerSide = store.NavCellsPerSide, navCellSizeCm = store.NavCellSizeCm;
        float cmPerMeter = WorldUnits.CmPerMeter;
        int cx0 = Math.Clamp((int)Math.Floor(minX * cmPerMeter / navCellSizeCm), 0, navCellsPerSide - 1), cy0 = Math.Clamp((int)Math.Floor(minY * cmPerMeter / navCellSizeCm), 0, navCellsPerSide - 1);
        int cx1 = Math.Clamp((int)Math.Floor(maxX * cmPerMeter / navCellSizeCm), 0, navCellsPerSide - 1), cy1 = Math.Clamp((int)Math.Floor(maxY * cmPerMeter / navCellSizeCm), 0, navCellsPerSide - 1);
        for (int cy = cy0; cy <= cy1; cy++)
        {
            int row = cy * navCellsPerSide;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                if (store.Blocked[row + cx] != 0) return false;
            }
        }

        return true;
    }

    /// <summary>足迹包围盒(视觉米)。rect = 中心 ± size/2;道路 = 光标向 +X 的条带(宽 size,长 RoadLength)。</summary>
    private static (float MinX, float MinY, float MaxX, float MaxY) FootprintVisual(S4DeployDemoRuntime.StructureChoice tpl, int xCm, int yCm)
    {
        if (tpl.IsRoad)
        {
            return (
                WorldUnits.CmToM(xCm),
                WorldUnits.CmToM(yCm - tpl.SizeCm / 2),
                WorldUnits.CmToM(xCm + tpl.RoadLengthCm),
                WorldUnits.CmToM(yCm + tpl.SizeCm / 2));
        }

        float half = WorldUnits.CmToM(tpl.SizeCm) * 0.5f;
        float x = WorldUnits.CmToM(xCm);
        float y = WorldUnits.CmToM(yCm);
        return (x - half, y - half, x + half, y + half);
    }

    private void AddRectOutline(RouteVisualId id, (float MinX, float MinY, float MaxX, float MaxY) rect, float thick, Vector4 color)
    {
        Span<Vector2> pts = stackalloc Vector2[5];
        pts[0] = new(rect.MinX, rect.MinY);
        pts[1] = new(rect.MaxX, rect.MinY);
        pts[2] = new(rect.MaxX, rect.MaxY);
        pts[3] = new(rect.MinX, rect.MaxY);
        pts[4] = pts[0];
        _routeVisuals.AddPolyline(id, pts, thick, color);
    }

    /// <summary>脏 tile 闪烁:报告 ReportTick 变化即起闪,FlashMs 内黄色轮廓线性淡出。
    /// tile 轮廓按导航格逐格细分:路线渲染器只对折点采样地面高度,1km tile 的四角直边
    /// 在丘陵地形会整段埋进山体(视野内又见不到角点),细分后贴地才可见。</summary>
    private void CurrentFlash(CrowdSimSession session, float navCellVisual)
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
        int navCellsPerSide = session.Config.NavCellsPerSide, tiles = session.Config.Hpa.NavCellsPerTile;
        int perRow = Math.Max(1, navCellsPerSide / tiles);
        float tileVisual = tiles * navCellVisual;
        var color = FlashYellow with { W = alpha };
        int shown = 0;
        foreach (int tile in report.UnionTiles)
        {
            var id = new RouteVisualId(200 + shown);
            float ox = tile % perRow * tileVisual, oy = tile / perRow * tileVisual;
            // 黄线宽 = 0.6 格长(37.5m):细线随地形起伏时实测可视性不足,加粗一档
            AddTileOutline(id, ox, oy, tileVisual, navCellVisual, navCellVisual * 0.6f, color);
            shown++;
        }
    }

    private void AddTileOutline(RouteVisualId id, float ox, float oy, float tileVisual, float stepVisual, float thick, Vector4 color)
    {
        int seg = Math.Max(1, (int)Math.Ceiling(tileVisual / stepVisual));
        var pts = new Vector2[seg * 4 + 1];
        int p = 0;
        for (int k = 0; k < seg; k++) pts[p++] = new(ox + tileVisual * k / seg, oy);
        for (int k = 0; k < seg; k++) pts[p++] = new(ox + tileVisual, oy + tileVisual * k / seg);
        for (int k = 0; k < seg; k++) pts[p++] = new(ox + tileVisual - tileVisual * k / seg, oy + tileVisual);
        for (int k = 0; k < seg; k++) pts[p++] = new(ox, oy + tileVisual - tileVisual * k / seg);
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

    internal void DrawHud(CrowdSimSession? session, CrowdSimulationRuntime? runtime, (bool Shown, bool Valid)? preview)
    {
        var telemetry = session?.Telemetry;
        var report = session?.LastRebakeReport;
        bool pending = session != null && NavVariantPending(session, _demo.ViewMode);
        bool expanded = session != null && _demo.TelemetryExpanded;
        ReadPlayerFogCounts(expanded ? session : null, out int visible, out int explored, out int ghosts);
        string templateLabel = _demo.Template.Label;
        string viewLabel = _demo.ViewLabel;
        if (!_hudText.Matches(session, telemetry, report, runtime, _demo, preview, pending, templateLabel, viewLabel, expanded, visible, explored, ghosts))
        {
            _hudText.Rebuild(_presentation, session, telemetry, report, runtime, _demo, preview, pending, templateLabel, viewLabel, expanded, visible, explored, ghosts);
        }

        CrowdHudPanel hud = _presentation.Hud;
        int left = hud.OriginXPx + 16;
        int top = hud.OriginYPx + 8;
        _overlay.AddRect(hud.OriginXPx, hud.OriginYPx, hud.WidthPx, hud.HeightPx, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(1f, 0.85f, 0.2f, 1f));
        _overlay.AddText(left, top, _presentation.Title, 20, new Vector4(1f, 0.92f, 0.35f, 1f));
        _overlay.AddText(left, top + 24, _hudText.Status, 16, new Vector4(1f, 1f, 1f, 1f));
        _overlay.AddText(left, top + 42, _hudText.Rebake, 16, new Vector4(1f, 1f, 1f, 1f));
        _overlay.AddText(left, top + 60, _hudText.Replay, 16, _demo.InteractiveMode ? new Vector4(1f, 0.55f, 0.4f, 1f) : new Vector4(0.8f, 1f, 0.8f, 1f));
        _overlay.AddText(left, top + 78, _hudText.Keys, 16, new Vector4(0.75f, 0.9f, 1f, 1f));
        _overlay.AddText(left, top + 96, _hudText.Telemetry, 16, new Vector4(0.85f, 0.95f, 1f, 1f));
        if (expanded) _overlay.AddText(left, top + 114, _hudText.TelemetryDetail, 16, new Vector4(0.85f, 0.95f, 1f, 1f));
    }

    /// <summary>玩家 1 的迷雾格计数已在刷新时维护,这里只按下标读取。</summary>
    private static void ReadPlayerFogCounts(CrowdSimSession? session, out int visible, out int explored, out int ghosts)
    {
        visible = explored = ghosts = 0;
        var fog = session?.Fog;
        if (fog == null) return;
        var index = session!.Config.PairRules.IndexByPlayerId;
        if ((uint)1 >= (uint)index.Length) return;
        int player = index[1];
        if ((uint)player >= (uint)fog.GroupOf.Length) return;
        int group = fog.GroupOf[player];
        visible = fog.VisibleCount[group];
        explored = fog.ExploredCount[group];
        ghosts = fog.GhostCount[group];
    }

    /// <summary>同一 tick 重复绘制复用字符串;数值变化新建一条,因为 ScreenOverlayBuffer.AddText 接收 string——呈现帧分配,不是仿真 tick 分配。</summary>
    private sealed class HudTextCache
    {
        public bool Ready;
        public bool SessionActive;
        public bool Expanded;
        public int Tick, Units, Idle, Moving, Arrived, Unreachable, Jumping, Selected;
        public long SubStepClamped, EvictStuck, Plans, Rebakes, FogRefreshes, Contacts, PathWaits;
        public int FogVisible, FogExplored, FogGhosts;
        public bool HasRebake;
        public int RebakeExec, RebakeReport, RebakeKind, Tiles, Hits, Misses, Orders, Evicted, Stuck;
        public bool RuntimePresent, Interactive;
        public int ReplayStatus, Divergence;
        public bool BuildMode, PreviewKnown, PreviewShown, PreviewValid, Pending;
        public int TemplateIndex, ViewMode;
        public string TemplateLabel = string.Empty;
        public string ViewLabel = string.Empty;
        public string Status = string.Empty;
        public string Rebake = string.Empty;
        public string Replay = string.Empty;
        public string Keys = string.Empty;
        public string Telemetry = string.Empty;
        public string TelemetryDetail = string.Empty;

        public bool Matches(
            CrowdSimSession? session,
            CrowdSimulationTelemetry? telemetry,
            CrowdRebakeReport? report,
            CrowdSimulationRuntime? runtime,
            S4DeployDemoRuntime demo,
            (bool Shown, bool Valid)? preview,
            bool pending,
            string templateLabel,
            string viewLabel,
            bool expanded,
            int fogVisible,
            int fogExplored,
            int fogGhosts)
        {
            if (!Ready) return false;
            if ((session != null) != SessionActive) return false;
            if (Expanded != expanded) return false;
            if (session != null)
            {
                if (Tick != session.TickCount || Units != session.Units.Count || Selected != session.SelectedCount) return false;
                if (Idle != telemetry!.Idle || Moving != telemetry.Moving || Arrived != telemetry.Arrived
                    || Unreachable != telemetry.Unreachable || Jumping != telemetry.Jumping) return false;
                if (SubStepClamped != telemetry.SubStepClamped || EvictStuck != telemetry.EvictStuck || Plans != telemetry.Plans
                    || Rebakes != telemetry.Rebakes || FogRefreshes != telemetry.FogRefreshes) return false;
                if (expanded && (Contacts != telemetry.Contacts || PathWaits != telemetry.PathWaits
                    || FogVisible != fogVisible || FogExplored != fogExplored || FogGhosts != fogGhosts)) return false;
                if ((report != null) != HasRebake) return false;
                if (report != null && (
                    RebakeExec != report.ExecTick || RebakeReport != report.ReportTick || RebakeKind != report.Kind
                    || Tiles != report.Tiles || Hits != report.Hits || Misses != report.Misses
                    || Orders != report.Orders || Evicted != report.Evicted || Stuck != report.Stuck)) return false;
            }

            if ((runtime != null) != RuntimePresent) return false;
            if (runtime != null && (ReplayStatus != runtime.ReplayStatus || Divergence != runtime.ReplayDivergenceTick)) return false;
            if (Interactive != demo.InteractiveMode || BuildMode != demo.BuildMode) return false;
            if (TemplateIndex != demo.TemplateIndex || ViewMode != demo.ViewMode || Pending != pending) return false;
            if (!string.Equals(TemplateLabel, templateLabel, StringComparison.Ordinal)
                || !string.Equals(ViewLabel, viewLabel, StringComparison.Ordinal)) return false;
            bool previewKnown = preview.HasValue;
            if (PreviewKnown != previewKnown) return false;
            if (previewKnown && (PreviewShown != preview!.Value.Shown || PreviewValid != preview.Value.Valid)) return false;
            return true;
        }

        public void Rebuild(
            CrowdPresentationConfig presentation,
            CrowdSimSession? session,
            CrowdSimulationTelemetry? telemetry,
            CrowdRebakeReport? report,
            CrowdSimulationRuntime? runtime,
            S4DeployDemoRuntime demo,
            (bool Shown, bool Valid)? preview,
            bool pending,
            string templateLabel,
            string viewLabel,
            bool expanded,
            int fogVisible,
            int fogExplored,
            int fogGhosts)
        {
            Ready = true;
            SessionActive = session != null;
            Expanded = expanded;
            CrowdDeployPresentation deploy = presentation.Deploy
                ?? throw new InvalidOperationException("部署演示 HUD 缺少 deploy 文案。");
            if (session == null)
            {
                Status = "会话未激活";
                Rebake = "最近重烘:-";
                Replay = "-";
                Telemetry = "";
                TelemetryDetail = "";
            }
            else
            {
                Tick = session.TickCount;
                Units = session.Units.Count;
                Selected = session.SelectedCount;
                Idle = telemetry!.Idle;
                Moving = telemetry.Moving;
                Arrived = telemetry.Arrived;
                Unreachable = telemetry.Unreachable;
                Jumping = telemetry.Jumping;
                Status = $"tick {Tick};单位 {Units}(闲 {Idle}/行 {Moving}/达 {Arrived}" +
                         (Unreachable + Jumping > 0 ? $"/不可达 {Unreachable}/跳跃 {Jumping}" : "") + $");选中 {Selected}";
                HasRebake = report != null;
                if (report == null)
                {
                    Rebake = "最近重烘:无结构变更";
                }
                else
                {
                    RebakeExec = report.ExecTick;
                    RebakeReport = report.ReportTick;
                    RebakeKind = report.Kind;
                    Tiles = report.Tiles;
                    Hits = report.Hits;
                    Misses = report.Misses;
                    Orders = report.Orders;
                    Evicted = report.Evicted;
                    Stuck = report.Stuck;
                    Rebake = $"最近重烘:tick {RebakeExec}→{RebakeReport} {(RebakeKind == CrowdRebakeReport.KindPlace ? "建造" : "拆除")}" +
                             $" · 脏 tile {Tiles} · 缓存命中 {Hits}/{Hits + Misses}" +
                             $" · 重规划 {Orders} · 挤离 {Evicted} · 无处安放 {Stuck}";
                }

                SubStepClamped = telemetry.SubStepClamped;
                EvictStuck = telemetry.EvictStuck;
                Plans = telemetry.Plans;
                Rebakes = telemetry.Rebakes;
                FogRefreshes = telemetry.FogRefreshes;
                Telemetry = $"subStepClamped {SubStepClamped} evictStuck {EvictStuck} plans {Plans} rebake {Rebakes} fog {FogRefreshes}";
                if (expanded)
                {
                    Contacts = telemetry.Contacts;
                    PathWaits = telemetry.PathWaits;
                    FogVisible = fogVisible;
                    FogExplored = fogExplored;
                    FogGhosts = fogGhosts;
                    TelemetryDetail = $"contacts {Contacts} pathWait {PathWaits} fogCells vis {FogVisible} explored {FogExplored} ghost {FogGhosts}";
                }
            }

            RuntimePresent = runtime != null;
            Interactive = demo.InteractiveMode;
            Replay = Interactive
                ? "回放已停用(交互模式)"
                : runtime == null
                    ? "回放:-"
                    : runtime.ReplayStatus switch
                    {
                        1 => "回放:逐位一致",
                        2 => $"回放:分歧 @tick {runtime.ReplayDivergenceTick}",
                        _ => "回放:待触发",
                    };
            if (runtime != null)
            {
                ReplayStatus = runtime.ReplayStatus;
                Divergence = runtime.ReplayDivergenceTick;
            }

            BuildMode = demo.BuildMode;
            TemplateIndex = demo.TemplateIndex;
            ViewMode = demo.ViewMode;
            Pending = pending;
            TemplateLabel = templateLabel;
            ViewLabel = viewLabel;
            PreviewKnown = preview.HasValue;
            if (preview.HasValue)
            {
                PreviewShown = preview.Value.Shown;
                PreviewValid = preview.Value.Valid;
            }

            string placeHint = preview == null
                ? ""
                : preview.Value.Shown
                    ? (preview.Value.Valid ? deploy.PlaceOk : deploy.PlaceBlocked)
                    : deploy.PlaceOffMap;
            string activity = BuildMode
                ? deploy.BuildActivity
                    .Replace("{label}", templateLabel, StringComparison.Ordinal)
                    .Replace("{placeHint}", placeHint, StringComparison.Ordinal)
                : deploy.ObserveActivity;
            string pendingText = pending ? deploy.PendingNav : "";
            Keys = presentation.Hud.Keys
                .Replace("{view}", viewLabel, StringComparison.Ordinal)
                .Replace("{templateIndex}", (TemplateIndex + 1).ToString(), StringComparison.Ordinal)
                .Replace("{activity}", activity, StringComparison.Ordinal)
                .Replace("{pending}", pendingText, StringComparison.Ordinal);
        }
    }
}
