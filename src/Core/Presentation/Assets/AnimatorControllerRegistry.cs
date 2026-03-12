using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimatorControllerRegistry
    {
        private readonly StringIntRegistry _ids;

        public AnimatorControllerRegistry(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.OrdinalIgnoreCase);
        }

        public int Register(string key) => _ids.Register(key);
        public int GetId(string key) => _ids.GetId(key);
        public string GetName(int id) => _ids.GetName(id);
    }
}
