using System.Collections.Generic;

namespace Ludots.Core.Map
{
    /// <summary>
    /// Hierarchical key-value context for nested maps.
    /// Lookups chain: local → parent → root.
    /// </summary>
    public sealed class MapContext
    {
        private readonly Dictionary<string, object> _local = new Dictionary<string, object>();
        private readonly MapContext _parent;

        public MapContext(MapContext parent = null)
        {
            _parent = parent;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_local.TryGetValue(key, out var obj) && obj is T typed)
            {
                value = typed;
                return true;
            }
            if (_parent != null)
            {
                return _parent.TryGet(key, out value);
            }
            value = default;
            return false;
        }

        public T Get<T>(string key)
        {
            if (TryGet<T>(key, out var val)) return val;
            throw new KeyNotFoundException($"MapContext key not found: {key}");
        }

        /// <summary>
        /// Sets a value in the local scope only (never writes to parent).
        /// </summary>
        public void Set(string key, object value)
        {
            _local[key] = value;
        }

        public bool ContainsKey(string key)
        {
            if (_local.ContainsKey(key)) return true;
            return _parent?.ContainsKey(key) ?? false;
        }
    }
}
