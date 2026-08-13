using Arch.Core;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Requests
{
    public enum PresentationRequestChannel : byte
    {
        VisualProxy = 1,
        GroundOverlay = 3,
        WorldHud = 4,
        SplineRibbon = 5,
        SurfaceSource = 6,
        Removal = 7,
        ClearTransient = 8,
    }

    public readonly struct PresentationRequestOp
    {
        public PresentationRequestOp(PresentationRequestChannel channel, int slot)
        {
            Channel = channel;
            Slot = slot;
        }

        public PresentationRequestChannel Channel { get; }
        public int Slot { get; }
    }

    public struct VisualProxyChannelItem
    {
        public Entity Owner;
        public PresentationVisualProxy VisualProxy;
    }

    public struct GroundOverlayChannelItem
    {
        public Entity Owner;
        public LODLevel LOD;
        public GroundOverlayItem Item;
    }

    public struct WorldHudChannelItem
    {
        public Entity Owner;
        public LODLevel LOD;
        public WorldHudItem Item;
    }

    public struct SplineRibbonChannelItem
    {
        public Entity Owner;
        public LODLevel LOD;
        public SplineRibbonRequest Item;
    }

    public struct SurfaceSourceChannelItem
    {
        public Entity Owner;
        public LODLevel LOD;
        public SurfaceSourceRequest Item;
    }

    public struct PresentationRemovalRequest
    {
        public PresentationRequestKind Kind;
        public Entity Owner;
        public int StableId;
    }
}
