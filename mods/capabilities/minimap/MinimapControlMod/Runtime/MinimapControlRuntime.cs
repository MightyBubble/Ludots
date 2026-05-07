using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace MinimapControlMod.Runtime;

public enum MinimapZoomBand : byte
{
    Strategic = 0,
    Regional = 1,
    Tactical = 2,
}

public enum MinimapSignalKind : byte
{
    Capital = 0,
    Settlement = 1,
    Fleet = 2,
    Army = 3,
    Scout = 4,
    Objective = 5,
    Resource = 6,
    Hazard = 7,
    Relay = 8,
}

[Flags]
public enum MinimapSignalFlags : ushort
{
    None = 0,
    Selected = 1 << 0,
    Friendly = 1 << 1,
    Hostile = 1 << 2,
    Neutral = 1 << 3,
    Structure = 1 << 4,
    Mobile = 1 << 5,
    Objective = 1 << 6,
    Resource = 1 << 7,
    Hazard = 1 << 8,
    Alert = 1 << 9,
    Frontier = 1 << 10,
}

public sealed record MinimapDebugSignal(
    string Label,
    int TeamId,
    MinimapSignalKind Kind,
    MinimapSignalFlags Flags,
    float WorldXcm,
    float WorldYcm,
    float NormalizedX,
    float NormalizedY);

public sealed record MinimapDebugCell(
    int CellX,
    int CellY,
    int Total,
    int Friendly,
    int Hostile,
    int Neutral,
    int Structures,
    int Objectives,
    int Resources,
    int Hazards);

public readonly record struct MinimapWorldRegion(
    string Id,
    string Label,
    float CenterXcm,
    float CenterYcm,
    float WidthCm,
    float HeightCm,
    bool Active);

public readonly record struct MinimapWorldClickRequest(
    string RegionId,
    float WorldXcm,
    float WorldYcm);

public enum MinimapDebugRectKind : byte
{
    HotZone = 0,
    FlowWorkArea = 1,
    SolverCache = 2,
}

public readonly record struct MinimapDebugRect(
    string Label,
    MinimapDebugRectKind Kind,
    float CenterXcm,
    float CenterYcm,
    float WidthCm,
    float HeightCm);

public sealed record MinimapDebugSnapshot(
    string MapId,
    string SelectedLabel,
    int PerspectiveTeamId,
    MinimapZoomBand ZoomBand,
    float CenterXcm,
    float CenterYcm,
    float HalfExtentCm,
    float MinWorldXcm,
    float MinWorldYcm,
    float MaxWorldXcm,
    float MaxWorldYcm,
    IReadOnlyList<MinimapDebugSignal> VisibleSignals,
    IReadOnlyList<MinimapDebugCell> StrategicCells);

public sealed class MinimapControlRuntime
{
    private const int MaxSignals = 512;
    private const int MaxRegions = 64;
    private const int MaxDebugRects = 32;
    private const int MaxDebugChunks = 128;
    private const int StrategicGridSide = 12;
    private const int StrategicCellCount = StrategicGridSide * StrategicGridSide;

    private const int PanelWidth = 456;
    private const int PanelHeight = 492;
    private const int FieldSize = 320;
    private const int PanelMargin = 16;

    private static readonly QueryDescription SignalQuery = new QueryDescription()
        .WithAll<Name, WorldPositionCm, Team, MapEntity>();

    private static readonly string[] BandLabels =
    {
        "Strategic",
        "Regional",
        "Tactical",
    };

    private static readonly string[] CountLabels =
    {
        string.Empty,
        "1",
        "2",
        "3",
        "4",
        "5",
        "6",
        "7",
        "8",
        "9+",
    };

    private readonly int[] _entityIds = new int[MaxSignals];
    private readonly int[] _entityVersions = new int[MaxSignals];
    private readonly int[] _teamIds = new int[MaxSignals];
    private readonly float[] _worldXcm = new float[MaxSignals];
    private readonly float[] _worldYcm = new float[MaxSignals];
    private readonly byte[] _kinds = new byte[MaxSignals];
    private readonly ushort[] _flags = new ushort[MaxSignals];
    private readonly byte[] _importance = new byte[MaxSignals];
    private readonly string[] _labels = new string[MaxSignals];
    private readonly int[] _visibleSignalIndices = new int[MaxSignals];
    private readonly MinimapWorldRegion[] _regions = new MinimapWorldRegion[MaxRegions];
    private readonly MinimapDebugRect[] _debugRects = new MinimapDebugRect[MaxDebugRects];
    private readonly int[] _debugChunkX = new int[MaxDebugChunks];
    private readonly int[] _debugChunkY = new int[MaxDebugChunks];

    private readonly int[] _cellTotals = new int[StrategicCellCount];
    private readonly int[] _cellFriendly = new int[StrategicCellCount];
    private readonly int[] _cellHostile = new int[StrategicCellCount];
    private readonly int[] _cellNeutral = new int[StrategicCellCount];
    private readonly int[] _cellStructures = new int[StrategicCellCount];
    private readonly int[] _cellObjectives = new int[StrategicCellCount];
    private readonly int[] _cellResources = new int[StrategicCellCount];
    private readonly int[] _cellHazards = new int[StrategicCellCount];

    private readonly int _capitalTagId = TagRegistry.Register("State.Minimap.Capital");
    private readonly int _objectiveTagId = TagRegistry.Register("State.Minimap.Objective");
    private readonly int _resourceTagId = TagRegistry.Register("State.Minimap.Resource");
    private readonly int _hazardTagId = TagRegistry.Register("State.Minimap.Hazard");
    private readonly int _alertTagId = TagRegistry.Register("State.Minimap.Alert");
    private readonly int _frontierTagId = TagRegistry.Register("State.Minimap.Frontier");

    private string _title = "4X Minimap";
    private string _scaleLabel = string.Empty;
    private int _signalCount;
    private int _visibleSignalCount;
    private int _perspectiveTeamId;
    private string _selectedLabel = string.Empty;
    private string _currentMapId = string.Empty;
    private float _centerXcm;
    private float _centerYcm;
    private float _halfExtentCm = 22000f;
    private float _minWorldXcm;
    private float _minWorldYcm;
    private float _maxWorldXcm;
    private float _maxWorldYcm;
    private float _minHalfExtentCm = 750f;
    private float _tacticalHalfExtentCm = 1800f;
    private float _regionalHalfExtentCm = 7000f;
    private float _strategicHalfExtentCm = 22000f;
    private float _maxHalfExtentCm = 36000f;
    private float _worldMinXcm;
    private float _worldMinYcm;
    private float _worldMaxXcm;
    private float _worldMaxYcm;
    private float _worldFullHalfExtentCm = 22000f;
    private int _regionCount;
    private int _debugRectCount;
    private int _debugChunkCount;
    private int _debugChunkSizeCm;
    private int _debugTotalActiveChunks;
    private bool _debugOverlayVisible;
    private float _cameraCenterXcm;
    private float _cameraCenterYcm;
    private float _cameraWidthCm;
    private float _cameraHeightCm;
    private bool _hasCameraView;
    private bool _viewportInitialized;
    private bool _absoluteWorldOverview;

    public bool Visible { get; set; }
    public MinimapZoomBand ZoomBand { get; private set; } = MinimapZoomBand.Strategic;
    public string CurrentMapId => _currentMapId;
    public string SelectedLabel => _selectedLabel;
    public int PerspectiveTeamId => _perspectiveTeamId;
    public int SignalCount => _signalCount;
    public int VisibleSignalCount => _visibleSignalCount;
    public float CenterXcm => _centerXcm;
    public float CenterYcm => _centerYcm;
    public float HalfExtentCm => _halfExtentCm;
    public int RegionCount => _regionCount;
    public bool AbsoluteWorldOverview => _absoluteWorldOverview;

    public void ClearDebugOverlay()
    {
        _debugOverlayVisible = false;
        _debugRectCount = 0;
        _debugChunkCount = 0;
        _debugChunkSizeCm = 0;
        _debugTotalActiveChunks = 0;
    }

    public void ConfigureDebugChunks(int chunkSizeCm, int totalActiveChunks)
    {
        if (chunkSizeCm <= 0)
        {
            throw new InvalidOperationException("Minimap debug chunks require a positive chunk size.");
        }

        _debugOverlayVisible = true;
        _debugChunkSizeCm = chunkSizeCm;
        _debugTotalActiveChunks = Math.Max(0, totalActiveChunks);
        _debugChunkCount = 0;
    }

    public void AddDebugChunk(int chunkX, int chunkY)
    {
        if (_debugChunkSizeCm <= 0)
        {
            throw new InvalidOperationException("Minimap debug chunk size must be configured before adding chunks.");
        }

        if (_debugChunkCount >= MaxDebugChunks)
        {
            return;
        }

        _debugOverlayVisible = true;
        _debugChunkX[_debugChunkCount] = chunkX;
        _debugChunkY[_debugChunkCount] = chunkY;
        _debugChunkCount++;
    }

    public void AddDebugRect(
        string label,
        MinimapDebugRectKind kind,
        float centerXcm,
        float centerYcm,
        float widthCm,
        float heightCm)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new InvalidOperationException("Minimap debug rect requires a non-empty label.");
        }

        if (widthCm <= 0f || heightCm <= 0f)
        {
            throw new InvalidOperationException($"Minimap debug rect '{label}' requires positive dimensions.");
        }

        if (_debugRectCount >= MaxDebugRects)
        {
            throw new InvalidOperationException($"Minimap debug rect capacity exceeded ({MaxDebugRects}).");
        }

        _debugOverlayVisible = true;
        _debugRects[_debugRectCount++] = new MinimapDebugRect(label, kind, centerXcm, centerYcm, widthCm, heightCm);
    }

    public void SetCameraView(float centerXcm, float centerYcm, float widthCm, float heightCm)
    {
        if (widthCm <= 0f || heightCm <= 0f)
        {
            _hasCameraView = false;
            return;
        }

        _cameraCenterXcm = centerXcm;
        _cameraCenterYcm = centerYcm;
        _cameraWidthCm = widthCm;
        _cameraHeightCm = heightCm;
        _hasCameraView = true;
    }

    public void ConfigureWorldScale(string title, float worldWidthCm, float worldHeightCm, float tacticalHalfExtentCm)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Minimap world scale title is required.");
        }

        if (worldWidthCm <= 0f || worldHeightCm <= 0f)
        {
            throw new InvalidOperationException("Minimap world scale requires positive world dimensions.");
        }

        if (tacticalHalfExtentCm <= 0f)
        {
            throw new InvalidOperationException("Minimap world scale requires a positive tactical half extent.");
        }

        _title = title;
        _scaleLabel = $"{MathF.Max(worldWidthCm, worldHeightCm) / 100_000f:0.#}km Map";
        _worldMinXcm = worldWidthCm * -0.5f;
        _worldMinYcm = worldHeightCm * -0.5f;
        _worldMaxXcm = worldWidthCm * 0.5f;
        _worldMaxYcm = worldHeightCm * 0.5f;
        _worldFullHalfExtentCm = MathF.Max(worldWidthCm, worldHeightCm) * 0.5f;
        _minHalfExtentCm = MathF.Min(750f, tacticalHalfExtentCm);
        _tacticalHalfExtentCm = MathF.Max(_minHalfExtentCm, tacticalHalfExtentCm);
        _strategicHalfExtentCm = _worldFullHalfExtentCm;
        _regionalHalfExtentCm = MathF.Max(_tacticalHalfExtentCm * 2f, _strategicHalfExtentCm * 0.18f);
        _maxHalfExtentCm = _strategicHalfExtentCm;
        _centerXcm = 0f;
        _centerYcm = 0f;
        _halfExtentCm = _strategicHalfExtentCm;
        _viewportInitialized = false;
        _absoluteWorldOverview = false;
    }

    public void SetAbsoluteWorldOverview(bool enabled)
    {
        _absoluteWorldOverview = enabled;
        if (enabled)
        {
            ShowFullWorld();
        }
    }

    public void ClearWorldRegions()
    {
        _regionCount = 0;
    }

    public void AddWorldRegion(
        string id,
        string label,
        float centerXcm,
        float centerYcm,
        float widthCm,
        float heightCm,
        bool active)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Minimap world region requires a non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new InvalidOperationException($"Minimap world region '{id}' requires a non-empty label.");
        }

        if (widthCm <= 0f || heightCm <= 0f)
        {
            throw new InvalidOperationException($"Minimap world region '{id}' requires positive dimensions.");
        }

        if (_regionCount >= MaxRegions)
        {
            throw new InvalidOperationException($"Minimap world region capacity exceeded ({MaxRegions}).");
        }

        _regions[_regionCount++] = new MinimapWorldRegion(id, label, centerXcm, centerYcm, widthCm, heightCm, active);
    }

    public void Refresh(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        _currentMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
        if (!Visible || string.IsNullOrWhiteSpace(_currentMapId))
        {
            ResetTransientState();
            return;
        }

        _signalCount = 0;
        _visibleSignalCount = 0;
        _selectedLabel = string.Empty;
        _perspectiveTeamId = ResolvePerspectiveTeamId(engine);
        _minWorldXcm = float.MaxValue;
        _minWorldYcm = float.MaxValue;
        _maxWorldXcm = float.MinValue;
        _maxWorldYcm = float.MinValue;

        Array.Clear(_cellTotals, 0, _cellTotals.Length);
        Array.Clear(_cellFriendly, 0, _cellFriendly.Length);
        Array.Clear(_cellHostile, 0, _cellHostile.Length);
        Array.Clear(_cellNeutral, 0, _cellNeutral.Length);
        Array.Clear(_cellStructures, 0, _cellStructures.Length);
        Array.Clear(_cellObjectives, 0, _cellObjectives.Length);
        Array.Clear(_cellResources, 0, _cellResources.Length);
        Array.Clear(_cellHazards, 0, _cellHazards.Length);

        MapId activeMapId = engine.CurrentMapSession?.MapId ?? default;
        Entity selectedEntity = ResolveSelectedEntity(engine);

        foreach (ref var chunk in engine.World.Query(in SignalQuery))
        {
            var names = chunk.GetSpan<Name>();
            var positions = chunk.GetSpan<WorldPositionCm>();
            var teams = chunk.GetSpan<Team>();
            var mapEntities = chunk.GetSpan<MapEntity>();
            bool hasTags = chunk.Has<GameplayTagContainer>();
            var tags = hasTags ? chunk.GetSpan<GameplayTagContainer>() : default;

            for (int i = 0; i < chunk.Count && _signalCount < MaxSignals; i++)
            {
                if (mapEntities[i].MapId != activeMapId)
                {
                    continue;
                }

                Entity entity = chunk.Entity(i);
                string label = names[i].Value ?? string.Empty;
                WorldCmInt2 world = positions[i].ToWorldCmInt2();
                GameplayTagContainer tagContainer = hasTags ? tags[i] : default;

                MinimapSignalKind kind = ClassifyKind(label, in tagContainer);
                MinimapSignalFlags flagMask = BuildFlags(
                    label,
                    in tagContainer,
                    kind,
                    teams[i].Id,
                    selectedEntity == entity,
                    _perspectiveTeamId);

                _entityIds[_signalCount] = entity.Id;
                _entityVersions[_signalCount] = entity.Version;
                _teamIds[_signalCount] = teams[i].Id;
                _worldXcm[_signalCount] = world.X;
                _worldYcm[_signalCount] = world.Y;
                _kinds[_signalCount] = (byte)kind;
                _flags[_signalCount] = (ushort)flagMask;
                _importance[_signalCount] = ComputeImportance(kind, flagMask);
                _labels[_signalCount] = label;

                if ((flagMask & MinimapSignalFlags.Selected) != 0)
                {
                    _selectedLabel = label;
                }

                _minWorldXcm = MathF.Min(_minWorldXcm, world.X);
                _minWorldYcm = MathF.Min(_minWorldYcm, world.Y);
                _maxWorldXcm = MathF.Max(_maxWorldXcm, world.X);
                _maxWorldYcm = MathF.Max(_maxWorldYcm, world.Y);

                _signalCount++;
            }
        }

        if (_signalCount == 0)
        {
            _minWorldXcm = _worldMinXcm;
            _minWorldYcm = _worldMinYcm;
            _maxWorldXcm = _worldMaxXcm;
            _maxWorldYcm = _worldMaxYcm;
        }

        if (_absoluteWorldOverview)
        {
            ShowFullWorld();
        }
        else if (!_viewportInitialized)
        {
            ShowFullWorld();
        }

        ClampViewportToWorld();
        ZoomBand = ResolveZoomBand(_halfExtentCm);
        RebuildVisibleSet();
    }

    public void Render(ScreenOverlayBuffer overlay)
    {
        Render(overlay, 1920, 1080);
    }

    public void Render(ScreenOverlayBuffer overlay, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (!Visible)
        {
            return;
        }

        ResolvePanelLayout(viewportWidth, viewportHeight, out int panelX, out int panelY, out int fieldX, out int fieldY, out int legendY);
        overlay.AddRect(
            panelX,
            panelY,
            PanelWidth,
            PanelHeight,
            new Vector4(0.03f, 0.06f, 0.09f, 0.90f),
            new Vector4(0.34f, 0.49f, 0.62f, 0.90f));
        overlay.AddText(panelX + 18, panelY + 24, _title, 20, new Vector4(0.94f, 0.96f, 0.99f, 1f));
        overlay.AddText(panelX + 220, panelY + 24, string.IsNullOrWhiteSpace(_scaleLabel) ? BandLabels[(int)ZoomBand] : _scaleLabel, 16, new Vector4(0.95f, 0.78f, 0.43f, 1f));

        overlay.AddRect(
            fieldX,
            fieldY,
            FieldSize,
            FieldSize,
            new Vector4(0.04f, 0.08f, 0.12f, 0.96f),
            new Vector4(0.22f, 0.35f, 0.43f, 0.95f));
        if (ZoomBand == MinimapZoomBand.Strategic)
        {
            RenderStrategicCells(overlay, fieldX, fieldY);
        }
        else
        {
            RenderSignals(overlay, fieldX, fieldY);
        }

        RenderGrid(overlay, fieldX, fieldY);
        RenderDebugChunks(overlay, fieldX, fieldY);
        RenderRegions(overlay, fieldX, fieldY);
        RenderDebugRects(overlay, fieldX, fieldY);
        RenderCameraView(overlay, fieldX, fieldY);

        string viewport = _absoluteWorldOverview
            ? "RTS absolute minimap: full world view"
            : $"view center ({FormatMeters(_centerXcm)},{FormatMeters(_centerYcm)})m half {_halfExtentCm / 100f:0}m";
        string world = $"ABS WORLD X[{FormatMeters(_worldMinXcm)},{FormatMeters(_worldMaxXcm)}]m Y[{FormatMeters(_worldMinYcm)},{FormatMeters(_worldMaxYcm)}]m";
        string chunks = _debugOverlayVisible && _debugChunkSizeCm > 0
            ? BuildChunkSummary()
            : "chunks inactive";
        string camera = _hasCameraView
            ? $"CAM center ({FormatMeters(_cameraCenterXcm)},{FormatMeters(_cameraCenterYcm)})m size {_cameraWidthCm / 100f:0}x{_cameraHeightCm / 100f:0}m"
            : "CAM view unavailable";
        overlay.AddText(panelX + 18, legendY, viewport, 13, new Vector4(0.72f, 0.90f, 1f, 1f));
        overlay.AddText(panelX + 18, legendY + 18, world, 12, new Vector4(0.72f, 0.79f, 0.86f, 1f));
        overlay.AddText(panelX + 18, legendY + 36, chunks, 12, new Vector4(0.54f, 0.96f, 0.58f, 1f));
        overlay.AddText(panelX + 18, legendY + 54, camera, 12, new Vector4(0.46f, 0.94f, 1f, 1f));
        overlay.AddText(panelX + 18, legendY + 72, "cyan CAM  green ACTIVE/WORK  blue SOLVER  amber HOTZONE", 12, new Vector4(0.84f, 0.88f, 0.92f, 1f));
        overlay.AddText(panelX + 18, legendY + 90, "Click any in-bounds world coordinate. Empty map space is valid.", 11, new Vector4(0.60f, 0.69f, 0.76f, 1f));
    }

    public void SetViewport(float centerXcm, float centerYcm, float halfExtentCm)
    {
        _centerXcm = centerXcm;
        _centerYcm = centerYcm;
        _halfExtentCm = ClampHalfExtent(halfExtentCm);
        ClampViewportToWorld();
        _viewportInitialized = true;
    }

    public bool TryResolveWorldPointFromScreen(int viewportWidth, int viewportHeight, Vector2 screenPosition, out Vector2 worldCm)
    {
        ResolvePanelLayout(viewportWidth, viewportHeight, out _, out _, out int fieldX, out int fieldY, out _);
        if (screenPosition.X < fieldX ||
            screenPosition.X > fieldX + FieldSize ||
            screenPosition.Y < fieldY ||
            screenPosition.Y > fieldY + FieldSize)
        {
            worldCm = default;
            return false;
        }

        float normalizedX = Math.Clamp((screenPosition.X - fieldX) / MathF.Max(1f, FieldSize - 1f), 0f, 1f);
        float normalizedY = Math.Clamp((screenPosition.Y - fieldY) / MathF.Max(1f, FieldSize - 1f), 0f, 1f);
        worldCm = new Vector2(ScreenToWorldX(normalizedX), ScreenToWorldY(normalizedY));
        return true;
    }

    public bool TryResolveRegionFromScreen(int viewportWidth, int viewportHeight, Vector2 screenPosition, out string regionId, out Vector2 worldCm)
    {
        if (!TryResolveWorldPointFromScreen(viewportWidth, viewportHeight, screenPosition, out worldCm))
        {
            regionId = string.Empty;
            return false;
        }

        int bestIndex = -1;
        ResolvePanelLayout(viewportWidth, viewportHeight, out _, out _, out int fieldX, out int fieldY, out _);
        for (int i = 0; i < _regionCount; i++)
        {
            MinimapWorldRegion region = _regions[i];
            ResolveRegionScreenRect(region, fieldX, fieldY, out int x, out int y, out int width, out int height);
            int centerX = x + (width / 2);
            int centerY = y + (height / 2);
            const int pickPaddingPx = 24;
            bool insideScreenPick = screenPosition.X >= x - pickPaddingPx &&
                screenPosition.X <= x + width + pickPaddingPx &&
                screenPosition.Y >= y - pickPaddingPx &&
                screenPosition.Y <= y + height + pickPaddingPx;
            if (insideScreenPick)
            {
                bestIndex = i;
                break;
            }

            float dx = screenPosition.X - centerX;
            float dy = screenPosition.Y - centerY;
            if ((dx * dx) + (dy * dy) <= pickPaddingPx * pickPaddingPx)
            {
                bestIndex = i;
                break;
            }
        }

        if (bestIndex < 0)
        {
            regionId = string.Empty;
            return true;
        }

        regionId = _regions[bestIndex].Id;
        return true;
    }

    public void FocusOnContent()
    {
        if (_absoluteWorldOverview)
        {
            ShowFullWorld();
            return;
        }

        if (_signalCount <= 0)
        {
            return;
        }

        _centerXcm = (_minWorldXcm + _maxWorldXcm) * 0.5f;
        _centerYcm = (_minWorldYcm + _maxWorldYcm) * 0.5f;
        float spanX = MathF.Max(2400f, _maxWorldXcm - _minWorldXcm);
        float spanY = MathF.Max(2400f, _maxWorldYcm - _minWorldYcm);
        _halfExtentCm = ClampHalfExtent(MathF.Max(spanX, spanY) * 0.6f);
        _viewportInitialized = true;
    }

    public void ShowFullWorld()
    {
        _centerXcm = 0f;
        _centerYcm = 0f;
        _halfExtentCm = _worldFullHalfExtentCm;
        _viewportInitialized = true;
    }

    public void CenterOnSelected(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_absoluteWorldOverview)
        {
            ShowFullWorld();
            return;
        }

        Entity selected = ResolveSelectedEntity(engine);
        if (selected == Entity.Null || !engine.World.IsAlive(selected) || !engine.World.TryGet(selected, out WorldPositionCm position))
        {
            return;
        }

        WorldCmInt2 world = position.ToWorldCmInt2();
        _centerXcm = world.X;
        _centerYcm = world.Y;
        ClampViewportToWorld();
        _viewportInitialized = true;
    }

    public void ApplyWheelZoom(float wheelDelta)
    {
        if (_absoluteWorldOverview || wheelDelta == 0f)
        {
            return;
        }

        float factor = wheelDelta > 0f ? 0.85f : 1.18f;
        _halfExtentCm = ClampHalfExtent(_halfExtentCm * factor);
        ClampViewportToWorld();
    }

    public void CycleZoom(int delta)
    {
        if (_absoluteWorldOverview || delta == 0)
        {
            return;
        }

        MinimapZoomBand next = ZoomBand;
        if (delta > 0 && next > MinimapZoomBand.Strategic)
        {
            next--;
        }
        else if (delta < 0 && next < MinimapZoomBand.Tactical)
        {
            next++;
        }

        _halfExtentCm = next switch
        {
            MinimapZoomBand.Strategic => _strategicHalfExtentCm,
            MinimapZoomBand.Regional => _regionalHalfExtentCm,
            _ => _tacticalHalfExtentCm,
        };
        ClampViewportToWorld();
    }

    public void PanNormalized(float dx, float dy)
    {
        if (_absoluteWorldOverview || (dx == 0f && dy == 0f))
        {
            return;
        }

        float step = _halfExtentCm * 1.1f;
        _centerXcm += dx * step;
        _centerYcm += dy * step;
        ClampViewportToWorld();
    }

    public MinimapDebugSnapshot CaptureDebugSnapshot()
    {
        var visibleSignals = new List<MinimapDebugSignal>(_visibleSignalCount);
        for (int i = 0; i < _visibleSignalCount; i++)
        {
            int index = _visibleSignalIndices[i];
            visibleSignals.Add(new MinimapDebugSignal(
                _labels[index] ?? string.Empty,
                _teamIds[index],
                (MinimapSignalKind)_kinds[index],
                (MinimapSignalFlags)_flags[index],
                _worldXcm[index],
                _worldYcm[index],
                NormalizeWorldX(_worldXcm[index]),
                NormalizeWorldY(_worldYcm[index])));
        }

        var cells = new List<MinimapDebugCell>(StrategicCellCount);
        for (int i = 0; i < StrategicCellCount; i++)
        {
            if (_cellTotals[i] <= 0)
            {
                continue;
            }

            int cellX = i % StrategicGridSide;
            int cellY = i / StrategicGridSide;
            cells.Add(new MinimapDebugCell(
                cellX,
                cellY,
                _cellTotals[i],
                _cellFriendly[i],
                _cellHostile[i],
                _cellNeutral[i],
                _cellStructures[i],
                _cellObjectives[i],
                _cellResources[i],
                _cellHazards[i]));
        }

        return new MinimapDebugSnapshot(
            _currentMapId,
            _selectedLabel,
            _perspectiveTeamId,
            ZoomBand,
            _centerXcm,
            _centerYcm,
            _halfExtentCm,
            _minWorldXcm,
            _minWorldYcm,
            _maxWorldXcm,
            _maxWorldYcm,
            visibleSignals,
            cells);
    }

    private void ResolvePanelLayout(
        int viewportWidth,
        int viewportHeight,
        out int panelX,
        out int panelY,
        out int fieldX,
        out int fieldY,
        out int legendY)
    {
        int safeWidth = Math.Max(PanelWidth + (PanelMargin * 2), viewportWidth);
        int safeHeight = Math.Max(PanelHeight + (PanelMargin * 2), viewportHeight);
        panelX = _absoluteWorldOverview
            ? PanelMargin
            : Math.Max(PanelMargin, safeWidth - PanelWidth - PanelMargin);
        panelY = Math.Max(PanelMargin, safeHeight - PanelHeight - PanelMargin);
        fieldX = panelX + 18;
        fieldY = panelY + 56;
        legendY = fieldY + FieldSize + 18;
    }

    private void RenderGrid(ScreenOverlayBuffer overlay, int fieldX, int fieldY)
    {
        int divisions = _absoluteWorldOverview ? 8 : 4;
        int step = FieldSize / divisions;
        float minX = _centerXcm - _halfExtentCm;
        float maxX = _centerXcm + _halfExtentCm;
        float minY = _centerYcm - _halfExtentCm;
        float maxY = _centerYcm + _halfExtentCm;
        for (int i = 1; i < divisions; i++)
        {
            int offset = step * i;
            Vector4 lineColor = i == divisions / 2
                ? new Vector4(0.34f, 0.48f, 0.56f, 0.82f)
                : new Vector4(0.15f, 0.25f, 0.30f, 0.70f);
            overlay.AddRect(fieldX + offset, fieldY, 1, FieldSize, Vector4.Zero, lineColor);
            overlay.AddRect(fieldX, fieldY + offset, FieldSize, 1, Vector4.Zero, lineColor);

            if (i % 2 == 0)
            {
                float xLabel = minX + ((maxX - minX) * i / divisions);
                float yLabel = _absoluteWorldOverview
                    ? maxY - ((maxY - minY) * i / divisions)
                    : minY + ((maxY - minY) * i / divisions);
                overlay.AddText(fieldX + offset - 20, fieldY + FieldSize + 2, FormatMeters(xLabel), 9, new Vector4(0.44f, 0.58f, 0.66f, 0.95f));
                overlay.AddText(fieldX - 42, fieldY + offset + 4, FormatMeters(yLabel), 9, new Vector4(0.44f, 0.58f, 0.66f, 0.95f));
            }
        }

        overlay.AddText(fieldX, fieldY - 14, $"X {FormatMeters(minX)} .. {FormatMeters(maxX)}m", 10, new Vector4(0.55f, 0.72f, 0.82f, 1f));
        overlay.AddText(fieldX + FieldSize - 122, fieldY - 14, _absoluteWorldOverview ? "top = +Y" : $"Y {FormatMeters(_centerYcm)}m", 10, new Vector4(0.55f, 0.72f, 0.82f, 1f));
        overlay.AddText(fieldX + 2, fieldY + 12, $"Y {FormatMeters(maxY)}", 9, new Vector4(0.50f, 0.67f, 0.76f, 0.95f));
        overlay.AddText(fieldX + 2, fieldY + FieldSize - 4, $"Y {FormatMeters(minY)}", 9, new Vector4(0.50f, 0.67f, 0.76f, 0.95f));
    }

    private void RenderDebugChunks(ScreenOverlayBuffer overlay, int fieldX, int fieldY)
    {
        if (!_debugOverlayVisible || _debugChunkSizeCm <= 0)
        {
            return;
        }

        Vector4 fill = new(0.20f, 0.72f, 0.32f, 0.22f);
        Vector4 border = new(0.36f, 0.96f, 0.42f, 0.60f);
        float minXcm = float.MaxValue;
        float minYcm = float.MaxValue;
        float maxXcm = float.MinValue;
        float maxYcm = float.MinValue;
        for (int i = 0; i < _debugChunkCount; i++)
        {
            float chunkMinXcm = _debugChunkX[i] * _debugChunkSizeCm;
            float chunkMinYcm = _debugChunkY[i] * _debugChunkSizeCm;
            float chunkMaxXcm = chunkMinXcm + _debugChunkSizeCm;
            float chunkMaxYcm = chunkMinYcm + _debugChunkSizeCm;
            minXcm = MathF.Min(minXcm, chunkMinXcm);
            minYcm = MathF.Min(minYcm, chunkMinYcm);
            maxXcm = MathF.Max(maxXcm, chunkMaxXcm);
            maxYcm = MathF.Max(maxYcm, chunkMaxYcm);
            ResolveWorldRectScreenRect(
                chunkMinXcm,
                chunkMinYcm,
                chunkMaxXcm,
                chunkMaxYcm,
                fieldX,
                fieldY,
                minPixelSize: _absoluteWorldOverview ? 4 : 2,
                out int x,
                out int y,
                out int width,
                out int height);
            overlay.AddRect(x, y, width, height, fill, border);
        }

        if (_debugChunkCount > 0)
        {
            ResolveWorldRectScreenRect(
                minXcm,
                minYcm,
                maxXcm,
                maxYcm,
                fieldX,
                fieldY,
                minPixelSize: _absoluteWorldOverview ? 22 : 4,
                out int x,
                out int y,
                out int width,
                out int height);
            Vector4 activeBorder = new(0.58f, 1f, 0.44f, 1f);
            overlay.AddRect(x, y, width, height, new Vector4(0.10f, 0.45f, 0.16f, 0.10f), activeBorder);
            overlay.AddText(Math.Clamp(x + 3, fieldX, fieldX + FieldSize - 96), Math.Clamp(y - 12, fieldY, fieldY + FieldSize - 12), "ACTIVE CHUNKS", 9, activeBorder);
        }
    }

    private void RenderStrategicCells(ScreenOverlayBuffer overlay, int fieldX, int fieldY)
    {
        int cellSize = FieldSize / StrategicGridSide;
        for (int index = 0; index < StrategicCellCount; index++)
        {
            int total = _cellTotals[index];
            if (total <= 0)
            {
                continue;
            }

            int x = index % StrategicGridSide;
            int y = index / StrategicGridSide;
            Vector4 fill = ResolveCellColor(index);
            overlay.AddRect(
                fieldX + (x * cellSize) + 2,
                fieldY + (y * cellSize) + 2,
                cellSize - 4,
                cellSize - 4,
                fill,
                new Vector4(0.10f, 0.17f, 0.23f, 0.80f));

            if (_cellObjectives[index] > 0)
            {
                overlay.AddText(fieldX + (x * cellSize) + 6, fieldY + (y * cellSize) + 14, "O", 14, new Vector4(1f, 0.86f, 0.54f, 1f));
            }
            else if (_cellResources[index] > 0)
            {
                overlay.AddText(fieldX + (x * cellSize) + 6, fieldY + (y * cellSize) + 14, "R", 14, new Vector4(0.64f, 0.92f, 0.84f, 1f));
            }
            else if (_cellHazards[index] > 0)
            {
                overlay.AddText(fieldX + (x * cellSize) + 6, fieldY + (y * cellSize) + 14, "!", 14, new Vector4(1f, 0.57f, 0.50f, 1f));
            }

            overlay.AddText(
                fieldX + (x * cellSize) + cellSize - 18,
                fieldY + (y * cellSize) + cellSize - 8,
                CountLabels[Math.Min(9, total)],
                11,
                new Vector4(0.97f, 0.98f, 1f, 1f));
        }

        RenderImportantSignals(overlay, fieldX, fieldY);
    }

    private void RenderRegions(ScreenOverlayBuffer overlay, int fieldX, int fieldY)
    {
        if (_regionCount <= 0)
        {
            return;
        }

        for (int i = 0; i < _regionCount; i++)
        {
            MinimapWorldRegion region = _regions[i];
            ResolveRegionScreenRect(region, fieldX, fieldY, out int x, out int y, out int width, out int height);
            Vector4 border = region.Active
                ? new Vector4(0.58f, 1f, 0.32f, 0.98f)
                : new Vector4(1f, 0.76f, 0.28f, 0.86f);
            Vector4 fill = region.Active
                ? new Vector4(0.18f, 0.56f, 0.12f, 0.14f)
                : new Vector4(0.62f, 0.40f, 0.08f, 0.10f);
            overlay.AddRect(x, y, width, height, fill, border);
            overlay.AddRect(
                x + Math.Max(2, (width / 2) - 2),
                y + Math.Max(2, (height / 2) - 2),
                region.Active ? 5 : 4,
                region.Active ? 5 : 4,
                border,
                border);
        }
    }

    private void RenderDebugRects(ScreenOverlayBuffer overlay, int fieldX, int fieldY)
    {
        if (!_debugOverlayVisible || _debugRectCount <= 0)
        {
            return;
        }

        for (int i = 0; i < _debugRectCount; i++)
        {
            MinimapDebugRect rect = _debugRects[i];
            Vector4 border = rect.Kind switch
            {
                MinimapDebugRectKind.FlowWorkArea => new Vector4(0.50f, 1f, 0.35f, 0.96f),
                MinimapDebugRectKind.SolverCache => new Vector4(0.32f, 0.88f, 1f, 0.96f),
                _ => new Vector4(1f, 0.76f, 0.28f, 0.90f),
            };
            Vector4 fill = rect.Kind switch
            {
                MinimapDebugRectKind.FlowWorkArea => new Vector4(0.16f, 0.62f, 0.10f, 0.12f),
                MinimapDebugRectKind.SolverCache => new Vector4(0.04f, 0.45f, 0.70f, 0.12f),
                _ => new Vector4(0.72f, 0.42f, 0.08f, 0.08f),
            };
            ResolveWorldRectScreenRect(
                rect.CenterXcm - (rect.WidthCm * 0.5f),
                rect.CenterYcm - (rect.HeightCm * 0.5f),
                rect.CenterXcm + (rect.WidthCm * 0.5f),
                rect.CenterYcm + (rect.HeightCm * 0.5f),
                fieldX,
                fieldY,
                minPixelSize: _absoluteWorldOverview ? ResolveAbsoluteDebugRectMinSize(rect.Kind) : rect.Kind == MinimapDebugRectKind.HotZone ? 6 : 10,
                out int x,
                out int y,
                out int width,
                out int height);
            overlay.AddRect(x, y, width, height, fill, border);
            overlay.AddText(Math.Clamp(x + 2, fieldX, fieldX + FieldSize - 80), Math.Clamp(y - 12, fieldY, fieldY + FieldSize - 12), rect.Label, 9, border);
        }
    }

    private void ResolveRegionScreenRect(MinimapWorldRegion region, int fieldX, int fieldY, out int x, out int y, out int width, out int height)
    {
        ResolveWorldRectScreenRect(
            region.CenterXcm - (region.WidthCm * 0.5f),
            region.CenterYcm - (region.HeightCm * 0.5f),
            region.CenterXcm + (region.WidthCm * 0.5f),
            region.CenterYcm + (region.HeightCm * 0.5f),
            fieldX,
            fieldY,
            minPixelSize: 10,
            out x,
            out y,
            out width,
            out height);
    }

    private void ResolveWorldRectScreenRect(
        float minWorldXcm,
        float minWorldYcm,
        float maxWorldXcm,
        float maxWorldYcm,
        int fieldX,
        int fieldY,
        int minPixelSize,
        out int x,
        out int y,
        out int width,
        out int height)
    {
        float x0 = NormalizeWorldX(minWorldXcm);
        float x1 = NormalizeWorldX(maxWorldXcm);
        float y0 = NormalizeWorldY(minWorldYcm);
        float y1 = NormalizeWorldY(maxWorldYcm);
        float minX = MathF.Min(x0, x1);
        float maxX = MathF.Max(x0, x1);
        float minY = MathF.Min(y0, y1);
        float maxY = MathF.Max(y0, y1);
        int rawX = fieldX + (int)MathF.Round(minX * (FieldSize - 1));
        int rawY = fieldY + (int)MathF.Round(minY * (FieldSize - 1));
        int rawWidth = Math.Max(1, (int)MathF.Round((maxX - minX) * (FieldSize - 1)));
        int rawHeight = Math.Max(1, (int)MathF.Round((maxY - minY) * (FieldSize - 1)));
        width = Math.Max(minPixelSize, rawWidth);
        height = Math.Max(minPixelSize, rawHeight);
        x = rawWidth < width
            ? fieldX + (int)MathF.Round(NormalizeWorldX((minWorldXcm + maxWorldXcm) * 0.5f) * (FieldSize - 1)) - (width / 2)
            : rawX;
        y = rawHeight < height
            ? fieldY + (int)MathF.Round(NormalizeWorldY((minWorldYcm + maxWorldYcm) * 0.5f) * (FieldSize - 1)) - (height / 2)
            : rawY;
        x = Math.Clamp(x, fieldX, Math.Max(fieldX, fieldX + FieldSize - width));
        y = Math.Clamp(y, fieldY, Math.Max(fieldY, fieldY + FieldSize - height));
    }

    private void RenderCameraView(ScreenOverlayBuffer overlay, int fieldX, int fieldY)
    {
        if (!_hasCameraView)
        {
            return;
        }

        float x0 = NormalizeWorldX(_cameraCenterXcm - (_cameraWidthCm * 0.5f));
        float x1 = NormalizeWorldX(_cameraCenterXcm + (_cameraWidthCm * 0.5f));
        float y0 = NormalizeWorldY(_cameraCenterYcm - (_cameraHeightCm * 0.5f));
        float y1 = NormalizeWorldY(_cameraCenterYcm + (_cameraHeightCm * 0.5f));
        float minX = MathF.Min(x0, x1);
        float maxX = MathF.Max(x0, x1);
        float minY = MathF.Min(y0, y1);
        float maxY = MathF.Max(y0, y1);
        int rawX = fieldX + (int)MathF.Round(minX * (FieldSize - 1));
        int rawY = fieldY + (int)MathF.Round(minY * (FieldSize - 1));
        int rawWidth = Math.Max(1, (int)MathF.Round((maxX - minX) * (FieldSize - 1)));
        int rawHeight = Math.Max(1, (int)MathF.Round((maxY - minY) * (FieldSize - 1)));

        int centerX = fieldX + (int)MathF.Round(NormalizeWorldX(_cameraCenterXcm) * (FieldSize - 1));
        int centerY = fieldY + (int)MathF.Round(NormalizeWorldY(_cameraCenterYcm) * (FieldSize - 1));
        int width = Math.Max(18, rawWidth);
        int height = Math.Max(18, rawHeight);
        int x = rawWidth < 18 ? centerX - (width / 2) : rawX;
        int y = rawHeight < 18 ? centerY - (height / 2) : rawY;
        x = Math.Clamp(x, fieldX, fieldX + FieldSize - width);
        y = Math.Clamp(y, fieldY, fieldY + FieldSize - height);

        Vector4 cameraColor = new(0.40f, 0.92f, 1f, 1f);
        overlay.AddRect(
            x,
            y,
            width,
            height,
            new Vector4(0.03f, 0.18f, 0.24f, 0.16f),
            cameraColor);
        overlay.AddRect(centerX - 5, centerY, 11, 1, Vector4.Zero, cameraColor);
        overlay.AddRect(centerX, centerY - 5, 1, 11, Vector4.Zero, cameraColor);
        overlay.AddText(Math.Clamp(x + 3, fieldX, fieldX + FieldSize - 36), Math.Max(fieldY + 12, y - 3), "CAM", 11, cameraColor);
    }

    private void RenderSignals(ScreenOverlayBuffer overlay, int fieldX, int fieldY)
    {
        for (int i = 0; i < _visibleSignalCount; i++)
        {
            RenderSignal(overlay, _visibleSignalIndices[i], iconOnly: false, fieldX, fieldY);
        }
    }

    private void RenderImportantSignals(ScreenOverlayBuffer overlay, int fieldX, int fieldY)
    {
        for (int i = 0; i < _visibleSignalCount; i++)
        {
            int index = _visibleSignalIndices[i];
            if (_importance[index] >= 3)
            {
                RenderSignal(overlay, index, iconOnly: true, fieldX, fieldY);
            }
        }
    }

    private void RenderSignal(ScreenOverlayBuffer overlay, int index, bool iconOnly, int fieldX, int fieldY)
    {
        float normalizedX = NormalizeWorldX(_worldXcm[index]);
        float normalizedY = NormalizeWorldY(_worldYcm[index]);
        int screenX = fieldX + (int)MathF.Round(normalizedX * (FieldSize - 1));
        int screenY = fieldY + (int)MathF.Round(normalizedY * (FieldSize - 1));
        string icon = ResolveIcon((MinimapSignalKind)_kinds[index]);
        Vector4 color = ResolveSignalColor((MinimapSignalFlags)_flags[index]);

        if (((MinimapSignalFlags)_flags[index] & MinimapSignalFlags.Selected) != 0)
        {
            overlay.AddRect(
                screenX - 8,
                screenY - 8,
                18,
                18,
                new Vector4(0.00f, 0.00f, 0.00f, 0.25f),
                new Vector4(0.98f, 0.88f, 0.44f, 0.95f));
        }

        overlay.AddText(screenX - (iconOnly ? 3 : 4), screenY + 4, icon, iconOnly ? 12 : 14, color);
        if (!iconOnly && ((MinimapSignalFlags)_flags[index] & MinimapSignalFlags.Alert) != 0)
        {
            overlay.AddRect(screenX + 8, screenY - 2, 5, 5, new Vector4(1f, 0.45f, 0.40f, 0.95f), new Vector4(1f, 0.64f, 0.61f, 1f));
        }
    }

    private void RebuildVisibleSet()
    {
        float minX = _centerXcm - _halfExtentCm;
        float maxX = _centerXcm + _halfExtentCm;
        float minY = _centerYcm - _halfExtentCm;
        float maxY = _centerYcm + _halfExtentCm;

        for (int i = 0; i < _signalCount; i++)
        {
            if (_worldXcm[i] < minX || _worldXcm[i] > maxX || _worldYcm[i] < minY || _worldYcm[i] > maxY)
            {
                continue;
            }

            if (ShouldRenderSignalAtBand(i, ZoomBand))
            {
                _visibleSignalIndices[_visibleSignalCount++] = i;
            }

            int cellIndex = ResolveStrategicCellIndex(_worldXcm[i], _worldYcm[i], minX, minY, _halfExtentCm);
            if ((uint)cellIndex >= (uint)StrategicCellCount)
            {
                continue;
            }

            _cellTotals[cellIndex]++;
            MinimapSignalFlags flags = (MinimapSignalFlags)_flags[i];
            if ((flags & MinimapSignalFlags.Friendly) != 0) _cellFriendly[cellIndex]++;
            if ((flags & MinimapSignalFlags.Hostile) != 0) _cellHostile[cellIndex]++;
            if ((flags & MinimapSignalFlags.Neutral) != 0) _cellNeutral[cellIndex]++;
            if ((flags & MinimapSignalFlags.Structure) != 0) _cellStructures[cellIndex]++;
            if ((flags & MinimapSignalFlags.Objective) != 0) _cellObjectives[cellIndex]++;
            if ((flags & MinimapSignalFlags.Resource) != 0) _cellResources[cellIndex]++;
            if ((flags & MinimapSignalFlags.Hazard) != 0) _cellHazards[cellIndex]++;
        }
    }

    private bool ShouldRenderSignalAtBand(int index, MinimapZoomBand band)
    {
        return band switch
        {
            MinimapZoomBand.Strategic => _importance[index] >= 3,
            MinimapZoomBand.Regional => _importance[index] >= 2,
            _ => true,
        };
    }

    private int ResolveStrategicCellIndex(float x, float y, float minX, float minY, float halfExtent)
    {
        float width = MathF.Max(1f, halfExtent * 2f);
        int cellX = Math.Clamp((int)(((x - minX) / width) * StrategicGridSide), 0, StrategicGridSide - 1);
        int cellY = Math.Clamp((int)(((y - minY) / width) * StrategicGridSide), 0, StrategicGridSide - 1);
        if (_absoluteWorldOverview)
        {
            cellY = (StrategicGridSide - 1) - cellY;
        }

        return (cellY * StrategicGridSide) + cellX;
    }

    private void ClampViewportToWorld()
    {
        _halfExtentCm = ClampHalfExtent(_halfExtentCm);
        float minCenterX = _worldMinXcm + _halfExtentCm;
        float maxCenterX = _worldMaxXcm - _halfExtentCm;
        float minCenterY = _worldMinYcm + _halfExtentCm;
        float maxCenterY = _worldMaxYcm - _halfExtentCm;
        _centerXcm = minCenterX <= maxCenterX
            ? Math.Clamp(_centerXcm, minCenterX, maxCenterX)
            : 0f;
        _centerYcm = minCenterY <= maxCenterY
            ? Math.Clamp(_centerYcm, minCenterY, maxCenterY)
            : 0f;
    }

    private MinimapZoomBand ResolveZoomBand(float halfExtentCm)
    {
        if (_absoluteWorldOverview)
        {
            return MinimapZoomBand.Strategic;
        }

        float regionalThreshold = (_regionalHalfExtentCm + _strategicHalfExtentCm) * 0.5f;
        if (halfExtentCm > regionalThreshold)
        {
            return MinimapZoomBand.Strategic;
        }

        float tacticalThreshold = (_tacticalHalfExtentCm + _regionalHalfExtentCm) * 0.5f;
        return halfExtentCm > tacticalThreshold
            ? MinimapZoomBand.Regional
            : MinimapZoomBand.Tactical;
    }

    private float ClampHalfExtent(float halfExtentCm)
    {
        return Math.Clamp(halfExtentCm, _minHalfExtentCm, _maxHalfExtentCm);
    }

    private float NormalizeWorldX(float worldXcm)
    {
        return _absoluteWorldOverview
            ? NormalizeToRange(worldXcm, _worldMinXcm, _worldMaxXcm)
            : NormalizeToField(worldXcm, _centerXcm, _halfExtentCm);
    }

    private float NormalizeWorldY(float worldYcm)
    {
        if (!_absoluteWorldOverview)
        {
            return NormalizeToField(worldYcm, _centerYcm, _halfExtentCm);
        }

        return 1f - NormalizeToRange(worldYcm, _worldMinYcm, _worldMaxYcm);
    }

    private float ScreenToWorldX(float normalizedX)
    {
        if (_absoluteWorldOverview)
        {
            return _worldMinXcm + (normalizedX * MathF.Max(1f, _worldMaxXcm - _worldMinXcm));
        }

        float minX = _centerXcm - _halfExtentCm;
        return minX + (normalizedX * _halfExtentCm * 2f);
    }

    private float ScreenToWorldY(float normalizedY)
    {
        if (_absoluteWorldOverview)
        {
            return _worldMaxYcm - (normalizedY * MathF.Max(1f, _worldMaxYcm - _worldMinYcm));
        }

        float minY = _centerYcm - _halfExtentCm;
        return minY + (normalizedY * _halfExtentCm * 2f);
    }

    private static float NormalizeToRange(float value, float min, float max)
    {
        return Math.Clamp((value - min) / MathF.Max(1f, max - min), 0f, 1f);
    }

    private static string FormatMeters(float worldCm)
    {
        return (worldCm / 100f).ToString("0");
    }

    private static int ResolveAbsoluteDebugRectMinSize(MinimapDebugRectKind kind)
    {
        return kind switch
        {
            MinimapDebugRectKind.HotZone => 12,
            MinimapDebugRectKind.FlowWorkArea => 18,
            _ => 16,
        };
    }

    private string BuildChunkSummary()
    {
        if (_debugChunkCount <= 0)
        {
            return $"active chunks {_debugTotalActiveChunks} size {_debugChunkSizeCm / 100f:0}m";
        }

        int minX = _debugChunkX[0];
        int maxX = _debugChunkX[0];
        int minY = _debugChunkY[0];
        int maxY = _debugChunkY[0];
        for (int i = 1; i < _debugChunkCount; i++)
        {
            minX = Math.Min(minX, _debugChunkX[i]);
            maxX = Math.Max(maxX, _debugChunkX[i]);
            minY = Math.Min(minY, _debugChunkY[i]);
            maxY = Math.Max(maxY, _debugChunkY[i]);
        }

        return $"active chunks {_debugTotalActiveChunks} shown {_debugChunkCount} x[{minX},{maxX}] y[{minY},{maxY}] size {_debugChunkSizeCm / 100f:0}m";
    }

    private static float NormalizeToField(float worldValue, float centerValue, float halfExtentCm)
    {
        float normalized = (worldValue - (centerValue - halfExtentCm)) / MathF.Max(1f, halfExtentCm * 2f);
        return Math.Clamp(normalized, 0f, 1f);
    }

    private static Entity ResolveSelectedEntity(GameEngine engine)
    {
        return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected)
            ? selected
            : Entity.Null;
    }

    private static int ResolvePerspectiveTeamId(GameEngine engine)
    {
        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
            localObj is Entity localPlayer &&
            engine.World.IsAlive(localPlayer) &&
            engine.World.TryGet(localPlayer, out Team localTeam))
        {
            return localTeam.Id;
        }

        Entity selected = ResolveSelectedEntity(engine);
        if (selected != Entity.Null &&
            engine.World.IsAlive(selected) &&
            engine.World.TryGet(selected, out Team selectedTeam))
        {
            return selectedTeam.Id;
        }

        return 0;
    }

    private static byte ComputeImportance(MinimapSignalKind kind, MinimapSignalFlags flags)
    {
        if ((flags & MinimapSignalFlags.Selected) != 0)
        {
            return 4;
        }

        return kind switch
        {
            MinimapSignalKind.Capital => 4,
            MinimapSignalKind.Objective => 4,
            MinimapSignalKind.Hazard => 3,
            MinimapSignalKind.Resource => 3,
            MinimapSignalKind.Settlement => 3,
            MinimapSignalKind.Fleet when (flags & MinimapSignalFlags.Alert) != 0 => 3,
            MinimapSignalKind.Army when (flags & MinimapSignalFlags.Alert) != 0 => 3,
            MinimapSignalKind.Scout => 1,
            _ => 2,
        };
    }

    private MinimapSignalFlags BuildFlags(
        string label,
        in GameplayTagContainer tags,
        MinimapSignalKind kind,
        int teamId,
        bool selected,
        int perspectiveTeamId)
    {
        MinimapSignalFlags flags = MinimapSignalFlags.None;
        if (selected) flags |= MinimapSignalFlags.Selected;
        if (kind is MinimapSignalKind.Capital or MinimapSignalKind.Settlement or MinimapSignalKind.Relay) flags |= MinimapSignalFlags.Structure;
        if (kind is MinimapSignalKind.Fleet or MinimapSignalKind.Army or MinimapSignalKind.Scout) flags |= MinimapSignalFlags.Mobile;
        if (HasTag(in tags, _objectiveTagId) || kind == MinimapSignalKind.Objective) flags |= MinimapSignalFlags.Objective;
        if (HasTag(in tags, _resourceTagId) || kind == MinimapSignalKind.Resource) flags |= MinimapSignalFlags.Resource;
        if (HasTag(in tags, _hazardTagId) || kind == MinimapSignalKind.Hazard) flags |= MinimapSignalFlags.Hazard;
        if (HasTag(in tags, _alertTagId) || label.Contains("Frontier", StringComparison.OrdinalIgnoreCase)) flags |= MinimapSignalFlags.Alert;
        if (HasTag(in tags, _frontierTagId)) flags |= MinimapSignalFlags.Frontier;

        TeamRelationship relation = TeamManager.GetRelationship(perspectiveTeamId, teamId);
        if (teamId == 0 || perspectiveTeamId == 0) flags |= MinimapSignalFlags.Neutral;
        else if (relation == TeamRelationship.Friendly) flags |= MinimapSignalFlags.Friendly;
        else if (relation == TeamRelationship.Hostile) flags |= MinimapSignalFlags.Hostile;
        else flags |= MinimapSignalFlags.Neutral;

        return flags;
    }

    private MinimapSignalKind ClassifyKind(string label, in GameplayTagContainer tags)
    {
        if (HasTag(in tags, _capitalTagId) ||
            label.Contains("Capital", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Prime", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Nest", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Enclave", StringComparison.OrdinalIgnoreCase))
        {
            return MinimapSignalKind.Capital;
        }

        if (HasTag(in tags, _objectiveTagId) || label.Contains("Gate", StringComparison.OrdinalIgnoreCase))
        {
            return MinimapSignalKind.Objective;
        }

        if (HasTag(in tags, _resourceTagId) ||
            label.Contains("Crystal", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Well", StringComparison.OrdinalIgnoreCase))
        {
            return MinimapSignalKind.Resource;
        }

        if (HasTag(in tags, _hazardTagId) ||
            label.Contains("Rift", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Storm", StringComparison.OrdinalIgnoreCase))
        {
            return MinimapSignalKind.Hazard;
        }

        if (label.Contains("Colony", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Bastion", StringComparison.OrdinalIgnoreCase))
        {
            return MinimapSignalKind.Settlement;
        }

        if (label.Contains("Scout", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Surveyor", StringComparison.OrdinalIgnoreCase))
        {
            return MinimapSignalKind.Scout;
        }

        if (label.Contains("Relay", StringComparison.OrdinalIgnoreCase))
        {
            return MinimapSignalKind.Relay;
        }

        if (label.Contains("Fleet", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Carrier", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Caravan", StringComparison.OrdinalIgnoreCase))
        {
            return MinimapSignalKind.Fleet;
        }

        if (label.Contains("Warpack", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Wing", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Watch", StringComparison.OrdinalIgnoreCase))
        {
            return MinimapSignalKind.Army;
        }

        return MinimapSignalKind.Settlement;
    }

    private bool HasTag(in GameplayTagContainer tags, int tagId)
    {
        return tagId > 0 && !tags.IsEmpty && tags.HasTag(tagId);
    }

    private static string ResolveIcon(MinimapSignalKind kind)
    {
        return kind switch
        {
            MinimapSignalKind.Capital => "C",
            MinimapSignalKind.Settlement => "S",
            MinimapSignalKind.Fleet => "F",
            MinimapSignalKind.Army => "A",
            MinimapSignalKind.Scout => "s",
            MinimapSignalKind.Objective => "O",
            MinimapSignalKind.Resource => "R",
            MinimapSignalKind.Hazard => "!",
            _ => "P",
        };
    }

    private static Vector4 ResolveSignalColor(MinimapSignalFlags flags)
    {
        if ((flags & MinimapSignalFlags.Selected) != 0) return new Vector4(0.98f, 0.88f, 0.44f, 1f);
        if ((flags & MinimapSignalFlags.Objective) != 0) return new Vector4(1f, 0.84f, 0.55f, 1f);
        if ((flags & MinimapSignalFlags.Resource) != 0) return new Vector4(0.54f, 0.92f, 0.80f, 1f);
        if ((flags & MinimapSignalFlags.Hazard) != 0) return new Vector4(1f, 0.55f, 0.50f, 1f);
        if ((flags & MinimapSignalFlags.Hostile) != 0) return new Vector4(0.96f, 0.38f, 0.36f, 1f);
        if ((flags & MinimapSignalFlags.Friendly) != 0) return new Vector4(0.42f, 0.82f, 1f, 1f);
        return new Vector4(0.77f, 0.82f, 0.88f, 1f);
    }

    private Vector4 ResolveCellColor(int index)
    {
        if (_cellHostile[index] > _cellFriendly[index] && _cellHostile[index] >= _cellNeutral[index])
        {
            return new Vector4(0.44f, 0.12f, 0.11f, ResolveDensityAlpha(_cellTotals[index]));
        }

        if (_cellFriendly[index] >= _cellNeutral[index])
        {
            return new Vector4(0.09f, 0.28f, 0.39f, ResolveDensityAlpha(_cellTotals[index]));
        }

        return new Vector4(0.16f, 0.18f, 0.21f, ResolveDensityAlpha(_cellTotals[index]));
    }

    private static float ResolveDensityAlpha(int total)
    {
        return Math.Clamp(0.20f + (Math.Min(6, total) * 0.09f), 0.24f, 0.82f);
    }

    private void ResetTransientState()
    {
        _signalCount = 0;
        _visibleSignalCount = 0;
        _selectedLabel = string.Empty;
        _currentMapId = string.Empty;
        _minWorldXcm = 0f;
        _minWorldYcm = 0f;
        _maxWorldXcm = 0f;
        _maxWorldYcm = 0f;
    }
}
