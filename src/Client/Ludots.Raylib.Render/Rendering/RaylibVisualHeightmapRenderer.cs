using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    public sealed unsafe class RaylibVisualHeightmapRenderer : IDisposable, IRaylibReceiverMeshProjector
    {
        public const string DefaultAlbedoRelativePath = "Presentation/terrain_albedo_environments.json";
        public const string BackendIdRaylib = "raylib";
        public const int TerrainAlbedoLayerCount = 4;
        internal const int DecalStampHeightSampleSegments = 6;
        private const int OverviewTextureMinLongEdgePixels = 1024;
        private const int OverviewTextureMaxLongEdgePixels = 3072;
        private const int OverviewTextureScreenScale = 2;

        private readonly Dictionary<long, ChunkGpu> _chunks = new(1024);
        private readonly List<long> _evictKeys = new(256);
        private readonly IRenderAssetPathResolver? _assetPaths;
        private readonly string _backendId;
        private readonly List<TerrainAlbedoDescriptor> _albedoDescriptors = new();

        private Shader _terrainShader;
        private Material _terrainMaterial;
        private RaylibFrameLightingLocations _terrainLightingLocs;
        private RaylibFrameLighting? _frameLighting;
        private RaylibDirectionalShadowMap? _frameShadow;
        private RaylibShadowSamplingLocations _terrainShadowLocs;
        private float _frameShadowTexelWorld = 0.08f;
        private bool _initialized;
        private int _frameIndex;

        private int _locUseTerrainAlbedo = -1;
        private int _locTerrainTileScale = -1;
        private int _locAntiTile = -1;
        private int _locUseControlMap = -1;
        private int _locControlBounds = -1;
        private int _locControlMap = -1;
        private TerrainAlbedoDescriptor? _activeAlbedo;
        private string? _activeAlbedoMapId;
        private IVisualHeightmap? _stampHeightSampleSource;
        private readonly Texture2D[] _albedoTextures = new Texture2D[TerrainAlbedoLayerCount];
        private Texture2D _controlMapTexture;
        private bool _ownsAlbedoTextures;
        private bool _ownsControlMapTexture;
        private bool _albedoEnabled;
        private bool _controlMapEnabled;
        private float _terrainTileScale = 0.25f;
        private Vector4 _controlBoundsMeters;
        private Mesh _overviewMesh;
        private int _overviewMeshRevision = -1;
        private bool _disableDistanceFog;

        public int DrawnChunkCountLastFrame { get; private set; }

        public bool OverviewActiveLastFrame { get; private set; }

        public int BuiltChunkCountLastFrame { get; private set; }

        public int MissingChunkCountLastFrame { get; private set; }

        public int TerrainVertexCountLastFrame { get; private set; }

        public double ChunkBuildMsLastFrame { get; private set; }

        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadiusCm { get; set; } = 120_000f;

        public bool TerrainAlbedoActive => _albedoEnabled;

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

        public RaylibVisualHeightmapRenderer()
            : this(assetPathResolver: null)
        {
        }

        public RaylibVisualHeightmapRenderer(
            IRenderAssetPathResolver? assetPathResolver,
            string backendId = BackendIdRaylib)
        {
            _assetPaths = assetPathResolver;
            if (string.IsNullOrWhiteSpace(backendId))
            {
                throw new ArgumentException("Terrain albedo backendId must not be empty.", nameof(backendId));
            }

            _backendId = backendId.Trim();
        }

        public void LoadAlbedoDescriptors(IReadOnlyList<MergedConfigEntry> merged)
        {
            if (merged == null)
            {
                throw new ArgumentNullException(nameof(merged));
            }

            _albedoDescriptors.Clear();
            ClearTerrainAlbedo();

            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"{DefaultAlbedoRelativePath} entry '{merged[i].Id}' must merge to a JSON object.");
                }

                TerrainAlbedoDescriptor descriptor = ParseAlbedoDescriptor(obj, merged[i].Id);
                if (!string.Equals(descriptor.BackendId, _backendId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!descriptor.Enabled)
                {
                    continue;
                }

                _albedoDescriptors.Add(descriptor);
            }
        }

        public void EnsureAlbedoActiveForMap(string? mapId)
        {
            if (_albedoDescriptors.Count == 0)
            {
                return;
            }

            if (_activeAlbedo != null &&
                string.Equals(_activeAlbedoMapId, mapId, StringComparison.Ordinal) &&
                _albedoEnabled)
            {
                return;
            }

            TerrainAlbedoDescriptor? match = FindMatchingAlbedoDescriptor(mapId);
            _activeAlbedoMapId = mapId;
            if (match == null)
            {
                ClearTerrainAlbedo();
                return;
            }

            if (_activeAlbedo != null &&
                string.Equals(_activeAlbedo.Id, match.Id, StringComparison.Ordinal) &&
                _albedoEnabled)
            {
                return;
            }

            ActivateAlbedoDescriptor(match);
        }

        public void BindTerrainAlbedo(
            Texture2D sand,
            Texture2D grass,
            Texture2D dirt,
            Texture2D rock,
            float tileScale,
            bool ownsTextures = false)
        {
            EnsureInitialized();
            ValidateAlbedoTexture(sand, "sand");
            ValidateAlbedoTexture(grass, "grass");
            ValidateAlbedoTexture(dirt, "dirt");
            ValidateAlbedoTexture(rock, "rock");
            if (!float.IsFinite(tileScale) || tileScale <= 0f)
            {
                throw new InvalidOperationException(
                    $"{nameof(BindTerrainAlbedo)} tileScale must be a positive finite number.");
            }

            UnloadOwnedAlbedoTextures();
            UnloadOwnedControlMapTexture();
            _albedoTextures[0] = sand;
            _albedoTextures[1] = grass;
            _albedoTextures[2] = dirt;
            _albedoTextures[3] = rock;
            _ownsAlbedoTextures = ownsTextures;
            _terrainTileScale = tileScale;
            _albedoEnabled = true;
            _controlMapEnabled = false;
            _controlBoundsMeters = default;
            ApplyAlbedoMaterialMaps();
            ApplyAlbedoUniforms();
        }

        public void ClearTerrainAlbedo()
        {
            UnloadOwnedAlbedoTextures();
            UnloadOwnedControlMapTexture();
            _activeAlbedo = null;
            _albedoEnabled = false;
            _controlMapEnabled = false;
            _controlBoundsMeters = default;
            _terrainTileScale = 0.25f;
            if (_initialized)
            {
                DetachAlbedoMaterialMaps();
                ApplyAlbedoUniforms();
            }
        }

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
                if (_disableDistanceFog)
                {
                    Vector4 fogOff = new(lighting.FogParams.X, lighting.FogParams.Y, lighting.FogParams.Z, 0f);
                    Rl.SetShaderValue(_terrainShader, _terrainLightingLocs.FogParams, &fogOff, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
                }

                ApplyTerrainShadow();
            }
        }

        public void Render(IVisualHeightmapRenderSource source, in Camera3D camera)
        {
            if (source == null)
            {
                return;
            }

            EnsureInitialized();
            if (_controlMapEnabled)
            {
                WorldAabbCm bounds = source.Bounds;
                _controlBoundsMeters = new Vector4(
                    bounds.Left * 0.01f,
                    bounds.Top * 0.01f,
                    MathF.Max(bounds.Width * 0.01f, 1e-5f),
                    MathF.Max(bounds.Height * 0.01f, 1e-5f));
            }

            UpdateUniforms(camera);

            _frameIndex++;
            DrawnChunkCountLastFrame = 0;
            BuiltChunkCountLastFrame = 0;
            MissingChunkCountLastFrame = 0;
            TerrainVertexCountLastFrame = 0;
            ChunkBuildMsLastFrame = 0d;
            OverviewActiveLastFrame = false;

            VisualHeightmapRenderProfile profile = source.RenderProfile;
            _disableDistanceFog = profile.DisableDistanceFog;
            float aspect = MathF.Max(0.001f, Rl.GetScreenWidth() / (float)Math.Max(1, Rl.GetScreenHeight()));
            float effectiveVisibleRadiusCm = ResolveEffectiveVisibleRadiusCm(source, in camera, aspect);

            if (ShouldUseOverviewMesh(source, in camera, aspect, VisibleRadiusCm, profile.OverviewSwitchChunkSpans))
            {
                EnsureOverviewMesh(source, profile.OverviewVertexLimit);
                RaylibMatrix overviewIdentity = RaylibMatrix.Identity;
                Rl.rlDisableBackfaceCulling();
                Rl.DrawMesh(_overviewMesh, _terrainMaterial, overviewIdentity);
                Rl.rlEnableBackfaceCulling();
                OverviewActiveLastFrame = true;
                TerrainVertexCountLastFrame = _overviewMesh.vertexCount;
            }
            else
            {
                int minChunkX = ResolveChunkIndex((camera.target.X * 100f) - effectiveVisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
                int maxChunkX = ResolveChunkIndex((camera.target.X * 100f) + effectiveVisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
                int minChunkY = ResolveChunkIndex((camera.target.Z * 100f) - effectiveVisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
                int maxChunkY = ResolveChunkIndex((camera.target.Z * 100f) + effectiveVisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);

                float chunkSpanCm = MathF.Max(
                    source.Bounds.Width / (float)Math.Max(1, source.ChunkColumns),
                    source.Bounds.Height / (float)Math.Max(1, source.ChunkRows));
                for (int y = minChunkY; y <= maxChunkY; y++)
                {
                    for (int x = minChunkX; x <= maxChunkX; x++)
                    {
                        if (!source.TryGetChunk(x, y, out VisualHeightmapRenderChunk chunk))
                        {
                            MissingChunkCountLastFrame++;
                            continue;
                        }

                        Vector3 chunkCenterMeters = new(
                            (chunk.Bounds.Left + chunk.Bounds.Right) * 0.005f,
                            0f,
                            (chunk.Bounds.Top + chunk.Bounds.Bottom) * 0.005f);
                        float chunkDistanceMeters = MathF.Max(1f, Vector3.Distance(camera.position, chunkCenterMeters));
                        float projectedChunkPx = chunkSpanCm * 0.01f * ResolvePixelsPerMeter(in camera, chunkDistanceMeters);
                        int strideScale = ResolveChunkLodStrideScale(projectedChunkPx, profile.ChunkLodErrorPx);

                        ref ChunkGpu gpu = ref GetOrCreateChunk(in chunk, strideScale);
                        gpu.LastUsedFrame = _frameIndex;
                        RaylibMatrix identity = RaylibMatrix.Identity;
                        Rl.rlDisableBackfaceCulling();
                        Rl.DrawMesh(gpu.Mesh, _terrainMaterial, identity);
                        Rl.rlEnableBackfaceCulling();

                        DrawnChunkCountLastFrame++;
                        TerrainVertexCountLastFrame += gpu.Mesh.vertexCount;
                    }
                }
            }

            if ((_frameIndex % 300) == 0)
            {
                RenderDiagnostics.Info(
                    "[visual-heightmap] f" + _frameIndex + " overview=" + OverviewActiveLastFrame + " drawn=" + DrawnChunkCountLastFrame + " verts=" + TerrainVertexCountLastFrame + " radiusCm=" + effectiveVisibleRadiusCm.ToString("F0") + " fogOff=" + _disableDistanceFog);
            }

            EvictUnusedChunks(240);
        }

        public void RenderShadow(IVisualHeightmapRenderSource source, in Camera3D camera, RaylibDirectionalShadowMap shadow)
        {
            if (source == null)
            {
                return;
            }

            if (shadow == null) throw new ArgumentNullException(nameof(shadow));

            EnsureInitialized();
            VisualHeightmapRenderProfile profile = source.RenderProfile;
            float shadowAspect = MathF.Max(0.001f, Rl.GetScreenWidth() / (float)Math.Max(1, Rl.GetScreenHeight()));
            float effectiveVisibleRadiusCm = ResolveEffectiveVisibleRadiusCm(source, in camera, shadowAspect);

            if (ShouldUseOverviewMesh(source, in camera, shadowAspect, VisibleRadiusCm, profile.OverviewSwitchChunkSpans))
            {
                EnsureOverviewMesh(source, profile.OverviewVertexLimit);
                RaylibMatrix overviewIdentity = RaylibMatrix.Identity;
                shadow.DrawMeshShadow(_overviewMesh, overviewIdentity);
                return;
            }

            int minChunkX = ResolveChunkIndex((camera.target.X * 100f) - effectiveVisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int maxChunkX = ResolveChunkIndex((camera.target.X * 100f) + effectiveVisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int minChunkY = ResolveChunkIndex((camera.target.Z * 100f) - effectiveVisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
            int maxChunkY = ResolveChunkIndex((camera.target.Z * 100f) + effectiveVisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
            RaylibMatrix identity = RaylibMatrix.Identity;
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    if (!source.TryGetChunk(x, y, out VisualHeightmapRenderChunk chunk))
                    {
                        continue;
                    }

                    // 影子 pass 恒用最低密度：深度图不需要高细节。
                    ref ChunkGpu gpu = ref GetOrCreateChunk(in chunk, strideScale: 4);
                    shadow.DrawMeshShadow(gpu.Mesh, identity);
                }
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
                    $"{nameof(RaylibVisualHeightmapRenderer)}.{nameof(DrawMeshesOverlappingAabbMeters)} requires finite AABB bounds.");
            }

            if (minX > maxX || minY > maxY || minZ > maxZ)
            {
                throw new ArgumentException(
                    $"{nameof(RaylibVisualHeightmapRenderer)}.{nameof(DrawMeshesOverlappingAabbMeters)} AABB min must be <= max.");
            }

            EnsureInitialized();
            if (_chunks.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} has no cached terrain meshes for projected Decals. Render the visual heightmap before drawing Decals.");
            }

            int drawn = 0;
            RaylibMatrix identity = RaylibMatrix.Identity;
            foreach (ChunkGpu gpu in _chunks.Values)
            {
                if (gpu.MaxX < minX || gpu.MinX > maxX ||
                    gpu.MaxY < minY || gpu.MinY > maxY ||
                    gpu.MaxZ < minZ || gpu.MinZ > maxZ)
                {
                    continue;
                }

                Rl.DrawMesh(gpu.Mesh, material, identity);
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
                    $"{nameof(RaylibVisualHeightmapRenderer)} Decal stableId={stableId} has no stamp height sample source. Call {nameof(BindStampHeightSampleSource)} before projecting Decals.");

            if (!float.IsFinite(stampSizeMeters.X) || !float.IsFinite(stampSizeMeters.Y) ||
                stampSizeMeters.X <= 0f || stampSizeMeters.Y <= 0f)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} Decal stableId={stableId} stamp size must be finite and positive, got {stampSizeMeters}.");
            }

            float cos = MathF.Cos(yawRad);
            float sin = MathF.Sin(yawRad);
            float minHeightM = float.PositiveInfinity;
            float maxHeightM = float.NegativeInfinity;
            int samples = DecalStampHeightSampleSegments;
            for (int y = 0; y <= samples; y++)
            {
                float v = (y / (float)samples) - 0.5f;
                float localZ = v * stampSizeMeters.Y;
                for (int x = 0; x <= samples; x++)
                {
                    float u = (x / (float)samples) - 0.5f;
                    float localX = u * stampSizeMeters.X;
                    float worldX = stampCenter.X + (localX * cos) - (localZ * sin);
                    float worldZ = stampCenter.Z + (localX * sin) + (localZ * cos);
                    float worldXCm = worldX * WorldUnits.CmPerMeter;
                    float worldYCm = worldZ * WorldUnits.CmPerMeter;
                    if (!heightmap.TrySampleHeightCm(worldXCm, worldYCm, out float heightCm))
                    {
                        throw new InvalidOperationException(
                            $"{nameof(RaylibVisualHeightmapRenderer)} Decal stableId={stableId} stamp does not overlap sampleable receiver height at ({worldXCm:F1},{worldYCm:F1}).");
                    }

                    float heightM = WorldUnits.CmToM(heightCm);
                    minHeightM = MathF.Min(minHeightM, heightM);
                    maxHeightM = MathF.Max(maxHeightM, heightM);
                }
            }

            return new Vector3(stampCenter.X, (minHeightM + maxHeightM) * 0.5f, stampCenter.Z);
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            _terrainShader = RaylibShaderLoader.Load(baseDir, "terrain.vs", "terrain.fs", "visual-heightmap terrain");

            _terrainMaterial = Rl.LoadMaterialDefault();
            _terrainMaterial.shader = _terrainShader;
            _terrainLightingLocs = RaylibFrameLightingLocations.ResolveOrThrow(_terrainShader, "visual-heightmap terrain");
            _terrainShadowLocs = RaylibShadowSamplingLocations.ResolveOrThrow(
                _terrainShader,
                "visual-heightmap terrain",
                RaylibShadowSampling.ShaderTextureSlot);
            int locMvp = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "mvp", "visual-heightmap terrain");
            int locMatModel = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "matModel", "visual-heightmap terrain");
            int locVertexPosition = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexPosition", "visual-heightmap terrain");
            int locVertexNormal = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexNormal", "visual-heightmap terrain");
            int locVertexColor = RaylibShaderBindingGuard.RequireAttribute(_terrainShader, "vertexColor", "visual-heightmap terrain");

            _locUseTerrainAlbedo = Rl.GetShaderLocation(_terrainShader, "uUseTerrainAlbedo");
            _locTerrainTileScale = Rl.GetShaderLocation(_terrainShader, "uTerrainTileScale");
            _locAntiTile = Rl.GetShaderLocation(_terrainShader, "uAntiTile");
            _locUseControlMap = Rl.GetShaderLocation(_terrainShader, "uUseControlMap");
            _locControlBounds = Rl.GetShaderLocation(_terrainShader, "uControlBounds");
            _locControlMap = Rl.GetShaderLocation(_terrainShader, "uControlMap");
            int locSand = Rl.GetShaderLocation(_terrainShader, "texture0");
            int locGrass = Rl.GetShaderLocation(_terrainShader, "texture1");
            int locDirt = Rl.GetShaderLocation(_terrainShader, "texture2");
            int locRock = Rl.GetShaderLocation(_terrainShader, "texture3");
            if (_locUseTerrainAlbedo < 0 ||
                _locTerrainTileScale < 0 ||
                _locAntiTile < 0 ||
                _locUseControlMap < 0 ||
                _locControlBounds < 0 ||
                _locControlMap < 0 ||
                locSand < 0 ||
                locGrass < 0 ||
                locDirt < 0 ||
                locRock < 0)
            {
                throw new InvalidOperationException(
                    "Visual heightmap terrain shader is missing albedo uniforms/samplers (uUseTerrainAlbedo/uTerrainTileScale/uAntiTile/uUseControlMap/uControlBounds/uControlMap/texture0..texture3).");
            }

            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locVertexNormal;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = locVertexColor;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locMatModel;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locSand;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_METALNESS] = locGrass;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_NORMAL] = locDirt;
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ROUGHNESS] = locRock;
            // DrawMesh binds MATERIAL_MAP_* slots; wire occlusion map slot to explicit uControlMap sampler.
            _terrainShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_OCCLUSION] = _locControlMap;

            _initialized = true;
            ApplyAlbedoUniforms();
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
                    $"{nameof(RaylibVisualHeightmapRenderer)} requires {nameof(ApplyFrameLighting)} before Render.");
            }

            _frameLighting.Apply(_terrainShader, in _terrainLightingLocs);
            _frameLighting.ApplyViewPosition(_terrainShader, in _terrainLightingLocs, camera.position);
            ApplyTerrainShadow();
            ApplyAlbedoUniforms();
        }

        private void ApplyTerrainShadow()
        {
            _terrainShadowLocs.ApplyUniforms(_terrainShader, _frameShadow, _frameShadowTexelWorld);
            RaylibShadowSampling.BindTexture(ref _terrainMaterial, _frameShadow);
        }

        private void ApplyAlbedoUniforms()
        {
            if (!_initialized)
            {
                return;
            }

            int useAlbedo = _albedoEnabled ? 1 : 0;
            int antiTile = _albedoEnabled ? 1 : 0;
            int useControlMap = _controlMapEnabled ? 1 : 0;
            float tileScale = _terrainTileScale;
            Vector4 controlBounds = _controlBoundsMeters;
            Rl.SetShaderValue(
                _terrainShader,
                _locUseTerrainAlbedo,
                &useAlbedo,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            Rl.SetShaderValue(
                _terrainShader,
                _locTerrainTileScale,
                &tileScale,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(
                _terrainShader,
                _locAntiTile,
                &antiTile,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            Rl.SetShaderValue(
                _terrainShader,
                _locUseControlMap,
                &useControlMap,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            Rl.SetShaderValue(
                _terrainShader,
                _locControlBounds,
                &controlBounds,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        private void ApplyAlbedoMaterialMaps()
        {
            Rl.SetMaterialTexture(ref _terrainMaterial, (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO, _albedoTextures[0]);
            Rl.SetMaterialTexture(ref _terrainMaterial, (int)Rl.MaterialMapIndex.MATERIAL_MAP_METALNESS, _albedoTextures[1]);
            Rl.SetMaterialTexture(ref _terrainMaterial, (int)Rl.MaterialMapIndex.MATERIAL_MAP_NORMAL, _albedoTextures[2]);
            Rl.SetMaterialTexture(ref _terrainMaterial, (int)Rl.MaterialMapIndex.MATERIAL_MAP_ROUGHNESS, _albedoTextures[3]);
            if (_controlMapEnabled && _controlMapTexture.id != 0)
            {
                Rl.SetMaterialTexture(
                    ref _terrainMaterial,
                    (int)Rl.MaterialMapIndex.MATERIAL_MAP_OCCLUSION,
                    _controlMapTexture);
            }
            else
            {
                _terrainMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_OCCLUSION].texture = default;
            }
        }

        private void DetachAlbedoMaterialMaps()
        {
            _terrainMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO].texture = default;
            _terrainMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_METALNESS].texture = default;
            _terrainMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_NORMAL].texture = default;
            _terrainMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_ROUGHNESS].texture = default;
            _terrainMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_OCCLUSION].texture = default;
        }

        private void ActivateAlbedoDescriptor(TerrainAlbedoDescriptor descriptor)
        {
            if (_assetPaths == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} cannot activate albedo without an asset path resolver.");
            }

            EnsureInitialized();
            var loaded = new Texture2D[TerrainAlbedoLayerCount];
            Texture2D controlMap = default;
            try
            {
                for (int i = 0; i < TerrainAlbedoLayerCount; i++)
                {
                    loaded[i] = LoadTextureUriOrThrow(
                        descriptor.LayerUris[i],
                        descriptor.Id,
                        $"layer[{i}]");
                }

                bool hasControlMap = !string.IsNullOrEmpty(descriptor.ControlMapUri);
                if (hasControlMap)
                {
                    controlMap = LoadTextureUriOrThrow(
                        descriptor.ControlMapUri!,
                        descriptor.Id,
                        "controlMapUri");
                }

                BindTerrainAlbedo(loaded[0], loaded[1], loaded[2], loaded[3], descriptor.TileScale, ownsTextures: true);
                for (int i = 0; i < loaded.Length; i++)
                {
                    loaded[i] = default;
                }

                if (hasControlMap)
                {
                    _controlMapTexture = controlMap;
                    _ownsControlMapTexture = true;
                    _controlMapEnabled = true;
                    controlMap = default;
                    ApplyAlbedoMaterialMaps();
                    ApplyAlbedoUniforms();
                }

                _activeAlbedo = descriptor;
            }
            catch
            {
                for (int i = 0; i < loaded.Length; i++)
                {
                    if (loaded[i].id != 0)
                    {
                        Rl.UnloadTexture(loaded[i]);
                    }
                }

                if (controlMap.id != 0)
                {
                    Rl.UnloadTexture(controlMap);
                }

                throw;
            }
        }

        private Texture2D LoadTextureUriOrThrow(string uri, string descriptorId, string fieldLabel)
        {
            if (!_assetPaths!.TryResolveFullPath(uri, out string fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} cannot resolve terrain albedo URI '{uri}' for '{descriptorId}' {fieldLabel}.");
            }

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} terrain albedo file missing: uri='{uri}' fullPath='{fullPath}' (descriptor '{descriptorId}' {fieldLabel}).");
            }

            Texture2D texture = Rl.LoadTexture(fullPath);
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    Rl.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} LoadTexture failed for terrain albedo uri='{uri}' fullPath='{fullPath}' ({fieldLabel}).");
            }

            return texture;
        }

        private void UnloadOwnedAlbedoTextures()
        {
            if (_ownsAlbedoTextures)
            {
                for (int i = 0; i < _albedoTextures.Length; i++)
                {
                    if (_albedoTextures[i].id != 0)
                    {
                        Rl.UnloadTexture(_albedoTextures[i]);
                    }

                    _albedoTextures[i] = default;
                }
            }
            else
            {
                for (int i = 0; i < _albedoTextures.Length; i++)
                {
                    _albedoTextures[i] = default;
                }
            }

            _ownsAlbedoTextures = false;
        }

        private void UnloadOwnedControlMapTexture()
        {
            if (_ownsControlMapTexture && _controlMapTexture.id != 0)
            {
                Rl.UnloadTexture(_controlMapTexture);
            }

            _controlMapTexture = default;
            _ownsControlMapTexture = false;
            _controlMapEnabled = false;
        }

        private static void ValidateAlbedoTexture(Texture2D texture, string layerName)
        {
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(BindTerrainAlbedo)} requires a valid {layerName} Texture2D.");
            }
        }

        private TerrainAlbedoDescriptor? FindMatchingAlbedoDescriptor(string? mapId)
        {
            for (int i = 0; i < _albedoDescriptors.Count; i++)
            {
                TerrainAlbedoDescriptor descriptor = _albedoDescriptors[i];
                if (descriptor.MatchesMap(mapId))
                {
                    return descriptor;
                }
            }

            return null;
        }

        private static TerrainAlbedoDescriptor ParseAlbedoDescriptor(JsonObject obj, string fallbackId)
        {
            string id = RequireString(obj["id"], fallbackId, "id");
            string backendId = RequireString(obj["backendId"], id, "backendId");
            bool enabled = obj["enabled"]?.GetValue<bool>() ?? true;

            float tileScale = ReadFloat(obj["tileScale"], 0.25f);
            if (!float.IsFinite(tileScale) || tileScale <= 0f)
            {
                throw new InvalidOperationException(
                    $"{DefaultAlbedoRelativePath} entry '{id}' tileScale must be a positive finite number.");
            }

            if (obj["layerUris"] is not JsonArray layerArr || layerArr.Count != TerrainAlbedoLayerCount)
            {
                throw new InvalidOperationException(
                    $"{DefaultAlbedoRelativePath} entry '{id}' must declare layerUris with exactly {TerrainAlbedoLayerCount} URIs (sand/grass/dirt/rock).");
            }

            var layerUris = new string[TerrainAlbedoLayerCount];
            for (int i = 0; i < TerrainAlbedoLayerCount; i++)
            {
                string uri = layerArr[i]?.GetValue<string>()?.Trim() ?? string.Empty;
                if (uri.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"{DefaultAlbedoRelativePath} entry '{id}' layerUris[{i}] must be a non-empty string.");
                }

                layerUris[i] = uri;
            }

            var mapIds = new List<string>();
            if (obj["mapIds"] is JsonArray mapArr)
            {
                for (int i = 0; i < mapArr.Count; i++)
                {
                    string mapId = mapArr[i]?.GetValue<string>()?.Trim() ?? string.Empty;
                    if (mapId.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"{DefaultAlbedoRelativePath} entry '{id}' mapIds[{i}] must be a non-empty string.");
                    }

                    mapIds.Add(mapId);
                }
            }

            string? controlMapUri = null;
            if (obj.ContainsKey("controlMapUri"))
            {
                string uri = obj["controlMapUri"]?.GetValue<string>()?.Trim() ?? string.Empty;
                if (uri.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"{DefaultAlbedoRelativePath} entry '{id}' controlMapUri must be a non-empty string when present.");
                }

                controlMapUri = uri;
            }

            return new TerrainAlbedoDescriptor(id, backendId, enabled, mapIds, tileScale, layerUris, controlMapUri);
        }

        private static string RequireString(JsonNode? node, string rowId, string fieldName)
        {
            string value = node?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"{DefaultAlbedoRelativePath} entry '{rowId}' must declare '{fieldName}'.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{DefaultAlbedoRelativePath} entry '{rowId}' field '{fieldName}' must not include leading or trailing whitespace.");
            }

            return value;
        }

        private static float ReadFloat(JsonNode? node, float fallback)
        {
            if (node == null)
            {
                return fallback;
            }

            return node.GetValue<float>();
        }

        private static long PackChunkKey(int x, int y, int strideScale)
        {
            return ((long)(uint)x << 40) | ((long)(uint)strideScale << 32) | (uint)y;
        }

        private ref ChunkGpu GetOrCreateChunk(in VisualHeightmapRenderChunk chunk, int strideScale)
        {
            if (strideScale < 1) throw new ArgumentOutOfRangeException(nameof(strideScale));
            long key = PackChunkKey(chunk.ChunkX, chunk.ChunkY, strideScale);
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
            ResolveChunkHeightRange(in chunk, out float minHeightCm, out float maxHeightCm);
            ChunkGpu gpu = new()
            {
                Mesh = CreateChunkMesh(in chunk, strideScale),
                Revision = chunk.Revision,
                LastUsedFrame = _frameIndex,
                MinX = chunk.Bounds.Left * 0.01f,
                MaxX = chunk.Bounds.Right * 0.01f,
                MinY = minHeightCm * 0.01f,
                MaxY = maxHeightCm * 0.01f,
                MinZ = chunk.Bounds.Top * 0.01f,
                MaxZ = chunk.Bounds.Bottom * 0.01f,
            };
            BuiltChunkCountLastFrame++;
            ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - buildStart) * 1000d / Stopwatch.Frequency;
            _chunks[key] = gpu;
            return ref CollectionsMarshal.GetValueRefOrNullRef(_chunks, key);
        }

        private Mesh CreateChunkMesh(in VisualHeightmapRenderChunk chunk, int strideScale)
        {
            ResolveChunkRenderSampling(
                chunk.SampleColumns,
                chunk.SampleRows,
                strideScale,
                out int columns,
                out int rows,
                out int sampleStride);
            int skirtEdgeSegments = (2 * (columns - 1)) + (2 * (rows - 1));
            int skirtVertexCount = (2 * columns) + Math.Max(0, 2 * (rows - 2));
            int vertexCount = checked((columns * rows) + skirtVertexCount);
            int indexCount = checked(((columns - 1) * (rows - 1) * 6) + (skirtEdgeSegments * 6));

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
                int sourceY = ResolveChunkSourceSampleIndex(y, chunk.SampleRows, sampleStride);
                for (int x = 0; x < columns; x++)
                {
                    int sourceX = ResolveChunkSourceSampleIndex(x, chunk.SampleColumns, sampleStride);
                    int vertex = (y * columns) + x;
                    float worldXCm = chunk.Bounds.Left + (sourceX * stepXCm);
                    float worldYCm = chunk.Bounds.Top + (sourceY * stepYCm);
                    chunk.TryReadHeightCm(sourceX, sourceY, out float heightCm);
                    Vector3 normal = ComputeNormal(in chunk, sourceX, sourceY, stepXCm, stepYCm);
                    int f = vertex * 3;
                    mesh.vertices[f + 0] = worldXCm * 0.01f;
                    mesh.vertices[f + 1] = heightCm * 0.01f;
                    mesh.vertices[f + 2] = worldYCm * 0.01f;
                    mesh.normals[f + 0] = normal.X;
                    mesh.normals[f + 1] = normal.Y;
                    mesh.normals[f + 2] = normal.Z;

                    int c = vertex * 4;
                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    float heightBand;
                    byte red;
                    byte green;
                    byte blue;
                    if (absoluteSeaCm is float seaCm)
                    {
                        // Keep negative bands for submerged shelf/abyss tint (refraction reads depth).
                        heightBand = MathF.Min(1f, (heightCm - seaCm) / absolutePeakSpanCm);
                        ResolveAbsoluteIslandTerrainColor(heightBand, slope, out red, out green, out blue);
                    }
                    else
                    {
                        heightBand = Math.Clamp((heightCm - minHeightCm) / heightRangeCm, 0f, 1f);
                        ResolveTerrainColor(heightBand, slope, out red, out green, out blue);
                    }

                    mesh.colors[c + 0] = red;
                    mesh.colors[c + 1] = green;
                    mesh.colors[c + 2] = blue;
                    mesh.colors[c + 3] = ClampToByte(Math.Clamp(heightBand, 0f, 1f) * 255f);
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

            // 裙边：LOD 相邻块密度不同会在共享边裂开，边缘顶点复制下探一段把缝挡住。
            float skirtDepthMeters = MathF.Max(1f, heightRangeCm * 0.2f) * 0.01f;
            int skirtCursor = columns * rows;
            for (int x = 0; x < columns; x++)
            {
                CopySkirtVertex(mesh, x, skirtCursor++, skirtDepthMeters);
            }

            for (int x = 0; x < columns; x++)
            {
                CopySkirtVertex(mesh, ((rows - 1) * columns) + x, skirtCursor++, skirtDepthMeters);
            }

            for (int y = 1; y < rows - 1; y++)
            {
                CopySkirtVertex(mesh, (y * columns) + 0, skirtCursor++, skirtDepthMeters);
            }

            for (int y = 1; y < rows - 1; y++)
            {
                CopySkirtVertex(mesh, (y * columns) + (columns - 1), skirtCursor++, skirtDepthMeters);
            }

            int topSkirt = columns * rows;
            int bottomSkirt = topSkirt + columns;
            int leftSkirt = bottomSkirt + columns;
            int rightSkirt = leftSkirt + Math.Max(0, rows - 2);
            for (int x = 0; x < columns - 1; x++)
            {
                int g0 = x;
                int g1 = x + 1;
                int s0 = topSkirt + x;
                int s1 = topSkirt + x + 1;
                mesh.indices[cursor++] = checked((ushort)g0);
                mesh.indices[cursor++] = checked((ushort)g1);
                mesh.indices[cursor++] = checked((ushort)s1);
                mesh.indices[cursor++] = checked((ushort)g0);
                mesh.indices[cursor++] = checked((ushort)s1);
                mesh.indices[cursor++] = checked((ushort)s0);

                int b0 = ((rows - 1) * columns) + x;
                int b1 = b0 + 1;
                int bs0 = bottomSkirt + x;
                int bs1 = bottomSkirt + x + 1;
                mesh.indices[cursor++] = checked((ushort)b0);
                mesh.indices[cursor++] = checked((ushort)b1);
                mesh.indices[cursor++] = checked((ushort)bs1);
                mesh.indices[cursor++] = checked((ushort)b0);
                mesh.indices[cursor++] = checked((ushort)bs1);
                mesh.indices[cursor++] = checked((ushort)bs0);
            }

            for (int y = 1; y < rows - 2; y++)
            {
                int lg0 = (y * columns) + 0;
                int lg1 = ((y + 1) * columns) + 0;
                int ls0 = leftSkirt + (y - 1);
                int ls1 = leftSkirt + y;
                mesh.indices[cursor++] = checked((ushort)lg0);
                mesh.indices[cursor++] = checked((ushort)lg1);
                mesh.indices[cursor++] = checked((ushort)ls1);
                mesh.indices[cursor++] = checked((ushort)lg0);
                mesh.indices[cursor++] = checked((ushort)ls1);
                mesh.indices[cursor++] = checked((ushort)ls0);

                int rg0 = (y * columns) + (columns - 1);
                int rg1 = ((y + 1) * columns) + (columns - 1);
                int rs0 = rightSkirt + (y - 1);
                int rs1 = rightSkirt + y;
                mesh.indices[cursor++] = checked((ushort)rg0);
                mesh.indices[cursor++] = checked((ushort)rg1);
                mesh.indices[cursor++] = checked((ushort)rs1);
                mesh.indices[cursor++] = checked((ushort)rg0);
                mesh.indices[cursor++] = checked((ushort)rs1);
                mesh.indices[cursor++] = checked((ushort)rs0);
            }

            Rl.UploadMesh(ref mesh, false);
            return mesh;
        }

        private static void CopySkirtVertex(Mesh mesh, int sourceVertex, int targetVertex, float depthMeters)
        {
            mesh.vertices[(targetVertex * 3) + 0] = mesh.vertices[(sourceVertex * 3) + 0];
            mesh.vertices[(targetVertex * 3) + 1] = mesh.vertices[(sourceVertex * 3) + 1] - depthMeters;
            mesh.vertices[(targetVertex * 3) + 2] = mesh.vertices[(sourceVertex * 3) + 2];
            mesh.normals[(targetVertex * 3) + 0] = mesh.normals[(sourceVertex * 3) + 0];
            mesh.normals[(targetVertex * 3) + 1] = mesh.normals[(sourceVertex * 3) + 1];
            mesh.normals[(targetVertex * 3) + 2] = mesh.normals[(sourceVertex * 3) + 2];
            mesh.colors[(targetVertex * 4) + 0] = mesh.colors[(sourceVertex * 4) + 0];
            mesh.colors[(targetVertex * 4) + 1] = mesh.colors[(sourceVertex * 4) + 1];
            mesh.colors[(targetVertex * 4) + 2] = mesh.colors[(sourceVertex * 4) + 2];
            mesh.colors[(targetVertex * 4) + 3] = mesh.colors[(sourceVertex * 4) + 3];
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
            Vector3 grass = new(68f, 142f, 58f);
            Vector3 dirt = new(148f, 108f, 64f);
            Vector3 rock = new(132f, 118f, 102f);
            Vector3 peak = new(188f, 182f, 172f);
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

        private float ResolveEffectiveVisibleRadiusCm(IVisualHeightmapRenderSource source, in Camera3D camera, float aspect)
        {
            float halfDiagonalCm = MathF.Sqrt(
                ((float)source.Bounds.Width * source.Bounds.Width) +
                ((float)source.Bounds.Height * source.Bounds.Height)) * 0.5f;
            float footprintCm = ComputeCameraFootprintRadiusCm(camera, aspect);
            float desired = MathF.Max(VisibleRadiusCm, footprintCm * 1.2f);
            return MathF.Min(desired, halfDiagonalCm);
        }

        internal static float ResolvePixelsPerMeter(in Camera3D camera, float distanceMeters)
        {
            float fovyRad = Math.Clamp(camera.fovy * (MathF.PI / 180f), 0.001f, MathF.PI - 0.001f);
            float screenPx = MathF.Max(1f, Rl.GetScreenHeight());
            return screenPx / (2f * MathF.Tan(fovyRad * 0.5f) * MathF.Max(1f, distanceMeters));
        }

        internal static int ResolveChunkLodStrideScale(float projectedChunkPx, float lodErrorPx)
        {
            if (!float.IsFinite(projectedChunkPx)) throw new ArgumentOutOfRangeException(nameof(projectedChunkPx));
            if (!float.IsFinite(lodErrorPx) || lodErrorPx <= 0f) throw new ArgumentOutOfRangeException(nameof(lodErrorPx));

            if (projectedChunkPx >= lodErrorPx)
            {
                return 1;
            }

            return projectedChunkPx >= lodErrorPx * 0.25f ? 2 : 4;
        }

        private void EnsureOverviewMesh(IVisualHeightmapRenderSource source, int vertexLimit)
        {
            if (_overviewMeshRevision == source.Revision)
            {
                return;
            }

            if (_overviewMesh.vertexCount > 0)
            {
                Rl.UnloadMesh(_overviewMesh);
                _overviewMesh = default;
            }

            _overviewMesh = BuildOverviewMesh(source, vertexLimit);
            _overviewMeshRevision = source.Revision;
        }

        private Mesh BuildOverviewMesh(IVisualHeightmapRenderSource source, int vertexLimit)
        {
            int step = ResolveOverviewStepChunks(source.ChunkColumns, source.ChunkRows, vertexLimit);
            int columns = ResolveOverviewAxisPointCount(source.ChunkColumns, step);
            int rows = ResolveOverviewAxisPointCount(source.ChunkRows, step);
            int vertexCount = checked(columns * rows);
            int indexCount = checked((columns - 1) * (rows - 1) * 6);

            VisualHeightmapRenderProfile profile = source.RenderProfile;
            float seaLevelCm = ResolveEffectiveSeaLevelCm(profile, 0f);
            float peakSpanCm = MathF.Max(1f, profile.AbsoluteColorPeakSpanCm);
            float stepXCm = source.Bounds.Width / (float)(columns - 1);
            float stepYCm = source.Bounds.Height / (float)(rows - 1);

            float[] heights = new float[vertexCount];
            for (int y = 0; y < rows; y++)
            {
                int chunkY = Math.Min(y * step, source.ChunkRows - 1);
                bool farY = (y * step) >= source.ChunkRows;
                for (int x = 0; x < columns; x++)
                {
                    int chunkX = Math.Min(x * step, source.ChunkColumns - 1);
                    bool farX = (x * step) >= source.ChunkColumns;
                    float heightCm = seaLevelCm;
                    if (source.TryGetChunk(chunkX, chunkY, out VisualHeightmapRenderChunk chunk))
                    {
                        int sampleX = farX ? chunk.SampleColumns - 1 : 0;
                        int sampleY = farY ? chunk.SampleRows - 1 : 0;
                        if (chunk.TryReadHeightCm(sampleX, sampleY, out float sampledCm))
                        {
                            heightCm = sampledCm;
                        }
                    }

                    heights[(y * columns) + x] = heightCm;
                }
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

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int vertex = (y * columns) + x;
                    float heightCm = heights[vertex];
                    float heightL = heights[(y * columns) + Math.Max(0, x - 1)];
                    float heightR = heights[(y * columns) + Math.Min(columns - 1, x + 1)];
                    float heightU = heights[(Math.Max(0, y - 1) * columns) + x];
                    float heightD = heights[(Math.Min(rows - 1, y + 1) * columns) + x];
                    Vector3 normal = Vector3.Normalize(new Vector3(
                        (heightL - heightR) / MathF.Max(1f, stepXCm * 2f),
                        1f,
                        (heightU - heightD) / MathF.Max(1f, stepYCm * 2f)));
                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    float heightBand = Math.Clamp((heightCm - seaLevelCm) / peakSpanCm, -1f, 1f);
                    ResolveAbsoluteIslandTerrainColor(heightBand, slope, out byte red, out byte green, out byte blue);

                    int f = vertex * 3;
                    mesh.vertices[f + 0] = (source.Bounds.Left + (x * stepXCm)) * 0.01f;
                    mesh.vertices[f + 1] = heightCm * 0.01f;
                    mesh.vertices[f + 2] = (source.Bounds.Top + (y * stepYCm)) * 0.01f;
                    mesh.normals[f + 0] = normal.X;
                    mesh.normals[f + 1] = normal.Y;
                    mesh.normals[f + 2] = normal.Z;

                    int c = vertex * 4;
                    mesh.colors[c + 0] = red;
                    mesh.colors[c + 1] = green;
                    mesh.colors[c + 2] = blue;
                    mesh.colors[c + 3] = ClampToByte(Math.Clamp(heightBand, 0f, 1f) * 255f);
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

        internal static void ResolveChunkRenderSampling(
            int sampleColumns,
            int sampleRows,
            int strideScale,
            out int renderColumns,
            out int renderRows,
            out int sampleStride)
        {
            if (strideScale < 1) throw new ArgumentOutOfRangeException(nameof(strideScale));
            sampleStride = ResolveChunkSampleStride(sampleColumns, sampleRows) * strideScale;
            renderColumns = ResolveChunkSampleAxisPointCount(sampleColumns, sampleStride);
            renderRows = ResolveChunkSampleAxisPointCount(sampleRows, sampleStride);
            int renderVertexCount = checked(renderColumns * renderRows);
            if (renderVertexCount > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Raylib visual heightmap render sampling resolved {renderVertexCount} vertices, exceeding the platform mesh index limit.");
            }
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

        public void Dispose()
        {
            foreach (var kvp in _chunks)
            {
                kvp.Value.Dispose();
            }

            _chunks.Clear();
            if (_overviewMesh.vertexCount > 0)
            {
                Rl.UnloadMesh(_overviewMesh);
                _overviewMesh = default;
                _overviewMeshRevision = -1;
            }

            ClearTerrainAlbedo();
            _albedoDescriptors.Clear();
            if (!_initialized)
            {
                return;
            }

            RaylibShadowSampling.ClearTexture(ref _terrainMaterial);
            _terrainMaterial.shader = default;
            Rl.UnloadMaterial(_terrainMaterial);
            Rl.UnloadShader(_terrainShader);
            _initialized = false;
        }

        private sealed class TerrainAlbedoDescriptor
        {
            public TerrainAlbedoDescriptor(
                string id,
                string backendId,
                bool enabled,
                List<string> mapIds,
                float tileScale,
                string[] layerUris,
                string? controlMapUri)
            {
                Id = id;
                BackendId = backendId;
                Enabled = enabled;
                MapIds = mapIds;
                TileScale = tileScale;
                LayerUris = layerUris;
                ControlMapUri = controlMapUri;
            }

            public string Id { get; }
            public string BackendId { get; }
            public bool Enabled { get; }
            public IReadOnlyList<string> MapIds { get; }
            public float TileScale { get; }
            public IReadOnlyList<string> LayerUris { get; }
            public string? ControlMapUri { get; }

            public bool MatchesMap(string? mapId)
            {
                if (MapIds.Count == 0)
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(mapId))
                {
                    return false;
                }

                for (int i = 0; i < MapIds.Count; i++)
                {
                    if (string.Equals(MapIds[i], "*", StringComparison.Ordinal) ||
                        string.Equals(MapIds[i], mapId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private struct ChunkGpu : IDisposable
        {
            public Mesh Mesh;
            public int Revision;
            public int LastUsedFrame;
            public float MinX;
            public float MinY;
            public float MinZ;
            public float MaxX;
            public float MaxY;
            public float MaxZ;

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
