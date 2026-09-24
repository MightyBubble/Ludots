using System;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class EmitterAssetRegistry
    {
        private readonly StringIntRegistry _ids;
        private EmitterAssetDescriptor[] _descriptors;
        private bool[] _registered;

        public int Count { get; private set; }

        public EmitterAssetRegistry(int capacity = 256)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, StringComparer.Ordinal);
            _descriptors = new EmitterAssetDescriptor[capacity];
            _registered = new bool[capacity];
        }

        public int Register(
            string key,
            AssetKind assetKind,
            int runtimeFormatVersion,
            string sha256)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Emitter asset key must not be empty.", nameof(key));
            }

            if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Emitter asset key must not include leading or trailing whitespace.", nameof(key));
            }

            if (!assetKind.IsEmitterKind())
            {
                throw new ArgumentOutOfRangeException(nameof(assetKind), assetKind, "Emitter assets require a concrete emitter AssetKind.");
            }

            string normalizedSha256 = EmitterAssetDescriptor.NormalizeSha256(sha256);
            if (runtimeFormatVersion != EmitterAssetDescriptor.RequiredRuntimeFormatVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(runtimeFormatVersion),
                    runtimeFormatVersion,
                    $"Emitter assets require runtime format version {EmitterAssetDescriptor.RequiredRuntimeFormatVersion}.");
            }

            int id = _ids.Register(key);
            EnsureCapacity(id);
            if (_registered[id] &&
                (_descriptors[id].AssetKind != assetKind ||
                 _descriptors[id].RuntimeFormatVersion != runtimeFormatVersion ||
                 !string.Equals(_descriptors[id].Sha256, normalizedSha256, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Emitter asset '{key}' is already registered with a different kind, runtime format version, or SHA-256.");
            }

            _descriptors[id] = new EmitterAssetDescriptor(
                id,
                assetKind,
                runtimeFormatVersion,
                normalizedSha256,
                Array.Empty<string>());
            if (!_registered[id])
            {
                Count++;
            }
            _registered[id] = true;
            return id;
        }

        public void BindSourceUris(
            string key,
            AssetKind expectedKind,
            string[] sourceUris)
        {
            int id = ResolveId(expectedKind, key);
            if (sourceUris == null || sourceUris.Length == 0)
            {
                throw new ArgumentException("Emitter host binding requires at least one source URI.", nameof(sourceUris));
            }

            for (int i = 0; i < sourceUris.Length; i++)
            {
                string uri = sourceUris[i];
                if (string.IsNullOrWhiteSpace(uri) ||
                    !string.Equals(uri, uri.Trim(), StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Emitter host binding source URI at index {i} must be a canonical non-empty string.",
                        nameof(sourceUris));
                }
            }

            EmitterAssetDescriptor descriptor = _descriptors[id];
            _descriptors[id] = new EmitterAssetDescriptor(
                id,
                descriptor.AssetKind,
                descriptor.RuntimeFormatVersion,
                descriptor.Sha256,
                sourceUris);
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGetDescriptor(int assetId, out EmitterAssetDescriptor descriptor)
        {
            if ((uint)assetId < (uint)_descriptors.Length && _registered[assetId])
            {
                descriptor = _descriptors[assetId];
                return true;
            }

            descriptor = default;
            return false;
        }

        public int ResolveId(AssetKind expectedKind, string key)
        {
            if (!expectedKind.IsEmitterKind())
            {
                throw new ArgumentOutOfRangeException(nameof(expectedKind), expectedKind, "Emitter asset resolution requires a concrete emitter AssetKind.");
            }

            int id = _ids.GetId(key);
            if (!TryGetDescriptor(id, out EmitterAssetDescriptor descriptor))
            {
                throw new InvalidOperationException($"Unknown emitter asset '{key}'.");
            }

            if (descriptor.AssetKind != expectedKind)
            {
                throw new InvalidOperationException(
                    $"Emitter asset '{key}' is '{descriptor.AssetKind}', not requested kind '{expectedKind}'.");
            }

            return id;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _descriptors.Length)
            {
                return;
            }

            int next = Math.Max(_descriptors.Length * 2, id + 1);
            Array.Resize(ref _descriptors, next);
            Array.Resize(ref _registered, next);
        }
    }
}
