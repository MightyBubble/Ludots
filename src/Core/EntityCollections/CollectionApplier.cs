using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Interaction;

namespace Ludots.Core.EntityCollections
{
    /// <summary>
    /// Caller-selected semantics for routed batch entries whose control domain cannot be resolved
    /// (RFC-0065 DEC-4). There is no default value on purpose: the writing side must state its
    /// choice explicitly — the routing layer never guesses a destination domain.
    /// </summary>
    public enum DomainRoutingUnresolvedPolicy
    {
        /// <summary>An unresolved entity is a pipeline error; the routed write throws.</summary>
        Reject = 1,

        /// <summary>Unresolved entities are explicitly declared to land in the writer's own domain.</summary>
        WriterDomain = 2,
    }

    /// <summary>
    /// The single collection write point (constitution §08: 集合只有一个写者). Every mutation of
    /// EntityCollectionStore contents goes through this class — the mount-host apply path
    /// (<see cref="Apply"/>), the cast commit path (<see cref="CommitCast"/>), and the RFC-0065
    /// DEC-4 domain-routed path (<see cref="ReplaceRouted"/>). Callers decide owner, key, op, and
    /// entities from their declarations; this class executes the set math, the cast commit
    /// handshake, and the domain split. Key semantics are data-declared
    /// (Input/collection_keys.json + graph/mount declarations); the engine holds no builtin key table.
    /// </summary>
    public sealed class CollectionApplier
    {
        private readonly World _world;
        private readonly EntityCollectionStore _store;
        private ControlDomainQuery? _domains;
        private FilterProfileRegistry? _filters;
        private int _castRawCollectionKeyId;

        private readonly EntityKeyedSoaTable<RouteRecord> _routes;
        private readonly Dictionary<Entity, int> _domainIndexMap = new(capacity: 8);

        private Entity[] _filteredScratch = new Entity[256];
        private int[] _rowDomainIndices = new int[64];
        private Entity[] _memberScratch = new Entity[64];
        private Entity[] _currentDomains = new Entity[8];
        private int[] _domainRowCounts = new int[8];
        private int[] _domainCursors = new int[8];
        private Entity[] _previousDomainPool = new Entity[64];
        private int _previousDomainCursor;

        public CollectionApplier(World world, EntityCollectionStore store)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _routes = new EntityKeyedSoaTable<RouteRecord>(initialCapacity: 16);
        }

        /// <summary>
        /// Binds the cast-commit / domain-routing dependencies (engine assembly time, after the
        /// input interaction registries exist). Cast commits and routed writes fail closed until
        /// this is called; <see cref="Apply"/> and <see cref="ApplyDescriptor"/> work from construction.
        /// </summary>
        public void BindInputInteraction(FilterProfileRegistry filters, ControlDomainQuery domains, int castRawCollectionKeyId)
        {
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));
            _domains = domains ?? throw new ArgumentNullException(nameof(domains));
            if (castRawCollectionKeyId <= 0 || string.IsNullOrEmpty(_store.KeyRegistry.GetName(castRawCollectionKeyId)))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(castRawCollectionKeyId),
                    "The cast-raw collection key must be a registered, data-declared key (Input/collection_keys.json castRaw).");
            }

            _castRawCollectionKeyId = castRawCollectionKeyId;
        }

        /// <summary>
        /// Apply replace/add/subtract semantics to one owned collection. The mount host and the
        /// graph bridge call this with the key resolved from their declarations; the key must
        /// already be registered in the store key space (unknown keys fail closed).
        /// </summary>
        public void Apply(Entity owner, int collectionKeyId, CollectionWriteOp op, ReadOnlySpan<Entity> entities)
        {
            CollectionWrite.Apply(_store, owner, collectionKeyId, op, entities);
        }

        /// <summary>
        /// Descriptor-level write entry for engine runtimes that author rich descriptors
        /// (roles, row flags, primary entities). Owner and key are fail-closed; the descriptor
        /// itself is the caller's declaration. Routing through this method keeps the store's
        /// mutation surface owned by the applier alone.
        /// </summary>
        public void ApplyDescriptor(
            Entity owner,
            int collectionKeyId,
            in EntityCollectionDescriptor descriptor,
            ReadOnlySpan<Entity> entities)
        {
            ApplyDescriptor(owner, collectionKeyId, in descriptor, entities, default, default, Entity.Null);
        }

        /// <summary>Descriptor-level write recording the maintaining writer domain (RFC-0065 PROV-1b).</summary>
        public void ApplyDescriptor(
            Entity owner,
            int collectionKeyId,
            in EntityCollectionDescriptor descriptor,
            ReadOnlySpan<Entity> entities,
            Entity writerDomain)
        {
            ApplyDescriptor(owner, collectionKeyId, in descriptor, entities, default, default, writerDomain);
        }

        /// <summary>Descriptor-level write with per-row role/flag data and no writer domain.</summary>
        public void ApplyDescriptor(
            Entity owner,
            int collectionKeyId,
            in EntityCollectionDescriptor descriptor,
            ReadOnlySpan<Entity> entities,
            ReadOnlySpan<int> rowRoleIds,
            ReadOnlySpan<EntityCollectionRowFlags> rowFlags)
        {
            ApplyDescriptor(owner, collectionKeyId, in descriptor, entities, rowRoleIds, rowFlags, Entity.Null);
        }

        /// <summary>Full descriptor-level write with per-row role/flag data and the maintaining writer domain.</summary>
        public void ApplyDescriptor(
            Entity owner,
            int collectionKeyId,
            in EntityCollectionDescriptor descriptor,
            ReadOnlySpan<Entity> entities,
            ReadOnlySpan<int> rowRoleIds,
            ReadOnlySpan<EntityCollectionRowFlags> rowFlags,
            Entity writerDomain)
        {
            if (owner == Entity.Null || owner == default)
            {
                throw new InvalidOperationException(
                    "COLLECTION.APPLY.OwnerMissing: collection writes require a live owner entity (the holding rep).");
            }

            if (string.IsNullOrEmpty(_store.KeyRegistry.GetName(collectionKeyId)))
            {
                throw new InvalidOperationException(
                    $"COLLECTION.APPLY.KeyUnknown: collection key id {collectionKeyId} is not registered in the EntityCollectionStore key space.");
            }

            _store.Replace(owner, collectionKeyId, in descriptor, entities, rowRoleIds, rowFlags, writerDomain);
        }

        /// <summary>
        /// Removes one owned collection (aim teardown, context clear). Teardown is a normal path:
        /// a dead or null owner simply has nothing left to remove and returns false; writes stay fail-closed.
        /// </summary>
        public bool Remove(Entity owner, int collectionKeyId)
        {
            if (owner == Entity.Null || owner == default)
            {
                return false;
            }

            return _store.Remove(owner, collectionKeyId);
        }

        private void RequireRoutedBound()
        {
            if (_domains == null || _filters == null)
            {
                throw new InvalidOperationException(
                    "COLLECTION.APPLIER.RoutingUnbound: cast commits and domain-routed writes require BindInputInteraction (engine assembly time).");
            }
        }

        /// <summary>
        /// Commit one cast batch for the local anchor (constitution §12 cast commit): store the raw
        /// hits verbatim under the data-declared cast-raw key, evaluate the anchor's active
        /// context's filter profile, and domain-route the survivors into the active collection key.
        /// Routed writes use
        /// <see cref="DomainRoutingUnresolvedPolicy.Reject"/>: an entity without a control domain
        /// reaching the routed command source is a pipeline error. Contexts with
        /// <c>FilterProfileId == 0</c> pass the raw hits through unfiltered, so their configurers
        /// must guarantee the cast result is routable (RFC-0065 DEC-4).
        /// </summary>
        public void CommitCast(Entity localAnchorRep, int collectionKeyId, ReadOnlySpan<Entity> rawHits, EntityCollectionSourceKind sourceKind)
        {
            RequireRoutedBound();
            if (localAnchorRep == Entity.Null)
            {
                throw new ArgumentException("Local anchor rep is required for cast commits.", nameof(localAnchorRep));
            }

            string castRawKeyName = _store.KeyRegistry.GetName(_castRawCollectionKeyId)
                ?? throw new InvalidOperationException("COLLECTION.COMMIT_CAST.RawKeyUnregistered.");
            var rawDescriptor = EntityCollectionDescriptor.Create(
                castRawKeyName,
                sourceKind,
                EntityCollectionRoleKind.AcquisitionPreview);
            _store.Replace(localAnchorRep, _castRawCollectionKeyId, in rawDescriptor, rawHits, localAnchorRep);

            int filterProfileId = _world.TryGet<InteractionContextInstance>(localAnchorRep, out InteractionContextInstance context)
                ? context.FilterProfileId
                : 0;

            ReadOnlySpan<Entity> routed = rawHits;
            if (filterProfileId != 0)
            {
                EnsureFilteredScratch(rawHits.Length);
                int filteredCount = _filters.Evaluate(filterProfileId, localAnchorRep, rawHits, _filteredScratch);
                routed = _filteredScratch.AsSpan(0, filteredCount);
            }

            ReplaceRouted(
                localAnchorRep,
                collectionKeyId,
                routed,
                sourceKind,
                DomainRoutingUnresolvedPolicy.Reject);
        }

        /// <summary>Convenience overload resolving the collection key string through the store registry.</summary>
        public void ReplaceRouted(
            Entity writerDomain,
            string collectionKey,
            ReadOnlySpan<Entity> entities,
            EntityCollectionSourceKind sourceKind,
            DomainRoutingUnresolvedPolicy unresolvedPolicy)
        {
            if (string.IsNullOrWhiteSpace(collectionKey))
            {
                throw new ArgumentException("Collection key is required.", nameof(collectionKey));
            }

            ReplaceRouted(writerDomain, _store.KeyRegistry.Register(collectionKey), entities, sourceKind, unresolvedPolicy);
        }

        /// <summary>
        /// Route one batch write (RFC-0065 DEC-4 / CTRL-4c): entities are grouped by
        /// <see cref="ControlDomainQuery.TryResolveControlDomain"/> and each group replaces
        /// <c>(domainRep, collectionKeyId)</c> with <paramref name="writerDomain"/> as the recorded
        /// maintainer. Entities without any control domain follow <paramref name="unresolvedPolicy"/>.
        /// Domains covered by the writer's previous batch for this key but not by this one are
        /// cleared so no rows linger. A given (domain, key) row is the domain's shared command
        /// state, not a per-controller view: controllers needing a private parallel selection write
        /// a different collection key. Grouping is a single counting-sort pass, O(rows + domains),
        /// allocation free at steady state.
        /// </summary>
        public void ReplaceRouted(
            Entity writerDomain,
            int collectionKeyId,
            ReadOnlySpan<Entity> entities,
            EntityCollectionSourceKind sourceKind,
            DomainRoutingUnresolvedPolicy unresolvedPolicy)
        {
            RequireRoutedBound();
            if (writerDomain == Entity.Null)
            {
                throw new ArgumentException("Writer domain is required for routed collection writes.", nameof(writerDomain));
            }

            if (unresolvedPolicy != DomainRoutingUnresolvedPolicy.Reject
                && unresolvedPolicy != DomainRoutingUnresolvedPolicy.WriterDomain)
            {
                throw new ArgumentOutOfRangeException(nameof(unresolvedPolicy), unresolvedPolicy, "Unresolved-entity policy must be an explicit, defined value.");
            }

            string key = _store.KeyRegistry.GetName(collectionKeyId);
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentOutOfRangeException(nameof(collectionKeyId), $"Collection key id {collectionKeyId} is not registered.");
            }

            var descriptor = EntityCollectionDescriptor.Create(
                key,
                sourceKind,
                EntityCollectionRoleKind.CommandSource);

            // Pass 1: resolve every row's domain once, assigning dense domain indices and per-domain counts.
            // The last-domain memo skips the hash lookup for the dominant case of batches clustered by domain.
            EnsureRowCapacity(entities.Length);
            _domainIndexMap.Clear();
            int currentDomainCount = 0;
            Entity lastDomain = Entity.Null;
            int lastDomainIndex = -1;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!_domains.TryResolveControlDomain(entities[i], out Entity domainRep))
                {
                    if (unresolvedPolicy == DomainRoutingUnresolvedPolicy.Reject)
                    {
                        throw new InvalidOperationException(
                            $"Entity {entities[i]} has no control domain; the routed write for collection key '{key}' rejects unresolved entities (policy {nameof(DomainRoutingUnresolvedPolicy.Reject)}).");
                    }

                    domainRep = writerDomain;
                }

                int domainIndex;
                if (domainRep == lastDomain)
                {
                    domainIndex = lastDomainIndex;
                }
                else
                {
                    if (!_domainIndexMap.TryGetValue(domainRep, out domainIndex))
                    {
                        domainIndex = currentDomainCount++;
                        EnsureDomainCapacity(currentDomainCount);
                        _currentDomains[domainIndex] = domainRep;
                        _domainRowCounts[domainIndex] = 0;
                        _domainIndexMap.Add(domainRep, domainIndex);
                    }

                    lastDomain = domainRep;
                    lastDomainIndex = domainIndex;
                }

                _rowDomainIndices[i] = domainIndex;
                _domainRowCounts[domainIndex]++;
            }

            // Pass 2: counting-sort layout — scatter rows into one scratch buffer partitioned by domain,
            // preserving batch order inside each partition.
            int cursor = 0;
            for (int d = 0; d < currentDomainCount; d++)
            {
                _domainCursors[d] = cursor;
                cursor += _domainRowCounts[d];
            }

            for (int i = 0; i < entities.Length; i++)
            {
                _memberScratch[_domainCursors[_rowDomainIndices[i]]++] = entities[i];
            }

            EntityKeyedSoaKey routeKey = EntityKeyedSoaKey.ForEntityAndDiscriminator(writerDomain, collectionKeyId);
            bool hadRecord = _routes.TryGet(routeKey, currentTick: 0, out RouteRecord record, out _, out _);

            if (hadRecord)
            {
                for (int i = 0; i < record.Count; i++)
                {
                    Entity previousDomain = _previousDomainPool[record.Start + i];
                    if (!_domainIndexMap.ContainsKey(previousDomain))
                    {
                        _store.Replace(previousDomain, collectionKeyId, in descriptor, ReadOnlySpan<Entity>.Empty, writerDomain);
                    }
                }
            }

            for (int d = 0; d < currentDomainCount; d++)
            {
                int memberCount = _domainRowCounts[d];
                int start = _domainCursors[d] - memberCount;
                _store.Replace(_currentDomains[d], collectionKeyId, in descriptor, _memberScratch.AsSpan(start, memberCount), writerDomain);
            }

            StoreRouteRecord(routeKey, hadRecord, in record, currentDomainCount);
        }





        private void EnsureFilteredScratch(int required)
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

        private void StoreRouteRecord(in EntityKeyedSoaKey routeKey, bool hadRecord, in RouteRecord record, int domainCount)
        {
            RouteRecord next = record;
            if (!hadRecord || next.Capacity < domainCount)
            {
                int capacity = Math.Max(4, hadRecord ? next.Capacity : 0);
                while (capacity < domainCount)
                {
                    capacity *= 2;
                }

                EnsurePreviousDomainCapacity(_previousDomainCursor + capacity);
                next.Start = _previousDomainCursor;
                next.Capacity = capacity;
                _previousDomainCursor += capacity;
            }

            for (int i = 0; i < domainCount; i++)
            {
                _previousDomainPool[next.Start + i] = _currentDomains[i];
            }

            next.Count = domainCount;
            _routes.Upsert(routeKey, next, expiryTick: 0, payloadChanged: true, out _);
        }

        private void EnsureRowCapacity(int required)
        {
            if (required > _rowDomainIndices.Length)
            {
                int next = _rowDomainIndices.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref _rowDomainIndices, next);
                Array.Resize(ref _memberScratch, next);
            }
        }

        private void EnsureDomainCapacity(int required)
        {
            if (required > _currentDomains.Length)
            {
                int next = _currentDomains.Length * 2;
                Array.Resize(ref _currentDomains, next);
                Array.Resize(ref _domainRowCounts, next);
                Array.Resize(ref _domainCursors, next);
            }
        }

        private void EnsurePreviousDomainCapacity(int required)
        {
            if (required > _previousDomainPool.Length)
            {
                int next = _previousDomainPool.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref _previousDomainPool, next);
            }
        }

        private struct RouteRecord
        {
            public int Start;
            public int Count;
            public int Capacity;
        }
    }
}
