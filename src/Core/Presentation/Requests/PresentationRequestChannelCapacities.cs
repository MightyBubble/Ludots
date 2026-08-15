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
            int? totalOperationCapacity = null)
        {
            VisualProxy = RequirePositive(visualProxy, nameof(visualProxy));
            GroundOverlay = RequirePositive(groundOverlay, nameof(groundOverlay));
            WorldHud = RequirePositive(worldHud, nameof(worldHud));
            SplineRibbon = RequirePositive(splineRibbon, nameof(splineRibbon));
            SurfaceSource = RequirePositive(surfaceSource, nameof(surfaceSource));
            Removal = RequirePositive(removal, nameof(removal));
            ClearTransient = RequirePositive(clearTransient, nameof(clearTransient));
            TotalOperationCapacity = RequirePositive(
                totalOperationCapacity ?? checked(visualProxy + groundOverlay + worldHud + splineRibbon + surfaceSource + removal + clearTransient),
                nameof(totalOperationCapacity));
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
            int request = config.PresentationRequestCapacity;
            int overlay = Min(config.GroundOverlayCapacity, request);
            int hud = Min(config.WorldHudCapacity, request);
            int ribbon = Min(config.SplineRibbonCapacity, request);
            int instances = Min(config.PresenterInstanceCapacity, request);
            int clearTransient = Min(config.ClearTransientVisualProjectionCapacity, request);
            return new PresentationRequestChannelCapacities(
                visualProxy: Min(config.VisualProxyBufferCapacity, request),
                groundOverlay: overlay,
                worldHud: hud,
                splineRibbon: ribbon,
                surfaceSource: instances,
                removal: Min(checked(overlay + hud + ribbon + instances), request),
                clearTransient: clearTransient,
                totalOperationCapacity: request);
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
                totalOperationCapacity: capacityPerChannel);
        }

        private static int RequirePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name, value, "Channel capacity must be > 0.");
            }

            return value;
        }

        private static int Min(int left, int right) => left < right ? left : right;
    }
}
