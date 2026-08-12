using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Minimal upstream phase input contract for presenter-facing projection decisions.
    /// This struct contains normalized facts only; raw world state translation belongs in PresentPhaseResolver.
    /// </summary>
    public struct PresentPhaseInput
    {
        public PresentAudienceContext Audience;
        public Entity Owner;
        public Team OwnerTeam;
        public PlayerOwner OwnerOwner;
        public bool HasOwnerTeam;
        public bool HasOwnerOwner;
        public bool IsVisible;
        public bool IsCulled;
        public bool HasVision;
        public bool RequiresAttributeProjection;
        public bool HasAttributeProjection;
        public bool AllowVisibleTransientWorldText;
        public bool HasRelationshipLink;
        public bool HasTeamRelationship;
        public bool IsOwnedByAudience;
        public TeamRelationship TeamRelationship;
        public PresentProjectionFacts Projection;
        public LODLevel LOD;
    }
}
