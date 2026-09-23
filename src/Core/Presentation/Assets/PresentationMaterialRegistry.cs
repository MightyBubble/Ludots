using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationMaterialRegistry : IRenderMaterialAssets
    {
        public const string DefaultSurfaceKey = "default_surface";

        private readonly StringIntRegistry _ids;
        private MaterialAssetDescriptor[] _data;
        private bool[] _has;
        private readonly Dictionary<int, IReadOnlyDictionary<string, string>> _hostTextureUris = new();
        private readonly Dictionary<int, ResolvedMaterialAsset> _resolvedCache = new();

        public PresentationMaterialRegistry(int capacity = 256)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _data = new MaterialAssetDescriptor[capacity];
            _has = new bool[capacity];

            Register(DefaultSurfaceKey, MaterialAssetDomain.Surface, MaterialAssetFlags.None);
        }

        public int Register(
            string key,
            MaterialAssetDomain domain,
            MaterialAssetFlags flags,
            string? shaderKey = null,
            string? parentKey = null,
            IReadOnlyDictionary<string, float>? floatParams = null,
            IReadOnlyDictionary<string, Vector4>? colorParams = null)
        {
            int id = _ids.Register(key);
            EnsureCapacity(id);
            _data[id] = new MaterialAssetDescriptor(id, domain, flags, shaderKey, parentKey, floatParams, colorParams);
            _has[id] = true;
            _resolvedCache.Clear();
            return id;
        }

        /// <summary>宿主侧按名挂载贴图 URI（host_assets.json 每 backend 一份）；重复挂载后者覆盖。</summary>
        public void SetHostTextureUris(int id, IReadOnlyDictionary<string, string> textureUris)
        {
            if (textureUris == null)
            {
                throw new ArgumentNullException(nameof(textureUris));
            }

            if (!TryGet(id, out _))
            {
                throw new InvalidOperationException(
                    $"{nameof(PresentationMaterialRegistry)} cannot attach host textures to unregistered materialId={id}.");
            }

            var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in textureUris)
            {
                copy[pair.Key] = pair.Value;
            }

            _hostTextureUris[id] = copy;
            _resolvedCache.Clear();
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int id, out MaterialAssetDescriptor descriptor)
        {
            if ((uint)id < (uint)_data.Length && _has[id])
            {
                descriptor = _data[id];
                return true;
            }

            descriptor = default;
            return false;
        }

        public bool TryResolve(int id, out ResolvedMaterialAsset material)
        {
            if (!TryGet(id, out _))
            {
                material = default;
                return false;
            }

            if (_resolvedCache.TryGetValue(id, out material))
            {
                return true;
            }

            material = MaterialAssetResolver.Resolve(this, id, ResolveHostTextureUris);
            _resolvedCache[id] = material;
            return true;
        }

        private IReadOnlyDictionary<string, string>? ResolveHostTextureUris(int id)
        {
            return _hostTextureUris.TryGetValue(id, out IReadOnlyDictionary<string, string>? uris) ? uris : null;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _data.Length)
            {
                return;
            }

            int next = Math.Max(_data.Length * 2, id + 1);
            Array.Resize(ref _data, next);
            Array.Resize(ref _has, next);
        }
    }
}
