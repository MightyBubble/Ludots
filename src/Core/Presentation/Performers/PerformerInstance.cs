using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Runtime state of a single performer instance. Managed by
    /// <see cref="PerformerInstanceBuffer"/>.
    ///
    /// Design: instances do NOT cache resolved parameter values (Position, Size, Color).
    /// All visual parameters are resolved each frame from declarative bindings by the
    /// PerformerEmitSystem. This guarantees data freshness after off-screen → on-screen
    /// transitions.
    /// </summary>
    public struct PerformerInstance
    {
        /// <summary>The PerformerDefinition ID this instance was created from.</summary>
        public int DefId;

        /// <summary>The entity this performer is attached to.</summary>
        public Entity Owner;

        /// <summary>
        /// Scope group ID. Instances sharing a ScopeId can be destroyed together
        /// via DestroyPerformerScope. -1 = no scope (standalone).
        /// </summary>
        public int ScopeId;

        /// <summary>
        /// Stable presentation id used by adapter-side instance maps.
        /// </summary>
        public int StableId;

        /// <summary>
        /// Entity anchor vs world anchor mapping.
        /// </summary>
        public PresentationAnchorKind AnchorKind;

        /// <summary>
        /// World-space anchor for instances that do not bind to an ECS entity.
        /// </summary>
        public Vector3 WorldPosition;

        /// <summary>
        /// Cached world-space rotation resolved by the runtime transform pipeline.
        /// </summary>
        public Quaternion WorldRotation;

        /// <summary>
        /// Cached world-space scale resolved by the runtime transform pipeline.
        /// </summary>
        public Vector3 WorldScale;

        /// <summary>
        /// Time elapsed since creation (seconds). Always advances regardless of
        /// visibility state, so duration-based performers expire on time and
        /// time-based animations stay in sync.
        /// </summary>
        public float Elapsed;

        /// <summary>
        /// Declares how this instance's transform should be resolved.
        /// </summary>
        public TransformSource TransformSource;

        /// <summary>
        /// Handle of the parent performer in the instance buffer. -1 means root.
        /// </summary>
        public int ParentHandle;

        /// <summary>
        /// Handle of the first child performer in the instance buffer. -1 means leaf.
        /// </summary>
        public int FirstChildHandle;

        /// <summary>
        /// Handle of the next sibling performer in the instance buffer. -1 means end.
        /// </summary>
        public int NextSiblingHandle;

        /// <summary>
        /// Bitmask of active behavior slots. Bit=1 means active.
        /// </summary>
        public uint BehaviorActiveMask;

        /// <summary>
        /// Hot-path visibility gate copied from the owner entity CullState.
        /// Child performers inherit their parent's effective gate.
        /// </summary>
        public bool OwnerCullVisible;

        /// <summary>
        /// Monotonic mutation version for cheap runtime-side cache invalidation.
        /// </summary>
        public int Version;

        /// <summary>Whether this slot is in use.</summary>
        public bool Active;
    }

    public enum TransformSource : byte
    {
        InheritParent = 0,
        EntityTransform = 1,
        SplineDriven = 2,
        BoneAttached = 3,
        AttachedToParent = 4,
        WorldFixed = 5,
    }
}
