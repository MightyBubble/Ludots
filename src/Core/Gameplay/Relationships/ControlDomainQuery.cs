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

        private readonly World _world;
        private readonly RelationshipRuntime _relationships;
        private readonly OwnershipResolver _ownership;
        private readonly int _controlsTypeId;
        private readonly List<Entity> _ownedScratch = new(32);
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
        /// Returns true when the target would appear in <see cref="CollectControlled"/> for the controller rep:
        /// it sits in the controller's owns subtree, is a directly granted non-rep entity, or sits in the owns
        /// subtree of a granted domain rep.
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

            if (!_world.Has<PlayerIdentity>(target) && _relationships.HasLink(controllerRep, target, _controlsTypeId))
            {
                return true;
            }

            Entity current = target;
            int guard = 0;
            while (_ownership.TryGetDirectOwner(current, out Entity owner))
            {
                if (owner == controllerRep)
                {
                    return true;
                }

                if (_world.Has<PlayerIdentity>(owner) && _relationships.HasLink(controllerRep, owner, _controlsTypeId))
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

            Entity current = target;
            int guard = 0;
            while (_ownership.TryGetDirectOwner(current, out Entity owner))
            {
                if (_world.Has<PlayerIdentity>(owner))
                {
                    domainRep = owner;
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
