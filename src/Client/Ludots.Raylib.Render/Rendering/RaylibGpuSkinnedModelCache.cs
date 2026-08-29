using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// GpuSkinnedInstance model cache: Model + ModelAnimation* + animCount.
    /// Missing animations fail loud — no silent static fallback.
    /// </summary>
    public sealed unsafe class RaylibGpuSkinnedModelCache : IDisposable
    {
        public const int MaxBones = 128;

        public readonly struct Entry
        {
            public readonly Model Model;
            public readonly ModelAnimation* Animations;
            public readonly int AnimCount;
            public readonly string SourcePath;
            public readonly bool Loaded;

            public Entry(Model model, ModelAnimation* animations, int animCount, string sourcePath, bool loaded)
            {
                Model = model;
                Animations = animations;
                AnimCount = animCount;
                SourcePath = sourcePath;
                Loaded = loaded;
            }
        }

        private readonly IRenderAssetPathResolver? _vfs;
        private readonly RaylibAssetStore<Model> _modelStore;
        private readonly Dictionary<int, Entry> _entries = new();
        private readonly Dictionary<int, RaylibAssetStore<Model>.Lease> _leases = new();
        private bool _disposed;

        private readonly bool _ownsModelStore;

        public RaylibGpuSkinnedModelCache(IRenderAssetPathResolver? vfs, RaylibAssetStore<Model>? modelStore = null)
        {
            _vfs = vfs;
            _ownsModelStore = modelStore == null;
            _modelStore = modelStore ?? new RaylibAssetStore<Model>(vfs, fullPath =>
            {
                // 统一经装载入口（glTF native / OBJ、FBX、DAE 先转 GLB）——OBJ 直走
                // native LoadModel 是 #1050 的 AccessViolation 路径。
                string loadablePath = RaylibModelFileLoader.PrepareNativeLoadable(fullPath);
                Model model = RaylibNativeResources.LoadModel(loadablePath);
                if (model.meshCount <= 0)
                {
                    RaylibNativeResources.UnloadModel(model);
                    throw new InvalidOperationException($"model '{fullPath}' loaded with meshCount=0.");
                }

                return model;
            }, RaylibNativeResources.UnloadModel);
        }

        public Entry GetOrLoad(int meshAssetId, in MeshAssetDescriptor descriptor)
        {
            ThrowIfDisposed();

            if (_entries.TryGetValue(meshAssetId, out Entry cached))
            {
                if (!cached.Loaded)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} previously failed to load for GpuSkinnedInstance.");
                }

                return cached;
            }

            if (descriptor.Type != MeshAssetType.Model)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} Type={descriptor.Type}; GpuSkinnedInstance requires MeshAssetType.Model.");
            }

            if (_vfs == null || descriptor.SourceUris == null || descriptor.SourceUris.Length == 0)
            {
                _entries[meshAssetId] = default;
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} has no VFS/sourceUris for GpuSkinnedInstance.");
            }

            List<string> loadFailures = new();
            for (int u = 0; u < descriptor.SourceUris.Length; u++)
            {
                string uri = descriptor.SourceUris[u];
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                if (!_vfs.TryResolveFullPath(uri, out string fullPath) || !File.Exists(fullPath))
                {
                    continue;
                }

                // 模型生命周期经共享存储按 URI 去重（#1327）；loadablePath 供动画装载复用。
                // 转换失败（不可转格式）只淘汰当前 URI，链式回退继续尝试后续 URI（复核修复）。
                string loadablePath;
                try
                {
                    loadablePath = RaylibModelFileLoader.PrepareNativeLoadable(fullPath);
                }
                catch (Exception ex)
                {
                    loadFailures.Add($"'{uri}': {ex.Message}");
                    continue;
                }

                if (!_modelStore.TryAcquire(uri, out RaylibAssetStore<Model>.Lease? lease, out string? acquireFailure))
                {
                    loadFailures.Add($"'{uri}': {acquireFailure}");
                    continue;
                }

                Model model = lease!.Resource;

                if (model.boneCount <= 0)
                {
                    lease.Dispose();
                    _entries[meshAssetId] = default;
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' has boneCount=0; GpuSkinnedInstance requires a skinned model.");
                }

                if (model.boneCount > MaxBones)
                {
                    lease.Dispose();
                    _entries[meshAssetId] = default;
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} boneCount={model.boneCount} exceeds MAX_BONE_NUM={MaxBones}.");
                }

                int animCount;
                ModelAnimation* animations = Rl.LoadModelAnimations(loadablePath, out animCount);
                if (animations == null || animCount <= 0)
                {
                    if (animations != null)
                    {
                        Rl.UnloadModelAnimations(animations, animCount);
                    }

                    lease.Dispose();
                    _entries[meshAssetId] = default;
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' loaded with animCount={animCount}; GpuSkinnedInstance forbids silent static fallback.");
                }

                for (int i = 0; i < animCount; i++)
                {
                    ModelAnimation anim = animations[i];
                    if (!Rl.IsModelAnimationValid(model, anim))
                    {
                        int animBones = anim.boneCount;
                        int modelBones = model.boneCount;
                        Rl.UnloadModelAnimations(animations, animCount);
                        lease.Dispose();
                        _entries[meshAssetId] = default;
                        throw new InvalidOperationException(
                            $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' animation[{i}] failed IsModelAnimationValid (modelBones={modelBones}, animBones={animBones}).");
                    }
                }

                var entry = new Entry(model, animations, animCount, fullPath, loaded: true);
                _entries[meshAssetId] = entry;
                _leases[meshAssetId] = lease;
                return entry;
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} could not resolve any existing model URI for GpuSkinnedInstance. Attempts: [{string.Join("; ", loadFailures)}]");
        }

        public void UnloadAll(Action<Model>? beforeUnloadModel = null)
        {
            if (_disposed)
            {
                return;
            }

            foreach (KeyValuePair<int, Entry> kvp in _entries)
            {
                Entry entry = kvp.Value;
                if (!entry.Loaded)
                {
                    continue;
                }

                beforeUnloadModel?.Invoke(entry.Model);

                if (entry.Animations != null && entry.AnimCount > 0)
                {
                    Rl.UnloadModelAnimations(entry.Animations, entry.AnimCount);
                }

                if (_leases.TryGetValue(kvp.Key, out RaylibAssetStore<Model>.Lease? lease))
                {
                    lease.Dispose();
                }
            }

            _entries.Clear();
            _leases.Clear();
        }

        public void Dispose()
        {
            UnloadAll();
            if (_ownsModelStore)
            {
                _modelStore.Dispose();
            }

            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibGpuSkinnedModelCache));
            }
        }
    }
}
