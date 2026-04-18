using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Minimal upstream phase input contract for performer-facing projection decisions.
    /// This struct contains normalized facts only; raw world state translation belongs in PerformPhaseResolver.
    /// </summary>
    public struct PerformPhaseInput
    {
        public PerformAudienceContext Audience;
        public Entity Owner;
        public Team OwnerTeam;
        public PlayerOwner OwnerOwner;
        public bool HasOwnerTeam;
        public bool HasOwnerOwner;
        public bool IsVisible;
        public bool IsCulled;
        public bool HasVision;
        public bool HasRelationshipLink;
        public bool HasTeamRelationship;
        public bool IsOwnedByAudience;
        public TeamRelationship TeamRelationship;
        public LODLevel LOD;
    }
}
