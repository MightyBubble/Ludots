using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Terrain;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed unsafe class RaylibVisualHeightmapRenderer : IDisposable
    {
        private readonly Dictionary<long, ChunkGpu> _chunks = new(1024);
        private readonly List<long> _evictKeys = new(256);

        private Shader _terrainShader;
        private Material _terrainMaterial;
        private RaylibFrameLightingLocations _terrainLightingLocs;
        private RaylibFrameLighting? _frameLighting;
        private bool _initialized;
        private int _frameIndex;

        public int DrawnChunkCountLastFrame { get; private set; }

        public int BuiltChunkCountLastFrame { get; private set; }

        public int MissingChunkCountLastFrame { get; private set; }

        public int TerrainVertexCountLastFrame { get; private set; }

        public double ChunkBuildMsLastFrame { get; private set; }

        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadiusCm { get; set; } = 120_000f;

        private float? _absoluteColorSeaLevelCm;
        private float _absoluteColorPeakSpanCm = 3600f;

        /// <summary>
        /// When set, terrain vertex colors use absolute elevation relative to this sea level (cm)
        /// instead of per-chunk min/max remapping. Required for readable island biomes across chunks.
        /// </summary>
        public float? AbsoluteColorSeaLevelCm
        {
            get => _absoluteColorSeaLevelCm;
            set
            {
                if (_absoluteColorSeaLevelCm == value)
                {
                    return;
                }

                _absoluteColorSeaLevelCm = value;
                ClearChunkGpuCache();
            }
        }

        /// <summary>Peak elevation span above sea level used when <see cref="AbsoluteColorSeaLevelCm"/> is set.</summary>
        public float AbsoluteColorPeakSpanCm
        {
            get => _absoluteColorPeakSpanCm;
            set
            {
                float clamped = MathF.Max(1f, value);
                if (MathF.Abs(_absoluteColorPeakSpanCm - clamped) <= 0.01f)
                {
                    return;
                }

                _absoluteColorPeakSpanCm = clamped;
                ClearChunkGpuCache();
            }
        }

        public void ApplyFrameLighting(RaylibFrameLighting lighting)
        {
            _frameLighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            if (_initialized)
            {
                lighting.Apply(_terrainShader, in _terrainLightingLocs);
            }
        }

        public void Render(IVisualHeightmapRenderSource source, in Camera3D camera)
        {
            if (source == null)
            {
                return;
            }

            EnsureInitialized();
            UpdateUniforms(camera);

            _frameIndex++;
            DrawnChunkCountLastFrame = 0;
            BuiltChunkCountLastFrame = 0;
            MissingChunkCountLastFrame = 0;
            TerrainVertexCountLastFrame = 0;
            ChunkBuildMsLastFrame = 0d;

            int minChunkX = ResolveChunkIndex((camera.target.X * 100f) - VisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int maxChunkX = ResolveChunkIndex((camera.target.X * 100f) + VisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int minChunkY = ResolveChunkIndex((camera.target.Z * 100f) - VisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
            int maxChunkY = ResolveChunkIndex((camera.target.Z * 100f) + VisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);

            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    if (!source.TryGetChunk(x, y, out VisualHeightmapRenderChunk chunk))
                    {
                        MissingChunkCountLastFrame++;
                        continue;
                    }

                    ref ChunkGpu gpu = ref GetOrCreateChunk(in chunk);
                    gpu.LastUsedFrame = _frameIndex;
                    RaylibMatrix identity = RaylibMatrix.Identity;
                    Rl.rlDisableBackfaceCulling();
                    Rl.DrawMesh(gpu.Mesh, _terrainMaterial, identity);
                    Rl.rlEnableBackfaceCulling();

                    DrawnChunkCountLastFrame++;
                    TerrainVertexCountLastFrame += gpu.Mesh.vertexCount;
                }
            }

            EvictUnusedChunks(240);
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            _terrainShader = Rl.LoadShader(Path.Combine(baseDir, "terrain.vs"), Path.Combine(baseDir, "terrain.fs"));
            if (_terrainShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load visual heightmap terrain shader (shader.id == 0).");
            }

            _terrainMaterial = Rl.LoadMaterialDefault();
            _terrainMaterial.shader = _terrainShader;
            _terrainLightingLocs = RaylibFrameLightingLocations.ResolveOrThrow(_terrainShader, "visual-heightmap terrain");
            _initialized = true;
            if (_frameLighting != null)
            {
                _frameLighting.Apply(_terrainShader, in _terrainLightingLocs);
            }
        }

        private void UpdateUniforms(in Camera3D camera)
        {
            if (_frameLighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} requires {nameof(ApplyFrameLighting)} before Render.");
            }

            _frameLighting.Apply(_terrainShader, in _terrainLightingLocs);
            _frameLighting.ApplyViewPosition(_terrainShader, in _terrainLightingLocs, camera.position);
        }

        private ref ChunkGpu GetOrCreateChunk(in VisualHeightmapRenderChunk chunk)
        {
            long key = GraphChunkKey.Pack(chunk.ChunkX, chunk.ChunkY);
            if (_chunks.TryGetValue(key, out ChunkGpu existing))
            {
                if (existing.Revision == chunk.Revision)
                {
                    _chunks[key] = existing;
                    return ref CollectionsMarshal.GetValueRefOrNullRef(_chunks, key);
                }

                existing.Dispose();
                _chunks.Remove(key);
            }

            long buildStart = Stopwatch.GetTimestamp();
            ChunkGpu gpu = new()
            {
                Mesh = CreateChunkMesh(in chunk),
                Revision = chunk.Revision,
                LastUsedFrame = _frameIndex,
            };
            BuiltChunkCountLastFrame++;
            ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - buildStart) * 1000d / Stopwatch.Frequency;
            _chunks[key] = gpu;
            return ref CollectionsMarshal.GetValueRefOrNullRef(_chunks, key);
        }

        private Mesh CreateChunkMesh(in VisualHeightmapRenderChunk chunk)
        {
            int columns = chunk.SampleColumns;
            int rows = chunk.SampleRows;
            int vertexCount = checked(columns * rows);
            int indexCount = checked((columns - 1) * (rows - 1) * 6);
            if (vertexCount > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Raylib visual heightmap chunk ({chunk.ChunkX},{chunk.ChunkY}) has {vertexCount} vertices, exceeding the platform mesh index limit. Reduce samples per chunk.");
            }

            Mesh mesh = new()
            {
                vertexCount = vertexCount,
                triangleCount = indexCount / 3,
            };

            int vertexFloatCount = vertexCount * 3;
            int colorByteCount = vertexCount * 4;
            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.colors = (byte*)Rl.MemAlloc(sizeof(byte) * colorByteCount);
            mesh.indices = (ushort*)Rl.MemAlloc(sizeof(ushort) * indexCount);

            float stepXCm = chunk.SampleStepXCm;
            float stepYCm = chunk.SampleStepYCm;
            ResolveChunkHeightRange(in chunk, out float minHeightCm, out float maxHeightCm);
            float heightRangeCm = MathF.Max(1f, maxHeightCm - minHeightCm);
            float? absoluteSeaCm = _absoluteColorSeaLevelCm;
            float absolutePeakSpanCm = MathF.Max(1f, _absoluteColorPeakSpanCm);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int vertex = (y * columns) + x;
                    float worldXCm = chunk.Bounds.Left + (x * stepXCm);
                    float worldYCm = chunk.Bounds.Top + (y * stepYCm);
                    chunk.TryReadHeightCm(x, y, out float heightCm);
                    Vector3 normal = ComputeNormal(in chunk, x, y, stepXCm, stepYCm);
                    int f = vertex * 3;
                    mesh.vertices[f + 0] = worldXCm * 0.01f;
                    mesh.vertices[f + 1] = heightCm * 0.01f;
                    mesh.vertices[f + 2] = worldYCm * 0.01f;
                    mesh.normals[f + 0] = normal.X;
                    mesh.normals[f + 1] = normal.Y;
                    mesh.normals[f + 2] = normal.Z;

                    int c = vertex * 4;
                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    byte red;
                    byte green;
                    byte blue;
                    if (absoluteSeaCm is float seaCm)
                    {
                        // Keep negative bands for submerged shelf/abyss tint (refraction reads depth).
                        float heightBand = MathF.Min(1f, (heightCm - seaCm) / absolutePeakSpanCm);
                        ResolveAbsoluteIslandTerrainColor(heightBand, slope, out red, out green, out blue);
                    }
                    else
                    {
                        float heightBand = Math.Clamp((heightCm - minHeightCm) / heightRangeCm, 0f, 1f);
                        ResolveTerrainColor(heightBand, slope, out red, out green, out blue);
                    }

                    mesh.colors[c + 0] = red;
                    mesh.colors[c + 1] = green;
                    mesh.colors[c + 2] = blue;
                    mesh.colors[c + 3] = 255;
                }
            }

            int cursor = 0;
            for (int y = 0; y < rows - 1; y++)
            {
                for (int x = 0; x < columns - 1; x++)
                {
                    int p00 = (y * columns) + x;
                    int p10 = p00 + 1;
                    int p01 = p00 + columns;
                    int p11 = p01 + 1;

                    mesh.indices[cursor++] = checked((ushort)p00);
                    mesh.indices[cursor++] = checked((ushort)p01);
                    mesh.indices[cursor++] = checked((ushort)p10);
                    mesh.indices[cursor++] = checked((ushort)p11);
                    mesh.indices[cursor++] = checked((ushort)p10);
                    mesh.indices[cursor++] = checked((ushort)p01);
                }
            }

            Rl.UploadMesh(ref mesh, false);
            return mesh;
        }

        private static void ResolveChunkHeightRange(in VisualHeightmapRenderChunk chunk, out float minHeightCm, out float maxHeightCm)
        {
            minHeightCm = float.PositiveInfinity;
            maxHeightCm = float.NegativeInfinity;
            int columns = chunk.SampleColumns;
            int rows = chunk.SampleRows;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (!chunk.TryReadHeightCm(x, y, out float heightCm))
                    {
                        continue;
                    }

                    minHeightCm = MathF.Min(minHeightCm, heightCm);
                    maxHeightCm = MathF.Max(maxHeightCm, heightCm);
                }
            }

            if (!float.IsFinite(minHeightCm) || !float.IsFinite(maxHeightCm))
            {
                minHeightCm = 0f;
                maxHeightCm = 1f;
            }
        }

        private static void ResolveTerrainColor(float heightBand, float slope, out byte red, out byte green, out byte blue)
        {
            Vector3 low = new(35f, 86f, 88f);
            Vector3 mid = new(82f, 143f, 84f);
            Vector3 high = new(190f, 174f, 108f);
            Vector3 peak = new(226f, 220f, 184f);
            Vector3 color = heightBand < 0.50f
                ? Vector3.Lerp(low, mid, heightBand * 2f)
                : heightBand < 0.82f
                    ? Vector3.Lerp(mid, high, (heightBand - 0.50f) / 0.32f)
                    : Vector3.Lerp(high, peak, (heightBand - 0.82f) / 0.18f);
            float shade = 1f - Math.Clamp(slope * 0.42f, 0f, 0.42f);
            red = ClampToByte(color.X * shade);
            green = ClampToByte(color.Y * shade);
            blue = ClampToByte(color.Z * shade);
        }

        private static void ResolveAbsoluteIslandTerrainColor(float heightBand, float slope, out byte red, out byte green, out byte blue)
        {
            // Bands are elevation / peak-span: negative = submerged depth, positive = land.
            Vector3 deepSeabed = new(16f, 48f, 92f);
            Vector3 shallowShelf = new(72f, 186f, 168f);
            Vector3 wetSand = new(210f, 188f, 128f);
            Vector3 sand = new(236f, 208f, 142f);
            Vector3 grass = new(52f, 128f, 48f);
            Vector3 dirt = new(128f, 92f, 54f);
            Vector3 rock = new(110f, 108f, 112f);
            Vector3 peak = new(178f, 176f, 172f);
            Vector3 color;
            if (heightBand <= 0f)
            {
                // ~0 at sea → wet sand/turquoise; ~-0.04 (~560cm with 14km span) → deep blue.
                float depth = Math.Clamp((-heightBand) / 0.04f, 0f, 1f);
                color = depth < 0.35f
                    ? Vector3.Lerp(wetSand, shallowShelf, depth / 0.35f)
                    : Vector3.Lerp(shallowShelf, deepSeabed, (depth - 0.35f) / 0.65f);
            }
            else if (heightBand < 0.045f)
            {
                color = Vector3.Lerp(sand, grass, heightBand / 0.045f);
            }
            else if (heightBand < 0.32f)
            {
                color = Vector3.Lerp(grass, dirt, (heightBand - 0.045f) / 0.275f);
            }
            else if (heightBand < 0.58f)
            {
                color = Vector3.Lerp(dirt, rock, (heightBand - 0.32f) / 0.26f);
            }
            else
            {
                color = Vector3.Lerp(rock, peak, Math.Clamp((heightBand - 0.58f) / 0.42f, 0f, 1f));
            }

            float shade = 1f - Math.Clamp(slope * 0.55f, 0f, 0.55f);
            red = ClampToByte(color.X * shade);
            green = ClampToByte(color.Y * shade);
            blue = ClampToByte(color.Z * shade);
        }

        private static Vector3 ComputeNormal(in VisualHeightmapRenderChunk chunk, int x, int y, float stepXCm, float stepYCm)
        {
            int left = Math.Max(0, x - 1);
            int right = Math.Min(chunk.SampleColumns - 1, x + 1);
            int top = Math.Max(0, y - 1);
            int bottom = Math.Min(chunk.SampleRows - 1, y + 1);
            chunk.TryReadHeightCm(left, y, out float hLeft);
            chunk.TryReadHeightCm(right, y, out float hRight);
            chunk.TryReadHeightCm(x, top, out float hTop);
            chunk.TryReadHeightCm(x, bottom, out float hBottom);

            float dx = MathF.Max(1f, (right - left) * stepXCm);
            float dz = MathF.Max(1f, (bottom - top) * stepYCm);
            Vector3 normal = Vector3.Normalize(new Vector3(-(hRight - hLeft) / dx, 1f, -(hBottom - hTop) / dz));
            return float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z)
                ? normal
                : Vector3.UnitY;
        }

        private void ClearChunkGpuCache()
        {
            foreach (var kvp in _chunks)
            {
                kvp.Value.Dispose();
            }

            _chunks.Clear();
        }

        private void EvictUnusedChunks(int maxAgeFrames)
        {
            if (_chunks.Count == 0)
            {
                return;
            }

            int threshold = _frameIndex - maxAgeFrames;
            _evictKeys.Clear();
            foreach (var kvp in _chunks)
            {
                if (kvp.Value.LastUsedFrame < threshold)
                {
                    _evictKeys.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _evictKeys.Count; i++)
            {
                long key = _evictKeys[i];
                if (_chunks.TryGetValue(key, out ChunkGpu chunk))
                {
                    chunk.Dispose();
                    _chunks.Remove(key);
                }
            }
        }

        private static int ResolveChunkIndex(float worldCm, int minCm, int sizeCm, int chunkCount)
        {
            float normalized = (worldCm - minCm) / Math.Max(1f, sizeCm);
            return Math.Clamp((int)MathF.Floor(normalized * chunkCount), 0, chunkCount - 1);
        }

        private static byte ClampToByte(float value)
        {
            return (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
        }

        public void Dispose()
        {
            foreach (var kvp in _chunks)
            {
                kvp.Value.Dispose();
            }

            _chunks.Clear();
            if (!_initialized)
            {
                return;
            }

            _terrainMaterial.shader = default;
            Rl.UnloadMaterial(_terrainMaterial);
            Rl.UnloadShader(_terrainShader);
            _initialized = false;
        }

        private struct ChunkGpu : IDisposable
        {
            public Mesh Mesh;
            public int Revision;
            public int LastUsedFrame;

            public void Dispose()
            {
                if (Mesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(Mesh);
                }
            }
        }
    }
}
