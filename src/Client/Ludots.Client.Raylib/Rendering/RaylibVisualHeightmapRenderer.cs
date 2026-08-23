using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
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
        private const float FlatOverviewBaseHeightMeters = -512f;

        private readonly Dictionary<long, ChunkGpu> _chunks = new(1024);
        private readonly List<long> _evictKeys = new(256);

        private OverviewGpu _overview;
        private bool _overviewLoaded;

        // Async overview build: the CPU-heavy sampling/color pass runs off the main
        // thread so the window never freezes on large maps. The last built overview
        // keeps drawing until the new one is ready, then GPU upload happens here.
        private Task<OverviewCpuData?>? _overviewBuildTask;
        private OverviewKey _overviewBuildKey;
        private bool _overviewBuildInFlight;

        private Shader _terrainShader;
        private Material _terrainMaterial;
        private int _locTerrainLightPos;
        private int _locTerrainViewPos;
        private int _locTerrainAmbient;
        private int _locTerrainIntensity;
        private int _locTerrainUseTexture;
        private bool _initialized;
        private int _frameIndex;

        public int DrawnChunkCountLastFrame { get; private set; }

        public int BuiltChunkCountLastFrame { get; private set; }

        public int MissingChunkCountLastFrame { get; private set; }

        public int TerrainVertexCountLastFrame { get; private set; }

        public double ChunkBuildMsLastFrame { get; private set; }

        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadiusCm { get; set; } = 120_000f;

        public int OverviewMaxVertices { get; set; } = 60_000;

        public float OverviewActivationMultiplier { get; set; } = 2.0f;

        // Hysteresis band around the overview activation threshold to stop the LOD from
        // flip-flopping between the sparse overview mesh and the dense detail patch every frame
        // when the camera footprint hovers near the threshold (which reads as violent jitter).
        // Once in overview we require shrinking well below the threshold before switching back,
        // and vice versa.
        public float OverviewSwitchHysteresis { get; set; } = 0.18f;
        private bool _overviewActive;

        public float HeightScale { get; set; } = 1.0f;

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
            RenderPresentation presentation = ResolveRenderPresentation(source, HeightScale);

            _frameIndex++;
            DrawnChunkCountLastFrame = 0;
            BuiltChunkCountLastFrame = 0;
            MissingChunkCountLastFrame = 0;
            TerrainVertexCountLastFrame = 0;
            ChunkBuildMsLastFrame = 0d;

            float aspect = MathF.Max(0.001f, Rl.GetScreenWidth() / (float)Math.Max(1, Rl.GetScreenHeight()));
            if (ResolveOverviewActive(source, camera, aspect))
            {
                long overviewStart = Stopwatch.GetTimestamp();
                if (TryGetOrCreateOverview(source, in presentation, out OverviewGpu overview))
                {
                    ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - overviewStart) * 1000d / Stopwatch.Frequency;
                    RaylibMatrix identity = RaylibMatrix.Identity;
                    SetTerrainTextureMode(overview.Texture, true);
                    Rl.rlDisableBackfaceCulling();
                    Rl.DrawMesh(overview.Mesh, _terrainMaterial, identity);
                    Rl.rlEnableBackfaceCulling();
                    SetTerrainTextureMode(default, false);
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

                    ref ChunkGpu gpu = ref GetOrCreateChunk(in chunk, in presentation);
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
            _locTerrainLightPos = Rl.GetShaderLocation(_terrainShader, "uLightPos");
            _locTerrainViewPos = Rl.GetShaderLocation(_terrainShader, "uViewPos");
            _locTerrainAmbient = Rl.GetShaderLocation(_terrainShader, "uAmbient");
            _locTerrainIntensity = Rl.GetShaderLocation(_terrainShader, "uLightIntensity");
            _locTerrainUseTexture = Rl.GetShaderLocation(_terrainShader, "uUseTexture");
            int locMapAlbedo = Rl.GetShaderLocation(_terrainShader, "texture0");
            int locMvp = Rl.GetShaderLocation(_terrainShader, "mvp");
            int locMatModel = Rl.GetShaderLocation(_terrainShader, "matModel");
            int locVertexPosition = Rl.GetShaderLocationAttrib(_terrainShader, "vertexPosition");
            int locVertexNormal = Rl.GetShaderLocationAttrib(_terrainShader, "vertexNormal");
            int locVertexColor = Rl.GetShaderLocationAttrib(_terrainShader, "vertexColor");
            int locVertexTexCoord = Rl.GetShaderLocationAttrib(_terrainShader, "vertexTexCoord");

            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD02] = -1;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locVertexNormal;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TANGENT] = -1;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = locVertexColor;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locMatModel;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;

            if (_locTerrainUseTexture < 0)
            {
                throw new InvalidOperationException("Shader uniform 'uUseTexture' not found.");
            }

            if (locMapAlbedo < 0)
            {
                throw new InvalidOperationException("Shader uniform 'texture0' not found.");
            }

            if (locVertexTexCoord < 0)
            {
                throw new InvalidOperationException("Shader attrib 'vertexTexCoord' not found.");
            }

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

        private ref ChunkGpu GetOrCreateChunk(in VisualHeightmapRenderChunk chunk, in RenderPresentation presentation)
        {
            long key = GraphChunkKey.Pack(chunk.ChunkX, chunk.ChunkY);
            if (_chunks.TryGetValue(key, out ChunkGpu existing))
            {
                if (existing.Revision == chunk.Revision &&
                    MathF.Abs(existing.HeightScale - presentation.HeightScale) <= 0.0001f &&
                    MathF.Abs(existing.ColorContrast - presentation.ColorContrast) <= 0.0001f &&
                    existing.ColorMode == presentation.ColorMode &&
                    existing.UseAbsoluteHeightColorRange == presentation.UseAbsoluteHeightColorRange &&
                    MathF.Abs(existing.MinHeightCm - presentation.MinHeightCm) <= 0.001f &&
                    MathF.Abs(existing.MaxHeightCm - presentation.MaxHeightCm) <= 0.001f)
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
                Mesh = CreateChunkMesh(in chunk, in presentation),
                Revision = chunk.Revision,
                HeightScale = presentation.HeightScale,
                ColorContrast = presentation.ColorContrast,
                ColorMode = presentation.ColorMode,
                UseAbsoluteHeightColorRange = presentation.UseAbsoluteHeightColorRange,
                MinHeightCm = presentation.MinHeightCm,
                MaxHeightCm = presentation.MaxHeightCm,
                LastUsedFrame = _frameIndex,
            };
            BuiltChunkCountLastFrame++;
            ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - buildStart) * 1000d / Stopwatch.Frequency;
            _chunks[key] = gpu;
            return ref _chunks.GetValueRefOrNullRef(key);
        }

        private static Mesh CreateChunkMesh(in VisualHeightmapRenderChunk chunk, in RenderPresentation presentation)
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
            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.colors = (byte*)Rl.MemAlloc(sizeof(byte) * colorByteCount);
            mesh.indices = (ushort*)Rl.MemAlloc(sizeof(ushort) * indexCount);

            float stepXCm = chunk.SampleStepXCm;
            float stepYCm = chunk.SampleStepYCm;
            float heightScale = presentation.HeightScale;
            ResolveChunkColorRange(in chunk, in presentation, out float minHeightCm, out float maxHeightCm);
            for (int y = 0; y < rows; y++)
            {
                int sourceY = ResolveChunkSourceSampleIndex(y, chunk.SampleRows, sampleStride);
                for (int x = 0; x < columns; x++)
                {
                    int sourceX = ResolveChunkSourceSampleIndex(x, chunk.SampleColumns, sampleStride);
                    int vertex = (y * columns) + x;
                    float worldXCm = chunk.Bounds.Left + (sourceX * stepXCm);
                    float worldYCm = chunk.Bounds.Top + (sourceY * stepYCm);
                    chunk.TryReadHeightCm(sourceX, sourceY, out float heightCm);
                    Vector3 normal = ComputeNormal(in chunk, sourceX, sourceY, stepXCm, stepYCm, heightScale);
                    int f = vertex * 3;
                    mesh.vertices[f + 0] = worldXCm * 0.01f;
                    mesh.vertices[f + 1] = heightCm * heightScale * 0.01f;
                    mesh.vertices[f + 2] = worldYCm * 0.01f;
                    mesh.normals[f + 0] = normal.X;
                    mesh.normals[f + 1] = normal.Y;
                    mesh.normals[f + 2] = normal.Z;

                    int c = vertex * 4;
                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    ResolveTerrainColorRanged(heightCm, slope, presentation.ColorMode, minHeightCm, maxHeightCm, presentation.ColorContrast, out byte red, out byte green, out byte blue);
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

        private static void ResolveChunkColorRange(
            in VisualHeightmapRenderChunk chunk,
            in RenderPresentation presentation,
            out float minHeightCm,
            out float maxHeightCm)
        {
            if (presentation.UseAbsoluteHeightColorRange)
            {
                minHeightCm = presentation.MinHeightCm;
                maxHeightCm = presentation.MaxHeightCm;
                return;
            }

            ResolveChunkHeightRange(in chunk, out minHeightCm, out maxHeightCm);
        }

        internal static Vector3 ResolveTerrainColor(float heightBand, float slope, VisualHeightmapRenderColorMode colorMode)
        {
            return ResolveTerrainColor(heightBand, slope, colorMode, heightCm: 1f, seaLevelCm: 0f);
        }

        internal static Vector3 ResolveTerrainColor(float heightBand, float slope, VisualHeightmapRenderColorMode colorMode, float heightCm, float seaLevelCm)
        {
            return colorMode switch
            {
                VisualHeightmapRenderColorMode.TerrainRamp => VisualHeightmapColorRamp.ResolveColor(heightBand, slope, heightCm, seaLevelCm),
                VisualHeightmapRenderColorMode.HeightmapGrayscale => VisualHeightmapColorRamp.ResolveGrayscale(heightBand),
                _ => throw new ArgumentOutOfRangeException(nameof(colorMode), colorMode, "Unsupported visual heightmap render color mode.")
            };
        }

        private static void ResolveTerrainColor(float heightBand, float slope, VisualHeightmapRenderColorMode colorMode, out byte red, out byte green, out byte blue)
        {
            ResolveTerrainColor(heightBand, slope, colorMode, 1f, 0f, out red, out green, out blue);
        }

        private static void ResolveTerrainColor(float heightBand, float slope, VisualHeightmapRenderColorMode colorMode, float heightCm, float seaLevelCm, out byte red, out byte green, out byte blue)
        {
            Vector3 color = ResolveTerrainColor(heightBand, slope, colorMode, heightCm, seaLevelCm);
            red = ClampToByte(color.X * 255f);
            green = ClampToByte(color.Y * 255f);
            blue = ClampToByte(color.Z * 255f);
        }

        // Range-aware color: land and sea normalize within their own vertical spans so land
        // relief stays readable even when the sea floor spans a far larger depth range.
        private static void ResolveTerrainColorRanged(
            float heightCm,
            float slope,
            VisualHeightmapRenderColorMode colorMode,
            float minHeightCm,
            float maxHeightCm,
            float colorContrast,
            out byte red,
            out byte green,
            out byte blue)
        {
            Vector3 color;
            if (colorMode == VisualHeightmapRenderColorMode.TerrainRamp)
            {
                color = VisualHeightmapColorRamp.ResolveColorRanged(heightCm, slope, minHeightCm, maxHeightCm, seaLevelCm: 0f, colorContrast);
            }
            else
            {
                float rangeCm = MathF.Max(1f, maxHeightCm - minHeightCm);
                float band = VisualHeightmapColorRamp.ResolveHeightBandContrast(Math.Clamp((heightCm - minHeightCm) / rangeCm, 0f, 1f), colorContrast);
                color = ResolveTerrainColor(band, slope, colorMode, heightCm, 0f);
            }

            red = ClampToByte(color.X * 255f);
            green = ClampToByte(color.Y * 255f);
            blue = ClampToByte(color.Z * 255f);
        }

        private static bool TryResolveMeasuredColorRange(
            in RenderPresentation presentation,
            float measuredMinHeightCm,
            float measuredMaxHeightCm,
            out float minHeightCm,
            out float maxHeightCm)
        {
            if (presentation.UseAbsoluteHeightColorRange)
            {
                minHeightCm = presentation.MinHeightCm;
                maxHeightCm = presentation.MaxHeightCm;
                return true;
            }

            minHeightCm = measuredMinHeightCm;
            maxHeightCm = measuredMaxHeightCm;
            return float.IsFinite(minHeightCm) && float.IsFinite(maxHeightCm);
        }

        private bool TryGetOrCreateOverview(IVisualHeightmapRenderSource source, in RenderPresentation presentation, out OverviewGpu overview)
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
                presentation.Revision,
                maxVertices,
                presentation.HeightScale,
                presentation.ColorContrast,
                presentation.FlatOverview,
                presentation.ColorMode,
                presentation.UseAbsoluteHeightColorRange,
                presentation.MinHeightCm,
                presentation.MaxHeightCm,
                textureWidth,
                textureHeight);

            // Already have the exact overview on the GPU.
            if (_overviewLoaded && _overview.Key == key)
            {
                overview = _overview;
                return true;
            }

            PumpOverviewBuild(source, maxVertices, textureWidth, textureHeight, in presentation, in key);

            // The freshly uploaded overview may now match; otherwise keep drawing the
            // previous overview (if any) so the view never blanks or freezes.
            if (_overviewLoaded)
            {
                overview = _overview;
                return true;
            }

            overview = default;
            return false;
        }

        // Drives the background CPU build and performs the main-thread GPU upload when
        // a job completes. Never blocks: if no completed job matches the requested key
        // it (re)starts a background build and returns, leaving the old overview intact.
        private void PumpOverviewBuild(
            IVisualHeightmapRenderSource source,
            int maxVertices,
            int textureWidth,
            int textureHeight,
            in RenderPresentation presentation,
            in OverviewKey key)
        {
            // A build finished: upload it on the main thread (GL context) and drop the task.
            if (_overviewBuildTask != null && _overviewBuildTask.IsCompleted)
            {
                Task<OverviewCpuData?> completed = _overviewBuildTask;
                _overviewBuildTask = null;
                _overviewBuildInFlight = false;

                OverviewCpuData? cpu = completed.IsCompletedSuccessfully ? completed.Result : null;
                if (cpu != null && cpu.Key == key)
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

            // Start a background build if none is running for the currently requested key.
            if (!_overviewBuildInFlight && (!_overviewLoaded || _overview.Key != key))
            {
                if (_overviewBuildKey != key || _overviewBuildTask == null)
                {
                    _overviewBuildKey = key;
                    _overviewBuildInFlight = true;
                    RenderPresentation capturedPresentation = presentation;
                    OverviewKey capturedKey = key;
                    _overviewBuildTask = Task.Run(() => BuildOverviewCpu(
                        source,
                        maxVertices,
                        textureWidth,
                        textureHeight,
                        capturedPresentation,
                        capturedKey));
                }
            }
        }

        private static OverviewCpuData? BuildOverviewCpu(
            IVisualHeightmapRenderSource source,
            int maxVertices,
            int textureWidth,
            int textureHeight,
            RenderPresentation presentation,
            OverviewKey key)
        {
            if (!TryBuildOverviewMeshCpu(source, maxVertices, in presentation, out OverviewMeshCpu meshCpu) ||
                !TryBuildOverviewTextureCpu(source, textureWidth, textureHeight, in presentation, out OverviewTextureCpu textureCpu))
            {
                return null;
            }

            return new OverviewCpuData(key, meshCpu, textureCpu);
        }

        private static bool TryBuildOverviewMeshCpu(
            IVisualHeightmapRenderSource source,
            int maxVertices,
            in RenderPresentation presentation,
            out OverviewMeshCpu meshCpu)
        {
            meshCpu = default;
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
            float minHeightCm = float.PositiveInfinity;
            float maxHeightCm = float.NegativeInfinity;

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
                    if (!TryReadOverviewHeightCm(source, boundaryX, boundaryY, out float heightCm))
                    {
                        return false;
                    }

                    int vertex = (y * columns) + x;
                    heightsCm[vertex] = heightCm;
                    minHeightCm = MathF.Min(minHeightCm, heightCm);
                    maxHeightCm = MathF.Max(maxHeightCm, heightCm);
                }
            }

            if (!TryResolveMeasuredColorRange(in presentation, minHeightCm, maxHeightCm, out float colorMinHeightCm, out float colorMaxHeightCm))
            {
                return false;
            }

            float heightScale = presentation.HeightScale;

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
                    Vector3 normal = presentation.FlatOverview
                        ? Vector3.UnitY
                        : ComputeOverviewNormal(worldXCm, worldYCm, heightsCm, columns, rows, x, y, heightScale);
                    vertices[f + 0] = worldXCm[x] * 0.01f;
                    vertices[f + 1] = ResolveOverviewVertexHeightMeters(heightCm, heightScale, presentation.FlatOverview);
                    vertices[f + 2] = worldYCm[y] * 0.01f;
                    normals[f + 0] = normal.X;
                    normals[f + 1] = normal.Y;
                    normals[f + 2] = normal.Z;

                    int uv = vertex * 2;
                    texcoords[uv + 0] = columns > 1 ? x / (float)(columns - 1) : 0f;
                    texcoords[uv + 1] = rows > 1 ? y / (float)(rows - 1) : 0f;

                    int c = vertex * 4;
                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    ResolveTerrainColorRanged(heightCm, slope, presentation.ColorMode, colorMinHeightCm, colorMaxHeightCm, presentation.ColorContrast, out byte red, out byte green, out byte blue);
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

            meshCpu = new OverviewMeshCpu(vertexCount, indexCount / 3, vertices, normals, texcoords, colors, indices);
            return true;
        }

        private static bool TryBuildOverviewTextureCpu(
            IVisualHeightmapRenderSource source,
            int textureWidth,
            int textureHeight,
            in RenderPresentation presentation,
            out OverviewTextureCpu textureCpu)
        {
            textureCpu = default;
            if (textureWidth <= 0 || textureHeight <= 0)
            {
                return false;
            }

            int sampleCount = checked(textureWidth * textureHeight);
            var heightsCm = new float[sampleCount];
            float minHeightCm = float.PositiveInfinity;
            float maxHeightCm = float.NegativeInfinity;
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
                    if (!TrySampleOverviewTextureHeightCm(source, sampleX, sampleY, out float heightCm))
                    {
                        return false;
                    }

                    int index = (y * textureWidth) + x;
                    heightsCm[index] = heightCm;
                    minHeightCm = MathF.Min(minHeightCm, heightCm);
                    maxHeightCm = MathF.Max(maxHeightCm, heightCm);
                }
            }

            if (!TryResolveMeasuredColorRange(in presentation, minHeightCm, maxHeightCm, out float colorMinHeightCm, out float colorMaxHeightCm))
            {
                return false;
            }

            float heightScale = presentation.HeightScale;
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
                    Vector3 normal = presentation.FlatOverview
                        ? Vector3.UnitY
                        : Vector3.Normalize(new Vector3(
                            -((hRight - hLeft) * heightScale) / dx,
                            1f,
                            -((hBottom - hTop) * heightScale) / dz));
                    if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z))
                    {
                        normal = Vector3.UnitY;
                    }

                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    ResolveTerrainColorRanged(heightsCm[index], slope, presentation.ColorMode, colorMinHeightCm, colorMaxHeightCm, presentation.ColorContrast, out byte red, out byte green, out byte blue);
                    int pixel = index * 4;
                    pixels[pixel + 0] = red;
                    pixels[pixel + 1] = green;
                    pixels[pixel + 2] = blue;
                    pixels[pixel + 3] = 255;
                }
            }

            textureCpu = new OverviewTextureCpu(textureWidth, textureHeight, pixels);
            return true;
        }

        private static bool TryReadOverviewHeightCm(
            IVisualHeightmapRenderSource source,
            int boundaryChunkX,
            int boundaryChunkY,
            out float heightCm)
        {
            heightCm = default;
            int chunkX = boundaryChunkX == 0 ? 0 : boundaryChunkX - 1;
            int chunkY = boundaryChunkY == 0 ? 0 : boundaryChunkY - 1;
            chunkX = Math.Clamp(chunkX, 0, source.ChunkColumns - 1);
            chunkY = Math.Clamp(chunkY, 0, source.ChunkRows - 1);
            if (!source.TryGetChunk(chunkX, chunkY, out VisualHeightmapRenderChunk chunk))
            {
                return false;
            }

            int sampleX = boundaryChunkX == 0 ? 0 : chunk.SampleColumns - 1;
            int sampleY = boundaryChunkY == 0 ? 0 : chunk.SampleRows - 1;
            return chunk.TryReadHeightCm(sampleX, sampleY, out heightCm);
        }

        private static bool TrySampleOverviewTextureHeightCm(
            IVisualHeightmapRenderSource source,
            float normalizedX,
            float normalizedY,
            out float heightCm)
        {
            heightCm = default;
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

            if (!TryReadOverviewTextureSampleHeightCm(source, x0, y0, sampleColumns, sampleRows, out float h00) ||
                !TryReadOverviewTextureSampleHeightCm(source, x1, y0, sampleColumns, sampleRows, out float h10) ||
                !TryReadOverviewTextureSampleHeightCm(source, x0, y1, sampleColumns, sampleRows, out float h01) ||
                !TryReadOverviewTextureSampleHeightCm(source, x1, y1, sampleColumns, sampleRows, out float h11))
            {
                return false;
            }

            float hx0 = Lerp(h00, h10, tx);
            float hx1 = Lerp(h01, h11, tx);
            heightCm = Lerp(hx0, hx1, ty);
            return true;
        }

        private static bool TryReadOverviewTextureSampleHeightCm(
            IVisualHeightmapRenderSource source,
            int globalX,
            int globalY,
            int sampleColumns,
            int sampleRows,
            out float heightCm)
        {
            heightCm = default;
            int stepX = source.SamplesPerChunkColumn - 1;
            int stepY = source.SamplesPerChunkRow - 1;
            int chunkX = globalX >= sampleColumns - 1 ? source.ChunkColumns - 1 : globalX / stepX;
            int chunkY = globalY >= sampleRows - 1 ? source.ChunkRows - 1 : globalY / stepY;
            int localX = globalX >= sampleColumns - 1 ? source.SamplesPerChunkColumn - 1 : globalX - (chunkX * stepX);
            int localY = globalY >= sampleRows - 1 ? source.SamplesPerChunkRow - 1 : globalY - (chunkY * stepY);
            if (!source.TryGetChunk(chunkX, chunkY, out VisualHeightmapRenderChunk chunk))
            {
                return false;
            }

            return chunk.TryReadHeightCm(localX, localY, out heightCm);
        }

        // Stateful, hysteresis-guarded overview decision used by the live render loop.
        // The pure geometric threshold lives in ShouldUseOverviewMesh; here we widen the
        // effective multiplier depending on the current state so the switch point differs on
        // the way in vs. out, eliminating per-frame LOD flip-flop near the boundary.
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

        private static Vector3 ComputeNormal(in VisualHeightmapRenderChunk chunk, int x, int y, float stepXCm, float stepYCm, float heightScale)
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
            Vector3 normal = Vector3.Normalize(new Vector3(
                -((hRight - hLeft) * heightScale) / dx,
                1f,
                -((hBottom - hTop) * heightScale) / dz));
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

        internal static float ResolveOverviewVertexHeightMeters(float heightCm, float heightScale, bool flatOverview)
        {
            if (flatOverview)
            {
                return FlatOverviewBaseHeightMeters;
            }

            return heightCm * heightScale * 0.01f;
        }

        internal static float ResolveHeightBandContrast(float heightBand, float colorContrast)
        {
            return VisualHeightmapColorRamp.ResolveHeightBandContrast(heightBand, colorContrast);
        }

        private static RenderPresentation ResolveRenderPresentation(IVisualHeightmapRenderSource source, float rendererHeightScale)
        {
            if (!float.IsFinite(rendererHeightScale) || rendererHeightScale <= 0f)
            {
                throw new InvalidOperationException("Raylib visual heightmap renderer requires a positive finite height scale.");
            }

            if (source is not IVisualHeightmapRenderPresentation sourcePresentation)
            {
                return new RenderPresentation(rendererHeightScale, 1f, false, VisualHeightmapRenderColorMode.TerrainRamp, false, 0f, 1f, 0);
            }

            float displayHeightScale = sourcePresentation.RenderDisplayHeightScale;
            float colorContrast = sourcePresentation.RenderColorContrast;
            VisualHeightmapRenderColorMode colorMode = sourcePresentation.RenderColorMode;
            bool useAbsoluteHeightColorRange = sourcePresentation.RenderUseAbsoluteHeightColorRange;
            float minHeightCm = sourcePresentation.RenderMinHeightCm;
            float maxHeightCm = sourcePresentation.RenderMaxHeightCm;
            if (!float.IsFinite(displayHeightScale) || displayHeightScale <= 0f)
            {
                throw new InvalidOperationException("Visual heightmap render presentation requires a positive finite display height scale.");
            }

            if (!float.IsFinite(colorContrast) || colorContrast <= 0f)
            {
                throw new InvalidOperationException("Visual heightmap render presentation requires a positive finite color contrast.");
            }

            if (!Enum.IsDefined(colorMode))
            {
                throw new InvalidOperationException("Visual heightmap render presentation uses an unsupported color mode.");
            }

            if (!float.IsFinite(minHeightCm) || !float.IsFinite(maxHeightCm))
            {
                throw new InvalidOperationException("Visual heightmap render presentation requires a finite height color range.");
            }

            if (useAbsoluteHeightColorRange && maxHeightCm < minHeightCm)
            {
                throw new InvalidOperationException("Visual heightmap render presentation height color range cannot be inverted.");
            }

            if (!useAbsoluteHeightColorRange)
            {
                minHeightCm = 0f;
                maxHeightCm = 1f;
            }

            return new RenderPresentation(
                rendererHeightScale * displayHeightScale,
                colorContrast,
                sourcePresentation.RenderFlatOverview,
                colorMode,
                useAbsoluteHeightColorRange,
                minHeightCm,
                maxHeightCm,
                sourcePresentation.RenderPresentationRevision);
        }

        private static byte ClampToByte(float value)
        {
            return (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
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
                    // Background build failures are irrelevant during teardown.
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
            _initialized = false;
        }

        // Main-thread GPU upload of a background-built overview. All raylib/GL calls
        // live here so the worker thread only touches managed arrays.
        private bool TryUploadOverview(OverviewCpuData cpu, out OverviewGpu overview)
        {
            overview = default;
            OverviewMeshCpu meshCpu = cpu.Mesh;
            OverviewTextureCpu textureCpu = cpu.Texture;

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

            Image image = Rl.GenImageColor(textureCpu.Width, textureCpu.Height, Color.BLANK);
            Texture2D texture = Rl.LoadTextureFromImage(image);
            Rl.UnloadImage(image);
            if (texture.id == 0)
            {
                if (mesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(mesh);
                }

                return false;
            }

            fixed (byte* ptr = textureCpu.Pixels)
            {
                Rl.UpdateTexture(texture, ptr);
            }

            Rl.SetTextureFilter(texture, Rl.TextureFilter.TEXTURE_FILTER_POINT);
            overview = new OverviewGpu(mesh, texture, cpu.Key);
            return true;
        }

        private sealed class OverviewCpuData
        {
            public OverviewCpuData(OverviewKey key, OverviewMeshCpu mesh, OverviewTextureCpu texture)
            {
                Key = key;
                Mesh = mesh;
                Texture = texture;
            }

            public OverviewKey Key { get; }

            public OverviewMeshCpu Mesh { get; }

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

        private struct ChunkGpu : IDisposable
        {
            public Mesh Mesh;
            public int Revision;
            public float HeightScale;
            public float ColorContrast;
            public VisualHeightmapRenderColorMode ColorMode;
            public bool UseAbsoluteHeightColorRange;
            public float MinHeightCm;
            public float MaxHeightCm;
            public int LastUsedFrame;

            public void Dispose()
            {
                if (Mesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(Mesh);
                }
            }
        }

        private readonly record struct OverviewKey(
            WorldAabbCm Bounds,
            int ChunkColumns,
            int ChunkRows,
            int SamplesPerChunkColumn,
            int SamplesPerChunkRow,
            int DefaultLayerIndex,
            int Revision,
            int PresentationRevision,
            int MaxVertices,
            float HeightScale,
            float ColorContrast,
            bool FlatOverview,
            VisualHeightmapRenderColorMode ColorMode,
            bool UseAbsoluteHeightColorRange,
            float MinHeightCm,
            float MaxHeightCm,
            int TextureWidth,
            int TextureHeight);

        private readonly record struct RenderPresentation(
            float HeightScale,
            float ColorContrast,
            bool FlatOverview,
            VisualHeightmapRenderColorMode ColorMode,
            bool UseAbsoluteHeightColorRange,
            float MinHeightCm,
            float MaxHeightCm,
            int Revision);

        private struct OverviewGpu : IDisposable
        {
            public OverviewGpu(Mesh mesh, Texture2D texture, OverviewKey key)
            {
                Mesh = mesh;
                Texture = texture;
                Key = key;
            }

            public Mesh Mesh;
            public Texture2D Texture;
            public OverviewKey Key;

            public void Dispose()
            {
                if (Mesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(Mesh);
                }

                if (Texture.id != 0)
                {
                    Rl.UnloadTexture(Texture);
                }
            }
        }
    }
}
