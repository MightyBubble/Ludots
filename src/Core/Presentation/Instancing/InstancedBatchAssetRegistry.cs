using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Instancing
{
    public sealed class InstancedBatchAssetRegistry
    {
        private readonly StringIntRegistry _ids;
        private InstancedBatchAsset[] _assets;

        public InstancedBatchAssetRegistry(int capacity = 256)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, StringComparer.Ordinal);
            _assets = new InstancedBatchAsset[capacity];
        }

        public int Register(string key, InstancedBatchAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            int id = _ids.Register(key);
            EnsureCapacity(id);
            asset.Id = id;
            asset.Key = key;
            _assets[id] = asset;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int id, out InstancedBatchAsset asset)
        {
            if ((uint)id < (uint)_assets.Length)
            {
                asset = _assets[id];
                return asset != null;
            }

            asset = null!;
            return false;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _assets.Length)
            {
                return;
            }

            int next = Math.Max(_assets.Length * 2, id + 1);
            Array.Resize(ref _assets, next);
        }
    }
}
