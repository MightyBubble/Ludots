using System;

namespace Ludots.Core.Presentation
{
    /// <summary>
    /// Engine-level presentation runtime capacity knobs merged from game.json.
    /// Defaults are sized for production-scale presentation scenes rather than tiny unit-test maps.
    /// </summary>
    public sealed class PresentationRuntimeConfig
    {
        public int PerformerInstanceCapacity { get; set; } = 32768;
        public int PresentationEventStreamCapacity { get; set; } = 65536;
        public int PerformerCommandCapacity { get; set; } = 262144;
        public int PrimitiveDrawBufferCapacity { get; set; } = 131072;
        public int VisualSnapshotBufferCapacity { get; set; } = 131072;
        public int VisualProxyBufferCapacity { get; set; } = 131072;
        public int SkinnedVisualBatchCapacity { get; set; } = 65536;
        public int PresentationRequestCapacity { get; set; } = 262144;
        public int GroundOverlayCapacity { get; set; } = 65536;
        public int RoadSplineCapacity { get; set; } = 65536;
        public int WorldHudCapacity { get; set; } = 131072;
        public int ScreenHudCapacity { get; set; } = 131072;
        public int MinimapMarkerCapacity { get; set; } = 131072;
        public int RuntimeEntitySpawnQueueCapacity { get; set; } = 131072;
        public CameraCullingRuntimeConfig CameraCulling { get; set; } = new CameraCullingRuntimeConfig();

        public int GetEffectivePerformerInstanceCapacity()
        {
            return PerformerInstanceCapacity > 0 ? PerformerInstanceCapacity : 32768;
        }

        public int GetEffectivePresentationEventStreamCapacity()
        {
            return PresentationEventStreamCapacity > 0 ? PresentationEventStreamCapacity : 65536;
        }

        public int GetEffectivePerformerCommandCapacity()
        {
            return PerformerCommandCapacity > 0 ? PerformerCommandCapacity : 262144;
        }

        public int GetEffectivePrimitiveDrawBufferCapacity()
        {
            return PrimitiveDrawBufferCapacity > 0 ? PrimitiveDrawBufferCapacity : 131072;
        }

        public int GetEffectiveVisualSnapshotBufferCapacity()
        {
            return VisualSnapshotBufferCapacity > 0 ? VisualSnapshotBufferCapacity : 131072;
        }

        public int GetEffectiveVisualProxyBufferCapacity()
        {
            return VisualProxyBufferCapacity > 0 ? VisualProxyBufferCapacity : 131072;
        }

        public int GetEffectiveSkinnedVisualBatchCapacity()
        {
            return SkinnedVisualBatchCapacity > 0 ? SkinnedVisualBatchCapacity : 65536;
        }

        public int GetEffectivePresentationRequestCapacity()
        {
            return PresentationRequestCapacity > 0 ? PresentationRequestCapacity : 262144;
        }

        public int GetEffectiveGroundOverlayCapacity()
        {
            return GroundOverlayCapacity > 0 ? GroundOverlayCapacity : 65536;
        }

        public int GetEffectiveRoadSplineCapacity()
        {
            return RoadSplineCapacity > 0 ? RoadSplineCapacity : 65536;
        }

        public int GetEffectiveWorldHudCapacity()
        {
            return WorldHudCapacity > 0 ? WorldHudCapacity : 131072;
        }

        public int GetEffectiveScreenHudCapacity()
        {
            return ScreenHudCapacity > 0 ? ScreenHudCapacity : 131072;
        }

        public int GetEffectiveMinimapMarkerCapacity()
        {
            return MinimapMarkerCapacity > 0 ? MinimapMarkerCapacity : 131072;
        }

        public int GetEffectiveRuntimeEntitySpawnQueueCapacity()
        {
            return RuntimeEntitySpawnQueueCapacity > 0 ? RuntimeEntitySpawnQueueCapacity : 131072;
        }
    }

    public sealed class CameraCullingRuntimeConfig
    {
        public float HighLodDistanceCm { get; set; } = 4000f;
        public float MediumLodDistanceCm { get; set; } = 10000f;
        public float LowLodDistanceCm { get; set; } = 20000f;

        public void Validate()
        {
            if (!float.IsFinite(HighLodDistanceCm) ||
                !float.IsFinite(MediumLodDistanceCm) ||
                !float.IsFinite(LowLodDistanceCm) ||
                HighLodDistanceCm <= 0f ||
                MediumLodDistanceCm <= HighLodDistanceCm ||
                LowLodDistanceCm <= MediumLodDistanceCm)
            {
                throw new InvalidOperationException(
                    "presentation.cameraCulling requires positive lod distances with high < medium < low.");
            }
        }
    }
}
