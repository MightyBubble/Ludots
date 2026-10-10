using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace CrowdSimulationS4DeployMod.Runtime;

/// <summary>
/// S4 部署演示交互与覆盖层(F03-b):V 巡回分层视图(流场/可走/NavMesh+HPA 线框),
/// B 建造模式(左键放置,足迹红绿预览)、X 拆除光标处结构、1/2/3 切模板;重烘发生后
/// 脏 tile 轮廓短暂闪烁;HUD 上屏 tick/单位状态分布/最近重烘报告/回放结论/按键。
/// 数据全部读内核现成状态(会话/结构仓/重烘报告/回放状态),不新增统计;
/// 世界落点走 ScreenRayProvider + 高度图射线,输入只入队(呈现线程不直接改会话)。
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

    // 指针域走后端原值(放置键/位置);键盘键走动作层(assets/Input/default_input.json
    // 声明,Default_Gameplay 上下文绑定),演示不解释设备路径。
    private const string MouseLeft = "<Mouse>/LeftButton";
    private const string ActionCycleView = "CrowdSimulation.S4.CycleView";
    private const string ActionToggleBuild = "CrowdSimulation.S4.ToggleBuild";
    private const string ActionRemoveStructure = "CrowdSimulation.S4.RemoveStructure";
    private const string ActionTemplate1 = "CrowdSimulation.S4.Template1";
    private const string ActionTemplate2 = "CrowdSimulation.S4.Template2";
    private const string ActionTemplate3 = "CrowdSimulation.S4.Template3";
    // HUD 面板矩形(窗口像素,绘制与世界点选排除共用同一份——点在面板上不算世界点选)
    private const int HudPanelX = 16, HudPanelY = 16, HudPanelW = 1568, HudPanelH = 148;
    private const long FlashMs = 2500; // 脏 tile 闪烁时长(呈现口径,与仿真无关)

    private readonly S4DeployDemoRuntime _demo;
    private readonly RouteVisualBuffer _routeVisuals;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly IInputBackend _input;
    private readonly PlayerInputHandler _actions;
    private readonly IScreenRayProvider _rays;
    private readonly Func<IContinuousHeightmap?> _heightmapSource;
    private readonly Func<CrowdSimulationRuntime?> _runtimeSource;
    private readonly Func<CrowdSimSession?> _sessionSource;

    private bool _prevLeft, _prevView, _prevBuild, _prevRemove, _prevT1, _prevT2, _prevT3;
    private int _wireNavId = -1;
    private int _wireReportTick = -1;
    private readonly List<(Vector2[] Pts, float Thick, Vector4 Color)> _wireLines = new();
    private readonly List<(Vector2 Pos, RouteVisualMarkerShape Shape, float Radius, Vector4 Color)> _wireMarkers = new();
    private int _flashReportTick = -1;
    private long _flashStartMs;

    public S4DeployDemoPresentationSystem(
        S4DeployDemoRuntime demo,
        RouteVisualBuffer routeVisuals,
        ScreenOverlayBuffer overlay,
        IInputBackend input,
        PlayerInputHandler actions,
        IScreenRayProvider rays,
        Func<IContinuousHeightmap?> heightmapSource,
        Func<CrowdSimulationRuntime?> runtimeSource,
        Func<CrowdSimSession?> sessionSource)
    {
        _demo = demo;
        _routeVisuals = routeVisuals;
        _overlay = overlay;
        _input = input;
        _actions = actions;
        _rays = rays;
        _heightmapSource = heightmapSource;
        _runtimeSource = runtimeSource;
        _sessionSource = sessionSource;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        var session = _sessionSource();
        var runtime = _runtimeSource();

        // ── 输入边沿 → 演示队列(呈现线程只入队,仿真 tick 消费) ─────────────────
        bool left = _input.GetButton(MouseLeft);
        bool keyView = _actions.IsDown(ActionCycleView);
        bool keyBuild = _actions.IsDown(ActionToggleBuild);
        bool keyRemove = _actions.IsDown(ActionRemoveStructure);
        bool t1 = _actions.IsDown(ActionTemplate1), t2 = _actions.IsDown(ActionTemplate2), t3 = _actions.IsDown(ActionTemplate3);
        var mouse = _input.GetMousePosition();
        var ground = PickGround(mouse);

        if (keyView && !_prevView) _demo.EnqueueCycleView();
        if (keyBuild && !_prevBuild) _demo.EnqueueToggleBuild();
        if (t1 && !_prevT1) _demo.EnqueueSelectTemplate(0);
        if (t2 && !_prevT2) _demo.EnqueueSelectTemplate(1);
        if (t3 && !_prevT3) _demo.EnqueueSelectTemplate(2);
        if (left && !_prevLeft && _demo.BuildMode && ground is { } g)
        {
            _demo.EnqueuePlace((int)Math.Floor(g.X), (int)Math.Floor(g.Y));
        }

        if (keyRemove && !_prevRemove && ground is { } r)
        {
            _demo.EnqueueRemove((int)Math.Floor(r.X), (int)Math.Floor(r.Y));
        }

        _prevLeft = left; _prevView = keyView; _prevBuild = keyBuild; _prevRemove = keyRemove;
        _prevT1 = t1; _prevT2 = t2; _prevT3 = t3;

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

        // NavMesh + HPA 线框(视图模式 2;重烘换 tile 后连带着重建缓存)
        if (_demo.ViewMode == 2)
        {
            EnsureWireframe(session);
            foreach (var (pts, thick, color) in _wireLines) _routeVisuals.AddPolylineShared(WireframeId, pts, thick, color);
            foreach (var (pos, shape, radius, color) in _wireMarkers) _routeVisuals.AddMarker(WireframeId, pos, shape, radius, 1f, color);
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

    /// <summary>屏幕坐标 → 地面世界厘米(HUD 面板矩形内的点击不算;高度图服务地图加载后才注册,逐帧取)。</summary>
    private (double X, double Y)? PickGround(Vector2 mouse)
    {
        if (mouse.X >= HudPanelX && mouse.X <= HudPanelX + HudPanelW &&
            mouse.Y >= HudPanelY && mouse.Y <= HudPanelY + HudPanelH)
        {
            return null;
        }

        var heightmap = _heightmapSource();
        if (heightmap == null) return null;
        var ray = _rays.GetRay(mouse);
        return heightmap.TryRaycastGround(in ray, out VisualGroundHit hit) ? (hit.WorldXCm, hit.WorldYCm) : null;
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
        if (tpl.RoadLengthCm > 0)
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

    // NavMesh 多边形线框 + cluster 网格 + HPA 入口 + 跳跃链接(S3 演示同款,按(导航,重烘序)缓存)
    private void EnsureWireframe(CrowdSimSession session)
    {
        var group = S4DeployDemoViewProjector.DominantGroup(session);
        int navId = group?.NavId ?? MinNavId(session);
        int reportTick = session.LastRebakeReport?.ReportTick ?? -1;
        if (_wireNavId == navId && _wireReportTick == reportTick) return;
        if (navId < 0)
        {
            throw new InvalidOperationException("S4 NavMesh 视图没有导航上下文。");
        }

        var nav = session.ResolveNavContext(navId);
        _wireNavId = navId;
        _wireReportTick = reportTick;
        _wireLines.Clear();
        _wireMarkers.Clear();

        int n = session.Config.NavCellCount;
        float csM = session.Config.NavCellSizeCm / 100f;
        int t = session.Config.Hpa.ClusterSize, c = n / t;
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

    private static int MinNavId(CrowdSimSession session)
    {
        int best = -1;
        foreach (var id in session.Navs.Keys) best = best < 0 || id < best ? id : best;
        return best;
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

        string templateLine = _demo.BuildMode
            ? $"建造模式 · {_demo.Template.Label}{(preview == null ? "" : preview.Value.Shown ? (preview.Value.Valid ? "(此处可放)" : "(此处不可放)") : "(光标不在地图上)")}"
            : "观察模式";
        string keys =
            $"V 视图 {S4DeployDemoRuntime.ViewModeLabels[_demo.ViewMode]} · B 建造/观察 · X 拆除光标处 · 1/2/3 模板[{_demo.TemplateIndex + 1}] · 建造模式下左键放置 · {templateLine}";
        _overlay.AddRect(HudPanelX, HudPanelY, HudPanelW, HudPanelH, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(1f, 0.85f, 0.2f, 1f));
        _overlay.AddText(32, 24, "CrowdSimulation S4 · 部署演示(种子 1337,脚本行军 + 交互建造)", 22, new Vector4(1f, 0.92f, 0.35f, 1f));
        _overlay.AddText(32, 54, status, 18, new Vector4(1f, 1f, 1f, 1f));
        _overlay.AddText(32, 78, $"最近重烘:{rebake}", 18, new Vector4(1f, 1f, 1f, 1f));
        _overlay.AddText(32, 102, replay, 18, _demo.InteractiveMode ? new Vector4(1f, 0.55f, 0.4f, 1f) : new Vector4(0.8f, 1f, 0.8f, 1f));
        _overlay.AddText(32, 126, keys, 18, new Vector4(0.75f, 0.9f, 1f, 1f));
    }
}
