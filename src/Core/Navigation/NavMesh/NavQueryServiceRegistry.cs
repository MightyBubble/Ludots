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
        private readonly int _gridOriginXcm;
        private readonly int _gridOriginYcm;

        public NavQueryServiceRegistry(Dictionary<NavQueryServiceKey, NavTileStore> stores, int tileWidthCm, int tileHeightCm)
            : this(stores, tileWidthCm, tileHeightCm, gridOriginXcm: 0, gridOriginYcm: 0)
        {
        }

        /// <param name="gridOriginXcm">
        /// 瓦片网格在世界系的最小角。烘焙按地形世界位置切块，板锚定居中世界时首个瓦片在负半轴；
        /// 缺省 0 只适用于从世界 0 起的地图。
        /// </param>
        public NavQueryServiceRegistry(
            Dictionary<NavQueryServiceKey, NavTileStore> stores,
            int tileWidthCm,
            int tileHeightCm,
            int gridOriginXcm,
            int gridOriginYcm)
        {
            _stores = stores ?? throw new ArgumentNullException(nameof(stores));
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm));
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm));
            _tileWidthCm = tileWidthCm;
            _tileHeightCm = tileHeightCm;
            _gridOriginXcm = gridOriginXcm;
            _gridOriginYcm = gridOriginYcm;
        }

        public int TileWidthCm => _tileWidthCm;

        public int TileHeightCm => _tileHeightCm;

        public int GridOriginXcm => _gridOriginXcm;

        public int GridOriginYcm => _gridOriginYcm;

        public bool TryGetStore(int layer, int profile, out NavTileStore store)
        {
            return _stores.TryGetValue(new NavQueryServiceKey(layer, profile), out store);
        }

        public bool TryCreateQuery(int layer, int profile, NavAreaCostTable areaCosts, out NavQueryService service)
        {
            if (TryGetStore(layer, profile, out var store))
            {
                service = new NavQueryService(store, layer, areaCosts, _tileWidthCm, _tileHeightCm, _gridOriginXcm, _gridOriginYcm);
                return true;
            }
            service = null;
            return false;
        }
    }
}
