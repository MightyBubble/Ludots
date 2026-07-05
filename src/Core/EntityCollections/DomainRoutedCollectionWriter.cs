using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.EntityCollections
{
    /// <summary>
    /// Domain-routed collection write path (RFC-0065 DEC-4 / CTRL-4c). A batch write is split by the control
    /// domain each entity belongs to and lands as one <see cref="EntityCollectionStore.Replace(Entity,in EntityCollectionDescriptor,ReadOnlySpan{Entity},Entity)"/>
    /// per domain rep, tagged with the maintaining writer domain. Collections never migrate across domains;
    /// domains written by the previous routed batch but absent from the current one are cleared for the key.
    /// </summary>
    public sealed class DomainRoutedCollectionWriter
    {
        private readonly EntityCollectionStore _store;
        private readonly ControlDomainQuery _domains;
        private readonly EntityKeyedSoaTable<RouteRecord> _routes;

        private Entity[] _resolvedDomains = new Entity[64];
        private Entity[] _currentDomains = new Entity[8];
        private Entity[] _memberScratch = new Entity[64];
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
            EntityCollectionSourceKind sourceKind)
        {
            if (string.IsNullOrWhiteSpace(collectionKey))
            {
                throw new ArgumentException("Collection key is required.", nameof(collectionKey));
            }

            ReplaceRouted(writerDomain, _store.KeyRegistry.Register(collectionKey), entities, sourceKind);
        }

        /// <summary>
        /// Route one batch write: entities are grouped by <see cref="ControlDomainQuery.TryResolveControlDomain"/>
        /// and each group replaces <c>(domainRep, collectionKeyId)</c> with <paramref name="writerDomain"/> as the
        /// recorded maintainer. Entities without any control domain belong to the writer's own domain (that is a
        /// topology fact, not a fallback). Domains covered by the writer's previous batch for this key but not by
        /// this one are cleared so no rows linger.
        /// </summary>
        public void ReplaceRouted(
            Entity writerDomain,
            int collectionKeyId,
            ReadOnlySpan<Entity> entities,
            EntityCollectionSourceKind sourceKind)
        {
            if (writerDomain == Entity.Null)
            {
                throw new ArgumentException("Writer domain is required for routed collection writes.", nameof(writerDomain));
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

            EnsureResolvedCapacity(entities.Length);
            int currentDomainCount = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!_domains.TryResolveControlDomain(entities[i], out Entity domainRep))
                {
                    domainRep = writerDomain;
                }

                _resolvedDomains[i] = domainRep;
                currentDomainCount = AppendUniqueDomain(domainRep, currentDomainCount);
            }

            EntityKeyedSoaKey routeKey = EntityKeyedSoaKey.ForEntityAndDiscriminator(writerDomain, collectionKeyId);
            bool hadRecord = _routes.TryGet(routeKey, currentTick: 0, out RouteRecord record, out _, out _);

            if (hadRecord)
            {
                for (int i = 0; i < record.Count; i++)
                {
                    Entity previousDomain = _previousDomainPool[record.Start + i];
                    if (!ContainsDomain(previousDomain, currentDomainCount))
                    {
                        _store.Replace(previousDomain, descriptor, ReadOnlySpan<Entity>.Empty, writerDomain);
                    }
                }
            }

            for (int d = 0; d < currentDomainCount; d++)
            {
                Entity domainRep = _currentDomains[d];
                EnsureMemberCapacity(entities.Length);
                int memberCount = 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    if (_resolvedDomains[i] == domainRep)
                    {
                        _memberScratch[memberCount++] = entities[i];
                    }
                }

                _store.Replace(domainRep, descriptor, _memberScratch.AsSpan(0, memberCount), writerDomain);
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

        private int AppendUniqueDomain(Entity domainRep, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (_currentDomains[i] == domainRep)
                {
                    return count;
                }
            }

            if (count == _currentDomains.Length)
            {
                Array.Resize(ref _currentDomains, _currentDomains.Length * 2);
            }

            _currentDomains[count] = domainRep;
            return count + 1;
        }

        private bool ContainsDomain(Entity domainRep, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (_currentDomains[i] == domainRep)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureResolvedCapacity(int required)
        {
            if (required > _resolvedDomains.Length)
            {
                int next = _resolvedDomains.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref _resolvedDomains, next);
            }
        }

        private void EnsureMemberCapacity(int required)
        {
            if (required > _memberScratch.Length)
            {
                int next = _memberScratch.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref _memberScratch, next);
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
