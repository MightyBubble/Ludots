using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.EntityCollections
{
    /// <summary>
    /// Caller-selected semantics for routed batch entries whose control domain cannot be resolved
    /// (RFC-0065 DEC-4). There is no default value on purpose: the writing profile must state its
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
    /// Domain-routed collection write path (RFC-0065 DEC-4 / CTRL-4c). A batch write is split by the control
    /// domain each entity belongs to and lands as one <see cref="EntityCollectionStore.Replace(Entity,in EntityCollectionDescriptor,ReadOnlySpan{Entity},Entity)"/>
    /// per domain rep, tagged with the maintaining writer domain. Entities without a resolvable domain are
    /// handled per the caller's <see cref="DomainRoutingUnresolvedPolicy"/>. Collections never migrate across
    /// domains; domains written by the previous routed batch but absent from the current one are cleared for the key.
    /// Grouping is a single pass over the batch (counting-sort layout), O(rows + domains), allocation free at steady state.
    /// </summary>
    public sealed class DomainRoutedCollectionWriter
    {
        private readonly EntityCollectionStore _store;
        private readonly ControlDomainQuery _domains;
        private readonly EntityKeyedSoaTable<RouteRecord> _routes;
        private readonly Dictionary<Entity, int> _domainIndexMap = new(capacity: 8);

        private int[] _rowDomainIndices = new int[64];
        private Entity[] _memberScratch = new Entity[64];
        private Entity[] _currentDomains = new Entity[8];
        private int[] _domainRowCounts = new int[8];
        private int[] _domainCursors = new int[8];
        private Entity[] _previousDomainPool = new Entity[64];
        private int _previousDomainCursor;

        public DomainRoutedCollectionWriter(EntityCollectionStore store, ControlDomainQuery domains)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _domains = domains ?? throw new ArgumentNullException(nameof(domains));
            _routes = new EntityKeyedSoaTable<RouteRecord>(initialCapacity: 16);
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
        /// Route one batch write: entities are grouped by <see cref="ControlDomainQuery.TryResolveControlDomain"/>
        /// and each group replaces <c>(domainRep, collectionKeyId)</c> with <paramref name="writerDomain"/> as the
        /// recorded maintainer. Entities without any control domain follow <paramref name="unresolvedPolicy"/>:
        /// <see cref="DomainRoutingUnresolvedPolicy.Reject"/> throws (a domain-routed command source must only
        /// receive routable entities), <see cref="DomainRoutingUnresolvedPolicy.WriterDomain"/> explicitly lands
        /// them in the writer's own domain. Domains covered by the writer's previous batch for this key but not
        /// by this one are cleared so no rows linger.
        /// </summary>
        public void ReplaceRouted(
            Entity writerDomain,
            int collectionKeyId,
            ReadOnlySpan<Entity> entities,
            EntityCollectionSourceKind sourceKind,
            DomainRoutingUnresolvedPolicy unresolvedPolicy)
        {
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
                        _store.Replace(previousDomain, descriptor, ReadOnlySpan<Entity>.Empty, writerDomain);
                    }
                }
            }

            for (int d = 0; d < currentDomainCount; d++)
            {
                int memberCount = _domainRowCounts[d];
                int start = _domainCursors[d] - memberCount;
                _store.Replace(_currentDomains[d], descriptor, _memberScratch.AsSpan(start, memberCount), writerDomain);
            }

            StoreRouteRecord(routeKey, hadRecord, in record, currentDomainCount);
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
