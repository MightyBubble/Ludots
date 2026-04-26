using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Requests
{
    public struct PresentationRequest
    {
        public PresentationRequestKind Kind;
        public Entity Owner;
        public LODLevel LOD;
        public PresentationVisualProxy VisualProxy;
        public int PrefabId;
        public int StableId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Vector4 Color;
        public PrefabFinalizationContext PrefabContext;
        public GroundOverlayItem GroundOverlay;
        public WorldHudItem WorldHud;
        public RoadSplineRequest RoadSpline;
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

        public static PresentationRequest FromPrefab(
            Entity owner,
            int prefabId,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            LODLevel lod,
            in PrefabFinalizationContext context)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.Prefab,
                Owner = owner,
                PrefabId = prefabId,
                StableId = stableId,
                Position = position,
                Rotation = rotation,
                Scale = scale,
                Color = color,
                LOD = lod,
                PrefabContext = context,
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

        public static PresentationRequest FromRoadSpline(Entity owner, in RoadSplineRequest spline, LODLevel lod)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.RoadSpline,
                Owner = owner,
                RoadSpline = spline,
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

        public static PresentationRequest RemoveRoadSpline(Entity owner, int stableId)
        {
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.RemoveRoadSpline,
                Owner = owner,
                StableId = stableId,
            };
        }
    }
}
