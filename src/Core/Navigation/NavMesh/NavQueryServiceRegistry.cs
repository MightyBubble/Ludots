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
        private readonly int _originXcm;
        private readonly int _originZcm;

        /// <summary>
        /// 零原点寻址帧：tile (0,0) 的世界原点即世界坐标原点。既有调用点的显式契约，
        /// 等价于 <see cref="NavQueryServiceRegistry(Dictionary{NavQueryServiceKey, NavTileStore}, int, int, int, int)"/> 传 0。
        /// </summary>
        public NavQueryServiceRegistry(Dictionary<NavQueryServiceKey, NavTileStore> stores, int tileWidthCm, int tileHeightCm)
            : this(stores, tileWidthCm, tileHeightCm, originXcm: 0, originZcm: 0)
        {
        }

        /// <summary>
        /// 显式寻址帧：tile (0,0) 的世界原点由地图声明的 NavTileGrid.OriginXcm/OriginZcm 给定，
        /// 查询定位（LocateTile）必须以该原点为参考系，不得假设世界原点即网格原点。
        /// </summary>
        public NavQueryServiceRegistry(
            Dictionary<NavQueryServiceKey, NavTileStore> stores,
            int tileWidthCm,
            int tileHeightCm,
            int originXcm,
            int originZcm)
        {
            _stores = stores ?? throw new ArgumentNullException(nameof(stores));
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm));
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm));
            _tileWidthCm = tileWidthCm;
            _tileHeightCm = tileHeightCm;
            _originXcm = originXcm;
            _originZcm = originZcm;
        }

        public int TileWidthCm => _tileWidthCm;

        public int TileHeightCm => _tileHeightCm;

        /// <summary>寻址帧原点（tile (0,0) 的世界 X 坐标，cm）。</summary>
        public int OriginXcm => _originXcm;

        /// <summary>寻址帧原点（tile (0,0) 的世界 Z 坐标，cm）。</summary>
        public int OriginZcm => _originZcm;

        public bool TryGetStore(int layer, int profile, out NavTileStore store)
        {
            return _stores.TryGetValue(new NavQueryServiceKey(layer, profile), out store);
        }

        public bool TryCreateQuery(int layer, int profile, NavAreaCostTable areaCosts, out NavQueryService service)
        {
            if (TryGetStore(layer, profile, out var store))
            {
                service = new NavQueryService(store, layer, areaCosts, _tileWidthCm, _tileHeightCm, _originXcm, _originZcm);
                return true;
            }
            service = null;
            return false;
        }
    }
}
