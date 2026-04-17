using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;

namespace Ludots.Core.Presentation.Perform
{
    /// <summary>
    /// Runtime state of a single perform behavior instance.
    /// Ownership stays separate from assets and adapter output.
    /// </summary>
    public struct PerformBehaviorInstance
    {
        public int DefinitionId;
        public int ScopeId;
        public int StableId;
        public Entity Owner;
        public PresentationAnchorKind AnchorKind;
        public Vector3 WorldPosition;
        public float Elapsed;
        public bool Active;
    }
}
