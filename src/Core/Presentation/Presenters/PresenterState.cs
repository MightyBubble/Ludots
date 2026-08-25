using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Presenters
{
    public struct PresenterState
    {
        public int DefId;
        public int StableId;
        public int ScopeId;
        public Entity OwnerEntity;
        public PresentationAnchorKind AnchorKind;
        public uint BehaviorActiveMask;
        public float Elapsed;
        public int Version;

        /// <summary>
        /// Instance of a lifecycle.durationSeconds definition: bounded by its compiled
        /// timer chain, therefore excluded from persistent scoped-instance reuse.
        /// </summary>
        public bool Transient;
    }
}
