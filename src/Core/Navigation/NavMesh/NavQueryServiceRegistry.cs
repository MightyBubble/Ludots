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
        // 每个 store 一份 Detour 网格缓存：TryCreateQuery 每次都产出新 service 实例
        // （AgentBridge HTTP 线程亦如此），缓存必须在 store 粒度跨实例共享
        private readonly Dictionary<NavTileStore, DetourQueryMeshCache> _meshCaches;
        private readonly int _tileWidthCm;
        private readonly int _tileHeightCm;

        public NavQueryServiceRegistry(Dictionary<NavQueryServiceKey, NavTileStore> stores, int tileWidthCm, int tileHeightCm)
        {
            _stores = stores ?? throw new ArgumentNullException(nameof(stores));
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm));
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm));
            _tileWidthCm = tileWidthCm;
            _tileHeightCm = tileHeightCm;

            _meshCaches = new Dictionary<NavTileStore, DetourQueryMeshCache>();
            foreach (NavTileStore store in stores.Values)
            {
                if (store != null && !_meshCaches.ContainsKey(store))
                {
                    _meshCaches.Add(store, new DetourQueryMeshCache());
                }
            }
        }

        public int TileWidthCm => _tileWidthCm;

        public int TileHeightCm => _tileHeightCm;

        public bool TryGetStore(int layer, int profile, out NavTileStore store)
        {
            return _stores.TryGetValue(new NavQueryServiceKey(layer, profile), out store);
        }

        /// <summary>取 layer/profile 对应 store 的 Detour 网格缓存（诊断与契约测试用，含 BuildCount 观测）。</summary>
        public bool TryGetMeshCache(int layer, int profile, out DetourQueryMeshCache cache)
        {
            if (TryGetStore(layer, profile, out NavTileStore store))
            {
                cache = _meshCaches[store];
                return true;
            }

            cache = null!;
            return false;
        }

        public bool TryCreateQuery(int layer, int profile, NavAreaCostTable areaCosts, out NavQueryService service)
        {
            if (TryGetStore(layer, profile, out var store))
            {
                service = new NavQueryService(
                    store,
                    layer,
                    areaCosts,
                    _tileWidthCm,
                    _tileHeightCm,
                    _meshCaches[store]);
                return true;
            }
            service = null;
            return false;
        }
    }
}
