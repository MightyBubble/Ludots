using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh
{
    public readonly struct NavQueryServiceKey : IEquatable<NavQueryServiceKey>
    {
        public readonly int Layer;
        public readonly int Profile;

        public NavQueryServiceKey(int layer, int profile)
        {
            Layer = layer;
            Profile = profile;
        }

        public bool Equals(NavQueryServiceKey other) => Layer == other.Layer && Profile == other.Profile;
        public override bool Equals(object obj) => obj is NavQueryServiceKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Layer, Profile);
    }

    public readonly struct NavQueryServiceStoreSnapshot
    {
        public NavQueryServiceStoreSnapshot(int layer, int profile, NavTileStore store)
        {
            Layer = layer;
            Profile = profile;
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public int Layer { get; }
        public int Profile { get; }
        public NavTileStore Store { get; }
    }

    public sealed class NavQueryServiceRegistry
    {
        private readonly Dictionary<NavQueryServiceKey, NavTileStore> _stores;
        private readonly NavQueryTileSpace _tileSpace;
        private readonly NavQueryServiceStoreSnapshot[] _snapshots;

        public NavQueryServiceRegistry(
            Dictionary<NavQueryServiceKey, NavTileStore> stores,
            in NavQueryTileSpace tileSpace)
        {
            _stores = stores ?? throw new ArgumentNullException(nameof(stores));
            _tileSpace = tileSpace;
            _snapshots = new NavQueryServiceStoreSnapshot[stores.Count];
            int index = 0;
            foreach (KeyValuePair<NavQueryServiceKey, NavTileStore> pair in stores)
            {
                _snapshots[index++] = new NavQueryServiceStoreSnapshot(
                    pair.Key.Layer,
                    pair.Key.Profile,
                    pair.Value);
            }

            Array.Sort(_snapshots, static (left, right) =>
            {
                int layer = left.Layer.CompareTo(right.Layer);
                return layer != 0 ? layer : left.Profile.CompareTo(right.Profile);
            });
        }

        public NavQueryTileSpace TileSpace => _tileSpace;

        public int StoreCount => _snapshots.Length;

        public int CopyStoreSnapshots(Span<NavQueryServiceStoreSnapshot> destination)
        {
            if (destination.Length < _snapshots.Length)
            {
                throw new ArgumentException(
                    $"NavQueryServiceRegistry store snapshot destination length {destination.Length} is below store count {_snapshots.Length}.",
                    nameof(destination));
            }

            _snapshots.AsSpan().CopyTo(destination);
            return _snapshots.Length;
        }

        public bool TryGetStore(int layer, int profile, out NavTileStore store)
        {
            return _stores.TryGetValue(new NavQueryServiceKey(layer, profile), out store);
        }

        public bool TryCreateQuery(int layer, int profile, NavAreaCostTable areaCosts, out NavQueryService service)
        {
            if (TryGetStore(layer, profile, out var store))
            {
                service = new NavQueryService(store, layer, areaCosts, _tileSpace);
                return true;
            }
            service = null;
            return false;
        }
    }
}
