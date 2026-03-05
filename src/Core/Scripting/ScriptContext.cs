using System.Collections.Generic;

namespace Ludots.Core.Scripting
{
    public class ScriptContext
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();

        // --- Typed API (preferred) ---

        public void Set<T>(ServiceKey<T> key, T value)
            => _data[key.Name] = value;

        public T Get<T>(ServiceKey<T> key)
        {
            if (_data.TryGetValue(key.Name, out var val) && val is T tVal)
                return tVal;
            return default;
        }

        public bool Contains<T>(ServiceKey<T> key) => _data.ContainsKey(key.Name);

        // --- Legacy string API (kept for incremental migration) ---

        public void Set(string key, object value)
        {
            _data[key] = value;
        }

        public T Get<T>(string key)
        {
            if (_data.TryGetValue(key, out var val) && val is T tVal)
            {
                return tVal;
            }
            return default;
        }
        
        public bool Contains(string key) => _data.ContainsKey(key);
    }
}
