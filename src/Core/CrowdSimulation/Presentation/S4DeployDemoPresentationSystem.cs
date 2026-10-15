using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Arch.System;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
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
    private static readonly Vector4 BoomAmber = new(1f, 0.55f, 0.12f, 0.9f);
    private static readonly Vector4 FlashYellow = new(1f, 0.9f, 0.2f, 1f);

    private static readonly RouteVisualId GoalMarker = new(1);
    private static readonly RouteVisualId HoverMarker = new(2);
    private static readonly RouteVisualId RemoveOutline = new(4);
    private static readonly RouteVisualId BoomInner = new(5);
    private static readonly RouteVisualId BoomOuter = new(6);
    private static readonly RouteVisualId SpawnMarker = new(7);
    private static readonly RouteVisualId WireframeId = new(CrowdSimulationLimits.WireframeRouteVisualId);
    private static readonly RouteVisualId FlashBase = new(200);

    private const long FlashMs = 2500; // 脏 tile 闪烁时长(呈现口径,与仿真无关)

    private readonly S4DeployDemoRuntime _demo;
    private readonly RouteVisualBuffer _routeVisuals;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly Func<CrowdSimulationRuntime?> _runtimeSource;
    private readonly Func<CrowdSimSession?> _sessionSource;
    private readonly CrowdPresentationConfig _presentation;
    private readonly Func<int>? _localSeatScope;

    private readonly S4DeployNavWireframe _wire = new();
    private readonly HudTextCache _hudText = new();
    private CrowdDebugViewFrame? _debugView;
    private CrowdPlayerViewHost? _playerView;
    internal Func<bool>? SuppressDominantGoal;
    private int _flashReportTick = -1;
    private long _flashStartMs;
    private Vector2[][] _flashLines = Array.Empty<Vector2[]>();
    private int _flashLineCount;
    private int _flashLineReport = int.MinValue;
    private int[] _previewCells = Array.Empty<int>();
    private byte[] _previewFlags = Array.Empty<byte>();
    private Vector2[][] _previewQuads = Array.Empty<Vector2[]>();
    private bool _placeColorsReady;
    private Vector4 _placeLegal;
    private Vector4 _placeIllegal;
    private Vector4 _placeAnchor;
    private Vector4 _placePortal;
    private static readonly RouteVisualId PreviewCellBase = new(4096);

    internal S4DeployDemoPresentationSystem(
        S4DeployDemoRuntime demo,
        RouteVisualBuffer routeVisuals,
        ScreenOverlayBuffer overlay,
        Func<CrowdSimulationRuntime?> runtimeSource,
        Func<CrowdSimSession?> sessionSource,
        CrowdPresentationConfig presentation,
        Func<int>? localSeatScope = null)
    {
        _demo = demo;
        _routeVisuals = routeVisuals;
        _overlay = overlay;
        _runtimeSource = runtimeSource;
        _sessionSource = sessionSource;
        _presentation = presentation;
        _localSeatScope = localSeatScope;
    }

    internal void UseDebugView(CrowdDebugViewFrame frame) => _debugView = frame;

    internal void UsePlayerView(CrowdPlayerViewHost host) => _playerView = host;

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        var session = _sessionSource();
        var runtime = _runtimeSource();
        var ground = _demo.HoverCm;

        // 路线通道是 gameplay 视觉。
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
        bool suppressGoal = SuppressDominantGoal != null && SuppressDominantGoal();
        if (_demo.ViewMode == 0 && !suppressGoal && S4DeployDemoViewProjector.DominantGroup(session) is { Goal: >= 0 } group)
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

        bool previewValid = false, previewShown = false;
        var previewReason = CrowdPlacementReject.None;
        if (ground is { } p)
        {
            int px = (int)Math.Floor(p.X), py = (int)Math.Floor(p.Y);
            CrowdStructuresStore? store = session.Structures;
            switch (_demo.Tool)
            {
                case CrowdClickTool.Boom when _demo.BoomRadiusCm > 0:
                    previewShown = true;
                    previewValid = true;
                    float inner = WorldUnits.CmToM(_demo.BoomRadiusCm);
                    var at = new Vector2(WorldUnits.CmToM(px), WorldUnits.CmToM(py));
                    _routeVisuals.AddMarker(BoomInner, at, RouteVisualMarkerShape.Ring, inner, navCellVisual * 0.2f, BoomAmber);
                    float outer = inner * (float)_demo.BlastRingScale.ToDouble();
                    if (outer > inner)
                    {
                        _routeVisuals.AddMarker(BoomOuter, at, RouteVisualMarkerShape.Ring, outer, navCellVisual * 0.12f, BoomAmber);
                    }

                    break;
                case CrowdClickTool.Spawn:
                    previewShown = true;
                    previewValid = true;
                    _routeVisuals.AddMarker(
                        SpawnMarker,
                        new Vector2(WorldUnits.CmToM(px), WorldUnits.CmToM(py)),
                        RouteVisualMarkerShape.Ring,
                        navCellVisual * 0.8f,
                        navCellVisual * 0.15f,
                        PlaceGreen);
                    break;
                case CrowdClickTool.Structure when store != null:
                    previewShown = true;
                    previewReason = JudgePreview(session, store, _demo.Template, px, py, out int painted);
                    previewValid = previewReason == CrowdPlacementReject.None;
                    DrawPlacementCells(store, painted, navCellVisual * 0.5f);
                    DrawEntityOutline(store, px, py, navCellVisual);
                    break;
                case CrowdClickTool.Demolish when store != null:
                    previewShown = DrawEntityOutline(store, px, py, navCellVisual);
                    break;
            }
        }

        // 重烘脏 tile 闪烁:最近一笔报告发布后短暂高亮其受影响 tile 并集
        CurrentFlash(session, navCellVisual);

        _playerView?.Draw(session, _routeVisuals, _overlay);
        _debugView?.Draw(session, _overlay);

        // HUD 全部读内核现成状态。
        DrawHud(session, runtime, (previewShown, previewValid, previewReason));
    }

    /// <summary>预览足迹与 <see cref="S4DeployDemoRuntime"/> 提交的 placeStructure 同一形状。</summary>
    private CrowdPlacementReject JudgePreview(
        CrowdSimSession session,
        CrowdStructuresStore store,
        S4DeployDemoRuntime.StructureChoice tpl,
        int xCm,
        int yCm,
        out int count)
    {
        int index = store.TemplateIndexOf(tpl.Id);
        if (index < 0) throw new InvalidOperationException($"未知结构模板 \"{tpl.Id}\"。");
        var template = session.Config.Structures.Templates[index];
        int x0 = xCm, y0 = yCm, x1 = 0, y1 = 0;
        if (tpl.IsSpan)
        {
            x0 = _demo.HasSpanAnchor ? _demo.SpanAnchorX : xCm;
            y0 = _demo.HasSpanAnchor ? _demo.SpanAnchorY : yCm;
            x1 = xCm;
            y1 = yCm;
        }
        else if (tpl.IsRoad)
        {
            x1 = xCm + tpl.RoadLengthCm;
            y1 = yCm;
        }

        var footprint = CrowdStructureFootprint.Make(
            template, Fix64.FromInt(x0), Fix64.FromInt(y0), Fix64.FromInt(tpl.SizeCm), Fix64.FromInt(x1), Fix64.FromInt(y1));
        return CrowdStructurePlacement.Judge(store, template, footprint, ref _previewCells, ref _previewFlags, out count);
    }

    private void DrawPlacementCells(CrowdStructuresStore store, int count, float thick)
    {
        EnsurePlaceColors();
        if (_previewQuads.Length < count)
        {
            var grown = new Vector2[count][];
            Array.Copy(_previewQuads, grown, _previewQuads.Length);
            _previewQuads = grown;
        }

        float cell = WorldUnits.CmToM(store.NavCellSizeCm);
        int side = store.NavCellsPerSide;
        for (int i = 0; i < count; i++)
        {
            var pts = _previewQuads[i] ??= new Vector2[5];
            int index = _previewCells[i];
            int x = index % side;
            int y = index / side;
            float x0 = x * cell;
            float y0 = y * cell;
            pts[0] = new Vector2(x0, y0);
            pts[1] = new Vector2(x0 + cell, y0);
            pts[2] = new Vector2(x0 + cell, y0 + cell);
            pts[3] = new Vector2(x0, y0 + cell);
            pts[4] = pts[0];
            byte flag = _previewFlags[i];
            Vector4 color = (flag & CrowdStructurePlacement.FlagIllegal) != 0
                ? _placeIllegal
                : (flag & CrowdStructurePlacement.FlagAnchor) != 0
                    ? _placeAnchor
                    : (flag & CrowdStructurePlacement.FlagPortal) != 0
                        ? _placePortal
                        : _placeLegal;
            _routeVisuals.AddPolylineShared(new RouteVisualId(PreviewCellBase.Value + i), pts, thick, color);
        }
    }

    private void EnsurePlaceColors()
    {
        if (_placeColorsReady) return;
        var deploy = _presentation.Deploy ?? throw new InvalidOperationException("部署演示缺少 deploy 配色。");
        _placeLegal = CrowdPresentationConfig.Rgba(deploy.PlaceLegal, "deploy.placeLegal");
        _placeIllegal = CrowdPresentationConfig.Rgba(deploy.PlaceIllegal, "deploy.placeIllegal");
        _placeAnchor = CrowdPresentationConfig.Rgba(deploy.PlaceAnchor, "deploy.placeAnchor");
        _placePortal = CrowdPresentationConfig.Rgba(deploy.PlacePortal, "deploy.placePortal");
        _placeColorsReady = true;
    }

    private bool DrawEntityOutline(CrowdStructuresStore store, int xCm, int yCm, float navCellVisual)
    {
        int hoverId = store.EntityAt(Fix64.FromInt(xCm), Fix64.FromInt(yCm));
        if (hoverId < 0) return false;
        var fp = store.FootprintOf(hoverId).Bbox();
        AddRectOutline(RemoveOutline, (
            WorldUnits.CmToM(fp.MinX.ToInt()), WorldUnits.CmToM(fp.MinY.ToInt()),
            WorldUnits.CmToM(fp.MaxX.ToInt()), WorldUnits.CmToM(fp.MaxY.ToInt())), navCellVisual * 0.5f, RemoveOrange);
        return true;
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
        if (report.ReportTick != _flashLineReport)
            CacheFlashLines(report, perRow, tileVisual, navCellVisual);
        var color = FlashYellow with { W = alpha };
        // 黄线宽 = 0.6 格长(37.5m):细线随地形起伏时实测可视性不足,加粗一档
        float thick = navCellVisual * 0.6f;
        for (int i = 0; i < _flashLineCount; i++)
            _routeVisuals.AddPolylineShared(new RouteVisualId(FlashBase.Value + i), _flashLines[i], thick, color);
    }

    /// <summary>折点只随报告变。闪烁窗内每帧只改透明度,数组留给 AddPolylineShared,避免逐帧拷贝。</summary>
    private void CacheFlashLines(CrowdRebakeReport report, int perRow, float tileVisual, float stepVisual)
    {
        int n = report.UnionTiles.Count;
        if (_flashLines.Length < n)
        {
            var grown = new Vector2[n][];
            Array.Copy(_flashLines, grown, _flashLines.Length);
            _flashLines = grown;
        }

        int seg = Math.Max(1, (int)Math.Ceiling(tileVisual / stepVisual));
        int len = seg * 4 + 1;
        for (int i = 0; i < n; i++)
        {
            int tile = report.UnionTiles[i];
            float ox = tile % perRow * tileVisual;
            float oy = tile / perRow * tileVisual;
            var pts = _flashLines[i];
            if (pts == null || pts.Length != len)
                pts = new Vector2[len];
            int p = 0;
            for (int k = 0; k < seg; k++) pts[p++] = new(ox + tileVisual * k / seg, oy);
            for (int k = 0; k < seg; k++) pts[p++] = new(ox + tileVisual, oy + tileVisual * k / seg);
            for (int k = 0; k < seg; k++) pts[p++] = new(ox + tileVisual - tileVisual * k / seg, oy + tileVisual);
            for (int k = 0; k < seg; k++) pts[p++] = new(ox, oy + tileVisual - tileVisual * k / seg);
            pts[p] = pts[0];
            _flashLines[i] = pts;
        }

        _flashLineCount = n;
        _flashLineReport = report.ReportTick;
    }

    internal static bool NavVariantPending(CrowdSimSession session, int viewMode)
    {
        if (viewMode != 1 && viewMode != 2) return false;
        var group = S4DeployDemoViewProjector.DominantGroup(session);
        if (group == null || group.NavId < CrowdBeliefNavs.BeliefStride) return false;
        return !session.TryGetMaterializedNav(group.NavId, out _);
    }

    internal void DrawHud(CrowdSimSession? session, CrowdSimulationRuntime? runtime, (bool Shown, bool Valid, CrowdPlacementReject Reason)? preview)
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
        _overlay.AddText(left, top + 24, _hudText.Tools, 16, new Vector4(1f, 0.86f, 0.45f, 1f));
        _overlay.AddText(left, top + 42, _hudText.Status, 16, new Vector4(1f, 1f, 1f, 1f));
        _overlay.AddText(left, top + 60, _hudText.Rebake, 16, new Vector4(1f, 1f, 1f, 1f));
        _overlay.AddText(left, top + 78, _hudText.Replay, 16, _demo.InteractiveMode ? new Vector4(1f, 0.55f, 0.4f, 1f) : new Vector4(0.8f, 1f, 0.8f, 1f));
        _overlay.AddText(left, top + 96, _hudText.Keys, 16, new Vector4(0.75f, 0.9f, 1f, 1f));
        _overlay.AddText(left, top + 114, _hudText.Telemetry, 16, new Vector4(0.85f, 0.95f, 1f, 1f));
        if (expanded) _overlay.AddText(left, top + 132, _hudText.TelemetryDetail, 16, new Vector4(0.85f, 0.95f, 1f, 1f));
        Vector4 validationColor = _hudText.ValidationFailed
            ? new Vector4(1f, 0.35f, 0.3f, 1f)
            : _hudText.ValidationRan
                ? new Vector4(0.45f, 1f, 0.55f, 1f)
                : new Vector4(0.75f, 0.75f, 0.75f, 1f);
        _overlay.AddText(left, top + 150, _hudText.Validation, 16, validationColor);
        Vector4 fuzzColor = _hudText.FuzzFailed
            ? new Vector4(1f, 0.35f, 0.3f, 1f)
            : _hudText.FuzzRan
                ? new Vector4(0.45f, 1f, 0.55f, 1f)
                : new Vector4(0.75f, 0.75f, 0.75f, 1f);
        _overlay.AddText(left, top + 168, _hudText.Fuzz, 16, fuzzColor);
    }

    /// <summary>可见数、已探索数取本机座位的视野组。没有座位时保持 0，不改用第一组。</summary>
    private void ReadPlayerFogCounts(CrowdSimSession? session, out int visible, out int explored, out int ghosts)
    {
        visible = explored = ghosts = 0;
        var fog = session?.Fog;
        if (fog == null || _localSeatScope == null) return;
        int scope = _localSeatScope();
        if (scope <= 0) return;
        int group = scope - 1;
        if ((uint)group >= (uint)fog.VisionGroupCount) return;
        visible = fog.VisibleCountOf(group);
        explored = fog.ExploredCountOf(group);
        ghosts = fog.GhostCount[group];
    }

    /// <summary>选中数读本机座位玩家代表的 selected 集合。没有座位时为 -1，HUD 显示为横线。</summary>
    private static int LocalSelected(CrowdSimSession session, CrowdSimulationRuntime? runtime)
        => runtime != null && runtime.TryLocalSeatPlayerId(out int seatPlayer)
            ? session.SelectedCollectionCount(seatPlayer)
            : -1;

    private const string NoSessionValidation = "校验:无会话";
    private const string NoSessionFuzz = CrowdSimulationRuntime.DebugFuzzNoSessionLine;

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
        public int RebakeExec, RebakeReport, RebakeKind, Tiles, Hits, Misses, Orders, Evicted, Stuck, CostOnly, Contexts;
        public long ElapsedTicks;
        public bool RuntimePresent, Interactive;
        public int ReplayStatus, Divergence;
        public string ReplaySection = string.Empty;
        public int ReplayDiagnosticTick = -1;
        public CrowdSimDiagnosticSection ReplayDiagnostic;
        public bool BuildMode, PreviewKnown, PreviewShown, PreviewValid, Pending;
        public CrowdPlacementReject PreviewReason;
        public int TemplateIndex, ViewMode;
        public int Tool, SpeedPermille, FixedHz, RebakeSlices;
        public bool PlaybackHeld, HasSpanAnchor;
        public long AvoidRaw;
        public string TickWindow = string.Empty;
        public string TemplateLabel = string.Empty;
        public string ViewLabel = string.Empty;
        public string Status = string.Empty;
        public string Tools = string.Empty;
        public string Rebake = string.Empty;
        public string Replay = string.Empty;
        public string Keys = string.Empty;
        public string Telemetry = string.Empty;
        public string TelemetryDetail = string.Empty;
        public string Validation = string.Empty;
        public bool ValidationFailed;
        public bool ValidationRan;
        public string Fuzz = string.Empty;
        public bool FuzzFailed;
        public bool FuzzRan;

        public bool Matches(
            CrowdSimSession? session,
            CrowdSimulationTelemetry? telemetry,
            CrowdRebakeReport? report,
            CrowdSimulationRuntime? runtime,
            S4DeployDemoRuntime demo,
            (bool Shown, bool Valid, CrowdPlacementReject Reason)? preview,
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
                if (Tick != session.TickCount || Units != session.Units.Count || Selected != LocalSelected(session, runtime)) return false;
                if (Idle != telemetry!.Idle || Moving != telemetry.Moving || Arrived != telemetry.Arrived
                    || Unreachable != telemetry.Unreachable || Jumping != telemetry.Jumping) return false;
                if (SubStepClamped != telemetry.SubStepClamped || EvictStuck != telemetry.EvictStuck || Plans != telemetry.Plans
                    || Rebakes != telemetry.Rebakes || FogRefreshes != telemetry.FogRefreshes) return false;
                if (expanded && (Contacts != telemetry.Contacts || PathWaits != telemetry.PathWaits
                    || FogVisible != fogVisible || FogExplored != fogExplored || FogGhosts != fogGhosts)) return false;
                if ((report != null) != HasRebake) return false;
                if (FixedHz != session.Config.FixedHz || AvoidRaw != session.AvoidHz.RawValue
                    || RebakeSlices != session.RebakeSliceCount) return false;
                if (report != null && (
                    RebakeExec != report.ExecTick || RebakeReport != report.ReportTick || RebakeKind != report.Kind
                    || Tiles != report.Tiles || Hits != report.Hits || Misses != report.Misses
                    || Orders != report.Orders || Evicted != report.Evicted || Stuck != report.Stuck
                    || CostOnly != report.CostOnly || Contexts != report.Contexts || ElapsedTicks != report.ElapsedTicks)) return false;
            }
            else if (FixedHz != 0 || AvoidRaw != 0 || RebakeSlices != -1)
            {
                return false;
            }

            if ((runtime != null) != RuntimePresent) return false;
            if (runtime != null && (ReplayStatus != runtime.ReplayStatus || Divergence != runtime.ReplayDivergenceTick
                || ReplayDiagnosticTick != runtime.ReplayDiagnosticTick
                || ReplayDiagnostic != runtime.ReplayDivergenceDiagnostic
                || !string.Equals(ReplaySection, runtime.ReplayDivergenceSection, StringComparison.Ordinal)
                || ValidationFailed != runtime.DebugValidationFailed
                || ValidationRan != runtime.DebugValidationRan
                || !string.Equals(Validation, runtime.DebugValidationLine, StringComparison.Ordinal)
                || FuzzFailed != runtime.DebugFuzzFailed
                || FuzzRan != runtime.DebugFuzzRan
                || !string.Equals(Fuzz, runtime.DebugFuzzLine, StringComparison.Ordinal))) return false;
            if (runtime == null && (ValidationFailed || ValidationRan || !string.Equals(Validation, NoSessionValidation, StringComparison.Ordinal)
                || FuzzFailed || FuzzRan || !string.Equals(Fuzz, NoSessionFuzz, StringComparison.Ordinal))) return false;
            if (Interactive != demo.InteractiveMode || BuildMode != demo.BuildMode) return false;
            if (Tool != (int)demo.Tool || PlaybackHeld != demo.PlaybackHeld || SpeedPermille != demo.SpeedPermille
                || HasSpanAnchor != demo.HasSpanAnchor
                || !string.Equals(TickWindow, demo.TickHzWindow, StringComparison.Ordinal)) return false;
            if (TemplateIndex != demo.TemplateIndex || ViewMode != demo.ViewMode || Pending != pending) return false;
            if (!string.Equals(TemplateLabel, templateLabel, StringComparison.Ordinal)
                || !string.Equals(ViewLabel, viewLabel, StringComparison.Ordinal)) return false;
            bool previewKnown = preview.HasValue;
            if (PreviewKnown != previewKnown) return false;
            if (previewKnown && (PreviewShown != preview!.Value.Shown || PreviewValid != preview.Value.Valid || PreviewReason != preview.Value.Reason)) return false;
            return true;
        }

        public void Rebuild(
            CrowdPresentationConfig presentation,
            CrowdSimSession? session,
            CrowdSimulationTelemetry? telemetry,
            CrowdRebakeReport? report,
            CrowdSimulationRuntime? runtime,
            S4DeployDemoRuntime demo,
            (bool Shown, bool Valid, CrowdPlacementReject Reason)? preview,
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
                FixedHz = 0;
                AvoidRaw = 0;
                RebakeSlices = -1;
                Status = "会话未激活";
                Rebake = "最近重烘:-";
                Replay = "-";
                Telemetry = "";
                TelemetryDetail = "";
            }
            else
            {
                Tick = session.TickCount;
                FixedHz = session.Config.FixedHz;
                AvoidRaw = session.AvoidHz.RawValue;
                RebakeSlices = session.RebakeSliceCount;
                Units = session.Units.Count;
                Selected = LocalSelected(session, runtime);
                Idle = telemetry!.Idle;
                Moving = telemetry.Moving;
                Arrived = telemetry.Arrived;
                Unreachable = telemetry.Unreachable;
                Jumping = telemetry.Jumping;
                string selectedText = Selected < 0 ? "-" : Selected.ToString();
                Status = $"tick {Tick};单位 {Units}(闲 {Idle}/行 {Moving}/达 {Arrived}" +
                         (Unreachable + Jumping > 0 ? $"/不可达 {Unreachable}/跳跃 {Jumping}" : "") + $");选中 {selectedText}";
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
                    CostOnly = report.CostOnly;
                    Contexts = report.Contexts;
                    ElapsedTicks = report.ElapsedTicks;
                    string msText = (ElapsedTicks / (double)Stopwatch.Frequency * 1000d).ToString("0.##", CultureInfo.InvariantCulture);
                    Rebake = $"最近重烘:tick {RebakeExec}→{RebakeReport} {(RebakeKind == CrowdRebakeReport.KindPlace ? "建造" : "拆除")}" +
                             $" · 脏 tile {Tiles} · 仅代价 {CostOnly} · 可通行 {Contexts - CostOnly}" +
                             $" · 缓存命中 {Hits}/{Hits + Misses}" +
                             $" · 重规划 {Orders} · 挤离 {Evicted} · 无处安放 {Stuck} · 耗时 {msText} ms";
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
                        1 => FormatReplayAgreement(runtime),
                        2 => FormatReplayDivergence(runtime),
                        _ => "回放:待触发",
                    };
            if (runtime != null)
            {
                ReplayStatus = runtime.ReplayStatus;
                Divergence = runtime.ReplayDivergenceTick;
                ReplaySection = runtime.ReplayDivergenceSection;
                ReplayDiagnosticTick = runtime.ReplayDiagnosticTick;
                ReplayDiagnostic = runtime.ReplayDivergenceDiagnostic;
            }
            else
            {
                ReplaySection = string.Empty;
                ReplayDiagnosticTick = -1;
                ReplayDiagnostic = CrowdSimDiagnosticSection.None;
            }

            BuildMode = demo.BuildMode;
            Tool = (int)demo.Tool;
            PlaybackHeld = demo.PlaybackHeld;
            SpeedPermille = demo.SpeedPermille;
            HasSpanAnchor = demo.HasSpanAnchor;
            TickWindow = demo.TickHzWindow;
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
                PreviewReason = preview.Value.Reason;
            }

            if (runtime == null)
            {
                Validation = NoSessionValidation;
                ValidationFailed = false;
                ValidationRan = false;
                Fuzz = NoSessionFuzz;
                FuzzFailed = false;
                FuzzRan = false;
            }
            else
            {
                Validation = runtime.DebugValidationLine;
                ValidationFailed = runtime.DebugValidationFailed;
                ValidationRan = runtime.DebugValidationRan;
                Fuzz = runtime.DebugFuzzLine;
                FuzzFailed = runtime.DebugFuzzFailed;
                FuzzRan = runtime.DebugFuzzRan;
            }

            string placeHint = preview == null
                ? ""
                : preview.Value.Shown
                    ? PlaceHint(deploy, preview.Value.Reason)
                    : deploy.PlaceOffMap;
            string activity = BuildMode
                ? deploy.BuildActivity
                    .Replace("{label}", templateLabel, StringComparison.Ordinal)
                    .Replace("{placeHint}", placeHint, StringComparison.Ordinal)
                : deploy.ObserveActivity;
            string pendingText = pending ? deploy.PendingNav : "";
            string avoid = session == null
                ? "-"
                : session.AvoidHz.ToDouble().ToString("0.###", CultureInfo.InvariantCulture);
            string hz = session == null ? "-" : session.Config.FixedHz.ToString(CultureInfo.InvariantCulture);
            string slices = session == null ? "-" : session.RebakeSliceCount.ToString(CultureInfo.InvariantCulture);
            string window = string.IsNullOrEmpty(demo.TickHzWindow) ? "-" : demo.TickHzWindow;
            string speed = demo.PlaybackHeld
                ? "暂停"
                : "×" + (demo.SpeedPermille / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
            string span = demo.HasSpanAnchor ? " · 连线起点已定" : "";
            Tools = $"工具 {demo.ToolLabel} · 步进 {hz} Hz（窗 {window}）· 避让 {avoid} Hz · {speed} · 切片 {slices}{span}";
            Keys = presentation.Hud.Keys
                .Replace("{view}", viewLabel, StringComparison.Ordinal)
                .Replace("{tool}", demo.ToolLabel, StringComparison.Ordinal)
                .Replace("{templateIndex}", (TemplateIndex + 1).ToString(), StringComparison.Ordinal)
                .Replace("{activity}", activity, StringComparison.Ordinal)
                .Replace("{pending}", pendingText, StringComparison.Ordinal);
        }
    }

    private static string PlaceHint(CrowdDeployPresentation deploy, CrowdPlacementReject reason) => reason switch
    {
        CrowdPlacementReject.None => deploy.PlaceOk,
        CrowdPlacementReject.OutOfBounds => Hint(deploy.PlaceOutOfBounds, "placeOutOfBounds"),
        CrowdPlacementReject.OverlapsStructure => Hint(deploy.PlaceOverlapsStructure, "placeOverlapsStructure"),
        CrowdPlacementReject.OverlapsBlocked => Hint(deploy.PlaceOverlapsBlocked, "placeOverlapsBlocked"),
        CrowdPlacementReject.InteriorArea => Hint(deploy.PlaceSpanTerrain, "placeSpanTerrain"),
        CrowdPlacementReject.EndpointArea => Hint(deploy.PlaceAnchorTerrain, "placeAnchorTerrain"),
        _ => throw new InvalidOperationException($"未知放置拒绝原因 {reason}。"),
    };

    private static string Hint(string text, string key)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.deploy.{key} 必填。");
        }

        return text;
    }

    private static string FormatReplayAgreement(CrowdSimulationRuntime runtime)
    {
        string diag = CrowdSimChecksum.FormatDiagnostic(runtime.ReplayDivergenceDiagnostic);
        return diag.Length == 0
            ? "回放:逐位一致"
            : $"回放:逐位一致 diag differs: {diag} @tick {runtime.ReplayDiagnosticTick}";
    }

    private static string FormatReplayDivergence(CrowdSimulationRuntime runtime)
    {
        string head = string.IsNullOrEmpty(runtime.ReplayDivergenceSection)
            ? $"回放:分歧 @tick {runtime.ReplayDivergenceTick}"
            : $"回放:分歧 @tick {runtime.ReplayDivergenceTick} {runtime.ReplayDivergenceSection}";
        string diag = CrowdSimChecksum.FormatDiagnostic(runtime.ReplayDivergenceDiagnostic);
        return diag.Length == 0
            ? head
            : $"{head} diag differs: {diag} @tick {runtime.ReplayDiagnosticTick}";
    }
}
