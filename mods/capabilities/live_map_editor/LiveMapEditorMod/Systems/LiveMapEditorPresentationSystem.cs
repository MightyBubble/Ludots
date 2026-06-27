using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.UI;
using LiveMapEditorMod.Runtime;
using LiveMapEditorMod.UI;

namespace LiveMapEditorMod.Systems;

internal sealed class LiveMapEditorPresentationSystem : ISystem<float>
{
    private const float OverlayY = 0.18f;
    private const float BoardGuideY = 0.24f;
    private const int MaxBoardGuideLinesPerAxis = 65;
    private static readonly Vector4 BoardGuideFillColor = new(0.18f, 0.75f, 1f, 0.22f);
    private static readonly Vector4 BoardGuideBorderColor = new(0.38f, 0.92f, 1f, 0.96f);
    private static readonly Vector4 BoardGuideMajorColor = new(1f, 0.86f, 0.24f, 0.9f);
    private static readonly Vector4 BoardGuideMinorColor = new(0.38f, 0.92f, 1f, 0.44f);
    private static readonly Vector4 BoardGuideStatusFillColor = new(0.02f, 0.06f, 0.10f, 0.72f);
    private static readonly Vector4 BoardGuideStatusBorderColor = new(0.20f, 0.82f, 1f, 0.52f);
    private static readonly Vector4 BoardGuideStatusTextColor = new(0.88f, 0.97f, 1f, 0.96f);
    private readonly GameEngine _engine;
    private readonly LiveMapEditorRuntime _runtime;
    private readonly LiveMapEditorPanelController _panelController;
    private PlayerInputHandler? _input;
    private float _publishAccumulator;

    public LiveMapEditorPresentationSystem(
        GameEngine engine,
        LiveMapEditorRuntime runtime,
        LiveMapEditorPanelController panelController)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _panelController = panelController ?? throw new ArgumentNullException(nameof(panelController));
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float t)
    {
    }

    public void Update(in float t)
    {
        ResolveInput();
        if (_input != null)
        {
            HandleToggle();
            UpdatePick();
            HandleViewportCommands();
        }

        _runtime.DrainSpawnReceipts(_engine);
        FlushDataPlane(t);
        DrawDebugOverlays();
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }

    private void ResolveInput()
    {
        if (_input != null)
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler input)
        {
            _input = input;
        }
    }

    private void HandleToggle()
    {
        if (_input!.PressedThisFrame(LiveMapEditorIds.TogglePanelAction))
        {
            _panelController.Toggle();
            _input.SuppressActionThisFrame(LiveMapEditorIds.TogglePanelAction);
        }
    }

    private void UpdatePick()
    {
        if (AuthoritativeGroundPointerHelper.TryRead(_input!, out WorldCmInt2 worldCm))
        {
            _runtime.UpdatePickedWorld(_engine, worldCm);
        }
        else
        {
            _runtime.ClearPick();
        }
    }

    private void HandleViewportCommands()
    {
        if (!_runtime.HasPickedWorld || IsPointerInPanelChrome())
        {
            return;
        }

        if (string.Equals(_runtime.Tool, "paint", StringComparison.Ordinal) &&
            _input!.IsDown(LiveMapEditorIds.PrimaryAction))
        {
            _runtime.PaintTerrain(_engine, null, null, null);
            return;
        }

        if (string.Equals(_runtime.Tool, "inspect", StringComparison.Ordinal) &&
            _input!.PressedThisFrame(LiveMapEditorIds.PrimaryAction))
        {
            _runtime.SelectNearestEntity(_engine, null, null);
            return;
        }

        if (string.Equals(_runtime.Tool, "sim", StringComparison.Ordinal) ||
            string.Equals(_runtime.Tool, "nav", StringComparison.Ordinal))
        {
            if (_input!.PressedThisFrame(LiveMapEditorIds.PrimaryAction))
            {
                _runtime.Nav.Start = _runtime.PickedWorld;
                _runtime.Nav.HasStart = true;
                if (_runtime.Nav.HasGoal)
                {
                    _runtime.QueryPath(_engine, null, null, null, null);
                }
            }

            if (_input.PressedThisFrame(LiveMapEditorIds.SecondaryAction))
            {
                _runtime.Nav.Goal = _runtime.PickedWorld;
                _runtime.Nav.HasGoal = true;
                if (_runtime.Nav.HasStart)
                {
                    _runtime.QueryPath(_engine, null, null, null, null);
                }
            }
        }
    }

    private bool IsPointerInPanelChrome()
    {
        if (!_runtime.PanelOpen || _input == null)
        {
            return false;
        }

        Vector2 pointer = _input.ReadAction<Vector2>(LiveMapEditorIds.PointerAction);
        if (_engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return false;
        }

        float width = Math.Max(1f, root.Width);
        float height = Math.Max(1f, root.Height);
        return pointer.Y <= 60f ||
               pointer.X <= 360f ||
               pointer.X >= width - 360f ||
               pointer.Y >= height - 118f;
    }

    private void FlushDataPlane(float dt)
    {
        if (_runtime.DataPlaneTickPump == null)
        {
            return;
        }

        try
        {
            _runtime.DataPlaneTickPump.FlushCommandsAsync().AsTask().GetAwaiter().GetResult();
            _publishAccumulator += dt;
            if (_publishAccumulator >= 0.1f)
            {
                _publishAccumulator = 0f;
                _runtime.DataPlaneTickPump.PublishTopicsAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DrawDebugOverlays()
    {
        if (_engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is GroundOverlayBuffer ground)
        {
            DrawBoardAuthoringGuides(ground);
            DrawPickAndBrush(ground);
            DrawPath(ground);
        }

        if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is ScreenOverlayBuffer screen)
        {
            DrawBoardAuthoringStatus(screen);
        }

        if (_engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer) is DebugDrawCommandBuffer debug)
        {
            DrawNavTiles(debug);
        }
    }

    private void DrawBoardAuthoringGuides(GroundOverlayBuffer ground)
    {
        if (!TryResolvePrimaryBoard(out IBoard? board, out WorldAabbCm bounds) ||
            bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            return;
        }

        int guideStepCm = ResolveBoardGuideStepCm(board, bounds);
        DrawBoardBounds(ground, bounds);
        DrawBoardChunkGrid(ground, bounds, guideStepCm);
        DrawBoardReferenceMarkers(ground, bounds);
    }

    private void DrawBoardAuthoringStatus(ScreenOverlayBuffer screen)
    {
        if (_runtime.PanelOpen)
        {
            return;
        }

        if (!TryResolvePrimaryBoard(out IBoard? board, out WorldAabbCm bounds) ||
            bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            return;
        }

        int guideStepCm = ResolveBoardGuideStepCm(board, bounds);
        int widthM = (int)MathF.Round(WorldUnits.CmToM(bounds.Width));
        int heightM = (int)MathF.Round(WorldUnits.CmToM(bounds.Height));
        int guideStepM = Math.Max(1, (int)MathF.Round(WorldUnits.CmToM(guideStepCm)));
        string boardName = board?.Name ?? "world";
        string line = $"Live Map Editor | board={boardName} | {widthM}m x {heightM}m | guide={guideStepM}m";

        screen.AddRect(16, 16, 560, 42, BoardGuideStatusFillColor, BoardGuideStatusBorderColor);
        screen.AddText(28, 28, line, 16, BoardGuideStatusTextColor);
    }

    private bool TryResolvePrimaryBoard(out IBoard? board, out WorldAabbCm bounds)
    {
        board = _engine.CurrentMapSession?.PrimaryBoard;
        if (board != null)
        {
            bounds = board.WorldSize.Bounds;
            return true;
        }

        bounds = _engine.WorldSizeSpec.Bounds;
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private int ResolveBoardGuideStepCm(IBoard? board, in WorldAabbCm bounds)
    {
        int chunkSizeCells = ResolveBoardChunkSizeCells(board);
        int cellCm = Math.Max(1, board?.WorldSize.GridCellSizeCm ?? _engine.WorldSizeSpec.GridCellSizeCm);
        int stepCm = Math.Max(cellCm, chunkSizeCells * cellCm);
        int maxLineCount = Math.Max(
            (int)MathF.Ceiling(bounds.Width / (float)stepCm),
            (int)MathF.Ceiling(bounds.Height / (float)stepCm));
        if (maxLineCount <= MaxBoardGuideLinesPerAxis)
        {
            return stepCm;
        }

        int multiplier = (int)MathF.Ceiling(maxLineCount / (float)MaxBoardGuideLinesPerAxis);
        return checked(stepCm * Math.Max(1, multiplier));
    }

    private int ResolveBoardChunkSizeCells(IBoard? board)
    {
        if (board is GridBoard gridBoard)
        {
            return Math.Max(1, gridBoard.ChunkSizeCells);
        }

        string? boardName = board?.Name;
        if (!string.IsNullOrWhiteSpace(boardName) &&
            _engine.CurrentMapSession?.MapConfig?.Boards != null)
        {
            foreach (BoardConfig config in _engine.CurrentMapSession.MapConfig.Boards)
            {
                if (string.Equals(config.Name, boardName, StringComparison.Ordinal))
                {
                    return Math.Max(1, config.ChunkSizeCells);
                }
            }
        }

        return SpatialScaleDefaults.TerrainChunkCells;
    }

    private static void DrawBoardBounds(GroundOverlayBuffer ground, in WorldAabbCm bounds)
    {
        float widthM = WorldUnits.CmToM(bounds.Width);
        float heightM = WorldUnits.CmToM(bounds.Height);
        AddGuideLine(ground, bounds.Left, bounds.Top, widthM, 0f, 0.42f, BoardGuideFillColor, BoardGuideBorderColor);
        AddGuideLine(ground, bounds.Left, bounds.Bottom, widthM, 0f, 0.42f, BoardGuideFillColor, BoardGuideBorderColor);
        AddGuideLine(ground, bounds.Left, bounds.Top, heightM, MathF.PI / 2f, 0.42f, BoardGuideFillColor, BoardGuideBorderColor);
        AddGuideLine(ground, bounds.Right, bounds.Top, heightM, MathF.PI / 2f, 0.42f, BoardGuideFillColor, BoardGuideBorderColor);
    }

    private static void DrawBoardChunkGrid(GroundOverlayBuffer ground, in WorldAabbCm bounds, int stepCm)
    {
        if (stepCm <= 0)
        {
            return;
        }

        int majorStepCm = Math.Max(stepCm, 4 * stepCm);
        float widthM = WorldUnits.CmToM(bounds.Width);
        float heightM = WorldUnits.CmToM(bounds.Height);

        for (int x = bounds.Left + stepCm; x < bounds.Right; x += stepCm)
        {
            bool major = ((x - bounds.Left) % majorStepCm) == 0;
            Vector4 color = major ? BoardGuideMajorColor : BoardGuideMinorColor;
            AddGuideLine(ground, x, bounds.Top, heightM, MathF.PI / 2f, major ? 0.24f : 0.12f, color, color);
        }

        for (int y = bounds.Top + stepCm; y < bounds.Bottom; y += stepCm)
        {
            bool major = ((y - bounds.Top) % majorStepCm) == 0;
            Vector4 color = major ? BoardGuideMajorColor : BoardGuideMinorColor;
            AddGuideLine(ground, bounds.Left, y, widthM, 0f, major ? 0.24f : 0.12f, color, color);
        }
    }

    private static void DrawBoardReferenceMarkers(GroundOverlayBuffer ground, in WorldAabbCm bounds)
    {
        ground.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Circle,
            Center = WorldUnits.WorldCmToVisualMeters(bounds.Left, bounds.Top, BoardGuideY + 0.02f),
            Radius = 1.2f,
            FillColor = new Vector4(0.22f, 1f, 0.44f, 0.24f),
            BorderColor = new Vector4(0.22f, 1f, 0.44f, 1f),
            BorderWidth = 0.06f
        });

        ground.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Ring,
            Center = WorldUnits.WorldCmToVisualMeters(
                bounds.Left + (bounds.Width * 0.5f),
                bounds.Top + (bounds.Height * 0.5f),
                BoardGuideY + 0.02f),
            Radius = 1.9f,
            InnerRadius = 1.35f,
            FillColor = new Vector4(1f, 0.86f, 0.24f, 0.18f),
            BorderColor = new Vector4(1f, 0.86f, 0.24f, 0.95f),
            BorderWidth = 0.06f
        });
    }

    private static void AddGuideLine(
        GroundOverlayBuffer ground,
        int startXCm,
        int startYCm,
        float lengthM,
        float rotation,
        float widthM,
        Vector4 fill,
        Vector4 border)
    {
        ground.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Line,
            Center = WorldUnits.WorldCmToVisualMeters(startXCm, startYCm, BoardGuideY),
            Length = lengthM,
            Width = widthM,
            Rotation = rotation,
            FillColor = fill,
            BorderColor = border,
            BorderWidth = 0.04f
        });
    }

    private void DrawPickAndBrush(GroundOverlayBuffer ground)
    {
        if (!_runtime.HasPickedWorld)
        {
            return;
        }

        ground.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Ring,
            Center = WorldUnits.WorldCmToVisualMeters(_runtime.PickedWorld, OverlayY),
            Radius = WorldUnits.CmToM(Math.Max(60, ResolveBrushRadiusCm())),
            InnerRadius = WorldUnits.CmToM(Math.Max(20, ResolveBrushRadiusCm() - 20)),
            FillColor = new Vector4(0.18f, 0.85f, 1f, 0.12f),
            BorderColor = new Vector4(0.18f, 0.85f, 1f, 0.9f),
            BorderWidth = 0.03f
        });

        if (_runtime.SelectedEntity != Entity.Null &&
            _engine.World.IsAlive(_runtime.SelectedEntity) &&
            _engine.World.TryGet(_runtime.SelectedEntity, out Ludots.Core.Components.WorldPositionCm position))
        {
            ground.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Ring,
                Center = WorldUnits.WorldCmToVisualMeters(position.ToWorldCmInt2(), OverlayY + 0.03f),
                Radius = 1.2f,
                InnerRadius = 0.95f,
                FillColor = new Vector4(1f, 0.85f, 0.16f, 0.12f),
                BorderColor = new Vector4(1f, 0.85f, 0.16f, 1f),
                BorderWidth = 0.04f
            });
        }
    }

    private int ResolveBrushRadiusCm()
    {
        if (_engine.LogicTerrain == null)
        {
            return 100;
        }

        return Math.Max(1, _runtime.Brush.RadiusCells) * _engine.LogicTerrain.HorizontalStepCm;
    }

    private void DrawPath(GroundOverlayBuffer ground)
    {
        if (_runtime.Nav.HasStart)
        {
            ground.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = WorldUnits.WorldCmToVisualMeters(_runtime.Nav.Start, OverlayY + 0.04f),
                Radius = 0.65f,
                FillColor = new Vector4(0.2f, 1f, 0.45f, 0.32f),
                BorderColor = new Vector4(0.2f, 1f, 0.45f, 1f),
                BorderWidth = 0.04f
            });
        }

        if (_runtime.Nav.HasGoal)
        {
            ground.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = WorldUnits.WorldCmToVisualMeters(_runtime.Nav.Goal, OverlayY + 0.04f),
                Radius = 0.65f,
                FillColor = new Vector4(1f, 0.35f, 0.2f, 0.32f),
                BorderColor = new Vector4(1f, 0.35f, 0.2f, 1f),
                BorderWidth = 0.04f
            });
        }

        int count = Math.Min(_runtime.Nav.PathXcm.Length, _runtime.Nav.PathZcm.Length);
        for (int i = 0; i < count - 1; i++)
        {
            Vector3 a = WorldUnits.WorldCmToVisualMeters(
                _runtime.Nav.PathXcm[i],
                _runtime.Nav.PathZcm[i],
                OverlayY + 0.08f);
            Vector3 b = WorldUnits.WorldCmToVisualMeters(
                _runtime.Nav.PathXcm[i + 1],
                _runtime.Nav.PathZcm[i + 1],
                OverlayY + 0.08f);
            Vector2 delta = new(b.X - a.X, b.Z - a.Z);
            float length = delta.Length();
            if (length <= 0.001f)
            {
                continue;
            }

            ground.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Line,
                Center = a,
                Length = length,
                Width = 0.22f,
                Rotation = WorldPlane2D.FacingRadFromDirection(delta.X, delta.Y),
                FillColor = new Vector4(0.18f, 0.58f, 1f, 0.72f),
                BorderColor = new Vector4(0.85f, 0.95f, 1f, 0.95f),
                BorderWidth = 0.04f
            });
        }
    }

    private void DrawNavTiles(DebugDrawCommandBuffer debug)
    {
        NavQueryServiceRegistry? registry = _engine.GetService(CoreServiceKeys.NavQueryServices);
        if (registry == null)
        {
            return;
        }

        IReadOnlyList<KeyValuePair<NavQueryServiceKey, NavTileStore>> stores = registry.SnapshotStores();
        for (int storeIndex = 0; storeIndex < stores.Count; storeIndex++)
        {
            NavTile[] tiles = stores[storeIndex].Value.SnapshotLoadedTiles();
            for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
            {
                DrawNavTile(debug, tiles[tileIndex]);
            }
        }
    }

    private static void DrawNavTile(DebugDrawCommandBuffer debug, NavTile tile)
    {
        DebugDrawColor color = new(50, 220, 160, 210);
        for (int i = 0; i < tile.TriangleCount; i++)
        {
            AddTriangleEdge(debug, tile, tile.TriA[i], tile.TriB[i], color);
            AddTriangleEdge(debug, tile, tile.TriB[i], tile.TriC[i], color);
            AddTriangleEdge(debug, tile, tile.TriC[i], tile.TriA[i], color);
        }
    }

    private static void AddTriangleEdge(
        DebugDrawCommandBuffer debug,
        NavTile tile,
        int a,
        int b,
        DebugDrawColor color)
    {
        debug.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(
                WorldUnits.CmToM(tile.OriginXcm + tile.VertexXcm[a]),
                WorldUnits.CmToM(tile.OriginZcm + tile.VertexZcm[a])),
            B = new Vector2(
                WorldUnits.CmToM(tile.OriginXcm + tile.VertexXcm[b]),
                WorldUnits.CmToM(tile.OriginZcm + tile.VertexZcm[b])),
            Thickness = 1.5f,
            Color = color
        });
    }
}
