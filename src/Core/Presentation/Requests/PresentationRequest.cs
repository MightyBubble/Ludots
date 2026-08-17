using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Requests
{
    public struct PresentationRequest
    {
        public PresentationRequestKind Kind;
        public Entity Owner;
        public LODLevel LOD;
        public PresentationVisualProxy VisualProxy;
        public int StableId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Vector4 Color;
        public GroundOverlayItem GroundOverlay;
        public WorldHudItem WorldHud;
        public SplineRibbonRequest SplineRibbon;
        public SurfaceSourceRequest SurfaceSource;

        public static PresentationRequest FromVisualProxy(Entity owner, in PresentationVisualProxy proxy)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.VisualProxy,
                Owner = owner,
                LOD = proxy.LOD,
                VisualProxy = proxy,
            };
        }

        public static PresentationRequest FromGroundOverlay(Entity owner, in GroundOverlayItem item, LODLevel lod)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.GroundOverlay,
                Owner = owner,
                GroundOverlay = item,
                LOD = lod,
            };
        }

        public static PresentationRequest FromWorldHud(Entity owner, in WorldHudItem item, LODLevel lod)
        {
            WorldHudItem ownedItem = item;
            ownedItem.Owner = owner;
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.WorldHud,
                Owner = owner,
                WorldHud = ownedItem,
                LOD = lod,
            };
        }

        public static PresentationRequest FromSplineRibbon(Entity owner, in SplineRibbonRequest spline, LODLevel lod)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.SplineRibbon,
                Owner = owner,
                SplineRibbon = spline,
                LOD = lod,
            };
        }

        public static PresentationRequest FromSurfaceSource(Entity owner, in SurfaceSourceRequest surfaceSource, LODLevel lod)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.SurfaceSource,
                Owner = owner,
                StableId = surfaceSource.StableId,
                SurfaceSource = surfaceSource,
                LOD = lod,
            };
        }

        public static PresentationRequest RemoveGroundOverlay(Entity owner, int stableId)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.RemoveGroundOverlay,
                Owner = owner,
                StableId = stableId,
            };
        }

        public static PresentationRequest RemoveWorldHud(Entity owner, int stableId)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.RemoveWorldHud,
                Owner = owner,
                StableId = stableId,
            };
        }

        public static PresentationRequest RemoveSplineRibbon(Entity owner, int stableId)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.RemoveSplineRibbon,
                Owner = owner,
                StableId = stableId,
            };
        }

        public static PresentationRequest RemoveSurfaceSource(Entity owner, int stableId)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.RemoveSurfaceSource,
                Owner = owner,
                StableId = stableId,
            };
        }

        public static PresentationRequest ClearTransientVisualProjection(Entity owner)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.ClearTransientVisualProjection,
                Owner = owner,
            };
        }
    }
}
