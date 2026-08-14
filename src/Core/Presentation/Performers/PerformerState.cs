using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    public struct PerformerState
    {
        public int DefId;
        public int StableId;
        public int OwnerStableId;
        public int ScopeId;
        public Entity OwnerEntity;
        public PresentationAnchorKind AnchorKind;
        public uint BehaviorActiveMask;
        public float Elapsed;
        public int Version;
        public float DefaultLifetime;
    }
}
