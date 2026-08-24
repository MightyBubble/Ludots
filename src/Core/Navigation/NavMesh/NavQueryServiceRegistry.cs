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

    public sealed class NavQueryServiceRegistry
    {
        private readonly Dictionary<NavQueryServiceKey, NavTileStore> _stores;
        private readonly int _tileWidthCm;
        private readonly int _tileHeightCm;

        public NavQueryServiceRegistry(Dictionary<NavQueryServiceKey, NavTileStore> stores, int tileWidthCm, int tileHeightCm)
        {
            _stores = stores ?? throw new ArgumentNullException(nameof(stores));
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm));
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm));
            _tileWidthCm = tileWidthCm;
            _tileHeightCm = tileHeightCm;
        }

        public bool TryGetStore(int layer, int profile, out NavTileStore store)
        {
            return _stores.TryGetValue(new NavQueryServiceKey(layer, profile), out store);
        }

        public bool TryCreateQuery(int layer, int profile, NavAreaCostTable areaCosts, out NavQueryService service)
        {
            if (TryGetStore(layer, profile, out var store))
            {
                service = new NavQueryService(store, layer, areaCosts, _tileWidthCm, _tileHeightCm);
                return true;
            }
            service = null;
            return false;
        }
    }
}
