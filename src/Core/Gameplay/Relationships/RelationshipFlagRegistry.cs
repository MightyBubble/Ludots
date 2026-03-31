using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipFlagRegistry
    {
        private readonly StringIntRegistry _ids = new(capacity: 32, startId: 0, invalidId: -1, comparer: StringComparer.Ordinal);
        private int _count;

        public int Register(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Relationship flag name must not be empty.", nameof(name));
            }

            if (_ids.TryGetId(name, out int existingId))
            {
                return existingId;
            }

            if (_count >= 32)
            {
                throw new InvalidOperationException("RelationshipFlagRegistry supports at most 32 flags.");
            }

            int id = _count;
            _ids.Register(name);
            _count++;
            return id;
        }

        public bool TryGetId(string name, out int id) => _ids.TryGetId(name, out id);

        public int GetId(string name)
        {
            if (!_ids.TryGetId(name, out int id))
            {
                throw new InvalidOperationException($"Unknown relationship flag '{name}'.");
            }

            return id;
        }

        public uint GetMask(int flagId)
        {
            if ((uint)flagId >= 32u)
            {
                throw new ArgumentOutOfRangeException(nameof(flagId), "Relationship flag id must be within [0, 31].");
            }

            return 1u << flagId;
        }

    }
}
