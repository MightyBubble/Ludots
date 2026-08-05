using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Terrain;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed unsafe class RaylibVisualHeightmapRenderer : IDisposable
    {
        private const int OverviewTextureMinLongEdgePixels = 1024;
        private const int OverviewTextureMaxLongEdgePixels = 3072;
        private const int OverviewTextureScreenScale = 2;

        private readonly Dictionary<long, ChunkGpu> _chunks = new(1024);
        private readonly List<long> _evictKeys = new(256);

        private OverviewGpu _overview;
        private bool _overviewLoaded;
        private Task<OverviewCpuData>? _overviewBuildTask;
        private OverviewKey _overviewBuildKey;
        private bool _overviewBuildInFlight;
        private bool _overviewActive;

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
        private int _locTerrainUseTexture;
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
        private bool _initialized;
        private int _frameIndex;
        private IVisualHeightmapRenderSource? _heightRangeSource;
        private int _heightRangeRevision = int.MinValue;
        private float _heightRangeMinCm;
        private float _heightRangeMaxCm;
        private RaylibRenderEnvironmentConfig _environmentConfig = RaylibRenderEnvironmentConfig.CreateDefault();

        public int DrawnChunkCountLastFrame { get; private set; }

        public int BuiltChunkCountLastFrame { get; private set; }

        public int MissingChunkCountLastFrame { get; private set; }

        public int TerrainVertexCountLastFrame { get; private set; }

        public int WaterVertexCountLastFrame { get; private set; }

        public double ChunkBuildMsLastFrame { get; private set; }

        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadiusCm { get; set; } = 120_000f;

        public int OverviewMaxVertices { get; set; } = 60_000;

        public float OverviewActivationMultiplier { get; set; } = 2.0f;

        public float OverviewSwitchHysteresis { get; set; } = 0.18f;

        public RaylibRenderEnvironmentConfig EnvironmentConfig
        {
            get => _environmentConfig;
            set => _environmentConfig = value?.NormalizeAndValidate() ?? throw new ArgumentNullException(nameof(value));
        }

        public void Render(IVisualHeightmapRenderSource source, in Camera3D camera, double timeSeconds)
        {
            if (source == null)
            {
                return;
            }

            EnsureInitialized();
            UpdateUniforms(camera, timeSeconds);
            ResolveSourceHeightRange(source, out float minHeightCm, out float maxHeightCm);
            VisualHeightmapRenderProfile renderProfile = source.RenderProfile.NormalizeAndValidate();
            float effectiveSeaLevelCm = ResolveEffectiveSeaLevelCm(renderProfile, minHeightCm);

            _frameIndex++;
            DrawnChunkCountLastFrame = 0;
            BuiltChunkCountLastFrame = 0;
            MissingChunkCountLastFrame = 0;
            TerrainVertexCountLastFrame = 0;
            WaterVertexCountLastFrame = 0;
            ChunkBuildMsLastFrame = 0d;

            float aspect = MathF.Max(0.001f, Rl.GetScreenWidth() / (float)Math.Max(1, Rl.GetScreenHeight()));
            if (ResolveOverviewActive(source, camera, aspect))
            {
                long overviewStart = Stopwatch.GetTimestamp();
                if (TryGetOrCreateOverview(
                    source,
                    renderProfile,
                    minHeightCm,
                    maxHeightCm,
                    effectiveSeaLevelCm,
                    out OverviewGpu overview))
                {
                    ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - overviewStart) * 1000d / Stopwatch.Frequency;
                    RaylibMatrix identity = RaylibMatrix.Identity;
                    SetTerrainTextureMode(overview.Texture, true);
                    try
                    {
                        Rl.rlDisableBackfaceCulling();
                        Rl.DrawMesh(overview.Mesh, _terrainMaterial, identity);
                        if (overview.WaterMesh.vertexCount > 0)
                        {
                            Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
                            Rl.DrawMesh(overview.WaterMesh, _waterMaterial, identity);
                            Rl.EndBlendMode();
                            WaterVertexCountLastFrame += overview.WaterMesh.vertexCount;
                        }

                        Rl.rlEnableBackfaceCulling();
                    }
                    finally
                    {
                        Rl.rlEnableBackfaceCulling();
                        SetTerrainTextureMode(default, false);
                    }

                    DrawnChunkCountLastFrame = 1;
                    TerrainVertexCountLastFrame = overview.Mesh.vertexCount;
                    EvictUnusedChunks(30);
                    return;
                }

                ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - overviewStart) * 1000d / Stopwatch.Frequency;
            }

            float chunkWidthCm = source.Bounds.Width / (float)Math.Max(1, source.ChunkColumns);
            float chunkHeightCm = source.Bounds.Height / (float)Math.Max(1, source.ChunkRows);
            float visibleRadiusCm = MathF.Max(
                VisibleRadiusCm,
                MathF.Max(chunkWidthCm, chunkHeightCm) * 1.25f);
            int minChunkX = ResolveChunkIndex((camera.target.X * 100f) - visibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int maxChunkX = ResolveChunkIndex((camera.target.X * 100f) + visibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int minChunkY = ResolveChunkIndex((camera.target.Z * 100f) - visibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
            int maxChunkY = ResolveChunkIndex((camera.target.Z * 100f) + visibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);

            SetTerrainTextureMode(default, false);
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    if (!source.TryGetChunk(x, y, out VisualHeightmapRenderChunk chunk))
                    {
                        MissingChunkCountLastFrame++;
                        continue;
                    }

                    ref ChunkGpu gpu = ref GetOrCreateChunk(
                        in chunk,
                        source.Revision,
                        minHeightCm,
                        maxHeightCm,
                        renderProfile,
                        effectiveSeaLevelCm);
                    gpu.LastUsedFrame = _frameIndex;
                    RaylibMatrix identity = RaylibMatrix.Identity;
                    Rl.rlDisableBackfaceCulling();
                    Rl.DrawMesh(gpu.Mesh, _terrainMaterial, identity);
                    if (gpu.WaterMesh.vertexCount > 0)
                    {
                        Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
                        Rl.DrawMesh(gpu.WaterMesh, _waterMaterial, identity);
                        Rl.EndBlendMode();
                        WaterVertexCountLastFrame += gpu.WaterMesh.vertexCount;
                    }
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
            int locMapAlbedo = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "texture0", "visual heightmap terrain");
            int locMvp = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "mvp", "visual heightmap terrain");
            int locMatModel = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "matModel", "visual heightmap terrain");
            int locVertexPosition = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexPosition", "visual heightmap terrain");
            int locVertexNormal = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexNormal", "visual heightmap terrain");
            int locVertexColor = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexColor", "visual heightmap terrain");
            int locVertexTexCoord = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexTexCoord", "visual heightmap terrain");
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD02] = -1;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locVertexNormal;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TANGENT] = -1;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = locVertexColor;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locMatModel;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
            _locTerrainViewPos = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uViewPos", "visual heightmap terrain");
            _locTerrainSunDirection = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uSunDirection", "visual heightmap terrain");
            _locTerrainSunColor = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uSunColor", "visual heightmap terrain");
            _locTerrainAmbientColor = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uAmbientColor", "visual heightmap terrain");
            _locTerrainAmbient = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uAmbient", "visual heightmap terrain");
            _locTerrainIntensity = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uLightIntensity", "visual heightmap terrain");
            _locTerrainFogColor = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uFogColor", "visual heightmap terrain");
            _locTerrainFogNear = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uFogNear", "visual heightmap terrain");
            _locTerrainFogFar = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uFogFar", "visual heightmap terrain");
            _locTerrainFogDensity = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uFogDensity", "visual heightmap terrain");
            _locTerrainUseTexture = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uUseTexture", "visual heightmap terrain");

            _waterShader = Rl.LoadShader(Path.Combine(baseDir, "water.vs"), Path.Combine(baseDir, "water.fs"));
            if (_waterShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load visual heightmap water shader (shader.id == 0).");
            }

            _waterMaterial = Rl.LoadMaterialDefault();
            _waterMaterial.shader = _waterShader;
            _locWaterViewPos = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uViewPos", "visual heightmap water");
            _locWaterSunDirection = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uSunDirection", "visual heightmap water");
            _locWaterSunColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uSunColor", "visual heightmap water");
            _locWaterAmbientColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uAmbientColor", "visual heightmap water");
            _locWaterAmbient = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uAmbient", "visual heightmap water");
            _locWaterIntensity = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uLightIntensity", "visual heightmap water");
            _locWaterFogColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFogColor", "visual heightmap water");
            _locWaterFogNear = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFogNear", "visual heightmap water");
            _locWaterFogFar = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFogFar", "visual heightmap water");
            _locWaterFogDensity = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFogDensity", "visual heightmap water");
            _locWaterTime = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uTime", "visual heightmap water");
            _locWaterShallowColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaterShallowColor", "visual heightmap water");
            _locWaterDeepColor = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaterDeepColor", "visual heightmap water");
            _locWaterWaveAmplitude = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaveAmplitude", "visual heightmap water");
            _locWaterWaveFrequency = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaveFrequency", "visual heightmap water");
            _locWaterWaveSpeed = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uWaveSpeed", "visual heightmap water");
            _locWaterFresnelStrength = RaylibShaderBindingGuard.RequireUniform(_waterShader, "uFresnelStrength", "visual heightmap water");
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

        private void SetTerrainTextureMode(Texture2D texture, bool useTexture)
        {
            int enabled = useTexture ? 1 : 0;
            Rl.SetShaderValue(_terrainShader, _locTerrainUseTexture, &enabled, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            if (!useTexture)
            {
                return;
            }

            int albedoIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO;
            _terrainMaterial.maps[albedoIndex].texture = texture;
            _terrainMaterial.maps[albedoIndex].color = Color.WHITE;
        }

        private ref ChunkGpu GetOrCreateChunk(
            in VisualHeightmapRenderChunk chunk,
            int sourceRevision,
            float minHeightCm,
            float maxHeightCm,
            VisualHeightmapRenderProfile renderProfile,
            float effectiveSeaLevelCm)
        {
            long key = GraphChunkKey.Pack(chunk.ChunkX, chunk.ChunkY);
            if (_chunks.TryGetValue(key, out ChunkGpu existing))
            {
                if (existing.Revision == chunk.Revision &&
                    existing.SourceRevision == sourceRevision &&
                    existing.WaterEnabled == renderProfile.WaterEnabled &&
                    MathF.Abs(existing.SeaLevelCm - renderProfile.SeaLevelCm) <= 0.001f &&
                    MathF.Abs(existing.DisplayHeightScale - renderProfile.DisplayHeightScale) <= 0.0001f &&
                    MathF.Abs(existing.ColorContrast - renderProfile.ColorContrast) <= 0.0001f)
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
                Mesh = CreateChunkMesh(
                    in chunk,
                    minHeightCm,
                    maxHeightCm,
                    effectiveSeaLevelCm,
                    renderProfile.DisplayHeightScale,
                    renderProfile.ColorContrast),
                WaterMesh = renderProfile.WaterEnabled
                    ? CreateWaterMesh(in chunk, renderProfile.SeaLevelCm, renderProfile.DisplayHeightScale)
                    : default,
                Revision = chunk.Revision,
                SourceRevision = sourceRevision,
                WaterEnabled = renderProfile.WaterEnabled,
                SeaLevelCm = renderProfile.SeaLevelCm,
                DisplayHeightScale = renderProfile.DisplayHeightScale,
                ColorContrast = renderProfile.ColorContrast,
                LastUsedFrame = _frameIndex,
            };
            BuiltChunkCountLastFrame++;
            ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - buildStart) * 1000d / Stopwatch.Frequency;
            _chunks[key] = gpu;
            return ref _chunks.GetValueRefOrNullRef(key);
        }

        private static Mesh CreateChunkMesh(
            in VisualHeightmapRenderChunk chunk,
            float minHeightCm,
            float maxHeightCm,
            float effectiveSeaLevelCm,
            float displayHeightScale,
            float colorContrast)
        {
            int sampleStride = ResolveChunkSampleStride(chunk.SampleColumns, chunk.SampleRows);
            int columns = ResolveChunkSampleAxisPointCount(chunk.SampleColumns, sampleStride);
            int rows = ResolveChunkSampleAxisPointCount(chunk.SampleRows, sampleStride);
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
            int uvFloatCount = vertexCount * 2;
            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.texcoords = (float*)Rl.MemAlloc(sizeof(float) * uvFloatCount);
            mesh.colors = (byte*)Rl.MemAlloc(sizeof(byte) * colorByteCount);
            mesh.indices = (ushort*)Rl.MemAlloc(sizeof(ushort) * indexCount);

            float stepXCm = chunk.SampleStepXCm;
            float stepYCm = chunk.SampleStepYCm;
            for (int y = 0; y < rows; y++)
            {
                int sourceY = ResolveChunkSourceSampleIndex(y, chunk.SampleRows, sampleStride);
                for (int x = 0; x < columns; x++)
                {
                    int sourceX = ResolveChunkSourceSampleIndex(x, chunk.SampleColumns, sampleStride);
                    int vertex = (y * columns) + x;
                    float worldXCm = chunk.Bounds.Left + (sourceX * stepXCm);
                    float worldYCm = chunk.Bounds.Top + (sourceY * stepYCm);
                    float heightCm = ReadRequiredHeightCm(in chunk, sourceX, sourceY);
                    Vector3 normal = ComputeNormal(in chunk, sourceX, sourceY, stepXCm, stepYCm, displayHeightScale);
                    int f = vertex * 3;
                    mesh.vertices[f + 0] = worldXCm * 0.01f;
                    mesh.vertices[f + 1] = heightCm * displayHeightScale * 0.01f;
                    mesh.vertices[f + 2] = worldYCm * 0.01f;
                    mesh.normals[f + 0] = normal.X;
                    mesh.normals[f + 1] = normal.Y;
                    mesh.normals[f + 2] = normal.Z;

                    int uv = vertex * 2;
                    mesh.texcoords[uv + 0] = columns > 1 ? x / (float)(columns - 1) : 0f;
                    mesh.texcoords[uv + 1] = rows > 1 ? y / (float)(rows - 1) : 0f;

                    int c = vertex * 4;
                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    ResolveTerrainColor(
                        heightCm,
                        minHeightCm,
                        maxHeightCm,
                        effectiveSeaLevelCm,
                        slope,
                        colorContrast,
                        out byte red,
                        out byte green,
                        out byte blue);
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

        internal static int ResolveChunkSampleStride(int sampleColumns, int sampleRows)
        {
            if (sampleColumns < 2) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows < 2) throw new ArgumentOutOfRangeException(nameof(sampleRows));

            int stride = 1;
            while (checked(ResolveChunkSampleAxisPointCount(sampleColumns, stride) * ResolveChunkSampleAxisPointCount(sampleRows, stride)) > ushort.MaxValue)
            {
                stride++;
            }

            return stride;
        }

        internal static int ResolveChunkSampleAxisPointCount(int sampleCount, int stride)
        {
            if (sampleCount < 2) throw new ArgumentOutOfRangeException(nameof(sampleCount));
            if (stride <= 0) throw new ArgumentOutOfRangeException(nameof(stride));

            return ((sampleCount - 2) / stride) + 2;
        }

        internal static int ResolveChunkSourceSampleIndex(int pointIndex, int sampleCount, int stride)
        {
            int pointCount = ResolveChunkSampleAxisPointCount(sampleCount, stride);
            if ((uint)pointIndex >= (uint)pointCount) throw new ArgumentOutOfRangeException(nameof(pointIndex));

            return pointIndex == pointCount - 1
                ? sampleCount - 1
                : pointIndex * stride;
        }

        private void ResolveSourceHeightRange(IVisualHeightmapRenderSource source, out float minHeightCm, out float maxHeightCm)
        {
            if (ReferenceEquals(_heightRangeSource, source) && _heightRangeRevision == source.Revision)
            {
                minHeightCm = _heightRangeMinCm;
                maxHeightCm = _heightRangeMaxCm;
                return;
            }

            minHeightCm = float.PositiveInfinity;
            maxHeightCm = float.NegativeInfinity;
            int validChunkCount = 0;
            for (int chunkY = 0; chunkY < source.ChunkRows; chunkY++)
            {
                for (int chunkX = 0; chunkX < source.ChunkColumns; chunkX++)
                {
                    if (!source.TryGetChunk(chunkX, chunkY, out VisualHeightmapRenderChunk chunk))
                    {
                        continue;
                    }

                    validChunkCount++;
                    ResolveChunkHeightRange(in chunk, ref minHeightCm, ref maxHeightCm);
                }
            }

            if (validChunkCount <= 0 || !float.IsFinite(minHeightCm) || !float.IsFinite(maxHeightCm))
            {
                throw new InvalidOperationException(
                    $"Raylib visual heightmap renderer could not resolve a finite height range for source revision={source.Revision} chunks={source.ChunkColumns}x{source.ChunkRows}.");
            }

            _heightRangeSource = source;
            _heightRangeRevision = source.Revision;
            _heightRangeMinCm = minHeightCm;
            _heightRangeMaxCm = maxHeightCm;
        }

        private static void ResolveChunkHeightRange(in VisualHeightmapRenderChunk chunk, ref float minHeightCm, ref float maxHeightCm)
        {
            int columns = chunk.SampleColumns;
            int rows = chunk.SampleRows;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (!chunk.TryReadHeightCm(x, y, out float heightCm))
                    {
                        throw new InvalidOperationException(
                            $"Raylib visual heightmap renderer failed to read height sample chunk=({chunk.ChunkX},{chunk.ChunkY}) sample=({x},{y}).");
                    }

                    if (!float.IsFinite(heightCm))
                    {
                        throw new InvalidOperationException(
                            $"Raylib visual heightmap renderer read non-finite height sample chunk=({chunk.ChunkX},{chunk.ChunkY}) sample=({x},{y}) heightCm={heightCm}.");
                    }

                    minHeightCm = MathF.Min(minHeightCm, heightCm);
                    maxHeightCm = MathF.Max(maxHeightCm, heightCm);
                }
            }
        }

        private static Mesh CreateWaterMesh(in VisualHeightmapRenderChunk chunk, float seaLevelCm, float displayHeightScale)
        {
            int sampleStride = ResolveChunkSampleStride(chunk.SampleColumns, chunk.SampleRows);
            int columns = ResolveChunkSampleAxisPointCount(chunk.SampleColumns, sampleStride);
            int rows = ResolveChunkSampleAxisPointCount(chunk.SampleRows, sampleStride);
            int waterCellCount = CountChunkWaterCells(in chunk, seaLevelCm, columns, rows, sampleStride);
            if (waterCellCount <= 0)
            {
                return default;
            }

            int vertexCount = checked(columns * rows);
            int indexCount = checked(waterCellCount * 6);
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

            float y = seaLevelCm * displayHeightScale * 0.01f;
            for (int row = 0; row < rows; row++)
            {
                int sourceY = ResolveChunkSourceSampleIndex(row, chunk.SampleRows, sampleStride);
                float worldYCm = chunk.Bounds.Top + (sourceY * chunk.SampleStepYCm);
                for (int column = 0; column < columns; column++)
                {
                    int sourceX = ResolveChunkSourceSampleIndex(column, chunk.SampleColumns, sampleStride);
                    float worldXCm = chunk.Bounds.Left + (sourceX * chunk.SampleStepXCm);
                    int vertex = (row * columns) + column;
                    WriteWaterVertex(mesh, vertex, worldXCm * 0.01f, y, worldYCm * 0.01f);
                }
            }

            int cursor = 0;
            for (int row = 0; row < rows - 1; row++)
            {
                int sourceY0 = ResolveChunkSourceSampleIndex(row, chunk.SampleRows, sampleStride);
                int sourceY1 = ResolveChunkSourceSampleIndex(row + 1, chunk.SampleRows, sampleStride);
                for (int column = 0; column < columns - 1; column++)
                {
                    int sourceX0 = ResolveChunkSourceSampleIndex(column, chunk.SampleColumns, sampleStride);
                    int sourceX1 = ResolveChunkSourceSampleIndex(column + 1, chunk.SampleColumns, sampleStride);
                    if (!ChunkCellHasWater(in chunk, sourceX0, sourceY0, sourceX1, sourceY1, seaLevelCm))
                    {
                        continue;
                    }

                    int p00 = (row * columns) + column;
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

        private static void WriteWaterVertex(Mesh mesh, int vertexIndex, float x, float y, float z)
        {
            int f = vertexIndex * 3;
            mesh.vertices[f + 0] = x;
            mesh.vertices[f + 1] = y;
            mesh.vertices[f + 2] = z;
            mesh.normals[f + 0] = 0f;
            mesh.normals[f + 1] = 1f;
            mesh.normals[f + 2] = 0f;

            int c = vertexIndex * 4;
            mesh.colors[c + 0] = 255;
            mesh.colors[c + 1] = 255;
            mesh.colors[c + 2] = 255;
            mesh.colors[c + 3] = 142;
        }

        private static int CountChunkWaterCells(
            in VisualHeightmapRenderChunk chunk,
            float seaLevelCm,
            int columns,
            int rows,
            int sampleStride)
        {
            int count = 0;
            for (int row = 0; row < rows - 1; row++)
            {
                int sourceY0 = ResolveChunkSourceSampleIndex(row, chunk.SampleRows, sampleStride);
                int sourceY1 = ResolveChunkSourceSampleIndex(row + 1, chunk.SampleRows, sampleStride);
                for (int column = 0; column < columns - 1; column++)
                {
                    int sourceX0 = ResolveChunkSourceSampleIndex(column, chunk.SampleColumns, sampleStride);
                    int sourceX1 = ResolveChunkSourceSampleIndex(column + 1, chunk.SampleColumns, sampleStride);
                    if (ChunkCellHasWater(in chunk, sourceX0, sourceY0, sourceX1, sourceY1, seaLevelCm))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool ChunkCellHasWater(
            in VisualHeightmapRenderChunk chunk,
            int x0,
            int y0,
            int x1,
            int y1,
            float seaLevelCm)
        {
            return ReadRequiredHeightCm(in chunk, x0, y0) <= seaLevelCm ||
                   ReadRequiredHeightCm(in chunk, x1, y0) <= seaLevelCm ||
                   ReadRequiredHeightCm(in chunk, x0, y1) <= seaLevelCm ||
                   ReadRequiredHeightCm(in chunk, x1, y1) <= seaLevelCm;
        }

        private static void ResolveTerrainColor(
            float heightCm,
            float minHeightCm,
            float maxHeightCm,
            float seaLevelCm,
            float slope,
            float colorContrast,
            out byte red,
            out byte green,
            out byte blue)
        {
            Vector3 color = VisualHeightmapColorRamp.ResolveColorRanged(
                heightCm,
                slope,
                minHeightCm,
                maxHeightCm,
                seaLevelCm,
                colorContrast);
            red = ClampToByte(color.X * 255f);
            green = ClampToByte(color.Y * 255f);
            blue = ClampToByte(color.Z * 255f);
        }

        private bool TryGetOrCreateOverview(
            IVisualHeightmapRenderSource source,
            VisualHeightmapRenderProfile renderProfile,
            float minHeightCm,
            float maxHeightCm,
            float effectiveSeaLevelCm,
            out OverviewGpu overview)
        {
            int maxVertices = Math.Clamp(OverviewMaxVertices, 4, ushort.MaxValue);
            ResolveOverviewTextureSize(
                source.Bounds,
                Rl.GetScreenWidth(),
                Rl.GetScreenHeight(),
                out int textureWidth,
                out int textureHeight);

            var key = new OverviewKey(
                source.Bounds,
                source.ChunkColumns,
                source.ChunkRows,
                source.SamplesPerChunkColumn,
                source.SamplesPerChunkRow,
                source.DefaultLayerIndex,
                source.Revision,
                maxVertices,
                renderProfile.WaterEnabled,
                renderProfile.SeaLevelCm,
                effectiveSeaLevelCm,
                renderProfile.DisplayHeightScale,
                renderProfile.ColorContrast,
                minHeightCm,
                maxHeightCm,
                textureWidth,
                textureHeight);

            if (_overviewLoaded && _overview.Key == key)
            {
                overview = _overview;
                return true;
            }

            PumpOverviewBuild(source, maxVertices, textureWidth, textureHeight, renderProfile, minHeightCm, maxHeightCm, effectiveSeaLevelCm, in key);
            if (_overviewLoaded)
            {
                overview = _overview;
                return true;
            }

            overview = default;
            return false;
        }

        private void PumpOverviewBuild(
            IVisualHeightmapRenderSource source,
            int maxVertices,
            int textureWidth,
            int textureHeight,
            VisualHeightmapRenderProfile renderProfile,
            float minHeightCm,
            float maxHeightCm,
            float effectiveSeaLevelCm,
            in OverviewKey key)
        {
            if (_overviewBuildTask != null && _overviewBuildTask.IsCompleted)
            {
                Task<OverviewCpuData> completed = _overviewBuildTask;
                _overviewBuildTask = null;
                _overviewBuildInFlight = false;

                if (completed.IsFaulted)
                {
                    throw new InvalidOperationException("Raylib visual heightmap overview build failed.", completed.Exception);
                }

                OverviewCpuData cpu = completed.Result;
                if (cpu.Key == key)
                {
                    if (_overviewLoaded)
                    {
                        _overview.Dispose();
                        _overview = default;
                        _overviewLoaded = false;
                    }

                    if (TryUploadOverview(cpu, out OverviewGpu uploaded))
                    {
                        _overview = uploaded;
                        _overviewLoaded = true;
                        BuiltChunkCountLastFrame++;
                    }
                }
            }

            if (!_overviewBuildInFlight && (!_overviewLoaded || _overview.Key != key))
            {
                if (_overviewBuildKey != key || _overviewBuildTask == null)
                {
                    _overviewBuildKey = key;
                    _overviewBuildInFlight = true;
                    VisualHeightmapRenderProfile capturedProfile = renderProfile.Clone();
                    OverviewKey capturedKey = key;
                    _overviewBuildTask = Task.Run(() => BuildOverviewCpu(
                        source,
                        maxVertices,
                        textureWidth,
                        textureHeight,
                        capturedProfile,
                        minHeightCm,
                        maxHeightCm,
                        effectiveSeaLevelCm,
                        capturedKey));
                }
            }
        }

        private static OverviewCpuData BuildOverviewCpu(
            IVisualHeightmapRenderSource source,
            int maxVertices,
            int textureWidth,
            int textureHeight,
            VisualHeightmapRenderProfile renderProfile,
            float minHeightCm,
            float maxHeightCm,
            float effectiveSeaLevelCm,
            OverviewKey key)
        {
            BuildOverviewMeshesCpu(
                source,
                maxVertices,
                renderProfile,
                minHeightCm,
                maxHeightCm,
                effectiveSeaLevelCm,
                out OverviewMeshCpu terrainMesh,
                out OverviewMeshCpu waterMesh);
            OverviewTextureCpu texture = BuildOverviewTextureCpu(
                source,
                textureWidth,
                textureHeight,
                renderProfile,
                minHeightCm,
                maxHeightCm,
                effectiveSeaLevelCm);
            return new OverviewCpuData(key, terrainMesh, waterMesh, texture);
        }

        private static void BuildOverviewMeshesCpu(
            IVisualHeightmapRenderSource source,
            int maxVertices,
            VisualHeightmapRenderProfile renderProfile,
            float minHeightCm,
            float maxHeightCm,
            float effectiveSeaLevelCm,
            out OverviewMeshCpu terrainMesh,
            out OverviewMeshCpu waterMesh)
        {
            int stepChunks = ResolveOverviewStepChunks(source.ChunkColumns, source.ChunkRows, maxVertices);
            int columns = ResolveOverviewAxisPointCount(source.ChunkColumns, stepChunks);
            int rows = ResolveOverviewAxisPointCount(source.ChunkRows, stepChunks);
            int vertexCount = checked(columns * rows);
            if (vertexCount > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Raylib visual heightmap overview has {vertexCount} vertices, exceeding the platform mesh index limit.");
            }

            int indexCount = checked((columns - 1) * (rows - 1) * 6);
            var worldXCm = new float[columns];
            var worldYCm = new float[rows];
            var heightsCm = new float[vertexCount];

            for (int x = 0; x < columns; x++)
            {
                int boundaryX = ResolveOverviewBoundaryChunk(x, source.ChunkColumns, stepChunks);
                worldXCm[x] = source.Bounds.Left + (source.Bounds.Width * (boundaryX / (float)source.ChunkColumns));
            }

            for (int y = 0; y < rows; y++)
            {
                int boundaryY = ResolveOverviewBoundaryChunk(y, source.ChunkRows, stepChunks);
                worldYCm[y] = source.Bounds.Top + (source.Bounds.Height * (boundaryY / (float)source.ChunkRows));
            }

            for (int y = 0; y < rows; y++)
            {
                int boundaryY = ResolveOverviewBoundaryChunk(y, source.ChunkRows, stepChunks);
                for (int x = 0; x < columns; x++)
                {
                    int boundaryX = ResolveOverviewBoundaryChunk(x, source.ChunkColumns, stepChunks);
                    heightsCm[(y * columns) + x] = ReadOverviewHeightCm(source, boundaryX, boundaryY);
                }
            }

            float heightScale = renderProfile.DisplayHeightScale;
            int vertexFloatCount = vertexCount * 3;
            int colorByteCount = vertexCount * 4;
            int uvFloatCount = vertexCount * 2;
            float[] vertices = new float[vertexFloatCount];
            float[] normals = new float[vertexFloatCount];
            float[] texcoords = new float[uvFloatCount];
            byte[] colors = new byte[colorByteCount];
            ushort[] indices = new ushort[indexCount];

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int vertex = (y * columns) + x;
                    int f = vertex * 3;
                    float heightCm = heightsCm[vertex];
                    Vector3 normal = ComputeOverviewNormal(worldXCm, worldYCm, heightsCm, columns, rows, x, y, heightScale);
                    vertices[f + 0] = worldXCm[x] * 0.01f;
                    vertices[f + 1] = heightCm * heightScale * 0.01f;
                    vertices[f + 2] = worldYCm[y] * 0.01f;
                    normals[f + 0] = normal.X;
                    normals[f + 1] = normal.Y;
                    normals[f + 2] = normal.Z;

                    int uv = vertex * 2;
                    texcoords[uv + 0] = columns > 1 ? x / (float)(columns - 1) : 0f;
                    texcoords[uv + 1] = rows > 1 ? y / (float)(rows - 1) : 0f;

                    int c = vertex * 4;
                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    ResolveTerrainColor(
                        heightCm,
                        minHeightCm,
                        maxHeightCm,
                        effectiveSeaLevelCm,
                        slope,
                        renderProfile.ColorContrast,
                        out byte red,
                        out byte green,
                        out byte blue);
                    colors[c + 0] = red;
                    colors[c + 1] = green;
                    colors[c + 2] = blue;
                    colors[c + 3] = 255;
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
                    indices[cursor++] = checked((ushort)p00);
                    indices[cursor++] = checked((ushort)p01);
                    indices[cursor++] = checked((ushort)p10);
                    indices[cursor++] = checked((ushort)p11);
                    indices[cursor++] = checked((ushort)p10);
                    indices[cursor++] = checked((ushort)p01);
                }
            }

            terrainMesh = new OverviewMeshCpu(vertexCount, indexCount / 3, vertices, normals, texcoords, colors, indices);
            waterMesh = renderProfile.WaterEnabled
                ? BuildOverviewWaterMeshCpu(worldXCm, worldYCm, heightsCm, columns, rows, renderProfile.SeaLevelCm, heightScale)
                : default;
        }

        private static OverviewMeshCpu BuildOverviewWaterMeshCpu(
            float[] worldXCm,
            float[] worldYCm,
            float[] heightsCm,
            int columns,
            int rows,
            float seaLevelCm,
            float displayHeightScale)
        {
            int waterCellCount = CountOverviewWaterCells(heightsCm, columns, rows, seaLevelCm);
            if (waterCellCount <= 0)
            {
                return default;
            }

            int vertexCount = checked(columns * rows);
            int vertexFloatCount = vertexCount * 3;
            int colorByteCount = vertexCount * 4;
            int indexCount = checked(waterCellCount * 6);
            float[] vertices = new float[vertexFloatCount];
            float[] normals = new float[vertexFloatCount];
            float[] texcoords = new float[vertexCount * 2];
            byte[] colors = new byte[colorByteCount];
            ushort[] indices = new ushort[indexCount];
            float waterY = seaLevelCm * displayHeightScale * 0.01f;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int vertex = (y * columns) + x;
                    int f = vertex * 3;
                    vertices[f + 0] = worldXCm[x] * 0.01f;
                    vertices[f + 1] = waterY;
                    vertices[f + 2] = worldYCm[y] * 0.01f;
                    normals[f + 0] = 0f;
                    normals[f + 1] = 1f;
                    normals[f + 2] = 0f;

                    int uv = vertex * 2;
                    texcoords[uv + 0] = columns > 1 ? x / (float)(columns - 1) : 0f;
                    texcoords[uv + 1] = rows > 1 ? y / (float)(rows - 1) : 0f;

                    int c = vertex * 4;
                    colors[c + 0] = 255;
                    colors[c + 1] = 255;
                    colors[c + 2] = 255;
                    colors[c + 3] = 142;
                }
            }

            int cursor = 0;
            for (int y = 0; y < rows - 1; y++)
            {
                for (int x = 0; x < columns - 1; x++)
                {
                    if (!OverviewCellHasWater(heightsCm, columns, x, y, seaLevelCm))
                    {
                        continue;
                    }

                    int p00 = (y * columns) + x;
                    int p10 = p00 + 1;
                    int p01 = p00 + columns;
                    int p11 = p01 + 1;
                    indices[cursor++] = checked((ushort)p00);
                    indices[cursor++] = checked((ushort)p01);
                    indices[cursor++] = checked((ushort)p10);
                    indices[cursor++] = checked((ushort)p11);
                    indices[cursor++] = checked((ushort)p10);
                    indices[cursor++] = checked((ushort)p01);
                }
            }

            return new OverviewMeshCpu(vertexCount, indexCount / 3, vertices, normals, texcoords, colors, indices);
        }

        private static OverviewTextureCpu BuildOverviewTextureCpu(
            IVisualHeightmapRenderSource source,
            int textureWidth,
            int textureHeight,
            VisualHeightmapRenderProfile renderProfile,
            float minHeightCm,
            float maxHeightCm,
            float effectiveSeaLevelCm)
        {
            if (textureWidth <= 0) throw new ArgumentOutOfRangeException(nameof(textureWidth));
            if (textureHeight <= 0) throw new ArgumentOutOfRangeException(nameof(textureHeight));

            int sampleCount = checked(textureWidth * textureHeight);
            var heightsCm = new float[sampleCount];
            for (int y = 0; y < textureHeight; y++)
            {
                float sampleY = textureHeight > 1
                    ? y / (float)(textureHeight - 1)
                    : 0f;
                for (int x = 0; x < textureWidth; x++)
                {
                    float sampleX = textureWidth > 1
                        ? x / (float)(textureWidth - 1)
                        : 0f;
                    heightsCm[(y * textureWidth) + x] = SampleOverviewTextureHeightCm(source, sampleX, sampleY);
                }
            }

            float heightScale = renderProfile.DisplayHeightScale;
            byte[] pixels = new byte[checked(sampleCount * 4)];
            float stepXCm = source.Bounds.Width / (float)Math.Max(1, textureWidth - 1);
            float stepYCm = source.Bounds.Height / (float)Math.Max(1, textureHeight - 1);
            for (int y = 0; y < textureHeight; y++)
            {
                int top = Math.Max(0, y - 1);
                int bottom = Math.Min(textureHeight - 1, y + 1);
                for (int x = 0; x < textureWidth; x++)
                {
                    int left = Math.Max(0, x - 1);
                    int right = Math.Min(textureWidth - 1, x + 1);
                    int index = (y * textureWidth) + x;
                    float hLeft = heightsCm[(y * textureWidth) + left];
                    float hRight = heightsCm[(y * textureWidth) + right];
                    float hTop = heightsCm[(top * textureWidth) + x];
                    float hBottom = heightsCm[(bottom * textureWidth) + x];
                    float dx = MathF.Max(1f, (right - left) * stepXCm);
                    float dz = MathF.Max(1f, (bottom - top) * stepYCm);
                    Vector3 normal = Vector3.Normalize(new Vector3(
                        -((hRight - hLeft) * heightScale) / dx,
                        1f,
                        -((hBottom - hTop) * heightScale) / dz));
                    if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z))
                    {
                        normal = Vector3.UnitY;
                    }

                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    ResolveTerrainColor(
                        heightsCm[index],
                        minHeightCm,
                        maxHeightCm,
                        effectiveSeaLevelCm,
                        slope,
                        renderProfile.ColorContrast,
                        out byte red,
                        out byte green,
                        out byte blue);
                    int pixel = index * 4;
                    pixels[pixel + 0] = red;
                    pixels[pixel + 1] = green;
                    pixels[pixel + 2] = blue;
                    pixels[pixel + 3] = 255;
                }
            }

            return new OverviewTextureCpu(textureWidth, textureHeight, pixels);
        }

        private static float ReadOverviewHeightCm(
            IVisualHeightmapRenderSource source,
            int boundaryChunkX,
            int boundaryChunkY)
        {
            int chunkX = boundaryChunkX == 0 ? 0 : boundaryChunkX - 1;
            int chunkY = boundaryChunkY == 0 ? 0 : boundaryChunkY - 1;
            chunkX = Math.Clamp(chunkX, 0, source.ChunkColumns - 1);
            chunkY = Math.Clamp(chunkY, 0, source.ChunkRows - 1);
            if (!source.TryGetChunk(chunkX, chunkY, out VisualHeightmapRenderChunk chunk))
            {
                throw new InvalidOperationException(
                    $"Raylib visual heightmap overview could not read chunk=({chunkX},{chunkY}).");
            }

            int sampleX = boundaryChunkX == 0 ? 0 : chunk.SampleColumns - 1;
            int sampleY = boundaryChunkY == 0 ? 0 : chunk.SampleRows - 1;
            return ReadRequiredHeightCm(in chunk, sampleX, sampleY);
        }

        private static float SampleOverviewTextureHeightCm(
            IVisualHeightmapRenderSource source,
            float normalizedX,
            float normalizedY)
        {
            int sampleColumns = checked(source.ChunkColumns * (source.SamplesPerChunkColumn - 1) + 1);
            int sampleRows = checked(source.ChunkRows * (source.SamplesPerChunkRow - 1) + 1);
            float sampleX = Math.Clamp(normalizedX, 0f, 1f) * (sampleColumns - 1);
            float sampleY = Math.Clamp(normalizedY, 0f, 1f) * (sampleRows - 1);
            int x0 = Math.Clamp((int)MathF.Floor(sampleX), 0, sampleColumns - 1);
            int y0 = Math.Clamp((int)MathF.Floor(sampleY), 0, sampleRows - 1);
            int x1 = Math.Min(sampleColumns - 1, x0 + 1);
            int y1 = Math.Min(sampleRows - 1, y0 + 1);
            float tx = sampleX - x0;
            float ty = sampleY - y0;

            float h00 = ReadOverviewTextureSampleHeightCm(source, x0, y0, sampleColumns, sampleRows);
            float h10 = ReadOverviewTextureSampleHeightCm(source, x1, y0, sampleColumns, sampleRows);
            float h01 = ReadOverviewTextureSampleHeightCm(source, x0, y1, sampleColumns, sampleRows);
            float h11 = ReadOverviewTextureSampleHeightCm(source, x1, y1, sampleColumns, sampleRows);
            float hx0 = Lerp(h00, h10, tx);
            float hx1 = Lerp(h01, h11, tx);
            return Lerp(hx0, hx1, ty);
        }

        private static float ReadOverviewTextureSampleHeightCm(
            IVisualHeightmapRenderSource source,
            int globalX,
            int globalY,
            int sampleColumns,
            int sampleRows)
        {
            int stepX = source.SamplesPerChunkColumn - 1;
            int stepY = source.SamplesPerChunkRow - 1;
            int chunkX = globalX >= sampleColumns - 1 ? source.ChunkColumns - 1 : globalX / stepX;
            int chunkY = globalY >= sampleRows - 1 ? source.ChunkRows - 1 : globalY / stepY;
            int localX = globalX >= sampleColumns - 1 ? source.SamplesPerChunkColumn - 1 : globalX - (chunkX * stepX);
            int localY = globalY >= sampleRows - 1 ? source.SamplesPerChunkRow - 1 : globalY - (chunkY * stepY);
            if (!source.TryGetChunk(chunkX, chunkY, out VisualHeightmapRenderChunk chunk))
            {
                throw new InvalidOperationException(
                    $"Raylib visual heightmap overview texture could not read chunk=({chunkX},{chunkY}).");
            }

            return ReadRequiredHeightCm(in chunk, localX, localY);
        }

        private static int CountOverviewWaterCells(float[] heightsCm, int columns, int rows, float seaLevelCm)
        {
            int count = 0;
            for (int y = 0; y < rows - 1; y++)
            {
                for (int x = 0; x < columns - 1; x++)
                {
                    if (OverviewCellHasWater(heightsCm, columns, x, y, seaLevelCm))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool OverviewCellHasWater(float[] heightsCm, int columns, int x, int y, float seaLevelCm)
        {
            int p00 = (y * columns) + x;
            int p10 = p00 + 1;
            int p01 = p00 + columns;
            int p11 = p01 + 1;
            return heightsCm[p00] <= seaLevelCm ||
                   heightsCm[p10] <= seaLevelCm ||
                   heightsCm[p01] <= seaLevelCm ||
                   heightsCm[p11] <= seaLevelCm;
        }

        private bool ResolveOverviewActive(IVisualHeightmapRenderSource source, in Camera3D camera, float aspect)
        {
            float hysteresis = Math.Clamp(OverviewSwitchHysteresis, 0f, 0.9f);
            float multiplier = _overviewActive
                ? OverviewActivationMultiplier * (1f - hysteresis)
                : OverviewActivationMultiplier * (1f + hysteresis);
            _overviewActive = ShouldUseOverviewMesh(source, camera, aspect, VisibleRadiusCm, multiplier);
            return _overviewActive;
        }

        internal static bool ShouldUseOverviewMesh(
            IVisualHeightmapRenderSource source,
            in Camera3D camera,
            float aspect,
            float detailVisibleRadiusCm,
            float activationMultiplier)
        {
            if (source == null)
            {
                return false;
            }

            if (source.ChunkColumns <= 0 || source.ChunkRows <= 0)
            {
                return false;
            }

            float chunkWidthCm = source.Bounds.Width / (float)source.ChunkColumns;
            float chunkHeightCm = source.Bounds.Height / (float)source.ChunkRows;
            float detailRadiusCm = MathF.Max(
                MathF.Max(1f, detailVisibleRadiusCm),
                MathF.Max(chunkWidthCm, chunkHeightCm) * 1.25f);
            float activationRadiusCm = detailRadiusCm * MathF.Max(1f, activationMultiplier);
            return ComputeCameraFootprintRadiusCm(camera, aspect) > activationRadiusCm;
        }

        internal static float ComputeCameraFootprintRadiusCm(in Camera3D camera, float aspect)
        {
            float distanceMeters = Vector3.Distance(camera.position, camera.target);
            if (!float.IsFinite(distanceMeters) || distanceMeters <= 0f)
            {
                return 0f;
            }

            float fovyRad = camera.fovy * (MathF.PI / 180f);
            float clampedFovyRad = Math.Clamp(fovyRad, 0.001f, MathF.PI - 0.001f);
            float halfHeightMeters = distanceMeters * MathF.Tan(clampedFovyRad * 0.5f);
            float halfWidthMeters = halfHeightMeters * MathF.Max(0.001f, aspect);
            float radiusMeters = MathF.Sqrt((halfWidthMeters * halfWidthMeters) + (halfHeightMeters * halfHeightMeters));
            return radiusMeters * 100f;
        }

        internal static void ResolveOverviewTextureSize(
            WorldAabbCm bounds,
            int screenWidth,
            int screenHeight,
            out int textureWidth,
            out int textureHeight)
        {
            int screenLongEdge = Math.Max(1, Math.Max(screenWidth, screenHeight));
            int longEdge = Math.Clamp(
                checked(screenLongEdge * OverviewTextureScreenScale),
                OverviewTextureMinLongEdgePixels,
                OverviewTextureMaxLongEdgePixels);
            float aspect = MathF.Max(0.001f, bounds.Width / (float)Math.Max(1, bounds.Height));
            if (aspect >= 1f)
            {
                textureWidth = longEdge;
                textureHeight = Math.Clamp((int)MathF.Round(longEdge / aspect), 1, OverviewTextureMaxLongEdgePixels);
                return;
            }

            textureHeight = longEdge;
            textureWidth = Math.Clamp((int)MathF.Round(longEdge * aspect), 1, OverviewTextureMaxLongEdgePixels);
        }

        internal static int ResolveOverviewStepChunks(int chunkColumns, int chunkRows, int maxVertices)
        {
            if (chunkColumns <= 0) throw new ArgumentOutOfRangeException(nameof(chunkColumns));
            if (chunkRows <= 0) throw new ArgumentOutOfRangeException(nameof(chunkRows));

            int vertexLimit = Math.Clamp(maxVertices, 4, ushort.MaxValue);
            int step = 1;
            while (checked(ResolveOverviewAxisPointCount(chunkColumns, step) * ResolveOverviewAxisPointCount(chunkRows, step)) > vertexLimit)
            {
                step++;
            }

            return step;
        }

        internal static int ResolveOverviewAxisPointCount(int chunkCount, int stepChunks)
        {
            if (chunkCount <= 0) throw new ArgumentOutOfRangeException(nameof(chunkCount));
            if (stepChunks <= 0) throw new ArgumentOutOfRangeException(nameof(stepChunks));

            return ((chunkCount + stepChunks - 1) / stepChunks) + 1;
        }

        private static int ResolveOverviewBoundaryChunk(int pointIndex, int chunkCount, int stepChunks)
        {
            int pointCount = ResolveOverviewAxisPointCount(chunkCount, stepChunks);
            return pointIndex == pointCount - 1
                ? chunkCount
                : Math.Min(chunkCount, pointIndex * stepChunks);
        }

        private static Vector3 ComputeOverviewNormal(
            float[] worldXCm,
            float[] worldYCm,
            float[] heightsCm,
            int columns,
            int rows,
            int x,
            int y,
            float heightScale)
        {
            int left = Math.Max(0, x - 1);
            int right = Math.Min(columns - 1, x + 1);
            int top = Math.Max(0, y - 1);
            int bottom = Math.Min(rows - 1, y + 1);
            float hLeft = heightsCm[(y * columns) + left] * heightScale;
            float hRight = heightsCm[(y * columns) + right] * heightScale;
            float hTop = heightsCm[(top * columns) + x] * heightScale;
            float hBottom = heightsCm[(bottom * columns) + x] * heightScale;
            float dx = MathF.Max(1f, worldXCm[right] - worldXCm[left]);
            float dz = MathF.Max(1f, worldYCm[bottom] - worldYCm[top]);
            Vector3 normal = Vector3.Normalize(new Vector3(-(hRight - hLeft) / dx, 1f, -(hBottom - hTop) / dz));
            return float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z)
                ? normal
                : Vector3.UnitY;
        }

        private static Vector3 ComputeNormal(
            in VisualHeightmapRenderChunk chunk,
            int x,
            int y,
            float stepXCm,
            float stepYCm,
            float displayHeightScale)
        {
            int left = Math.Max(0, x - 1);
            int right = Math.Min(chunk.SampleColumns - 1, x + 1);
            int top = Math.Max(0, y - 1);
            int bottom = Math.Min(chunk.SampleRows - 1, y + 1);
            float hLeft = ReadRequiredHeightCm(in chunk, left, y);
            float hRight = ReadRequiredHeightCm(in chunk, right, y);
            float hTop = ReadRequiredHeightCm(in chunk, x, top);
            float hBottom = ReadRequiredHeightCm(in chunk, x, bottom);

            float dx = MathF.Max(1f, (right - left) * stepXCm);
            float dz = MathF.Max(1f, (bottom - top) * stepYCm);
            Vector3 normal = Vector3.Normalize(new Vector3(
                -((hRight - hLeft) * displayHeightScale) / dx,
                1f,
                -((hBottom - hTop) * displayHeightScale) / dz));
            return float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z)
                ? normal
                : Vector3.UnitY;
        }

        private static float ReadRequiredHeightCm(in VisualHeightmapRenderChunk chunk, int sampleX, int sampleY)
        {
            if (!chunk.TryReadHeightCm(sampleX, sampleY, out float heightCm))
            {
                throw new InvalidOperationException(
                    $"Raylib visual heightmap renderer failed to read height sample chunk=({chunk.ChunkX},{chunk.ChunkY}) sample=({sampleX},{sampleY}).");
            }

            if (!float.IsFinite(heightCm))
            {
                throw new InvalidOperationException(
                    $"Raylib visual heightmap renderer read non-finite height sample chunk=({chunk.ChunkX},{chunk.ChunkY}) sample=({sampleX},{sampleY}) heightCm={heightCm}.");
            }

            return heightCm;
        }

        internal static float ResolveEffectiveSeaLevelCm(VisualHeightmapRenderProfile renderProfile, float minHeightCm)
        {
            if (renderProfile == null)
            {
                throw new ArgumentNullException(nameof(renderProfile));
            }

            VisualHeightmapRenderProfile normalized = renderProfile.NormalizeAndValidate();
            if (!float.IsFinite(minHeightCm))
            {
                throw new ArgumentOutOfRangeException(nameof(minHeightCm));
            }

            return normalized.WaterEnabled
                ? normalized.SeaLevelCm
                : minHeightCm - 1f;
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

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        private bool TryUploadOverview(OverviewCpuData cpu, out OverviewGpu overview)
        {
            overview = default;
            Mesh mesh = UploadOverviewMesh(cpu.Mesh);
            Mesh waterMesh = cpu.WaterMesh.VertexCount > 0
                ? UploadOverviewMesh(cpu.WaterMesh)
                : default;
            Texture2D texture = default;
            try
            {
                Image image = Rl.GenImageColor(cpu.Texture.Width, cpu.Texture.Height, Color.BLANK);
                texture = Rl.LoadTextureFromImage(image);
                Rl.UnloadImage(image);
                if (texture.id == 0)
                {
                    return false;
                }

                fixed (byte* ptr = cpu.Texture.Pixels)
                {
                    Rl.UpdateTexture(texture, ptr);
                }

                Rl.SetTextureFilter(texture, Rl.TextureFilter.TEXTURE_FILTER_POINT);
                overview = new OverviewGpu(mesh, waterMesh, texture, cpu.Key);
                mesh = default;
                waterMesh = default;
                texture = default;
                return true;
            }
            finally
            {
                if (mesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(mesh);
                }

                if (waterMesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(waterMesh);
                }

                if (texture.id != 0)
                {
                    Rl.UnloadTexture(texture);
                }
            }
        }

        private static Mesh UploadOverviewMesh(OverviewMeshCpu meshCpu)
        {
            Mesh mesh = new()
            {
                vertexCount = meshCpu.VertexCount,
                triangleCount = meshCpu.TriangleCount,
            };

            int vertexFloatCount = meshCpu.VertexCount * 3;
            int colorByteCount = meshCpu.VertexCount * 4;
            int uvFloatCount = meshCpu.VertexCount * 2;
            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.texcoords = (float*)Rl.MemAlloc(sizeof(float) * uvFloatCount);
            mesh.colors = (byte*)Rl.MemAlloc(sizeof(byte) * colorByteCount);
            mesh.indices = (ushort*)Rl.MemAlloc(sizeof(ushort) * meshCpu.Indices.Length);

            for (int i = 0; i < vertexFloatCount; i++)
            {
                mesh.vertices[i] = meshCpu.Vertices[i];
                mesh.normals[i] = meshCpu.Normals[i];
            }

            for (int i = 0; i < uvFloatCount; i++)
            {
                mesh.texcoords[i] = meshCpu.Texcoords[i];
            }

            for (int i = 0; i < colorByteCount; i++)
            {
                mesh.colors[i] = meshCpu.Colors[i];
            }

            for (int i = 0; i < meshCpu.Indices.Length; i++)
            {
                mesh.indices[i] = meshCpu.Indices[i];
            }

            Rl.UploadMesh(ref mesh, false);
            return mesh;
        }

        public void Dispose()
        {
            if (_overviewBuildTask != null)
            {
                try
                {
                    _overviewBuildTask.Wait();
                }
                catch
                {
                    // Renderer teardown must release GPU resources even if a superseded overview build failed.
                }

                _overviewBuildTask = null;
                _overviewBuildInFlight = false;
            }

            foreach (var kvp in _chunks)
            {
                kvp.Value.Dispose();
            }

            _chunks.Clear();
            if (_overviewLoaded)
            {
                _overview.Dispose();
                _overview = default;
                _overviewLoaded = false;
            }

            if (!_initialized)
            {
                return;
            }

            _terrainMaterial.shader = default;
            Rl.UnloadMaterial(_terrainMaterial);
            Rl.UnloadShader(_terrainShader);
            _waterMaterial.shader = default;
            Rl.UnloadMaterial(_waterMaterial);
            Rl.UnloadShader(_waterShader);
            _initialized = false;
        }

        private struct ChunkGpu : IDisposable
        {
            public Mesh Mesh;
            public Mesh WaterMesh;
            public int Revision;
            public int SourceRevision;
            public bool WaterEnabled;
            public float SeaLevelCm;
            public float DisplayHeightScale;
            public float ColorContrast;
            public int LastUsedFrame;

            public void Dispose()
            {
                if (Mesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(Mesh);
                }

                if (WaterMesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(WaterMesh);
                }
            }
        }

        private sealed class OverviewCpuData
        {
            public OverviewCpuData(
                OverviewKey key,
                OverviewMeshCpu mesh,
                OverviewMeshCpu waterMesh,
                OverviewTextureCpu texture)
            {
                Key = key;
                Mesh = mesh;
                WaterMesh = waterMesh;
                Texture = texture;
            }

            public OverviewKey Key { get; }

            public OverviewMeshCpu Mesh { get; }

            public OverviewMeshCpu WaterMesh { get; }

            public OverviewTextureCpu Texture { get; }
        }

        private readonly struct OverviewMeshCpu
        {
            public OverviewMeshCpu(
                int vertexCount,
                int triangleCount,
                float[] vertices,
                float[] normals,
                float[] texcoords,
                byte[] colors,
                ushort[] indices)
            {
                VertexCount = vertexCount;
                TriangleCount = triangleCount;
                Vertices = vertices;
                Normals = normals;
                Texcoords = texcoords;
                Colors = colors;
                Indices = indices;
            }

            public int VertexCount { get; }

            public int TriangleCount { get; }

            public float[] Vertices { get; }

            public float[] Normals { get; }

            public float[] Texcoords { get; }

            public byte[] Colors { get; }

            public ushort[] Indices { get; }
        }

        private readonly struct OverviewTextureCpu
        {
            public OverviewTextureCpu(int width, int height, byte[] pixels)
            {
                Width = width;
                Height = height;
                Pixels = pixels;
            }

            public int Width { get; }

            public int Height { get; }

            public byte[] Pixels { get; }
        }

        private readonly record struct OverviewKey(
            WorldAabbCm Bounds,
            int ChunkColumns,
            int ChunkRows,
            int SamplesPerChunkColumn,
            int SamplesPerChunkRow,
            int DefaultLayerIndex,
            int Revision,
            int MaxVertices,
            bool WaterEnabled,
            float SeaLevelCm,
            float EffectiveSeaLevelCm,
            float DisplayHeightScale,
            float ColorContrast,
            float MinHeightCm,
            float MaxHeightCm,
            int TextureWidth,
            int TextureHeight);

        private struct OverviewGpu : IDisposable
        {
            public OverviewGpu(Mesh mesh, Mesh waterMesh, Texture2D texture, OverviewKey key)
            {
                Mesh = mesh;
                WaterMesh = waterMesh;
                Texture = texture;
                Key = key;
            }

            public Mesh Mesh;
            public Mesh WaterMesh;
            public Texture2D Texture;
            public OverviewKey Key;

            public void Dispose()
            {
                if (Mesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(Mesh);
                }

                if (WaterMesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(WaterMesh);
                }

                if (Texture.id != 0)
                {
                    Rl.UnloadTexture(Texture);
                }
            }
        }
    }
}
