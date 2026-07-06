using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Knowledge;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Default knowledge gate (RFC-0065 INT-2/INT-4): wraps the explicit-resolver
    /// <see cref="SelectionEligibility.CanTargetCommand(World, KnowledgeProjectionResolver, int, Entity, Entity, KnowledgePositionAccess)"/>
    /// so command-intent routing and ContextScored candidate filtering share the hover-target gating
    /// semantics. The resolver and clock are hard constructor dependencies: a missing
    /// <see cref="KnowledgeProjectionResolver"/> is a startup failure, never an allow-all fallback.
    /// The viewer rep is passed through as the explicit knowledge viewer (no global viewer fallback).
    /// <see cref="CanTarget"/> matches both <see cref="CommandIntentTargetGate"/> and
    /// <see cref="Ludots.Core.Input.Orders.ContextScoredCandidateGate"/>. Allocation free per call.
    /// </summary>
    public sealed class KnowledgeCommandTargetGate
    {
        private readonly World _world;
        private readonly KnowledgeProjectionResolver _resolver;
        private readonly IClock _clock;
        private readonly KnowledgePositionAccess _requiredPosition;

        public KnowledgeCommandTargetGate(
            World world,
            KnowledgeProjectionResolver resolver,
            IClock clock,
            KnowledgePositionAccess requiredPosition = KnowledgePositionAccess.Live)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _requiredPosition = requiredPosition;
        }

        /// <summary>True when <paramref name="viewerRep"/> may command-target <paramref name="target"/>.</summary>
        public bool CanTarget(Entity viewerRep, Entity target)
        {
            return SelectionEligibility.CanTargetCommand(
                _world,
                _resolver,
                _clock.Now(ClockDomainId.Step),
                viewerRep,
                target,
                _requiredPosition);
        }
    }
}
