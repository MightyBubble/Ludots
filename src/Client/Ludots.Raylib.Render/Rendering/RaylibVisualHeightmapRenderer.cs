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
        private const int OverviewTextureMinLongEdgePixels = 1024;
        private const int OverviewTextureMaxLongEdgePixels = 3072;
        private const int OverviewTextureScreenScale = 2;
        private const Rl.MaterialMapIndex NavWalkabilityMaterialSlot = Rl.MaterialMapIndex.MATERIAL_MAP_HEIGHT;
        private const Rl.ShaderLocationIndex NavWalkabilityShaderSlot = Rl.ShaderLocationIndex.SHADER_LOC_MAP_HEIGHT;

        private readonly Dictionary<long, ChunkGpu> _chunks = new(1024);
        private readonly List<long> _evictKeys = new(256);
        private Mesh _overviewMesh;
        private int _overviewRevision = int.MinValue;
        private long _overviewBoundsKey;
        private int _overviewVertexLimit = -1;
        private bool _overviewMeshLoaded;
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

        private int _locSkyZenith = -1;
        private int _locSkyGround = -1;
        private int _locUseTerrainAlbedo = -1;
        private int _locTerrainTileScale = -1;
        private int _locAntiTile = -1;
        private int _locUseControlMap = -1;
        private int _locControlBounds = -1;
        private int _locControlMap = -1;
        private int _locUseNavWalkability = -1;
        private int _locNavWalkabilityBounds = -1;
        private int _locNavWalkabilityMap = -1;
        private TerrainAlbedoDescriptor? _activeAlbedo;
        private string? _activeAlbedoMapId;
        private IVisualHeightmap? _stampHeightSampleSource;
        private readonly Texture2D[] _albedoTextures = new Texture2D[TerrainAlbedoLayerCount];
        private Texture2D _controlMapTexture;
        private Texture2D _navWalkabilityTexture;
        private bool _ownsAlbedoTextures;
        private bool _ownsControlMapTexture;
        private bool _albedoEnabled;
        private bool _controlMapEnabled;
        private bool _navWalkabilityEnabled;
        private float _terrainTileScale = 0.25f;
        private Vector4 _controlBoundsMeters;
        private Vector4 _navWalkabilityBoundsCm;
        private string? _navWalkabilityTextureUri;

        public int DrawnChunkCountLastFrame { get; private set; }

        public int BuiltChunkCountLastFrame { get; private set; }

        public int MissingChunkCountLastFrame { get; private set; }

        public int TerrainVertexCountLastFrame { get; private set; }

        public double ChunkBuildMsLastFrame { get; private set; }

        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadiusCm { get; set; } = 120_000f;

        public bool TerrainAlbedoActive => _albedoEnabled;

        public bool NavWalkabilityOverlayActive => _navWalkabilityEnabled;

        private float? _absoluteColorSeaLevelCm;
        private float _absoluteColorPeakSpanCm = 3600f;
        private float _displayHeightScale = 1f;

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

        /// <summary>
        /// Multiplies authored height samples for mesh Y only (colors still use raw cm).
        /// Continental boards need a large scale so relief reads at overview distance.
        /// </summary>
        public float DisplayHeightScale
        {
            get => _displayHeightScale;
            set
            {
                float clamped = Math.Clamp(
                    value,
                    VisualHeightmapRenderProfile.MinDisplayHeightScale,
                    VisualHeightmapRenderProfile.MaxDisplayHeightScale);
                if (MathF.Abs(_displayHeightScale - clamped) <= 1e-4f)
                {
                    return;
                }

                _displayHeightScale = clamped;
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

        public void SetNavWalkabilityOverlay(
            string textureUri,
            WorldAabbCm bounds,
            bool enabled)
        {
            if (!enabled)
            {
                ClearNavWalkabilityOverlay();
                return;
            }

            if (string.IsNullOrWhiteSpace(textureUri) ||
                !string.Equals(textureUri, textureUri.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{nameof(SetNavWalkabilityOverlay)} texture URI must be non-empty without surrounding whitespace.",
                    nameof(textureUri));
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bounds),
                    bounds,
                    $"{nameof(SetNavWalkabilityOverlay)} bounds must have positive extents.");
            }

            long right = (long)bounds.X + bounds.Width;
            long bottom = (long)bounds.Y + bounds.Height;
            if (right > int.MaxValue || bottom > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bounds),
                    bounds,
                    $"{nameof(SetNavWalkabilityOverlay)} bounds maxima must fit Int32 centimeters.");
            }

            Vector4 boundsCm = new(bounds.Left, bounds.Top, (int)right, (int)bottom);
            if (_navWalkabilityTexture.id != 0 &&
                string.Equals(_navWalkabilityTextureUri, textureUri, StringComparison.Ordinal))
            {
                _navWalkabilityBoundsCm = boundsCm;
                _navWalkabilityEnabled = true;
                ApplyNavWalkabilityMaterialMap();
                ApplyNavWalkabilityUniforms();
                return;
            }

            Texture2D loaded = LoadNavWalkabilityTextureOrThrow(textureUri);
            UnloadNavWalkabilityTexture();
            _navWalkabilityTexture = loaded;
            _navWalkabilityTextureUri = textureUri;
            _navWalkabilityBoundsCm = boundsCm;
            _navWalkabilityEnabled = true;
            ApplyNavWalkabilityMaterialMap();
            ApplyNavWalkabilityUniforms();
        }

        public void ClearNavWalkabilityOverlay()
        {
            if (!_navWalkabilityEnabled && _navWalkabilityTexture.id == 0)
            {
                return;
            }

            UnloadNavWalkabilityTexture();
            _navWalkabilityEnabled = false;
            _navWalkabilityBoundsCm = default;
            _navWalkabilityTextureUri = null;
            if (_initialized)
            {
                _terrainMaterial.maps[(int)NavWalkabilityMaterialSlot].texture = default;
                ApplyNavWalkabilityUniforms();
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
            VisualHeightmapRenderProfile profile = source.RenderProfile.NormalizeAndValidate();
            if (_controlMapEnabled)
            {
                WorldAabbCm bounds = source.Bounds;
                _controlBoundsMeters = new Vector4(
                    bounds.Left * 0.01f,
                    bounds.Top * 0.01f,
                    MathF.Max(bounds.Width * 0.01f, 1e-5f),
                    MathF.Max(bounds.Height * 0.01f, 1e-5f));
            }

            UpdateUniforms(camera, profile.DisableDistanceFog);

            _frameIndex++;
            DrawnChunkCountLastFrame = 0;
            BuiltChunkCountLastFrame = 0;
            MissingChunkCountLastFrame = 0;
            TerrainVertexCountLastFrame = 0;
            ChunkBuildMsLastFrame = 0d;

            float aspect = ResolveFrameAspect();
            bool useOverview = ShouldUseOverviewMesh(
                source,
                in camera,
                aspect,
                VisibleRadiusCm,
                profile.OverviewSwitchChunkSpans);
            if (useOverview)
            {
                if (source is not IVisualHeightmap heightSampleSource)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibVisualHeightmapRenderer)} overview mesh requires the render source to also implement {nameof(IVisualHeightmap)}.");
                }

                long buildStart = Stopwatch.GetTimestamp();
                EnsureOverviewMesh(source, heightSampleSource, profile.OverviewVertexLimit);
                ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - buildStart) * 1000d / Stopwatch.Frequency;
                // Keep albedo/control so authored weight maps stay readable; drop nav walkability wash only.
                ApplyOverviewWithoutNavWalkabilityUniforms();
                RaylibMatrix identity = RaylibMatrix.Identity;
                Rl.rlDisableBackfaceCulling();
                Rl.DrawMesh(_overviewMesh, _terrainMaterial, identity);
                Rl.rlEnableBackfaceCulling();
                ApplyNavWalkabilityUniforms();
                DrawnChunkCountLastFrame = 1;
                TerrainVertexCountLastFrame = _overviewMesh.vertexCount;
                EvictUnusedChunks(240);
                return;
            }

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

        public void RenderShadow(IVisualHeightmapRenderSource source, in Camera3D camera, RaylibDirectionalShadowMap shadow)
        {
            if (source == null)
            {
                return;
            }

            if (shadow == null) throw new ArgumentNullException(nameof(shadow));

            EnsureInitialized();
            int minChunkX = ResolveChunkIndex((camera.target.X * 100f) - VisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int maxChunkX = ResolveChunkIndex((camera.target.X * 100f) + VisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int minChunkY = ResolveChunkIndex((camera.target.Z * 100f) - VisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
            int maxChunkY = ResolveChunkIndex((camera.target.Z * 100f) + VisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
            RaylibMatrix identity = RaylibMatrix.Identity;
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    if (!source.TryGetChunk(x, y, out VisualHeightmapRenderChunk chunk))
                    {
                        continue;
                    }

                    ref ChunkGpu gpu = ref GetOrCreateChunk(in chunk);
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

            return RaylibDecalStampFit.FitCenter(
                in stampCenter,
                yawRad,
                in stampSizeMeters,
                stableId,
                heightmap,
                nameof(RaylibVisualHeightmapRenderer));
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            _terrainShader = RaylibShaderLoader.Load(baseDir, "terrain.vs", "terrain.fs", "visual-heightmap terrain");

            _terrainMaterial = RaylibNativeResources.LoadMaterialDefault();
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

            _locSkyZenith = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uSkyZenith", "visual-heightmap terrain");
            _locSkyGround = RaylibShaderBindingGuard.RequireUniform(_terrainShader, "uSkyGround", "visual-heightmap terrain");
            _locUseTerrainAlbedo = Rl.GetShaderLocation(_terrainShader, "uUseTerrainAlbedo");
            _locTerrainTileScale = Rl.GetShaderLocation(_terrainShader, "uTerrainTileScale");
            _locAntiTile = Rl.GetShaderLocation(_terrainShader, "uAntiTile");
            _locUseControlMap = Rl.GetShaderLocation(_terrainShader, "uUseControlMap");
            _locControlBounds = Rl.GetShaderLocation(_terrainShader, "uControlBounds");
            _locControlMap = Rl.GetShaderLocation(_terrainShader, "uControlMap");
            _locUseNavWalkability = Rl.GetShaderLocation(_terrainShader, "uUseNavWalkability");
            _locNavWalkabilityBounds = Rl.GetShaderLocation(_terrainShader, "uNavWalkabilityBounds");
            _locNavWalkabilityMap = Rl.GetShaderLocation(_terrainShader, "uNavWalkabilityMap");
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
                _locUseNavWalkability < 0 ||
                _locNavWalkabilityBounds < 0 ||
                _locNavWalkabilityMap < 0 ||
                locSand < 0 ||
                locGrass < 0 ||
                locDirt < 0 ||
                locRock < 0)
            {
                throw new InvalidOperationException(
                    "Visual heightmap terrain shader is missing albedo/nav uniforms or samplers (uUseTerrainAlbedo/uTerrainTileScale/uAntiTile/uUseControlMap/uControlBounds/uControlMap/uUseNavWalkability/uNavWalkabilityBounds/uNavWalkabilityMap/texture0..texture3).");
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
            _terrainShader.locs[(int)NavWalkabilityShaderSlot] = _locNavWalkabilityMap;

            _initialized = true;
            ApplyAlbedoUniforms();
            ApplyNavWalkabilityMaterialMap();
            ApplyNavWalkabilityUniforms();
            ApplyTerrainShadow();
            if (_frameLighting != null)
            {
                ApplySkyIrradianceUniforms();
            }
        }

        private void UpdateUniforms(in Camera3D camera, bool disableDistanceFog)
        {
            if (_frameLighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} requires {nameof(ApplyFrameLighting)} before Render.");
            }

            ApplySkyIrradianceUniforms();
            if (disableDistanceFog)
            {
                ApplyDisabledDistanceFog();
            }

            _frameLighting.ApplyViewPosition(_terrainShader, in _terrainLightingLocs, camera.position);
            ApplyTerrainShadow();
            ApplyAlbedoUniforms();
            ApplyNavWalkabilityUniforms();
        }

        private unsafe void ApplyDisabledDistanceFog()
        {
            Vector4 fogParams = Vector4.Zero;
            Vector3 fogColor = Vector3.Zero;
            Rl.SetShaderValue(
                _terrainShader,
                _terrainLightingLocs.FogParams,
                &fogParams,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            Rl.SetShaderValue(
                _terrainShader,
                _terrainLightingLocs.FogColor,
                &fogColor,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        }

        private void ApplySkyIrradianceUniforms()
        {
            _frameLighting!.Apply(_terrainShader, in _terrainLightingLocs);
            Vector3 zenith = _frameLighting.SkyZenithColor;
            Vector3 ground = _frameLighting.SkyGroundColor;
            Rl.SetShaderValue(_terrainShader, _locSkyZenith, &zenith, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locSkyGround, &ground, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
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

        private void ApplyOverviewWithoutNavWalkabilityUniforms()
        {
            if (!_initialized)
            {
                return;
            }

            int off = 0;
            Rl.SetShaderValue(
                _terrainShader,
                _locUseNavWalkability,
                &off,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
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

        private void ApplyNavWalkabilityUniforms()
        {
            if (!_initialized)
            {
                return;
            }

            int useNavWalkability = _navWalkabilityEnabled ? 1 : 0;
            Vector4 bounds = _navWalkabilityBoundsCm;
            Rl.SetShaderValue(
                _terrainShader,
                _locUseNavWalkability,
                &useNavWalkability,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            Rl.SetShaderValue(
                _terrainShader,
                _locNavWalkabilityBounds,
                &bounds,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        private void ApplyNavWalkabilityMaterialMap()
        {
            if (!_initialized)
            {
                return;
            }

            if (_navWalkabilityEnabled && _navWalkabilityTexture.id != 0)
            {
                Rl.SetMaterialTexture(
                    ref _terrainMaterial,
                    (int)NavWalkabilityMaterialSlot,
                    _navWalkabilityTexture);
            }
            else
            {
                _terrainMaterial.maps[(int)NavWalkabilityMaterialSlot].texture = default;
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
                        RaylibNativeResources.UnloadTexture(loaded[i]);
                    }
                }

                if (controlMap.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(controlMap);
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

            Texture2D texture = RaylibNativeResources.LoadTexture(fullPath);
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} LoadTexture failed for terrain albedo uri='{uri}' fullPath='{fullPath}' ({fieldLabel}).");
            }

            return texture;
        }

        private Texture2D LoadNavWalkabilityTextureOrThrow(string uri)
        {
            if (_assetPaths == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} cannot load nav walkability texture without an asset path resolver.");
            }

            if (!_assetPaths.TryResolveFullPath(uri, out string fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} cannot resolve nav walkability texture URI '{uri}'.");
            }

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} nav walkability texture file missing: uri='{uri}' fullPath='{fullPath}'.");
            }

            EnsureInitialized();
            Texture2D texture = RaylibNativeResources.LoadTexture(fullPath);
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} LoadTexture failed for nav walkability texture uri='{uri}' fullPath='{fullPath}'.");
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
                        RaylibNativeResources.UnloadTexture(_albedoTextures[i]);
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
                RaylibNativeResources.UnloadTexture(_controlMapTexture);
            }

            _controlMapTexture = default;
            _ownsControlMapTexture = false;
            _controlMapEnabled = false;
        }

        private void UnloadNavWalkabilityTexture()
        {
            if (_navWalkabilityTexture.id != 0)
            {
                RaylibNativeResources.UnloadTexture(_navWalkabilityTexture);
            }

            _navWalkabilityTexture = default;
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

        private static long PackChunkKey(int x, int y)
        {
            return (long)(uint)x << 32 | (uint)y;
        }

        private ref ChunkGpu GetOrCreateChunk(in VisualHeightmapRenderChunk chunk)
        {
            long key = PackChunkKey(chunk.ChunkX, chunk.ChunkY);
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
                Mesh = CreateChunkMesh(in chunk),
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

        private Mesh CreateChunkMesh(in VisualHeightmapRenderChunk chunk)
        {
            ResolveChunkRenderSampling(
                chunk.SampleColumns,
                chunk.SampleRows,
                out int columns,
                out int rows,
                out int sampleStride);
            int vertexCount = checked(columns * rows);
            int indexCount = checked((columns - 1) * (rows - 1) * 6);

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
            float displayHeightScale = _displayHeightScale;
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
                    float displayHeightCm = heightCm;
                    int f = vertex * 3;
                    int c = vertex * 4;
                    float heightBand;
                    byte red;
                    byte green;
                    byte blue;
                    if (absoluteSeaCm is float seaCm)
                    {
                        heightBand = ResolveAbsoluteHeightBand(heightCm, seaCm, absolutePeakSpanCm);
                        displayHeightCm = ResolveAbsoluteDisplayHeightCm(heightCm, seaCm, absolutePeakSpanCm);
                    }
                    else
                    {
                        heightBand = Math.Clamp((heightCm - minHeightCm) / heightRangeCm, 0f, 1f);
                    }

                    Vector3 normal = ComputeNormal(
                        in chunk,
                        sourceX,
                        sourceY,
                        stepXCm,
                        stepYCm,
                        displayHeightScale,
                        absoluteSeaCm,
                        absolutePeakSpanCm);
                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    if (absoluteSeaCm is float)
                    {
                        ResolveAbsoluteIslandTerrainColor(heightBand, slope, out red, out green, out blue);
                    }
                    else
                    {
                        ResolveTerrainColor(heightBand, slope, out red, out green, out blue);
                    }

                    mesh.vertices[f + 0] = worldXCm * 0.01f;
                    mesh.vertices[f + 1] = displayHeightCm * displayHeightScale * 0.01f;
                    mesh.vertices[f + 2] = worldYCm * 0.01f;
                    mesh.normals[f + 0] = normal.X;
                    mesh.normals[f + 1] = normal.Y;
                    mesh.normals[f + 2] = normal.Z;
                    mesh.colors[c + 0] = red;
                    mesh.colors[c + 1] = green;
                    mesh.colors[c + 2] = blue;
                    // Alpha 0 marks open-water fill so terrain.fs skips albedo/control over ocean sentinels.
                    mesh.colors[c + 3] = heightBand <= 0f
                        ? (byte)0
                        : ClampToByte(Math.Clamp(heightBand, 1f / 255f, 1f) * 255f);
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

            RaylibNativeResources.UploadMesh(ref mesh, false);
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

        internal static float ResolveAbsoluteHeightBand(float heightCm, float seaLevelCm, float absolutePeakSpanCm)
        {
            float peakSpanCm = MathF.Max(1f, absolutePeakSpanCm);
            float relative = (heightCm - seaLevelCm) / peakSpanCm;
            // Authored land peaks sit within AbsoluteColorPeakSpanCm. Continental assets may fill
            // ocean/void with sentinel values far above that span — tint those as open water, not peaks.
            if (relative > 1f)
            {
                float overshoot = relative - 1f;
                return -Math.Clamp(overshoot * 0.02f, 0.01f, 0.08f);
            }

            return relative;
        }

        internal static float ResolveAbsoluteDisplayHeightCm(float heightCm, float seaLevelCm, float absolutePeakSpanCm)
        {
            float peakSpanCm = MathF.Max(1f, absolutePeakSpanCm);
            float relative = (heightCm - seaLevelCm) / peakSpanCm;
            // Absolute tint treats open water (below sea) and overshoot sentinels as a flat sea plane.
            // Continental DisplayHeightScale would otherwise excavate multi-kilometer ocean pits.
            return relative <= 0f || relative > 1f ? seaLevelCm : heightCm;
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

        private static Vector3 ComputeNormal(
            in VisualHeightmapRenderChunk chunk,
            int x,
            int y,
            float stepXCm,
            float stepYCm,
            float displayHeightScale,
            float? absoluteSeaCm,
            float absolutePeakSpanCm)
        {
            int left = Math.Max(0, x - 1);
            int right = Math.Min(chunk.SampleColumns - 1, x + 1);
            int top = Math.Max(0, y - 1);
            int bottom = Math.Min(chunk.SampleRows - 1, y + 1);
            chunk.TryReadHeightCm(left, y, out float hLeft);
            chunk.TryReadHeightCm(right, y, out float hRight);
            chunk.TryReadHeightCm(x, top, out float hTop);
            chunk.TryReadHeightCm(x, bottom, out float hBottom);
            if (absoluteSeaCm is float seaCm)
            {
                hLeft = ResolveAbsoluteDisplayHeightCm(hLeft, seaCm, absolutePeakSpanCm);
                hRight = ResolveAbsoluteDisplayHeightCm(hRight, seaCm, absolutePeakSpanCm);
                hTop = ResolveAbsoluteDisplayHeightCm(hTop, seaCm, absolutePeakSpanCm);
                hBottom = ResolveAbsoluteDisplayHeightCm(hBottom, seaCm, absolutePeakSpanCm);
            }

            float scale = MathF.Max(VisualHeightmapRenderProfile.MinDisplayHeightScale, displayHeightScale);
            float dx = MathF.Max(1f, (right - left) * stepXCm);
            float dz = MathF.Max(1f, (bottom - top) * stepYCm);
            Vector3 normal = Vector3.Normalize(
                new Vector3(-(hRight - hLeft) * scale / dx, 1f, -(hBottom - hTop) * scale / dz));
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
            ClearOverviewMesh();
        }

        private void ClearOverviewMesh()
        {
            if (_overviewMeshLoaded && _overviewMesh.vertexCount > 0)
            {
                RaylibNativeResources.UnloadMesh(_overviewMesh);
            }

            _overviewMesh = default;
            _overviewMeshLoaded = false;
            _overviewRevision = int.MinValue;
            _overviewBoundsKey = 0;
            _overviewVertexLimit = -1;
        }

        private static float ResolveFrameAspect()
        {
            int width = Math.Max(1, Rl.GetScreenWidth());
            int height = Math.Max(1, Rl.GetScreenHeight());
            return width / (float)height;
        }

        private void EnsureOverviewMesh(
            IVisualHeightmapRenderSource source,
            IVisualHeightmap heightSampleSource,
            int overviewVertexLimit)
        {
            long boundsKey = PackBoundsKey(source.Bounds);
            if (_overviewMeshLoaded &&
                _overviewRevision == source.Revision &&
                _overviewBoundsKey == boundsKey &&
                _overviewVertexLimit == overviewVertexLimit)
            {
                return;
            }

            ClearOverviewMesh();
            _overviewMesh = CreateOverviewMesh(source, heightSampleSource, overviewVertexLimit);
            _overviewMeshLoaded = true;
            _overviewRevision = source.Revision;
            _overviewBoundsKey = boundsKey;
            _overviewVertexLimit = overviewVertexLimit;
            BuiltChunkCountLastFrame++;
        }

        private unsafe Mesh CreateOverviewMesh(
            IVisualHeightmapRenderSource source,
            IVisualHeightmap heightSampleSource,
            int overviewVertexLimit)
        {
            int stepChunks = ResolveOverviewStepChunks(source.ChunkColumns, source.ChunkRows, overviewVertexLimit);
            int columns = ResolveOverviewAxisPointCount(source.ChunkColumns, stepChunks);
            int rows = ResolveOverviewAxisPointCount(source.ChunkRows, stepChunks);
            int vertexCount = checked(columns * rows);
            if (vertexCount > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVisualHeightmapRenderer)} overview mesh vertex count {vertexCount} exceeds Raylib ushort index limit.");
            }

            int indexCount = checked((columns - 1) * (rows - 1) * 6);
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

            WorldAabbCm bounds = source.Bounds;
            float stepXCm = columns > 1 ? bounds.Width / (float)(columns - 1) : 0f;
            float stepZCm = rows > 1 ? bounds.Height / (float)(rows - 1) : 0f;
            float? absoluteSeaCm = _absoluteColorSeaLevelCm;
            float absolutePeakSpanCm = MathF.Max(1f, _absoluteColorPeakSpanCm);
            float displayHeightScale = _displayHeightScale;
            float minHeightCm = float.PositiveInfinity;
            float maxHeightCm = float.NegativeInfinity;
            var heights = new float[vertexCount];
            var displayHeights = new float[vertexCount];
            for (int y = 0; y < rows; y++)
            {
                float worldYCm = bounds.Top + (y * stepZCm);
                for (int x = 0; x < columns; x++)
                {
                    float worldXCm = bounds.Left + (x * stepXCm);
                    int vertex = (y * columns) + x;
                    if (!heightSampleSource.TrySampleHeightCm(worldXCm, worldYCm, out float heightCm))
                    {
                        heightCm = absoluteSeaCm ?? 0f;
                    }

                    float displayHeightCm = absoluteSeaCm is float seaForDisplay
                        ? ResolveAbsoluteDisplayHeightCm(heightCm, seaForDisplay, absolutePeakSpanCm)
                        : heightCm;
                    heights[vertex] = heightCm;
                    displayHeights[vertex] = displayHeightCm;
                    minHeightCm = MathF.Min(minHeightCm, heightCm);
                    maxHeightCm = MathF.Max(maxHeightCm, heightCm);
                    int f = vertex * 3;
                    mesh.vertices[f + 0] = worldXCm * 0.01f;
                    mesh.vertices[f + 1] = displayHeightCm * displayHeightScale * 0.01f;
                    mesh.vertices[f + 2] = worldYCm * 0.01f;
                    mesh.normals[f + 0] = 0f;
                    mesh.normals[f + 1] = 1f;
                    mesh.normals[f + 2] = 0f;
                }
            }

            if (!float.IsFinite(minHeightCm) || !float.IsFinite(maxHeightCm))
            {
                minHeightCm = 0f;
                maxHeightCm = 1f;
            }

            float heightRangeCm = MathF.Max(1f, maxHeightCm - minHeightCm);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int vertex = (y * columns) + x;
                    float heightCm = heights[vertex];
                    float hL = displayHeights[(y * columns) + Math.Max(0, x - 1)];
                    float hR = displayHeights[(y * columns) + Math.Min(columns - 1, x + 1)];
                    float hT = displayHeights[(Math.Max(0, y - 1) * columns) + x];
                    float hB = displayHeights[(Math.Min(rows - 1, y + 1) * columns) + x];
                    float dx = MathF.Max(1f, stepXCm);
                    float dz = MathF.Max(1f, stepZCm);
                    Vector3 normal = Vector3.Normalize(
                        new Vector3(-(hR - hL) * displayHeightScale / dx, 1f, -(hB - hT) * displayHeightScale / dz));
                    if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z))
                    {
                        normal = Vector3.UnitY;
                    }

                    int f = vertex * 3;
                    mesh.normals[f + 0] = normal.X;
                    mesh.normals[f + 1] = normal.Y;
                    mesh.normals[f + 2] = normal.Z;

                    float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
                    float heightBand;
                    byte red;
                    byte green;
                    byte blue;
                    if (absoluteSeaCm is float seaCm)
                    {
                        heightBand = ResolveAbsoluteHeightBand(heightCm, seaCm, absolutePeakSpanCm);
                        ResolveAbsoluteIslandTerrainColor(heightBand, slope, out red, out green, out blue);
                    }
                    else
                    {
                        heightBand = Math.Clamp((heightCm - minHeightCm) / heightRangeCm, 0f, 1f);
                        ResolveTerrainColor(heightBand, slope, out red, out green, out blue);
                    }

                    int c = vertex * 4;
                    mesh.colors[c + 0] = red;
                    mesh.colors[c + 1] = green;
                    mesh.colors[c + 2] = blue;
                    mesh.colors[c + 3] = heightBand <= 0f
                        ? (byte)0
                        : ClampToByte(Math.Clamp(heightBand, 1f / 255f, 1f) * 255f);
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

            RaylibNativeResources.UploadMesh(ref mesh, false);
            return mesh;
        }

        private static long PackBoundsKey(WorldAabbCm bounds)
        {
            unchecked
            {
                long key = bounds.Left;
                key = (key * 397) ^ bounds.Top;
                key = (key * 397) ^ bounds.Width;
                key = (key * 397) ^ bounds.Height;
                return key;
            }
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
            out int renderColumns,
            out int renderRows,
            out int sampleStride)
        {
            sampleStride = ResolveChunkSampleStride(sampleColumns, sampleRows);
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
            ClearOverviewMesh();
            ClearNavWalkabilityOverlay();
            ClearTerrainAlbedo();
            _albedoDescriptors.Clear();
            if (!_initialized)
            {
                return;
            }

            RaylibShadowSampling.ClearTexture(ref _terrainMaterial);
            _terrainMaterial.shader = default;
            RaylibNativeResources.UnloadMaterial(_terrainMaterial);
            RaylibNativeResources.UnloadShader(_terrainShader);
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
                    RaylibNativeResources.UnloadMesh(Mesh);
                }
            }
        }
    }
}
