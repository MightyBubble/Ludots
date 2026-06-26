using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
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
        private int _locTerrainLightPos;
        private int _locTerrainViewPos;
        private int _locTerrainAmbient;
        private int _locTerrainIntensity;
        private bool _initialized;
        private int _frameIndex;

        public int DrawnChunkCountLastFrame { get; private set; }

        public int BuiltChunkCountLastFrame { get; private set; }

        public int MissingChunkCountLastFrame { get; private set; }

        public int TerrainVertexCountLastFrame { get; private set; }

        public double ChunkBuildMsLastFrame { get; private set; }

        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadiusCm { get; set; } = 120_000f;

        public Vector3 LightPosition { get; set; } = new(50f, 200f, 100f);

        public float Ambient { get; set; } = 0.45f;

        public float LightIntensity { get; set; } = 0.55f;

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
            IVisualHeightmapMipRenderSource? mipSource = source as IVisualHeightmapMipRenderSource;

            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    if (!TryGetRenderChunk(source, mipSource, x, y, in camera, out VisualHeightmapRenderChunk chunk))
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

        private static bool TryGetRenderChunk(
            IVisualHeightmapRenderSource source,
            IVisualHeightmapMipRenderSource? mipSource,
            int chunkX,
            int chunkY,
            in Camera3D camera,
            out VisualHeightmapRenderChunk chunk)
        {
            if (mipSource == null || mipSource.MaxRenderMipLevel <= 0)
            {
                return source.TryGetChunk(chunkX, chunkY, out chunk);
            }

            int requestedMipLevel = ResolveRenderMipLevel(source, mipSource.MaxRenderMipLevel, chunkX, chunkY, in camera);
            for (int mipLevel = requestedMipLevel; mipLevel > 0; mipLevel--)
            {
                if (mipSource.TryGetChunk(chunkX, chunkY, mipLevel, out chunk))
                {
                    return true;
                }
            }

            return source.TryGetChunk(chunkX, chunkY, out chunk);
        }

        private static int ResolveRenderMipLevel(
            IVisualHeightmapRenderSource source,
            int maxMipLevel,
            int chunkX,
            int chunkY,
            in Camera3D camera)
        {
            if (maxMipLevel <= 0)
            {
                return 0;
            }

            float chunkWidthCm = source.Bounds.Width / (float)Math.Max(1, source.ChunkColumns);
            float chunkHeightCm = source.Bounds.Height / (float)Math.Max(1, source.ChunkRows);
            float centerXCm = source.Bounds.Left + ((chunkX + 0.5f) * chunkWidthCm);
            float centerYCm = source.Bounds.Top + ((chunkY + 0.5f) * chunkHeightCm);
            float dx = centerXCm - (camera.target.X * 100f);
            float dy = centerYCm - (camera.target.Z * 100f);
            float distanceCm = MathF.Sqrt((dx * dx) + (dy * dy));
            float nearDistanceCm = MathF.Sqrt((chunkWidthCm * chunkWidthCm) + (chunkHeightCm * chunkHeightCm)) * 2f;
            if (!float.IsFinite(distanceCm) || distanceCm <= nearDistanceCm)
            {
                return 0;
            }

            int mipLevel = 0;
            float ratio = distanceCm / Math.Max(1f, nearDistanceCm);
            while (ratio >= 2f && mipLevel < maxMipLevel)
            {
                mipLevel++;
                ratio *= 0.5f;
            }

            return mipLevel;
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
            _locTerrainLightPos = Rl.GetShaderLocation(_terrainShader, "uLightPos");
            _locTerrainViewPos = Rl.GetShaderLocation(_terrainShader, "uViewPos");
            _locTerrainAmbient = Rl.GetShaderLocation(_terrainShader, "uAmbient");
            _locTerrainIntensity = Rl.GetShaderLocation(_terrainShader, "uLightIntensity");
            _initialized = true;
        }

        private void UpdateUniforms(in Camera3D camera)
        {
            Vector3 lightPos = LightPosition;
            Vector3 viewPos = camera.position;
            float ambient = Ambient;
            float intensity = LightIntensity;

            Rl.SetShaderValue(_terrainShader, _locTerrainLightPos, &lightPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locTerrainViewPos, &viewPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locTerrainAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_terrainShader, _locTerrainIntensity, &intensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        private ref ChunkGpu GetOrCreateChunk(in VisualHeightmapRenderChunk chunk)
        {
            long key = GraphChunkKey.Pack(chunk.ChunkX, chunk.ChunkY);
            if (_chunks.TryGetValue(key, out ChunkGpu existing))
            {
                if (existing.Revision == chunk.Revision)
                {
                    _chunks[key] = existing;
                    return ref _chunks.GetValueRefOrNullRef(key);
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
            return ref _chunks.GetValueRefOrNullRef(key);
        }

        private static Mesh CreateChunkMesh(in VisualHeightmapRenderChunk chunk)
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
                    float heightBand = Math.Clamp((heightCm - minHeightCm) / heightRangeCm, 0f, 1f);
                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    ResolveTerrainColor(heightBand, slope, out byte red, out byte green, out byte blue);
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
