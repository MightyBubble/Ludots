path = 'src/Client/Ludots.Raylib.Render/Rendering/RaylibGpuSkinnedModelCache.cs'
with open(path, encoding='utf-8', newline='') as f:
    t = f.read()

old_head = """        private readonly IRenderAssetPathResolver? _vfs;
        private readonly Dictionary<int, Entry> _entries = new();
        private bool _disposed;

        public RaylibGpuSkinnedModelCache(IRenderAssetPathResolver? vfs)
        {
            _vfs = vfs;
        }"""
new_head = """        private readonly IRenderAssetPathResolver? _vfs;
        private readonly RaylibAssetStore<Model> _modelStore;
        private readonly Dictionary<int, Entry> _entries = new();
        private readonly Dictionary<int, RaylibAssetStore<Model>.Lease> _leases = new();
        private bool _disposed;

        public RaylibGpuSkinnedModelCache(IRenderAssetPathResolver? vfs, RaylibAssetStore<Model>? modelStore = null)
        {
            _vfs = vfs;
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
        }"""
assert old_head in t, "cache head not found"
t = t.replace(old_head, new_head, 1)

old_loop = """            for (int u = 0; u < descriptor.SourceUris.Length; u++)
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

                // 统一经装载入口（glTF native / OBJ、FBX、DAE 先转 GLB）——OBJ 直走
                // native LoadModel 是 #1050 的 AccessViolation 路径。
                string loadablePath = RaylibModelFileLoader.PrepareNativeLoadable(fullPath);

                Model model = RaylibNativeResources.LoadModel(loadablePath);
                if (model.meshCount <= 0)
                {
                    RaylibNativeResources.UnloadModel(model);
                    continue;
                }

                if (model.boneCount <= 0)"""
new_loop = """            List<string> loadFailures = new();
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
                string loadablePath = RaylibModelFileLoader.PrepareNativeLoadable(fullPath);

                if (!_modelStore.TryAcquire(uri, out RaylibAssetStore<Model>.Lease? lease, out string? acquireFailure))
                {
                    loadFailures.Add($"'{uri}': {acquireFailure}");
                    continue;
                }

                Model model = lease!.Resource;

                if (model.boneCount <= 0)"""
assert old_loop in t, "load loop not found"
t = t.replace(old_loop, new_loop, 1)

t = t.replace("""                if (model.boneCount <= 0)
                {
                    RaylibNativeResources.UnloadModel(model);
                    _entries[meshAssetId] = default;""",
"""                if (model.boneCount <= 0)
                {
                    lease.Dispose();
                    _entries[meshAssetId] = default;""", 1)
t = t.replace("""                if (model.boneCount > MaxBones)
                {
                    RaylibNativeResources.UnloadModel(model);
                    _entries[meshAssetId] = default;""",
"""                if (model.boneCount > MaxBones)
                {
                    lease.Dispose();
                    _entries[meshAssetId] = default;""", 1)
t = t.replace("""                    RaylibNativeResources.UnloadModel(model);
                    _entries[meshAssetId] = default;
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' loaded with animCount={animCount}; GpuSkinnedInstance forbids silent static fallback.");""",
"""                    lease.Dispose();
                    _entries[meshAssetId] = default;
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' loaded with animCount={animCount}; GpuSkinnedInstance forbids silent static fallback.");""", 1)
t = t.replace("""                        Rl.UnloadModelAnimations(animations, animCount);
                        RaylibNativeResources.UnloadModel(model);
                        _entries[meshAssetId] = default;""",
"""                        Rl.UnloadModelAnimations(animations, animCount);
                        lease.Dispose();
                        _entries[meshAssetId] = default;""", 1)
t = t.replace("""                var entry = new Entry(model, animations, animCount, fullPath, loaded: true);
                _entries[meshAssetId] = entry;
                return entry;
            }

            _entries[meshAssetId] = default;
            throw new InvalidOperationException(
                $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} could not resolve any existing model URI for GpuSkinnedInstance.");""",
"""                var entry = new Entry(model, animations, animCount, fullPath, loaded: true);
                _entries[meshAssetId] = entry;
                _leases[meshAssetId] = lease;
                return entry;
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} could not resolve any existing model URI for GpuSkinnedInstance. Attempts: [{string.Join("; ", loadFailures)}]");""", 1)

old_unload = """                if (entry.Animations != null && entry.AnimCount > 0)
                {
                    Rl.UnloadModelAnimations(entry.Animations, entry.AnimCount);
                }

                RaylibNativeResources.UnloadModel(entry.Model);
            }

            _entries.Clear();
        }"""
new_unload = """                if (entry.Animations != null && entry.AnimCount > 0)
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
        }"""
assert old_unload in t, "unload block not found"
t = t.replace(old_unload, new_unload, 1)

with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(t)
print("skinned cache migrated")
