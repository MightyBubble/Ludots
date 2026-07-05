using System;
using Arch.Core;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Context-bound collection write service (RFC-0065 CTX-5). A committed cast lands in two steps:
    /// the raw client acquisition is stored verbatim on the local anchor under
    /// <see cref="EntityCollectionKeys.UiCastRaw"/> (never routed — it is a client capture product),
    /// then the top interaction context frame decides the filter profile (0 = explicit pass-through)
    /// and the active collection key, and the filtered set is domain-routed through
    /// <see cref="DomainRoutedCollectionWriter"/>. Steady-state allocation free.
    /// </summary>
    public sealed class ContextBoundCollectionWriter
    {
        private readonly InteractionContextStack _contextStack;
        private readonly FilterProfileRegistry _filters;
        private readonly DomainRoutedCollectionWriter _routedWriter;
        private readonly EntityCollectionStore _store;

        private Entity[] _filteredScratch = new Entity[256];

        public ContextBoundCollectionWriter(
            InteractionContextStack contextStack,
            FilterProfileRegistry filters,
            DomainRoutedCollectionWriter routedWriter,
            EntityCollectionStore store)
        {
            _contextStack = contextStack ?? throw new ArgumentNullException(nameof(contextStack));
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));
            _routedWriter = routedWriter ?? throw new ArgumentNullException(nameof(routedWriter));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Commit one cast batch for the local anchor: store raw hits, evaluate the top frame's
        /// filter profile, and domain-route the survivors into the frame's active collection key.
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

            if (!_contextStack.TryPeek(out InteractionContextFrame frame))
            {
                throw new InvalidOperationException(
                    "Interaction context stack is empty; the engine default frame must exist before casts commit.");
            }

            ReadOnlySpan<Entity> routed = rawHits;
            if (frame.FilterProfileId != 0)
            {
                EnsureScratch(rawHits.Length);
                int filteredCount = _filters.Evaluate(frame.FilterProfileId, localAnchorRep, rawHits, _filteredScratch);
                routed = _filteredScratch.AsSpan(0, filteredCount);
            }

            _routedWriter.ReplaceRouted(localAnchorRep, frame.ActiveCollectionKeyId, routed, sourceKind);
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
