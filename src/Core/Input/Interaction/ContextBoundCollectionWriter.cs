using System;
using Arch.Core;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Context-bound collection write service (RFC-0065 CTX-5). A committed cast lands in two steps:
    /// the raw client acquisition is stored verbatim on the local anchor under
    /// <see cref="EntityCollectionKeys.UiCastRaw"/> (never routed — it is a client capture product),
    /// then the anchor's active interaction context decides the filter profile (0 = explicit
    /// pass-through) and the active collection key, and the filtered set is domain-routed through
    /// <see cref="DomainRoutedCollectionWriter"/>. In the steady state (no context mounted on the
    /// anchor) the data-declared default profile's filter and collection key apply. Steady-state
    /// allocation free.
    /// </summary>
    public sealed class ContextBoundCollectionWriter
    {
        private readonly World _world;
        private readonly InteractionContextProfileRegistry _contextProfiles;
        private readonly FilterProfileRegistry _filters;
        private readonly DomainRoutedCollectionWriter _routedWriter;
        private readonly EntityCollectionStore _store;
        private readonly int _steadyStateCollectionKeyId;
        private readonly int _steadyStateFilterProfileId;

        private Entity[] _filteredScratch = new Entity[256];

        public ContextBoundCollectionWriter(
            World world,
            InteractionContextProfileRegistry contextProfiles,
            FilterProfileRegistry filters,
            DomainRoutedCollectionWriter routedWriter,
            EntityCollectionStore store)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _contextProfiles = contextProfiles ?? throw new ArgumentNullException(nameof(contextProfiles));
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));
            _routedWriter = routedWriter ?? throw new ArgumentNullException(nameof(routedWriter));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (!_contextProfiles.TryGetSteadyStateRouting(out _steadyStateCollectionKeyId, out _steadyStateFilterProfileId))
            {
                throw new InvalidOperationException(
                    $"Context-bound collection writing requires the steady-state interaction context profile '{InteractionContextIds.Default}' to be installed.");
            }
        }

        /// <summary>
        /// Commit one cast batch for the local anchor: store raw hits, evaluate the anchor's active
        /// context's filter profile (or the default profile's in the steady state), and domain-route
        /// the survivors into the active collection key. Routed writes use
        /// <see cref="DomainRoutingUnresolvedPolicy.Reject"/>: an entity without a control domain
        /// reaching the routed command source is a pipeline error. Contexts with
        /// <c>FilterProfileId == 0</c> pass the raw hits through unfiltered, so their configurers
        /// must guarantee the cast result is routable (RFC-0065 DEC-4: a domainless entity has no
        /// place in a domain-routed command source).
        /// </summary>
        public void CommitCast(Entity localAnchorRep, ReadOnlySpan<Entity> rawHits, EntityCollectionSourceKind sourceKind)
        {
            if (localAnchorRep == Entity.Null)
            {
                throw new ArgumentException("Local anchor rep is required for cast commits.", nameof(localAnchorRep));
            }

            var rawDescriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.UiCastRaw,
                sourceKind,
                EntityCollectionRoleKind.AcquisitionPreview);
            _store.Replace(localAnchorRep, rawDescriptor, rawHits, localAnchorRep);

            int filterProfileId = _steadyStateFilterProfileId;
            int collectionKeyId = _steadyStateCollectionKeyId;
            if (_world.TryGet<InteractionContextInstance>(localAnchorRep, out InteractionContextInstance context))
            {
                filterProfileId = context.FilterProfileId;
                collectionKeyId = context.ActiveCollectionKeyId;
            }

            ReadOnlySpan<Entity> routed = rawHits;
            if (filterProfileId != 0)
            {
                EnsureScratch(rawHits.Length);
                int filteredCount = _filters.Evaluate(filterProfileId, localAnchorRep, rawHits, _filteredScratch);
                routed = _filteredScratch.AsSpan(0, filteredCount);
            }

            _routedWriter.ReplaceRouted(
                localAnchorRep,
                collectionKeyId,
                routed,
                sourceKind,
                DomainRoutingUnresolvedPolicy.Reject);
        }

        private void EnsureScratch(int required)
        {
            if (required <= _filteredScratch.Length)
            {
                return;
            }

            int next = _filteredScratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            _filteredScratch = new Entity[next];
        }
    }
}
