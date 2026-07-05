using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Input.Selection;
using Ludots.Core.Knowledge;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Default knowledge gate (RFC-0065 INT-2/INT-4): wraps
    /// <see cref="SelectionEligibility.CanTargetCommand"/> so command-intent routing and ContextScored
    /// candidate filtering share the hover-target gating semantics. The viewer rep is passed through as
    /// the explicit knowledge viewer (no global viewer fallback); when no
    /// <c>KnowledgeProjectionResolver</c> is registered in globals, <c>CanTargetCommand</c> allows every
    /// live entity — the same assembly semantics as the existing hover path.
    /// <see cref="CanTarget"/> matches both <see cref="CommandIntentTargetGate"/> and
    /// <see cref="Ludots.Core.Input.Orders.ContextScoredCandidateGate"/>. Allocation free per call.
    /// </summary>
    public sealed class KnowledgeCommandTargetGate
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly KnowledgePositionAccess _requiredPosition;

        public KnowledgeCommandTargetGate(
            World world,
            Dictionary<string, object> globals,
            KnowledgePositionAccess requiredPosition = KnowledgePositionAccess.Live)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _requiredPosition = requiredPosition;
        }

        /// <summary>True when <paramref name="viewerRep"/> may command-target <paramref name="target"/>.</summary>
        public bool CanTarget(Entity viewerRep, Entity target)
        {
            return SelectionEligibility.CanTargetCommand(_world, _globals, viewerRep, target, _requiredPosition);
        }
    }
}
