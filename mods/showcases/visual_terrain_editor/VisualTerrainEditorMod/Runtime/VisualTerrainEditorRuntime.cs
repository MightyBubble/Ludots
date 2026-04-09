using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Runtime;
using VisualTerrainEditorMod.UI;

namespace VisualTerrainEditorMod.Runtime;

internal sealed class VisualTerrainEditorRuntime
{
    private const string TerrainMeshAssetKeyPrefix = "visual_terrain_editor.runtime_terrain";
    private const int MinVisibleChunkRadius = 4;
    private const int MaxVisibleChunkRadius = 8;
    private const int RetainedChunkMargin = 1;

    private readonly Dictionary<long, RenderedChunk> _renderedChunks = new();

    private VisualTerrainEditorDocument _document;
    private VisualTerrainEditorPanelController _panelController;
    private bool _panelDirty = true;
    private bool _active;
    private bool _renderDebugCaptured;
    private bool _cameraPrimed;
    private bool _previousDrawTerrain = true;
    private bool _previousDrawPrimitives = true;
    private bool _previousDrawDebugDraw = true;
    private bool _previousDrawSkiaUi = true;
    private bool _forceChunkEntityRefresh = true;
    private IVisualHeightmap? _previousVisualHeightmap;
    private VisualTerrainAssetDescriptor? _pendingAssetReplacement;
    private bool _mapDirty = true;
    private string _statusText = "Unsaved map.";
    private string _lastSavedManifestPath = string.Empty;
    private bool _pointerWorldValid;
    private WorldCmInt2 _pointerWorldCm;
    private int _hoverChunkX = -1;
    private int _hoverChunkY = -1;
    private int _visibleCenterChunkX = -1;
    private int _visibleCenterChunkY = -1;
    private int _visibleMinChunkX = -1;
    private int _visibleMaxChunkX = -1;
    private int _visibleMinChunkY = -1;
    private int _visibleMaxChunkY = -1;
    private float _lastViewportWidth = -1f;
    private float _lastViewportHeight = -1f;

    public VisualTerrainEditorRuntime()
    {
        _document = new VisualTerrainEditorDocument(CreatePresetAsset("8K", 32, 32, 50_000, 257, 33));
        _panelController = new VisualTerrainEditorPanelController(this, _document);
    }

    public VisualTerrainEditorDocument Document => _document;

    public bool TryGetHoveredChunk(out int chunkX, out int chunkY)
    {
        chunkX = _hoverChunkX;
        chunkY = _hoverChunkY;
        return _pointerWorldValid && chunkX >= 0 && chunkY >= 0;
    }

    public void GetVisibleChunkWindow(out int centerChunkX, out int centerChunkY, out int minChunkX, out int maxChunkX, out int minChunkY, out int maxChunkY)
    {
        centerChunkX = _visibleCenterChunkX;
        centerChunkY = _visibleCenterChunkY;
        minChunkX = _visibleMinChunkX;
        maxChunkX = _visibleMaxChunkX;
        minChunkY = _visibleMinChunkY;
        maxChunkY = _visibleMaxChunkY;
    }

    public bool TryGetPointerWorld(out WorldCmInt2 worldCm)
    {
        worldCm = _pointerWorldCm;
        return _pointerWorldValid;
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
        if (VisualTerrainEditorIds.IsEditorMap(activeMapId))
        {
            Activate(engine);
        }
        else
        {
            Deactivate(engine);
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        var mapId = context.Get(CoreServiceKeys.MapId);
        if (VisualTerrainEditorIds.IsEditorMap(mapId.Value))
        {
            Deactivate(engine);
        }

        return Task.CompletedTask;
    }

    public void Update(GameEngine engine)
    {
        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
        if (!VisualTerrainEditorIds.IsEditorMap(activeMapId))
        {
            Deactivate(engine);
            return;
        }

        Activate(engine);
        ApplyPendingAssetReplacement(engine);

        bool viewportChanged = TrackViewport(engine);

        int loadedBefore = _document.LoadedChunkCount;
        bool chunkWindowChanged = EnsureChunkWindowLoaded(engine);
        if (loadedBefore != _document.LoadedChunkCount || chunkWindowChanged)
        {
            _panelDirty = true;
        }

        bool pointerChanged = UpdatePointerState(engine);
        if (pointerChanged)
        {
            _panelDirty = true;
        }

        HandleWorldPainting(engine);

        bool terrainChanged = _document.Update();
        SyncRenderedChunks(engine);
        EmitBrushOverlay(engine);

        if (_panelDirty)
        {
            RefreshPanel(engine);
            return;
        }

        if ((viewportChanged || chunkWindowChanged || pointerChanged) &&
            engine.GetService(CoreServiceKeys.UIRoot) is UIRoot dynamicRoot)
        {
            dynamicRoot.IsDirty = true;
        }
    }

    public void SetViewMode(TerrainViewMode viewMode)
    {
        _document.SetViewMode(viewMode);
        _panelDirty = true;
    }

    public void SetBrushMode(bool lower)
    {
        _document.SetBrushMode(lower);
        _panelDirty = true;
    }

    public void AdjustBrushRadius(float deltaMeters)
    {
        _document.AdjustBrushRadius(deltaMeters);
        _panelDirty = true;
    }

    public void AdjustScale(float delta)
    {
        _document.AdjustScale(delta);
        MarkMapDirty("Terrain parameters changed.");
        _panelDirty = true;
    }

    public void AdjustStrength(float delta)
    {
        _document.AdjustStrength(delta);
        MarkMapDirty("Terrain parameters changed.");
        _panelDirty = true;
    }

    public void AdjustGullyWeight(float delta)
    {
        _document.AdjustGullyWeight(delta);
        MarkMapDirty("Terrain parameters changed.");
        _panelDirty = true;
    }

    public void AdjustDetail(float delta)
    {
        _document.AdjustDetail(delta);
        MarkMapDirty("Terrain parameters changed.");
        _panelDirty = true;
    }

    public void AdjustOctaves(int delta)
    {
        _document.AdjustOctaves(delta);
        MarkMapDirty("Terrain parameters changed.");
        _panelDirty = true;
    }

    public void ResetDocument()
    {
        _document.Reset();
        _forceChunkEntityRefresh = true;
        MarkMapDirty("Terrain reset.");
        _panelDirty = true;
    }

    public void CreateSmallMap()
    {
        QueueNewMapAsset(CreatePresetAsset("4K", 16, 16, 50_000, 257, 33), "Created new 4K world.");
    }

    public void CreateMediumMap()
    {
        QueueNewMapAsset(CreatePresetAsset("8K", 32, 32, 50_000, 257, 33), "Created new 8K world.");
    }

    public void CreateLargeMap()
    {
        QueueNewMapAsset(CreatePresetAsset("16K", 64, 64, 50_000, 257, 33), "Created new 16K world.");
    }

    public void SaveCurrentMap()
    {
        try
        {
            _document.Update();
            _lastSavedManifestPath = VisualTerrainEditorPersistence.SaveMap(_document);
            _mapDirty = false;
            _statusText = $"Saved map to {_lastSavedManifestPath}";
        }
        catch (Exception ex)
        {
            _statusText = $"Save failed: {ex.Message}";
        }

        _panelDirty = true;
    }

    public VisualTerrainEditorPanelState BuildPanelState()
    {
        return BuildPanelState(
            _lastViewportWidth > 0f ? _lastViewportWidth : 1280f,
            _lastViewportHeight > 0f ? _lastViewportHeight : 720f);
    }

    public VisualTerrainEditorPanelState BuildPanelState(float viewportWidth, float viewportHeight)
    {
        VisualTerrainAssetDescriptor asset = _document.Asset;
        return new VisualTerrainEditorPanelState(
            asset.Id,
            asset.DisplayName,
            viewportWidth,
            viewportHeight,
            _lastSavedManifestPath,
            _statusText,
            _mapDirty,
            asset.Binding.Kind.ToString(),
            asset.ChunkColumns,
            asset.ChunkRows,
            _document.LoadedChunkCount,
            _document.EditedChunkCount,
            asset.SampleColumns,
            asset.SampleRows,
            asset.SamplesPerChunkColumn,
            asset.SamplesPerChunkRow,
            asset.RenderColumns,
            asset.RenderRows,
            asset.RenderColumnsPerChunk,
            asset.RenderRowsPerChunk,
            asset.Bounds.Width * 0.01f,
            asset.Bounds.Height * 0.01f,
            _document.ViewMode,
            _document.LowerBrush,
            _document.BrushRadiusMeters,
            _document.Scale,
            _document.Strength,
            _document.GullyWeight,
            _document.Detail,
            _document.Octaves);
    }

    private void Activate(GameEngine engine)
    {
        _active = true;
        ApplyEditorRenderDefaults(engine);
        InstallHeightmap(engine);
        PrimeCamera(engine);
        ClampCameraDistance(engine);
    }

    private void RefreshPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _panelController.MountOrRefresh(root, engine, BuildPanelState(root.Width, root.Height));
        root.IsDirty = true;
        _panelDirty = false;
    }

    private void Deactivate(GameEngine engine)
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _cameraPrimed = false;
        ClearRenderedChunks(engine);
        RestoreVisualHeightmap(engine);
        RestoreRenderDebug(engine);
        ClearPanelIfOwned(engine);
    }

    private void ClearPanelIfOwned(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private void InstallHeightmap(GameEngine engine)
    {
        IVisualHeightmap current = _document.HeightmapRuntime;
        if (engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap existing) &&
            !ReferenceEquals(existing, current))
        {
            _previousVisualHeightmap = existing;
        }
        else
        {
            _previousVisualHeightmap = null;
        }

        engine.SetService(CoreServiceKeys.VisualHeightmap, current);
    }

    private void RestoreVisualHeightmap(GameEngine engine)
    {
        if (_previousVisualHeightmap != null)
        {
            engine.SetService(CoreServiceKeys.VisualHeightmap, _previousVisualHeightmap);
            _previousVisualHeightmap = null;
            return;
        }

        if (engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap existing) &&
            ReferenceEquals(existing, _document.HeightmapRuntime))
        {
            engine.RemoveService(CoreServiceKeys.VisualHeightmap);
        }
    }

    private void PrimeCamera(GameEngine engine)
    {
        if (_cameraPrimed)
        {
            return;
        }

        float distanceCm = GetPreferredCameraDistanceCm();
        engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            TargetCm = Vector2.Zero,
            DistanceCm = distanceCm,
            Pitch = 42f,
            Yaw = 225f,
            FovYDeg = 50f,
        });

        _cameraPrimed = true;
    }

    private void ClampCameraDistance(GameEngine engine)
    {
        float maxDistanceCm = GetMaxCameraDistanceCm();
        float minDistanceCm = Math.Max(8_000f, maxDistanceCm * 0.2f);
        var state = engine.GameSession.Camera.State;
        float clampedDistanceCm = Math.Clamp(state.DistanceCm, minDistanceCm, maxDistanceCm);
        if (MathF.Abs(clampedDistanceCm - state.DistanceCm) <= 1f)
        {
            return;
        }

        engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            TargetCm = state.TargetCm,
            DistanceCm = clampedDistanceCm,
            Pitch = state.Pitch,
            Yaw = state.Yaw,
            FovYDeg = state.FovYDeg,
        });
    }

    private void QueueNewMapAsset(VisualTerrainAssetDescriptor asset, string statusText)
    {
        _pendingAssetReplacement = asset;
        _lastSavedManifestPath = string.Empty;
        _mapDirty = true;
        _statusText = statusText;
        _panelDirty = true;
    }

    private void ApplyPendingAssetReplacement(GameEngine engine)
    {
        if (_pendingAssetReplacement == null)
        {
            return;
        }

        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }

        ClearRenderedChunks(engine);
        _document.Dispose();
        _document = new VisualTerrainEditorDocument(_pendingAssetReplacement);
        _panelController = new VisualTerrainEditorPanelController(this, _document);
        _pendingAssetReplacement = null;
        _forceChunkEntityRefresh = true;
        _cameraPrimed = false;

        InstallHeightmap(engine);
        PrimeCamera(engine);
        _panelDirty = true;
    }

    private bool EnsureChunkWindowLoaded(GameEngine engine)
    {
        int visibleRadius = GetVisibleChunkRadius(engine);
        int retainedRadius = visibleRadius + RetainedChunkMargin;
        GetCameraChunkWindow(engine, visibleRadius, out int centerChunkX, out int centerChunkY, out int minChunkX, out int maxChunkX, out int minChunkY, out int maxChunkY);
        bool changed = centerChunkX != _visibleCenterChunkX ||
                       centerChunkY != _visibleCenterChunkY ||
                       minChunkX != _visibleMinChunkX ||
                       maxChunkX != _visibleMaxChunkX ||
                       minChunkY != _visibleMinChunkY ||
                       maxChunkY != _visibleMaxChunkY;
        _visibleCenterChunkX = centerChunkX;
        _visibleCenterChunkY = centerChunkY;
        _visibleMinChunkX = minChunkX;
        _visibleMaxChunkX = maxChunkX;
        _visibleMinChunkY = minChunkY;
        _visibleMaxChunkY = maxChunkY;
        _document.EnsureChunkWindowLoaded(centerChunkX, centerChunkY, retainedRadius);
        _document.PruneUneditedChunksOutsideWindow(centerChunkX, centerChunkY, retainedRadius);
        return changed;
    }

    private bool TrackViewport(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return false;
        }

        bool changed = MathF.Abs(root.Width - _lastViewportWidth) > 0.5f ||
                       MathF.Abs(root.Height - _lastViewportHeight) > 0.5f;
        _lastViewportWidth = root.Width;
        _lastViewportHeight = root.Height;
        return changed;
    }

    private bool UpdatePointerState(GameEngine engine)
    {
        bool previousValid = _pointerWorldValid;
        int previousHoverChunkX = _hoverChunkX;
        int previousHoverChunkY = _hoverChunkY;

        _pointerWorldValid = false;
        _hoverChunkX = -1;
        _hoverChunkY = -1;

        if (!engine.TryGetService(CoreServiceKeys.InputBackend, out IInputBackend input))
        {
            return previousValid != _pointerWorldValid;
        }

        Vector2 mousePosition = input.GetMousePosition();
        if (mousePosition.X < 0f || mousePosition.Y < 0f || IsPointerOverUi(engine, mousePosition))
        {
            return previousValid != _pointerWorldValid ||
                   previousHoverChunkX != _hoverChunkX ||
                   previousHoverChunkY != _hoverChunkY;
        }

        if (!AuthoritativeGroundPointerHelper.TryResolveFromScreen(engine.GlobalContext, mousePosition, out WorldCmInt2 worldCm))
        {
            return previousValid != _pointerWorldValid ||
                   previousHoverChunkX != _hoverChunkX ||
                   previousHoverChunkY != _hoverChunkY;
        }

        _pointerWorldValid = true;
        _pointerWorldCm = worldCm;
        _hoverChunkX = WorldToChunkX(_document.Asset, worldCm.X);
        _hoverChunkY = WorldToChunkY(_document.Asset, worldCm.Y);
        return previousValid != _pointerWorldValid ||
               previousHoverChunkX != _hoverChunkX ||
               previousHoverChunkY != _hoverChunkY;
    }

    private void SyncRenderedChunks(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry registry) ||
            !engine.TryGetService(CoreServiceKeys.PresentationStableIdAllocator, out var stableIds))
        {
            return;
        }

        if (_forceChunkEntityRefresh)
        {
            ClearRenderedChunks(engine);
            _forceChunkEntityRefresh = false;
        }

        int visibleRadius = GetVisibleChunkRadius(engine);
        GetCameraChunkWindow(engine, visibleRadius, out _, out _, out int minChunkX, out int maxChunkX, out int minChunkY, out int maxChunkY);

        for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
        {
            for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                if (!_document.TryGetChunkRuntimeMesh(chunkX, chunkY, out RuntimeMeshAssetData runtimeMesh))
                {
                    continue;
                }

                long key = GraphChunkKey.Pack(chunkX, chunkY);
                if (_renderedChunks.TryGetValue(key, out RenderedChunk existing))
                {
                    if (engine.World.IsAlive(existing.Entity))
                    {
                        continue;
                    }

                    _renderedChunks.Remove(key);
                }

                string meshKey = $"{TerrainMeshAssetKeyPrefix}.{chunkX}.{chunkY}";
                int meshAssetId = registry.Register(meshKey, MeshAssetDescriptor.Runtime(id: 0, runtimeMesh));
                int stableId = stableIds.Allocate();
                Entity entity = engine.World.Create(
                    new PresentationStableId { Value = stableId },
                    new VisualTransform
                    {
                        Position = Vector3.Zero,
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One,
                    },
                    VisualRuntimeState.Create(
                        meshAssetId,
                        materialId: 0,
                        baseScale: 1f,
                        renderPath: VisualRenderPath.StaticMesh,
                        mobility: VisualMobility.Static));

                _renderedChunks.Add(key, new RenderedChunk(chunkX, chunkY, entity));
            }
        }

        List<long>? keysToRemove = null;
        foreach ((long key, RenderedChunk chunk) in _renderedChunks)
        {
            bool insideWindow = chunk.ChunkX >= minChunkX &&
                                chunk.ChunkX <= maxChunkX &&
                                chunk.ChunkY >= minChunkY &&
                                chunk.ChunkY <= maxChunkY;
            if (insideWindow)
            {
                continue;
            }

            keysToRemove ??= new List<long>();
            keysToRemove.Add(key);
        }

        if (keysToRemove == null)
        {
            return;
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            long key = keysToRemove[i];
            RenderedChunk rendered = _renderedChunks[key];
            if (engine.World.IsAlive(rendered.Entity))
            {
                engine.World.Destroy(rendered.Entity);
            }

            _renderedChunks.Remove(key);
        }
    }

    private void ClearRenderedChunks(GameEngine engine)
    {
        foreach (RenderedChunk rendered in _renderedChunks.Values)
        {
            if (engine.World.IsAlive(rendered.Entity))
            {
                engine.World.Destroy(rendered.Entity);
            }
        }

        _renderedChunks.Clear();
    }

    private void EmitBrushOverlay(GameEngine engine)
    {
        if (!_pointerWorldValid ||
            !engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer overlays) ||
            !_document.HeightmapRuntime.TrySampleHeightCm(_pointerWorldCm.X, _pointerWorldCm.Y, out float heightCm))
        {
            return;
        }

        Vector4 borderColor = _document.LowerBrush
            ? new Vector4(1.00f, 0.44f, 0.28f, 0.95f)
            : new Vector4(0.18f, 0.88f, 0.95f, 0.95f);

        float outerRadius = MathF.Max(1f, _document.BrushRadiusMeters);
        float innerRadius = MathF.Max(0.2f, outerRadius - Math.Clamp(outerRadius * 0.08f, 0.20f, 1.20f));
        overlays.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Ring,
            Center = new Vector3(
                _pointerWorldCm.X * 0.01f,
                (heightCm * 0.01f) + 0.06f,
                _pointerWorldCm.Y * 0.01f),
            Radius = outerRadius,
            InnerRadius = innerRadius,
            FillColor = new Vector4(borderColor.X, borderColor.Y, borderColor.Z, 0.18f),
            BorderColor = borderColor,
            BorderWidth = 0.05f,
        });
    }

    private void HandleWorldPainting(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.InputBackend, out IInputBackend input) ||
            !input.GetButton("<Mouse>/LeftButton") ||
            !_pointerWorldValid)
        {
            return;
        }

        int editedChunkCountBefore = _document.EditedChunkCount;
        bool statusChanged = !string.Equals(_statusText, "Terrain painted.", StringComparison.Ordinal);
        _document.PaintWorld(_pointerWorldCm.X, _pointerWorldCm.Y);
        MarkMapDirty("Terrain painted.");
        if (statusChanged || _document.EditedChunkCount != editedChunkCountBefore)
        {
            _panelDirty = true;
        }
    }

    private static bool IsPointerOverUi(GameEngine engine, Vector2 mousePosition)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root ||
            root.Scene == null ||
            root.Width <= 0f ||
            root.Height <= 0f)
        {
            return false;
        }

        var hitNode = root.Scene.HitTest(mousePosition.X, mousePosition.Y);
        if (hitNode == null)
        {
            return false;
        }

        UiNode? sceneRoot = root.Scene.Root;
        return sceneRoot == null || hitNode.Id != sceneRoot.Id;
    }

    private void ApplyEditorRenderDefaults(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
        {
            return;
        }

        if (!_renderDebugCaptured)
        {
            _previousDrawTerrain = renderDebug.DrawTerrain;
            _previousDrawPrimitives = renderDebug.DrawPrimitives;
            _previousDrawDebugDraw = renderDebug.DrawDebugDraw;
            _previousDrawSkiaUi = renderDebug.DrawSkiaUi;
            _renderDebugCaptured = true;
        }

        renderDebug.DrawTerrain = false;
        renderDebug.DrawPrimitives = true;
        renderDebug.DrawDebugDraw = false;
        renderDebug.DrawSkiaUi = true;
    }

    private void RestoreRenderDebug(GameEngine engine)
    {
        if (!_renderDebugCaptured)
        {
            return;
        }

        if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
        {
            renderDebug.DrawTerrain = _previousDrawTerrain;
            renderDebug.DrawPrimitives = _previousDrawPrimitives;
            renderDebug.DrawDebugDraw = _previousDrawDebugDraw;
            renderDebug.DrawSkiaUi = _previousDrawSkiaUi;
        }

        _renderDebugCaptured = false;
    }

    private void GetCameraChunkWindow(
        GameEngine engine,
        int radius,
        out int centerChunkX,
        out int centerChunkY,
        out int minChunkX,
        out int maxChunkX,
        out int minChunkY,
        out int maxChunkY)
    {
        VisualTerrainAssetDescriptor asset = _document.Asset;
        Vector2 targetCm = engine.GameSession.Camera.State.TargetCm;
        centerChunkX = WorldToChunkX(asset, (int)MathF.Round(targetCm.X));
        centerChunkY = WorldToChunkY(asset, (int)MathF.Round(targetCm.Y));
        minChunkX = Math.Max(0, centerChunkX - radius);
        maxChunkX = Math.Min(asset.ChunkColumns - 1, centerChunkX + radius);
        minChunkY = Math.Max(0, centerChunkY - radius);
        maxChunkY = Math.Min(asset.ChunkRows - 1, centerChunkY + radius);
    }

    private int GetVisibleChunkRadius(GameEngine engine)
    {
        VisualTerrainAssetDescriptor asset = _document.Asset;
        float chunkSpanCm = MathF.Max(asset.ChunkWorldWidthCm, asset.ChunkWorldHeightCm);
        float distanceCm = MathF.Max(engine.GameSession.Camera.State.DistanceCm, chunkSpanCm);
        int radiusFromDistance = (int)MathF.Ceiling(distanceCm / chunkSpanCm) + 1;
        int maxRadiusForMap = Math.Max(asset.ChunkColumns, asset.ChunkRows) - 1;
        return Math.Clamp(radiusFromDistance, MinVisibleChunkRadius, Math.Min(MaxVisibleChunkRadius, maxRadiusForMap));
    }

    private static int WorldToChunkX(VisualTerrainAssetDescriptor asset, int worldXCm)
    {
        int clamped = Math.Clamp(worldXCm, asset.Bounds.Left, asset.Bounds.Right - 1);
        return Math.Clamp((clamped - asset.Bounds.Left) / asset.ChunkWorldWidthCm, 0, asset.ChunkColumns - 1);
    }

    private static int WorldToChunkY(VisualTerrainAssetDescriptor asset, int worldYCm)
    {
        int clamped = Math.Clamp(worldYCm, asset.Bounds.Top, asset.Bounds.Bottom - 1);
        return Math.Clamp((clamped - asset.Bounds.Top) / asset.ChunkWorldHeightCm, 0, asset.ChunkRows - 1);
    }

    private void MarkMapDirty(string statusText)
    {
        _mapDirty = true;
        _statusText = statusText;
    }

    private static VisualTerrainAssetDescriptor CreatePresetAsset(
        string sizeLabel,
        int chunkColumns,
        int chunkRows,
        int chunkWorldSizeCm,
        int samplesPerChunk,
        int renderPerChunk)
    {
        string suffix = sizeLabel.ToLowerInvariant();
        string unique = Guid.NewGuid().ToString("N")[..8];
        string id = $"vtmap_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}_{unique}";
        string displayName = $"Visual Terrain {sizeLabel} {DateTime.Now:HH:mm:ss}";
        int worldWidthCm = checked(chunkColumns * chunkWorldSizeCm);
        int worldHeightCm = checked(chunkRows * chunkWorldSizeCm);

        return new VisualTerrainAssetDescriptor(
            id: id,
            displayName: displayName,
            bounds: new WorldAabbCm(-(worldWidthCm / 2), -(worldHeightCm / 2), worldWidthCm, worldHeightCm),
            chunkColumns: chunkColumns,
            chunkRows: chunkRows,
            samplesPerChunkColumn: samplesPerChunk,
            samplesPerChunkRow: samplesPerChunk,
            renderColumnsPerChunk: renderPerChunk,
            renderRowsPerChunk: renderPerChunk,
            defaultHeight01: 0.45f,
            binding: VisualTerrainBindingDescriptor.None);
    }

    private float GetPreferredCameraDistanceCm()
    {
        return Math.Clamp(_document.Asset.ChunkWorldWidthCm * 1.2f, 16_000f, 60_000f);
    }

    private float GetMaxCameraDistanceCm()
    {
        return Math.Clamp(_document.Asset.ChunkWorldWidthCm * 1.45f, 22_000f, 72_000f);
    }

    private readonly record struct RenderedChunk(int ChunkX, int ChunkY, Entity Entity);
}
