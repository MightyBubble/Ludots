path = 'src/Client/Ludots.Raylib.Render/Rendering/RaylibPrimitiveRenderer.cs'
with open(path, encoding='utf-8', newline='') as f:
    t = f.read()

def member_span(sig):
    idx = t.find(sig)
    assert idx >= 0, sig[:70]
    ls = t.rfind("\n", 0, idx) + 1
    ob = t.find("{", idx)
    depth = 0
    end = None
    for i in range(ob, len(t)):
        if t[i] == "{":
            depth += 1
        elif t[i] == "}":
            depth -= 1
            if depth == 0:
                end = i + 1
                break
    while end < len(t) and t[end] == "\n":
        end += 1
    return ls, end

# 1) ctor: env knob + async delegates for both stores
old = """            _textureStore = new RaylibAssetStore<Texture2D>(vfs, LoadTextureResource, RaylibNativeResources.UnloadTexture);
            _modelStore = new RaylibAssetStore<Model>(vfs, LoadModelResource, RaylibNativeResources.UnloadModel);"""
new = """            bool syncAssetLoad = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_SYNC_ASSET_LOAD") == "1";
            if (syncAssetLoad)
            {
                _textureStore = new RaylibAssetStore<Texture2D>(vfs, LoadTextureResource, RaylibNativeResources.UnloadTexture);
                _modelStore = new RaylibAssetStore<Model>(vfs, LoadModelResource, RaylibNativeResources.UnloadModel);
            }
            else
            {
                _textureStore = new RaylibAssetStore<Texture2D>(
                    vfs,
                    LoadTextureResource,
                    RaylibNativeResources.UnloadTexture,
                    cpuPrepare: fullPath =>
                    {
                        Image image = Rl.LoadImage(fullPath);
                        if (image.data == IntPtr.Zero || image.width <= 0 || image.height <= 0)
                        {
                            throw new InvalidOperationException($"raylib rejected image '{fullPath}' (size={image.width}x{image.height}).");
                        }

                        return image;
                    },
                    uploader: payload =>
                    {
                        var image = (Image)payload!;
                        Texture2D texture = RaylibNativeResources.LoadTextureFromImage(image);
                        RaylibNativeResources.UnloadImage(image);
                        ValidateTexture(texture, fullPath: string.Empty);
                        return texture;
                    });
                _modelStore = new RaylibAssetStore<Model>(
                    vfs,
                    LoadModelResource,
                    RaylibNativeResources.UnloadModel,
                    cpuPrepare: fullPath => RaylibModelFileLoader.PrepareNativeLoadable(fullPath),
                    uploader: payload => LoadModelResource((string)payload!));
            }"""
assert old in t, "ctor store block not found"
t = t.replace(old, new, 1)

# 2) factor texture validation for reuse by uploader
old = """        private static Texture2D LoadTextureResource(string fullPath)
        {
            Texture2D texture = RaylibNativeResources.LoadTexture(fullPath);
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"raylib rejected texture '{fullPath}' (textureId={texture.id}, size={texture.width}x{texture.height}).");
            }

            return texture;
        }"""
new = """        private static Texture2D LoadTextureResource(string fullPath)
        {
            Texture2D texture = RaylibNativeResources.LoadTexture(fullPath);
            return ValidateTexture(texture, fullPath);
        }

        private static Texture2D ValidateTexture(Texture2D texture, string fullPath)
        {
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"raylib rejected texture '{fullPath}' (textureId={texture.id}, size={texture.width}x{texture.height}).");
            }

            return texture;
        }"""
assert old in t, "LoadTextureResource not found"
t = t.replace(old, new, 1)

# 3) TryGetOrLoadModel → TryAcquireOrBegin chain
ls, e = member_span("        private bool TryGetOrLoadModel(int meshAssetId, in MeshAssetDescriptor desc, out CachedModel cached)")
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

            List<string>? failures = null;
            foreach (string uri in desc.SourceUris)
            {
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                RaylibAssetAcquireOutcome outcome = _modelStore.TryAcquireOrBegin(uri, out RaylibAssetStore<Model>.Lease? lease, out string? status);
                if (outcome == RaylibAssetAcquireOutcome.InFlight)
                {
                    // 两阶段装载进行中：本帧不绘制、不记负缓存，下一帧重问（#1328）。
                    return false;
                }

                if (outcome == RaylibAssetAcquireOutcome.Failed)
                {
                    failures ??= new List<string>();
                    failures.Add($"'{uri}': {status}");
                    continue;
                }

                try
                {
                    Mesh[] modelMeshes = CopyModelMeshes(lease!.Resource);
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
                    lease!.Dispose();
                    throw;
                }
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibPrimitiveRenderer)} meshAssetId={meshAssetId} could not load any sourceUri. Attempts: [{string.Join("; ", failures ?? new List<string>())}]");
        }
"""
t = t[:ls] + new_model + t[e:]

# 4) TryGetOrLoadTexture → TryAcquireOrBegin chain
ls, e = member_span("        private bool TryGetOrLoadTexture(int meshAssetId, in MeshAssetDescriptor desc, out CachedTexture cached)")
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

            List<string>? failures = null;
            foreach (string uri in desc.SourceUris)
            {
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                RaylibAssetAcquireOutcome outcome = _textureStore.TryAcquireOrBegin(uri, out RaylibAssetStore<Texture2D>.Lease? lease, out string? status);
                if (outcome == RaylibAssetAcquireOutcome.InFlight)
                {
                    return false;
                }

                if (outcome == RaylibAssetAcquireOutcome.Failed)
                {
                    failures ??= new List<string>();
                    failures.Add($"'{uri}': {status}");
                    continue;
                }

                Texture2D texture = lease!.Resource;
                cached = new CachedTexture
                {
                    Lease = lease,
                    Loaded = true,
                    AspectRatio = texture.height > 0 ? (float)texture.width / texture.height : 1f,
                };
                _textureCache[meshAssetId] = cached;
                return true;
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibPrimitiveRenderer)} texture meshAssetId={meshAssetId} could not load any sourceUri. Attempts: [{string.Join("; ", failures ?? new List<string>())}]");
        }
"""
t = t[:ls] + new_tex + t[e:]

# 5) pumps at Draw / DrawShadow entry (frame phase for gallery + host alike)
old_draw = """            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            _frameViewPos = camera.position;"""
new_draw = """            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            _textureStore.PumpUploads();
            _modelStore.PumpUploads();
            _frameViewPos = camera.position;"""
assert old_draw in t, "Draw prologue not found"
t = t.replace(old_draw, new_draw, 1)

old_shadow = """        public void DrawShadow(
            IPrimitiveDrawSnapshot draw,
            RaylibDirectionalShadowMap shadow,
            IRenderMeshAssets meshes,
            Camera3D camera,
            float scaleMul = 1f)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (shadow == null) throw new ArgumentNullException(nameof(shadow));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));"""
new_shadow = """        public void DrawShadow(
            IPrimitiveDrawSnapshot draw,
            RaylibDirectionalShadowMap shadow,
            IRenderMeshAssets meshes,
            Camera3D camera,
            float scaleMul = 1f)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (shadow == null) throw new ArgumentNullException(nameof(shadow));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            _textureStore.PumpUploads();
            _modelStore.PumpUploads();"""
assert old_shadow in t, "DrawShadow prologue not found"
t = t.replace(old_shadow, new_shadow, 1)

with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(t)
print("primitive renderer async migrated")
