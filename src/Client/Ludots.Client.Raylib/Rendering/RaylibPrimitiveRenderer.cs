using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.AdapterSync;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public enum RaylibPrimitiveRenderMode : byte
    {
        Immediate = 0,
        Instanced = 1
    }

    public sealed unsafe class RaylibPrimitiveRenderer : IDisposable
    {
        private readonly RaylibPrimitiveRenderMode _mode;
        private readonly IVirtualFileSystem? _vfs;
        private const int MaxPrefabDepth = 6;

        private bool _initialized;
        private Mesh _cubeMesh;
        private Mesh _sphereMesh;
        private Shader _shader;
        private Material _material;
        private int _locColDiffuse;
        private int _locTint;

        private readonly List<Batch> _cubeBatches = new List<Batch>(16);
        private readonly List<Batch> _sphereBatches = new List<Batch>(16);
        private readonly StaticMeshAdapterSyncPlanner _persistentStaticLaneSync = new StaticMeshAdapterSyncPlanner();

        private readonly Dictionary<int, CachedModel> _modelCache = new Dictionary<int, CachedModel>();

        public int LastInstancedInstances { get; private set; }
        public int LastInstancedBatches { get; private set; }
        public int LastPersistentCreates { get; private set; }
        public int LastPersistentUpdates { get; private set; }
        public int LastPersistentRemoves { get; private set; }

        public RaylibPrimitiveRenderer(
            RaylibPrimitiveRenderMode mode = RaylibPrimitiveRenderMode.Immediate,
            IVirtualFileSystem? vfs = null)
        {
            _mode = mode;
            _vfs = vfs;
        }

        public void Draw(PrimitiveDrawBuffer draw, MeshAssetRegistry meshes, float scaleMul = 1f)
        {
            Draw(draw, snapshot: null, meshes, scaleMul);
        }

        public void Draw(PrimitiveDrawBuffer draw, PrimitiveDrawBuffer? snapshot, MeshAssetRegistry meshes, float scaleMul = 1f)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            LastInstancedInstances = 0;
            LastInstancedBatches = 0;
            LastPersistentCreates = 0;
            LastPersistentUpdates = 0;
            LastPersistentRemoves = 0;

            var span = draw.GetSpan();
            bool usePersistentStaticLanes = snapshot != null;
            if (usePersistentStaticLanes)
            {
                _persistentStaticLaneSync.Sync(snapshot);
                LastPersistentCreates = _persistentStaticLaneSync.LastCreateCount;
                LastPersistentUpdates = _persistentStaticLaneSync.LastUpdateCount;
                LastPersistentRemoves = _persistentStaticLaneSync.LastRemoveCount;
                DrawPersistentStaticLanes(meshes, scaleMul);
            }

            DrawImmediateWithDescriptors(span, meshes, scaleMul, usePersistentStaticLanes);
        }

        private void DrawPersistentStaticLanes(MeshAssetRegistry meshes, float scaleMul)
        {
            foreach (var pair in _persistentStaticLaneSync.ActiveBindings)
            {
                var binding = pair.Value;
                var item = binding.Item;
                if (!binding.IsVisible)
                {
                    continue;
                }

                DrawAssetRecursive(
                    item.MeshAssetId,
                    item.Position,
                    item.Scale * scaleMul,
                    item.Color,
                    meshes,
                    0);
            }
        }

        private void DrawImmediateWithDescriptors(ReadOnlySpan<PrimitiveDrawItem> span, MeshAssetRegistry meshes, float scaleMul, bool persistentStaticLanesActive)
        {
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (persistentStaticLanesActive &&
                    item.StableId > 0 &&
                    StaticMeshLaneKey.Supports(item.RenderPath))
                {
                    continue;
                }

                DrawAssetRecursive(
                    item.MeshAssetId, item.Position,
                    item.Scale * scaleMul, item.Color,
                    meshes, 0);
            }
        }

        private void DrawAssetRecursive(int meshAssetId, Vector3 position, Vector3 scale, Vector4 color, MeshAssetRegistry meshes, int depth)
        {
            if (depth > MaxPrefabDepth) return;
            if (!meshes.TryGetDescriptor(meshAssetId, out var desc)) return;

            switch (desc.Type)
            {
                case MeshAssetType.Primitive:
                    DrawPrimitive(desc.PrimitiveKind, position, scale, color);
                    break;

                case MeshAssetType.Model:
                    DrawModel(meshAssetId, desc, position, scale, color);
                    break;

                case MeshAssetType.Prefab:
                    if (desc.PrefabParts != null)
                    {
                        for (int p = 0; p < desc.PrefabParts.Length; p++)
                        {
                            ref var part = ref desc.PrefabParts[p];
                            var childPos = position + part.LocalPosition * scale;
                            var childScale = scale * part.LocalScale;
                            var childColor = new Vector4(
                                color.X * part.ColorTint.X,
                                color.Y * part.ColorTint.Y,
                                color.Z * part.ColorTint.Z,
                                color.W * part.ColorTint.W);
                            DrawAssetRecursive(part.MeshAssetId, childPos, childScale, childColor, meshes, depth + 1);
                        }
                    }
                    break;
            }
        }

        private void DrawPrimitive(PrimitiveMeshKind kind, Vector3 position, Vector3 scale, Vector4 color)
        {
            var c = ToRaylibColor(color);
            if (kind == PrimitiveMeshKind.Cube)
            {
                Rl.DrawCube(position, scale.X, scale.Y, scale.Z, c);
            }
            else if (kind == PrimitiveMeshKind.Sphere)
            {
                float r = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)) * 0.5f;
                Rl.DrawSphere(position, r, c);
            }
        }

        private void DrawModel(int meshAssetId, in MeshAssetDescriptor desc, Vector3 position, Vector3 scale, Vector4 color)
        {
            if (!TryGetOrLoadModel(meshAssetId, desc, out var cached))
            {
                DrawMissingModelMarker(position, scale);
                return;
            }

            var tint = ToRaylibColor(color);
            var model = cached.Model;
            Rl.DrawModelEx(model, position, Vector3.UnitY, 0f, scale, tint);
        }

        private bool TryGetOrLoadModel(int meshAssetId, in MeshAssetDescriptor desc, out CachedModel cached)
        {
            if (_modelCache.TryGetValue(meshAssetId, out cached))
                return cached.Loaded;

            cached = new CachedModel { Loaded = false };

            if (_vfs == null || desc.SourceUris == null || desc.SourceUris.Length == 0)
            {
                _modelCache[meshAssetId] = cached;
                return false;
            }

            for (int u = 0; u < desc.SourceUris.Length; u++)
            {
                string uri = desc.SourceUris[u];
                if (string.IsNullOrWhiteSpace(uri)) continue;

                if (!_vfs.TryResolveFullPath(uri, out string fullPath)) continue;
                if (!File.Exists(fullPath)) continue;

                var model = Rl.LoadModel(fullPath);
                if (model.meshCount > 0)
                {
                    cached = new CachedModel { Model = model, Loaded = true };
                    _modelCache[meshAssetId] = cached;
                    return true;
                }

                Rl.UnloadModel(model);
            }

            _modelCache[meshAssetId] = cached;
            return false;
        }

        private static void DrawMissingModelMarker(Vector3 position, Vector3 scale)
        {
            float s = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)) * 0.3f;
            if (s < 0.05f) s = 0.3f;
            Rl.DrawCube(position, s, s, s, new Color(255, 0, 255, 255));
        }

        // ── Instanced rendering (unchanged from original) ──

        public void DrawInstanced(PrimitiveDrawBuffer draw, MeshAssetRegistry meshes)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            LastInstancedInstances = 0;
            LastInstancedBatches = 0;

            EnsureInitialized();

            var span = draw.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (!meshes.TryGetPrimitiveKind(item.MeshAssetId, out var kind)) continue;

                if (kind == PrimitiveMeshKind.Cube)
                {
                    uint key = PackRgba(item.Color);
                    var matrix = RaylibMatrix.FromScaleTranslation(item.Position.X, item.Position.Y, item.Position.Z, item.Scale.X, item.Scale.Y, item.Scale.Z);
                    AddInstance(_cubeBatches, key, matrix);
                    continue;
                }

                if (kind == PrimitiveMeshKind.Sphere)
                {
                    uint key = PackRgba(item.Color);
                    var matrix = RaylibMatrix.FromScaleTranslation(item.Position.X, item.Position.Y, item.Position.Z, item.Scale.X, item.Scale.Y, item.Scale.Z);
                    AddInstance(_sphereBatches, key, matrix);
                }
            }

            FlushInstancedBatches();
        }

        private void AddInstance(List<Batch> batches, uint colorKey, in RaylibMatrix matrix)
        {
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                if (b.ColorKey != colorKey) continue;

                b.Add(matrix);
                batches[i] = b;
                return;
            }

            var nb = new Batch(colorKey);
            nb.Add(matrix);
            batches.Add(nb);
        }

        private void FlushInstancedBatches()
        {
            int totalInstances = 0;
            int batches = 0;

            FlushMeshBatches(_cubeBatches, ref totalInstances, ref batches, ref _cubeMesh);
            FlushMeshBatches(_sphereBatches, ref totalInstances, ref batches, ref _sphereMesh);

            LastInstancedInstances = totalInstances;
            LastInstancedBatches = batches;
        }

        private void FlushMeshBatches(List<Batch> batches, ref int totalInstances, ref int batchCount, ref Mesh mesh)
        {
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                if (b.Count == 0) continue;

                SetTintUniform(b.ColorKey);

                fixed (RaylibMatrix* p = b.Transforms)
                {
                    Rl.DrawMeshInstanced(mesh, _material, p, b.Count);
                }

                totalInstances += b.Count;
                batchCount++;

                b.Count = 0;
                batches[i] = b;
            }
        }

        private void SetTintUniform(uint colorKey)
        {
            if (_locTint < 0) return;

            float r = (colorKey & 0xFF) / 255f;
            float g = ((colorKey >> 8) & 0xFF) / 255f;
            float b = ((colorKey >> 16) & 0xFF) / 255f;
            float a = ((colorKey >> 24) & 0xFF) / 255f;
            var cd = new Vector4(r, g, b, a);
            Rl.SetShaderValue(_shader, _locTint, &cd, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            _cubeMesh = Rl.GenMeshCube(1f, 1f, 1f);
            if (_cubeMesh.colors == null)
            {
                int bytes = _cubeMesh.vertexCount * 4;
                _cubeMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _cubeMesh.colors[i] = 255;
            }
            Rl.UploadMesh(ref _cubeMesh, false);

            _sphereMesh = Rl.GenMeshSphere(0.5f, 8, 8);
            if (_sphereMesh.colors == null)
            {
                int bytes = _sphereMesh.vertexCount * 4;
                _sphereMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _sphereMesh.colors[i] = 255;
            }
            Rl.UploadMesh(ref _sphereMesh, false);

            string baseDir = AppContext.BaseDirectory;
            _shader = Rl.LoadShader(Path.Combine(baseDir, "instancing.vs"), Path.Combine(baseDir, "instancing.fs"));
            if (_shader.id == 0) throw new InvalidOperationException("Failed to load instancing shader (shader.id == 0).");

            _material = Rl.LoadMaterialDefault();
            _material.shader = _shader;

            _locColDiffuse = Rl.GetShaderLocation(_shader, "colDiffuse");
            _locTint = Rl.GetShaderLocation(_shader, "tint");
            int locMvp = Rl.GetShaderLocation(_shader, "mvp");
            int locInstance = Rl.GetShaderLocationAttrib(_shader, "instanceTransform");

            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locInstance;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = _locColDiffuse;

            if (locMvp < 0) throw new InvalidOperationException("Shader uniform 'mvp' not found.");
            if (locInstance < 0) throw new InvalidOperationException("Shader attrib 'instanceTransform' not found.");
            if (_locTint < 0) throw new InvalidOperationException("Shader uniform 'tint' not found.");

            _initialized = true;
        }

        private static uint PackRgba(in Vector4 c)
        {
            uint r = Clamp01ToByte(c.X);
            uint g = Clamp01ToByte(c.Y);
            uint b = Clamp01ToByte(c.Z);
            uint a = Clamp01ToByte(c.W);
            return r | (g << 8) | (b << 16) | (a << 24);
        }

        private static Color ToRaylibColor(in Vector4 c) => RaylibColorUtil.ToRaylibColor(in c);

        private static byte Clamp01ToByte(float v) => RaylibColorUtil.Clamp01ToByte(v);

        public void Dispose()
        {
            foreach (var kvp in _modelCache)
            {
                if (kvp.Value.Loaded)
                    Rl.UnloadModel(kvp.Value.Model);
            }
            _modelCache.Clear();

            if (!_initialized) return;

            if (_cubeMesh.vertexCount > 0) Rl.UnloadMesh(_cubeMesh);
            if (_sphereMesh.vertexCount > 0) Rl.UnloadMesh(_sphereMesh);
            Rl.UnloadMaterial(_material);
            Rl.UnloadShader(_shader);
        }

        private struct CachedModel
        {
            public Model Model;
            public bool Loaded;
        }

        private struct Batch
        {
            public readonly uint ColorKey;
            public RaylibMatrix[] Transforms;
            public int Count;

            public Batch(uint colorKey, int initialCapacity = 256)
            {
                ColorKey = colorKey;
                Transforms = new RaylibMatrix[Math.Max(4, initialCapacity)];
                Count = 0;
            }

            public void Add(in RaylibMatrix matrix)
            {
                if (Count >= Transforms.Length)
                {
                    Array.Resize(ref Transforms, Transforms.Length * 2);
                }
                Transforms[Count++] = matrix;
            }
        }
    }
}
