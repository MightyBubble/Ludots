using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Perform
{
    /// <summary>
    /// Resolved performer-facing phase result derived from normalized audience and ownership facts.
    /// </summary>
    public struct PerformPhaseResult
    {
        public bool ShouldPresent;
        public bool AllowWorldHudProjection;
        public bool IsVisible;
        public bool IsCulled;
        public bool HasVision;
        public LODLevel LOD;
        public TeamRelationship TeamRelationship;
        public bool IsOwnedByAudience;
        public bool HasRelationshipLink;
        public bool IsFriendly;
        public bool IsHostile;
    }
}
