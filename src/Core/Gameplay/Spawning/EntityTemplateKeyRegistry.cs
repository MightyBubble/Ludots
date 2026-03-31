using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Spawning
{
    public sealed class EntityTemplateKeyRegistry
    {
        private StringIntRegistry _keys;

        public EntityTemplateKeyRegistry(int capacity = 256)
        {
            _keys = CreateRegistry(capacity);
        }

        public int Count => _keys.Count;

        public void Clear(int capacity = 256)
        {
            _keys = CreateRegistry(capacity);
        }

        public int Register(string templateId) => _keys.Register(templateId);

        public int GetId(string templateId) => _keys.GetId(templateId);

        public bool TryGetId(string templateId, out int templateKeyId) => _keys.TryGetId(templateId, out templateKeyId);

        public string GetName(int templateKeyId) => _keys.GetName(templateKeyId);

        public void Freeze() => _keys.Freeze();

        private static StringIntRegistry CreateRegistry(int capacity)
        {
            return new StringIntRegistry(
                capacity: Math.Max(16, capacity),
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
        }
    }
}
