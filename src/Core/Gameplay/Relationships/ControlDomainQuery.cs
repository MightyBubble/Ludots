using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>
    /// Control-plane topology query (RFC-0065 DEC-1): <c>controls(rep) ≡ owns(rep) ∪ explicit grant edges</c>.
    /// The union is computed at query time; owned entities are never mirrored into Controls edges.
    /// </summary>
    public sealed class ControlDomainQuery
    {
        private const int MaxOwnershipDepth = 1024;
        private const int DomainCacheDiscriminator = 1;

        private readonly World _world;
        private readonly RelationshipRuntime _relationships;
        private readonly OwnershipResolver _ownership;
        private readonly int _controlsTypeId;
        private readonly List<Entity> _ownedScratch = new(32);
        private readonly EntityKeyedSoaTable<DomainCacheEntry> _domainCache = new(initialCapacity: 256);
        private uint _domainCacheRevision;
        private Entity[] _grantScratch = new Entity[8];

        public ControlDomainQuery(
            World world,
            RelationshipRuntime relationships,
            OwnershipResolver ownership,
            int ownsTypeId,
            int controlsTypeId)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
            _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            if (ownsTypeId != ownership.OwnsTypeId)
            {
                throw new ArgumentException(
                    $"ControlDomainQuery ownsTypeId {ownsTypeId} must match the OwnershipResolver owns type {ownership.OwnsTypeId}.",
                    nameof(ownsTypeId));
            }

            if (controlsTypeId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(controlsTypeId));
            }

            _controlsTypeId = controlsTypeId;
        }

        /// <summary>Relationship topology change signal; any edge mutation invalidates previously collected results.</summary>
        public uint Revision => _relationships.ReverseIndex.Revision;

        /// <summary>
        /// Collects every entity the controller rep can command: its owns subtree plus each Controls grant target,
        /// expanded to the target's owns subtree when the target is itself a domain rep (carries <see cref="PlayerIdentity"/>).
        /// Results are de-duplicated and truncated to the buffer length.
        /// </summary>
        public int CollectControlled(Entity controllerRep, Span<Entity> buffer)
        {
            if (controllerRep == Entity.Null || buffer.IsEmpty || !_world.IsAlive(controllerRep))
            {
                return 0;
            }

            int written = AppendOwnedSubtree(controllerRep, buffer, written: 0);
            int grantCount = CollectGrants(controllerRep);
            for (int i = 0; i < grantCount && written < buffer.Length; i++)
            {
                Entity target = _grantScratch[i];
                if (!_world.IsAlive(target))
                {
                    continue;
                }

                if (_world.Has<PlayerIdentity>(target))
                {
                    written = AppendOwnedSubtree(target, buffer, written);
                }
                else
                {
                    written = AppendUnique(buffer, written, target);
                }
            }

            return written;
        }

        /// <summary>
        /// Collects only the <b>fully controlled</b> domain reps: the controller rep itself plus every Controls
        /// grant target that is a domain rep (carries <see cref="PlayerIdentity"/>). Domains reached through a
        /// grant to a plain unit are partially controlled and are not reported here; use
        /// <see cref="CollectControlledDomains(Entity, Span{Entity}, Span{bool})"/> when they matter.
        /// Results are de-duplicated and truncated to the buffer length.
        /// </summary>
        public int CollectControlledDomains(Entity controllerRep, Span<Entity> buffer)
        {
            if (controllerRep == Entity.Null || buffer.IsEmpty || !_world.IsAlive(controllerRep))
            {
                return 0;
            }

            int written = AppendUnique(buffer, written: 0, controllerRep);
            int grantCount = CollectGrants(controllerRep);
            for (int i = 0; i < grantCount && written < buffer.Length; i++)
            {
                Entity target = _grantScratch[i];
                if (_world.IsAlive(target) && _world.Has<PlayerIdentity>(target))
                {
                    written = AppendUnique(buffer, written, target);
                }
            }

            return written;
        }

        /// <summary>
        /// Collects every domain rep the controller can reach and marks, per entry, whether the domain is
        /// <b>fully controlled</b> (the anchor's own domain or a Controls grant to the domain rep itself) or
        /// <b>partially controlled</b> (reached only through a Controls grant to a plain unit; the unit's domain
        /// is resolved via <see cref="TryResolveControlDomain"/>). A grant to a unit without any control domain
        /// produces no entry — there is no domain to project it into. A domain that is both fully granted and
        /// reached through a unit grant reports as fully controlled. Results are de-duplicated and truncated
        /// to the domain buffer length.
        /// </summary>
        public int CollectControlledDomains(Entity controllerRep, Span<Entity> domains, Span<bool> fullyControlled)
        {
            if (fullyControlled.Length < domains.Length)
            {
                throw new ArgumentException(
                    "Fully-controlled span must be at least as long as the domain destination.",
                    nameof(fullyControlled));
            }

            if (controllerRep == Entity.Null || domains.IsEmpty || !_world.IsAlive(controllerRep))
            {
                return 0;
            }

            int written = AppendUniqueDomain(domains, fullyControlled, written: 0, controllerRep, full: true);
            int grantCount = CollectGrants(controllerRep);

            // Fully controlled domains first so a later unit grant into the same domain never downgrades it.
            for (int i = 0; i < grantCount && written < domains.Length; i++)
            {
                Entity target = _grantScratch[i];
                if (_world.IsAlive(target) && _world.Has<PlayerIdentity>(target))
                {
                    written = AppendUniqueDomain(domains, fullyControlled, written, target, full: true);
                }
            }

            for (int i = 0; i < grantCount && written < domains.Length; i++)
            {
                Entity target = _grantScratch[i];
                if (_world.IsAlive(target) &&
                    !_world.Has<PlayerIdentity>(target) &&
                    TryResolveControlDomain(target, out Entity unitDomain))
                {
                    written = AppendUniqueDomain(domains, fullyControlled, written, unitDomain, full: false);
                }
            }

            return written;
        }

        /// <summary>
        /// Returns true when the target would appear in <see cref="CollectControlled"/> for the controller rep:
        /// it sits in the controller's owns subtree, is a directly granted non-rep entity, or sits in the owns
        /// subtree of a granted domain rep. Edge membership is resolved through the reverse index so per-row
        /// filtering (partial-domain projection) stays allocation-free.
        /// </summary>
        public bool IsControllableBy(Entity controllerRep, Entity target)
        {
            if (controllerRep == Entity.Null || target == Entity.Null || controllerRep == target)
            {
                return false;
            }

            if (!_world.IsAlive(controllerRep) || !_world.IsAlive(target))
            {
                return false;
            }

            if (!_world.Has<PlayerIdentity>(target) && HasControlsGrantFrom(controllerRep, target))
            {
                return true;
            }

            Entity current = target;
            int guard = 0;
            while (TryGetDirectOwnerViaIndex(current, out Entity owner))
            {
                if (owner == controllerRep)
                {
                    return true;
                }

                if (_world.Has<PlayerIdentity>(owner) && HasControlsGrantFrom(controllerRep, owner))
                {
                    return true;
                }

                current = owner;
                guard++;
                if (guard > MaxOwnershipDepth)
                {
                    throw new InvalidOperationException("Ownership graph exceeded the maximum supported traversal depth.");
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves the control domain rep for a target by walking the owns chain upward to the nearest entity
        /// carrying <see cref="PlayerIdentity"/> (a rep resolves to itself). Returns false when no domain exists.
        /// Resolutions are cached per target and invalidated by <see cref="Revision"/> (DEC-2: routing must not
        /// re-walk the graph per write on an unchanged topology).
        /// </summary>
        public bool TryResolveControlDomain(Entity target, out Entity domainRep)
        {
            domainRep = Entity.Null;
            if (target == Entity.Null || !_world.IsAlive(target))
            {
                return false;
            }

            if (_world.Has<PlayerIdentity>(target))
            {
                domainRep = target;
                return true;
            }

            uint revision = Revision;
            if (revision != _domainCacheRevision)
            {
                // Any edge mutation invalidates every cached resolution; sweeping wholesale also
                // reclaims slots held by destroyed entities.
                _domainCache.Expire(currentTick: 1);
                _domainCache.Compact();
                _domainCacheRevision = revision;
            }

            EntityKeyedSoaKey cacheKey = EntityKeyedSoaKey.ForEntityAndDiscriminator(target, DomainCacheDiscriminator);
            if (_domainCache.TryGet(cacheKey, currentTick: 0, out DomainCacheEntry cached, out _, out _))
            {
                domainRep = cached.DomainRep;
                return cached.HasDomain;
            }

            bool hasDomain = false;
            Entity current = target;
            int guard = 0;
            while (_ownership.TryGetDirectOwner(current, out Entity owner))
            {
                if (_world.Has<PlayerIdentity>(owner))
                {
                    domainRep = owner;
                    hasDomain = true;
                    break;
                }

                current = owner;
                guard++;
                if (guard > MaxOwnershipDepth)
                {
                    throw new InvalidOperationException("Ownership graph exceeded the maximum supported traversal depth.");
                }
            }

            _domainCache.Upsert(
                cacheKey,
                new DomainCacheEntry { DomainRep = domainRep, HasDomain = hasDomain },
                expiryTick: 1,
                payloadChanged: true,
                out _);
            return hasDomain;
        }

        /// <summary>Grant membership via the reverse index; the index is kept in lockstep with every runtime edge mutation.</summary>
        private bool HasControlsGrantFrom(Entity controllerRep, Entity target)
        {
            return _relationships.ReverseIndex.ContainsIncoming(target, controllerRep, _controlsTypeId);
        }

        /// <summary>Direct owner via the reverse index (owns edges have at most one live source per target).</summary>
        private bool TryGetDirectOwnerViaIndex(Entity owned, out Entity owner)
        {
            return _relationships.ReverseIndex.TryGetFirstIncoming(owned, _ownership.OwnsTypeId, out owner);
        }

        private int AppendOwnedSubtree(Entity owner, Span<Entity> buffer, int written)
        {
            _ownedScratch.Clear();
            _ownership.CollectOwned(owner, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count && written < buffer.Length; i++)
            {
                written = AppendUnique(buffer, written, _ownedScratch[i]);
            }

            return written;
        }

        private int CollectGrants(Entity controllerRep)
        {
            while (true)
            {
                int count = _relationships.CollectOutgoing(controllerRep, _controlsTypeId, _grantScratch);
                if (count < _grantScratch.Length)
                {
                    return count;
                }

                Array.Resize(ref _grantScratch, _grantScratch.Length * 2);
            }
        }

        private struct DomainCacheEntry
        {
            public Entity DomainRep;
            public bool HasDomain;
        }

        private static int AppendUniqueDomain(
            Span<Entity> domains,
            Span<bool> fullyControlled,
            int written,
            Entity candidate,
            bool full)
        {
            for (int i = 0; i < written; i++)
            {
                if (domains[i] == candidate)
                {
                    // Full entries are appended before partial ones, so an existing entry is never weaker.
                    return written;
                }
            }

            domains[written] = candidate;
            fullyControlled[written] = full;
            return written + 1;
        }

        private static int AppendUnique(Span<Entity> buffer, int written, Entity candidate)
        {
            for (int i = 0; i < written; i++)
            {
                if (buffer[i] == candidate)
                {
                    return written;
                }
            }

            buffer[written] = candidate;
            return written + 1;
        }
    }
}
