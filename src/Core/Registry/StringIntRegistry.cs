using System;
using System.Collections.Generic;

namespace Ludots.Core.Registry
{
    /// <summary>
    /// Unified string-to-int mapping infrastructure.
    /// All domain registries (MeshAsset, Prefab, Performer, etc.) compose this
    /// class for their key mapping instead of implementing their own Dictionary logic.
    /// <para>
    /// Int IDs are auto-incremented and never externally specified.
    /// String keys are the single source of truth in configuration;
    /// int IDs are opaque runtime handles.
    /// </para>
    /// </summary>
    public sealed class StringIntRegistry
    {
        private readonly Dictionary<string, int> _nameToId;
        private string[] _idToName;
        private int _nextId;
        private readonly int _startId;
        private readonly int _invalidId;
        private bool _frozen;

        public int InvalidId => _invalidId;
        public bool IsFrozen => _frozen;
        public int Count => _nameToId.Count;

        public StringIntRegistry(
            int capacity = 256,
            int startId = 1,
            int invalidId = 0,
            StringComparer comparer = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _startId = startId;
            _invalidId = invalidId;
            _nextId = startId;
            _nameToId = new Dictionary<string, int>(capacity, comparer ?? StringComparer.OrdinalIgnoreCase);
            _idToName = new string[capacity];
        }

        /// <summary>
        /// Register a string key and return its auto-assigned int ID.
        /// If already registered, returns the existing ID (idempotent).
        /// </summary>
        public int Register(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name must not be null or whitespace.", nameof(name));

            if (_nameToId.TryGetValue(name, out int existing))
                return existing;

            if (_frozen)
                throw new InvalidOperationException($"StringIntRegistry is frozen. Cannot register '{name}'.");

            int id = _nextId++;
            EnsureCapacity(id);
            _nameToId[name] = id;
            _idToName[id] = name;
            return id;
        }

        /// <summary>
        /// Look up the int ID for a string key. Returns <see cref="InvalidId"/> if not found.
        /// </summary>
        public int GetId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return _invalidId;
            return _nameToId.TryGetValue(name, out int id) ? id : _invalidId;
        }

        /// <summary>
        /// Try to get the int ID for a string key.
        /// </summary>
        public bool TryGetId(string name, out int id)
        {
            if (!string.IsNullOrWhiteSpace(name) && _nameToId.TryGetValue(name, out id))
                return true;
            id = _invalidId;
            return false;
        }

        /// <summary>
        /// Reverse-lookup: get the string key for an int ID.
        /// Returns empty string if ID is out of range or unregistered.
        /// </summary>
        public string GetName(int id)
        {
            if (id < _startId || id >= _nextId) return string.Empty;
            if ((uint)id >= (uint)_idToName.Length) return string.Empty;
            return _idToName[id] ?? string.Empty;
        }

        public bool Contains(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _nameToId.ContainsKey(name);
        }

        /// <summary>
        /// Freeze the registry. No further registrations are allowed after this call.
        /// </summary>
        public void Freeze() => _frozen = true;

        private void EnsureCapacity(int id)
        {
            if (id < _idToName.Length) return;
            int newLen = Math.Max(_idToName.Length * 2, id + 1);
            Array.Resize(ref _idToName, newLen);
        }
    }
}
