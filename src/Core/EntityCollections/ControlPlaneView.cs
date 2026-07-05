using System;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.EntityCollections
{
    /// <summary>
    /// Composite read-only control-plane view (RFC-0065 §5.10 / CTRL-4d). For an anchor rep it concatenates the
    /// <c>(domainRep, collectionKeyId)</c> collections of every control-reachable domain (anchor first). Nothing is
    /// materialized: a Controls edge disappearing shrinks the view on the next read while each domain keeps its rows.
    /// </summary>
    public sealed class ControlPlaneView
    {
        private readonly EntityCollectionStore _store;
        private readonly ControlDomainQuery _domains;
        private Entity[] _domainScratch = new Entity[8];

        public ControlPlaneView(EntityCollectionStore store, ControlDomainQuery domains)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _domains = domains ?? throw new ArgumentNullException(nameof(domains));
        }

        /// <summary>Concatenate the members of every reachable domain's collection; returns rows written.</summary>
        public int CopyMembers(Entity anchorRep, int collectionKeyId, Span<Entity> destination)
        {
            return CopyMembersWithDomain(anchorRep, collectionKeyId, destination, Span<Entity>.Empty);
        }

        /// <summary>
        /// Concatenate members and report, per row, the domain rep the row lives in (provenance for markers is
        /// the collection address itself, DEC-5). <paramref name="domains"/> may be empty to skip that column.
        /// </summary>
        public int CopyMembersWithDomain(Entity anchorRep, int collectionKeyId, Span<Entity> entities, Span<Entity> domains)
        {
            if (entities.IsEmpty)
            {
                return 0;
            }

            if (!domains.IsEmpty && domains.Length < entities.Length)
            {
                throw new ArgumentException("Domain span must be empty or at least as long as the entity destination.", nameof(domains));
            }

            int domainCount = CollectDomains(anchorRep);
            int written = 0;
            for (int d = 0; d < domainCount && written < entities.Length; d++)
            {
                Entity domainRep = _domainScratch[d];
                if (!_store.TryGet(domainRep, collectionKeyId, out EntityCollectionHandle handle))
                {
                    continue;
                }

                int copied = _store.CopyEntities(handle, 0, entities[written..]);
                if (!domains.IsEmpty)
                {
                    domains.Slice(written, copied).Fill(domainRep);
                }

                written += copied;
            }

            return written;
        }

        /// <summary>
        /// Aggregate invalidation signal for the composite view: changes when any reachable domain's collection
        /// content changes or when the control topology itself changes.
        /// </summary>
        public uint ComputeRevision(Entity anchorRep, int collectionKeyId)
        {
            uint hash = 2166136261u;
            hash = HashCombine(hash, _domains.Revision);
            int domainCount = CollectDomains(anchorRep);
            for (int d = 0; d < domainCount; d++)
            {
                Entity domainRep = _domainScratch[d];
                hash = HashCombine(hash, (uint)domainRep.Id);
                hash = HashCombine(hash, (uint)domainRep.WorldId);
                hash = HashCombine(hash, (uint)domainRep.Version);
                hash = HashCombine(hash, _store.TryGet(domainRep, collectionKeyId, out EntityCollectionHandle handle)
                    ? handle.Revision
                    : 0u);
            }

            return hash;
        }

        private int CollectDomains(Entity anchorRep)
        {
            while (true)
            {
                int count = _domains.CollectControlledDomains(anchorRep, _domainScratch);
                if (count < _domainScratch.Length)
                {
                    return count;
                }

                Array.Resize(ref _domainScratch, _domainScratch.Length * 2);
            }
        }

        private static uint HashCombine(uint hash, uint value)
        {
            unchecked
            {
                return (hash ^ value) * 16777619u;
            }
        }
    }
}
