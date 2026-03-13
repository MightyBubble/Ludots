namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Lightweight presentation timing samples shared between adapters and debug HUDs.
    /// Values are exponentially smoothed so manual toggle experiments are easier to read.
    /// </summary>
    public sealed class PresentationTimingDiagnostics
    {
        private const float SampleWeight = 0.18f;

        public float UiInputMs { get; private set; }
        public float UiRenderMs { get; private set; }
        public float UiUploadMs { get; private set; }
        public float ScreenOverlayDrawMs { get; private set; }
        public float HudTextDrawMs { get; private set; }
        public float CameraCullingMs { get; private set; }
        public float CameraPresenterMs { get; private set; }
        public float WorldHudProjectionMs { get; private set; }
        public float TerrainRenderMs { get; private set; }
        public float TerrainChunkBuildMs { get; private set; }
        public float PrimitiveRenderMs { get; private set; }

        public int HudTextItemsLastFrame { get; private set; }
        public int VisibleEntitiesLastFrame { get; private set; }
        public int TerrainChunksDrawnLastFrame { get; private set; }
        public int TerrainChunksBuiltLastFrame { get; private set; }
        public int PrimitiveInstancesLastFrame { get; private set; }
        public int PrimitiveBatchesLastFrame { get; private set; }

        public void ObserveUiInput(double sampleMs) => UiInputMs = Smooth(UiInputMs, (float)sampleMs);
        public void ObserveUiRender(double sampleMs) => UiRenderMs = Smooth(UiRenderMs, (float)sampleMs);
        public void ObserveUiUpload(double sampleMs) => UiUploadMs = Smooth(UiUploadMs, (float)sampleMs);
        public void ObserveScreenOverlayDraw(double sampleMs) => ScreenOverlayDrawMs = Smooth(ScreenOverlayDrawMs, (float)sampleMs);
        public void ObserveHudTextDraw(double sampleMs, int itemCount)
        {
            HudTextDrawMs = Smooth(HudTextDrawMs, (float)sampleMs);
            HudTextItemsLastFrame = itemCount < 0 ? 0 : itemCount;
        }

        public void ObserveCameraCulling(double sampleMs, int visibleEntities)
        {
            CameraCullingMs = Smooth(CameraCullingMs, (float)sampleMs);
            VisibleEntitiesLastFrame = visibleEntities;
        }

        public void ObserveCameraPresenter(double sampleMs) => CameraPresenterMs = Smooth(CameraPresenterMs, (float)sampleMs);
        public void ObserveWorldHudProjection(double sampleMs) => WorldHudProjectionMs = Smooth(WorldHudProjectionMs, (float)sampleMs);

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
