using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Rendering;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed unsafe class RaylibTerrainRenderer : IDisposable
    {
        private readonly Dictionary<long, ChunkGpu> _chunks = new Dictionary<long, ChunkGpu>(1024);
        private readonly VertexMapChunkMeshData _meshData = new VertexMapChunkMeshData();
        private readonly List<long> _evictKeys = new List<long>(256);

        private VertexMapChunkMeshBuilder _builder;
        private bool _initialized;

        private Shader _terrainShader;
        private Material _terrainMaterial;
        private int _locTerrainLightPos;
        private int _locTerrainViewPos;
        private int _locTerrainAmbient;
        private int _locTerrainIntensity;

        private Shader _waterShader;
        private Material _waterMaterial;
        private int _locWaterLightPos;
        private int _locWaterViewPos;
        private int _locWaterAmbient;
        private int _locWaterIntensity;

        private int _frameIndex;

        public int DrawnChunkCountLastFrame { get; private set; }
        public int TerrainVertexCountLastFrame { get; private set; }
        public int WaterVertexCountLastFrame { get; private set; }
        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadius { get; set; } = 900f;
        public float SimplifiedCliffRadius { get; set; } = 350f;

        public Vector3 LightPosition { get; set; } = new Vector3(50f, 200f, 100f);
        public float Ambient { get; set; } = 0.8f;
        public float LightIntensity { get; set; } = 1.0f;

        public float HeightScale { get; set; } = 2.0f;

        public void Render(VertexMap map, in Camera3D camera)
        {
            if (map == null) return;

            EnsureInitialized(map);
            UpdateUniforms(camera);

            _frameIndex++;
            DrawnChunkCountLastFrame = 0;
            TerrainVertexCountLastFrame = 0;
            WaterVertexCountLastFrame = 0;
            float cx = camera.target.X;
            float cz = camera.target.Z;

            int minChunkX = (int)MathF.Floor((cx - VisibleRadius) / (HexCoordinates.HexWidth * VertexChunk.ChunkSize));
            int maxChunkX = (int)MathF.Ceiling((cx + VisibleRadius) / (HexCoordinates.HexWidth * VertexChunk.ChunkSize));
            int minChunkY = (int)MathF.Floor((cz - VisibleRadius) / (HexCoordinates.RowSpacing * VertexChunk.ChunkSize));
            int maxChunkY = (int)MathF.Ceiling((cz + VisibleRadius) / (HexCoordinates.RowSpacing * VertexChunk.ChunkSize));

            minChunkX = Math.Max(0, minChunkX);
            minChunkY = Math.Max(0, minChunkY);
            maxChunkX = Math.Min(map.WidthInChunks - 1, maxChunkX);
            maxChunkY = Math.Min(map.HeightInChunks - 1, maxChunkY);

            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    long key = HexCoordinates.GetChunkKey(x, y);
                    float chunkWorldX = x * VertexChunk.ChunkSize * HexCoordinates.HexWidth;
                    float chunkWorldZ = y * VertexChunk.ChunkSize * HexCoordinates.RowSpacing;
                    float dx = chunkWorldX - cx;
                    float dz = chunkWorldZ - cz;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);

                    bool simplified = dist > SimplifiedCliffRadius;
                    ref ChunkGpu chunk = ref GetOrCreateChunk(map, x, y, simplified);
                    chunk.LastUsedFrame = _frameIndex;

                    RaylibMatrix identity = RaylibMatrix.Identity;
                    Rl.rlDisableBackfaceCulling();
                    Rl.DrawMesh(chunk.TerrainMesh, _terrainMaterial, identity);
                    if (chunk.WaterMesh.vertexCount > 0)
                    {
                        Rl.DrawMesh(chunk.WaterMesh, _waterMaterial, identity);
                        WaterVertexCountLastFrame += chunk.WaterMesh.vertexCount;
                    }
                    Rl.rlEnableBackfaceCulling();
                    DrawnChunkCountLastFrame++;
                    TerrainVertexCountLastFrame += chunk.TerrainMesh.vertexCount;
                }
            }

            EvictUnusedChunks(240);
        }

        private void EnsureInitialized(VertexMap map)
        {
            if (_initialized) return;

            _builder = new VertexMapChunkMeshBuilder(map);
            string baseDir = AppContext.BaseDirectory;
            _terrainShader = Rl.LoadShader(Path.Combine(baseDir, "terrain.vs"), Path.Combine(baseDir, "terrain.fs"));
            if (_terrainShader.id == 0) throw new InvalidOperationException("Failed to load terrain shader (shader.id == 0).");
            _terrainMaterial = Rl.LoadMaterialDefault();
            _terrainMaterial.shader = _terrainShader;

            _locTerrainLightPos = Rl.GetShaderLocation(_terrainShader, "uLightPos");
            _locTerrainViewPos = Rl.GetShaderLocation(_terrainShader, "uViewPos");
            _locTerrainAmbient = Rl.GetShaderLocation(_terrainShader, "uAmbient");
            _locTerrainIntensity = Rl.GetShaderLocation(_terrainShader, "uLightIntensity");

            _waterShader = Rl.LoadShader(Path.Combine(baseDir, "water.vs"), Path.Combine(baseDir, "water.fs"));
            if (_waterShader.id == 0) throw new InvalidOperationException("Failed to load water shader (shader.id == 0).");
            _waterMaterial = Rl.LoadMaterialDefault();
            _waterMaterial.shader = _waterShader;

            _locWaterLightPos = Rl.GetShaderLocation(_waterShader, "uLightPos");
            _locWaterViewPos = Rl.GetShaderLocation(_waterShader, "uViewPos");
            _locWaterAmbient = Rl.GetShaderLocation(_waterShader, "uAmbient");
            _locWaterIntensity = Rl.GetShaderLocation(_waterShader, "uLightIntensity");

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

            Rl.SetShaderValue(_waterShader, _locWaterLightPos, &lightPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterViewPos, &viewPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterIntensity, &intensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        private ref ChunkGpu GetOrCreateChunk(VertexMap map, int chunkX, int chunkY, bool simplifiedCliffs)
        {
            long key = HexCoordinates.GetChunkKey(chunkX, chunkY);
            if (_chunks.TryGetValue(key, out var existing))
            {
                if (existing.SimplifiedCliffs != simplifiedCliffs)
                {
                    existing.Dispose();
                    _chunks.Remove(key);
                }
                else
                {
                    _chunks[key] = existing;
                    return ref _chunks.GetValueRefOrNullRef(key);
                }
            }

            _builder.BuildChunk(chunkX, chunkY, 0f, 0f, HeightScale, simplifiedCliffs, _meshData);
            ChunkGpu gpu = new ChunkGpu();
            gpu.SimplifiedCliffs = simplifiedCliffs;
            gpu.TerrainMesh = CreateMesh(_meshData.Terrain);
            gpu.WaterMesh = _meshData.Water.VertexCount > 0 ? CreateMesh(_meshData.Water) : default;
            gpu.LastUsedFrame = _frameIndex;
            _chunks[key] = gpu;
            return ref _chunks.GetValueRefOrNullRef(key);
        }

        private static Mesh CreateMesh(ChunkMeshWriteBuffer src)
        {
            Mesh mesh = new Mesh();
            mesh.vertexCount = src.VertexCount;
            mesh.triangleCount = src.VertexCount / 3;

            int vFloats = src.VertexCount * 3;
            int cBytes = src.VertexCount * 4;

            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vFloats);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * vFloats);
            mesh.colors = (byte*)Rl.MemAlloc(sizeof(byte) * cBytes);

            src.Vertices.AsSpan(0, vFloats).CopyTo(new Span<float>(mesh.vertices, vFloats));
            src.Normals.AsSpan(0, vFloats).CopyTo(new Span<float>(mesh.normals, vFloats));
            src.Colors.AsSpan(0, cBytes).CopyTo(new Span<byte>(mesh.colors, cBytes));

            Rl.UploadMesh(ref mesh, false);
            return mesh;
        }

        private void EvictUnusedChunks(int maxAgeFrames)
        {
            if (_chunks.Count == 0) return;
            int threshold = _frameIndex - maxAgeFrames;
            _evictKeys.Clear();
            foreach (var kvp in _chunks)
            {
                if (kvp.Value.LastUsedFrame < threshold) _evictKeys.Add(kvp.Key);
            }

            for (int i = 0; i < _evictKeys.Count; i++)
            {
                long key = _evictKeys[i];
                if (_chunks.TryGetValue(key, out var chunk))
                {
                    chunk.Dispose();
                    _chunks.Remove(key);
                }
            }
        }

        public void Dispose()
        {
            foreach (var kvp in _chunks)
            {
                kvp.Value.Dispose();
            }
            _chunks.Clear();

            if (_initialized)
            {
                Rl.UnloadMaterial(_terrainMaterial);
                Rl.UnloadShader(_terrainShader);
                Rl.UnloadMaterial(_waterMaterial);
                Rl.UnloadShader(_waterShader);
            }
        }

        private struct ChunkGpu : IDisposable
        {
            public Mesh TerrainMesh;
            public Mesh WaterMesh;
            public int LastUsedFrame;
            public bool SimplifiedCliffs;

            public void Dispose()
            {
                if (TerrainMesh.vertexCount > 0) Rl.UnloadMesh(TerrainMesh);
                if (WaterMesh.vertexCount > 0) Rl.UnloadMesh(WaterMesh);
            }
        }
    }

    internal static class DictionaryExtensions
    {
        public static ref T GetValueRefOrNullRef<TKey, T>(this Dictionary<TKey, T> dict, TKey key) where TKey : notnull
        {
            return ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(dict, key);
        }
    }
}
