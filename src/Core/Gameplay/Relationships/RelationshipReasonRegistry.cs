using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipReasonRegistry
    {
        private readonly StringIntRegistry _ids = new(capacity: 128, startId: 1, invalidId: 0, comparer: StringComparer.OrdinalIgnoreCase);

        public int Register(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Relationship reason name must not be empty.", nameof(name));
            }

            if (_ids.TryGetId(name, out int existingId))
            {
                return existingId;
            }

            return _ids.Register(name);
        }

        public bool TryGetId(string name, out int id) => _ids.TryGetId(name, out id);

        public string GetName(int id)
        {
            string name = _ids.GetName(id);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"Unknown relationship reason id '{id}'.");
            }

            return name;
        }

        public bool TryGetName(int id, out string? name)
        {
            name = _ids.GetName(id);
            return !string.IsNullOrWhiteSpace(name);
        }
    }
}
