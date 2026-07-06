using System;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.EntityCollections
{
    /// <summary>
    /// Composite read-only control-plane view (RFC-0065 §5.10 / CTRL-4d). For an anchor rep it concatenates the
    /// <c>(domainRep, collectionKeyId)</c> collections of every control-reachable domain (anchor first). Fully
    /// controlled domains contribute all rows; partially controlled domains (reached only through a Controls grant
    /// to a plain unit) contribute only the anchor's directly granted units. Nothing is materialized: a Controls
    /// edge disappearing shrinks the view on the next read while each domain keeps its rows.
    /// <para>
    /// Complexity contract: a fully controlled domain is a straight block copy. A partially controlled domain is
    /// a single O(rows) pass over its collection with an O(1) pooled hash probe per row against the materialized
    /// direct-grant unit set (grant out-degree is small). True O(granted_rows) would require an entity→row index
    /// inside <see cref="EntityCollectionStore"/> and is deliberately out of scope here.
    /// </para>
    /// </summary>
    public sealed class ControlPlaneView
    {
        private readonly EntityCollectionStore _store;
        private readonly ControlDomainQuery _domains;
        private Entity[] _domainScratch = new Entity[8];
        private bool[] _fullyControlledScratch = new bool[8];
        private Entity[] _unitGrantScratch = new Entity[8];
        private Entity[] _probeEntities = new Entity[16];
        private bool[] _probeUsed = new bool[16];

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

                int copied = _fullyControlledScratch[d]
                    ? _store.CopyEntities(handle, 0, entities[written..])
                    : CopyControllableRows(anchorRep, domainRep, handle, entities[written..]);
                if (!domains.IsEmpty)
                {
                    domains.Slice(written, copied).Fill(domainRep);
                }

                written += copied;
            }

            return written;
        }

        /// <summary>
        /// Row-level projection for a partially controlled domain: materializes the anchor's direct-grant unit
        /// set for the domain into a pooled hash set, then filters the collection in one pass with O(1) probes
        /// instead of an owns-chain query per row.
        /// </summary>
        private int CopyControllableRows(Entity anchorRep, Entity domainRep, EntityCollectionHandle handle, Span<Entity> destination)
        {
            if (!_store.TryGetView(handle, out EntityCollectionView view))
            {
                return 0;
            }

            int grantCount = CollectDirectUnitGrants(anchorRep, domainRep);
            if (grantCount == 0)
            {
                return 0;
            }

            PrepareProbeSet(grantCount);
            for (int i = 0; i < grantCount; i++)
            {
                AddToProbeSet(_unitGrantScratch[i]);
            }

            int written = 0;
            for (int i = 0; i < view.Count && written < destination.Length; i++)
            {
                if (_store.TryGetEntityAt(handle, i, out Entity row) && ProbeSetContains(row))
                {
                    destination[written++] = row;
                }
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
                int count = _domains.CollectControlledDomains(anchorRep, _domainScratch, _fullyControlledScratch);
                if (count < _domainScratch.Length)
                {
                    return count;
                }

                Array.Resize(ref _domainScratch, _domainScratch.Length * 2);
                Array.Resize(ref _fullyControlledScratch, _fullyControlledScratch.Length * 2);
            }
        }

        private int CollectDirectUnitGrants(Entity anchorRep, Entity domainRep)
        {
            while (true)
            {
                int count = _domains.CollectDirectUnitGrants(anchorRep, domainRep, _unitGrantScratch);
                if (count < _unitGrantScratch.Length)
                {
                    return count;
                }

                Array.Resize(ref _unitGrantScratch, _unitGrantScratch.Length * 2);
            }
        }

        /// <summary>Resets the pooled open-addressed probe set for at most <paramref name="expectedCount"/> insertions.</summary>
        private void PrepareProbeSet(int expectedCount)
        {
            int required = NextPowerOfTwo(Math.Max(4, expectedCount * 2));
            if (_probeEntities.Length < required)
            {
                _probeEntities = new Entity[required];
                _probeUsed = new bool[required];
            }
            else
            {
                Array.Clear(_probeUsed, 0, _probeUsed.Length);
            }
        }

        private void AddToProbeSet(Entity candidate)
        {
            int mask = _probeEntities.Length - 1;
            int slot = ProbeSlot(candidate, mask);
            while (_probeUsed[slot])
            {
                if (_probeEntities[slot] == candidate)
                {
                    return;
                }

                slot = (slot + 1) & mask;
            }

            _probeEntities[slot] = candidate;
            _probeUsed[slot] = true;
        }

        private bool ProbeSetContains(Entity candidate)
        {
            int mask = _probeEntities.Length - 1;
            int slot = ProbeSlot(candidate, mask);
            while (_probeUsed[slot])
            {
                if (_probeEntities[slot] == candidate)
                {
                    return true;
                }

                slot = (slot + 1) & mask;
            }

            return false;
        }

        private static int ProbeSlot(Entity candidate, int mask)
        {
            return (int)((((uint)candidate.Id * 2654435761u) ^ (uint)candidate.WorldId) & (uint)mask);
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
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
