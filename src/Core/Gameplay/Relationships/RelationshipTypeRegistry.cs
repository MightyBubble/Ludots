using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Relationships
{
    public readonly struct RelationshipTypeDefinition
    {
        public RelationshipTypeDefinition(int id, string name, bool isSymmetric)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsSymmetric = isSymmetric;
        }

        public int Id { get; }
        public string Name { get; }
        public bool IsSymmetric { get; }
    }

    public sealed class RelationshipTypeRegistry
    {
        public const int AnyTypeId = -1;

        private readonly StringIntRegistry _ids = new(capacity: 16, startId: 0, invalidId: -1, comparer: StringComparer.Ordinal);
        private RelationshipTypeDefinition[] _definitions = new RelationshipTypeDefinition[16];
        private int _count;

        public int Count => _count;

        public int Register(string name, bool isSymmetric = false)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Relationship type name must not be empty.", nameof(name));
            }

            if (_ids.TryGetId(name, out int existingId))
            {
                return existingId;
            }

            int id = _count;
            _ids.Register(name);
            EnsureCapacity(id + 1);
            _definitions[id] = new RelationshipTypeDefinition(id, name, isSymmetric);
            _count++;
            return id;
        }

        public bool TryGetId(string name, out int id) => _ids.TryGetId(name, out id);

        public int GetId(string name)
        {
            if (!_ids.TryGetId(name, out int id))
            {
                throw new InvalidOperationException($"Unknown relationship type '{name}'.");
            }

            return id;
        }

        public ref readonly RelationshipTypeDefinition Get(int id)
        {
            if ((uint)id >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(id), $"Relationship type id {id} is not registered.");
            }

            return ref _definitions[id];
        }

        private void EnsureCapacity(int requiredCount)
        {
            if (requiredCount <= _definitions.Length)
            {
                return;
            }

            int newLength = Math.Max(_definitions.Length * 2, requiredCount);
            Array.Resize(ref _definitions, newLength);
        }
    }
}
