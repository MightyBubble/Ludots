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
            int clearTransient)
        {
            VisualProxy = RequirePositive(visualProxy, nameof(visualProxy));
            GroundOverlay = RequirePositive(groundOverlay, nameof(groundOverlay));
            WorldHud = RequirePositive(worldHud, nameof(worldHud));
            SplineRibbon = RequirePositive(splineRibbon, nameof(splineRibbon));
            SurfaceSource = RequirePositive(surfaceSource, nameof(surfaceSource));
            Removal = RequirePositive(removal, nameof(removal));
            ClearTransient = RequirePositive(clearTransient, nameof(clearTransient));
        }

        public int VisualProxy { get; }
        public int GroundOverlay { get; }
        public int WorldHud { get; }
        public int SplineRibbon { get; }
        public int SurfaceSource { get; }
        public int Removal { get; }
        public int ClearTransient { get; }

        public int TotalOperationCapacity =>
            checked(VisualProxy + GroundOverlay + WorldHud + SplineRibbon + SurfaceSource + Removal + ClearTransient);

        public static PresentationRequestChannelCapacities From(Ludots.Core.Presentation.PresentationRuntimeConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            int overlay = config.GroundOverlayCapacity;
            int hud = config.WorldHudCapacity;
            int ribbon = config.SplineRibbonCapacity;
            int instances = config.PresenterInstanceCapacity;
            return new PresentationRequestChannelCapacities(
                visualProxy: config.VisualProxyBufferCapacity,
                groundOverlay: overlay,
                worldHud: hud,
                splineRibbon: ribbon,
                surfaceSource: instances,
                removal: checked(overlay + hud + ribbon + instances),
                clearTransient: instances);
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
                clearTransient: capacityPerChannel);
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
