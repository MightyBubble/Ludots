using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Runtime;
using VisualTerrainEditorMod.UI;
using Ludots.Platform.Abstractions;
using Ludots.Core.Client;

namespace VisualTerrainEditorMod.Runtime;

internal sealed class VisualTerrainEditorRuntime
{
    private const string ChunkMeshAssetParamKey = "visual_terrain_editor.chunkMesh.asset";
    private const string ChunkMaterialParamKey = "visual_terrain_editor.chunkMesh.material";
    private const string BrushRaiseOverlayKey = "visual_terrain_editor.brush.raise";
    private const string BrushLowerOverlayKey = "visual_terrain_editor.brush.lower";
    private static readonly int BrushOverlayScope = PresentationWorldFactPublisher.ComposeScope("visual_terrain_editor.brush", 1);
    private const string TerrainMeshAssetKeyPrefix = "visual_terrain_editor.runtime_terrain";
    private const string TerrainChunkPresenterKeyPrefix = "visual_terrain_editor.runtime_chunk";
    private static readonly int DefaultChunkMaterialAssetId = 1;
    private const float ImportedVisualHeightmapDefaultHeight01 = 0.45f;
    private const int MinVisibleChunkRadius = 4;
    private const int MaxVisibleChunkRadius = 16;
    private const int LargeMapChunkCountThreshold = 512;
    private const float LargeMapWorldSpanThresholdCm = 100_000_000f;
    private const int LargeMapVisibleChunkRadius = 14;
    // Overview must engage before the camera pulls back past what the detail window
    // (visibleRadius=1 => a 3-chunk-wide patch) can cover, otherwise there is a dead zone
    // where only an isolated 3x3 detail patch floats in an empty continent-scale view.
    private const float LargeMapOverviewDistanceInChunks = 2.5f;
    private const float StandardMapPreferredCameraDistanceRatio = 0.65f;
    private const float StandardMapMaxPreferredCameraDistanceRatio = 0.85f;
    private const float LargeMapPreferredCameraDistanceRatio = 0.95f;
    private const float LargeMapMaxPreferredCameraDistanceRatio = 1.05f;
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
    private string _previousVisualHeightmapMapId = string.Empty;
    private VisualTerrainAssetDescriptor? _pendingAssetReplacement;
    private bool _mapDirty = true;
    private string _statusText = "Unsaved map.";
    private string _lastSavedManifestPath = string.Empty;
    private string _activeMapId = string.Empty;
    private string _activeDocumentKey = string.Empty;
    private string _activeVisualHeightmapFullPath = string.Empty;
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
    private int _chunkMeshPresenterDefinitionId;
    private string _activeBrushOverlayKey = string.Empty;

    public VisualTerrainEditorRuntime()
    {
        _document = new VisualTerrainEditorDocument(CreatePresetAsset("8K", 32, 32, 50_000, 257, 33), DefaultChunkMaterialAssetId);
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

        MapConfig? mapConfig = engine.CurrentMapSession?.MapConfig;
        if (VisualTerrainEditorIds.IsEditableMap(mapConfig))
        {
            EnsureDocumentForFocusedMap(engine);
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
        if (string.Equals(mapId.Value, _activeMapId, StringComparison.Ordinal))
        {
            Deactivate(engine);
        }

        return Task.CompletedTask;
    }

    public void Update(GameEngine engine)
    {
        MapConfig? mapConfig = engine.CurrentMapSession?.MapConfig;
        if (!VisualTerrainEditorIds.IsEditableMap(mapConfig))
        {
            Deactivate(engine);
            return;
        }

        EnsureDocumentForFocusedMap(engine);
        Activate(engine);
        ApplyPendingAssetReplacement(engine);
        UpdateSharedTerrainOverviewDebug(engine);

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
        PublishBrushWorldFact(engine);

        if (_panelDirty)
        {
            RefreshPanel(engine);
            return;
        }

        if (viewportChanged || chunkWindowChanged || pointerChanged)
        {
            _panelController.InvalidateIfMounted(engine);
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

    public void SetApplyErosion(bool applyErosion)
    {
        _document.SetApplyErosion(applyErosion);
        MarkMapDirty(applyErosion ? "Erosion output enabled." : "Pure visual heightmap output enabled.");
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

    public void AdjustDisplayHeightScale(float delta)
    {
        _document.AdjustDisplayHeightScale(delta);
        _statusText = "Display height contrast changed.";
        _panelDirty = true;
    }

    public void SetDisplayHeightScale(float value)
    {
        _document.SetDisplayHeightScale(value);
        _statusText = $"Vertical exaggeration set to {value:0}x.";
        _panelDirty = true;
    }

    public void AdjustDisplayColorContrast(float delta)
    {
        _document.AdjustDisplayColorContrast(delta);
        _statusText = "Display color contrast changed.";
        _panelDirty = true;
    }

    public void SetDisplayFlatOverview(bool flatOverview)
    {
        _document.SetDisplayFlatOverview(flatOverview);
        _statusText = flatOverview ? "Overview renders as a flat visual heightmap." : "Overview renders with 3D relief.";
        _panelDirty = true;
    }

    public void SetDisplayColorMode(VisualHeightmapRenderColorMode colorMode)
    {
        _document.SetDisplayColorMode(colorMode);
        _statusText = colorMode == VisualHeightmapRenderColorMode.HeightmapGrayscale
            ? "Showing source heightmap contrast for editing."
            : "Showing terrain color ramp.";
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
            if (!string.IsNullOrWhiteSpace(_activeVisualHeightmapFullPath))
            {
                VisualHeightmapAsset asset = _document.CreateVisualHeightmapAsset("edited");
                using FileStream stream = File.Create(_activeVisualHeightmapFullPath);
                VisualHeightmapBinary.Write(stream, asset);
                _lastSavedManifestPath = _activeVisualHeightmapFullPath;
            }
            else
            {
                _lastSavedManifestPath = VisualTerrainEditorPersistence.SaveMap(_document);
            }

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
            _document.ApplyErosion,
            _document.BrushRadiusMeters,
            _document.DisplayHeightScale,
            _document.DisplayColorContrast,
            _document.DisplayFlatOverview,
            _document.DisplayColorMode,
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
        ClampCameraTarget(engine);
        ClampCameraDistance(engine);
    }

    private void EnsureDocumentForFocusedMap(GameEngine engine)
    {
        MapConfig mapConfig = engine.CurrentMapSession?.MapConfig
            ?? throw new InvalidOperationException("Visual terrain editor requires a focused map config.");
        string mapId = engine.CurrentMapSession?.MapId.Value ?? mapConfig.Id;
        string declaredVisualHeightmap = ResolveDeclaredVisualHeightmapAssetPath(mapConfig);
        string documentKey = VisualTerrainEditorIds.IsEditorMap(mapId)
            ? mapId
            : $"{mapId}|{declaredVisualHeightmap}";

        if (string.Equals(_activeDocumentKey, documentKey, StringComparison.Ordinal))
        {
            return;
        }

        if (_active)
        {
            Deactivate(engine);
        }

        VisualTerrainEditorDocument nextDocument;
        string nextSavePath = string.Empty;
        string statusText;
        if (VisualTerrainEditorIds.IsEditorMap(mapId))
        {
            nextDocument = new VisualTerrainEditorDocument(CreatePresetAsset("8K", 32, 32, 50_000, 257, 33), DefaultChunkMaterialAssetId);
            statusText = "Unsaved map.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(declaredVisualHeightmap))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' is tagged '{VisualTerrainEditorIds.EditableMapTag}' but does not declare VisualHeightmapAsset.");
            }

            nextSavePath = ResolveSingleMountedAssetPath(engine, declaredVisualHeightmap, "visual heightmap");
            using FileStream stream = File.OpenRead(nextSavePath);
            VisualHeightmapAsset asset = VisualHeightmapBinary.Read(stream);
            Ludots.Platform.Abstractions.VisualHeightmapRenderProfile mapRenderProfile =
                (mapConfig.VisualHeightmap?.RenderProfile ?? Ludots.Platform.Abstractions.VisualHeightmapRenderProfile.CreateDefault())
                .NormalizeAndValidate();
            nextDocument = VisualTerrainEditorDocument.CreateFromVisualHeightmapAsset(
                id: mapId,
                displayName: $"Visual Heightmap: {mapId}",
                source: asset,
                defaultMaterialAssetId: DefaultChunkMaterialAssetId,
                defaultHeight01: ImportedVisualHeightmapDefaultHeight01,
                renderProfile: mapRenderProfile);
            statusText = $"Loaded visual heightmap from {nextSavePath}";
        }

        ReplaceDocument(engine, nextDocument);
        _activeMapId = mapId;
        _activeDocumentKey = documentKey;
        _activeVisualHeightmapFullPath = nextSavePath;
        _lastSavedManifestPath = nextSavePath;
        _mapDirty = false;
        _statusText = statusText;
        _panelDirty = true;
    }

    private void RefreshPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _panelController.MountOrRefresh(root, engine, BuildPanelState(root.Width, root.Height));
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
        EndBrushOverlay(engine);
        ClearRenderedChunks(engine);
        RestoreVisualHeightmap(engine);
        RestoreRenderDebug(engine);
        ClearPanelIfOwned(engine);
        _activeMapId = string.Empty;
        _activeDocumentKey = string.Empty;
        _activeVisualHeightmapFullPath = string.Empty;
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
            _previousVisualHeightmapMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
        }
        else
        {
            _previousVisualHeightmap = null;
            _previousVisualHeightmapMapId = string.Empty;
        }

        engine.SetService(CoreServiceKeys.VisualHeightmap, current);
    }

    private void RestoreVisualHeightmap(GameEngine engine)
    {
        string currentMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
        if (_previousVisualHeightmap != null &&
            string.Equals(currentMapId, _previousVisualHeightmapMapId, StringComparison.Ordinal) &&
            string.Equals(currentMapId, _activeMapId, StringComparison.Ordinal))
        {
            engine.SetService(CoreServiceKeys.VisualHeightmap, _previousVisualHeightmap);
            _previousVisualHeightmap = null;
            _previousVisualHeightmapMapId = string.Empty;
            return;
        }

        _previousVisualHeightmap = null;
        _previousVisualHeightmapMapId = string.Empty;
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

        Vector2 targetCm = GetWorldCenterCm(_document.Asset);
        float distanceCm = GetPreferredCameraDistanceCm();
        ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ApplyPose(new CameraPoseRequest
        {
            TargetCm = targetCm,
            DistanceCm = distanceCm,
            Pitch = 42f,
            Yaw = 225f,
            FovYDeg = 50f,
        });

        _cameraPrimed = true;
    }

    private void ClampCameraTarget(GameEngine engine)
    {
        var state = ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State;
        Vector2 clampedTargetCm = ResolveCameraTargetInsideBounds(_document.Asset, state.TargetCm);
        if (Vector2.DistanceSquared(clampedTargetCm, state.TargetCm) <= 1f)
        {
            return;
        }

        ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ApplyPose(new CameraPoseRequest
        {
            TargetCm = clampedTargetCm,
            DistanceCm = state.DistanceCm,
            Pitch = state.Pitch,
            Yaw = state.Yaw,
            FovYDeg = state.FovYDeg,
        });
    }

    private void ClampCameraDistance(GameEngine engine)
    {
        float maxDistanceCm = GetMaxCameraDistanceCm();
        float minDistanceCm = GetMinCameraDistanceCm();
        var state = ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State;
        float clampedDistanceCm = Math.Clamp(state.DistanceCm, minDistanceCm, maxDistanceCm);
        if (MathF.Abs(clampedDistanceCm - state.DistanceCm) <= 1f)
        {
            return;
        }

        ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ApplyPose(new CameraPoseRequest
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
        _activeVisualHeightmapFullPath = string.Empty;
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

        ReplaceDocument(engine, new VisualTerrainEditorDocument(_pendingAssetReplacement, DefaultChunkMaterialAssetId));
        _pendingAssetReplacement = null;

        InstallHeightmap(engine);
        PrimeCamera(engine);
        _panelDirty = true;
    }

    private void ReplaceDocument(GameEngine engine, VisualTerrainEditorDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }

        ClearRenderedChunks(engine);
        _document.Dispose();
        _document = document;
        _panelController = new VisualTerrainEditorPanelController(this, _document);
        _forceChunkEntityRefresh = true;
        _cameraPrimed = false;
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
        _document.PruneUneditedChunksOutsideWindow(centerChunkX, centerChunkY, retainedRadius + 1);
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
            !engine.TryGetService(CoreServiceKeys.PresentationMaterialRegistry, out PresentationMaterialRegistry materials) ||
            !engine.TryGetService(CoreServiceKeys.PresentationStableIdAllocator, out var stableIds) ||
            !engine.TryGetService(CoreServiceKeys.PresenterCommandBuffer, out PresenterCommandBuffer presenterCommands) ||
            !engine.TryGetService(CoreServiceKeys.PresenterDefinitionRegistry, out PresenterDefinitionRegistry presenterDefinitions))
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
                if (!_document.TryGetChunkProceduralMesh(chunkX, chunkY, out ProceduralMeshAssetData proceduralMesh))
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
                int meshAssetId = registry.Register(meshKey, MeshAssetDescriptor.Procedural(id: 0, proceduralMesh));
                int materialAssetId = ResolveChunkMaterialAssetId(materials);
                int definitionId = RegisterChunkMeshDefinition(presenterDefinitions, meshAssetId, materialAssetId, proceduralMesh.UsageHint, chunkX, chunkY);
                int stableId = stableIds.Allocate();
                Vector3 chunkCenterMeters = ComputeChunkCenterMeters(_document.Asset, chunkX, chunkY);
                WorldPositionCm worldPosition = WorldPositionCm.FromCmFloat(
                    chunkCenterMeters.X * 100f,
                    chunkCenterMeters.Z * 100f);
                Entity entity = engine.World.Create(
                    worldPosition,
                    new PreviousWorldPositionCm { Value = worldPosition.Value },
                    new PresentationStableId { Value = stableId },
                    new VisualTransform
                    {
                        Position = chunkCenterMeters,
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One,
                    },
                    PresentationLocalBounds.Create(proceduralMesh.LocalBounds.Center, proceduralMesh.LocalBounds.Extents),
                    CreateChunkLodProfile(_document.Asset),
                    new SpatialPartitionExcluded(),
                    new CullState
                    {
                        IsVisible = true,
                        LOD = LODLevel.High,
                    },
                    new PresentationStaticTransform(),
                    new PresentationStaticVisualPending(),
                    new PresentationStaticCullPending());

                int scopeId = ComposeChunkScopeId(chunkX, chunkY);
                if (!presenterCommands.TryAdd(new PresenterCommand
                    {
                        CommandKind = PresenterCommandKind.CreatePresenter,
                        PresenterDefinitionId = definitionId,
                        ScopeTag = scopeId,
                        Source = entity,
                        AnchorKind = PresentationAnchorKind.Entity,
                    }))
                {
                    throw new InvalidOperationException("VisualTerrainEditor failed to queue chunk presenter creation.");
                }

                _renderedChunks.Add(key, new RenderedChunk(chunkX, chunkY, entity, scopeId));
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
            if (engine.TryGetService(CoreServiceKeys.PresenterCommandBuffer, out PresenterCommandBuffer commands))
            {
                commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.DestroyPresenterScope,
                    ScopeTag = rendered.ScopeId,
                });
            }

            if (engine.World.IsAlive(rendered.Entity))
            {
                engine.World.Destroy(rendered.Entity);
            }

            _renderedChunks.Remove(key);
        }
    }

    private static Vector3 ComputeChunkCenterMeters(VisualTerrainAssetDescriptor asset, int chunkX, int chunkY)
    {
        float centerXCm = asset.Bounds.Left + ((chunkX + 0.5f) * asset.ChunkWorldWidthCm);
        float centerYCm = asset.Bounds.Top + ((chunkY + 0.5f) * asset.ChunkWorldHeightCm);
        return new Vector3(centerXCm * 0.01f, 0f, centerYCm * 0.01f);
    }

    private void ClearRenderedChunks(GameEngine engine)
    {
        foreach (RenderedChunk rendered in _renderedChunks.Values)
        {
            if (engine.TryGetService(CoreServiceKeys.PresenterCommandBuffer, out PresenterCommandBuffer commands))
            {
                commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.DestroyPresenterScope,
                    ScopeTag = rendered.ScopeId,
                });
            }

            if (engine.World.IsAlive(rendered.Entity))
            {
                engine.World.Destroy(rendered.Entity);
            }
        }

        _renderedChunks.Clear();
    }

    private void PublishBrushWorldFact(GameEngine engine)
    {
        if (!_pointerWorldValid ||
            !PresentationWorldFactPublisher.TryCreate(engine.GlobalContext, out PresentationWorldFactPublisher facts) ||
            !_document.HeightmapRuntime.TrySampleHeightCm(_pointerWorldCm.X, _pointerWorldCm.Y, out float heightCm))
        {
            EndBrushOverlay(engine);
            return;
        }

        float outerRadius = MathF.Max(1f, _document.BrushRadiusMeters);
        float innerRadius = MathF.Max(0.2f, outerRadius - Math.Clamp(outerRadius * 0.08f, 0.20f, 1.20f));
        string key = _document.LowerBrush ? BrushLowerOverlayKey : BrushRaiseOverlayKey;
        if (!string.IsNullOrEmpty(_activeBrushOverlayKey) &&
            !string.Equals(_activeBrushOverlayKey, key, StringComparison.Ordinal))
        {
            facts.PublishWorldOverlayEnded(_activeBrushOverlayKey, Entity.Null, BrushOverlayScope);
        }

        _activeBrushOverlayKey = key;
        facts.PublishWorldOverlayUpdated(
            key,
            Entity.Null,
            BrushOverlayScope,
            new Vector3(
                _pointerWorldCm.X * 0.01f,
                (heightCm * 0.01f) + 0.06f,
                _pointerWorldCm.Y * 0.01f),
            outerRadius,
            innerRadius,
            borderWidth: 0.05f);
    }

    private void EndBrushOverlay(GameEngine engine)
    {
        if (string.IsNullOrEmpty(_activeBrushOverlayKey) ||
            !PresentationWorldFactPublisher.TryCreate(engine.GlobalContext, out PresentationWorldFactPublisher facts))
        {
            _activeBrushOverlayKey = string.Empty;
            return;
        }

        facts.PublishWorldOverlayEnded(_activeBrushOverlayKey, Entity.Null, BrushOverlayScope);
        _activeBrushOverlayKey = string.Empty;
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

    private void UpdateSharedTerrainOverviewDebug(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
        {
            return;
        }

        renderDebug.DrawTerrain = ShouldUseSharedTerrainOverview(engine);
        renderDebug.DrawPrimitives = true;
        renderDebug.DrawDebugDraw = false;
        renderDebug.DrawSkiaUi = true;
    }

    private bool ShouldUseSharedTerrainOverview(GameEngine engine)
    {
        // 与宿主渲染器同一政策源：全景切换倍数取 map 的 RenderProfile，不再用本地常量。
        MapConfig? mapConfig = engine.CurrentMapSession?.MapConfig;
        float switchChunkSpans = (mapConfig?.VisualHeightmap?.RenderProfile
            ?? Ludots.Platform.Abstractions.VisualHeightmapRenderProfile.CreateDefault())
            .NormalizeAndValidate()
            .OverviewSwitchChunkSpans;
        return ShouldUseSharedTerrainOverview(_document.Asset, ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State.DistanceCm, switchChunkSpans);
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
        Vector2 targetCm = ResolveCameraTargetInsideBounds(asset, ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State.TargetCm);
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
        float distanceCm = MathF.Max(ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State.DistanceCm, chunkSpanCm);
        int radiusFromDistance = (int)MathF.Ceiling(distanceCm / chunkSpanCm) + 1;
        int maxRadiusForMap = Math.Max(asset.ChunkColumns, asset.ChunkRows) - 1;
        if (ShouldUseLargeMapMode(asset))
        {
            return Math.Clamp(LargeMapVisibleChunkRadius, 0, maxRadiusForMap);
        }

        int dynamicMaxRadius = asset.ChunkCount <= LargeMapChunkCountThreshold
            ? maxRadiusForMap
            : Math.Min(MaxVisibleChunkRadius, maxRadiusForMap);
        return Math.Clamp(radiusFromDistance, MinVisibleChunkRadius, dynamicMaxRadius);
    }

    internal static bool ShouldUseLargeMapMode(VisualTerrainAssetDescriptor asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));

        float longestWorldSpanCm = MathF.Max(asset.Bounds.Width, asset.Bounds.Height);
        return asset.ChunkCount > LargeMapChunkCountThreshold ||
            longestWorldSpanCm >= LargeMapWorldSpanThresholdCm;
    }

    internal static bool ShouldUseSharedTerrainOverview(
        VisualTerrainAssetDescriptor asset,
        float cameraDistanceCm,
        float switchChunkSpans = LargeMapOverviewDistanceInChunks)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));

        if (!ShouldUseLargeMapMode(asset))
        {
            return false;
        }

        float chunkSpanCm = MathF.Max(asset.ChunkWorldWidthCm, asset.ChunkWorldHeightCm);
        float overviewDistanceCm = chunkSpanCm * MathF.Max(0.25f, switchChunkSpans);
        return cameraDistanceCm >= overviewDistanceCm;
    }

    internal static float ResolvePreferredCameraDistanceCm(VisualTerrainAssetDescriptor asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));

        float diagonalCm = GetWorldDiagonalCm(asset);
        float chunkSpanCm = GetChunkSpanCm(asset);
        bool largeMap = ShouldUseLargeMapMode(asset);
        float preferredRatio = largeMap
            ? LargeMapPreferredCameraDistanceRatio
            : StandardMapPreferredCameraDistanceRatio;
        float maxRatio = largeMap
            ? LargeMapMaxPreferredCameraDistanceRatio
            : StandardMapMaxPreferredCameraDistanceRatio;
        return Math.Clamp(diagonalCm * preferredRatio, chunkSpanCm * 1.2f, diagonalCm * maxRatio);
    }

    internal static Vector2 ResolveCameraTargetInsideBounds(VisualTerrainAssetDescriptor asset, Vector2 targetCm)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));

        return new Vector2(
            Math.Clamp(targetCm.X, asset.Bounds.Left, asset.Bounds.Right - 1),
            Math.Clamp(targetCm.Y, asset.Bounds.Top, asset.Bounds.Bottom - 1));
    }

    internal static Vector2 GetWorldCenterCm(VisualTerrainAssetDescriptor asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));

        return new Vector2(
            asset.Bounds.Left + (asset.Bounds.Width * 0.5f),
            asset.Bounds.Top + (asset.Bounds.Height * 0.5f));
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

    private static string ResolveDeclaredVisualHeightmapAssetPath(MapConfig mapConfig)
    {
        if (mapConfig == null) throw new ArgumentNullException(nameof(mapConfig));

        string? resolved = NormalizeDeclaredAssetPath(mapConfig.VisualHeightmapAsset);
        if (mapConfig.Boards == null)
        {
            return resolved ?? string.Empty;
        }

        for (int i = 0; i < mapConfig.Boards.Count; i++)
        {
            string? boardAsset = NormalizeDeclaredAssetPath(mapConfig.Boards[i]?.VisualHeightmapAsset);
            if (string.IsNullOrWhiteSpace(boardAsset))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(resolved))
            {
                resolved = boardAsset;
                continue;
            }

            if (!string.Equals(resolved, boardAsset, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Map '{mapConfig.Id}' declares conflicting visual heightmap assets. Editable visual terrain maps must resolve to one shared vhtm.");
            }
        }

        return resolved ?? string.Empty;
    }

    private static string ResolveSingleMountedAssetPath(GameEngine engine, string assetPath, string assetKind)
    {
        if (engine == null) throw new ArgumentNullException(nameof(engine));
        string normalized = NormalizeDeclaredAssetPath(assetPath)
            ?? throw new InvalidOperationException($"{assetKind} asset path must not be empty.");
        var matches = new List<string>();

        void AddMatchIfExists(string uri)
        {
            if (engine.VFS.TryResolveFullPath(uri, out string fullPath) && File.Exists(fullPath))
            {
                matches.Add(fullPath);
            }
        }

        AddMatchIfExists($"Core:{normalized}");
        if (engine.ModLoader?.LoadedModIds != null)
        {
            foreach (string modId in engine.ModLoader.LoadedModIds)
            {
                AddMatchIfExists($"{modId}:{normalized}");
            }
        }

        if (matches.Count == 0)
        {
            throw new FileNotFoundException(
                $"Declared {assetKind} asset '{normalized}' could not be resolved from the mounted Core/mod assets.");
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Declared {assetKind} asset '{normalized}' resolves to multiple mounted files ({string.Join(", ", matches)}).");
        }

        return matches[0];
    }

    private static string? NormalizeDeclaredAssetPath(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        string normalized = assetPath.Replace('\\', '/').Trim();
        while (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(1);
        }

        return normalized.Length == 0 ? null : normalized;
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
        return ResolvePreferredCameraDistanceCm(_document.Asset);
    }

    private float GetMaxCameraDistanceCm()
    {
        float diagonalCm = GetWorldDiagonalCm(_document.Asset);
        float chunkSpanCm = GetChunkSpanCm(_document.Asset);
        return Math.Max(chunkSpanCm * 1.45f, diagonalCm * 1.2f);
    }

    private float GetMinCameraDistanceCm()
    {
        return Math.Max(8_000f, GetChunkSpanCm(_document.Asset) * 0.15f);
    }

    private static PresentationLodProfile CreateChunkLodProfile(VisualTerrainAssetDescriptor asset)
    {
        float diagonalCm = GetWorldDiagonalCm(asset);
        float chunkSpanCm = GetChunkSpanCm(asset);
        float lowDistanceCm = Math.Max(180_000f, diagonalCm * 1.5f);
        float mediumDistanceCm = Math.Max(90_000f, lowDistanceCm * 0.66f);
        float highDistanceCm = Math.Max(30_000f, Math.Min(mediumDistanceCm * 0.5f, chunkSpanCm * 2f));
        return new PresentationLodProfile(
            new PresentationLodEntry(highDistanceCm, minScreenCoverage01: 0.01f),
            new PresentationLodEntry(mediumDistanceCm, minScreenCoverage01: 0.002f),
            new PresentationLodEntry(lowDistanceCm, minScreenCoverage01: 0.0001f));
    }

    private static float GetWorldDiagonalCm(VisualTerrainAssetDescriptor asset)
    {
        float width = asset.Bounds.Width;
        float height = asset.Bounds.Height;
        return MathF.Sqrt((width * width) + (height * height));
    }

    private static float GetChunkSpanCm(VisualTerrainAssetDescriptor asset)
    {
        return MathF.Max(asset.ChunkWorldWidthCm, asset.ChunkWorldHeightCm);
    }

    private static int ResolveChunkMaterialAssetId(PresentationMaterialRegistry materials)
    {
        int materialAssetId = materials.GetId(PresentationMaterialRegistry.DefaultSurfaceKey);
        if (materialAssetId <= 0)
        {
            throw new InvalidOperationException(
                $"VisualTerrainEditor requires material '{PresentationMaterialRegistry.DefaultSurfaceKey}' to be registered.");
        }

        return materialAssetId;
    }

    private int ResolveChunkMeshPresenterDefinitionId(PresenterDefinitionRegistry presenterDefinitions)
    {
        if (_chunkMeshPresenterDefinitionId > 0)
        {
            return _chunkMeshPresenterDefinitionId;
        }

        _chunkMeshPresenterDefinitionId = presenterDefinitions.GetId(VisualTerrainEditorIds.ChunkMeshPresenterId);
        if (_chunkMeshPresenterDefinitionId <= 0)
        {
            throw new InvalidOperationException(
                $"Presenter '{VisualTerrainEditorIds.ChunkMeshPresenterId}' is required by VisualTerrainEditorMod.");
        }

        return _chunkMeshPresenterDefinitionId;
    }

    private int RegisterChunkMeshDefinition(
        PresenterDefinitionRegistry presenterDefinitions,
        int meshAssetId,
        int materialAssetId,
        ProceduralMeshUsageHint usageHint,
        int chunkX,
        int chunkY)
    {
        int templateDefinitionId = ResolveChunkMeshPresenterDefinitionId(presenterDefinitions);
        PresenterDefinition template = presenterDefinitions.Get(templateDefinitionId);
        string definitionKey = $"{TerrainChunkPresenterKeyPrefix}.{chunkX}.{chunkY}";
        var definition = new PresenterDefinition
        {
            Key = definitionKey,
            DefaultLifetime = template.DefaultLifetime,
            PositionOffset = template.PositionOffset,
            PositionYDriftPerSecond = template.PositionYDriftPerSecond,
            AlphaFadeOverLifetime = template.AlphaFadeOverLifetime,
            DefaultColor = template.DefaultColor,
            DefaultFontSize = template.DefaultFontSize,
            WorldTextMode = template.WorldTextMode,
            VisibilityCondition = template.VisibilityCondition,
            Surface = template.Surface,
            Children = template.Children,
            Rules = CloneRules(template.Rules),
            Bindings = CloneBindings(template.Bindings),
            Behaviors = CloneChunkMeshBehaviors(template.Behaviors, usageHint),
            ParamDefaults = new[]
            {
                new ParamDefault
                {
                    ParamKey = PresenterParamKeyRegistry.Register(ChunkMeshAssetParamKey),
                    Lane = ParamLane.Int,
                    IntValue = meshAssetId,
                },
                new ParamDefault
                {
                    ParamKey = PresenterParamKeyRegistry.Register(ChunkMaterialParamKey),
                    Lane = ParamLane.Int,
                    IntValue = materialAssetId,
                },
            },
        };
        return presenterDefinitions.Register(definitionKey, definition);
    }

    private static PresenterRule[] CloneRules(PresenterRule[] source)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<PresenterRule>();
        }

        PresenterRule[] cloned = new PresenterRule[source.Length];
        Array.Copy(source, cloned, source.Length);
        return cloned;
    }

    private static PresenterParamBinding[] CloneBindings(PresenterParamBinding[] source)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<PresenterParamBinding>();
        }

        PresenterParamBinding[] cloned = new PresenterParamBinding[source.Length];
        Array.Copy(source, cloned, source.Length);
        return cloned;
    }

    private static BehaviorSlot[] CloneChunkMeshBehaviors(BehaviorSlot[] source, ProceduralMeshUsageHint usageHint)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<BehaviorSlot>();
        }

        BehaviorSlot[] cloned = new BehaviorSlot[source.Length];
        Array.Copy(source, cloned, source.Length);
        for (int i = 0; i < cloned.Length; i++)
        {
            if (cloned[i].Kind != BehaviorKind.AssetBinding)
            {
                continue;
            }

            AssetBindingConfig binding = cloned[i].AssetBinding;
            binding.Mobility = usageHint == ProceduralMeshUsageHint.Static
                ? VisualMobility.Static
                : VisualMobility.Movable;
            cloned[i].AssetBinding = binding;
        }

        return cloned;
    }

    private static int ComposeChunkScopeId(int chunkX, int chunkY)
    {
        int scopeId = HashCode.Combine(VisualTerrainEditorIds.ChunkMeshPresenterId, chunkX, chunkY) & int.MaxValue;
        return scopeId == 0 ? 1 : scopeId;
    }

    private readonly record struct RenderedChunk(int ChunkX, int ChunkY, Entity Entity, int ScopeId);
}
