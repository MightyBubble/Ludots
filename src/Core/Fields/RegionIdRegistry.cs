using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Fields
{
    /// <summary>
    /// Per-layer region id table. Id 0 is reserved for "no region" and never maps to a key;
    /// registrations start at 1. Registration is atomic against capacity: the fullness check
    /// runs before any write, so a rejected registration leaves the table untouched.
    /// </summary>
    public sealed class RegionIdRegistry
    {
        private readonly StringIntRegistry _keys;
        private readonly string _layerKey;
        private readonly int _maxRegionIds;

        public RegionIdRegistry(string layerKey, int maxRegionIds, int capacity = 16)
        {
            if (string.IsNullOrWhiteSpace(layerKey))
            {
                throw new ArgumentException("Region registry layer key is required.", nameof(layerKey));
            }

            if (maxRegionIds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRegionIds), "Region registry capacity must be positive.");
            }

            _layerKey = layerKey;
            _maxRegionIds = maxRegionIds;
            _keys = new StringIntRegistry(capacity: capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        }

        public int MaxRegionIds => _maxRegionIds;
        public int Count => _keys.Count;

        public int Register(string regionKey)
        {
            if (_keys.TryGetId(regionKey, out int existing))
            {
                return existing;
            }

            if (_keys.Count >= _maxRegionIds)
            {
                throw new InvalidOperationException(
                    $"Field layer '{_layerKey}' region registry is full: {_keys.Count} of {_maxRegionIds} region ids used.");
            }

            return _keys.Register(regionKey);
        }

        public int GetId(string regionKey) => _keys.GetId(regionKey);

        public string GetName(int regionId) => _keys.GetName(regionId);

        public bool Contains(string regionKey) => _keys.Contains(regionKey);

        public void Freeze() => _keys.Freeze();
    }
}
