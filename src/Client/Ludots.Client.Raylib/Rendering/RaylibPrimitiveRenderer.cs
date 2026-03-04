using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Modding;
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
        private const int MaxPrefabDepth = 6;

        private readonly IVirtualFileSystem _vfs;
        private readonly RaylibPrimitiveRenderMode _mode;

        private bool _initialized;
        private Mesh _cubeMesh;
        private Mesh _sphereMesh;
        private Shader _shader;
        private Material _material;
        private int _locColDiffuse;
        private int _locTint;

        private readonly List<Batch> _cubeBatches = new List<Batch>(16);
        private readonly List<Batch> _sphereBatches = new List<Batch>(16);
        private readonly Dictionary<int, CachedModel> _modelCache = new Dictionary<int, CachedModel>(64);

        public int LastInstancedInstances { get; private set; }
        public int LastInstancedBatches { get; private set; }
        public int LastModelDrawCalls { get; private set; }
        public int LastModelLoadFailures { get; private set; }
        public int LastModelFallbackDraws { get; private set; }
        public int LastMissingModelAssetId { get; private set; } = -1;
        public int CachedModelCount => _modelCache.Count;

        public RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode mode = RaylibPrimitiveRenderMode.Immediate)
            : this(vfs: null, mode)
        {
        }

        public RaylibPrimitiveRenderer(IVirtualFileSystem vfs, RaylibPrimitiveRenderMode mode = RaylibPrimitiveRenderMode.Immediate)
        {
            _vfs = vfs;
            _mode = mode;
        }

        public void Draw(PrimitiveDrawBuffer draw, MeshAssetRegistry meshes, float globalScaleMultiplier = 1f)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            LastInstancedInstances = 0;
            LastInstancedBatches = 0;
            LastModelDrawCalls = 0;
            LastModelLoadFailures = 0;
            LastModelFallbackDraws = 0;
            LastMissingModelAssetId = -1;
            float scaleMul = globalScaleMultiplier <= 0f ? 1f : globalScaleMultiplier;

            var span = draw.GetSpan();
            if (_mode == RaylibPrimitiveRenderMode.Immediate)
            {
                for (int i = 0; i < span.Length; i++)
                {
                    ref readonly var item = ref span[i];
                    Vector3 scaled = item.Scale * scaleMul;
                    DrawAssetRecursive(
                        item.MeshAssetId,
                        item.Position,
                        scaled,
                        item.Color,
                        meshes,
                        depth: 0,
                        allowInstancing: false);
                }
                return;
            }

            EnsureInitialized();

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                Vector3 scaled = item.Scale * scaleMul;
                DrawAssetRecursive(
                    item.MeshAssetId,
                    item.Position,
                    scaled,
                    item.Color,
                    meshes,
                    depth: 0,
                    allowInstancing: true);
            }

            FlushInstancedBatches();
        }

        private void DrawAssetRecursive(
            int meshAssetId,
            in Vector3 position,
            in Vector3 scale,
            in Vector4 color,
            MeshAssetRegistry meshes,
            int depth,
            bool allowInstancing)
        {
            if (depth > MaxPrefabDepth) return;
            if (!meshes.TryGetDescriptor(meshAssetId, out var descriptor)) return;

            switch (descriptor.Type)
            {
                case MeshAssetType.Primitive:
                    if (descriptor.PrimitiveKind == PrimitiveMeshKind.None) return;
                    DrawPrimitive(descriptor.PrimitiveKind, position, scale, color, allowInstancing);
                    return;

                case MeshAssetType.Model:
                    DrawModelAsset(meshAssetId, in descriptor, in position, in scale, in color);
                    return;

                case MeshAssetType.Prefab:
                    DrawPrefabAsset(in descriptor, in position, in scale, in color, meshes, depth, allowInstancing);
                    return;
            }
        }

        private void DrawPrefabAsset(
            in MeshAssetDescriptor descriptor,
            in Vector3 parentPosition,
            in Vector3 parentScale,
            in Vector4 parentColor,
            MeshAssetRegistry meshes,
            int depth,
            bool allowInstancing)
        {
            var parts = descriptor.PrefabParts;

            for (int i = 0; i < parts.Length; i++)
            {
                ref readonly var part = ref parts[i];
                var childPosition = new Vector3(
                    parentPosition.X + part.LocalPosition.X * parentScale.X,
                    parentPosition.Y + part.LocalPosition.Y * parentScale.Y,
                    parentPosition.Z + part.LocalPosition.Z * parentScale.Z);

                var childScale = new Vector3(
                    parentScale.X * part.LocalScale.X,
                    parentScale.Y * part.LocalScale.Y,
                    parentScale.Z * part.LocalScale.Z);

                var childColor = MultiplyColor(parentColor, part.ColorTint);
                DrawAssetRecursive(
                    part.MeshAssetId,
                    in childPosition,
                    in childScale,
                    in childColor,
                    meshes,
                    depth + 1,
                    allowInstancing);
            }
        }

        private void DrawPrimitive(
            PrimitiveMeshKind kind,
            in Vector3 position,
            in Vector3 scale,
            in Vector4 color,
            bool allowInstancing)
        {
            if (_mode == RaylibPrimitiveRenderMode.Instanced && allowInstancing)
            {
                QueueInstancedPrimitive(kind, in position, in scale, in color);
                return;
            }

            DrawPrimitiveImmediate(kind, in position, in scale, in color);
        }

        private void QueueInstancedPrimitive(
            PrimitiveMeshKind kind,
            in Vector3 position,
            in Vector3 scale,
            in Vector4 color)
        {
            uint packed = PackRgba(color);
            var matrix = RaylibMatrix.FromScaleTranslation(position.X, position.Y, position.Z, scale.X, scale.Y, scale.Z);

            if (kind == PrimitiveMeshKind.Cube)
            {
                AddInstance(_cubeBatches, packed, matrix);
                return;
            }

            if (kind == PrimitiveMeshKind.Sphere)
            {
                AddInstance(_sphereBatches, packed, matrix);
            }
        }

        private static void DrawPrimitiveImmediate(
            PrimitiveMeshKind kind,
            in Vector3 position,
            in Vector3 scale,
            in Vector4 color)
        {
            var rayColor = ToRaylibColor(color);
            if (kind == PrimitiveMeshKind.Cube)
            {
                Rl.DrawCube(position, scale.X, scale.Y, scale.Z, rayColor);
            }
            else if (kind == PrimitiveMeshKind.Sphere)
            {
                float r = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)) * 0.5f;
                Rl.DrawSphere(position, r, rayColor);
            }
        }

        private void DrawModelAsset(
            int meshAssetId,
            in MeshAssetDescriptor descriptor,
            in Vector3 position,
            in Vector3 scale,
            in Vector4 color)
        {
            if (!TryGetOrLoadModel(meshAssetId, in descriptor, out var model))
            {
                LastModelLoadFailures++;
                LastModelFallbackDraws++;
                LastMissingModelAssetId = meshAssetId;
                DrawMissingModelMarker(in position, in scale);
                return;
            }

            var tint = ToRaylibColor(color);
            Rl.DrawModelEx(model, position, Vector3.UnitY, 0f, scale, tint);
            LastModelDrawCalls++;
        }

        private bool TryGetOrLoadModel(int meshAssetId, in MeshAssetDescriptor descriptor, out Model model)
        {
            if (_modelCache.TryGetValue(meshAssetId, out var cached))
            {
                model = cached.Model;
                return true;
            }

            if (!TryResolveSourcePath(descriptor.SourceUris, out var fullPath))
            {
                model = default;
                return false;
            }

            model = Rl.LoadModel(fullPath);
            if (model.meshCount <= 0)
            {
                model = default;
                return false;
            }

            _modelCache[meshAssetId] = new CachedModel
            {
                Model = model,
                SourcePath = fullPath,
            };
            return true;
        }

        private static void DrawMissingModelMarker(in Vector3 position, in Vector3 scale)
        {
            float sx = MathF.Max(MathF.Abs(scale.X), 0.6f);
            float sy = MathF.Max(MathF.Abs(scale.Y), 0.6f);
            float sz = MathF.Max(MathF.Abs(scale.Z), 0.6f);
            var c = new Color(255, 0, 255, 255);
            Rl.DrawCube(position, sx, sy, sz, new Color(140, 0, 140, 180));

            float halfX = sx * 0.5f;
            float halfY = sy * 0.5f;
            float halfZ = sz * 0.5f;

            Rl.DrawLine3D(
                new Vector3(position.X - halfX, position.Y - halfY, position.Z - halfZ),
                new Vector3(position.X + halfX, position.Y + halfY, position.Z + halfZ),
                c);
            Rl.DrawLine3D(
                new Vector3(position.X - halfX, position.Y + halfY, position.Z + halfZ),
                new Vector3(position.X + halfX, position.Y - halfY, position.Z - halfZ),
                c);
        }

        private bool TryResolveSourcePath(string[] sourceUris, out string fullPath)
        {
            fullPath = string.Empty;
            if (sourceUris == null || sourceUris.Length == 0) return false;

            for (int i = 0; i < sourceUris.Length; i++)
            {
                if (TryResolveSingleSourcePath(sourceUris[i], out fullPath))
                    return true;
            }

            fullPath = string.Empty;
            return false;
        }

        private bool TryResolveSingleSourcePath(string sourceUri, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(sourceUri)) return false;

            if (Path.IsPathRooted(sourceUri))
            {
                if (File.Exists(sourceUri))
                {
                    fullPath = sourceUri;
                    return true;
                }
                return false;
            }

            if (sourceUri.IndexOf(':') >= 0 && _vfs != null)
            {
                if (_vfs.TryResolveFullPath(sourceUri, out var resolved) && File.Exists(resolved))
                {
                    fullPath = resolved;
                    return true;
                }
            }

            string fromBaseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, sourceUri));
            if (File.Exists(fromBaseDir))
            {
                fullPath = fromBaseDir;
                return true;
            }

            string fromCwd = Path.GetFullPath(sourceUri);
            if (File.Exists(fromCwd))
            {
                fullPath = fromCwd;
                return true;
            }

            return false;
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

        private static Color ToRaylibColor(in Vector4 c)
        {
            byte r = Clamp01ToByte(c.X);
            byte g = Clamp01ToByte(c.Y);
            byte b = Clamp01ToByte(c.Z);
            byte a = Clamp01ToByte(c.W);
            return new Color(r, g, b, a);
        }

        private static byte Clamp01ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f);
        }

        private static Vector4 MultiplyColor(in Vector4 a, in Vector4 b)
        {
            return new Vector4(
                a.X * b.X,
                a.Y * b.Y,
                a.Z * b.Z,
                a.W * b.W);
        }

        public void Dispose()
        {
            foreach (var kv in _modelCache)
            {
                var model = kv.Value.Model;
                if (model.meshCount > 0)
                {
                    Rl.UnloadModel(model);
                }
            }
            _modelCache.Clear();

            if (!_initialized) return;

            if (_cubeMesh.vertexCount > 0) Rl.UnloadMesh(_cubeMesh);
            if (_sphereMesh.vertexCount > 0) Rl.UnloadMesh(_sphereMesh);
            Rl.UnloadMaterial(_material);
            Rl.UnloadShader(_shader);
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

        private struct CachedModel
        {
            public Model Model;
            public string SourcePath;
        }
    }
}
