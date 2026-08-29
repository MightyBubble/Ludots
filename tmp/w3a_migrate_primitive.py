path = 'src/Client/Ludots.Raylib.Render/Rendering/RaylibPrimitiveRenderer.cs'
with open(path, encoding='utf-8', newline='') as f:
    t = f.read()

def find_member_end(sig):
    idx = t.find(sig)
    assert idx >= 0, sig[:60]
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
    return line_start, end

old = "        private readonly Dictionary<int, CachedModel> _modelCache = new Dictionary<int, CachedModel>();"
assert old in t
t = t.replace(old, old + "\n        private readonly RaylibAssetStore<Texture2D> _textureStore;\n        private readonly RaylibAssetStore<Model> _modelStore;", 1)

# CachedModel / CachedTexture: replace via member-boundary spans
ls, e = find_member_end("        private struct CachedModel")
new_cm = """        private struct CachedModel
        {
            public RaylibAssetStore<Model>.Lease? Lease;
            public Mesh[] Meshes;
            public Vector3 LocalMin;
            public Vector3 LocalMax;
            public bool Loaded;

            public readonly Model Model => Lease!.Resource;
        }
"""
t = t[:ls] + new_cm + t[e:]

ls, e = find_member_end("        private struct CachedTexture")
new_ct = """        private struct CachedTexture
        {
            public RaylibAssetStore<Texture2D>.Lease? Lease;
            public bool Loaded;
            public float AspectRatio;

            public readonly Texture2D Texture => Lease!.Resource;
        }
"""
t = t[:ls] + new_ct + t[e:]

ls, e = find_member_end("        private bool TryGetOrLoadModel(int meshAssetId, in MeshAssetDescriptor desc, out CachedModel cached)")
new_model = """        private bool TryGetOrLoadModel(int meshAssetId, in MeshAssetDescriptor desc, out CachedModel cached)
        {
            if (_modelCache.TryGetValue(meshAssetId, out cached))
                return cached.Loaded;

            cached = new CachedModel { Loaded = false };

            if (_vfs == null || desc.SourceUris == null || desc.SourceUris.Length == 0)
            {
                _modelCache[meshAssetId] = cached;
                return false;
            }

            RaylibAssetStore<Model>.Lease lease = _modelStore.Acquire(desc.SourceUris);
            try
            {
                Mesh[] modelMeshes = CopyModelMeshes(lease.Resource);
                ComputeModelLocalAabbMeters(modelMeshes, out Vector3 localMin, out Vector3 localMax);
                cached = new CachedModel
                {
                    Lease = lease,
                    Meshes = modelMeshes,
                    LocalMin = localMin,
                    LocalMax = localMax,
                    Loaded = true,
                };
                _modelCache[meshAssetId] = cached;
                return true;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
"""
t = t[:ls] + new_model + t[e:]

ls, e = find_member_end("        private bool TryGetOrLoadTexture(int meshAssetId, in MeshAssetDescriptor desc, out CachedTexture cached)")
new_tex = """        private bool TryGetOrLoadTexture(int meshAssetId, in MeshAssetDescriptor desc, out CachedTexture cached)
        {
            if (_textureCache.TryGetValue(meshAssetId, out cached))
                return cached.Loaded;

            cached = new CachedTexture { Loaded = false, AspectRatio = 1f };

            if (_vfs == null || desc.SourceUris == null || desc.SourceUris.Length == 0)
            {
                RenderDiagnostics.Detail("texture", meshAssetId, $"texture-load skipped; vfsMissing={_vfs == null}; uriCount={desc.SourceUris?.Length ?? 0}");
                _textureCache[meshAssetId] = cached;
                return false;
            }

            RaylibAssetStore<Texture2D>.Lease lease = _textureStore.Acquire(desc.SourceUris);
            Texture2D texture = lease.Resource;
            cached = new CachedTexture
            {
                Lease = lease,
                Loaded = true,
                AspectRatio = texture.height > 0 ? (float)texture.width / texture.height : 1f,
            };
            _textureCache[meshAssetId] = cached;
            return true;
        }
"""
t = t[:ls] + new_tex + t[e:]

old_disp = """            foreach (var kvp in _modelCache)
            {
                if (!kvp.Value.Loaded)
                {
                    continue;
                }

                Model model = kvp.Value.Model;
                _materialLibrary?.DetachOwnedMaps(model);
                RaylibNativeResources.UnloadModel(model);
            }
            _modelCache.Clear();"""
new_disp = """            foreach (var kvp in _modelCache)
            {
                if (!kvp.Value.Loaded)
                {
                    continue;
                }

                _materialLibrary?.DetachOwnedMaps(kvp.Value.Model);
                kvp.Value.Lease?.Dispose();
            }
            _modelCache.Clear();
            _modelStore.Dispose();"""
assert old_disp in t, "model dispose block not found"
t = t.replace(old_disp, new_disp, 1)

old_disp2 = """            foreach (var kvp in _textureCache)
            {
                if (kvp.Value.Loaded)
                    RaylibNativeResources.UnloadTexture(kvp.Value.Texture);
            }
            _textureCache.Clear();"""
new_disp2 = """            foreach (var kvp in _textureCache)
            {
                if (kvp.Value.Loaded)
                    kvp.Value.Lease?.Dispose();
            }
            _textureCache.Clear();
            _textureStore.Dispose();"""
assert old_disp2 in t, "texture dispose block not found"
t = t.replace(old_disp2, new_disp2, 1)

with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(t)
print("primitive renderer migrated")
