using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;

namespace Ludots.Raylib.Render
{
    public sealed unsafe class RaylibTerrainRenderer : IDisposable, IRaylibReceiverMeshProjector
    {
        private readonly Dictionary<long, ChunkGpu> _chunks = new Dictionary<long, ChunkGpu>(1024);
        private readonly VertexMapChunkMeshData _meshData = new VertexMapChunkMeshData();
        private readonly List<long> _evictKeys = new List<long>(256);

        private bool _initialized;

        private Shader _terrainShader;
        private Material _terrainMaterial;
        private RaylibFrameLightingLocations _terrainLightingLocs;
        private RaylibShadowSamplingLocations _terrainShadowLocs;

        private Shader _waterShader;
        private Material _waterMaterial;
        private int _locWaterLightPos;
        private int _locWaterViewPos;
        private int _locWaterAmbient;
        private int _locWaterIntensity;
        private int _locWaterSampleReflection;
        private int _locWaterUseDudv;
        private int _locWaterMoveFactor;
        private int _locWaterWaveStrength;

        private RaylibFrameLighting? _frameLighting;
        private RaylibDirectionalShadowMap? _frameShadow;
        private float _frameShadowTexelWorld = 0.08f;
        private int _frameIndex;
        private Mesh _oceanPlaneMesh;
        private bool _oceanPlaneReady;
        private IVisualHeightmap? _stampHeightSampleSource;

        public int DrawnChunkCountLastFrame { get; private set; }
        public int BuiltChunkCountLastFrame { get; private set; }
        public int TerrainVertexCountLastFrame { get; private set; }
        public int WaterVertexCountLastFrame { get; private set; }
        public double ChunkBuildMsLastFrame { get; private set; }
        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadius { get; set; } = 900f;
        public float SimplifiedCliffRadius { get; set; } = 350f;

        public float HeightScale { get; set; } = 2.0f;

        public void ApplyFrameLighting(
            RaylibFrameLighting lighting,
            RaylibDirectionalShadowMap? shadow = null,
            float shadowTexelWorld = 0.08f)
        {
            _frameLighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            _frameShadow = shadow;
            _frameShadowTexelWorld = shadowTexelWorld;
            if (_initialized)
            {
                lighting.Apply(_terrainShader, in _terrainLightingLocs);
                ApplyTerrainShadow();
            }
        }

        public void EnsureWaterShadersReady()
        {
            EnsureShadersInitialized();
        }

        /// <summary>
        /// Draws a single reflective ocean plane for VisualHeightmap maps (no VertexMap water mesh).
        /// Requires <see cref="BindReflectiveWater"/> first.
        /// </summary>
        public void DrawReflectiveOceanPlane(float planeYMeters, float halfExtentMeters, in Camera3D camera)
        {
            EnsureShadersInitialized();
            if (_frameLighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(DrawReflectiveOceanPlane)} requires {nameof(ApplyFrameLighting)} first.");
            }

            if (!_oceanPlaneReady)
            {
                _oceanPlaneMesh = CreateOceanPlaneMesh(halfExtentMeters);
                _oceanPlaneReady = true;
            }

            UpdateUniforms(in camera);
            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateTranslation(camera.target.X, planeYMeters, camera.target.Z));
            Rl.rlEnableDepthTest();
            Rl.rlDisableDepthMask();
            Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
            Rl.rlDisableBackfaceCulling();
            Rl.DrawMesh(_oceanPlaneMesh, _waterMaterial, transform);
            Rl.rlEnableBackfaceCulling();
            Rl.EndBlendMode();
            Rl.rlEnableDepthMask();
        }

        private static Mesh CreateOceanPlaneMesh(float halfExtentMeters)
        {
            // Two triangles on XZ, Y=0 local; tinted tropical cyan — depth cue comes from refraction.
            float e = MathF.Max(1f, halfExtentMeters);
            float[] vertices =
            {
                -e, 0f, -e,
                 e, 0f, -e,
                 e, 0f,  e,
                -e, 0f, -e,
                 e, 0f,  e,
                -e, 0f,  e,
            };
            float[] normals =
            {
                0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f,
                0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f,
            };
            byte[] colors =
            {
                0x3D, 0xB5, 0xE0, 0x9A,
                0x3D, 0xB5, 0xE0, 0x9A,
                0x3D, 0xB5, 0xE0, 0x9A,
                0x3D, 0xB5, 0xE0, 0x9A,
                0x3D, 0xB5, 0xE0, 0x9A,
                0x3D, 0xB5, 0xE0, 0x9A,
            };

            Mesh mesh = new Mesh
            {
                vertexCount = 6,
                triangleCount = 2,
            };
            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vertices.Length);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * normals.Length);
            mesh.colors = (byte*)Rl.MemAlloc(sizeof(byte) * colors.Length);
            vertices.AsSpan().CopyTo(new Span<float>(mesh.vertices, vertices.Length));
            normals.AsSpan().CopyTo(new Span<float>(mesh.normals, normals.Length));
            colors.AsSpan().CopyTo(new Span<byte>(mesh.colors, colors.Length));
            RaylibNativeResources.UploadMesh(ref mesh, false);
            return mesh;
        }

        public void BindReflectiveWater(RaylibWaterPass waterPass)
        {
            if (waterPass == null) throw new ArgumentNullException(nameof(waterPass));
            EnsureShadersInitialized();

            if (!waterPass.IsActive)
            {
                ClearReflectiveWater();
                return;
            }

            Texture2D reflection = waterPass.ReflectionTexture;
            Texture2D refraction = waterPass.RefractionTexture;
            Texture2D dudv = waterPass.DudvTexture;
            if (reflection.id == 0 || refraction.id == 0 || dudv.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibTerrainRenderer)} reflective water requires configured reflection/refraction/DUDV textures; refusing flat-alpha fallback.");
            }

            Rl.SetMaterialTexture(ref _waterMaterial, (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO, reflection);
            Rl.SetMaterialTexture(ref _waterMaterial, (int)Rl.MaterialMapIndex.MATERIAL_MAP_METALNESS, refraction);
            Rl.SetMaterialTexture(ref _waterMaterial, (int)Rl.MaterialMapIndex.MATERIAL_MAP_NORMAL, dudv);

            int sample = 1;
            int useDudv = waterPass.HasDudvMap ? 1 : 0;
            float moveFactor = waterPass.MoveFactor;
            float waveStrength = waterPass.WaveStrength;
            Rl.SetShaderValue(_waterShader, _locWaterSampleReflection, &sample, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            Rl.SetShaderValue(_waterShader, _locWaterUseDudv, &useDudv, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            Rl.SetShaderValue(_waterShader, _locWaterMoveFactor, &moveFactor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterWaveStrength, &waveStrength, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        public void ClearReflectiveWater()
        {
            if (!_initialized)
            {
                return;
            }

            int sample = 0;
            int useDudv = 0;
            float moveFactor = 0f;
            float waveStrength = 0f;
            Rl.SetShaderValue(_waterShader, _locWaterSampleReflection, &sample, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            Rl.SetShaderValue(_waterShader, _locWaterUseDudv, &useDudv, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            Rl.SetShaderValue(_waterShader, _locWaterMoveFactor, &moveFactor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterWaveStrength, &waveStrength, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            DetachWaterPassTextures();
        }

        private void DetachWaterPassTextures()
        {
            if (_waterMaterial.maps == null)
            {
                return;
            }

            _waterMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO].texture = default;
            _waterMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_METALNESS].texture = default;
            _waterMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_NORMAL].texture = default;
        }

        public void Render(ITerrainChunkMeshSource source, in Camera3D camera)
        {
            RenderInternal(source, in camera, drawTerrain: true, drawWater: true, bumpFrame: true);
        }

        public void RenderTerrainOnly(ITerrainChunkMeshSource source, in Camera3D camera)
        {
            RenderInternal(source, in camera, drawTerrain: true, drawWater: false, bumpFrame: false);
        }

        public void RenderWaterOnly(ITerrainChunkMeshSource source, in Camera3D camera)
        {
            RenderInternal(source, in camera, drawTerrain: false, drawWater: true, bumpFrame: false);
        }

        public void RenderTerrainShadow(ITerrainChunkMeshSource source, in Camera3D camera, RaylibDirectionalShadowMap shadow)
        {
            if (shadow == null) throw new ArgumentNullException(nameof(shadow));
            if (source == null || source.WidthInChunks <= 0) return;

            EnsureShadersInitialized();

            float cx = camera.target.X;
            float cz = camera.target.Z;

            int minChunkX = (int)MathF.Floor((cx - VisibleRadius) / source.ChunkSpacingXMeters);
            int maxChunkX = (int)MathF.Ceiling((cx + VisibleRadius) / source.ChunkSpacingXMeters);
            int minChunkY = (int)MathF.Floor((cz - VisibleRadius) / source.ChunkSpacingYMeters);
            int maxChunkY = (int)MathF.Ceiling((cz + VisibleRadius) / source.ChunkSpacingYMeters);

            minChunkX = Math.Max(0, minChunkX);
            minChunkY = Math.Max(0, minChunkY);
            maxChunkX = Math.Min(source.WidthInChunks - 1, maxChunkX);
            maxChunkY = Math.Min(source.HeightInChunks - 1, maxChunkY);

            RaylibMatrix identity = RaylibMatrix.Identity;
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    float chunkWorldX = x * source.ChunkSpacingXMeters;
                    float chunkWorldZ = y * source.ChunkSpacingYMeters;
                    float dx = chunkWorldX - cx;
                    float dz = chunkWorldZ - cz;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    bool simplified = dist > SimplifiedCliffRadius;
                    ref ChunkGpu chunk = ref GetOrCreateChunk(source, x, y, simplified);
                    shadow.DrawMeshShadow(chunk.TerrainMesh, identity);
                }
            }
        }

        private void RenderInternal(ITerrainChunkMeshSource source, in Camera3D camera, bool drawTerrain, bool drawWater, bool bumpFrame)
        {
            if (source == null || source.WidthInChunks <= 0) return;

            EnsureShadersInitialized();
            UpdateUniforms(camera);

            if (bumpFrame)
            {
                _frameIndex++;
                DrawnChunkCountLastFrame = 0;
                BuiltChunkCountLastFrame = 0;
                TerrainVertexCountLastFrame = 0;
                WaterVertexCountLastFrame = 0;
                ChunkBuildMsLastFrame = 0d;
            }

            float cx = camera.target.X;
            float cz = camera.target.Z;

            int minChunkX = (int)MathF.Floor((cx - VisibleRadius) / source.ChunkSpacingXMeters);
            int maxChunkX = (int)MathF.Ceiling((cx + VisibleRadius) / source.ChunkSpacingXMeters);
            int minChunkY = (int)MathF.Floor((cz - VisibleRadius) / source.ChunkSpacingYMeters);
            int maxChunkY = (int)MathF.Ceiling((cz + VisibleRadius) / source.ChunkSpacingYMeters);

            minChunkX = Math.Max(0, minChunkX);
            minChunkY = Math.Max(0, minChunkY);
            maxChunkX = Math.Min(source.WidthInChunks - 1, maxChunkX);
            maxChunkY = Math.Min(source.HeightInChunks - 1, maxChunkY);

            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    float chunkWorldX = x * source.ChunkSpacingXMeters;
                    float chunkWorldZ = y * source.ChunkSpacingYMeters;
                    float dx = chunkWorldX - cx;
                    float dz = chunkWorldZ - cz;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);

                    bool simplified = dist > SimplifiedCliffRadius;
                    ref ChunkGpu chunk = ref GetOrCreateChunk(source, x, y, simplified);
                    chunk.LastUsedFrame = _frameIndex;

                    RaylibMatrix identity = RaylibMatrix.Identity;
                    Rl.rlDisableBackfaceCulling();
                    if (drawTerrain)
                    {
                        Rl.DrawMesh(chunk.TerrainMesh, _terrainMaterial, identity);
                        if (bumpFrame)
                        {
                            DrawnChunkCountLastFrame++;
                            TerrainVertexCountLastFrame += chunk.TerrainMesh.vertexCount;
                        }
                    }

                    if (drawWater && chunk.WaterMesh.vertexCount > 0)
                    {
                        Rl.DrawMesh(chunk.WaterMesh, _waterMaterial, identity);
                        if (bumpFrame)
                        {
                            WaterVertexCountLastFrame += chunk.WaterMesh.vertexCount;
                        }
                    }

                    Rl.rlEnableBackfaceCulling();
                }
            }

            if (bumpFrame)
            {
                EvictUnusedChunks(240);
            }
        }

        private void EnsureShadersInitialized()
        {
            if (_initialized)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            _terrainShader = RaylibShaderLoader.Load(baseDir, "terrain.vs", "terrain.fs", "terrain");
            _terrainMaterial = RaylibNativeResources.LoadMaterialDefault();
            _terrainMaterial.shader = _terrainShader;

            _terrainLightingLocs = RaylibFrameLightingLocations.ResolveOrThrow(_terrainShader, "terrain");
            _terrainShadowLocs = RaylibShadowSamplingLocations.ResolveOrThrow(
                _terrainShader,
                "terrain",
                RaylibShadowSampling.ShaderTextureSlot);
            int locTerrainMvp = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "mvp", "terrain");
            int locTerrainMatModel = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "matModel", "terrain");
            int locTerrainVertexPosition = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexPosition", "terrain");
            int locTerrainVertexNormal = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexNormal", "terrain");
            int locTerrainVertexColor = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexColor", "terrain");
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locTerrainVertexPosition;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locTerrainVertexNormal;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = locTerrainVertexColor;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locTerrainMvp;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locTerrainMatModel;
            // Shared terrain.fs supports optional VH height-band albedos; VertexMap path stays vertex tint only.
            int locUseTerrainAlbedo = Rl.GetShaderLocation(_terrainShader, "uUseTerrainAlbedo");
            if (locUseTerrainAlbedo >= 0)
            {
                int useAlbedo = 0;
                Rl.SetShaderValue(
                    _terrainShader,
                    locUseTerrainAlbedo,
                    &useAlbedo,
                    (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            }

            _waterShader = RaylibNativeResources.LoadShader(Path.Combine(baseDir, "water.vs"), Path.Combine(baseDir, "water.fs"));
            if (_waterShader.id == 0) throw new InvalidOperationException("Failed to load water shader (shader.id == 0).");
            _waterMaterial = RaylibNativeResources.LoadMaterialDefault();
            _waterMaterial.shader = _waterShader;

            _locWaterLightPos = Rl.GetShaderLocation(_waterShader, "uLightPos");
            _locWaterViewPos = Rl.GetShaderLocation(_waterShader, "uViewPos");
            _locWaterAmbient = Rl.GetShaderLocation(_waterShader, "uAmbient");
            _locWaterIntensity = Rl.GetShaderLocation(_waterShader, "uLightIntensity");
            _locWaterSampleReflection = Rl.GetShaderLocation(_waterShader, "uSampleReflection");
            _locWaterUseDudv = Rl.GetShaderLocation(_waterShader, "uUseDudv");
            _locWaterMoveFactor = Rl.GetShaderLocation(_waterShader, "uMoveFactor");
            _locWaterWaveStrength = Rl.GetShaderLocation(_waterShader, "uWaveStrength");
            int locWaterReflection = Rl.GetShaderLocation(_waterShader, "texture0");
            int locWaterRefraction = Rl.GetShaderLocation(_waterShader, "texture1");
            int locWaterDudv = Rl.GetShaderLocation(_waterShader, "texture2");
            if (_locWaterSampleReflection < 0 ||
                _locWaterUseDudv < 0 ||
                _locWaterMoveFactor < 0 ||
                _locWaterWaveStrength < 0 ||
                locWaterReflection < 0 ||
                locWaterRefraction < 0 ||
                locWaterDudv < 0)
            {
                throw new InvalidOperationException(
                    "Water shader is missing reflective uniforms/samplers (uSampleReflection/uUseDudv/uMoveFactor/uWaveStrength/texture0/texture1/texture2).");
            }

            _waterShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locWaterReflection;
            _waterShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_METALNESS] = locWaterRefraction;
            _waterShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_NORMAL] = locWaterDudv;

            _initialized = true;
            ClearReflectiveWater();
            ApplyTerrainShadow();
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
                    $"{nameof(RaylibTerrainRenderer)} requires {nameof(ApplyFrameLighting)} before Render.");
            }

            RaylibFrameLighting lighting = _frameLighting;
            lighting.Apply(_terrainShader, in _terrainLightingLocs);
            Vector3 viewPos = camera.position;
            lighting.ApplyViewPosition(_terrainShader, in _terrainLightingLocs, viewPos);
            ApplyTerrainShadow();

            Vector3 lightPos = lighting.FarLightPosition();
            float ambient = lighting.AmbientRgba.W;
            float intensity = lighting.LightIntensity;
            Rl.SetShaderValue(_waterShader, _locWaterLightPos, &lightPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterViewPos, &viewPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterIntensity, &intensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        private ref ChunkGpu GetOrCreateChunk(ITerrainChunkMeshSource source, int chunkX, int chunkY, bool simplifiedCliffs)
        {
            long key = source.GetChunkKey(chunkX, chunkY);
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
                    return ref CollectionsMarshal.GetValueRefOrNullRef(_chunks, key);
                }
            }

            long buildStart = Stopwatch.GetTimestamp();
            source.BuildChunk(chunkX, chunkY, simplifiedCliffs, HeightScale, _meshData);
            ChunkGpu gpu = new ChunkGpu();
            gpu.SimplifiedCliffs = simplifiedCliffs;
            gpu.TerrainMesh = CreateMesh(_meshData.Terrain);
            gpu.WaterMesh = _meshData.Water.VertexCount > 0 ? CreateMesh(_meshData.Water) : default;
            ComputeTerrainAabbMeters(
                _meshData.Terrain,
                out gpu.MinX,
                out gpu.MinY,
                out gpu.MinZ,
                out gpu.MaxX,
                out gpu.MaxY,
                out gpu.MaxZ);
            gpu.LastUsedFrame = _frameIndex;
            BuiltChunkCountLastFrame++;
            ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - buildStart) * 1000.0 / Stopwatch.Frequency;
            _chunks[key] = gpu;
            return ref CollectionsMarshal.GetValueRefOrNullRef(_chunks, key);
        }

        private void ApplyTerrainShadow()
        {
            _terrainShadowLocs.ApplyUniforms(_terrainShader, _frameShadow, _frameShadowTexelWorld);
            RaylibShadowSampling.BindTexture(ref _terrainMaterial, _frameShadow);
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

            RaylibNativeResources.UploadMesh(ref mesh, false);
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
                // Detach pass-owned RT textures before material teardown (avoids double-free).
                ClearReflectiveWater();
                DetachWaterPassTextures();
                RaylibShadowSampling.ClearTexture(ref _terrainMaterial);
            }

            if (_oceanPlaneReady)
            {
                RaylibNativeResources.UnloadMesh(_oceanPlaneMesh);
                _oceanPlaneMesh = default;
                _oceanPlaneReady = false;
            }

            if (_initialized)
            {
                _terrainMaterial.shader = default;
                RaylibNativeResources.UnloadMaterial(_terrainMaterial);
                RaylibNativeResources.UnloadShader(_terrainShader);
                _waterMaterial.shader = default;
                RaylibNativeResources.UnloadMaterial(_waterMaterial);
                RaylibNativeResources.UnloadShader(_waterShader);
                _initialized = false;
            }
        }

        private struct ChunkGpu : IDisposable
        {
            public Mesh TerrainMesh;
            public Mesh WaterMesh;
            public int LastUsedFrame;
            public bool SimplifiedCliffs;
            public float MinX;
            public float MinY;
            public float MinZ;
            public float MaxX;
            public float MaxY;
            public float MaxZ;

            public void Dispose()
            {
                if (TerrainMesh.vertexCount > 0) RaylibNativeResources.UnloadMesh(TerrainMesh);
                if (WaterMesh.vertexCount > 0) RaylibNativeResources.UnloadMesh(WaterMesh);
                TerrainMesh = default;
                WaterMesh = default;
            }
        }

        internal static void ComputeTerrainAabbMeters(
            ChunkMeshWriteBuffer terrain,
            out float minX,
            out float minY,
            out float minZ,
            out float maxX,
            out float maxY,
            out float maxZ)
        {
            minX = minY = minZ = float.PositiveInfinity;
            maxX = maxY = maxZ = float.NegativeInfinity;
            ReadOnlySpan<float> vertices = terrain.Vertices.AsSpan(0, terrain.VertexCount * 3);
            for (int i = 0; i < vertices.Length; i += 3)
            {
                float x = vertices[i];
                float y = vertices[i + 1];
                float z = vertices[i + 2];
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (z < minZ) minZ = z;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;
            }
        }

        public int DrawMeshesOverlappingAabbMeters(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            Material material)
        {
            if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(minZ) ||
                !float.IsFinite(maxX) || !float.IsFinite(maxY) || !float.IsFinite(maxZ))
            {
                throw new ArgumentException(
                    $"{nameof(RaylibTerrainRenderer)}.{nameof(DrawMeshesOverlappingAabbMeters)} requires finite AABB bounds.");
            }

            if (minX > maxX || minY > maxY || minZ > maxZ)
            {
                throw new ArgumentException(
                    $"{nameof(RaylibTerrainRenderer)}.{nameof(DrawMeshesOverlappingAabbMeters)} AABB min must be <= max.");
            }

            EnsureShadersInitialized();
            if (_chunks.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibTerrainRenderer)} has no cached terrain meshes for projected Decals. Render the VertexMap terrain before drawing Decals.");
            }

            int drawn = 0;
            RaylibMatrix identity = RaylibMatrix.Identity;
            foreach (ChunkGpu gpu in _chunks.Values)
            {
                if (gpu.TerrainMesh.vertexCount == 0)
                {
                    continue;
                }

                if (gpu.MaxX < minX || gpu.MinX > maxX ||
                    gpu.MaxY < minY || gpu.MinY > maxY ||
                    gpu.MaxZ < minZ || gpu.MinZ > maxZ)
                {
                    continue;
                }

                Rl.rlDisableBackfaceCulling();
                Rl.DrawMesh(gpu.TerrainMesh, material, identity);
                Rl.rlEnableBackfaceCulling();
                drawn++;
            }

            return drawn;
        }

        public void BindStampHeightSampleSource(IVisualHeightmap heightmap)
        {
            _stampHeightSampleSource = heightmap ?? throw new ArgumentNullException(nameof(heightmap));
        }

        public Vector3 FitYawedStampProjectorCenter(
            in Vector3 stampCenter,
            float yawRad,
            in Vector2 stampSizeMeters,
            int stableId)
        {
            IVisualHeightmap heightmap = _stampHeightSampleSource
                ?? throw new InvalidOperationException(
                    $"{nameof(RaylibTerrainRenderer)} Decal stableId={stableId} has no stamp height sample source. Call {nameof(BindStampHeightSampleSource)} before projecting Decals.");

            return RaylibDecalStampFit.FitCenter(
                in stampCenter,
                yawRad,
                in stampSizeMeters,
                stableId,
                heightmap,
                nameof(RaylibTerrainRenderer));
        }
    }
}
