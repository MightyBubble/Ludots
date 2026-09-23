using System.Diagnostics;
using System.Numerics;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.AdapterSync;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.WebGpu;

public sealed class MassNavigationWebGpuFrameSource : IWebGpuFrameSource, IDisposable
{
    private readonly GameEngine _engine;
    private readonly WebGpuPresentationHost _presentation;
    private readonly PresentationTimingDiagnostics _timingDiagnostics;
    private readonly PresentationTextGlyphCompiler _textCompiler;
    private readonly GroundOverlayMeshCompiler _groundOverlayCompiler = new();
    private readonly Dictionary<int, WebGpuMeshFrame> _terrainMeshCache = new();
    private readonly Dictionary<int, WebGpuMeshFrame> _worldMeshCache = new();
    private readonly Dictionary<WorldBatchKey, int> _worldBatchSlotByKey = new();
    private WorldBatchState[] _worldBatchStates = Array.Empty<WorldBatchState>();
    private int[] _activeWorldBatchSlots = Array.Empty<int>();
    private WebGpuScreenInstance[] _topMostScreenInstances = Array.Empty<WebGpuScreenInstance>();
    private WebGpuGlyphInstance[] _topMostTextGlyphInstances = Array.Empty<WebGpuGlyphInstance>();
    private readonly WorldAnchoredWebGpuHudStore _worldHudStore;
    private WebGpuMeshFrame[] _visibleTerrainChunks = Array.Empty<WebGpuMeshFrame>();
    private WebGpuMeshFrame _groundOverlayMesh;
    private WebGpuCameraFrame _camera;
    private int _worldBatchSlotCount;
    private int _activeWorldBatchCount;
    private int _topMostScreenInstanceCount;
    private int _topMostTextGlyphInstanceCount;
    private int _terrainChunkCount;
    private uint _frameNumber;
    private WebGpuFrameDiagnostics _diagnostics;
    private WebGpuHudFrameDiagnostics _hudDiagnostics;
    private double _frameBuildMilliseconds;
    private bool _reportedReady;

    public MassNavigationWebGpuFrameSource(
        ResolvedModLoadPlan modPlan,
        string assetsRoot,
        WebGpuFontAtlas textAtlas)
    {
        ArgumentNullException.ThrowIfNull(modPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRoot);
        ArgumentNullException.ThrowIfNull(textAtlas);

        _engine = new GameEngine();
        _engine.InitializeWithConfigPipeline(modPlan, assetsRoot);
        ApplyHostAssets(_engine);
        InstallInput(_engine);
        _presentation = WebGpuPresentationHost.Install(_engine);
        _timingDiagnostics = _engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
            ?? throw new InvalidOperationException("PresentationTimingDiagnostics is required by the WebGPU host.");
        _textCompiler = new PresentationTextGlyphCompiler(
            textAtlas,
            _engine.GetService(CoreServiceKeys.PresentationTextCatalog)
                ?? throw new InvalidOperationException("PresentationTextCatalog is required by the WebGPU host."),
            _engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection)
                ?? throw new InvalidOperationException("PresentationTextLocaleSelection is required by the WebGPU host."),
            _engine.GetService(CoreServiceKeys.PresentationWorldHudStrings)
                ?? throw new InvalidOperationException("WorldHudStringTable is required by the WebGPU host."));
        _worldHudStore = new WorldAnchoredWebGpuHudStore(_textCompiler);
        _engine.Start();
        _engine.LoadStartupMap();

        Console.WriteLine(
            $"[Ludots WebGPU] Formal Mod launch started: map={_engine.MergedConfig.StartupMapId}, mods={string.Join(',', _engine.ModLoader.LoadedModIds)}.");
    }

    public GameEngine Engine => _engine;

    public WebGpuCameraAdapter Camera => _presentation.Camera;

    WebGpuCameraFrame IWebGpuFrameSource.Camera => _camera;

    public WebGpuFrameDiagnostics Diagnostics => _diagnostics;

    public int WorldBatchCount => _activeWorldBatchCount;

    public WebGpuWorldBatchFrame GetWorldBatch(int index)
    {
        if ((uint)index >= (uint)_activeWorldBatchCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int slot = _activeWorldBatchSlots[index];
        WorldBatchState state = _worldBatchStates[slot];
        return new WebGpuWorldBatchFrame(slot + 1, state.Mesh, state.Instances, state.Count);
    }

    public WebGpuMeshFrame GroundOverlayMesh => _groundOverlayMesh;

    public ReadOnlySpan<WebGpuScreenInstance> UnderUiScreenInstances =>
        ReadOnlySpan<WebGpuScreenInstance>.Empty;

    public ReadOnlySpan<WebGpuWorldBarInstance> WorldHudBarInstances =>
        _worldHudStore.BarInstances;

    public ReadOnlySpan<WebGpuWorldGlyphInstance> WorldHudGlyphInstances =>
        _worldHudStore.GlyphInstances;

    public ReadOnlySpan<WebGpuWorldHudAnchor> WorldHudAnchors =>
        _worldHudStore.Anchors;

    public ReadOnlySpan<WebGpuScreenInstance> TopMostScreenInstances =>
        _topMostScreenInstances.AsSpan(0, _topMostScreenInstanceCount);

    public WebGpuFontAtlas TextAtlas => _textCompiler.Atlas;

    public ReadOnlySpan<WebGpuGlyphInstance> UnderUiTextGlyphInstances =>
        ReadOnlySpan<WebGpuGlyphInstance>.Empty;

    public ReadOnlySpan<WebGpuGlyphInstance> TopMostTextGlyphInstances =>
        _topMostTextGlyphInstances.AsSpan(0, _topMostTextGlyphInstanceCount);

    public WebGpuHudUploadHint WorldHudUploadHint
    {
        get
        {
            WebGpuHudUploadPlan plan = _worldHudStore.UploadPlan;
            return new WebGpuHudUploadHint(
                plan.BarUploadStart,
                plan.BarUploadCount,
                plan.BarFullUpload,
                plan.AnchorUploadStart,
                plan.AnchorUploadCount,
                plan.AnchorFullUpload,
                plan.GlyphUploadStart,
                plan.GlyphUploadCount,
                plan.GlyphFullUpload);
        }
    }

    public int TerrainChunkCount => _terrainChunkCount;

    public WebGpuMeshFrame GetTerrainChunk(int index)
    {
        if ((uint)index >= (uint)_terrainChunkCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _visibleTerrainChunks[index];
    }

    public void Update(float deltaSeconds, uint viewportWidth, uint viewportHeight)
    {
        _frameNumber++;
        _presentation.SetViewport(viewportWidth, viewportHeight);
        _engine.SetService(CoreServiceKeys.UiCaptured, false);

        _engine.Tick(deltaSeconds);
        long frameBuildStart = Stopwatch.GetTimestamp();
        _presentation.Update(viewportWidth, viewportHeight, deltaSeconds);
        UpdateCamera(viewportWidth, viewportHeight);
        UpdateWorldBatches();
        UpdateGroundOverlayMesh();
        UpdateTerrainChunks();
        UpdateScreenInstances();
        double frameBuildSample = (Stopwatch.GetTimestamp() - frameBuildStart) * 1000d / Stopwatch.Frequency;
        _frameBuildMilliseconds = _frameBuildMilliseconds == 0d
            ? frameBuildSample
            : _frameBuildMilliseconds + ((frameBuildSample - _frameBuildMilliseconds) * 0.18d);
        WebGpuHudBuildDiagnostics hudBuild = _worldHudStore.BuildDiagnostics;
        WebGpuHudUploadPlan hudUpload = _worldHudStore.UploadPlan;
        PerformerEntityRuntime performers = _engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
            ?? throw new InvalidOperationException("PerformerEntityRuntime is required by the WebGPU host.");
        _hudDiagnostics = _hudDiagnostics with
        {
            BuildPath = (byte)hudBuild.Path,
            HudCpuBuildMilliseconds = hudBuild.HudCpuBuildMilliseconds,
            BarBuildMilliseconds = hudBuild.BarBuildMilliseconds,
            TextResolveMilliseconds = hudBuild.TextResolveMilliseconds,
            GlyphWriteMilliseconds = hudBuild.GlyphWriteMilliseconds,
            ActivePerformerCount = performers.ActiveCount,
            BarInstanceCount = hudBuild.BarInstanceCount,
            BarResidentBytes = hudBuild.BarResidentBytes,
            BarUploadBytes = hudUpload.BarUploadBytes,
            LabelAnchorCount = hudBuild.LabelAnchorCount,
            AnchorResidentBytes = hudBuild.AnchorResidentBytes,
            AnchorUploadBytes = hudUpload.AnchorUploadBytes,
            GlyphCount = hudBuild.GlyphCount,
            GlyphResidentBytes = hudBuild.GlyphResidentBytes,
            GlyphUploadBytes = hudUpload.GlyphUploadBytes,
            BarInstancesBuilt = hudBuild.BarInstancesBuilt,
            TextsResolved = hudBuild.TextsResolved,
            GlyphsWritten = hudBuild.GlyphsWritten,
            PositionOnlyBarsUpdated = hudBuild.PositionOnlyBarsUpdated,
            PositionOnlyTextsUpdated = hudBuild.PositionOnlyTextsUpdated,
            ContentDirtyBars = hudBuild.ContentDirtyBars,
            ContentDirtyTexts = hudBuild.ContentDirtyTexts,
            RemovedCount = hudBuild.RemovedCount,
            FullRebuildCount = hudBuild.FullRebuildCount,
        };
        _diagnostics = new WebGpuFrameDiagnostics(
            _timingDiagnostics.TotalTickMs,
            _timingDiagnostics.SimulationMs,
            _timingDiagnostics.PresentationMs,
            _timingDiagnostics.PerformerEmitMs,
            _timingDiagnostics.PerformerEntityTransformSyncMs,
            _timingDiagnostics.PerformerBehaviorMs,
            _frameBuildMilliseconds,
            _timingDiagnostics.PerformerEmitSingleVisualFastCountLastFrame,
            _hudDiagnostics);

        if (!_reportedReady &&
            _engine.TryGetService(MassNavigationKeys.RuntimeBinding, out MassNavigationRuntimeBinding binding) &&
            binding.IsReady)
        {
            MassNavigationSimulationRuntime simulation = binding.RequireCurrent();
            _reportedReady = simulation.NavigationAgentCount == 10_000;
            if (_reportedReady)
            {
                ReportState("MASS_NAVIGATION_READY", simulation);
            }
        }

        if ((_frameNumber % 300u) == 0u &&
            _engine.TryGetService(MassNavigationKeys.RuntimeBinding, out MassNavigationRuntimeBinding periodicBinding) &&
            periodicBinding.IsReady)
        {
            ReportState("MASS_NAVIGATION_HEARTBEAT", periodicBinding.RequireCurrent());
        }
    }

    public void ReportWorldHudUploadTiming(
        int barUploadBytes,
        double barUploadMilliseconds,
        int anchorUploadBytes,
        double anchorUploadMilliseconds,
        int glyphUploadBytes,
        double glyphUploadMilliseconds)
    {
        _hudDiagnostics = _hudDiagnostics with
        {
            BarUploadBytes = barUploadBytes,
            BarUploadMilliseconds = barUploadMilliseconds,
            AnchorUploadBytes = anchorUploadBytes,
            AnchorUploadMilliseconds = anchorUploadMilliseconds,
            GlyphUploadBytes = glyphUploadBytes,
            GlyphUploadMilliseconds = glyphUploadMilliseconds,
        };
        _diagnostics = _diagnostics with { Hud = _hudDiagnostics };
    }

    public void Dispose()
    {
        _engine.Dispose();
    }

    private void UpdateCamera(uint viewportWidth, uint viewportHeight)
    {
        var state = _presentation.Camera.State;
        float aspect = Math.Max(1u, viewportWidth) / (float)Math.Max(1u, viewportHeight);
        CameraClipPlanes clipPlanes = CameraViewportUtil.ResolveClipPlanes(in state);
        Matrix4x4 view = Matrix4x4.CreateLookAt(state.Position, state.Target, state.Up);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            WorldPlane2D.DegToRadValue(state.FovYDeg),
            aspect,
            clipPlanes.NearMeters,
            clipPlanes.FarMeters);
        _camera = new WebGpuCameraFrame(
            view * projection,
            new Vector2(Math.Max(1u, viewportWidth), Math.Max(1u, viewportHeight)));
    }

    private void UpdateWorldBatches()
    {
        for (int i = 0; i < _activeWorldBatchCount; i++)
        {
            _worldBatchStates[_activeWorldBatchSlots[i]].Count = 0;
        }

        _activeWorldBatchCount = 0;

        SkinnedVisualBatchBuffer skinned = _engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
            ?? throw new InvalidOperationException("PresentationSkinnedVisualBatchBuffer is required by the WebGPU host.");
        PrimitiveDrawBuffer draw = _engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
            ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer is required by the WebGPU host.");
        PrimitiveDrawBuffer snapshot = _engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
            ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer is required by the WebGPU host.");
        if (skinned.DroppedSinceClear > 0 || draw.DroppedSinceClear > 0 || snapshot.DroppedSinceClear > 0)
        {
            throw new InvalidOperationException(
                $"WebGPU cannot present the formal frame: skinned dropped={skinned.DroppedSinceClear}, primitive dropped={draw.DroppedSinceClear}, snapshot dropped={snapshot.DroppedSinceClear}.");
        }

        ReadOnlySpan<SkinnedVisualBatchItem> skinnedItems = skinned.GetSpan();
        for (int i = 0; i < skinnedItems.Length; i++)
        {
            ref readonly SkinnedVisualBatchItem item = ref skinnedItems[i];
            if (item.Visibility != VisualVisibility.Visible)
            {
                continue;
            }

            AddWorldInstance(
                new WorldBatchKey(item.RenderPath, item.MeshAssetId, item.MaterialId),
                new WebGpuWorldInstance
                {
                    Position = item.Position,
                    Scale = item.Scale,
                    Rotation = item.Rotation,
                    Color = item.Color,
                });
        }

        ReadOnlySpan<PrimitiveDrawItem> snapshotItems = snapshot.GetSpan();
        for (int i = 0; i < snapshotItems.Length; i++)
        {
            ref readonly PrimitiveDrawItem item = ref snapshotItems[i];
            if (item.Visibility != VisualVisibility.Visible || !StaticMeshLaneKey.Supports(in item))
            {
                continue;
            }

            AddPrimitive(in item);
        }

        ReadOnlySpan<PrimitiveDrawItem> drawItems = draw.GetSpan();
        for (int i = 0; i < drawItems.Length; i++)
        {
            ref readonly PrimitiveDrawItem item = ref drawItems[i];
            if (item.Visibility != VisualVisibility.Visible ||
                item.RenderPath.IsSkinnedLane() ||
                StaticMeshLaneKey.Supports(in item))
            {
                continue;
            }

            if (item.AssetKind == AssetKind.Surface || item.RenderPath.IsSurfaceLane())
            {
                continue;
            }

            AddPrimitive(in item);
        }
    }

    private void AddPrimitive(in PrimitiveDrawItem item)
    {
        if (item.AssetKind != AssetKind.Mesh)
        {
            throw new InvalidOperationException(
                $"WebGPU cannot present primitive stableId={item.StableId}: asset kind {item.AssetKind} is not a mesh lane.");
        }

        if (!item.RenderPath.IsStaticInstanceLane())
        {
            throw new InvalidOperationException(
                $"WebGPU cannot present primitive stableId={item.StableId}: render path {item.RenderPath} is unsupported.");
        }

        AddWorldInstance(
            new WorldBatchKey(item.RenderPath, item.MeshAssetId, item.MaterialId),
            new WebGpuWorldInstance
            {
                Position = item.Position,
                Scale = item.Scale,
                Rotation = item.Rotation,
                Color = item.Color,
            });
    }

    private void AddWorldInstance(WorldBatchKey key, WebGpuWorldInstance instance)
    {
        int slot = GetOrCreateWorldBatchSlot(key);
        WorldBatchState state = _worldBatchStates[slot];
        if (state.Count == 0)
        {
            EnsureCapacity(ref _activeWorldBatchSlots, _activeWorldBatchCount + 1);
            _activeWorldBatchSlots[_activeWorldBatchCount++] = slot;
        }

        EnsureCapacity(ref state.Instances, state.Count + 1);
        state.Instances[state.Count++] = instance;
    }

    private int GetOrCreateWorldBatchSlot(WorldBatchKey key)
    {
        if (_worldBatchSlotByKey.TryGetValue(key, out int slot))
        {
            return slot;
        }

        if (key.MeshAssetId <= 0)
        {
            throw new InvalidOperationException($"WebGPU world batch references invalid mesh asset id {key.MeshAssetId}.");
        }

        PresentationMaterialRegistry materials = _engine.GetService(CoreServiceKeys.PresentationMaterialRegistry)
            ?? throw new InvalidOperationException("PresentationMaterialRegistry is required by the WebGPU host.");
        if (!materials.TryGet(key.MaterialId, out MaterialAssetDescriptor material))
        {
            throw new InvalidOperationException(
                $"WebGPU world batch references unknown material id {key.MaterialId} for mesh {key.MeshAssetId}.");
        }

        if (material.Flags != MaterialAssetFlags.None || material.SourceUris.Length != 0)
        {
            throw new InvalidOperationException(
                $"WebGPU material '{materials.GetName(key.MaterialId)}' requires unsupported flags or external material sources.");
        }

        EnsureCapacity(ref _worldBatchStates, _worldBatchSlotCount + 1);
        slot = _worldBatchSlotCount++;
        var state = new WorldBatchState(key, LoadWorldMesh(key.MeshAssetId));
        _worldBatchStates[slot] = state;
        _worldBatchSlotByKey.Add(key, slot);
        return slot;
    }

    private WebGpuMeshFrame LoadWorldMesh(int meshAssetId)
    {
        if (_worldMeshCache.TryGetValue(meshAssetId, out WebGpuMeshFrame cached))
        {
            return cached;
        }

        MeshAssetRegistry registry = _engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
            ?? throw new InvalidOperationException("PresentationMeshAssetRegistry is required by the WebGPU host.");
        if (!registry.TryGetDescriptor(meshAssetId, out MeshAssetDescriptor descriptor) ||
            descriptor.Type != MeshAssetType.Model ||
            descriptor.SourceUris is not { Length: 1 })
        {
            throw new InvalidOperationException(
                $"The formal WebGPU mesh {meshAssetId} must resolve to exactly one registered model source.");
        }

        string sourceUri = descriptor.SourceUris[0];
        using Stream stream = _engine.VFS.GetStream(sourceUri);
        WebGpuMeshFrame mesh = WebGpuMeshAssetLoader.Load(stream, sourceUri, meshAssetId);
        _worldMeshCache.Add(meshAssetId, mesh);
        Console.WriteLine(
            $"[Ludots WebGPU] Formal world mesh loaded: asset={registry.GetName(meshAssetId)} uri={sourceUri} vertices={mesh.Vertices.Length} indices={mesh.Indices.Length}.");
        return mesh;
    }

    private void UpdateGroundOverlayMesh()
    {
        GroundOverlayBuffer overlays = _engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
            ?? throw new InvalidOperationException("GroundOverlayBuffer is required by the WebGPU host.");
        _groundOverlayMesh = _groundOverlayCompiler.Compile(overlays.GetSpan());
    }

    private void UpdateTerrainChunks()
    {
        if (!_engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? heightmap) ||
            heightmap is not IVisualHeightmapRenderSource source)
        {
            throw new InvalidOperationException(
                "The formal Mass Navigation map requires an IVisualHeightmapRenderSource for WebGPU terrain rendering.");
        }

        float chunkWidthCm = source.Bounds.Width / (float)source.ChunkColumns;
        float chunkHeightCm = source.Bounds.Height / (float)source.ChunkRows;
        float cameraDistanceCm = Vector3.Distance(_presentation.Camera.State.Position, _presentation.Camera.State.Target) * 100f;
        float visibleRadiusCm = MathF.Max(
            cameraDistanceCm * 10f,
            MathF.Max(chunkWidthCm, chunkHeightCm) * 2f);
        float targetXCm = _presentation.Camera.State.Target.X * 100f;
        float targetYCm = _presentation.Camera.State.Target.Z * 100f;
        int minChunkX = ResolveChunkIndex(targetXCm - visibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
        int maxChunkX = ResolveChunkIndex(targetXCm + visibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
        int minChunkY = ResolveChunkIndex(targetYCm - visibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
        int maxChunkY = ResolveChunkIndex(targetYCm + visibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
        int visibleCapacity = checked((maxChunkX - minChunkX + 1) * (maxChunkY - minChunkY + 1));
        EnsureCapacity(ref _visibleTerrainChunks, visibleCapacity);
        _terrainChunkCount = 0;

        for (int y = minChunkY; y <= maxChunkY; y++)
        {
            for (int x = minChunkX; x <= maxChunkX; x++)
            {
                if (!source.TryGetChunk(x, y, out VisualHeightmapRenderChunk chunk))
                {
                    throw new InvalidOperationException($"Visual heightmap chunk ({x},{y}) is missing.");
                }

                int key = checked((y * source.ChunkColumns) + x + 1);
                if (!_terrainMeshCache.TryGetValue(key, out WebGpuMeshFrame mesh) || mesh.Revision != chunk.Revision)
                {
                    mesh = BuildTerrainMesh(in chunk, key);
                    _terrainMeshCache[key] = mesh;
                }

                _visibleTerrainChunks[_terrainChunkCount++] = mesh;
            }
        }
    }

    private static WebGpuMeshFrame BuildTerrainMesh(in VisualHeightmapRenderChunk chunk, int key)
    {
        int columns = chunk.SampleColumns;
        int rows = chunk.SampleRows;
        var vertices = new WebGpuWorldVertex[checked(columns * rows)];
        var indices = new uint[checked((columns - 1) * (rows - 1) * 6)];
        ResolveHeightRange(in chunk, out float minHeightCm, out float maxHeightCm);
        float heightRangeCm = MathF.Max(1f, maxHeightCm - minHeightCm);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int vertex = (y * columns) + x;
                if (!chunk.TryReadHeightCm(x, y, out float heightCm))
                {
                    throw new InvalidOperationException(
                        $"Visual heightmap chunk ({chunk.ChunkX},{chunk.ChunkY}) sample ({x},{y}) is unreadable.");
                }

                Vector3 normal = ComputeTerrainNormal(in chunk, x, y);
                vertices[vertex] = new WebGpuWorldVertex
                {
                    Position = new Vector3(
                        (chunk.Bounds.Left + (x * chunk.SampleStepXCm)) * 0.01f,
                        heightCm * 0.01f,
                        (chunk.Bounds.Top + (y * chunk.SampleStepYCm)) * 0.01f),
                    Normal = normal,
                    Color = ResolveTerrainColor((heightCm - minHeightCm) / heightRangeCm, normal.Y),
                };
            }
        }

        int cursor = 0;
        for (int y = 0; y < rows - 1; y++)
        {
            for (int x = 0; x < columns - 1; x++)
            {
                uint p00 = checked((uint)((y * columns) + x));
                uint p10 = p00 + 1u;
                uint p01 = p00 + checked((uint)columns);
                uint p11 = p01 + 1u;
                indices[cursor++] = p00;
                indices[cursor++] = p01;
                indices[cursor++] = p10;
                indices[cursor++] = p11;
                indices[cursor++] = p10;
                indices[cursor++] = p01;
            }
        }

        return new WebGpuMeshFrame(key, chunk.Revision, vertices, indices);
    }

    private void UpdateScreenInstances()
    {
        WorldHudBatchBuffer worldHud = _engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
            ?? throw new InvalidOperationException("PresentationWorldHudBuffer is required by the WebGPU host.");
        MinimapScreenMarkerBuffer minimap = _engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer is required by the WebGPU host.");
        ScreenOverlayBuffer overlay = _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer is required by the WebGPU host.");
        if (worldHud.DroppedSinceClear > 0 || minimap.DroppedSinceClear > 0)
        {
            throw new InvalidOperationException(
                $"WebGPU cannot present the formal frame: world HUD dropped={worldHud.DroppedSinceClear}, minimap dropped={minimap.DroppedSinceClear}.");
        }

        // World-anchored under-UI HUD consumes WorldHudBatchBuffer directly.
        // WorldHudToScreenSystem is intentionally not installed on the WebGPU host path.
        _worldHudStore.Sync(worldHud);

        // Top-most minimap/diagnostic overlay remains immediate for this slice and must not
        // force a world-HUD full rebuild.
        EnsureCapacity(ref _topMostScreenInstances, checked((minimap.Count * 2) + (overlay.Count * 5)));
        _topMostScreenInstanceCount = 0;
        _topMostTextGlyphInstanceCount = 0;

        ReadOnlySpan<ScreenOverlayItem> overlayItems = overlay.GetSpan();
        for (int i = 0; i < overlayItems.Length; i++)
        {
            ref readonly ScreenOverlayItem item = ref overlayItems[i];
            if (item.Kind == ScreenOverlayItemKind.Rect)
            {
                AddScreenRect(item.X, item.Y, item.Width, item.Height, item.BackgroundColor, item.ClipShape, topMost: true);
                AddScreenRectBorder(item.X, item.Y, item.Width, item.Height, item.Color, item.ClipShape, topMost: true);
            }
        }

        PresentationClipShape minimapClip = minimap.ClipShape;
        for (int i = 0; i < minimap.Count; i++)
        {
            float size = minimap.GetSizePx(i);
            float markerX = minimap.GetScreenX(i);
            float markerY = minimap.GetScreenY(i);
            float markerHalfSize = size * 0.5f;
            if (!IntersectsClip(markerX, markerY, markerHalfSize, markerHalfSize, minimapClip))
            {
                continue;
            }

            AddScreenShape(
                markerX,
                markerY,
                markerHalfSize,
                markerHalfSize,
                minimap.GetColor(i),
                minimap.GetOrientationRad(i),
                shape: 1f,
                minimapClip,
                topMost: true);
            if ((minimap.GetFlags(i) & MinimapMarkerFlags.HasOrientation) != 0u)
            {
                float orientation = minimap.GetOrientationRad(i);
                float length = minimap.GetOrientationLengthPx(i);
                float endX = markerX + (MathF.Cos(orientation) * length);
                float endY = markerY + (MathF.Sin(orientation) * length);
                AddScreenLine(
                    markerX,
                    markerY,
                    endX,
                    endY,
                    MathF.Max(1f, size * 0.2f),
                    minimap.GetColor(i),
                    minimapClip,
                    topMost: true);
            }
        }

        for (int i = 0; i < overlayItems.Length; i++)
        {
            ref readonly ScreenOverlayItem item = ref overlayItems[i];
            if (item.Kind == ScreenOverlayItemKind.Line)
            {
                AddScreenLine(item.X, item.Y, item.Width, item.Height, item.Thickness, item.Color, item.ClipShape, topMost: true);
            }
        }

        UpdateTopMostTextGlyphInstances(overlayItems, overlay);
        overlay.Clear();
    }

    private static bool IntersectsClip(
        float centerX,
        float centerY,
        float halfWidth,
        float halfHeight,
        in PresentationClipShape clip)
    {
        if (!clip.IsActive)
        {
            return true;
        }

        float clipHalfWidth = clip.Width * 0.5f;
        float clipHalfHeight = clip.Height * 0.5f;
        float distanceX = MathF.Max(
            0f,
            MathF.Abs(centerX - (clip.X + clipHalfWidth)) - halfWidth);
        float distanceY = MathF.Max(
            0f,
            MathF.Abs(centerY - (clip.Y + clipHalfHeight)) - halfHeight);
        return clip.Kind switch
        {
            PresentationClipShapeKind.Rect =>
                distanceX <= clipHalfWidth && distanceY <= clipHalfHeight,
            PresentationClipShapeKind.Circle =>
                ((distanceX * distanceX) / MathF.Max(0.0001f, clipHalfWidth * clipHalfWidth)) +
                ((distanceY * distanceY) / MathF.Max(0.0001f, clipHalfHeight * clipHalfHeight)) <= 1f,
            PresentationClipShapeKind.Diamond =>
                (distanceX / MathF.Max(0.0001f, clipHalfWidth)) +
                (distanceY / MathF.Max(0.0001f, clipHalfHeight)) <= 1f,
            _ => throw new InvalidOperationException($"Unsupported presentation clip shape '{clip.Kind}'."),
        };
    }

    private void UpdateTopMostTextGlyphInstances(
        ReadOnlySpan<ScreenOverlayItem> overlayItems,
        ScreenOverlayBuffer overlay)
    {
        for (int i = 0; i < overlayItems.Length; i++)
        {
            ref readonly ScreenOverlayItem item = ref overlayItems[i];
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            WebGpuGlyphRun run = _textCompiler.Resolve(in item, overlay);
            EnsureCapacity(
                ref _topMostTextGlyphInstances,
                checked(_topMostTextGlyphInstanceCount + run.GlyphCount));
            _topMostTextGlyphInstanceCount += _textCompiler.WriteInstances(
                run,
                item.X,
                item.Y,
                item.FontSize,
                item.Color,
                item.ClipShape,
                _topMostTextGlyphInstances.AsSpan(_topMostTextGlyphInstanceCount, run.GlyphCount));
        }
    }

    private void AddScreenRect(
        float x,
        float y,
        float width,
        float height,
        Vector4 color,
        PresentationClipShape clip,
        bool topMost)
    {
        if (width <= 0f || height <= 0f || color.W <= 0f)
        {
            return;
        }

        AddScreenShape(
            x + (width * 0.5f),
            y + (height * 0.5f),
            width * 0.5f,
            height * 0.5f,
            color,
            0f,
            shape: 0f,
            clip,
            topMost);
    }

    private void AddScreenRectBorder(
        float x,
        float y,
        float width,
        float height,
        Vector4 color,
        PresentationClipShape clip,
        bool topMost)
    {
        if (width <= 0f || height <= 0f || color.W <= 0f)
        {
            return;
        }

        const float thickness = 1f;
        AddScreenRect(x, y, width, thickness, color, clip, topMost);
        AddScreenRect(x, y + height - thickness, width, thickness, color, clip, topMost);
        AddScreenRect(x, y, thickness, height, color, clip, topMost);
        AddScreenRect(x + width - thickness, y, thickness, height, color, clip, topMost);
    }

    private void AddScreenLine(
        float x0,
        float y0,
        float x1,
        float y1,
        float thickness,
        Vector4 color,
        PresentationClipShape clip,
        bool topMost)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0f || thickness <= 0f || color.W <= 0f)
        {
            return;
        }

        AddScreenShape(
            (x0 + x1) * 0.5f,
            (y0 + y1) * 0.5f,
            length * 0.5f,
            thickness * 0.5f,
            color,
            MathF.Atan2(dy, dx),
            shape: 0f,
            clip,
            topMost);
    }

    private void AddScreenShape(
        float centerX,
        float centerY,
        float halfWidth,
        float halfHeight,
        Vector4 color,
        float rotation,
        float shape,
        PresentationClipShape clip,
        bool topMost)
    {
        if (!topMost)
        {
            throw new InvalidOperationException(
                "Under-UI screen instances are not used for world-anchored HUD; top-most overlays only.");
        }

        WebGpuScreenInstance[] instances = _topMostScreenInstances;
        int count = _topMostScreenInstanceCount;
        if ((uint)count >= (uint)instances.Length)
        {
            throw new InvalidOperationException("WebGPU screen instance capacity was underestimated.");
        }

        instances[count++] = new WebGpuScreenInstance
        {
            CenterPx = new Vector2(centerX, centerY),
            HalfSizePx = new Vector2(halfWidth, halfHeight),
            Color = color,
            ClipRectPx = new Vector4(clip.X, clip.Y, clip.Width, clip.Height),
            RotationRad = rotation,
            Shape = shape,
            ClipShape = (float)clip.Kind,
        };

        _topMostScreenInstanceCount = count;
    }

    private static int ResolveChunkIndex(float worldCm, int minCm, int sizeCm, int chunkCount)
    {
        float normalized = (worldCm - minCm) / Math.Max(1f, sizeCm);
        return Math.Clamp((int)MathF.Floor(normalized * chunkCount), 0, chunkCount - 1);
    }

    private static void ResolveHeightRange(
        in VisualHeightmapRenderChunk chunk,
        out float minHeightCm,
        out float maxHeightCm)
    {
        minHeightCm = float.PositiveInfinity;
        maxHeightCm = float.NegativeInfinity;
        for (int y = 0; y < chunk.SampleRows; y++)
        {
            for (int x = 0; x < chunk.SampleColumns; x++)
            {
                if (!chunk.TryReadHeightCm(x, y, out float heightCm))
                {
                    continue;
                }

                minHeightCm = MathF.Min(minHeightCm, heightCm);
                maxHeightCm = MathF.Max(maxHeightCm, heightCm);
            }
        }

        if (!float.IsFinite(minHeightCm) || !float.IsFinite(maxHeightCm))
        {
            throw new InvalidOperationException(
                $"Visual heightmap chunk ({chunk.ChunkX},{chunk.ChunkY}) has no finite height samples.");
        }
    }

    private static Vector3 ComputeTerrainNormal(in VisualHeightmapRenderChunk chunk, int x, int y)
    {
        int left = Math.Max(0, x - 1);
        int right = Math.Min(chunk.SampleColumns - 1, x + 1);
        int top = Math.Max(0, y - 1);
        int bottom = Math.Min(chunk.SampleRows - 1, y + 1);
        chunk.TryReadHeightCm(left, y, out float hLeft);
        chunk.TryReadHeightCm(right, y, out float hRight);
        chunk.TryReadHeightCm(x, top, out float hTop);
        chunk.TryReadHeightCm(x, bottom, out float hBottom);
        float dx = MathF.Max(1f, (right - left) * chunk.SampleStepXCm);
        float dz = MathF.Max(1f, (bottom - top) * chunk.SampleStepYCm);
        Vector3 normal = Vector3.Normalize(new Vector3(-(hRight - hLeft) / dx, 1f, -(hBottom - hTop) / dz));
        return float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z)
            ? normal
            : Vector3.UnitY;
    }

    private static Vector4 ResolveTerrainColor(float heightBand, float normalY)
    {
        heightBand = Math.Clamp(heightBand, 0f, 1f);
        Vector3 low = new(0.14f, 0.34f, 0.35f);
        Vector3 mid = new(0.32f, 0.56f, 0.33f);
        Vector3 high = new(0.75f, 0.68f, 0.42f);
        Vector3 peak = new(0.89f, 0.86f, 0.72f);
        Vector3 color = heightBand < 0.5f
            ? Vector3.Lerp(low, mid, heightBand * 2f)
            : heightBand < 0.82f
                ? Vector3.Lerp(mid, high, (heightBand - 0.5f) / 0.32f)
                : Vector3.Lerp(high, peak, (heightBand - 0.82f) / 0.18f);
        float shade = 1f - Math.Clamp((1f - normalY) * 0.42f, 0f, 0.42f);
        return new Vector4(color * shade, 1f);
    }

    private static void EnsureCapacity<T>(ref T[] buffer, int required)
    {
        if (buffer.Length >= required)
        {
            return;
        }

        int capacity = Math.Max(required, Math.Max(1024, buffer.Length * 2));
        Array.Resize(ref buffer, capacity);
    }

    private static void ApplyHostAssets(GameEngine engine)
    {
        new PresentationHostAssetConfigLoader(
            engine.ConfigPipeline,
            engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry is required."),
            engine.GetService(CoreServiceKeys.PresentationMaterialRegistry)
                ?? throw new InvalidOperationException("PresentationMaterialRegistry is required."))
            .Apply("webgpu", engine.ConfigCatalog, engine.ConfigConflictReport);
    }

    private static void InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var backend = new BrowserInputBackend();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private void ReportState(string marker, MassNavigationSimulationRuntime simulation)
    {
        int skinned = _engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)?.Count ?? -1;
        int hud = _engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)?.Count ?? -1;
        int minimap = _engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)?.Count ?? -1;
        Console.WriteLine(
            $"[Ludots WebGPU] {marker} map={_engine.CurrentMapSession?.MapId.Value ?? "<none>"} navigationAgents={simulation.NavigationAgentCount} scenarioSpawns={simulation.ScenarioSpawnCount} skinned={skinned} hud={hud} minimap={minimap}.");
    }

    private readonly record struct WorldBatchKey(
        VisualRenderPath RenderPath,
        int MeshAssetId,
        int MaterialId);

    private sealed class WorldBatchState
    {
        public WorldBatchState(WorldBatchKey key, WebGpuMeshFrame mesh)
        {
            Key = key;
            Mesh = mesh;
        }

        public WorldBatchKey Key { get; }

        public WebGpuMeshFrame Mesh { get; }

        public WebGpuWorldInstance[] Instances = Array.Empty<WebGpuWorldInstance>();

        public int Count;
    }

}
