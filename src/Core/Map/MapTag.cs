using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Ludots.Core.Map
{
    public readonly struct MapTag : IEquatable<MapTag>
    {
        private static readonly ConcurrentDictionary<string, int> _registry = new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<int, string> _reverseRegistry = new ConcurrentDictionary<int, string>();
        private static int _nextId = 1;

        public readonly int Id;

        public string Name => _reverseRegistry.TryGetValue(Id, out var name) ? name : "Unknown";

        public MapTag(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Id = 0;
                return;
            }

            if (_registry.TryGetValue(name, out int existingId))
            {
                Id = existingId;
            }
            else
            {
                lock (_registry)
                {
                    if (_registry.TryGetValue(name, out existingId))
                    {
                        Id = existingId;
                    }
                    else
                    {
                        Id = _nextId++;
                        _registry[name] = Id;
                        _reverseRegistry[Id] = name;
                    }
                }
            }
        }

        public static MapTag Parse(string name) => new MapTag(name);

        public bool Equals(MapTag other) => Id == other.Id;
        public override bool Equals(object obj) => obj is MapTag other && Equals(other);
        public override int GetHashCode() => Id;
        public override string ToString() => Name;

        public static bool operator ==(MapTag left, MapTag right) => left.Id == right.Id;
        public static bool operator !=(MapTag left, MapTag right) => left.Id != right.Id;
    }
}
