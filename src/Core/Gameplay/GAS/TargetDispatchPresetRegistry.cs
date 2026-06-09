using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Named payload context mappings shared by effect templates and graph fan-out dispatch.
    /// All content-facing preset names are config-driven; the runtime only stores resolved mappings.
    /// </summary>
    public sealed class TargetDispatchPresetRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.Ordinal);
        private readonly List<TargetResolverContextMapping> _mappings = new() { default };

        public int Count => _mappings.Count - 1;

        public void Clear()
        {
            _nameToId.Clear();
            _mappings.Clear();
            _mappings.Add(default);
        }

        public int Register(string id, in TargetResolverContextMapping mapping)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Target dispatch preset id must not be null or whitespace.", nameof(id));
            }
            if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Target dispatch preset id must not include leading or trailing whitespace.", nameof(id));
            }

            if (_nameToId.TryGetValue(id, out int existingId))
            {
                _mappings[existingId] = mapping;
                return existingId;
            }

            int newId = _mappings.Count;
            _nameToId[id] = newId;
            _mappings.Add(mapping);
            return newId;
        }

        public int GetId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !_nameToId.TryGetValue(id, out int resolvedId))
            {
                throw new InvalidOperationException($"Unknown target dispatch preset '{id}'.");
            }

            return resolvedId;
        }

        public bool TryGetId(string id, out int presetId)
        {
            if (!string.IsNullOrWhiteSpace(id) && _nameToId.TryGetValue(id, out presetId))
            {
                return true;
            }

            presetId = 0;
            return false;
        }

        public TargetResolverContextMapping Get(int presetId)
        {
            if (!TryGet(presetId, out _))
            {
                throw new InvalidOperationException($"Unknown target dispatch preset id '{presetId}'.");
            }

            return _mappings[presetId];
        }

        public bool TryGet(int presetId, out TargetResolverContextMapping mapping)
        {
            if (presetId > 0 && presetId < _mappings.Count)
            {
                mapping = _mappings[presetId];
                return true;
            }

            mapping = default;
            return false;
        }
    }
}
