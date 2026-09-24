using System;

namespace Ludots.Core.Presentation.Requests
{
    public readonly struct PresentationRequestChannelCapacities
    {
        public PresentationRequestChannelCapacities(
            int visualProxy,
            int groundOverlay,
            int worldHud,
            int splineRibbon,
            int surfaceSource,
            int removal,
            int clearTransient,
            int operation)
        {
            VisualProxy = RequirePositive(visualProxy, nameof(visualProxy));
            GroundOverlay = RequirePositive(groundOverlay, nameof(groundOverlay));
            WorldHud = RequirePositive(worldHud, nameof(worldHud));
            SplineRibbon = RequirePositive(splineRibbon, nameof(splineRibbon));
            SurfaceSource = RequirePositive(surfaceSource, nameof(surfaceSource));
            Removal = RequirePositive(removal, nameof(removal));
            ClearTransient = RequirePositive(clearTransient, nameof(clearTransient));
            TotalOperationCapacity = RequirePositive(operation, nameof(operation));
        }

        public int VisualProxy { get; }
        public int GroundOverlay { get; }
        public int WorldHud { get; }
        public int SplineRibbon { get; }
        public int SurfaceSource { get; }
        public int Removal { get; }
        public int ClearTransient { get; }

        public int TotalOperationCapacity { get; }

        public static PresentationRequestChannelCapacities From(Ludots.Core.Presentation.PresentationRuntimeConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            int overlay = config.GroundOverlayCapacity;
            int hud = config.WorldHudCapacity;
            int ribbon = config.SplineRibbonCapacity;
            int instances = config.PresenterInstanceCapacity;
            int requests = RequirePositive(config.PresentationRequestCapacity, nameof(config.PresentationRequestCapacity));
            return new PresentationRequestChannelCapacities(
                visualProxy: Math.Min(config.VisualProxyBufferCapacity, requests),
                groundOverlay: Math.Min(overlay, requests),
                worldHud: Math.Min(hud, requests),
                splineRibbon: Math.Min(ribbon, requests),
                surfaceSource: Math.Min(instances, requests),
                removal: requests,
                clearTransient: Math.Min(instances, requests),
                operation: requests);
        }

        public static PresentationRequestChannelCapacities Uniform(int capacityPerChannel)
        {
            return new PresentationRequestChannelCapacities(
                visualProxy: capacityPerChannel,
                groundOverlay: capacityPerChannel,
                worldHud: capacityPerChannel,
                splineRibbon: capacityPerChannel,
                surfaceSource: capacityPerChannel,
                removal: capacityPerChannel,
                clearTransient: capacityPerChannel,
                operation: capacityPerChannel);
        }

        private static int RequirePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name, value, "Channel capacity must be > 0.");
            }

            return value;
        }
    }
}
