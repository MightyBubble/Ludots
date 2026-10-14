using System.Numerics;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Fields;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 调试视图只读绘制。热力走可走区域通道的直接 RGBA：渲染端不接收 Heat 种类。
/// </summary>
internal sealed class CrowdDebugViewFrame
{
    private readonly CrowdSimulationDebugViewConfig _config;
    private readonly CrowdFlowFieldVisualSource _flowSource = new();
    private readonly CrowdFlowFieldVisualProjector _flow;
    private GameEngine? _engine;

    private bool _viewValid;
    private float _minX, _minY, _maxX, _maxY;
    private int _x0, _y0, _x1, _y1;
    private int _cacheNav = int.MinValue;
    private int _cacheVersion = int.MinValue;
    private int _cacheFlags = -1;
    private int _poly0, _polyN, _hpa0, _hpaN, _jump0, _jumpN, _nodeN;
    private int _diffCount;
    private int _ghostStat;
    private FlowField? _firstFlow;
    private CrowdDebugSegment[]? _world;
    private CrowdDebugSegment[]? _scratch;
    private CrowdDebugSegment[]? _clip;
    private int[]? _nodes;
    private GlobalFieldVisualCell[] _fieldCells = Array.Empty<GlobalFieldVisualCell>();
    private readonly IntRect[] _dirty = new IntRect[1];
    private byte[] _stamp = Array.Empty<byte>();
    private byte _stampGen;
    private byte[] _corridor = Array.Empty<byte>();
    private int[] _unitIds = Array.Empty<int>();
    private int[] _bins = Array.Empty<int>();
    private int[] _beliefCells = Array.Empty<int>();
    private byte[] _beliefGhost = Array.Empty<byte>();
    private CrowdLeader?[]? _seen;
    private readonly CrowdDebugPanels _panels;
    private string _inspectText = "";
    private string _beliefText = "";
    private int _shownLayer = int.MinValue;
    private int _shownRadius = int.MinValue;
    private int _shownColor = int.MinValue;
    private int _shownGroup = int.MinValue;
    private int _shownDiff = int.MinValue;
    private int _shownGhost = int.MinValue;
    private int _shownTerrain = int.MinValue;

    internal bool Funnel;
    internal bool Corridor;
    internal bool HpaGraph;
    internal bool NavPolygons;
    internal bool TileGrid;
    internal bool Flow;
    internal bool Density;
    internal bool Blocked;
    internal bool JumpLinks;
    internal bool Belief;
    internal int UnitColorMode;
    internal int LayerIndex;
    internal int RadiusIndex;
    internal int BeliefGroup;
    internal int ControllingPlayer = -1;
    internal bool NavPanel;
    internal bool TelemetryPanel;
    internal bool ViewportLocked;
    internal float CellPx;

    public CrowdDebugViewFrame(CrowdSimulationDebugViewConfig config)
    {
        _config = config;
        Funnel = config.Overlays.Funnel;
        Corridor = config.Overlays.Corridor;
        HpaGraph = config.Overlays.HpaGraph;
        NavPolygons = config.Overlays.NavPolygons;
        TileGrid = config.Overlays.TileGrid;
        Flow = config.Overlays.Flow;
        Density = config.Overlays.Density;
        Blocked = config.Overlays.Blocked;
        JumpLinks = config.Overlays.JumpLinks;
        Belief = config.Overlays.Belief;
        UnitColorMode = config.UnitColorMode;
        LayerIndex = config.Inspect.LayerIndex;
        RadiusIndex = config.Inspect.RadiusIndex;
        BeliefGroup = config.BeliefGroup;
        NavPanel = config.Panels.Nav;
        TelemetryPanel = config.Panels.Telemetry;
        _panels = new CrowdDebugPanels(config);
        _flow = new CrowdFlowFieldVisualProjector(_flowSource)
        {
            LimitToRect = true,
            LayerKeyId = config.Layers.Flow,
        };
    }

    internal bool Visible =>
        Funnel || Corridor || HpaGraph || NavPolygons || TileGrid || Flow || Density || Blocked || JumpLinks || Belief
        || TerrainTypes
        || UnitColorMode != CrowdSimulationDebugViewConfig.ColorOff;

    /// <summary>开关在输入系统。帧只读叠层，用来占贴花槽和写检视行。</summary>
    internal bool TerrainTypes =>
        _engine != null
        && _engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out CrowdSimulationRuntime? runtime)
        && runtime?.TerrainTypeOverlay is { Enabled: true };

    /// <summary>宿主只有一个流场或可走贴花槽。调试场层打开时占住它，不再叠演示流场。地形类型叠层占用同一槽。</summary>
    internal bool OwnsDecal =>
        Corridor || Blocked || Density || Belief || Flow || NavPolygons || HpaGraph || JumpLinks || TerrainTypes;

    internal static bool ArrowsAllowed(float cellPx, int minPx) => cellPx >= minPx;

    /// <summary>操控方是玩家表序，不是基地数组下标。玩家号 0 和表外号没有表序。</summary>
    internal static int ControllingIndex(int[] indexByPlayerId, int playerId)
    {
        if (playerId < 1 || (uint)playerId >= (uint)indexByPlayerId.Length) return -1;
        return indexByPlayerId[playerId];
    }

    internal void BindEngine(GameEngine engine) => _engine = engine;

    internal void SetViewport(float minX, float minY, float maxX, float maxY, float cellPx)
    {
        _minX = minX;
        _minY = minY;
        _maxX = maxX;
        _maxY = maxY;
        CellPx = cellPx;
        _viewValid = maxX > minX && maxY > minY;
        ViewportLocked = true;
    }

    internal void ApplyInput(FrozenInputActionReader actions, GameEngine engine)
    {
        var bindings = _config.Actions;
        if (actions.PressedThisTick(bindings.ToggleFunnel)) Funnel = !Funnel;
        if (actions.PressedThisTick(bindings.ToggleCorridor)) Corridor = !Corridor;
        if (actions.PressedThisTick(bindings.ToggleHpaGraph)) HpaGraph = !HpaGraph;
        if (actions.PressedThisTick(bindings.ToggleNavPolygons)) NavPolygons = !NavPolygons;
        if (actions.PressedThisTick(bindings.ToggleTileGrid)) TileGrid = !TileGrid;
        if (actions.PressedThisTick(bindings.ToggleFlow)) Flow = !Flow;
        if (actions.PressedThisTick(bindings.ToggleDensity)) Density = !Density;
        if (actions.PressedThisTick(bindings.ToggleObstacles)) Blocked = !Blocked;
        if (actions.PressedThisTick(bindings.ToggleJumpLinks)) JumpLinks = !JumpLinks;
        if (actions.PressedThisTick(bindings.ToggleBelief)) Belief = !Belief;
        if (actions.PressedThisTick(bindings.ToggleNavPanel)) NavPanel = !NavPanel;
        if (actions.PressedThisTick(bindings.ToggleTelemetryPanel)) TelemetryPanel = !TelemetryPanel;
        if (actions.PressedThisTick(bindings.ProbePathFault)
            && engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out CrowdSimulationRuntime? faultRuntime)
            && faultRuntime != null)
        {
            faultRuntime.ProbePathFault();
        }

        if (actions.PressedThisTick(bindings.CycleUnitColor))
            UnitColorMode = (UnitColorMode + 1) % (CrowdSimulationDebugViewConfig.ColorPriority + 1);
        if (!engine.TryGetService(CoreServiceKeys.CrowdSimulationSession, out CrowdSimSession? session) || session == null)
            return;
        if (actions.PressedThisTick(bindings.CycleBeliefGroup))
        {
            int groups = session.Fog?.VisionGroupCount ?? 0;
            if (groups > 0) BeliefGroup = (BeliefGroup + 1) % groups;
        }

        if (actions.PressedThisTick(bindings.CycleInspectLayer))
        {
            int layers = session.Config.AgentTypes.Count;
            if (layers > 0) LayerIndex = (LayerIndex + 1) % layers;
        }

        if (actions.PressedThisTick(bindings.CycleInspectRadius))
        {
            int radii = session.Config.RadiusClasses.Count;
            if (radii > 0) RadiusIndex = (RadiusIndex + 1) % radii;
        }
    }

    public void Draw(CrowdSimSession session, ScreenOverlayBuffer overlay)
    {
        DrawReadouts(session, overlay);
        if (!Visible) return;
        Prepare(session);
        if (!_viewValid) return;
        if (_engine == null || !TryView(out var projector, out _, out var height, out _)) return;
        int budget = ScreenOverlayBuffer.MaxItems - _config.OverlayReserve;
        int used = overlay.Count;
        int cell = session.Config.NavCellSizeCm;
        int n = session.Config.NavCellsPerSide;
        int tile = session.Config.Hpa.NavCellsPerTile;
        if (NavPolygons || HpaGraph || JumpLinks)
            EmitCached(session, overlay, projector, height, ref used, budget);
        if (TileGrid)
            EmitGenerated(CrowdDebugOverlayGeometry.AppendTileGrid(n, tile, cell, Scratch(), 0), overlay, projector, height, ref used, budget, _config.Colors.Tile.ToVector4(), 1f);
        if (TileGrid && TryInspectNav(session, out var nav))
        {
            float pulse = Pulse();
            int tilesPerSide = n / tile;
            int lines = CrowdDebugOverlayGeometry.AppendDirtyOutlines(nav.ChangedTiles, tilesPerSide, tile, cell, Scratch(), 0);
            EmitGenerated(lines, overlay, projector, height, ref used, budget, _config.Colors.TileDirty.ToVector4(), pulse);
        }

        if (Funnel)
            EmitFunnel(session, overlay, projector, height, ref used, budget);
        if (HpaGraph)
            EmitNodes(session, overlay, projector, height, ref used, budget);
        if (UnitColorMode != CrowdSimulationDebugViewConfig.ColorOff)
            EmitUnits(session, overlay, projector, height, ref used, budget);
        EmitScreenFields(session, overlay, projector, height, ref used, budget);
        EmitPanel(session, overlay, ref used, budget);
    }

    public void Project(GlobalFieldVisualBuffer buffer, CrowdSimSession? session)
    {
        if (!Visible || session == null) return;
        Prepare(session);
        if (!_viewValid) return;
        int n = session.Config.NavCellsPerSide;
        int cell = session.Config.NavCellSizeCm;
        int spanX = _x1 - _x0, spanY = _y1 - _y0;
        if (spanX <= 0 || spanY <= 0) return;
        int step = CrowdDebugOverlayGeometry.StepFor(spanX, spanY, _config.MaxFieldCells);
        int bw = (spanX + step - 1) / step;
        int bh = (spanY + step - 1) / step;
        bool paper = NavPolygons || HpaGraph || JumpLinks;
        if (paper)
            FillRect(buffer, _config.Layers.Paper, cell, step, bw, bh, _config.Colors.Paper.ToVector4());
        if (Corridor)
            FillCorridor(buffer, session, cell, step, bw, bh);
        if (Blocked && session.Structures != null)
            FillBlocked(buffer, session.Structures.Blocked, n, cell, step, bw, bh);
        if (Density)
            FillDensity(buffer, session, cell, step, bw, bh);
        if (Belief)
            FillBelief(buffer, cell, step, bw, bh);
        if (Flow && _firstFlow != null && ArrowsAllowed(CellPx, _config.ArrowMinScreenPx))
        {
            _flowSource.Flow = _firstFlow;
            _flowSource.NavCellsPerSide = n;
            _flowSource.NavCellSizeCm = cell;
            _flow.LayerKeyId = _config.Layers.Flow;
            _flow.MinCellX = _x0;
            _flow.MinCellY = _y0;
            _flow.MaxCellX = _x1;
            _flow.MaxCellY = _y1;
            _flow.CellStep = step;
            _flow.MaxCells = _config.MaxFieldCells;
            _flow.Project(buffer);
        }
    }

    private void Prepare(CrowdSimSession session)
    {
        if (_engine != null)
            ResolvePlayer(session);
        if (!ViewportLocked)
            Measure(session.Config.NavCellSizeCm);
        if (!_viewValid) return;
        _navSide = session.Config.NavCellsPerSide;
        SyncCells(session);
        _firstFlow = null;
        if (Corridor || Flow)
            _firstFlow = FirstFlow(session);
        int flags = (NavPolygons ? 1 : 0) | (HpaGraph ? 2 : 0) | (JumpLinks ? 4 : 0);
        if (flags != 0 && TryInspectNav(session, out var nav))
            EnsureNavCache(nav, session.Config.NavCellsPerSide, session.Config.Hpa.NavCellsPerTile, session.Config.NavCellSizeCm, flags);
        else if (flags == 0)
            _cacheFlags = 0;
        ReadBelief(session);
    }

    private void ResolvePlayer(CrowdSimSession session)
    {
        ControllingPlayer = -1;
        if (_engine == null) return;
        if (!_engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out CrowdSimulationRuntime? runtime) || runtime == null)
            return;
        if (!runtime.TryLocalSeatPlayerId(out int playerId)) return;
        ControllingPlayer = ControllingIndex(session.Config.PairRules.IndexByPlayerId, playerId);
    }

    private void DrawReadouts(CrowdSimSession session, ScreenOverlayBuffer overlay)
    {
        int width = 0, height = 0;
        if (TryView(out _, out _, out _, out var view))
        {
            width = (int)view.Resolution.X;
            height = (int)view.Resolution.Y;
        }

        PresentationTimingDiagnostics? timing = null;
        CrowdSimulationRuntime? runtime = null;
        if (_engine != null)
        {
            _engine.TryGetService(CoreServiceKeys.PresentationTimingDiagnostics, out timing);
            _engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out runtime);
        }
        _panels.Draw(session, runtime, timing, width, height, NavPanel, TelemetryPanel, overlay);
    }

    private void Measure(int cellCm)
    {
        _viewValid = false;
        CellPx = 0f;
        if (_engine == null || !TryView(out var projector, out var rays, out var height, out var view)) return;
        float width = view.Resolution.X, heightPx = view.Resolution.Y;
        if (width <= 1f || heightPx <= 1f) return;
        if (!TryGround(rays, height, width * 0.5f, heightPx * 0.5f, out float cx, out float cy, out float cz)) return;
        var origin = projector.WorldToScreen(ToWorld(cx, cy, cz));
        var stepped = projector.WorldToScreen(ToWorld(cx + cellCm, cy, cz));
        if (!float.IsNaN(origin.X) && !float.IsNaN(stepped.X))
        {
            float dx = stepped.X - origin.X, dy = stepped.Y - origin.Y;
            CellPx = MathF.Sqrt(dx * dx + dy * dy);
        }

        ReadOnlySpan<(float X, float Y)> corners = stackalloc (float, float)[] { (0f, 0f), (width - 1f, 0f), (width - 1f, heightPx - 1f), (0f, heightPx - 1f) };
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < corners.Length; i++)
        {
            if (!TryGround(rays, height, corners[i].X, corners[i].Y, out float x, out float y, out _)) return;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        _minX = minX;
        _minY = minY;
        _maxX = maxX;
        _maxY = maxY;
        _viewValid = maxX > minX && maxY > minY;
    }

    private bool TryView(out IScreenProjector projector, out IScreenRayProvider rays, out IContinuousHeightmap height, out IViewController view)
    {
        projector = null!;
        rays = null!;
        height = null!;
        view = null!;
        if (_engine == null) return false;
        return _engine.TryGetService(CoreServiceKeys.ScreenProjector, out projector!) && projector != null
            && _engine.TryGetService(CoreServiceKeys.ScreenRayProvider, out rays!) && rays != null
            && _engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out height!) && height != null
            && _engine.TryGetService(CoreServiceKeys.ViewController, out view!) && view != null;
    }

    private static bool TryGround(IScreenRayProvider rays, IContinuousHeightmap height, float x, float y, out float worldX, out float worldY, out float heightCm)
    {
        var ray = rays.GetRay(new Vector2(x, y));
        if (!height.TryRaycastGround(in ray, out var hit))
        {
            worldX = worldY = heightCm = 0f;
            return false;
        }

        worldX = hit.WorldXCm;
        worldY = hit.WorldYCm;
        heightCm = hit.HeightCm;
        return true;
    }

    private void SyncCells(CrowdSimSession session)
    {
        int n = session.Config.NavCellsPerSide;
        float cell = session.Config.NavCellSizeCm;
        _x0 = Math.Clamp((int)MathF.Floor(_minX / cell), 0, n);
        _y0 = Math.Clamp((int)MathF.Floor(_minY / cell), 0, n);
        _x1 = Math.Clamp((int)MathF.Ceiling(_maxX / cell), 0, n);
        _y1 = Math.Clamp((int)MathF.Ceiling(_maxY / cell), 0, n);
        if (_x1 < _x0) _x1 = _x0;
        if (_y1 < _y0) _y1 = _y0;
    }

    private FlowField? FirstFlow(CrowdSimSession session)
    {
        int tiles = session.Config.NavCellsPerSide / session.Config.Hpa.NavCellsPerTile;
        int mask = tiles * tiles;
        if (_corridor.Length < mask) _corridor = new byte[mask];
        var groups = session.Groups.Groups;
        FlowField? first = null;
        Array.Clear(_corridor);
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group == null || group.Player != ControllingPlayer || group.LayerIdx != LayerIndex || group.RIdx != RadiusIndex || group.Flow == null)
                continue;
            if (first == null) first = group.Flow;
            var bits = group.Flow.Mask;
            if (bits == null) continue;
            int len = bits.Length < _corridor.Length ? bits.Length : _corridor.Length;
            for (int t = 0; t < len; t++)
            {
                if (bits[t] != 0) _corridor[t] = 1;
            }
        }

        return first;
    }

    private void ReadBelief(CrowdSimSession session)
    {
        _diffCount = 0;
        _ghostStat = 0;
        if (!Belief) return;
        if (session.Fog != null && (uint)BeliefGroup < (uint)session.Fog.GhostCount.Length)
            _ghostStat = session.Fog.GhostCount[BeliefGroup];
        if (session.Beliefs == null || _x1 <= _x0 || _y1 <= _y0) return;
        int spanX = _x1 - _x0, spanY = _y1 - _y0;
        int step = CrowdDebugOverlayGeometry.StepFor(spanX, spanY, _config.MaxFieldCells);
        int cap = ((spanX + step - 1) / step) * ((spanY + step - 1) / step);
        if (_beliefCells.Length < cap)
        {
            _beliefCells = new int[cap];
            _beliefGhost = new byte[cap];
        }

        int slot = 0;
        var state = session.Belief;
        if (state != null && (uint)BeliefGroup < (uint)state.Slot.Length)
            slot = state.Slot[BeliefGroup];
        _diffCount = session.Beliefs.CopyDivergentCells(slot, _x0, _y0, _x1, _y1, step, _beliefCells, _beliefGhost);
    }

    private void EnsureNavCache(NavContext nav, int n, int navCellsPerTile, int cellCm, int flags)
    {
        if (_cacheNav == nav.Id && _cacheVersion == nav.Version && _cacheFlags == flags) return;
        _world ??= new CrowdDebugSegment[_config.MaxWorldSegments];
        if (_world.Length < _config.MaxWorldSegments)
            _world = new CrowdDebugSegment[_config.MaxWorldSegments];
        int w = 0;
        _poly0 = _polyN = _hpa0 = _hpaN = _jump0 = _jumpN = _nodeN = 0;
        if ((flags & 1) != 0)
        {
            _poly0 = 0;
            _polyN = CrowdDebugOverlayGeometry.AppendPolygons(nav, n, navCellsPerTile, cellCm, _world, 0);
            w = _polyN;
        }

        if ((flags & 2) != 0)
        {
            _hpa0 = w;
            _hpaN = CrowdDebugOverlayGeometry.AppendHpaEdges(nav.Hpa, n, cellCm, _world, w);
            w += _hpaN;
            _nodes ??= new int[_config.MaxWorldSegments];
            _nodeN = CrowdDebugOverlayGeometry.CopyHpaNodes(nav.Hpa, _nodes);
        }

        if ((flags & 4) != 0)
        {
            _jump0 = w;
            _jumpN = CrowdDebugOverlayGeometry.AppendJumps(nav.Links, n, cellCm, _world, w);
        }

        _cacheNav = nav.Id;
        _cacheVersion = nav.Version;
        _cacheFlags = flags;
    }

    private bool TryInspectNav(CrowdSimSession session, out NavContext nav)
        => session.NavByLayerRadius.TryGetValue((LayerIndex, RadiusIndex), out nav!);

    private void EmitCached(CrowdSimSession session, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget)
    {
        if (_world == null || _cacheFlags <= 0) return;
        if (NavPolygons) EmitRange(_poly0, _polyN, overlay, projector, height, ref used, budget, 1f);
        if (HpaGraph) EmitRange(_hpa0, _hpaN, overlay, projector, height, ref used, budget, 1f);
        if (JumpLinks) EmitRange(_jump0, _jumpN, overlay, projector, height, ref used, budget, 1f);
    }

    private void EmitRange(int offset, int count, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, float alphaScale)
    {
        if (count <= 0 || _world == null || used >= budget) return;
        int room = budget - used;
        _clip ??= new CrowdDebugSegment[_config.MaxScreenSegments];
        int take = count < room ? count : room;
        if (take > _clip.Length) take = _clip.Length;
        int clipped = CrowdDebugOverlayGeometry.ClipSegments(_world.AsSpan(offset, count), _minX, _minY, _maxX, _maxY, _clip.AsSpan(0, take));
        for (int i = 0; i < clipped && used < budget; i++)
            EmitSegment(_clip[i], overlay, projector, height, ref used, budget, KindColor(_clip[i].Kind, alphaScale));
    }

    private void EmitGenerated(int count, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, Vector4 color, float alphaScale)
        => EmitScratch(count, overlay, projector, height, ref used, budget, perKind: false, color, alphaScale);

    private void EmitScratch(int count, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, bool perKind, Vector4 color, float alphaScale)
    {
        if (count <= 0 || used >= budget || _scratch == null) return;
        int room = budget - used;
        _clip ??= new CrowdDebugSegment[_config.MaxScreenSegments];
        int take = count < room ? count : room;
        if (take > _clip.Length) take = _clip.Length;
        int clipped = CrowdDebugOverlayGeometry.ClipSegments(_scratch.AsSpan(0, count), _minX, _minY, _maxX, _maxY, _clip.AsSpan(0, take));
        if (!perKind && alphaScale != 1f) color.W *= alphaScale;
        for (int i = 0; i < clipped && used < budget; i++)
        {
            var ink = perKind ? KindColor(_clip[i].Kind, alphaScale) : color;
            EmitSegment(_clip[i], overlay, projector, height, ref used, budget, ink);
        }
    }

    private void EmitFunnel(CrowdSimSession session, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget)
    {
        var scratch = Scratch();
        var seen = SeenLeaders();
        int seenCount = 0;
        int count = CrowdDebugOverlayGeometry.AppendFunnel(session.Groups.Groups, ControllingPlayer, seen, ref seenCount, scratch, 0);
        for (int i = 0; i < seenCount; i++) seen[i] = null;
        EmitScratch(count, overlay, projector, height, ref used, budget, perKind: true, default, 1f);
    }

    private void EmitNodes(CrowdSimSession session, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget)
    {
        if (_nodes == null || _nodeN == 0) return;
        int n = session.Config.NavCellsPerSide;
        int cell = session.Config.NavCellSizeCm;
        int mark = _config.UnitMarkPx;
        var color = _config.Colors.Plan.ToVector4();
        for (int i = 0; i < _nodeN && used < budget; i++)
        {
            int index = _nodes[i];
            int n2 = n * n;
            int c = index >= n2 ? index - n2 : index;
            int x = c % n, y = c / n;
            if (x < _x0 || x >= _x1 || y < _y0 || y >= _y1) continue;
            CrowdDebugOverlayGeometry.CellCenter(index, n, cell, out float wx, out float wy);
            if (!TryScreen(wx, wy, projector, height, out int sx, out int sy)) continue;
            if (overlay.AddRect(sx - mark / 2, sy - mark / 2, mark, mark, color, color))
                used = overlay.Count;
        }
    }

    private void EmitUnits(CrowdSimSession session, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget)
    {
        var movement = session.Movement;
        if (movement == null) return;
        int count = CopyUnits(session);
        int mark = _config.UnitMarkPx;
        int radii = session.Config.RadiusClasses.Count;
        string path = _config.SourcePath;
        for (int i = 0; i < count && used < budget; i++)
        {
            int unit = _unitIds[i];
            var pos = movement.Positions[unit];
            if (!TryScreen(pos.X.ToFloat(), pos.Y.ToFloat(), projector, height, out int sx, out int sy)) continue;
            Vector4 color = UnitColorMode switch
            {
                CrowdSimulationDebugViewConfig.ColorTeam => CrowdDebugViewColors.At(_config.Colors.Teams, movement.PlayerIdx[unit], path, "colors.teams"),
                CrowdSimulationDebugViewConfig.ColorPriority => CrowdDebugViewColors.At(_config.Colors.Priorities, movement.PushPriorities[unit].ToInt(), path, "colors.priorities"),
                CrowdSimulationDebugViewConfig.ColorTemplate => TemplateColor(session, movement.States[unit].GroupId, radii, path),
                _ => default,
            };
            if (UnitColorMode == CrowdSimulationDebugViewConfig.ColorOff) continue;
            var owner = session.Units.EntityAt(unit);
            if (session.World.Has<PresentationHidden>(owner) && session.World.Get<PresentationHidden>(owner).Hidden)
            {
                continue;
            }

            if (overlay.AddRect(sx - mark / 2, sy - mark / 2, mark, mark, color, color))
                used = overlay.Count;
        }
    }

    private Vector4 TemplateColor(CrowdSimSession session, int groupId, int radii, string path)
    {
        if (!session.Groups.TryGet(groupId, out var group))
            throw new InvalidOperationException($"{path}: colors.templates 缺少组 {groupId}。");
        int index = CrowdDebugOverlayGeometry.TemplateIndex(group.LayerIdx, group.RIdx, radii);
        return CrowdDebugViewColors.At(_config.Colors.Templates, index, path, "colors.templates");
    }

    private void EmitPanel(CrowdSimSession session, ScreenOverlayBuffer overlay, ref int used, int budget)
    {
        bool nav = Corridor || Flow || NavPolygons || HpaGraph || JumpLinks || TileGrid || Density || Blocked || TerrainTypes;
        if (!nav && !Belief && UnitColorMode == CrowdSimulationDebugViewConfig.ColorOff) return;
        if (used >= budget) return;
        var color = _config.Colors.PanelText.ToVector4();
        int font = _config.Panel.FontSize;
        int y = _config.Panel.OriginYPx;
        if (nav || UnitColorMode != CrowdSimulationDebugViewConfig.ColorOff)
        {
            int terrainFlag = TerrainTypes ? 1 : 0;
            if (_shownLayer != LayerIndex || _shownRadius != RadiusIndex || _shownColor != UnitColorMode || _shownTerrain != terrainFlag)
            {
                _shownLayer = LayerIndex;
                _shownRadius = RadiusIndex;
                _shownColor = UnitColorMode;
                _shownTerrain = terrainFlag;
                _inspectText = UnitColorMode == CrowdSimulationDebugViewConfig.ColorOff
                    ? $"检视 移动 {LayerIndex} 半径 {RadiusIndex}"
                    : $"检视 移动 {LayerIndex} 半径 {RadiusIndex} 着色 {ColorLabel(UnitColorMode)}";
                if (TerrainTypes) _inspectText += " 地形类型";
            }

            if (overlay.AddText(_config.Panel.OriginXPx, y, _inspectText, font, color))
                used = overlay.Count;
            y += font + 4;
        }

        if (!Belief || used >= budget) return;
        if (_shownGroup != BeliefGroup || _shownDiff != _diffCount || _shownGhost != _ghostStat)
        {
            _shownGroup = BeliefGroup;
            _shownDiff = _diffCount;
            _shownGhost = _ghostStat;
            _beliefText = $"视野组 {BeliefGroup} 分歧 {_diffCount} 残影 {_ghostStat}";
        }

        if (overlay.AddText(_config.Panel.OriginXPx, y, _beliefText, font, color))
            used = overlay.Count;
    }

    private static string ColorLabel(int mode) => mode switch
    {
        CrowdSimulationDebugViewConfig.ColorTeam => "队伍",
        CrowdSimulationDebugViewConfig.ColorTemplate => "模板",
        CrowdSimulationDebugViewConfig.ColorPriority => "优先级",
        _ => "关",
    };

    private void EmitSegment(CrowdDebugSegment segment, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, Vector4 color)
    {
        if (used >= budget) return;
        if (!TryScreen(segment.X0, segment.Y0, projector, height, out int x0, out int y0)) return;
        if (!TryScreen(segment.X1, segment.Y1, projector, height, out int x1, out int y1)) return;
        if (overlay.AddLine(x0, y0, x1, y1, _config.LineThicknessPx, color))
            used = overlay.Count;
    }

    /// <summary>贴花和地形混合会把小色块洗淡，屏幕矩形用配置颜色盖住同一格。</summary>
    private void EmitScreenFields(CrowdSimSession session, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget)
    {
        if (used >= budget) return;
        if (!Corridor && !Blocked && !Density && !Belief && !Flow) return;
        int n = session.Config.NavCellsPerSide;
        int cellCm = session.Config.NavCellSizeCm;
        int spanX = _x1 - _x0, spanY = _y1 - _y0;
        if (spanX <= 0 || spanY <= 0) return;
        int step = CrowdDebugOverlayGeometry.StepFor(spanX, spanY, _config.MaxFieldCells);
        int mark = (int)MathF.Max(1f, CellPx * step);
        if (Corridor)
            EmitCorridorMarks(session, overlay, projector, height, ref used, budget, cellCm, step, mark);
        if (Blocked && session.Structures != null)
            EmitBlockedMarks(session.Structures.Blocked, n, overlay, projector, height, ref used, budget, cellCm, step, mark);
        if (Density)
            EmitDensityMarks(session, overlay, projector, height, ref used, budget, cellCm, step, mark);
        if (Belief)
            EmitBeliefMarks(overlay, projector, height, ref used, budget, cellCm, step, mark);
        if (Flow && _firstFlow != null && ArrowsAllowed(CellPx, _config.ArrowMinScreenPx))
            EmitFlowMarks(session, overlay, projector, height, ref used, budget, cellCm, step);
    }

    private void EmitCorridorMarks(CrowdSimSession session, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, int cellCm, int step, int mark)
    {
        int tile = session.Config.Hpa.NavCellsPerTile;
        int tilesPerSide = session.Config.NavCellsPerSide / tile;
        if (_corridor.Length < tilesPerSide * tilesPerSide) return;
        var color = _config.Colors.Plan.ToVector4();
        for (int y = _y0; y < _y1 && used < budget; y += step)
        {
            int ty = y / tile;
            for (int x = _x0; x < _x1 && used < budget; x += step)
            {
                if (_corridor[ty * tilesPerSide + x / tile] == 0) continue;
                EmitCellMark(x, y, cellCm, overlay, projector, height, ref used, budget, mark, color);
            }
        }
    }

    private void EmitBlockedMarks(byte[] blocked, int n, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, int cellCm, int step, int mark)
    {
        var color = _config.Colors.Ink.ToVector4();
        for (int y = _y0; y < _y1 && used < budget; y += step)
        {
            int row = y * n;
            for (int x = _x0; x < _x1 && used < budget; x += step)
            {
                if (blocked[row + x] == 0) continue;
                EmitCellMark(x, y, cellCm, overlay, projector, height, ref used, budget, mark, color);
            }
        }
    }

    private void EmitDensityMarks(CrowdSimSession session, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, int cellCm, int step, int mark)
    {
        var movement = session.Movement;
        if (movement == null) return;
        int spanX = _x1 - _x0, spanY = _y1 - _y0;
        int bw = (spanX + step - 1) / step, bh = (spanY + step - 1) / step;
        int bins = bw * bh;
        if (_bins.Length < bins) _bins = new int[bins];
        Array.Clear(_bins, 0, bins);
        int n = session.Config.NavCellsPerSide;
        int countUnits = CopyUnits(session);
        for (int i = 0; i < countUnits; i++)
        {
            var pos = movement.Positions[_unitIds[i]];
            int x = (int)MathF.Floor(pos.X.ToFloat() / cellCm);
            int y = (int)MathF.Floor(pos.Y.ToFloat() / cellCm);
            if (x < _x0 || x >= _x1 || y < _y0 || y >= _y1 || x < 0 || y < 0 || x >= n || y >= n) continue;
            _bins[((y - _y0) / step) * bw + (x - _x0) / step]++;
        }

        var low = _config.Colors.DensityLow.ToVector4();
        var high = _config.Colors.DensityHigh.ToVector4();
        for (int i = 0; i < bins && used < budget; i++)
        {
            if (_bins[i] == 0) continue;
            float t = _bins[i] / (float)_config.DensityMax;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            int x = _x0 + (i % bw) * step;
            int y = _y0 + (i / bw) * step;
            EmitCellMark(x, y, cellCm, overlay, projector, height, ref used, budget, mark, low + (high - low) * t);
        }
    }

    private void EmitBeliefMarks(ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, int cellCm, int step, int mark)
    {
        if (_diffCount == 0) return;
        var diff = _config.Colors.BeliefDiff.ToVector4();
        var ghost = _config.Colors.BeliefGhost.ToVector4();
        int side = _navSide;
        for (int i = 0; i < _diffCount && used < budget; i++)
        {
            int cell = _beliefCells[i];
            int x = cell % side, y = cell / side;
            if (x < _x0 || x >= _x1 || y < _y0 || y >= _y1) continue;
            EmitCellMark(x, y, cellCm, overlay, projector, height, ref used, budget, mark, _beliefGhost[i] != 0 ? ghost : diff);
        }
    }

    private void EmitFlowMarks(CrowdSimSession session, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, int cellCm, int step)
    {
        var flow = _firstFlow;
        if (flow == null) return;
        int n = session.Config.NavCellsPerSide;
        int n2 = n * n;
        var inf = Fix64.MaxValue / 4;
        var color = _config.Colors.Plan.ToVector4();
        float reach = cellCm * 0.45f;
        for (int y = _y0; y < _y1 && used < budget; y += step)
        {
            for (int x = _x0; x < _x1 && used < budget; x += step)
            {
                int c = y * n + x;
                if (!CrowdFlowFieldVisualProjector.TryScreenArrow(flow, c, n, n2, inf, out float dx, out float dy)
                    && !CrowdFlowFieldVisualProjector.TryScreenArrow(flow, c + n2, n, n2, inf, out dx, out dy))
                    continue;
                float cx = (x + 0.5f) * cellCm, cy = (y + 0.5f) * cellCm;
                var segment = new CrowdDebugSegment { X0 = cx, Y0 = cy, X1 = cx + dx * reach, Y1 = cy + dy * reach };
                EmitSegment(segment, overlay, projector, height, ref used, budget, color);
            }
        }
    }

    private void EmitCellMark(int x, int y, int cellCm, ScreenOverlayBuffer overlay, IScreenProjector projector, IContinuousHeightmap height, ref int used, int budget, int mark, Vector4 color)
    {
        if (used >= budget) return;
        float wx = (x + 0.5f) * cellCm, wy = (y + 0.5f) * cellCm;
        if (!TryScreen(wx, wy, projector, height, out int sx, out int sy)) return;
        if (overlay.AddRect(sx - mark / 2, sy - mark / 2, mark, mark, color, color))
            used = overlay.Count;
    }

    private int CopyUnits(CrowdSimSession session)
    {
        var movement = session.Movement;
        if (movement == null) return 0;
        int cap = session.Config.Sim.MaxUnits;
        if (_unitIds.Length < cap) _unitIds = new int[cap];
        movement.Hash.CellRect(Fix64.FromFloat(_minX), Fix64.FromFloat(_minY), Fix64.FromFloat(_maxX), Fix64.FromFloat(_maxY), out int x0, out int y0, out int x1, out int y1);
        return movement.Hash.CopyItemsInRect(x0, y0, x1, y1, _unitIds);
    }

    private void FillRect(GlobalFieldVisualBuffer buffer, int layer, int cellCm, int step, int bw, int bh, Vector4 color)
    {
        int count = bw * bh;
        EnsureField(count);
        int i = 0;
        for (int y = 0; y < bh; y++)
        {
            for (int x = 0; x < bw; x++)
                _fieldCells[i++] = new GlobalFieldVisualCell(new FieldCell2D(x, y), color);
        }

        Publish(buffer, layer, cellCm, step, bw, bh, count);
    }

    private void FillCorridor(GlobalFieldVisualBuffer buffer, CrowdSimSession session, int cellCm, int step, int bw, int bh)
    {
        int n = session.Config.NavCellsPerSide;
        int tile = session.Config.Hpa.NavCellsPerTile;
        int tilesPerSide = n / tile;
        if (_corridor.Length < tilesPerSide * tilesPerSide) return;
        var color = _config.Colors.Plan.ToVector4();
        BeginStamp(bw * bh);
        EnsureField(bw * bh);
        int count = 0;
        for (int y = _y0; y < _y1; y += step)
        {
            int ty = y / tile;
            for (int x = _x0; x < _x1; x += step)
            {
                int tx = x / tile;
                if (_corridor[ty * tilesPerSide + tx] == 0) continue;
                int cx = (x - _x0) / step, cy = (y - _y0) / step;
                if (!Claim(cy * bw + cx)) continue;
                _fieldCells[count++] = new GlobalFieldVisualCell(new FieldCell2D(cx, cy), color);
            }
        }

        Publish(buffer, _config.Layers.Corridor, cellCm, step, bw, bh, count);
    }

    private void FillBlocked(GlobalFieldVisualBuffer buffer, byte[] blocked, int n, int cellCm, int step, int bw, int bh)
    {
        var color = _config.Colors.Ink.ToVector4();
        BeginStamp(bw * bh);
        EnsureField(bw * bh);
        int count = 0;
        for (int y = _y0; y < _y1; y += step)
        {
            int row = y * n;
            for (int x = _x0; x < _x1; x += step)
            {
                if (blocked[row + x] == 0) continue;
                int cx = (x - _x0) / step, cy = (y - _y0) / step;
                if (!Claim(cy * bw + cx)) continue;
                _fieldCells[count++] = new GlobalFieldVisualCell(new FieldCell2D(cx, cy), color);
            }
        }

        Publish(buffer, _config.Layers.Blocked, cellCm, step, bw, bh, count);
    }

    private void FillDensity(GlobalFieldVisualBuffer buffer, CrowdSimSession session, int cellCm, int step, int bw, int bh)
    {
        var movement = session.Movement;
        if (movement == null) return;
        int bins = bw * bh;
        if (_bins.Length < bins) _bins = new int[bins];
        Array.Clear(_bins, 0, bins);
        int countUnits = CopyUnits(session);
        int n = session.Config.NavCellsPerSide;
        for (int i = 0; i < countUnits; i++)
        {
            var pos = movement.Positions[_unitIds[i]];
            int x = (int)MathF.Floor(pos.X.ToFloat() / cellCm);
            int y = (int)MathF.Floor(pos.Y.ToFloat() / cellCm);
            if (x < _x0 || x >= _x1 || y < _y0 || y >= _y1 || x < 0 || y < 0 || x >= n || y >= n) continue;
            _bins[((y - _y0) / step) * bw + (x - _x0) / step]++;
        }

        var low = _config.Colors.DensityLow.ToVector4();
        var high = _config.Colors.DensityHigh.ToVector4();
        EnsureField(bins);
        int count = 0;
        for (int i = 0; i < bins; i++)
        {
            if (_bins[i] == 0) continue;
            float t = _bins[i] / (float)_config.DensityMax;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            _fieldCells[count++] = new GlobalFieldVisualCell(new FieldCell2D(i % bw, i / bw), low + (high - low) * t);
        }

        Publish(buffer, _config.Layers.Density, cellCm, step, bw, bh, count);
    }

    private void FillBelief(GlobalFieldVisualBuffer buffer, int cellCm, int step, int bw, int bh)
    {
        if (_diffCount == 0) return;
        var diff = _config.Colors.BeliefDiff.ToVector4();
        var ghost = _config.Colors.BeliefGhost.ToVector4();
        BeginStamp(bw * bh);
        EnsureField(_diffCount);
        int side = _navSide;
        int count = 0;
        for (int i = 0; i < _diffCount; i++)
        {
            int cell = _beliefCells[i];
            int x = cell % side;
            int y = cell / side;
            if (x < _x0 || x >= _x1 || y < _y0 || y >= _y1) continue;
            int cx = (x - _x0) / step, cy = (y - _y0) / step;
            if ((uint)cx >= (uint)bw || (uint)cy >= (uint)bh) continue;
            if (!Claim(cy * bw + cx)) continue;
            _fieldCells[count++] = new GlobalFieldVisualCell(new FieldCell2D(cx, cy), _beliefGhost[i] != 0 ? ghost : diff);
        }

        Publish(buffer, _config.Layers.Belief, cellCm, step, bw, bh, count);
    }

    private int _navSide = 1;

    private void Publish(GlobalFieldVisualBuffer buffer, int layer, int cellCm, int step, int bw, int bh, int count)
    {
        if (count <= 0) return;
        _dirty[0] = new IntRect(0, 0, bw, bh);
        var descriptor = new GlobalFieldVisualDescriptor(
            new GlobalFieldVisualId(GlobalFieldVisualKind.Walkable, scopeKeyId: 1, layerKeyId: layer, surfaceKeyId: 0),
            cellCm * step,
            new WorldCmInt2(_x0 * cellCm, _y0 * cellCm),
            new IntRect(0, 0, bw, bh),
            GlobalFieldVisualValueKind.Vector4);
        buffer.Upsert(in descriptor, _fieldCells.AsSpan(0, count), _dirty);
    }

    private void EnsureField(int count)
    {
        if (_fieldCells.Length < count) _fieldCells = new GlobalFieldVisualCell[count];
    }

    private void BeginStamp(int count)
    {
        if (_stamp.Length < count) _stamp = new byte[count];
        _stampGen++;
        if (_stampGen == 0)
        {
            Array.Clear(_stamp);
            _stampGen = 1;
        }
    }

    private bool Claim(int index)
    {
        if (_stamp[index] == _stampGen) return false;
        _stamp[index] = _stampGen;
        return true;
    }

    private CrowdLeader?[] SeenLeaders() => _seen ??= new CrowdLeader[_config.MaxWorldSegments];

    private CrowdDebugSegment[] Scratch()
    {
        _scratch ??= new CrowdDebugSegment[_config.MaxWorldSegments];
        return _scratch;
    }

    private Vector4 KindColor(byte kind, float alphaScale)
    {
        var color = kind switch
        {
            CrowdDebugSegmentKind.Nav => _config.Colors.Nav.ToVector4(),
            CrowdDebugSegmentKind.Plan => _config.Colors.Plan.ToVector4(),
            CrowdDebugSegmentKind.Tile => _config.Colors.Tile.ToVector4(),
            CrowdDebugSegmentKind.TwoWay => _config.Colors.TwoWay.ToVector4(),
            CrowdDebugSegmentKind.OneWay => _config.Colors.OneWay.ToVector4(),
            CrowdDebugSegmentKind.Leader => _config.Colors.Leader.ToVector4(),
            CrowdDebugSegmentKind.Funnel => _config.Colors.Funnel.ToVector4(),
            _ => throw new InvalidOperationException($"{_config.SourcePath}: 未知线段种类 {kind}。"),
        };
        if (alphaScale != 1f) color.W *= alphaScale;
        return color;
    }

    private float Pulse()
    {
        float floor = _config.DirtyPulsePercent / (float)CrowdSimulationLimits.FullPercent;
        float wave = 0.5f + 0.5f * MathF.Sin(Environment.TickCount64 * 0.006f);
        return floor + (1f - floor) * wave;
    }

    private static Vector3 ToWorld(float xCm, float yCm, float heightCm)
        => new(WorldUnits.CmToM(xCm), WorldUnits.CmToM(heightCm), WorldUnits.CmToM(yCm));

    private static bool TryScreen(float xCm, float yCm, IScreenProjector projector, IContinuousHeightmap height, out int sx, out int sy)
    {
        float h = 0f;
        height.TrySampleHeightCm(xCm, yCm, out h);
        var point = projector.WorldToScreen(ToWorld(xCm, yCm, h));
        if (float.IsNaN(point.X) || float.IsNaN(point.Y))
        {
            sx = sy = 0;
            return false;
        }

        sx = (int)MathF.Round(point.X);
        sy = (int)MathF.Round(point.Y);
        return true;
    }
}

internal sealed class S4DeployFrameProjector : IGlobalFieldVisualProjector
{
    private readonly S4DeployDemoViewProjector _demo;
    private readonly CrowdDebugViewFrame _frame;
    private readonly Func<CrowdSimSession?> _session;

    public S4DeployFrameProjector(S4DeployDemoViewProjector demo, CrowdDebugViewFrame frame, Func<CrowdSimSession?> session)
    {
        _demo = demo;
        _frame = frame;
        _session = session;
    }

    public void Project(GlobalFieldVisualBuffer buffer)
    {
        if (_frame.OwnsDecal)
            _frame.Project(buffer, _session());
        else
            _demo.Project(buffer);
    }
}
