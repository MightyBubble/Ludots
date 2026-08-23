using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Runtime;
using NavDomainShowcaseMod.UI;

namespace NavDomainShowcaseMod.Runtime;

internal sealed class NavDomainAuthoringRuntime
{
    private const int MinVisibleChunkRadius = 2;
    private const int MaxVisibleChunkRadius = 8;
    private const int RetainedChunkMargin = 1;
    private const float NavTileOverlayYOffsetMeters = 0.25f;
    private const int NavTileMeshVertexCapacity = 8192;
    private const int NavTileMeshIndexCapacity = 24576;

    private static readonly PresentationLodProfile MeshLodProfile =
        new(
            new PresentationLodEntry(maxDistanceCm: 40000f, minScreenCoverage01: 0.002f),
            new PresentationLodEntry(maxDistanceCm: 120000f, minScreenCoverage01: 0.0005f),
            new PresentationLodEntry(maxDistanceCm: 240000f, minScreenCoverage01: 0.0001f));

    private readonly Dictionary<long, RenderedMesh> _terrainChunks = new();
    private readonly Dictionary<long, RenderedMesh> _navTiles = new();
    private readonly Dictionary<long, ProceduralMeshAssetData> _navTileMeshAssets = new();

    private readonly LogicTerrainDocument _document;
    private readonly NavBakeSession _bakeSession;
    private NavDomainPanelController _panelController;
    private bool _panelDirty = true;
    private bool _active;
    private bool _renderDebugCaptured;
    private bool _cameraPrimed;
    private bool _previousDrawTerrain = true;
    private bool _previousDrawPrimitives = true;
    private bool _previousDrawDebugDraw = true;
    private bool _previousDrawSkiaUi = true;
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
    private int _meshPerformerDefinitionId;
    private string _statusText = "Paint terrain with the left mouse button; then estimate and bake dirty tiles.";

    public NavDomainAuthoringRuntime(LogicTerrainDocument document)
    {
        _document = document;
        _bakeSession = new NavBakeSession(document);
        _panelController = new NavDomainPanelController(this, document, _bakeSession);
    }

    public LogicTerrainDocument Document => _document;

    public NavBakeSession BakeSession => _bakeSession;

    public bool TryGetPointerWorld(out WorldCmInt2 worldCm)
    {
        worldCm = _pointerWorldCm;
        return _pointerWorldValid;
    }

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

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
        if (NavDomainShowcaseIds.IsEditorMap(activeMapId))
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
        if (NavDomainShowcaseIds.IsEditorMap(mapId.Value))
        {
            Deactivate(engine);
        }

        return Task.CompletedTask;
    }

    public void Update(GameEngine engine)
    {
        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
        if (!NavDomainShowcaseIds.IsEditorMap(activeMapId))
        {
            Deactivate(engine);
            return;
        }

        Activate(engine);
        TrackViewport(engine);
        UpdateVisibleChunkWindow(engine);
        bool pointerChanged = UpdatePointerState(engine);
        HandleWorldPainting(engine);

        SyncTerrainChunkMeshes(engine);
        SyncNavTileMeshes(engine);
        EmitBrushOverlay(engine);

        if (pointerChanged)
        {
            _panelDirty = true;
        }

        if (_panelDirty)
        {
            RefreshPanel(engine);
        }
    }

    public void SetBrushMode(TerrainBrushMode mode)
    {
        _document.SetBrushMode(mode);
        _panelDirty = true;
    }

    public void AdjustBrushRadius(float deltaMeters)
    {
        _document.AdjustBrushRadius(deltaMeters);
        _panelDirty = true;
    }

    public void ResetTerrain()
    {
        _document.Reset();
        _statusText = "Logic terrain reset.";
        _panelDirty = true;
    }

    public void EstimateDirty()
    {
        try
        {
            _bakeSession.EstimateDirty(CurrentEngine!);
            _statusText = "Estimate refreshed.";
        }
        catch (Exception ex)
        {
            _statusText = $"Estimate failed: {ex.Message}";
        }

        _panelDirty = true;
    }

    public void BakeDirty()
    {
        RunBake(dirtyOnly: true);
    }

    public void BakeAll()
    {
        RunBake(dirtyOnly: false);
    }

    private GameEngine? CurrentEngine { get; set; }

    private void RunBake(bool dirtyOnly)
    {
        if (CurrentEngine == null)
        {
            return;
        }

        try
        {
            NavBakeSessionOutcome outcome = dirtyOnly
                ? _bakeSession.BakeDirty(CurrentEngine)
                : _bakeSession.BakeAll(CurrentEngine);
            _statusText = outcome.FailCount > 0
                ? $"Bake failed ({outcome.FailMessage})"
                : outcome.OkCount + outcome.EmptyCount == 0
                    ? outcome.FailMessage
                    : $"Baked ok={outcome.OkCount} empty={outcome.EmptyCount} tris={outcome.TriangleCount} in {outcome.ElapsedMs:0} ms.";
        }
        catch (Exception ex)
        {
            _statusText = $"Bake failed: {ex.Message}";
        }

        _panelDirty = true;
    }

    private void Activate(GameEngine engine)
    {
        CurrentEngine = engine;
        if (!_active)
        {
            _active = true;
            ApplyEditorRenderDefaults(engine);
            PrimeCamera(engine);
        }

        ClampCameraDistance(engine);
    }

    private void Deactivate(GameEngine engine)
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _cameraPrimed = false;
        CurrentEngine = null;
        ClearRenderedMeshes(engine, _terrainChunks);
        ClearRenderedMeshes(engine, _navTiles);
        RestoreRenderDebug(engine);
        ClearPanelIfOwned(engine);
    }

    private void RefreshPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        float viewportWidth = _lastViewportWidth > 0f ? _lastViewportWidth : 1280f;
        float viewportHeight = _lastViewportHeight > 0f ? _lastViewportHeight : 720f;
        _panelController.MountOrRefresh(root, engine, BuildPanelState(viewportWidth, viewportHeight));
        root.IsDirty = true;
        _panelDirty = false;
    }

    private NavDomainPanelState BuildPanelState(float viewportWidth, float viewportHeight)
    {
        return new NavDomainPanelState(
            viewportWidth,
            viewportHeight,
            _statusText,
            _document.WidthChunks,
            _document.HeightChunks,
            _document.DirtyChunkCount,
            _document.PaintedChunkCount,
            _document.BrushMode,
            _document.BrushRadiusMeters,
            _bakeSession.LastEstimate,
            _bakeSession.LastOutcome);
    }

    private void ClearPanelIfOwned(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
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

    private void PrimeCamera(GameEngine engine)
    {
        if (_cameraPrimed)
        {
            return;
        }

        float centerXcm = _document.WorldWidthCm * 0.5f;
        float centerZcm = _document.WorldHeightCm * 0.5f;
        engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            TargetCm = new Vector2(centerXcm, centerZcm),
            DistanceCm = Math.Clamp(_document.ChunkWorldSizeCm * 2.2f, 16000f, 60000f),
            Pitch = 42f,
            Yaw = 225f,
            FovYDeg = 50f,
        });

        _cameraPrimed = true;
    }

    private void ClampCameraDistance(GameEngine engine)
    {
        float maxDistanceCm = Math.Clamp(_document.ChunkWorldSizeCm * 3.2f, 22000f, 72000f);
        float minDistanceCm = Math.Max(8000f, maxDistanceCm * 0.2f);
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

    private void TrackViewport(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        bool changed = MathF.Abs(root.Width - _lastViewportWidth) > 0.5f ||
                       MathF.Abs(root.Height - _lastViewportHeight) > 0.5f;
        _lastViewportWidth = root.Width;
        _lastViewportHeight = root.Height;
        if (changed)
        {
            _panelDirty = true;
        }
    }

    private void UpdateVisibleChunkWindow(GameEngine engine)
    {
        int visibleRadius = GetVisibleChunkRadius(engine);
        Vector2 targetCm = engine.GameSession.Camera.State.TargetCm;
        int centerChunkX = Math.Clamp((int)MathF.Round(targetCm.X) / _document.ChunkWorldSizeCm, 0, _document.WidthChunks - 1);
        int centerChunkY = Math.Clamp((int)MathF.Round(targetCm.Y) / _document.ChunkWorldSizeCm, 0, _document.HeightChunks - 1);
        int minChunkX = Math.Max(0, centerChunkX - visibleRadius);
        int maxChunkX = Math.Min(_document.WidthChunks - 1, centerChunkX + visibleRadius);
        int minChunkY = Math.Max(0, centerChunkY - visibleRadius);
        int maxChunkY = Math.Min(_document.HeightChunks - 1, centerChunkY + visibleRadius);
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
        if (changed)
        {
            _panelDirty = true;
        }
    }

    private int GetVisibleChunkRadius(GameEngine engine)
    {
        float chunkSpanCm = _document.ChunkWorldSizeCm;
        float distanceCm = MathF.Max(engine.GameSession.Camera.State.DistanceCm, chunkSpanCm);
        int radiusFromDistance = (int)MathF.Ceiling(distanceCm / chunkSpanCm) + 1;
        int maxRadiusForMap = Math.Max(_document.WidthChunks, _document.HeightChunks) - 1;
        return Math.Clamp(radiusFromDistance, MinVisibleChunkRadius, Math.Min(MaxVisibleChunkRadius, maxRadiusForMap));
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
            return previousValid != _pointerWorldValid;
        }

        _pointerWorldValid = true;
        _pointerWorldCm = worldCm;
        _hoverChunkX = Math.Clamp(worldCm.X / _document.ChunkWorldSizeCm, 0, _document.WidthChunks - 1);
        _hoverChunkY = Math.Clamp(worldCm.Y / _document.ChunkWorldSizeCm, 0, _document.HeightChunks - 1);
        return previousValid != _pointerWorldValid ||
               previousHoverChunkX != _hoverChunkX ||
               previousHoverChunkY != _hoverChunkY;
    }

    private void HandleWorldPainting(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.InputBackend, out IInputBackend input) ||
            !input.GetButton("<Mouse>/LeftButton") ||
            !_pointerWorldValid)
        {
            return;
        }

        int dirtyBefore = _document.DirtyChunkCount;
        _document.PaintWorldCm(_pointerWorldCm.X, _pointerWorldCm.Y);
        if (_document.DirtyChunkCount != dirtyBefore)
        {
            _panelDirty = true;
        }
    }

    private void SyncTerrainChunkMeshes(GameEngine engine)
    {
        if (!TryGetMeshRegistries(engine, out MeshAssetRegistry meshes, out PresentationMaterialRegistry materials, out var stableIds, out PerformerCommandBuffer performerCommands, out PerformerDefinitionRegistry performerDefinitions))
        {
            return;
        }

        for (int chunkY = _visibleMinChunkY; chunkY <= _visibleMaxChunkY; chunkY++)
        {
            for (int chunkX = _visibleMinChunkX; chunkX <= _visibleMaxChunkX; chunkX++)
            {
                if (!_document.TryGetChunkMesh(chunkX, chunkY, out ProceduralMeshAssetData mesh, out _))
                {
                    continue;
                }

                EnsureRenderedMesh(
                    engine,
                    _terrainChunks,
                    meshes,
                    materials,
                    stableIds,
                    performerCommands,
                    performerDefinitions,
                    chunkX,
                    chunkY,
                    $"{NavDomainShowcaseIds.TerrainMeshKeyPrefix}.{chunkX}.{chunkY}",
                    $"{NavDomainShowcaseIds.TerrainPerformerKeyPrefix}.{chunkX}.{chunkY}",
                    mesh,
                    yOffsetMeters: 0f,
                    VisualMobility.Static);
            }
        }

        RemoveOutOfWindowMeshes(engine, _terrainChunks, _visibleMinChunkX, _visibleMaxChunkX, _visibleMinChunkY, _visibleMaxChunkY);
    }

    private void SyncNavTileMeshes(GameEngine engine)
    {
        if (!TryGetMeshRegistries(engine, out MeshAssetRegistry meshes, out PresentationMaterialRegistry materials, out var stableIds, out PerformerCommandBuffer performerCommands, out PerformerDefinitionRegistry performerDefinitions))
        {
            return;
        }

        for (int chunkY = _visibleMinChunkY; chunkY <= _visibleMaxChunkY; chunkY++)
        {
            for (int chunkX = _visibleMinChunkX; chunkX <= _visibleMaxChunkX; chunkX++)
            {
                if (!_bakeSession.TryGetTile(chunkX, chunkY, out NavTile tile))
                {
                    continue;
                }

                ProceduralMeshAssetData mesh = GetOrCreateNavTileMesh(meshes, chunkX, chunkY, tile);
                EnsureRenderedMesh(
                    engine,
                    _navTiles,
                    meshes,
                    materials,
                    stableIds,
                    performerCommands,
                    performerDefinitions,
                    chunkX,
                    chunkY,
                    $"{NavDomainShowcaseIds.NavTileMeshKeyPrefix}.{chunkX}.{chunkY}",
                    $"{NavDomainShowcaseIds.NavTilePerformerKeyPrefix}.{chunkX}.{chunkY}",
                    mesh,
                    yOffsetMeters: NavTileOverlayYOffsetMeters,
                    VisualMobility.Movable);
            }
        }

        RemoveOutOfWindowMeshes(engine, _navTiles, _visibleMinChunkX, _visibleMaxChunkX, _visibleMinChunkY, _visibleMaxChunkY);
    }

    private ProceduralMeshAssetData GetOrCreateNavTileMesh(MeshAssetRegistry meshes, int chunkX, int chunkY, NavTile tile)
    {
        long key = ((long)chunkX << 32) | (uint)chunkY;
        int requiredVerts = Math.Max(1, tile.VertexCount);
        int requiredIndices = Math.Max(3, tile.TriangleCount * 3);
        if (!_navTileMeshAssets.TryGetValue(key, out ProceduralMeshAssetData? mesh) ||
            mesh.VertexCapacity < requiredVerts ||
            mesh.IndexCapacity < requiredIndices)
        {
            mesh = new ProceduralMeshAssetData(
                Math.Max(NavTileMeshVertexCapacity, requiredVerts),
                Math.Max(NavTileMeshIndexCapacity, requiredIndices),
                maxSubmeshCount: 1,
                includeColors32: true);
            _navTileMeshAssets[key] = mesh;
            meshes.Register($"{NavDomainShowcaseIds.NavTileMeshKeyPrefix}.{chunkX}.{chunkY}", MeshAssetDescriptor.Procedural(id: 0, mesh));
        }

        WriteNavTileMesh(mesh, tile);
        mesh.Commit(
            vertexCount: requiredVerts,
            indexCount: requiredIndices,
            submeshes: stackalloc[] { new ProceduralSubmeshDescriptor(0, requiredIndices, 1) },
            localBounds: BuildNavTileBounds(tile),
            usageHint: ProceduralMeshUsageHint.Dynamic);
        return mesh;
    }

    private static ProceduralMeshBounds BuildNavTileBounds(NavTile tile)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < tile.VertexCount; i++)
        {
            float x = tile.VertexXcm[i] * 0.01f;
            float y = tile.VertexYcm[i] * 0.01f;
            float z = tile.VertexZcm[i] * 0.01f;
            minX = MathF.Min(minX, x);
            maxX = MathF.Max(maxX, x);
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
            minZ = MathF.Min(minZ, z);
            maxZ = MathF.Max(maxZ, z);
        }

        var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
        var extents = new Vector3(
            MathF.Max(0.5f, (maxX - minX) * 0.5f),
            MathF.Max(0.5f, (maxY - minY) * 0.5f),
            MathF.Max(0.5f, (maxZ - minZ) * 0.5f));
        return new ProceduralMeshBounds(center, extents);
    }

    private static void WriteNavTileMesh(ProceduralMeshAssetData mesh, NavTile tile)
    {
        for (int i = 0; i < tile.VertexCount; i++)
        {
            int offset = i * 3;
            mesh.Positions[offset + 0] = tile.VertexXcm[i] * 0.01f;
            mesh.Positions[offset + 1] = tile.VertexYcm[i] * 0.01f;
            mesh.Positions[offset + 2] = tile.VertexZcm[i] * 0.01f;
            mesh.Normals[offset + 0] = 0f;
            mesh.Normals[offset + 1] = 1f;
            mesh.Normals[offset + 2] = 0f;

            int tangentOffset = i * 4;
            mesh.Tangents[tangentOffset + 0] = 1f;
            mesh.Tangents[tangentOffset + 1] = 0f;
            mesh.Tangents[tangentOffset + 2] = 0f;
            mesh.Tangents[tangentOffset + 3] = 1f;

            int uvOffset = i * 2;
            mesh.Uv0[uvOffset + 0] = 0f;
            mesh.Uv0[uvOffset + 1] = 0f;

            int colorOffset = i * 4;
            mesh.Colors32![colorOffset + 0] = 64;
            mesh.Colors32![colorOffset + 1] = 200;
            mesh.Colors32![colorOffset + 2] = 224;
            mesh.Colors32![colorOffset + 3] = 190;
        }

        for (int i = 0; i < tile.TriangleCount; i++)
        {
            mesh.Indices[(i * 3) + 0] = tile.TriA[i];
            mesh.Indices[(i * 3) + 1] = tile.TriB[i];
            mesh.Indices[(i * 3) + 2] = tile.TriC[i];
        }
    }

    private bool TryGetMeshRegistries(
        GameEngine engine,
        out MeshAssetRegistry meshes,
        out PresentationMaterialRegistry materials,
        out PresentationStableIdAllocator stableIds,
        out PerformerCommandBuffer performerCommands,
        out PerformerDefinitionRegistry performerDefinitions)
    {
        meshes = null!;
        materials = null!;
        stableIds = null!;
        performerCommands = null!;
        performerDefinitions = null!;
        return engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out meshes) &&
               engine.TryGetService(CoreServiceKeys.PresentationMaterialRegistry, out materials) &&
               engine.TryGetService(CoreServiceKeys.PresentationStableIdAllocator, out stableIds) &&
               engine.TryGetService(CoreServiceKeys.PerformerCommandBuffer, out performerCommands) &&
               engine.TryGetService(CoreServiceKeys.PerformerDefinitionRegistry, out performerDefinitions);
    }

    private void EnsureRenderedMesh(
        GameEngine engine,
        Dictionary<long, RenderedMesh> rendered,
        MeshAssetRegistry meshes,
        PresentationMaterialRegistry materials,
        PresentationStableIdAllocator stableIds,
        PerformerCommandBuffer performerCommands,
        PerformerDefinitionRegistry performerDefinitions,
        int chunkX,
        int chunkY,
        string meshKey,
        string performerKey,
        ProceduralMeshAssetData mesh,
        float yOffsetMeters,
        VisualMobility mobility)
    {
        long key = ((long)chunkX << 32) | (uint)chunkY;
        if (rendered.TryGetValue(key, out RenderedMesh existing) && engine.World.IsAlive(existing.Entity))
        {
            return;
        }

        rendered.Remove(key);

        int meshAssetId = meshes.Register(meshKey, MeshAssetDescriptor.Procedural(id: 0, mesh));
        int materialAssetId = materials.GetId(PresentationMaterialRegistry.DefaultSurfaceKey);
        if (materialAssetId <= 0)
        {
            throw new InvalidOperationException(
                $"NavDomainShowcaseMod requires material '{PresentationMaterialRegistry.DefaultSurfaceKey}' to be registered.");
        }

        int templateId = ResolveMeshPerformerDefinitionId(performerDefinitions);
        int definitionId = RegisterMeshPerformerDefinition(performerDefinitions, templateId, performerKey, meshAssetId, materialAssetId, mobility);
        int stableId = stableIds.Allocate();

        Vector3 centerMeters = ResolveChunkCenterMeters(chunkX, chunkY, yOffsetMeters);
        WorldPositionCm worldPosition = WorldPositionCm.FromCmFloat(centerMeters.X * 100f, centerMeters.Z * 100f);
        Entity entity = engine.World.Create(
            worldPosition,
            new PreviousWorldPositionCm { Value = worldPosition.Value },
            new PresentationStableId { Value = stableId },
            new VisualTransform
            {
                Position = new Vector3(0f, centerMeters.Y, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            },
            PresentationLocalBounds.Create(mesh.LocalBounds.Center, mesh.LocalBounds.Extents),
            MeshLodProfile,
            new CullState
            {
                IsVisible = true,
                LOD = LODLevel.High,
            });

        int scopeId = ComposeScopeId(meshKey);
        if (!performerCommands.TryAdd(new PerformerCommand
        {
            CommandKind = PerformerCommandKind.CreatePerformer,
            PerformerDefinitionId = definitionId,
            ScopeTag = scopeId,
            Source = entity,
            AnchorKind = PresentationAnchorKind.Entity,
        }))
        {
            throw new InvalidOperationException("NavDomainShowcaseMod failed to queue mesh performer creation.");
        }

        rendered.Add(key, new RenderedMesh(chunkX, chunkY, entity, scopeId));
    }

    private int ResolveMeshPerformerDefinitionId(PerformerDefinitionRegistry performerDefinitions)
    {
        if (_meshPerformerDefinitionId > 0)
        {
            return _meshPerformerDefinitionId;
        }

        _meshPerformerDefinitionId = performerDefinitions.GetId(NavDomainShowcaseIds.MeshPerformerId);
        if (_meshPerformerDefinitionId <= 0)
        {
            throw new InvalidOperationException(
                $"Performer '{NavDomainShowcaseIds.MeshPerformerId}' is required by NavDomainShowcaseMod.");
        }

        return _meshPerformerDefinitionId;
    }

    private static int RegisterMeshPerformerDefinition(
        PerformerDefinitionRegistry performerDefinitions,
        int templateId,
        string definitionKey,
        int meshAssetId,
        int materialAssetId,
        VisualMobility mobility)
    {
        PerformerDefinition template = performerDefinitions.Get(templateId);
        var definition = new PerformerDefinition
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
            Rules = CloneArray(template.Rules),
            Bindings = CloneArray(template.Bindings),
            Behaviors = CloneBehaviorsWithMobility(template.Behaviors, mobility),
            ParamDefaults = new[]
            {
                new ParamDefault
                {
                    ParamKey = PerformerParamKeyRegistry.Register(NavDomainShowcaseIds.MeshAssetParamKey),
                    Lane = ParamLane.Int,
                    IntValue = meshAssetId,
                },
                new ParamDefault
                {
                    ParamKey = PerformerParamKeyRegistry.Register(NavDomainShowcaseIds.MeshMaterialParamKey),
                    Lane = ParamLane.Int,
                    IntValue = materialAssetId,
                },
            },
        };
        return performerDefinitions.Register(definitionKey, definition);
    }

    private static T[] CloneArray<T>(T[]? source)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<T>();
        }

        var clone = new T[source.Length];
        Array.Copy(source, clone, source.Length);
        return clone;
    }

    private static BehaviorSlot[] CloneBehaviorsWithMobility(BehaviorSlot[]? source, VisualMobility mobility)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<BehaviorSlot>();
        }

        var clone = new BehaviorSlot[source.Length];
        Array.Copy(source, clone, source.Length);
        for (int i = 0; i < clone.Length; i++)
        {
            if (clone[i].Kind != BehaviorKind.AssetBinding)
            {
                continue;
            }

            AssetBindingConfig binding = clone[i].AssetBinding;
            binding.Mobility = mobility;
            clone[i].AssetBinding = binding;
        }

        return clone;
    }

    private void RemoveOutOfWindowMeshes(GameEngine engine, Dictionary<long, RenderedMesh> rendered, int minChunkX, int maxChunkX, int minChunkY, int maxChunkY)
    {
        List<long>? keysToRemove = null;
        foreach ((long key, RenderedMesh mesh) in rendered)
        {
            bool insideWindow = mesh.ChunkX >= minChunkX && mesh.ChunkX <= maxChunkX &&
                                mesh.ChunkY >= minChunkY && mesh.ChunkY <= maxChunkY;
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
            DestroyRenderedMesh(engine, rendered, keysToRemove[i]);
        }
    }

    private void ClearRenderedMeshes(GameEngine engine, Dictionary<long, RenderedMesh> rendered)
    {
        List<long> keys = new(rendered.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            DestroyRenderedMesh(engine, rendered, keys[i]);
        }
    }

    private void DestroyRenderedMesh(GameEngine engine, Dictionary<long, RenderedMesh> rendered, long key)
    {
        RenderedMesh mesh = rendered[key];
        if (engine.TryGetService(CoreServiceKeys.PerformerCommandBuffer, out PerformerCommandBuffer commands))
        {
            commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformerScope,
                ScopeTag = mesh.ScopeId,
            });
        }

        if (engine.World.IsAlive(mesh.Entity))
        {
            engine.World.Destroy(mesh.Entity);
        }

        rendered.Remove(key);
    }

    private void EmitBrushOverlay(GameEngine engine)
    {
        if (!_pointerWorldValid ||
            !engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer overlays))
        {
            return;
        }

        Vector4 borderColor = _document.BrushMode switch
        {
            TerrainBrushMode.Block => new Vector4(0.95f, 0.32f, 0.28f, 0.95f),
            TerrainBrushMode.Unblock => new Vector4(0.42f, 0.85f, 0.36f, 0.95f),
            TerrainBrushMode.LowerHeight => new Vector4(1.00f, 0.60f, 0.24f, 0.95f),
            _ => new Vector4(0.18f, 0.88f, 0.95f, 0.95f)
        };

        float outerRadius = MathF.Max(1f, _document.BrushRadiusMeters);
        float innerRadius = MathF.Max(0.2f, outerRadius - Math.Clamp(outerRadius * 0.08f, 0.2f, 1.2f));
        overlays.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Ring,
            Center = new Vector3(_pointerWorldCm.X * 0.01f, 0.1f, _pointerWorldCm.Y * 0.01f),
            Radius = outerRadius,
            InnerRadius = innerRadius,
            FillColor = new Vector4(borderColor.X, borderColor.Y, borderColor.Z, 0.14f),
            BorderColor = borderColor,
            BorderWidth = 0.05f,
        });

        EmitDirtyChunkOverlays(overlays);
    }

    private void EmitDirtyChunkOverlays(GroundOverlayBuffer overlays)
    {
        for (int chunkY = 0; chunkY < _document.HeightChunks; chunkY++)
        {
            for (int chunkX = 0; chunkX < _document.WidthChunks; chunkX++)
            {
                if (!_document.IsChunkDirty(chunkX, chunkY))
                {
                    continue;
                }

                overlays.TryAdd(new GroundOverlayItem
                {
                    Shape = GroundOverlayShape.Ring,
                    Center = ChunkCenterMeters(chunkX, chunkY, 0.05f),
                    Radius = (_document.ChunkWorldSizeCm * 0.5f * 0.01f) - 0.4f,
                    InnerRadius = (_document.ChunkWorldSizeCm * 0.5f * 0.01f) - 0.9f,
                    FillColor = new Vector4(0.98f, 0.72f, 0.18f, 0.10f),
                    BorderColor = new Vector4(0.98f, 0.72f, 0.18f, 0.9f),
                    BorderWidth = 0.08f,
                });
            }
        }
    }

    private Vector3 ChunkCenterMeters(int chunkX, int chunkY, float yOffset)
    {
        float centerXcm = (chunkX + 0.5f) * _document.ChunkWorldSizeCm;
        float centerZcm = (chunkY + 0.5f) * _document.ChunkWorldSizeCm;
        return new Vector3(centerXcm * 0.01f, yOffset, centerZcm * 0.01f);
    }

    private Vector3 ResolveChunkCenterMeters(int chunkX, int chunkY, float yOffsetMeters)
    {
        return ChunkCenterMeters(chunkX, chunkY, yOffsetMeters);
    }

    private static int ComposeScopeId(string meshKey)
    {
        int scopeId = meshKey.GetHashCode() & int.MaxValue;
        return scopeId == 0 ? 1 : scopeId;
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

    private readonly record struct RenderedMesh(int ChunkX, int ChunkY, Entity Entity, int ScopeId);
}
