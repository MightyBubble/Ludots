using System;
using System.Collections.Generic;

namespace Ludots.Core.Modding
{
    /// <summary>
    /// Startup-only semantic key to runtime id registry for extensible mod contracts.
    /// Builtins may reserve fixed ids; mods receive monotonically assigned ids.
    /// </summary>
    public sealed class ExtensionKeyRegistry
    {
        private readonly Dictionary<string, int> _keyToId;
        private string[] _idToKey;
        private readonly int _firstDynamicId;
        private readonly int _maxIdExclusive;
        private int _nextDynamicId;
        private bool _frozen;

        public ExtensionKeyRegistry(
            int capacity,
            int firstDynamicId,
            int maxIdExclusive,
            StringComparer? comparer = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (firstDynamicId <= 0) throw new ArgumentOutOfRangeException(nameof(firstDynamicId));
            if (maxIdExclusive <= firstDynamicId) throw new ArgumentOutOfRangeException(nameof(maxIdExclusive));

            _keyToId = new Dictionary<string, int>(capacity, comparer ?? StringComparer.Ordinal);
            _idToKey = new string[Math.Max(maxIdExclusive, firstDynamicId + 1)];
            _firstDynamicId = firstDynamicId;
            _maxIdExclusive = maxIdExclusive;
            _nextDynamicId = firstDynamicId;
        }

        public bool IsFrozen => _frozen;
        public int Count => _keyToId.Count;

        public void RegisterFixed(string key, int id)
        {
            key = NormalizeKey(key, nameof(key));
            ValidateId(id, nameof(id));

            if (_keyToId.TryGetValue(key, out int existingId))
            {
                if (existingId != id)
                {
                    throw new InvalidOperationException(
                        $"Extension key '{key}' is already bound to id {existingId}, cannot bind fixed id {id}.");
                }

                return;
            }

            if (_frozen)
            {
                throw new InvalidOperationException($"Extension registry is frozen. Cannot register fixed key '{key}'.");
            }

            string existingKey = GetKey(id);
            if (!string.IsNullOrEmpty(existingKey) && !string.Equals(existingKey, key, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Extension id {id} is already reserved by key '{existingKey}', cannot bind '{key}'.");
            }

            _keyToId[key] = id;
            _idToKey[id] = key;
        }

        public int RegisterDynamic(string key)
        {
            key = NormalizeKey(key, nameof(key));

            if (_keyToId.TryGetValue(key, out int existingId))
            {
                return existingId;
            }

            if (_frozen)
            {
                throw new InvalidOperationException($"Extension registry is frozen. Cannot register '{key}'.");
            }

            if (_nextDynamicId >= _maxIdExclusive)
            {
                throw new InvalidOperationException(
                    $"Extension registry exhausted dynamic ids [{_firstDynamicId}, {_maxIdExclusive}).");
            }

            int id = _nextDynamicId++;
            _keyToId[key] = id;
            _idToKey[id] = key;
            return id;
        }

        public bool TryGetId(string? key, out int id)
        {
            if (!string.IsNullOrWhiteSpace(key) && _keyToId.TryGetValue(key, out id))
            {
                return true;
            }

            id = 0;
            return false;
        }

        public int GetId(string? key)
        {
            return TryGetId(key, out int id) ? id : 0;
        }

        public string GetKey(int id)
        {
            if ((uint)id >= (uint)_idToKey.Length)
            {
                return string.Empty;
            }

            return _idToKey[id] ?? string.Empty;
        }

        public bool IsRegisteredId(int id)
        {
            return (uint)id < (uint)_idToKey.Length && !string.IsNullOrEmpty(_idToKey[id]);
        }

        public void Freeze() => _frozen = true;

        public void Clear()
        {
            _keyToId.Clear();
            Array.Clear(_idToKey, 0, _idToKey.Length);
            _nextDynamicId = _firstDynamicId;
            _frozen = false;
        }

        private void ValidateId(int id, string paramName)
        {
            if (id <= 0 || id >= _maxIdExclusive)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    $"Extension id {id} must be in range [1, {_maxIdExclusive}).");
            }
        }

        private static string NormalizeKey(string key, string paramName)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Extension key must not be null or whitespace.", paramName);
            }

            string trimmed = key.Trim();
            if (!string.Equals(key, trimmed, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Extension key '{key}' must not contain leading or trailing whitespace.", paramName);
            }

            return key;
        }
    }
}
