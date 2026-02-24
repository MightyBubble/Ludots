using System;
using System.Collections.Generic;

namespace Ludots.Core.Layers
{
    /// <summary>
    /// Maps layer names (e.g. "Hero", "Projectile", "Structure") to bit indices (0..31).
    /// Thread-safe after Freeze(). Shared across all systems.
    ///
    /// Usage:
    ///   LayerRegistry.Register("Hero");       // → index 0
    ///   LayerRegistry.Register("Projectile");  // → index 1
    ///   uint heroMask = LayerRegistry.GetBit("Hero");  // → 0x0000_0001
    ///   uint mask = LayerRegistry.GetCombinedMask("Hero", "Projectile"); // → 0x0000_0003
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

        /// <summary>Register a layer name, returning its bit index (0..31). Idempotent.</summary>
        public static int Register(string name)
        {
            if (_frozen)
                throw new InvalidOperationException("LayerRegistry is frozen.");
            if (_nameToIndex.TryGetValue(name, out var idx))
                return idx;
            if (_nextIndex >= MaxLayers)
                throw new InvalidOperationException($"LayerRegistry: max {MaxLayers} layers reached.");

            idx = _nextIndex++;
            _nameToIndex[name] = idx;
            _indexToName[idx] = name;
            return idx;
        }

        /// <summary>Get the bit index for a name, or -1 if not found.</summary>
        public static int GetIndex(string name)
            => _nameToIndex.TryGetValue(name, out var idx) ? idx : -1;

        /// <summary>Get the name for a bit index, or null if unregistered.</summary>
        public static string GetName(int index)
            => (uint)index < MaxLayers ? _indexToName[index] : null;

        /// <summary>Get a single-bit mask for the named layer.</summary>
        public static uint GetBit(string name)
        {
            int idx = GetIndex(name);
            return idx >= 0 ? 1u << idx : 0u;
        }

        /// <summary>Get a combined bitmask for multiple layer names.</summary>
        public static uint GetCombinedMask(params string[] names)
        {
            uint mask = 0;
            foreach (var n in names)
            {
                int idx = GetIndex(n);
                if (idx >= 0) mask |= 1u << idx;
            }
            return mask;
        }

        /// <summary>Get a combined bitmask for multiple layer names (List version, zero-alloc on call).</summary>
        public static uint GetCombinedMask(List<string> names)
        {
            if (names == null) return 0;
            uint mask = 0;
            for (int i = 0; i < names.Count; i++)
            {
                int idx = GetIndex(names[i]);
                if (idx >= 0) mask |= 1u << idx;
            }
            return mask;
        }
    }
}
