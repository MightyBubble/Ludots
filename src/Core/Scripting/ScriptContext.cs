using System.Collections.Generic;

namespace Ludots.Core.Scripting
{
    public class ScriptContext
    {
        private readonly TypedServiceScope _services;

        public ScriptContext()
        {
            _services = new TypedServiceScope("script");
        }

        // --- Typed API (preferred) ---

        public void Set<T>(ServiceKey<T> key, T value)
            => _services.Set(key, value);

        public T Get<T>(ServiceKey<T> key)
        {
            return _services.GetOrDefault(key);
        }

        public bool TryGet<T>(ServiceKey<T> key, out T value)
            => _services.TryGet(key, out value);

        public bool Contains<T>(ServiceKey<T> key) => _services.Contains(key.Name);

        public void MergeFrom(TypedServiceScope sourceScope)
        {
            foreach (var kvp in sourceScope.EnumerateLocal())
            {
                _services.Set(kvp.Key, kvp.Value);
            }
        }

        // --- Legacy string API (kept for incremental migration) ---

        public void Set(string key, object value)
        {
            _services.Set(key, value);
        }

        public T Get<T>(string key)
        {
            if (_services.TryGet(key, out var val) && val is T tVal)
            {
                return tVal;
            }
            return default;
        }

        public bool Contains(string key) => _services.Contains(key);
    }
}
