using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh
{
    public readonly struct NavQueryServiceStoreSnapshot
    {
        public readonly int Layer;
        public readonly int Profile;
        public readonly NavTileStore Store;

        public NavQueryServiceStoreSnapshot(int layer, int profile, NavTileStore store)
        {
            Layer = layer;
            Profile = profile;
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }
    }

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

    public sealed class NavQueryServiceRegistry
    {
        private readonly Dictionary<NavQueryServiceKey, NavTileStore> _stores;
        private readonly NavQueryServiceStoreSnapshot[] _snapshots;

        public NavQueryServiceRegistry(Dictionary<NavQueryServiceKey, NavTileStore> stores)
        {
            _stores = stores ?? throw new ArgumentNullException(nameof(stores));
            _snapshots = new NavQueryServiceStoreSnapshot[_stores.Count];
            int i = 0;
            foreach (KeyValuePair<NavQueryServiceKey, NavTileStore> kvp in _stores)
            {
                if (kvp.Value == null)
                {
                    throw new InvalidOperationException("NavQueryServiceRegistry cannot contain a null NavTileStore.");
                }

                _snapshots[i++] = new NavQueryServiceStoreSnapshot(kvp.Key.Layer, kvp.Key.Profile, kvp.Value);
            }
        }

        public bool TryGetStore(int layer, int profile, out NavTileStore store)
        {
            return _stores.TryGetValue(new NavQueryServiceKey(layer, profile), out store);
        }

        public NavQueryServiceStoreSnapshot[] SnapshotStores()
        {
            var snapshot = new NavQueryServiceStoreSnapshot[_snapshots.Length];
            Array.Copy(_snapshots, snapshot, _snapshots.Length);
            return snapshot;
        }

        public bool TryCreateQuery(int layer, int profile, NavAreaCostTable areaCosts, out NavQueryService service)
        {
            if (TryGetStore(layer, profile, out var store))
            {
                service = new NavQueryService(store, layer, areaCosts);
                return true;
            }
            service = null;
            return false;
        }
    }
}
