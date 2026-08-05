using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private int _locTerrainViewPos;
        private int _locTerrainSunDirection;
        private int _locTerrainSunColor;
        private int _locTerrainAmbientColor;
        private int _locTerrainAmbient;
        private int _locTerrainIntensity;
        private int _locTerrainFogColor;
        private int _locTerrainFogNear;
        private int _locTerrainFogFar;
        private int _locTerrainFogDensity;

        private Shader _waterShader;
        private Material _waterMaterial;
        private int _locWaterViewPos;
        private int _locWaterSunDirection;
        private int _locWaterSunColor;
        private int _locWaterAmbientColor;
        private int _locWaterAmbient;
        private int _locWaterIntensity;
        private int _locWaterFogColor;
        private int _locWaterFogNear;
        private int _locWaterFogFar;
        private int _locWaterFogDensity;
        private int _locWaterTime;
        private int _locWaterShallowColor;
        private int _locWaterDeepColor;
        private int _locWaterWaveAmplitude;
        private int _locWaterWaveFrequency;
        private int _locWaterWaveSpeed;
        private int _locWaterFresnelStrength;

        private int _frameIndex;
        private RaylibRenderEnvironmentConfig _environmentConfig = RaylibRenderEnvironmentConfig.CreateDefault();

        public int DrawnChunkCountLastFrame { get; private set; }
        public int BuiltChunkCountLastFrame { get; private set; }
        public int TerrainVertexCountLastFrame { get; private set; }
        public int WaterVertexCountLastFrame { get; private set; }
        public double ChunkBuildMsLastFrame { get; private set; }
        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadius { get; set; } = 900f;
        public float SimplifiedCliffRadius { get; set; } = 350f;

        public RaylibRenderEnvironmentConfig EnvironmentConfig
        {
            get => _environmentConfig;
            set => _environmentConfig = value?.NormalizeAndValidate() ?? throw new ArgumentNullException(nameof(value));
        }

        public float HeightScale { get; set; } = 2.0f;

        public void Render(VertexMap map, in Camera3D camera, double timeSeconds)
        {
            if (map == null) return;

            EnsureInitialized(map);
            UpdateUniforms(camera, timeSeconds);

            _frameIndex++;
            DrawnChunkCountLastFrame = 0;
            BuiltChunkCountLastFrame = 0;
            TerrainVertexCountLastFrame = 0;
            WaterVertexCountLastFrame = 0;
            ChunkBuildMsLastFrame = 0d;
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
                        Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
                        Rl.DrawMesh(chunk.WaterMesh, _waterMaterial, identity);
                        Rl.EndBlendMode();
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

            _locTerrainViewPos = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uViewPos", "terrain");
            _locTerrainSunDirection = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uSunDirection", "terrain");
            _locTerrainSunColor = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uSunColor", "terrain");
            _locTerrainAmbientColor = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uAmbientColor", "terrain");
            _locTerrainAmbient = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uAmbient", "terrain");
            _locTerrainIntensity = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uLightIntensity", "terrain");
            _locTerrainFogColor = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uFogColor", "terrain");
            _locTerrainFogNear = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uFogNear", "terrain");
            _locTerrainFogFar = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uFogFar", "terrain");
            _locTerrainFogDensity = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uFogDensity", "terrain");

            _waterShader = Rl.LoadShader(Path.Combine(baseDir, "water.vs"), Path.Combine(baseDir, "water.fs"));
            if (_waterShader.id == 0) throw new InvalidOperationException("Failed to load water shader (shader.id == 0).");
            _waterMaterial = Rl.LoadMaterialDefault();
            _waterMaterial.shader = _waterShader;

            _locWaterViewPos = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uViewPos", "water");
            _locWaterSunDirection = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uSunDirection", "water");
            _locWaterSunColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uSunColor", "water");
            _locWaterAmbientColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uAmbientColor", "water");
            _locWaterAmbient = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uAmbient", "water");
            _locWaterIntensity = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uLightIntensity", "water");
            _locWaterFogColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFogColor", "water");
            _locWaterFogNear = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFogNear", "water");
            _locWaterFogFar = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFogFar", "water");
            _locWaterFogDensity = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFogDensity", "water");
            _locWaterTime = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uTime", "water");
            _locWaterShallowColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaterShallowColor", "water");
            _locWaterDeepColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaterDeepColor", "water");
            _locWaterWaveAmplitude = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaveAmplitude", "water");
            _locWaterWaveFrequency = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaveFrequency", "water");
            _locWaterWaveSpeed = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaveSpeed", "water");
            _locWaterFresnelStrength = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFresnelStrength", "water");

            _initialized = true;
        }

        private void UpdateUniforms(in Camera3D camera, double timeSeconds)
        {
            RaylibRenderEnvironmentConfig config = EnvironmentConfig;
            RaylibLightingConfig lighting = config.Lighting;
            RaylibWaterRenderConfig water = config.Water;
            Vector3 viewPos = camera.position;
            Vector3 sunDirection = lighting.SunDirection;
            Vector3 sunColor = lighting.SunColor;
            Vector3 ambientColor = lighting.AmbientColor;
            Vector3 fogColor = lighting.FogColor;
            float ambient = lighting.AmbientStrength;
            float intensity = lighting.SunStrength;
            float fogNear = lighting.FogNearMeters;
            float fogFar = lighting.FogFarMeters;
            float fogDensity = lighting.FogDensity;
            float time = (float)(timeSeconds % 100000.0);
            Vector3 waterShallowColor = water.ShallowColor;
            Vector3 waterDeepColor = water.DeepColor;
            float waveAmplitude = water.WaveAmplitudeMeters;
            float waveFrequency = water.WaveFrequency;
            float waveSpeed = water.WaveSpeed;
            float fresnelStrength = water.FresnelStrength;

            Rl.SetShaderValue(_terrainShader, _locTerrainViewPos, &viewPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locTerrainSunDirection, &sunDirection, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locTerrainSunColor, &sunColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locTerrainAmbientColor, &ambientColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locTerrainAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_terrainShader, _locTerrainIntensity, &intensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_terrainShader, _locTerrainFogColor, &fogColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locTerrainFogNear, &fogNear, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_terrainShader, _locTerrainFogFar, &fogFar, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_terrainShader, _locTerrainFogDensity, &fogDensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

            Rl.SetShaderValue(_waterShader, _locWaterViewPos, &viewPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterSunDirection, &sunDirection, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterSunColor, &sunColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterAmbientColor, &ambientColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterIntensity, &intensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterFogColor, &fogColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterFogNear, &fogNear, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterFogFar, &fogFar, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterFogDensity, &fogDensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterTime, &time, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterShallowColor, &waterShallowColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterDeepColor, &waterDeepColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterWaveAmplitude, &waveAmplitude, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterWaveFrequency, &waveFrequency, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterWaveSpeed, &waveSpeed, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterFresnelStrength, &fresnelStrength, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
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

            long buildStart = Stopwatch.GetTimestamp();
            _builder.BuildChunk(chunkX, chunkY, 0f, 0f, HeightScale, simplifiedCliffs, _meshData);
            ChunkGpu gpu = new ChunkGpu();
            gpu.SimplifiedCliffs = simplifiedCliffs;
            gpu.TerrainMesh = CreateMesh(_meshData.Terrain);
            gpu.WaterMesh = _meshData.Water.VertexCount > 0 ? CreateMesh(_meshData.Water) : default;
            gpu.LastUsedFrame = _frameIndex;
            BuiltChunkCountLastFrame++;
            ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - buildStart) * 1000.0 / Stopwatch.Frequency;
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
                _terrainMaterial.shader = default;
                Rl.UnloadMaterial(_terrainMaterial);
                Rl.UnloadShader(_terrainShader);
                _waterMaterial.shader = default;
                Rl.UnloadMaterial(_waterMaterial);
                Rl.UnloadShader(_waterShader);
                _initialized = false;
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
