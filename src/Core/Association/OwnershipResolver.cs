using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Association
{
    public sealed class OwnershipResolver
    {
        private readonly RelationshipRuntime _relationships;
        private readonly int _ownsTypeId;
        private readonly List<Entity> _queue = new(32);
        private readonly List<Entity> _visited = new(32);
        private Entity[] _edgeScratch = new Entity[8];
        private Entity[] _incomingScratch = new Entity[2];

        public OwnershipResolver(RelationshipRuntime relationships, int ownsTypeId)
        {
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
            if (ownsTypeId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ownsTypeId));
            }

            _ownsTypeId = ownsTypeId;
        }

        public int OwnsTypeId => _ownsTypeId;

        public void EnsureOwnership(Entity owner, Entity owned)
        {
            if (owner == Entity.Null)
            {
                throw new ArgumentException("Ownership owner must not be null.", nameof(owner));
            }

            if (owned == Entity.Null)
            {
                throw new ArgumentException("Ownership target must not be null.", nameof(owned));
            }

            ClearOwnership(owned, owner);
            _relationships.EnsureLink(owner, owned, _ownsTypeId);
        }

        public void ClearOwnership(Entity owned)
        {
            ClearOwnership(owned, Entity.Null);
        }

        public bool TryGetDirectOwner(Entity owned, out Entity owner)
        {
            owner = Entity.Null;
            if (owned == Entity.Null)
            {
                return false;
            }

            int count = CollectIncomingOwners(owned);
            if (count <= 0)
            {
                return false;
            }

            owner = _incomingScratch[0];
            return owner != Entity.Null;
        }

        public bool TryResolveRootOwner(Entity owned, out Entity owner)
        {
            owner = Entity.Null;
            if (owned == Entity.Null)
            {
                return false;
            }

            Entity current = owned;
            int guard = 0;
            while (TryGetDirectOwner(current, out Entity directOwner))
            {
                owner = directOwner;
                current = directOwner;
                guard++;
                if (guard > 1024)
                {
                    throw new InvalidOperationException("Ownership graph exceeded the maximum supported traversal depth.");
                }
            }

            return owner != Entity.Null;
        }

        public bool IsOwnedBy(Entity possibleOwner, Entity owned)
        {
            if (possibleOwner == Entity.Null || owned == Entity.Null)
            {
                return false;
            }

            Entity current = owned;
            int guard = 0;
            while (TryGetDirectOwner(current, out Entity directOwner))
            {
                if (directOwner == possibleOwner)
                {
                    return true;
                }

                current = directOwner;
                guard++;
                if (guard > 1024)
                {
                    throw new InvalidOperationException("Ownership graph exceeded the maximum supported traversal depth.");
                }
            }

            return false;
        }

        public void CollectOwned(Entity owner, List<Entity> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (owner == Entity.Null)
            {
                return;
            }

            _queue.Clear();
            _visited.Clear();
            _queue.Add(owner);
            _visited.Add(owner);

            for (int queueIndex = 0; queueIndex < _queue.Count; queueIndex++)
            {
                Entity current = _queue[queueIndex];
                int childCount = CollectOutgoingOwned(current);
                for (int i = 0; i < childCount; i++)
                {
                    Entity child = _edgeScratch[i];
                    if (child == Entity.Null || Contains(_visited, child))
                    {
                        continue;
                    }

                    _visited.Add(child);
                    _queue.Add(child);
                    output.Add(child);
                }
            }
        }

        private void ClearOwnership(Entity owned, Entity preservedOwner)
        {
            if (owned == Entity.Null)
            {
                return;
            }

            int count = CollectIncomingOwners(owned);
            for (int i = 0; i < count; i++)
            {
                Entity currentOwner = _incomingScratch[i];
                if (currentOwner == Entity.Null || currentOwner == preservedOwner)
                {
                    continue;
                }

                _relationships.RemoveLink(currentOwner, owned, _ownsTypeId);
            }
        }

        private int CollectOutgoingOwned(Entity owner)
        {
            while (true)
            {
                int count = _relationships.CollectOutgoing(owner, _ownsTypeId, _edgeScratch);
                if (count < _edgeScratch.Length)
                {
                    return count;
                }

                Array.Resize(ref _edgeScratch, _edgeScratch.Length * 2);
            }
        }

        private int CollectIncomingOwners(Entity owned)
        {
            while (true)
            {
                int count = _relationships.CollectIncoming(owned, _ownsTypeId, _incomingScratch);
                if (count < _incomingScratch.Length)
                {
                    if (count > 1)
                    {
                        throw new InvalidOperationException(
                            $"Owns relationship must have a single direct owner, but entity {owned.Id} has {count} owners.");
                    }

                    return count;
                }

                Array.Resize(ref _incomingScratch, _incomingScratch.Length * 2);
            }
        }

        private static bool Contains(List<Entity> entities, Entity entity)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] == entity)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
