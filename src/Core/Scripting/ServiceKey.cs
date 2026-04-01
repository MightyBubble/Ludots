using System;
using System.Collections.Generic;

namespace Ludots.Core.Scripting
{
    /// <summary>
    /// A typed key for <see cref="ScriptContext"/> and GlobalContext lookups.
    /// Binds the key string to a value type at compile time, preventing
    /// type mismatches that the old string-based API could not catch.
    /// </summary>
    public readonly struct ServiceKey<T>
    {
        public string Name { get; }
        public ServiceKey(string name) => Name = name;
        public override string ToString() => $"ServiceKey<{typeof(T).Name}>(\"{Name}\")";
    }

    public interface IServiceScope
    {
        string Name { get; }
        void Set<T>(ServiceKey<T> key, T value);
        bool TryGet<T>(ServiceKey<T> key, out T value);
        T GetOrDefault<T>(ServiceKey<T> key);
        bool Remove<T>(ServiceKey<T> key);
    }

    /// <summary>
    /// Typed scope container used by engine/script contexts.
    /// Supports optional parent fallback to model a service scope graph.
    /// </summary>
    public sealed class TypedServiceScope : IServiceScope
    {
        private readonly Dictionary<string, object> _services;
        private readonly TypedServiceScope? _parent;

        public TypedServiceScope(string name, TypedServiceScope? parent = null, Dictionary<string, object>? seed = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Scope name is required.", nameof(name));
            }

            Name = name;
            _parent = parent;
            _services = seed ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public string Name { get; }

        // Compatibility bridge for legacy callers that still consume GlobalContext directly.
        public Dictionary<string, object> LegacyStore => _services;

        public void Set<T>(ServiceKey<T> key, T value)
        {
            Set(key.Name, value!);
        }

        public bool TryGet<T>(ServiceKey<T> key, out T value)
        {
            if (TryGet(key.Name, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default!;
            return false;
        }

        public T GetOrDefault<T>(ServiceKey<T> key)
        {
            return TryGet(key, out T value) ? value : default!;
        }

        public bool Remove<T>(ServiceKey<T> key)
        {
            return Remove(key.Name);
        }

        public void Set(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Service key is required.", nameof(key));
            }

            _services[key] = value;
        }

        public bool TryGet(string key, out object value)
        {
            if (_services.TryGetValue(key, out value))
            {
                return true;
            }

            if (_parent != null)
            {
                return _parent.TryGet(key, out value);
            }

            value = default!;
            return false;
        }

        public bool Contains(string key)
        {
            return _services.ContainsKey(key) || (_parent?.Contains(key) ?? false);
        }

        public bool Remove(string key)
        {
            return _services.Remove(key);
        }

        public IEnumerable<KeyValuePair<string, object>> EnumerateLocal()
        {
            return _services;
        }
    }
}
