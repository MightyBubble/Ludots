using Ludots.Core.Presentation.Components;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    public struct PresentPhaseResult
    {
        public bool ShouldPresent;
        public bool AllowWorldHudProjection;
        public bool IsVisible;
        public bool IsCulled;
        public bool HasVision;
        public bool HasKnowledgeProjection;
        public bool RequiresAttributeProjection;
        public bool HasAttributeProjection;
        public LODLevel LOD;
        public bool IsOwnedByAudience;
        public bool IsFriendly;
        public bool IsHostile;
    }
}
