using System.Collections.Generic;

namespace Ludots.Core.Scripting
{
    public class ScriptContext
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();

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
