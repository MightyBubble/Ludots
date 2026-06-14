using System;
using System.Collections.Generic;

namespace Ludots.Core.Layers
{
    /// <summary>
    /// Maps layer names (e.g. "Hero", "Projectile", "Structure") to bit indices (0..31).
    /// Thread-safe after Freeze(). Shared across all systems.
    /// </summary>
    public static class LayerRegistry
    {
        public const int MaxLayers = 32;

        private static readonly Dictionary<string, int> _nameToIndex = new();
        private static readonly string[] _indexToName = new string[MaxLayers];
        private static int _nextIndex;
        private static bool _frozen;

        public static bool IsFrozen => _frozen;
        public static int Count => _nextIndex;

        public static void Freeze() { _frozen = true; }

        public static void Clear()
        {
            _nameToIndex.Clear();
            Array.Clear(_indexToName, 0, MaxLayers);
            _nextIndex = 0;
            _frozen = false;
        }

        public static int Register(string name)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("LayerRegistry is frozen.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("LayerRegistry requires a non-empty layer name.");
            }

            if (_nameToIndex.TryGetValue(name, out var idx))
            {
                return idx;
            }

            if (_nextIndex >= MaxLayers)
            {
                throw new InvalidOperationException($"LayerRegistry max {MaxLayers} layers reached.");
            }

            idx = _nextIndex++;
            _nameToIndex[name] = idx;
            _indexToName[idx] = name;
            return idx;
        }

        public static int GetIndex(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("LayerRegistry requires a non-empty layer name.");
            }

            if (_nameToIndex.TryGetValue(name, out var idx))
            {
                return idx;
            }

            throw new InvalidOperationException($"LayerRegistry has no registered layer '{name}'.");
        }

        public static string GetName(int index)
        {
            if ((uint)index >= MaxLayers || string.IsNullOrEmpty(_indexToName[index]))
            {
                throw new InvalidOperationException($"LayerRegistry has no registered layer at index {index}.");
            }

            return _indexToName[index];
        }

        public static uint GetBit(string name)
        {
            return 1u << GetIndex(name);
        }

        public static uint GetCombinedMask(params string[] names)
        {
            if (names == null || names.Length == 0)
            {
                throw new InvalidOperationException("LayerRegistry requires at least one layer name for a combined mask.");
            }

            uint mask = 0;
            foreach (var name in names)
            {
                mask |= 1u << GetIndex(name);
            }

            return mask;
        }

        public static uint GetCombinedMask(List<string> names)
        {
            if (names == null || names.Count == 0)
            {
                throw new InvalidOperationException("LayerRegistry requires at least one layer name for a combined mask.");
            }

            uint mask = 0;
            for (int i = 0; i < names.Count; i++)
            {
                mask |= 1u << GetIndex(names[i]);
            }

            return mask;
        }
    }
}
