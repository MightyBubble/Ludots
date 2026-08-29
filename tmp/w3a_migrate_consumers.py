path = 'src/Client/Ludots.Raylib.Render/Rendering/RaylibVfxRenderer.cs'
with open(path, encoding='utf-8', newline='') as f:
    t = f.read()

old_ctor = """    public sealed class RaylibVfxRenderer : IDisposable
    {
        private readonly IRenderAssetPathResolver? _vfs;
        private readonly Dictionary<RaylibVfxKey, RaylibParticleVfxInstance> _particleVfx = new();
        private readonly HashSet<RaylibVfxKey> _activeKeys = new();
        private readonly List<RaylibVfxKey> _inactiveKeys = new();
        private readonly Dictionary<int, Texture2D> _textureCache = new();

        public RaylibVfxRenderer(IRenderAssetPathResolver? vfs = null)
        {
            _vfs = vfs;
        }"""
new_ctor = """    public sealed class RaylibVfxRenderer : IDisposable
    {
        private readonly IRenderAssetPathResolver? _vfs;
        private readonly RaylibAssetStore<Texture2D> _textureStore;
        private readonly bool _ownsTextureStore;
        private readonly Dictionary<RaylibVfxKey, RaylibParticleVfxInstance> _particleVfx = new();
        private readonly HashSet<RaylibVfxKey> _activeKeys = new();
        private readonly List<RaylibVfxKey> _inactiveKeys = new();
        private readonly Dictionary<int, RaylibAssetStore<Texture2D>.Lease> _textureCache = new();

        public RaylibVfxRenderer(IRenderAssetPathResolver? vfs = null, RaylibAssetStore<Texture2D>? textureStore = null)
        {
            _vfs = vfs;
            _ownsTextureStore = textureStore == null;
            _textureStore = textureStore ?? new RaylibAssetStore<Texture2D>(
                vfs,
                fullPath =>
                {
                    Texture2D texture = RaylibNativeResources.LoadTexture(fullPath);
                    if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
                    {
                        if (texture.id != 0)
                        {
                            RaylibNativeResources.UnloadTexture(texture);
                        }

                        throw new InvalidOperationException(
                            $"raylib rejected particle sheet '{fullPath}' (textureId={texture.id}, size={texture.width}x{texture.height}).");
                    }

                    return texture;
                },
                RaylibNativeResources.UnloadTexture);
        }"""
assert old_ctor in t
t = t.replace(old_ctor, new_ctor, 1)

old_cache = """            if (_textureCache.TryGetValue(textureAssetId, out Texture2D cached))
            {
                return cached;
            }

            Texture2D loaded = LoadTexture(textureSheet.TextureAssetId, textureDescriptor);
            _textureCache.Add(textureAssetId, loaded);
            return loaded;"""
new_cache = """            if (_textureCache.TryGetValue(textureAssetId, out RaylibAssetStore<Texture2D>.Lease? cached))
            {
                return cached.Resource;
            }

            RaylibAssetStore<Texture2D>.Lease loaded = LoadTexture(textureSheet.TextureAssetId, textureDescriptor);
            _textureCache.Add(textureAssetId, loaded);
            return loaded.Resource;"""
assert old_cache in t
t = t.replace(old_cache, new_cache, 1)

old_load_sig = "        private Texture2D LoadTexture(string textureAssetKey, in MeshAssetDescriptor textureDescriptor)"
idx = t.find(old_load_sig)
assert idx >= 0
line_start = t.rfind("\n", 0, idx) + 1
open_brace = t.find("{", idx)
depth = 0
end = None
for i in range(open_brace, len(t)):
    if t[i] == "{":
        depth += 1
    elif t[i] == "}":
        depth -= 1
        if depth == 0:
            end = i + 1
            break
while end < len(t) and t[end] == "\n":
    end += 1
new_load = """        private RaylibAssetStore<Texture2D>.Lease LoadTexture(string textureAssetKey, in MeshAssetDescriptor textureDescriptor)
        {
            if (_vfs == null)
            {
                throw new InvalidOperationException(
                    $"Particle texture sheet asset '{textureAssetKey}' requires a virtual file system to resolve Presentation/host_assets.json sourceUris.");
            }

            if (textureDescriptor.SourceUris == null || textureDescriptor.SourceUris.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Particle texture sheet asset '{textureAssetKey}' requires raylib sourceUris from Presentation/host_assets.json.");
            }

            try
            {
                return _textureStore.Acquire(textureDescriptor.SourceUris);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Particle texture sheet asset '{textureAssetKey}' could not load any sourceUri. {ex.Message}");
            }
        }
"""
t = t[:line_start] + new_load + t[end:]

old_dispose = """        public void Dispose()
        {
            foreach (Texture2D texture in _textureCache.Values)
            {
                if (texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(texture);
                }
            }

            _textureCache.Clear();
        }"""
new_dispose = """        public void Dispose()
        {
            foreach (RaylibAssetStore<Texture2D>.Lease lease in _textureCache.Values)
            {
                lease.Dispose();
            }

            _textureCache.Clear();
            if (_ownsTextureStore)
            {
                _textureStore.Dispose();
            }
        }"""
assert old_dispose in t
t = t.replace(old_dispose, new_dispose, 1)

with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(t)
print("vfx renderer migrated")

# ---------------- MaterialLibrary ----------------
path = 'src/Client/Ludots.Raylib.Render/Rendering/RaylibMaterialLibrary.cs'
with open(path, encoding='utf-8', newline='') as f:
    t = f.read()

old_fields = """        private readonly IRenderAssetPathResolver _vfs;"""
assert old_fields in t
new_fields = """        private readonly IRenderAssetPathResolver _vfs;
        private readonly RaylibAssetStore<Texture2D> _textureStore;
        private readonly Dictionary<int, List<RaylibAssetStore<Texture2D>.Lease>> _bindingLeases = new();"""
t = t.replace(old_fields, new_fields, 1)

import re
ctor_m = re.search(r"        public RaylibMaterialLibrary\(([^)]*)\)\s*\{", t)
assert ctor_m
old_ctor_whole = t[ctor_m.start():ctor_m.end()]
new_ctor_whole = "        public RaylibMaterialLibrary(IRenderAssetPathResolver vfs, IRenderMaterialAssets materials, RaylibAssetStore<Texture2D> textureStore)\n            : this(vfs, materials)\n        {"
t = t.replace(old_ctor_whole, new_ctor_whole, 1)
# find the ctor we just renamed: convert original ctor to chaining overload
# original ctor body remains; wrap: insert store assignment into the ORIGINAL (2-arg) ctor
# locate 2-arg ctor body opening after our edit
m2 = re.search(r"public RaylibMaterialLibrary\(IRenderAssetPathResolver vfs, IRenderMaterialAssets materials\)\s*\{", t)
assert m2, "2-arg ctor not found"
insert_at = m2.end()
t = t[:insert_at] + "\n            _textureStore = textureStore;" + t[insert_at:]

old_load_map = """        private Texture2D LoadMapOrThrow(
            int materialAssetId,
            string materialName,
            string uri,
            string slotName)
        {
            if (!_vfs.TryResolveFullPath(uri, out string fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialLibrary)} cannot resolve {slotName} URI '{uri}' for materialId={materialAssetId} ({materialName}).");
            }

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialLibrary)} {slotName} file missing for materialId={materialAssetId} ({materialName}): uri='{uri}' fullPath='{fullPath}'.");
            }

            Texture2D texture = RaylibNativeResources.LoadTexture(fullPath);
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialLibrary)} LoadTexture failed for {slotName} materialId={materialAssetId} ({materialName}): uri='{uri}' fullPath='{fullPath}'.");
            }

            _ownedTextureIds.Add(texture.id);
            return texture;
        }"""
new_load_map = """        private Texture2D LoadMapOrThrow(
            int materialAssetId,
            string materialName,
            string uri,
            string slotName)
        {
            RaylibAssetStore<Texture2D>.Lease lease;
            try
            {
                lease = _textureStore.Acquire(uri);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialLibrary)} {slotName} texture load failed for materialId={materialAssetId} ({materialName}): uri='{uri}': {ex.Message}");
            }

            if (!_bindingLeases.TryGetValue(materialAssetId, out List<RaylibAssetStore<Texture2D>.Lease>? leases))
            {
                leases = new List<RaylibAssetStore<Texture2D>.Lease>();
                _bindingLeases[materialAssetId] = leases;
            }

            leases.Add(lease);
            Texture2D texture = lease.Resource;
            _ownedTextureIds.Add(texture.id);
            return texture;
        }"""
assert old_load_map in t, "LoadMapOrThrow not found"
t = t.replace(old_load_map, new_load_map, 1)

old_unload_owned = """        private void UnloadOwned(Texture2D texture)
        {
            if (texture.id != 0)
            {
                RaylibNativeResources.UnloadTexture(texture);
            }
        }"""
new_unload_owned = """        private void UnloadOwned(Texture2D texture)
        {
            // 贴图生命周期归 RaylibAssetStore：库只在 Dispose 释放租约，实际销毁延迟到存储冲刷（#1327）。
        }"""
assert old_unload_owned in t
t = t.replace(old_unload_owned, new_unload_owned, 1)

old_dispose = """            _bindingsByMaterialId.Clear();
            _ownedTextureIds.Clear();
            _disposed = true;
        }"""
new_dispose = """            _bindingsByMaterialId.Clear();
            foreach (List<RaylibAssetStore<Texture2D>.Lease> leases in _bindingLeases.Values)
            {
                foreach (RaylibAssetStore<Texture2D>.Lease lease in leases)
                {
                    lease.Dispose();
                }
            }

            _bindingLeases.Clear();
            _ownedTextureIds.Clear();
            _disposed = true;
        }"""
assert old_dispose in t
t = t.replace(old_dispose, new_dispose, 1)

with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(t)
print("material library migrated")

# ---------------- GpuSkinnedModelCache ----------------
path = 'src/Client/Ludots.Raylib.Render/Rendering/RaylibGpuSkinnedModelCache.cs'
with open(path, encoding='utf-8', newline='') as f:
    t = f.read()

t = t.replace(
"""        public readonly struct Entry
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
        private readonly Dictionary<int, Entry> _entries = new();
        private bool _disposed;

        public RaylibGpuSkinnedModelCache(IRenderAssetPathResolver? vfs)
        {
            _vfs = vfs;
        }""",
"""        public readonly struct Entry
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

        public RaylibGpuSkinnedModelCache(IRenderAssetPathResolver? vfs, RaylibAssetStore<Model>? modelStore = null)
        {
            _vfs = vfs;
            _modelStore = modelStore ?? new RaylibAssetStore<Model>(vfs, path =>
            {
                string loadablePath = RaylibModelFileLoader.PrepareNativeLoadable(path);
                Model model = RaylibNativeResources.LoadModel(loadablePath);
                if (model.meshCount <= 0)
                {
                    RaylibNativeResources.UnloadModel(model);
                    throw new InvalidOperationException($"model '{path}' loaded with meshCount=0.");
                }

                return model;
            }, RaylibNativeResources.UnloadModel);
        }""", 1)

old_getload = """            if (_vfs == null || descriptor.SourceUris == null || descriptor.SourceUris.Length == 0)
            {
                _entries[meshAssetId] = default;
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} has no VFS/sourceUris for GpuSkinnedInstance.");
            }

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
new_getload = """            if (_vfs == null || descriptor.SourceUris == null || descriptor.SourceUris.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} has no VFS/sourceUris for GpuSkinnedInstance.");
            }

            string[] loadFailures = Array.Empty<string>();
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

                // 统一经装载入口（glTF native / OBJ、FBX、DAE 先转 GLB）——OBJ 直走
                // native LoadModel 是 #1050 的 AccessViolation 路径。模型生命周期经共享存储按 URI 去重（#1327）。
                string loadablePath = RaylibModelFileLoader.PrepareNativeLoadable(fullPath);

                if (!_modelStore.TryAcquire(uri, out RaylibAssetStore<Model>.Lease? lease, out string? acquireFailure))
                {
                    Array.Resize(ref loadFailures, loadFailures.Length + 1);
                    loadFailures[^1] = $"'{uri}': {acquireFailure}";
                    continue;
                }

                Model model = lease!.Resource;

                if (model.boneCount <= 0)"""
assert old_getload in t, "GetOrLoad head not found"
t = t.replace(old_getload, new_getload, 1)

# inside the per-uri block: on bone/anim failures, dispose lease instead of UnloadModel
t = t.replace("""                if (model.boneCount <= 0)
                {
                    RaylibNativeResources.UnloadModel(model);
                    _entries[meshAssetId] = default;
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' has boneCount=0; GpuSkinnedInstance requires a skinned model.");
                }

                if (model.boneCount > MaxBones)
                {
                    RaylibNativeResources.UnloadModel(model);
                    _entries[meshAssetId] = default;
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} boneCount={model.boneCount} exceeds MAX_BONE_NUM={MaxBones}.");
                }""",
"""                if (model.boneCount <= 0)
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
                }""", 1)

t = t.replace("""                if (animations == null || animCount <= 0)
                {
                    if (animations != null)
                    {
                        Rl.UnloadModelAnimations(animations, animCount);
                    }

                    RaylibNativeResources.UnloadModel(model);
                    _entries[meshAssetId] = default;
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' loaded with animCount={animCount}; GpuSkinnedInstance forbids silent static fallback.");
                }""",
"""                if (animations == null || animCount <= 0)
                {
                    if (animations != null)
                    {
                        Rl.UnloadModelAnimations(animations, animCount);
                    }

                    lease.Dispose();
                    _entries[meshAssetId] = default;
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' loaded with animCount={animCount}; GpuSkinnedInstance forbids silent static fallback.");
                }""", 1)

t = t.replace("""                        Rl.UnloadModelAnimations(animations, animCount);
                        RaylibNativeResources.UnloadModel(model);
                        _entries[meshAssetId] = default;
                        throw new InvalidOperationException(
                            $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' animation[{i}] failed IsModelAnimationValid (modelBones={modelBones}, animBones={animBones}).");""",
"""                        Rl.UnloadModelAnimations(animations, animCount);
                        lease.Dispose();
                        _entries[meshAssetId] = default;
                        throw new InvalidOperationException(
                            $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} path='{fullPath}' animation[{i}] failed IsModelAnimationValid (modelBones={modelBones}, animBones={animBones}).");""", 1)

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

t = t.replace("""                if (entry.Animations != null && entry.AnimCount > 0)
                {
                    Rl.UnloadModelAnimations(entry.Animations, entry.AnimCount);
                }

                RaylibNativeResources.UnloadModel(entry.Model);
            }

            _entries.Clear();
        }""",
"""                if (entry.Animations != null && entry.AnimCount > 0)
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
        }""", 1)

with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(t)
print("skinned model cache migrated")
