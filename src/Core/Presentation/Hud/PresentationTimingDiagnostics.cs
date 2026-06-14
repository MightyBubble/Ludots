namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Lightweight presentation timing samples shared between adapters and debug HUDs.
    /// Values are exponentially smoothed so manual toggle experiments are easier to read.
    /// </summary>
    public sealed class PresentationTimingDiagnostics
    {
        private const float SampleWeight = 0.18f;
        private const int CompositeWindowSize = 60;
        private int _compositeSkipAccumulator;
        private int _compositeFrameAccumulator;
        private string _lastPresentationTopSystem1Name = string.Empty;
        private float _lastPresentationTopSystem1Ms;
        private string _lastPresentationTopSystem2Name = string.Empty;
        private float _lastPresentationTopSystem2Ms;
        private string _lastPresentationTopSystem3Name = string.Empty;
        private float _lastPresentationTopSystem3Ms;
        private string _lastSimulationTopSystem1Name = string.Empty;
        private float _lastSimulationTopSystem1Ms;
        private string _lastSimulationTopSystem2Name = string.Empty;
        private float _lastSimulationTopSystem2Ms;
        private string _lastSimulationTopSystem3Name = string.Empty;
        private float _lastSimulationTopSystem3Ms;

        public bool SystemBreakdownEnabled { get; set; }

        public float UiInputMs { get; private set; }
        public float UiRenderMs { get; private set; }
        public float LastUiRenderMs { get; private set; }
        public float UiUploadMs { get; private set; }
        public float LastUiUploadMs { get; private set; }
        public float FrameMs { get; private set; }
        public float LastFrameMs { get; private set; }
        public float WallFrameMs { get; private set; }
        public float LastWallFrameMs { get; private set; }
        public float ScreenOverlayBuildMs { get; private set; }
        public float LastScreenOverlayBuildMs { get; private set; }
        public float ScreenOverlayPaintMs { get; private set; }
        public float LastScreenOverlayPaintMs { get; private set; }
        public float ScreenOverlayCompositeMs { get; private set; }
        public float LastScreenOverlayCompositeMs { get; private set; }
        public float ScreenOverlayDrawMs { get; private set; }
        public float LastScreenOverlayDrawMs { get; private set; }
        public float ScreenOverlayFinalDrawMs { get; private set; }
        public float LastScreenOverlayFinalDrawMs { get; private set; }
        public float EndDrawingMs { get; private set; }
        public float LastEndDrawingMs { get; private set; }
        public float ScreenshotMs { get; private set; }
        public float LastScreenshotMs { get; private set; }
        public float CameraCullingMs { get; private set; }
        public float LastCameraCullingMs { get; private set; }
        public float CameraCullingEntityProcessMs { get; private set; }
        public float LastCameraCullingEntityProcessMs { get; private set; }
        public float CameraCullingPerformerSyncMs { get; private set; }
        public float LastCameraCullingPerformerSyncMs { get; private set; }
        public float CameraCullingSpatialQueryMs { get; private set; }
        public float LastCameraCullingSpatialQueryMs { get; private set; }
        public float CameraCullingStaticProcessMs { get; private set; }
        public float LastCameraCullingStaticProcessMs { get; private set; }
        public float CameraCullingStaticPendingRemoveMs { get; private set; }
        public float LastCameraCullingStaticPendingRemoveMs { get; private set; }
        public float CameraCullingDynamicProcessMs { get; private set; }
        public float LastCameraCullingDynamicProcessMs { get; private set; }
        public float CameraPresenterMs { get; private set; }
        public float WorldHudProjectionMs { get; private set; }
        public float LastWorldHudProjectionMs { get; private set; }
        public float SimulationMs { get; private set; }
        public float LastSimulationMs { get; private set; }
        public float PresentationMs { get; private set; }
        public float LastPresentationMs { get; private set; }
        public float TotalTickMs { get; private set; }
        public float LastTotalTickMs { get; private set; }
        public string LastPresentationTopSystem1Name => _lastPresentationTopSystem1Name;
        public float LastPresentationTopSystem1Ms => _lastPresentationTopSystem1Ms;
        public string LastPresentationTopSystem2Name => _lastPresentationTopSystem2Name;
        public float LastPresentationTopSystem2Ms => _lastPresentationTopSystem2Ms;
        public string LastPresentationTopSystem3Name => _lastPresentationTopSystem3Name;
        public float LastPresentationTopSystem3Ms => _lastPresentationTopSystem3Ms;
        public string LastSimulationTopSystem1Name => _lastSimulationTopSystem1Name;
        public float LastSimulationTopSystem1Ms => _lastSimulationTopSystem1Ms;
        public string LastSimulationTopSystem2Name => _lastSimulationTopSystem2Name;
        public float LastSimulationTopSystem2Ms => _lastSimulationTopSystem2Ms;
        public string LastSimulationTopSystem3Name => _lastSimulationTopSystem3Name;
        public float LastSimulationTopSystem3Ms => _lastSimulationTopSystem3Ms;
        public float PerformerBehaviorMs { get; private set; }
        public float LastPerformerBehaviorMs { get; private set; }
        public int PerformerBootstrapCountLastFrame { get; private set; }
        public int PerformerOwnerChangesLastFrame { get; private set; }
        public int PerformerOwnerAttributeChangesLastFrame { get; private set; }
        public int PerformerOwnerTagChangesLastFrame { get; private set; }
        public int PerformerTickDrivenCountLastFrame { get; private set; }
        public int PerformerActiveSoundTrackingCountLastFrame { get; private set; }
        public int PerformerDestroyEventScanCountLastFrame { get; private set; }
        public float PerformerAnimatorMs { get; private set; }
        public float LastPerformerAnimatorMs { get; private set; }
        public float PerformerEntityTransformSyncMs { get; private set; }
        public float LastPerformerEntityTransformSyncMs { get; private set; }
        public float PerformerMinimapMarkerMs { get; private set; }
        public float LastPerformerMinimapMarkerMs { get; private set; }
        public int PerformerMinimapMarkersLastFrame { get; private set; }
        public int PerformerMinimapDroppedLastFrame { get; private set; }
        public float MinimapProjectionMs { get; private set; }
        public float LastMinimapProjectionMs { get; private set; }
        public int MinimapScreenMarkersLastFrame { get; private set; }
        public int MinimapScreenMarkersDroppedLastFrame { get; private set; }
        public float RuntimeSpawnBatchPrepareMs { get; private set; }
        public float LastRuntimeSpawnBatchPrepareMs { get; private set; }
        public float RuntimeSpawnWorldCreateMs { get; private set; }
        public float LastRuntimeSpawnWorldCreateMs { get; private set; }
        public float RuntimeSpawnFillBatchMs { get; private set; }
        public float LastRuntimeSpawnFillBatchMs { get; private set; }
        public float RuntimeSpawnPostSpawnMs { get; private set; }
        public float LastRuntimeSpawnPostSpawnMs { get; private set; }
        public float RuntimeSpawnPerformerBatchMs { get; private set; }
        public float LastRuntimeSpawnPerformerBatchMs { get; private set; }
        public float RuntimeSpawnPerformerCreateMs { get; private set; }
        public float LastRuntimeSpawnPerformerCreateMs { get; private set; }
        public float RuntimeSpawnPerformerBootstrapMarkMs { get; private set; }
        public float LastRuntimeSpawnPerformerBootstrapMarkMs { get; private set; }
        public float LastRuntimeSpawnPerformerCreateSetupMs { get; private set; }
        public float LastRuntimeSpawnPerformerWorldCreateMs { get; private set; }
        public float LastRuntimeSpawnPerformerComponentFillMs { get; private set; }
        public float LastRuntimeSpawnPerformerIndexWriteMs { get; private set; }
        public float LastRuntimeSpawnPerformerOwnerPayloadMs { get; private set; }
        public float LastRuntimeSpawnPerformerPostCreateMs { get; private set; }
        public float LastRuntimeSpawnPerformerChildSetupMs { get; private set; }
        public float LastRuntimeSpawnPerformerChildWorldCreateMs { get; private set; }
        public float LastRuntimeSpawnPerformerChildComponentFillMs { get; private set; }
        public float LastRuntimeSpawnPerformerChildIndexWriteMs { get; private set; }
        public float LastRuntimeSpawnPerformerChildStableIdMs { get; private set; }
        public int RuntimeSpawnBatchCountLastFrame { get; private set; }
        public int RuntimeSpawnPerformerCreatedLastFrame { get; private set; }
        public float PerformerEmitMs { get; private set; }
        public float LastPerformerEmitMs { get; private set; }
        public float PerformerEmitDirtyProcessMs { get; private set; }
        public float LastPerformerEmitDirtyProcessMs { get; private set; }
        public float PerformerEmitDirtyCleanupMs { get; private set; }
        public float LastPerformerEmitDirtyCleanupMs { get; private set; }
        public int PerformerEmitDirtyCountLastFrame { get; private set; }
        public float PerformerEmitRetainedProcessMs { get; private set; }
        public float LastPerformerEmitRetainedProcessMs { get; private set; }
        public int PerformerEmitRetainedCountLastFrame { get; private set; }
        public int PerformerEmitRetainedDirectHitsLastFrame { get; private set; }
        public int PerformerEmitRetainedFullPathLastFrame { get; private set; }
        public int PerformerEmitRetainedDirectMissesLastFrame { get; private set; }
        public float PresentationRequestFlushMs { get; private set; }
        public float LastPresentationRequestFlushMs { get; private set; }
        public float TerrainRenderMs { get; private set; }
        public float LastTerrainRenderMs { get; private set; }
        public float TerrainChunkBuildMs { get; private set; }
        public float TerrainHeightSyncMs { get; private set; }
        public float LastTerrainHeightSyncMs { get; private set; }
        public float PrimitiveRenderMs { get; private set; }
        public float LastPrimitiveRenderMs { get; private set; }
        public float PrimitiveMatrixBuildMs { get; private set; }
        public float LastPrimitiveMatrixBuildMs { get; private set; }
        public float PrimitiveMeshDrawMs { get; private set; }
        public float LastPrimitiveMeshDrawMs { get; private set; }
        public float PrimitivePersistentSyncMs { get; private set; }
        public float LastPrimitivePersistentSyncMs { get; private set; }
        public float PrimitivePersistentBucketDrawMs { get; private set; }
        public float LastPrimitivePersistentBucketDrawMs { get; private set; }
        public float PrimitiveImmediateDrawMs { get; private set; }
        public float LastPrimitiveImmediateDrawMs { get; private set; }
        public float HostPreTickMs { get; private set; }
        public float LastHostPreTickMs { get; private set; }
        public float HostPostTickMs { get; private set; }
        public float LastHostPostTickMs { get; private set; }
        public float BeginDrawingMs { get; private set; }
        public float LastBeginDrawingMs { get; private set; }
        public float Mode3DMs { get; private set; }
        public float LastMode3DMs { get; private set; }
        public float GroundOverlayRenderMs { get; private set; }
        public float LastGroundOverlayRenderMs { get; private set; }
        public float RoadSplineRenderMs { get; private set; }
        public float LastRoadSplineRenderMs { get; private set; }
        public float DebugDrawRenderMs { get; private set; }
        public float LastDebugDrawRenderMs { get; private set; }
        public float NativeDiagnosticHudMs { get; private set; }
        public float LastNativeDiagnosticHudMs { get; private set; }
        public float HostLoopGapMs { get; private set; }
        public float LastHostLoopGapMs { get; private set; }
        public float WindowPollMs { get; private set; }
        public float LastWindowPollMs { get; private set; }

        public int VisibleEntitiesLastFrame { get; private set; }
        public int ScreenOverlayDirtyLanesLastFrame { get; private set; }
        public int ScreenOverlayItemsLastFrame { get; private set; }
        public int ScreenOverlayRebuiltLanesLastFrame { get; private set; }
        public int ScreenOverlayTextLayoutCacheCount { get; private set; }
        public int TerrainChunksDrawnLastFrame { get; private set; }
        public int TerrainChunksBuiltLastFrame { get; private set; }
        public int TerrainHeightSamplesLastFrame { get; private set; }
        public int PrimitiveInstancesLastFrame { get; private set; }
        public int PrimitiveBatchesLastFrame { get; private set; }
        public int SkinnedRawLastFrame { get; private set; }
        public int GpuSkinnedInstancesLastFrame { get; private set; }
        public int GpuSkinnedBatchesLastFrame { get; private set; }
        public float LastGpuSkinnedMatrixBuildMs { get; private set; }
        public float LastGpuSkinnedMeshDrawMs { get; private set; }
        public int PrimitiveMatrixCacheHitsLastFrame { get; private set; }
        public int PrimitiveMatrixCacheMissesLastFrame { get; private set; }
        public int PrimitiveImmediateSkippedLastFrame { get; private set; }
        public int WorldHudItemsLastProjection { get; private set; }
        public int WorldHudProjectedLastFrame { get; private set; }
        public int WorldHudDensitySkippedLastFrame { get; private set; }
        public int GroundOverlaysLastFrame { get; private set; }
        public int RoadSplinesLastFrame { get; private set; }
        public int DebugDrawCommandsLastFrame { get; private set; }
        public int CompositeSkipCountLastSecond { get; private set; }

        public void ObserveFrame(double sampleMs)
        {
            LastFrameMs = (float)sampleMs;
            FrameMs = Smooth(FrameMs, (float)sampleMs);
        }

        public void ObserveWallFrame(double sampleMs)
        {
            LastWallFrameMs = (float)sampleMs;
            WallFrameMs = Smooth(WallFrameMs, (float)sampleMs);
        }

        public void ObserveUiInput(double sampleMs) => UiInputMs = Smooth(UiInputMs, (float)sampleMs);
        public void ObserveUiRender(double sampleMs)
        {
            LastUiRenderMs = (float)sampleMs;
            UiRenderMs = Smooth(UiRenderMs, (float)sampleMs);
        }

        public void ObserveUiUpload(double sampleMs)
        {
            LastUiUploadMs = (float)sampleMs;
            UiUploadMs = Smooth(UiUploadMs, (float)sampleMs);
        }

        public void ObserveScreenOverlayBuild(double sampleMs, int dirtyLanes, int totalItems)
        {
            LastScreenOverlayBuildMs = (float)sampleMs;
            ScreenOverlayBuildMs = Smooth(ScreenOverlayBuildMs, (float)sampleMs);
            ScreenOverlayDirtyLanesLastFrame = dirtyLanes;
            ScreenOverlayItemsLastFrame = totalItems;
        }

        public void ObserveScreenOverlayDraw(
            double totalMs,
            double paintMs,
            double compositeMs,
            double uploadMs,
            double finalDrawMs,
            int rebuiltLanes,
            int textLayoutCacheCount)
        {
            LastScreenOverlayDrawMs = (float)totalMs;
            LastScreenOverlayPaintMs = (float)paintMs;
            LastScreenOverlayCompositeMs = (float)compositeMs;
            LastScreenOverlayFinalDrawMs = (float)finalDrawMs;
            ScreenOverlayDrawMs = Smooth(ScreenOverlayDrawMs, (float)totalMs);
            ScreenOverlayPaintMs = Smooth(ScreenOverlayPaintMs, (float)paintMs);
            ScreenOverlayCompositeMs = Smooth(ScreenOverlayCompositeMs, (float)compositeMs);
            LastUiUploadMs = (float)uploadMs;
            UiUploadMs = Smooth(UiUploadMs, (float)uploadMs);
            ScreenOverlayFinalDrawMs = Smooth(ScreenOverlayFinalDrawMs, (float)finalDrawMs);
            ScreenOverlayRebuiltLanesLastFrame = rebuiltLanes;
            ScreenOverlayTextLayoutCacheCount = textLayoutCacheCount;
        }

        public void ObserveEndDrawing(double sampleMs)
        {
            LastEndDrawingMs = (float)sampleMs;
            EndDrawingMs = Smooth(EndDrawingMs, (float)sampleMs);
        }

        public void ObserveScreenshot(double sampleMs)
        {
            LastScreenshotMs = (float)sampleMs;
            ScreenshotMs = Smooth(ScreenshotMs, (float)sampleMs);
        }

        public void ObserveCameraCulling(double sampleMs, int visibleEntities)
        {
            LastCameraCullingMs = (float)sampleMs;
            CameraCullingMs = Smooth(CameraCullingMs, (float)sampleMs);
            VisibleEntitiesLastFrame = visibleEntities;
        }

        public void ObserveCameraCullingBreakdown(double entityProcessMs, double performerSyncMs)
        {
            LastCameraCullingEntityProcessMs = (float)entityProcessMs;
            CameraCullingEntityProcessMs = Smooth(CameraCullingEntityProcessMs, (float)entityProcessMs);
            LastCameraCullingPerformerSyncMs = (float)performerSyncMs;
            CameraCullingPerformerSyncMs = Smooth(CameraCullingPerformerSyncMs, (float)performerSyncMs);
        }

        public void ObserveCameraCullingSpatialQuery(double spatialQueryMs)
        {
            LastCameraCullingSpatialQueryMs = (float)spatialQueryMs;
            CameraCullingSpatialQueryMs = Smooth(CameraCullingSpatialQueryMs, (float)spatialQueryMs);
        }

        public void ObserveCameraCullingStageBreakdown(double staticProcessMs, double staticPendingRemoveMs, double dynamicProcessMs)
        {
            LastCameraCullingStaticProcessMs = (float)staticProcessMs;
            CameraCullingStaticProcessMs = Smooth(CameraCullingStaticProcessMs, (float)staticProcessMs);
            LastCameraCullingStaticPendingRemoveMs = (float)staticPendingRemoveMs;
            CameraCullingStaticPendingRemoveMs = Smooth(CameraCullingStaticPendingRemoveMs, (float)staticPendingRemoveMs);
            LastCameraCullingDynamicProcessMs = (float)dynamicProcessMs;
            CameraCullingDynamicProcessMs = Smooth(CameraCullingDynamicProcessMs, (float)dynamicProcessMs);
        }

        public void ObserveCameraPresenter(double sampleMs) => CameraPresenterMs = Smooth(CameraPresenterMs, (float)sampleMs);

        public void ObservePerformerMinimapMarker(double sampleMs, int markers, int dropped)
        {
            LastPerformerMinimapMarkerMs = (float)sampleMs;
            PerformerMinimapMarkerMs = Smooth(PerformerMinimapMarkerMs, (float)sampleMs);
            PerformerMinimapMarkersLastFrame = markers;
            PerformerMinimapDroppedLastFrame = dropped;
        }

        public void ObserveMinimapProjection(double sampleMs, int screenMarkers, int dropped)
        {
            LastMinimapProjectionMs = (float)sampleMs;
            MinimapProjectionMs = Smooth(MinimapProjectionMs, (float)sampleMs);
            MinimapScreenMarkersLastFrame = screenMarkers;
            MinimapScreenMarkersDroppedLastFrame = dropped;
        }

        public void ObserveWorldHudProjection(double sampleMs)
        {
            ObserveWorldHudProjection(sampleMs, WorldHudItemsLastProjection, WorldHudProjectedLastFrame, WorldHudDensitySkippedLastFrame);
        }

        public void ObserveWorldHudProjection(double sampleMs, int rawItems, int projectedItems, int densitySkippedItems)
        {
            LastWorldHudProjectionMs = (float)sampleMs;
            WorldHudProjectionMs = Smooth(WorldHudProjectionMs, (float)sampleMs);
            WorldHudItemsLastProjection = rawItems;
            WorldHudProjectedLastFrame = projectedItems;
            WorldHudDensitySkippedLastFrame = densitySkippedItems;
        }
        public void ObserveSimulation(double sampleMs)
        {
            LastSimulationMs = (float)sampleMs;
            SimulationMs = Smooth(SimulationMs, (float)sampleMs);
        }

        public void ObservePresentation(double sampleMs)
        {
            LastPresentationMs = (float)sampleMs;
            PresentationMs = Smooth(PresentationMs, (float)sampleMs);
        }

        public void ObserveTotalTick(double sampleMs)
        {
            LastTotalTickMs = (float)sampleMs;
            TotalTickMs = Smooth(TotalTickMs, (float)sampleMs);
        }

        public void BeginPresentationSystemBreakdown()
        {
            _lastPresentationTopSystem1Name = string.Empty;
            _lastPresentationTopSystem1Ms = 0f;
            _lastPresentationTopSystem2Name = string.Empty;
            _lastPresentationTopSystem2Ms = 0f;
            _lastPresentationTopSystem3Name = string.Empty;
            _lastPresentationTopSystem3Ms = 0f;
        }

        public void ObservePresentationSystem(string systemName, double sampleMs)
        {
            InsertTopSystem(
                systemName,
                (float)sampleMs,
                ref _lastPresentationTopSystem1Name,
                ref _lastPresentationTopSystem1Ms,
                ref _lastPresentationTopSystem2Name,
                ref _lastPresentationTopSystem2Ms,
                ref _lastPresentationTopSystem3Name,
                ref _lastPresentationTopSystem3Ms);
        }

        public void BeginSimulationSystemBreakdown()
        {
            _lastSimulationTopSystem1Name = string.Empty;
            _lastSimulationTopSystem1Ms = 0f;
            _lastSimulationTopSystem2Name = string.Empty;
            _lastSimulationTopSystem2Ms = 0f;
            _lastSimulationTopSystem3Name = string.Empty;
            _lastSimulationTopSystem3Ms = 0f;
        }

        public void ObserveSimulationSystem(string systemName, double sampleMs)
        {
            InsertTopSystem(
                systemName,
                (float)sampleMs,
                ref _lastSimulationTopSystem1Name,
                ref _lastSimulationTopSystem1Ms,
                ref _lastSimulationTopSystem2Name,
                ref _lastSimulationTopSystem2Ms,
                ref _lastSimulationTopSystem3Name,
                ref _lastSimulationTopSystem3Ms);
        }

        public void ObservePerformerBehavior(double sampleMs)
        {
            LastPerformerBehaviorMs = (float)sampleMs;
            PerformerBehaviorMs = Smooth(PerformerBehaviorMs, (float)sampleMs);
        }

        public void ObservePerformerBehaviorCounts(
            int bootstrapCount,
            int ownerChanges,
            int ownerAttributeChanges,
            int ownerTagChanges,
            int tickDrivenCount,
            int activeSoundTrackingCount,
            int destroyEventScanCount)
        {
            PerformerBootstrapCountLastFrame = bootstrapCount;
            PerformerOwnerChangesLastFrame = ownerChanges;
            PerformerOwnerAttributeChangesLastFrame = ownerAttributeChanges;
            PerformerOwnerTagChangesLastFrame = ownerTagChanges;
            PerformerTickDrivenCountLastFrame = tickDrivenCount;
            PerformerActiveSoundTrackingCountLastFrame = activeSoundTrackingCount;
            PerformerDestroyEventScanCountLastFrame = destroyEventScanCount;
        }

        public void ObservePerformerAnimator(double sampleMs)
        {
            LastPerformerAnimatorMs = (float)sampleMs;
            PerformerAnimatorMs = Smooth(PerformerAnimatorMs, (float)sampleMs);
        }

        public void ObservePerformerEntityTransformSync(double sampleMs)
        {
            LastPerformerEntityTransformSyncMs = (float)sampleMs;
            PerformerEntityTransformSyncMs = Smooth(PerformerEntityTransformSyncMs, (float)sampleMs);
        }

        public void ObserveRuntimeSpawnBatch(
            int batchCount,
            int performerCreated,
            double prepareMs,
            double worldCreateMs,
            double fillBatchMs,
            double postSpawnMs,
            double performerBatchMs,
            double performerCreateMs,
            double performerBootstrapMarkMs,
            double performerCreateSetupMs = 0d,
            double performerWorldCreateMs = 0d,
            double performerComponentFillMs = 0d,
            double performerIndexWriteMs = 0d,
            double performerOwnerPayloadMs = 0d,
            double performerPostCreateMs = 0d,
            double performerChildSetupMs = 0d,
            double performerChildWorldCreateMs = 0d,
            double performerChildComponentFillMs = 0d,
            double performerChildIndexWriteMs = 0d,
            double performerChildStableIdMs = 0d)
        {
            RuntimeSpawnBatchCountLastFrame = batchCount;
            RuntimeSpawnPerformerCreatedLastFrame = performerCreated;
            LastRuntimeSpawnBatchPrepareMs = (float)prepareMs;
            RuntimeSpawnBatchPrepareMs = Smooth(RuntimeSpawnBatchPrepareMs, (float)prepareMs);
            LastRuntimeSpawnWorldCreateMs = (float)worldCreateMs;
            RuntimeSpawnWorldCreateMs = Smooth(RuntimeSpawnWorldCreateMs, (float)worldCreateMs);
            LastRuntimeSpawnFillBatchMs = (float)fillBatchMs;
            RuntimeSpawnFillBatchMs = Smooth(RuntimeSpawnFillBatchMs, (float)fillBatchMs);
            LastRuntimeSpawnPostSpawnMs = (float)postSpawnMs;
            RuntimeSpawnPostSpawnMs = Smooth(RuntimeSpawnPostSpawnMs, (float)postSpawnMs);
            LastRuntimeSpawnPerformerBatchMs = (float)performerBatchMs;
            RuntimeSpawnPerformerBatchMs = Smooth(RuntimeSpawnPerformerBatchMs, (float)performerBatchMs);
            LastRuntimeSpawnPerformerCreateMs = (float)performerCreateMs;
            RuntimeSpawnPerformerCreateMs = Smooth(RuntimeSpawnPerformerCreateMs, (float)performerCreateMs);
            LastRuntimeSpawnPerformerBootstrapMarkMs = (float)performerBootstrapMarkMs;
            RuntimeSpawnPerformerBootstrapMarkMs = Smooth(RuntimeSpawnPerformerBootstrapMarkMs, (float)performerBootstrapMarkMs);
            LastRuntimeSpawnPerformerCreateSetupMs = (float)performerCreateSetupMs;
            LastRuntimeSpawnPerformerWorldCreateMs = (float)performerWorldCreateMs;
            LastRuntimeSpawnPerformerComponentFillMs = (float)performerComponentFillMs;
            LastRuntimeSpawnPerformerIndexWriteMs = (float)performerIndexWriteMs;
            LastRuntimeSpawnPerformerOwnerPayloadMs = (float)performerOwnerPayloadMs;
            LastRuntimeSpawnPerformerPostCreateMs = (float)performerPostCreateMs;
            LastRuntimeSpawnPerformerChildSetupMs = (float)performerChildSetupMs;
            LastRuntimeSpawnPerformerChildWorldCreateMs = (float)performerChildWorldCreateMs;
            LastRuntimeSpawnPerformerChildComponentFillMs = (float)performerChildComponentFillMs;
            LastRuntimeSpawnPerformerChildIndexWriteMs = (float)performerChildIndexWriteMs;
            LastRuntimeSpawnPerformerChildStableIdMs = (float)performerChildStableIdMs;
        }

        public void ObservePerformerEmit(double sampleMs)
        {
            LastPerformerEmitMs = (float)sampleMs;
            PerformerEmitMs = Smooth(PerformerEmitMs, (float)sampleMs);
        }

        public void ObservePerformerEmitDirtyBreakdown(double processMs, double cleanupMs, int dirtyCount)
        {
            LastPerformerEmitDirtyProcessMs = (float)processMs;
            PerformerEmitDirtyProcessMs = Smooth(PerformerEmitDirtyProcessMs, (float)processMs);
            LastPerformerEmitDirtyCleanupMs = (float)cleanupMs;
            PerformerEmitDirtyCleanupMs = Smooth(PerformerEmitDirtyCleanupMs, (float)cleanupMs);
            PerformerEmitDirtyCountLastFrame = dirtyCount;
        }

        public void ObservePerformerEmitRetainedBreakdown(double processMs, int dirtyCount)
        {
            LastPerformerEmitRetainedProcessMs = (float)processMs;
            PerformerEmitRetainedProcessMs = Smooth(PerformerEmitRetainedProcessMs, (float)processMs);
            PerformerEmitRetainedCountLastFrame = dirtyCount;
        }

        public void ObservePerformerEmitRetainedDirectPath(int directHits, int fullPathCount, int directMisses)
        {
            PerformerEmitRetainedDirectHitsLastFrame = directHits;
            PerformerEmitRetainedFullPathLastFrame = fullPathCount;
            PerformerEmitRetainedDirectMissesLastFrame = directMisses;
        }

        public void ObservePresentationRequestFlush(double sampleMs)
        {
            LastPresentationRequestFlushMs = (float)sampleMs;
            PresentationRequestFlushMs = Smooth(PresentationRequestFlushMs, (float)sampleMs);
        }

        public void ObserveTerrain(double renderMs, double chunkBuildMs, int drawnChunks, int builtChunks)
        {
            LastTerrainRenderMs = (float)renderMs;
            TerrainRenderMs = Smooth(TerrainRenderMs, (float)renderMs);
            TerrainChunkBuildMs = Smooth(TerrainChunkBuildMs, (float)chunkBuildMs);
            TerrainChunksDrawnLastFrame = drawnChunks;
            TerrainChunksBuiltLastFrame = builtChunks;
        }

        public void ObserveTerrainHeightSync(double sampleMs, int sampledCount)
        {
            LastTerrainHeightSyncMs = (float)sampleMs;
            TerrainHeightSyncMs = Smooth(TerrainHeightSyncMs, (float)sampleMs);
            TerrainHeightSamplesLastFrame = sampledCount;
        }

        public void ObservePrimitiveRender(double sampleMs, int instances, int batches)
        {
            ObservePrimitiveRender(sampleMs, instances, batches, 0d, 0d, 0, 0, 0d, 0d, 0d, 0);
        }

        public void ObservePrimitiveRender(
            double sampleMs,
            int instances,
            int batches,
            double matrixBuildMs,
            double meshDrawMs,
            int matrixCacheHits,
            int matrixCacheMisses)
        {
            ObservePrimitiveRender(
                sampleMs,
                instances,
                batches,
                matrixBuildMs,
                meshDrawMs,
                matrixCacheHits,
                matrixCacheMisses,
                0d,
                0d,
                0d,
                0);
        }

        public void ObservePrimitiveRender(
            double sampleMs,
            int instances,
            int batches,
            double matrixBuildMs,
            double meshDrawMs,
            int matrixCacheHits,
            int matrixCacheMisses,
            double persistentSyncMs,
            double persistentBucketDrawMs,
            double immediateDrawMs,
            int immediateSkippedCount,
            int skinnedRawCount = 0,
            int gpuSkinnedInstances = 0,
            int gpuSkinnedBatches = 0,
            double gpuSkinnedMatrixBuildMs = 0d,
            double gpuSkinnedMeshDrawMs = 0d)
        {
            LastPrimitiveRenderMs = (float)sampleMs;
            PrimitiveRenderMs = Smooth(PrimitiveRenderMs, (float)sampleMs);
            LastPrimitiveMatrixBuildMs = (float)matrixBuildMs;
            PrimitiveMatrixBuildMs = Smooth(PrimitiveMatrixBuildMs, (float)matrixBuildMs);
            LastPrimitiveMeshDrawMs = (float)meshDrawMs;
            PrimitiveMeshDrawMs = Smooth(PrimitiveMeshDrawMs, (float)meshDrawMs);
            LastPrimitivePersistentSyncMs = (float)persistentSyncMs;
            PrimitivePersistentSyncMs = Smooth(PrimitivePersistentSyncMs, (float)persistentSyncMs);
            LastPrimitivePersistentBucketDrawMs = (float)persistentBucketDrawMs;
            PrimitivePersistentBucketDrawMs = Smooth(PrimitivePersistentBucketDrawMs, (float)persistentBucketDrawMs);
            LastPrimitiveImmediateDrawMs = (float)immediateDrawMs;
            PrimitiveImmediateDrawMs = Smooth(PrimitiveImmediateDrawMs, (float)immediateDrawMs);
            PrimitiveInstancesLastFrame = instances;
            PrimitiveBatchesLastFrame = batches;
            SkinnedRawLastFrame = skinnedRawCount;
            GpuSkinnedInstancesLastFrame = gpuSkinnedInstances;
            GpuSkinnedBatchesLastFrame = gpuSkinnedBatches;
            LastGpuSkinnedMatrixBuildMs = (float)gpuSkinnedMatrixBuildMs;
            LastGpuSkinnedMeshDrawMs = (float)gpuSkinnedMeshDrawMs;
            PrimitiveMatrixCacheHitsLastFrame = matrixCacheHits;
            PrimitiveMatrixCacheMissesLastFrame = matrixCacheMisses;
            PrimitiveImmediateSkippedLastFrame = immediateSkippedCount;
        }

        public void ObserveHostPreTick(double sampleMs)
        {
            LastHostPreTickMs = (float)sampleMs;
            HostPreTickMs = Smooth(HostPreTickMs, (float)sampleMs);
        }

        public void ObserveHostPostTick(double sampleMs)
        {
            LastHostPostTickMs = (float)sampleMs;
            HostPostTickMs = Smooth(HostPostTickMs, (float)sampleMs);
        }

        public void ObserveBeginDrawing(double sampleMs)
        {
            LastBeginDrawingMs = (float)sampleMs;
            BeginDrawingMs = Smooth(BeginDrawingMs, (float)sampleMs);
        }

        public void ObserveMode3D(double sampleMs)
        {
            LastMode3DMs = (float)sampleMs;
            Mode3DMs = Smooth(Mode3DMs, (float)sampleMs);
        }

        public void ObserveGroundOverlayRender(double sampleMs, int count)
        {
            LastGroundOverlayRenderMs = (float)sampleMs;
            GroundOverlayRenderMs = Smooth(GroundOverlayRenderMs, (float)sampleMs);
            GroundOverlaysLastFrame = count;
        }

        public void ObserveRoadSplineRender(double sampleMs, int count)
        {
            LastRoadSplineRenderMs = (float)sampleMs;
            RoadSplineRenderMs = Smooth(RoadSplineRenderMs, (float)sampleMs);
            RoadSplinesLastFrame = count;
        }

        public void ObserveDebugDrawRender(double sampleMs, int count)
        {
            LastDebugDrawRenderMs = (float)sampleMs;
            DebugDrawRenderMs = Smooth(DebugDrawRenderMs, (float)sampleMs);
            DebugDrawCommandsLastFrame = count;
        }

        public void ObserveNativeDiagnosticHud(double sampleMs)
        {
            LastNativeDiagnosticHudMs = (float)sampleMs;
            NativeDiagnosticHudMs = Smooth(NativeDiagnosticHudMs, (float)sampleMs);
        }

        public void ObserveHostLoopGap(double sampleMs)
        {
            LastHostLoopGapMs = (float)sampleMs;
            HostLoopGapMs = Smooth(HostLoopGapMs, (float)sampleMs);
        }

        public void ObserveWindowPoll(double sampleMs)
        {
            LastWindowPollMs = (float)sampleMs;
            WindowPollMs = Smooth(WindowPollMs, (float)sampleMs);
        }

        public void ObserveCompositeSkip(bool skipped)
        {
            if (skipped) _compositeSkipAccumulator++;
            _compositeFrameAccumulator++;
            if (_compositeFrameAccumulator >= CompositeWindowSize)
            {
                CompositeSkipCountLastSecond = _compositeSkipAccumulator;
                _compositeSkipAccumulator = 0;
                _compositeFrameAccumulator = 0;
            }
        }

        private static float Smooth(float current, float sampleMs)
        {
            if (sampleMs < 0f)
            {
                sampleMs = 0f;
            }

            return current <= 0.001f
                ? sampleMs
                : (current * (1f - SampleWeight)) + (sampleMs * SampleWeight);
        }

        private static void InsertTopSystem(
            string systemName,
            float sampleMs,
            ref string top1Name,
            ref float top1Ms,
            ref string top2Name,
            ref float top2Ms,
            ref string top3Name,
            ref float top3Ms)
        {
            if (sampleMs <= 0f)
            {
                return;
            }

            if (sampleMs > top1Ms)
            {
                top3Name = top2Name;
                top3Ms = top2Ms;
                top2Name = top1Name;
                top2Ms = top1Ms;
                top1Name = systemName;
                top1Ms = sampleMs;
                return;
            }

            if (sampleMs > top2Ms)
            {
                top3Name = top2Name;
                top3Ms = top2Ms;
                top2Name = systemName;
                top2Ms = sampleMs;
                return;
            }

            if (sampleMs > top3Ms)
            {
                top3Name = systemName;
                top3Ms = sampleMs;
            }
        }
    }
}
