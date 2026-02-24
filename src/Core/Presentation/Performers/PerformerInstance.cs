using Arch.Core;

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
        /// Time elapsed since creation (seconds). Always advances regardless of
        /// visibility state, so duration-based performers expire on time and
        /// time-based animations stay in sync.
        /// </summary>
        public float Elapsed;

        /// <summary>Whether this slot is in use.</summary>
        public bool Active;
    }
}
