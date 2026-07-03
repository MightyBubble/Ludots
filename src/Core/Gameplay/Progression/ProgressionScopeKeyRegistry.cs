using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Progression
{
    public sealed class ProgressionScopeKeyRegistry
    {
        private readonly StringIntRegistry _registry = new(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.OrdinalIgnoreCase);

        public int Register(string key) => _registry.Register(key);

        public int GetId(string key) => _registry.GetId(key);

        public bool TryGetId(string key, out int id) => _registry.TryGetId(key, out id);

        public string GetName(int id) => _registry.GetName(id);

        public void Freeze() => _registry.Freeze();
    }
}
