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

        public float UiInputMs { get; private set; }
        public float UiRenderMs { get; private set; }
        public float UiUploadMs { get; private set; }
        public float ScreenOverlayBuildMs { get; private set; }
        public float ScreenOverlayDrawMs { get; private set; }
        public float CameraCullingMs { get; private set; }
        public float CameraPresenterMs { get; private set; }
        public float WorldHudProjectionMs { get; private set; }
        public float SimulationMs { get; private set; }
        public float PresentationMs { get; private set; }
        public float TotalTickMs { get; private set; }
        public float PerformerBehaviorMs { get; private set; }
        public float PerformerAnimatorMs { get; private set; }
        public float PerformerEmitMs { get; private set; }
        public float PresentationRequestFlushMs { get; private set; }
        public float TerrainRenderMs { get; private set; }
        public float TerrainChunkBuildMs { get; private set; }
        public float PrimitiveRenderMs { get; private set; }

        public int VisibleEntitiesLastFrame { get; private set; }
        public int ScreenOverlayDirtyLanesLastFrame { get; private set; }
        public int ScreenOverlayItemsLastFrame { get; private set; }
        public int ScreenOverlayRebuiltLanesLastFrame { get; private set; }
        public int ScreenOverlayTextLayoutCacheCount { get; private set; }
        public int TerrainChunksDrawnLastFrame { get; private set; }
        public int TerrainChunksBuiltLastFrame { get; private set; }
        public int PrimitiveInstancesLastFrame { get; private set; }
        public int PrimitiveBatchesLastFrame { get; private set; }
        public int CompositeSkipCountLastSecond { get; private set; }

        public void ObserveUiInput(double sampleMs) => UiInputMs = Smooth(UiInputMs, (float)sampleMs);
        public void ObserveUiRender(double sampleMs) => UiRenderMs = Smooth(UiRenderMs, (float)sampleMs);
        public void ObserveUiUpload(double sampleMs) => UiUploadMs = Smooth(UiUploadMs, (float)sampleMs);
        public void ObserveScreenOverlayBuild(double sampleMs, int dirtyLanes, int totalItems)
        {
            ScreenOverlayBuildMs = Smooth(ScreenOverlayBuildMs, (float)sampleMs);
            ScreenOverlayDirtyLanesLastFrame = dirtyLanes;
            ScreenOverlayItemsLastFrame = totalItems;
        }

        public void ObserveScreenOverlayDraw(double sampleMs, int rebuiltLanes, int textLayoutCacheCount)
        {
            ScreenOverlayDrawMs = Smooth(ScreenOverlayDrawMs, (float)sampleMs);
            ScreenOverlayRebuiltLanesLastFrame = rebuiltLanes;
            ScreenOverlayTextLayoutCacheCount = textLayoutCacheCount;
        }

        public void ObserveCameraCulling(double sampleMs, int visibleEntities)
        {
            CameraCullingMs = Smooth(CameraCullingMs, (float)sampleMs);
            VisibleEntitiesLastFrame = visibleEntities;
        }

        public void ObserveCameraPresenter(double sampleMs) => CameraPresenterMs = Smooth(CameraPresenterMs, (float)sampleMs);
        public void ObserveWorldHudProjection(double sampleMs) => WorldHudProjectionMs = Smooth(WorldHudProjectionMs, (float)sampleMs);
        public void ObserveSimulation(double sampleMs) => SimulationMs = Smooth(SimulationMs, (float)sampleMs);
        public void ObservePresentation(double sampleMs) => PresentationMs = Smooth(PresentationMs, (float)sampleMs);
        public void ObserveTotalTick(double sampleMs) => TotalTickMs = Smooth(TotalTickMs, (float)sampleMs);
        public void ObservePerformerBehavior(double sampleMs) => PerformerBehaviorMs = Smooth(PerformerBehaviorMs, (float)sampleMs);
        public void ObservePerformerAnimator(double sampleMs) => PerformerAnimatorMs = Smooth(PerformerAnimatorMs, (float)sampleMs);
        public void ObservePerformerEmit(double sampleMs) => PerformerEmitMs = Smooth(PerformerEmitMs, (float)sampleMs);
        public void ObservePresentationRequestFlush(double sampleMs) => PresentationRequestFlushMs = Smooth(PresentationRequestFlushMs, (float)sampleMs);

        public void ObserveTerrain(double renderMs, double chunkBuildMs, int drawnChunks, int builtChunks)
        {
            TerrainRenderMs = Smooth(TerrainRenderMs, (float)renderMs);
            TerrainChunkBuildMs = Smooth(TerrainChunkBuildMs, (float)chunkBuildMs);
            TerrainChunksDrawnLastFrame = drawnChunks;
            TerrainChunksBuiltLastFrame = builtChunks;
        }

        public void ObservePrimitiveRender(double sampleMs, int instances, int batches)
        {
            PrimitiveRenderMs = Smooth(PrimitiveRenderMs, (float)sampleMs);
            PrimitiveInstancesLastFrame = instances;
            PrimitiveBatchesLastFrame = batches;
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
    }
}
