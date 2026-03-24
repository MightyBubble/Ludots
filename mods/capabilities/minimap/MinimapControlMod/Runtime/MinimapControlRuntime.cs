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
    private const int StrategicGridSide = 12;
    private const int StrategicCellCount = StrategicGridSide * StrategicGridSide;

    private const int PanelX = 1472;
    private const int PanelY = 32;
    private const int PanelWidth = 416;
    private const int PanelHeight = 414;
    private const int FieldX = PanelX + 18;
    private const int FieldY = PanelY + 56;
    private const int FieldSize = 272;
    private const int LegendY = FieldY + FieldSize + 18;

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
    private bool _viewportInitialized;

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
            ResetTransientState();
            return;
        }

        if (!_viewportInitialized)
        {
            FocusOnContent();
        }

        ClampViewportToContent();
        ZoomBand = ResolveZoomBand(_halfExtentCm);
        RebuildVisibleSet();
    }

    public void Render(ScreenOverlayBuffer overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (!Visible || _signalCount == 0)
        {
            return;
        }

        overlay.AddRect(
            PanelX,
            PanelY,
            PanelWidth,
            PanelHeight,
            new Vector4(0.03f, 0.06f, 0.09f, 0.90f),
            new Vector4(0.34f, 0.49f, 0.62f, 0.90f));
        overlay.AddText(PanelX + 18, PanelY + 24, "4X Minimap", 20, new Vector4(0.94f, 0.96f, 0.99f, 1f));
        overlay.AddText(PanelX + 220, PanelY + 24, BandLabels[(int)ZoomBand], 16, new Vector4(0.95f, 0.78f, 0.43f, 1f));

        overlay.AddRect(
            FieldX,
            FieldY,
            FieldSize,
            FieldSize,
            new Vector4(0.04f, 0.08f, 0.12f, 0.96f),
            new Vector4(0.22f, 0.35f, 0.43f, 0.95f));
        RenderGrid(overlay);

        if (ZoomBand == MinimapZoomBand.Strategic)
        {
            RenderStrategicCells(overlay);
        }
        else
        {
            RenderSignals(overlay);
        }

        overlay.AddText(PanelX + 18, LegendY, "Wheel/PageUp/PageDown zoom", 14, new Vector4(0.72f, 0.79f, 0.86f, 1f));
        overlay.AddText(PanelX + 18, LegendY + 22, "Arrows pan  C center selected", 14, new Vector4(0.72f, 0.79f, 0.86f, 1f));
        overlay.AddText(PanelX + 18, LegendY + 48, "Selected", 14, new Vector4(0.95f, 0.78f, 0.43f, 1f));
        overlay.AddText(PanelX + 92, LegendY + 48, string.IsNullOrWhiteSpace(_selectedLabel) ? "None" : _selectedLabel, 14, new Vector4(0.97f, 0.98f, 1f, 1f));
        overlay.AddText(PanelX + 18, LegendY + 72, "Empire / frontier / tactical layers switch by zoom band.", 13, new Vector4(0.60f, 0.69f, 0.76f, 1f));
    }

    public void SetViewport(float centerXcm, float centerYcm, float halfExtentCm)
    {
        _centerXcm = centerXcm;
        _centerYcm = centerYcm;
        _halfExtentCm = ClampHalfExtent(halfExtentCm);
        _viewportInitialized = true;
    }

    public void FocusOnContent()
    {
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

    public void CenterOnSelected(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        Entity selected = ResolveSelectedEntity(engine);
        if (selected == Entity.Null || !engine.World.IsAlive(selected) || !engine.World.TryGet(selected, out WorldPositionCm position))
        {
            return;
        }

        WorldCmInt2 world = position.ToWorldCmInt2();
        _centerXcm = world.X;
        _centerYcm = world.Y;
        _viewportInitialized = true;
    }

    public void ApplyWheelZoom(float wheelDelta)
    {
        if (wheelDelta == 0f)
        {
            return;
        }

        float factor = wheelDelta > 0f ? 0.85f : 1.18f;
        _halfExtentCm = ClampHalfExtent(_halfExtentCm * factor);
    }

    public void CycleZoom(int delta)
    {
        if (delta == 0)
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
            MinimapZoomBand.Strategic => 22000f,
            MinimapZoomBand.Regional => 7000f,
            _ => 1800f,
        };
    }

    public void PanNormalized(float dx, float dy)
    {
        if (dx == 0f && dy == 0f)
        {
            return;
        }

        float step = _halfExtentCm * 1.1f;
        _centerXcm += dx * step;
        _centerYcm += dy * step;
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
                NormalizeToField(_worldXcm[index], _centerXcm, _halfExtentCm),
                NormalizeToField(_worldYcm[index], _centerYcm, _halfExtentCm)));
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

    private void RenderGrid(ScreenOverlayBuffer overlay)
    {
        int step = FieldSize / 4;
        for (int i = 1; i < 4; i++)
        {
            int offset = step * i;
            overlay.AddRect(FieldX + offset, FieldY, 1, FieldSize, Vector4.Zero, new Vector4(0.15f, 0.25f, 0.30f, 0.70f));
            overlay.AddRect(FieldX, FieldY + offset, FieldSize, 1, Vector4.Zero, new Vector4(0.15f, 0.25f, 0.30f, 0.70f));
        }

        overlay.AddRect(FieldX + (FieldSize / 2), FieldY, 1, FieldSize, Vector4.Zero, new Vector4(0.32f, 0.43f, 0.50f, 0.75f));
        overlay.AddRect(FieldX, FieldY + (FieldSize / 2), FieldSize, 1, Vector4.Zero, new Vector4(0.32f, 0.43f, 0.50f, 0.75f));
    }

    private void RenderStrategicCells(ScreenOverlayBuffer overlay)
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
                FieldX + (x * cellSize) + 2,
                FieldY + (y * cellSize) + 2,
                cellSize - 4,
                cellSize - 4,
                fill,
                new Vector4(0.10f, 0.17f, 0.23f, 0.80f));

            if (_cellObjectives[index] > 0)
            {
                overlay.AddText(FieldX + (x * cellSize) + 6, FieldY + (y * cellSize) + 14, "O", 14, new Vector4(1f, 0.86f, 0.54f, 1f));
            }
            else if (_cellResources[index] > 0)
            {
                overlay.AddText(FieldX + (x * cellSize) + 6, FieldY + (y * cellSize) + 14, "R", 14, new Vector4(0.64f, 0.92f, 0.84f, 1f));
            }
            else if (_cellHazards[index] > 0)
            {
                overlay.AddText(FieldX + (x * cellSize) + 6, FieldY + (y * cellSize) + 14, "!", 14, new Vector4(1f, 0.57f, 0.50f, 1f));
            }

            overlay.AddText(
                FieldX + (x * cellSize) + cellSize - 18,
                FieldY + (y * cellSize) + cellSize - 8,
                CountLabels[Math.Min(9, total)],
                11,
                new Vector4(0.97f, 0.98f, 1f, 1f));
        }

        RenderImportantSignals(overlay);
    }

    private void RenderSignals(ScreenOverlayBuffer overlay)
    {
        for (int i = 0; i < _visibleSignalCount; i++)
        {
            RenderSignal(overlay, _visibleSignalIndices[i], iconOnly: false);
        }
    }

    private void RenderImportantSignals(ScreenOverlayBuffer overlay)
    {
        for (int i = 0; i < _visibleSignalCount; i++)
        {
            int index = _visibleSignalIndices[i];
            if (_importance[index] >= 3)
            {
                RenderSignal(overlay, index, iconOnly: true);
            }
        }
    }

    private void RenderSignal(ScreenOverlayBuffer overlay, int index, bool iconOnly)
    {
        float normalizedX = NormalizeToField(_worldXcm[index], _centerXcm, _halfExtentCm);
        float normalizedY = NormalizeToField(_worldYcm[index], _centerYcm, _halfExtentCm);
        int screenX = FieldX + (int)MathF.Round(normalizedX * (FieldSize - 1));
        int screenY = FieldY + (int)MathF.Round(normalizedY * (FieldSize - 1));
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
        return (cellY * StrategicGridSide) + cellX;
    }

    private void ClampViewportToContent()
    {
        float spanX = MathF.Max(2400f, _maxWorldXcm - _minWorldXcm);
        float spanY = MathF.Max(2400f, _maxWorldYcm - _minWorldYcm);
        float maxSpan = MathF.Max(spanX, spanY);
        _halfExtentCm = MathF.Min(ClampHalfExtent(_halfExtentCm), MathF.Max(1400f, maxSpan * 0.8f));

        float padding = _halfExtentCm * 0.35f;
        _centerXcm = Math.Clamp(_centerXcm, _minWorldXcm - padding, _maxWorldXcm + padding);
        _centerYcm = Math.Clamp(_centerYcm, _minWorldYcm - padding, _maxWorldYcm + padding);
    }

    private static MinimapZoomBand ResolveZoomBand(float halfExtentCm)
    {
        if (halfExtentCm > 11000f)
        {
            return MinimapZoomBand.Strategic;
        }

        return halfExtentCm > 3600f
            ? MinimapZoomBand.Regional
            : MinimapZoomBand.Tactical;
    }

    private static float ClampHalfExtent(float halfExtentCm)
    {
        return Math.Clamp(halfExtentCm, 750f, 36000f);
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
