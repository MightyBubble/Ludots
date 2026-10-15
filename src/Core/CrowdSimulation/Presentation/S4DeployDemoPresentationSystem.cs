using System;
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
/// S4 部署演示世界层:分层视图线框、建造足迹预览、重烘脏 tile 闪烁。
/// 键鼠由 <see cref="S4DeployInputSystem"/> 写入演示态与群体指令队列,这里只读状态。
/// 文字面板由界面 mod 经 <see cref="CrowdHudFeed"/> 读取，这里只把本帧预览结论交给它。
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
    private readonly Func<CrowdSimSession?> _sessionSource;
    private readonly CrowdPresentationConfig _presentation;
    private readonly CrowdHudFeed _feed;

    private readonly S4DeployNavWireframe _wire = new();
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
    private Vector4 _placeEndpoint;
    private Vector4 _placePortal;
    private static readonly RouteVisualId PreviewCellBase = new(4096);

    internal S4DeployDemoPresentationSystem(
        S4DeployDemoRuntime demo,
        RouteVisualBuffer routeVisuals,
        ScreenOverlayBuffer overlay,
        Func<CrowdSimSession?> sessionSource,
        CrowdPresentationConfig presentation,
        CrowdHudFeed feed)
    {
        _demo = demo;
        _routeVisuals = routeVisuals;
        _overlay = overlay;
        _sessionSource = sessionSource;
        _presentation = presentation;
        _feed = feed;
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
        var pointer = _demo.HoverCm;

        // 路线通道是 gameplay 视觉。
        _routeVisuals.BeginFrame();
        if (session == null)
        {
            _feed.NotePreview(false, false, false, CrowdPlacementReject.None);
            return;
        }

        float navCellVisual = WorldUnits.CmToM(session.Config.NavCellSizeCm);
        if (pointer is { } hover)
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
        if (pointer is { } p)
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
                    previewReason = JudgePreview(session, store, _demo.Choice, px, py, out int painted);
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
        _feed.NotePreview(pointer.HasValue, previewShown, previewValid, previewReason);
    }

    /// <summary>预览足迹与 <see cref="S4DeployDemoRuntime"/> 提交的 placeStructure 同一形状。</summary>
    private CrowdPlacementReject JudgePreview(
        CrowdSimSession session,
        CrowdStructuresStore store,
        S4DeployDemoRuntime.PaletteChoice choice,
        int xCm,
        int yCm,
        out int count)
    {
        int index = store.TemplateIndexOf(choice.TemplateId);
        if (index < 0) throw new InvalidOperationException($"未知结构模板 \"{choice.TemplateId}\"。");
        var template = session.Config.Structures.Templates[index];
        var (x0, y0, x1, y1) = _demo.FootprintEnds(choice, xCm, yCm);
        var footprint = CrowdStructureFootprint.Make(
            template, Fix64.FromInt(x0), Fix64.FromInt(y0), Fix64.FromInt(choice.SizeCm), Fix64.FromInt(x1), Fix64.FromInt(y1));
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
                : (flag & CrowdStructurePlacement.FlagEndpoint) != 0
                    ? _placeEndpoint
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
        _placeEndpoint = CrowdPresentationConfig.Rgba(deploy.PlaceEndpoint, "deploy.placeEndpoint");
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
    /// tile 轮廓按导航格逐格细分:路线渲染器只对折点采样地表高度,1km tile 的四角直边
    /// 在丘陵地形会整段埋进地形(视野内又见不到角点),细分后贴地才可见。</summary>
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
}
