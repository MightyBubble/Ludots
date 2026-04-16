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
            return new PresentationRequest
            {
                Kind = PresentationRequestKind.WorldHud,
                Owner = owner,
                WorldHud = item,
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
    }
}
