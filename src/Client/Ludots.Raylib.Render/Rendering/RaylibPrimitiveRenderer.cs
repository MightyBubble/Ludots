using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;

namespace Ludots.Raylib.Render
{
    public enum RaylibPrimitiveRenderMode : byte
    {
        Immediate = 0,
        Instanced = 1
    }

    public sealed unsafe class RaylibPrimitiveRenderer : IDisposable, IRenderAssetResidency
    {
        private readonly RaylibPrimitiveRenderMode _mode;
        private readonly System.Func<string, int> _channelRegistrar;

        private static int ThrowMissingChannelRegistrar(string name)
        {
            throw new System.InvalidOperationException(
                $"{nameof(RaylibPrimitiveRenderer)} animation overlay requires a channel registrar (host wires AnimationChannelRegistry.Register).");
        }
        private readonly IRenderAssetPathResolver? _vfs;
        private readonly IRenderMaterialAssets? _materials;
        private readonly RaylibMaterialLibrary? _materialLibrary;
        private const int DefaultMaxModelInstancesPerDraw = 32768;
        private const int HardMaxModelInstancesPerDraw = 131072;
        private const uint ShadowColorKey = 0;

        private bool _initialized;
        internal const float DefaultVegetationAlphaCutoff = 0.9f;
        private const string BillboardTextureRequiredError = "RAYLIB.PRIMITIVE.ERR.BillboardTextureRequired";

        private Mesh _cubeMesh;
        private Mesh _sphereMesh;
        private Mesh _vfxBillboardMesh;
        private IRaylibReceiverMeshProjector? _receiverMeshProjector;
        private IContinuousHeightmap? _frameContinuousHeightmap;
        private Shader _shader;
        private Material _material;
        private RaylibLaneShader _instancingLane = null!;
        private readonly RaylibShaderCatalog _shaderCatalog = new();
        private Material _vfxMaterial;
        private bool _vfxMaterialLoaded;
        private readonly RaylibEffectShaderRegistry _effectShaders = new RaylibEffectShaderRegistry();
        private RaylibPbrUniformLocations _instancingPbrLocs;
        private RaylibFrameLighting? _frameLighting;
        private Vector3 _frameViewPos;
        private bool _hasFrameViewPos;
        private RaylibDirectionalShadowMap? _frameShadow;
        private float _frameShadowTexelWorld = 0.04f;
        private RaylibSkyIbl? _skyIbl;
        private RaylibLitModel? _immediateLit;
        private Mesh _billboardShadowMesh;

        private readonly List<Batch> _cubeBatches = new List<Batch>(16);
        private readonly List<Batch> _sphereBatches = new List<Batch>(16);
        private readonly Dictionary<long, ModelInstanceBatch> _modelInstanceBatches = new Dictionary<long, ModelInstanceBatch>();
        private readonly Dictionary<RaylibIsmRenderBridge.Bucket, ModelInstanceBatch> _staticModelInstanceBatches = new();
        private readonly Dictionary<RaylibIsmRenderBridge.Bucket, ModelInstanceBatch> _shadowInstanceBatches = new();
        private readonly Dictionary<int, ModelInstanceBatch> _typedLaneBatches = new();
        private readonly Dictionary<int, ModelInstanceBatch> _typedLaneShadowBatches = new();
        private readonly List<int> _typedLaneIdsSeen = new(8);
        private IRaylibInstancedBatchLaneSource? _instancedBatchLaneSource;
        private readonly RaylibIsmRenderBridge _ismBridge = new RaylibIsmRenderBridge();
        private readonly RaylibGpuSkinnedModelCache _gpuSkinnedModelCache;
        private readonly RaylibInstancedMaterialPipeline _materialPipeline;
        private readonly RaylibGpuSkinnedBatchRenderer _gpuSkinned;
        private readonly RaylibVfxRenderer _vfxRenderer;
        private readonly RaylibDecalProjectorRenderer _decalRenderer;
        private readonly RaylibStaticMeshReceiverProjector _staticMeshReceiverProjector = new();
        private readonly RaylibVegetationCutoutRenderer _vegetationCutout = new();
        private double _frameTimeSeconds;

        private readonly Dictionary<int, CachedModel> _modelCache = new Dictionary<int, CachedModel>();
        private readonly RaylibAssetStore<Texture2D> _textureStore;
        private Vector4[] _frameFrustumPlanes = Array.Empty<Vector4>();
        private bool _frameFrustumValid;
        private RaylibMatrix[] _laneCullScratch = Array.Empty<RaylibMatrix>();
        private const float UnitCubeRadiusMeters = 0.867f;
        public int LastInstancedLaneCullSkippedCount { get; private set; }
        private readonly RaylibAssetStore<Model> _modelStore;
        private readonly Dictionary<int, CachedProceduralMesh> _proceduralMeshCache = new Dictionary<int, CachedProceduralMesh>();
        private readonly Dictionary<int, CachedTexture> _textureCache = new Dictionary<int, CachedTexture>();
        private readonly Dictionary<string, Stack<IDisposable>> _residencyLeases = new(StringComparer.Ordinal);
        private IRenderMeshAssets? _residencyMeshAssets;
        private readonly HashSet<int> _reportedMissingModelDraws = new HashSet<int>();
        private Material _proceduralMeshMaterial;
        private bool _proceduralMeshMaterialLoaded;
        private readonly int _maxModelInstancesPerDraw;

        public int LastInstancedInstances { get; private set; }
        public int LastInstancedBatches { get; private set; }
        public double LastInstancedMatrixBuildMs { get; private set; }
        public double LastInstancedMeshDrawMs { get; private set; }
        public double LastPersistentSyncMs { get; private set; }
        public double LastPersistentBucketDrawMs { get; private set; }
        public double LastImmediateDrawMs { get; private set; }
        public int LastImmediateSkippedCount { get; private set; }
        public int LastInstancedMatrixCacheHits { get; private set; }
        public int LastInstancedMatrixCacheMisses { get; private set; }
        public int LastPersistentCreates { get; private set; }
        public int LastPersistentUpdates { get; private set; }
        public int LastPersistentRemoves { get; private set; }
        public int LastGpuSkinnedInstances { get; private set; }
        public int LastGpuSkinnedBatches { get; private set; }
        public double LastGpuSkinnedMatrixBuildMs { get; private set; }
        public double LastGpuSkinnedMeshDrawMs { get; private set; }
        public int LastMeshVisualCount { get; private set; }
        public int LastDecalVisualCount { get; private set; }
        public int LastVfxVisualCount { get; private set; }
        public int LastSurfaceVisualCount { get; private set; }
        public int TotalMeshVisualCount { get; private set; }
        public int TotalDecalVisualCount { get; private set; }
        public int TotalVfxVisualCount { get; private set; }
        public int TotalSurfaceVisualCount { get; private set; }
        public int LastDrawnVfxCount => _vfxRenderer.LastDrawnVfxCount;
        public int TotalDrawnVfxCount => _vfxRenderer.TotalDrawnVfxCount;

        public RaylibIsmRenderBridge IsmBridge => _ismBridge;

        public void BindResidencyMeshAssets(IRenderMeshAssets meshes)
        {
            _residencyMeshAssets = meshes ?? throw new ArgumentNullException(nameof(meshes));
        }

        /// <summary>GPU 蒙皮模型/动画缓存；宿主骨骼挂点 provider 与绘制路径共用同一实例（同一动画数据源）。</summary>
        public RaylibGpuSkinnedModelCache GpuSkinnedModelCache => _gpuSkinnedModelCache;

        /// <summary>单件静态网格（StaticMesh 车道）的 Decal 接收面；宿主把它与地形接收面组合绑定。</summary>
        public IRaylibReceiverMeshProjector StaticMeshReceiverProjector => _staticMeshReceiverProjector;

        public void ApplyFrameLighting(
            RaylibFrameLighting lighting,
            Vector3 viewPos,
            RaylibDirectionalShadowMap? shadow = null,
            float shadowTexelWorld = 0.04f)
        {
            _frameLighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            _frameViewPos = viewPos;
            _hasFrameViewPos = true;
            _frameShadow = shadow;
            _frameShadowTexelWorld = shadowTexelWorld;
            if (_initialized)
            {
                ApplyLightingToInstancingLanes(lighting, viewPos);
                ApplyFrameShadowToInstancingLanes();
            }

            _gpuSkinned.ApplyFrameLighting(lighting, viewPos, shadow, shadowTexelWorld);

            if (_immediateLit != null)
            {
                _immediateLit.BeginFrame(lighting, viewPos, shadow, shadowTexelWorld);
            }
        }

        /// <summary>split-sum IBL：烘焙/重烘环境立方图 + LUT，光照与天空 uniform 推到全部已注册车道着色器，IBL 纹理挂合批材质槽位（与 shader 无关）。</summary>
        private void ApplyLightingToInstancingLanes(RaylibFrameLighting lighting, Vector3 viewPos)
        {
            if (!_initialized)
            {
                return;
            }

            _skyIbl ??= new RaylibSkyIbl();
            _skyIbl.Ensure(lighting);
            Vector3 zenith = lighting.SkyZenithColor;
            Vector3 ground = lighting.SkyGroundColor;
            foreach (RaylibLaneShader lane in _shaderCatalog.InstancingShaders)
            {
                lane.ApplyFrameLighting(lighting, viewPos);
                lane.ApplySkyUniforms(zenith, ground, envSpecular: 1f);
            }

            Rl.SetMaterialTexture(ref _material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP, _skyIbl.EnvCubemap);
            Rl.SetMaterialTexture(ref _material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_BRDF, _skyIbl.BrdfLut);
        }

        private void ApplyFrameShadowToInstancingLanes()
        {
            foreach (RaylibLaneShader lane in _shaderCatalog.InstancingShaders)
            {
                lane.ApplyFrameShadow(_frameShadow, _frameShadowTexelWorld);
            }
        }

        private void BindFrameShadow(ref Material material)
        {
            RaylibInstancedMaterialPipeline.BindFrameShadow(ref material, _frameShadow);
        }

        public void BindReceiverMeshProjector(IRaylibReceiverMeshProjector projector)
        {
            _receiverMeshProjector = projector ?? throw new ArgumentNullException(nameof(projector));
        }

        public void BindInstancedBatchLaneSource(IRaylibInstancedBatchLaneSource source)
        {
            _instancedBatchLaneSource = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>Surface 线框是调试可视化；宿主每帧用 RenderDebugState.DrawDebugDraw 与 cleanPerformanceMode 覆写。</summary>
        public bool DrawSurfaceWireBoxes { get; set; } = true;

        internal static IRaylibReceiverMeshProjector RequireBoundReceiverMeshProjector(
            IRaylibReceiverMeshProjector? projector,
            int stableId)
        {
            if (projector == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} Decal stableId={stableId} requires {nameof(BindReceiverMeshProjector)} before projected Decals can paint receiver meshes.");
            }

            return projector;
        }

        public RaylibPrimitiveRenderer(
            RaylibPrimitiveRenderMode mode = RaylibPrimitiveRenderMode.Immediate,
            IRenderAssetPathResolver? vfs = null,
            IRenderMaterialAssets? materials = null,
            System.Func<string, int>? channelRegistrar = null)
        {
            _mode = mode;
            _channelRegistrar = channelRegistrar ?? ThrowMissingChannelRegistrar;
            _vfs = vfs;
            _materials = materials;
            bool syncAssetLoad = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_SYNC_ASSET_LOAD") == "1";
            if (syncAssetLoad)
            {
                _textureStore = new RaylibAssetStore<Texture2D>(vfs, LoadTextureResource, RaylibNativeResources.UnloadTexture);
                _modelStore = new RaylibAssetStore<Model>(vfs, LoadModelResource, RaylibNativeResources.UnloadModel);
            }
            else
            {
                _textureStore = new RaylibAssetStore<Texture2D>(
                    vfs,
                    LoadTextureResource,
                    RaylibNativeResources.UnloadTexture,
                    cpuPrepare: fullPath => RaylibNativeResources.LoadImageFile(fullPath),
                    uploader: payload =>
                    {
                        var image = (Image)payload!;
                        Texture2D texture = RaylibNativeResources.LoadTextureFromImage(image);
                        Rl.UnloadImage(image);
                        return ValidateTexture(texture, "cpu-prepared image");
                    });
                _modelStore = new RaylibAssetStore<Model>(
                    vfs,
                    LoadModelResource,
                    RaylibNativeResources.UnloadModel,
                    cpuPrepare: fullPath => RaylibModelFileLoader.PrepareNativeLoadable(fullPath),
                    uploader: payload => LoadModelResource((string)payload!));
            }
            _materialLibrary = vfs != null && materials != null
                ? new RaylibMaterialLibrary(vfs, materials, _textureStore)
                : null;
            _maxModelInstancesPerDraw = ResolveMaxModelInstancesPerDraw();
            _gpuSkinnedModelCache = new RaylibGpuSkinnedModelCache(vfs, _modelStore);
            _materialPipeline = new RaylibInstancedMaterialPipeline(_materialLibrary);
            _gpuSkinned = new RaylibGpuSkinnedBatchRenderer(_gpuSkinnedModelCache, _materialPipeline, _maxModelInstancesPerDraw);
            _vfxRenderer = new RaylibVfxRenderer(vfs, _textureStore);
            _decalRenderer = new RaylibDecalProjectorRenderer(materials, _materialLibrary);
        }

        private static Texture2D LoadTextureResource(string fullPath)
        {
            Texture2D texture = RaylibNativeResources.LoadTexture(fullPath);
            return ValidateTexture(texture, fullPath);
        }

        private static Texture2D ValidateTexture(Texture2D texture, string fullPath)
        {
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"raylib rejected texture '{fullPath}' (textureId={texture.id}, size={texture.width}x{texture.height}).");
            }

            return texture;
        }

        private static Model LoadModelResource(string fullPath)
        {
            // OBJ 直走 native LoadModel 是 #1050 的 AccessViolation 路径；统一经
            // 装载入口分流（glTF native / OBJ、FBX、DAE 先转 GLB）。
            Model model = RaylibModelFileLoader.LoadModel(fullPath);
            if (model.meshCount <= 0)
            {
                RaylibNativeResources.UnloadModel(model);
                throw new InvalidOperationException($"model '{fullPath}' loaded with meshCount=0.");
            }

            return model;
        }

        /// <summary>帧末冲刷引用归零的退役资产；由唯一帧执行者在 pass 序列后调用（#1327 帧末延迟销毁）。</summary>
        public void FlushRetiredAssets()
        {
            _textureStore.FlushRetired();
            _modelStore.FlushRetired();
        }

        public int ResidentAssetCount => _modelStore.ResidentCount + _textureStore.ResidentCount;

        public int InFlightAssetCount => _modelStore.InFlightCount + _textureStore.InFlightCount + _gpuSkinnedModelCache.AnimationInFlightCount;

        public int RetiredAssetCount => _modelStore.RetiredCount + _textureStore.RetiredCount;

        /// <summary>
        /// Non-blocking backend bridge used by the map-load rendezvous. The residency lease is
        /// retained until <see cref="ReleaseAsset"/> so a focused map cannot lose its warm-up
        /// assets between the gate completing and the first draw.
        /// </summary>
        public RenderAssetResidencySnapshot EnsureAssetResident(in MapPresentationAsset asset)
        {
            string key = BuildResidencyKey(in asset);
            if (asset.SourceUris == null || asset.SourceUris.Length == 0)
            {
                return new RenderAssetResidencySnapshot(
                    RenderAssetResidencyState.Failed,
                    "required render asset has no source URI");
            }

            MeshAssetDescriptor descriptor = _residencyMeshAssets != null &&
                _residencyMeshAssets.TryGetDescriptor(asset.AssetId, out MeshAssetDescriptor registered)
                ? registered
                : MeshAssetDescriptor.Model(asset.AssetId, asset.SourceUris);
            if (asset.AssetKind is AssetKind.SkinnedMesh || asset.RenderPath == VisualRenderPath.GpuSkinnedInstance)
            {
                RaylibGpuSkinnedModelAcquireOutcome outcome = _gpuSkinnedModelCache.TryGetOrLoad(
                    asset.AssetId,
                    in descriptor,
                    out _,
                    out string? status);
                if (outcome == RaylibGpuSkinnedModelAcquireOutcome.InFlight)
                {
                    return BuildSkinnedResidencySnapshot(status);
                }

                if (outcome == RaylibGpuSkinnedModelAcquireOutcome.Failed)
                {
                    return new RenderAssetResidencySnapshot(RenderAssetResidencyState.Failed, status);
                }

                if (_gpuSkinnedModelCache.TryGetSelectedSourceUri(asset.AssetId, out string selectedUri))
                {
                    RaylibAssetAcquireOutcome selectedOutcome = _modelStore.TryAcquireOrBegin(
                        selectedUri,
                        out RaylibAssetStore<Model>.Lease? selectedLease,
                        out string? selectedFailure);
                    if (selectedOutcome == RaylibAssetAcquireOutcome.Resident)
                    {
                        RetainResidencyLease(key, selectedLease!);
                        return new RenderAssetResidencySnapshot(RenderAssetResidencyState.Resident);
                    }

                    if (selectedOutcome == RaylibAssetAcquireOutcome.InFlight)
                    {
                        return BuildResidencySnapshot(_modelStore, selectedUri, selectedFailure);
                    }

                    return new RenderAssetResidencySnapshot(
                        RenderAssetResidencyState.Failed,
                        $"selected skinned model source URI '{selectedUri}' is no longer resident: {selectedFailure}");
                }

                List<string>? failures = null;
                for (int i = 0; i < asset.SourceUris.Length; i++)
                {
                    string uri = asset.SourceUris[i];
                    RaylibAssetAcquireOutcome modelOutcome = _modelStore.TryAcquireOrBegin(
                        uri,
                        out RaylibAssetStore<Model>.Lease? lease,
                        out string? failure);
                    if (modelOutcome == RaylibAssetAcquireOutcome.InFlight)
                    {
                        return BuildResidencySnapshot(_modelStore, uri, failure);
                    }

                    if (modelOutcome == RaylibAssetAcquireOutcome.Resident)
                    {
                        RetainResidencyLease(key, lease!);
                        return new RenderAssetResidencySnapshot(RenderAssetResidencyState.Resident);
                    }

                    failures ??= new List<string>();
                    failures.Add($"'{uri}': {failure}");
                }

                return new RenderAssetResidencySnapshot(
                    RenderAssetResidencyState.Failed,
                    $"no skinned model source URI loaded: {string.Join("; ", failures ?? new List<string>())}");
            }

            if (descriptor.Type == MeshAssetType.Billboard)
            {
                List<string>? failures = null;
                for (int i = 0; i < asset.SourceUris.Length; i++)
                {
                    string uri = asset.SourceUris[i];
                    RaylibAssetAcquireOutcome outcome = _textureStore.TryAcquireOrBegin(
                        uri,
                        out RaylibAssetStore<Texture2D>.Lease? lease,
                        out string? status);
                    if (outcome == RaylibAssetAcquireOutcome.InFlight)
                    {
                        return BuildResidencySnapshot(_textureStore, uri, status);
                    }

                    if (outcome == RaylibAssetAcquireOutcome.Resident)
                    {
                        RetainResidencyLease(key, lease!);
                        return new RenderAssetResidencySnapshot(RenderAssetResidencyState.Resident);
                    }

                    failures ??= new List<string>();
                    failures.Add($"'{uri}': {status}");
                }

                return new RenderAssetResidencySnapshot(
                    RenderAssetResidencyState.Failed,
                    $"no billboard source URI loaded: {string.Join("; ", failures ?? new List<string>())}");
            }

            List<string>? modelFailures = null;
            for (int i = 0; i < asset.SourceUris.Length; i++)
            {
                string uri = asset.SourceUris[i];
                RaylibAssetAcquireOutcome modelOutcome = _modelStore.TryAcquireOrBegin(
                    uri,
                    out RaylibAssetStore<Model>.Lease? modelLease,
                    out string? modelStatus);
                if (modelOutcome == RaylibAssetAcquireOutcome.InFlight)
                {
                    return BuildResidencySnapshot(_modelStore, uri, modelStatus);
                }

                if (modelOutcome == RaylibAssetAcquireOutcome.Resident)
                {
                    RetainResidencyLease(key, modelLease!);
                    return new RenderAssetResidencySnapshot(RenderAssetResidencyState.Resident);
                }

                modelFailures ??= new List<string>();
                modelFailures.Add($"'{uri}': {modelStatus}");
            }

            return new RenderAssetResidencySnapshot(
                RenderAssetResidencyState.Failed,
                $"no model source URI loaded: {string.Join("; ", modelFailures ?? new List<string>())}");
        }

        public void ReleaseAsset(in MapPresentationAsset asset)
        {
            string key = BuildResidencyKey(in asset);
            if (!_residencyLeases.TryGetValue(key, out Stack<IDisposable>? leases) || leases.Count == 0)
            {
                return;
            }

            leases.Pop().Dispose();
            if (leases.Count == 0)
            {
                _residencyLeases.Remove(key);
            }
        }

        RenderAssetResidencySnapshot IRenderAssetResidency.EnsureResident(in MapPresentationAsset asset)
            => EnsureAssetResident(in asset);

        void IRenderAssetResidency.Release(in MapPresentationAsset asset)
            => ReleaseAsset(in asset);

        private static string BuildResidencyKey(in MapPresentationAsset asset)
        {
            string uris = asset.SourceUris == null ? string.Empty : string.Join('\u001f', asset.SourceUris);
            return $"{(byte)asset.AssetKind}:{asset.AssetId}:{(byte)asset.RenderPath}:{uris}";
        }

        private void RetainResidencyLease(string key, IDisposable lease)
        {
            if (!_residencyLeases.TryGetValue(key, out Stack<IDisposable>? leases))
            {
                leases = new Stack<IDisposable>();
                _residencyLeases.Add(key, leases);
            }

            leases.Push(lease);
        }

        private static RenderAssetResidencySnapshot BuildResidencySnapshot<T>(
            RaylibAssetStore<T> store,
            string uri,
            string? status)
            where T : struct
        {
            if (store.TryGetState(uri, out RaylibAssetState state, out string? failure, out _))
            {
                return new RenderAssetResidencySnapshot(MapResidencyState(state), failure ?? status);
            }

            return new RenderAssetResidencySnapshot(RenderAssetResidencyState.Unrequested, status);
        }

        private static RenderAssetResidencySnapshot BuildSkinnedResidencySnapshot(string? status)
        {
            if (Enum.TryParse(status, ignoreCase: false, out RaylibAssetState state))
            {
                return new RenderAssetResidencySnapshot(MapResidencyState(state), status);
            }

            return new RenderAssetResidencySnapshot(RenderAssetResidencyState.Preparing, status);
        }

        private static RenderAssetResidencyState MapResidencyState(RaylibAssetState state)
        {
            return state switch
            {
                RaylibAssetState.Unrequested => RenderAssetResidencyState.Unrequested,
                RaylibAssetState.Preparing => RenderAssetResidencyState.Preparing,
                RaylibAssetState.CpuReady => RenderAssetResidencyState.CpuReady,
                RaylibAssetState.UploadQueued => RenderAssetResidencyState.UploadQueued,
                RaylibAssetState.Resident => RenderAssetResidencyState.Resident,
                RaylibAssetState.Failed => RenderAssetResidencyState.Failed,
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported raylib asset state."),
            };
        }

        public void Draw(IPrimitiveDrawSnapshot draw, Camera3D camera, IRenderMeshAssets meshes, float scaleMul = 1f, IContinuousHeightmap? continuousHeightmap = null, double timeSeconds = 0d)
        {
            Draw(draw, camera, snapshot: null, skinnedBatch: null, meshes, scaleMul, continuousHeightmap, timeSeconds);
        }

        public void Draw(IPrimitiveDrawSnapshot draw, Camera3D camera, IPrimitiveDrawSnapshot? snapshot, IRenderMeshAssets meshes, float scaleMul = 1f, IContinuousHeightmap? continuousHeightmap = null, double timeSeconds = 0d)
        {
            Draw(draw, camera, snapshot, skinnedBatch: null, meshes, scaleMul, continuousHeightmap, timeSeconds);
        }

        public void Draw(
            IPrimitiveDrawSnapshot draw,
            Camera3D camera,
            IPrimitiveDrawSnapshot? snapshot,
            ISkinnedVisualBatchSnapshot? skinnedBatch,
            IRenderMeshAssets meshes,
            float scaleMul = 1f,
            IContinuousHeightmap? continuousHeightmap = null,
            double timeSeconds = 0d)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            _textureStore.PumpUploads();
            _modelStore.PumpUploads();
            BuildFrameFrustum(in camera);
            _frameViewPos = camera.position;
            _hasFrameViewPos = true;
            _frameTimeSeconds = timeSeconds;

            LastInstancedInstances = 0;
            LastInstancedBatches = 0;
            LastInstancedMatrixBuildMs = 0d;
            LastInstancedMeshDrawMs = 0d;
            LastPersistentSyncMs = 0d;
            LastPersistentBucketDrawMs = 0d;
            LastImmediateDrawMs = 0d;
            LastImmediateSkippedCount = 0;
            LastInstancedMatrixCacheHits = 0;
            LastInstancedMatrixCacheMisses = 0;
            LastInstancedLaneCullSkippedCount = 0;
            LastPersistentCreates = 0;
            LastPersistentUpdates = 0;
            LastPersistentRemoves = 0;
            LastGpuSkinnedInstances = 0;
            LastGpuSkinnedBatches = 0;
            LastGpuSkinnedMatrixBuildMs = 0d;
            LastGpuSkinnedMeshDrawMs = 0d;
            _gpuSkinned.ResetStats();
            LastMeshVisualCount = 0;
            LastDecalVisualCount = 0;
            LastVfxVisualCount = 0;
            LastSurfaceVisualCount = 0;
            _frameContinuousHeightmap = continuousHeightmap;
            _vfxRenderer.BeginFrame();
            _staticMeshReceiverProjector.BeginFrame();
            try
            {
                var span = draw.GetSpan();
                bool usePersistentStaticLanes = snapshot != null;
                if (usePersistentStaticLanes)
                {
                    _ismBridge.SyncPersistentLanes(snapshot);
                    LastPersistentSyncMs = _ismBridge.LastPersistentSyncMs;
                    LastPersistentCreates = _ismBridge.Planner.LastCreateCount;
                    LastPersistentUpdates = _ismBridge.Planner.LastUpdateCount;
                    LastPersistentRemoves = _ismBridge.Planner.LastRemoveCount;
                    long bucketStart = Stopwatch.GetTimestamp();
                    DrawPersistentStaticLanes(camera, meshes, scaleMul);
                    LastPersistentBucketDrawMs = (Stopwatch.GetTimestamp() - bucketStart) * 1000d / Stopwatch.Frequency;
                    DrawInstancedBatchLanes(meshes, scaleMul);
                    if (skinnedBatch != null)
                    {
                        DrawSkinnedBatch(skinnedBatch, camera, meshes, scaleMul);
                    }

                    long dynamicLaneStart = Stopwatch.GetTimestamp();
                    DrawSnapshotDynamicLanes(span, camera, meshes, scaleMul, skinnedBatchActive: skinnedBatch != null);
                    LastImmediateDrawMs = (Stopwatch.GetTimestamp() - dynamicLaneStart) * 1000d / Stopwatch.Frequency;

                    return;
                }

                if (_mode == RaylibPrimitiveRenderMode.Instanced)
                {
                    DrawHybridInstanced(span, camera, meshes, scaleMul);
                    return;
                }

                long immediateDrawStart = Stopwatch.GetTimestamp();
                DrawImmediateWithDescriptors(span, camera, meshes, scaleMul, persistentStaticLanesActive: false, skinnedBatchActive: false);
                LastImmediateDrawMs = (Stopwatch.GetTimestamp() - immediateDrawStart) * 1000d / Stopwatch.Frequency;
            }
            finally
            {
                _vfxRenderer.EndFrame();
                _frameContinuousHeightmap = null;
            }
        }

        public void DrawShadow(
            IPrimitiveDrawSnapshot draw,
            RaylibDirectionalShadowMap shadow,
            IRenderMeshAssets meshes,
            Camera3D camera,
            float scaleMul = 1f)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (shadow == null) throw new ArgumentNullException(nameof(shadow));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            _textureStore.PumpUploads();
            _modelStore.PumpUploads();

            EnsureInitialized();
            var span = draw.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (item.Visibility != VisualVisibility.Visible ||
                    item.AssetKind == AssetKind.VFX ||
                    item.AssetKind == AssetKind.Decal)
                {
                    continue;
                }

                DrawShadowLeafAsset(
                    item.MeshAssetId,
                    item.Position,
                    item.Rotation,
                    item.Scale * scaleMul,
                    camera,
                    meshes,
                    shadow,
                    item.MaterialId);
            }

            DrawInstancedBatchLaneShadows(meshes, shadow, scaleMul);
        }

        public void DrawShadow(
            ISkinnedVisualBatchSnapshot skinnedBatch,
            RaylibDirectionalShadowMap shadow,
            IRenderMeshAssets meshes,
            float scaleMul = 1f)
        {
            _textureStore.PumpUploads();
            _modelStore.PumpUploads();
            if (skinnedBatch == null) throw new ArgumentNullException(nameof(skinnedBatch));
            if (shadow == null) throw new ArgumentNullException(nameof(shadow));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            var span = skinnedBatch.GetSpan();
            _gpuSkinned.Prepare();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (item.Visibility != VisualVisibility.Visible)
                {
                    continue;
                }

                if (!RaylibMaterialDrawState.CastsShadow(RaylibMaterialDrawState.ResolveBlendMode(
                        _materials,
                        item.MaterialId,
                        MaterialBlendMode.Opaque,
                        $"{nameof(RaylibPrimitiveRenderer)} skinned shadow")))
                {
                    continue;
                }

                if (_gpuSkinned.TrySubmit(in item, meshes, scaleMul, out RaylibGpuSkinnedSubmitOutcome submitOutcome))
                {
                    continue;
                }

                if (submitOutcome == RaylibGpuSkinnedSubmitOutcome.InFlight)
                {
                    continue;
                }

                DrawShadowLeafAsset(
                    item.MeshAssetId,
                    item.Position,
                    item.Rotation,
                    item.Scale * scaleMul,
                    default,
                    meshes,
                    shadow,
                    item.MaterialId);
            }

            _gpuSkinned.FlushShadow(shadow);
            LastGpuSkinnedInstances = _gpuSkinned.LastInstances;
            LastGpuSkinnedBatches = _gpuSkinned.LastBatches;
            LastGpuSkinnedMatrixBuildMs = _gpuSkinned.LastMatrixBuildMs;
            LastGpuSkinnedMeshDrawMs = _gpuSkinned.LastMeshDrawMs;
        }

        private void DrawPersistentStaticLanes(Camera3D camera, IRenderMeshAssets meshes, float scaleMul)
        {
            foreach (RaylibIsmRenderBridge.Bucket bucket in _ismBridge.ActiveBuckets)
            {
                if (bucket.Lane.RenderPath == VisualRenderPath.StaticMesh)
                {
                    RegisterStaticMeshReceiverBucket(bucket, meshes, scaleMul);
                }

                DrawInstancedBucket(bucket, meshes, scaleMul);
            }
        }

        /// <summary>
        /// 单件静态网格车道注册为 Decal 接收面。注册发生在重画可见面之前且使用与可见 pass 完全相同的
        /// TRS 与网格缓存，保证贴花重画与已画表面逐顶点对齐。Billboard 是面向相机的 splat，不是接收面；
        /// 无法加载的模型同样不可见地跳过（可见 pass 已负责告警/抛错）。
        /// </summary>
        private void RegisterStaticMeshReceiverBucket(RaylibIsmRenderBridge.Bucket bucket, IRenderMeshAssets meshes, float scaleMul)
        {
            EnsureInitialized();
            List<PrimitiveDrawItem> items = bucket.Items;
            for (int i = 0; i < items.Count; i++)
            {
                PrimitiveDrawItem item = items[i];
                if (!meshes.TryGetDescriptor(item.MeshAssetId, out MeshAssetDescriptor descriptor))
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} cannot register static receiver for unknown meshAssetId={item.MeshAssetId}.");
                }

                Vector3 scale = item.Scale * scaleMul;
                switch (descriptor.Type)
                {
                    case MeshAssetType.Primitive when descriptor.PrimitiveKind is PrimitiveMeshKind.Cube or PrimitiveMeshKind.Sphere:
                        _staticMeshReceiverProjector.RegisterReceiver(
                            item.StableId,
                            descriptor.PrimitiveKind == PrimitiveMeshKind.Cube ? _cubeMesh : _sphereMesh,
                            submeshes: null,
                            item.Position,
                            item.Rotation,
                            scale,
                            new Vector3(-0.5f),
                            new Vector3(0.5f));
                        break;
                    case MeshAssetType.Model:
                        if (!TryGetOrLoadModel(item.MeshAssetId, in descriptor, out CachedModel cached) || cached.Meshes.Length == 0)
                        {
                            break;
                        }

                        _staticMeshReceiverProjector.RegisterReceiver(
                            item.StableId,
                            cached.Meshes[0],
                            cached.Meshes,
                            item.Position,
                            item.Rotation,
                            scale,
                            cached.LocalMin,
                            cached.LocalMax);
                        break;
                    case MeshAssetType.ProceduralMesh:
                        if (!TryGetOrBuildProceduralMesh(item.MeshAssetId, in descriptor, out CachedProceduralMesh cachedProcedural))
                        {
                            break;
                        }

                        ProceduralMeshBounds localBounds = descriptor.ProceduralMeshData.LocalBounds;
                        _staticMeshReceiverProjector.RegisterReceiver(
                            item.StableId,
                            cachedProcedural.Mesh,
                            cachedProcedural.SubmeshMeshes,
                            item.Position,
                            item.Rotation,
                            scale,
                            localBounds.Min,
                            localBounds.Max);
                        break;
                    case MeshAssetType.Billboard:
                        break;
                    default:
                        break;
                }
            }
        }

        private void DrawImmediateWithDescriptors(
            ReadOnlySpan<PrimitiveDrawItem> span,
            Camera3D camera,
            IRenderMeshAssets meshes,
            float scaleMul,
            bool persistentStaticLanesActive,
            bool skinnedBatchActive)
        {
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (TryDrawTypedPresenterChild(in item, camera, meshes, scaleMul, instancedPrimitives: false))
                {
                    continue;
                }

                if (skinnedBatchActive && item.RenderPath.IsSkinnedLane())
                {
                    LastImmediateSkippedCount++;
                    continue;
                }

                if (ShouldSkipImmediateDraw(item, persistentStaticLanesActive))
                {
                    LastImmediateSkippedCount++;
                    continue;
                }

                if (TryDrawPrototypeSkinned(item, meshes, scaleMul))
                {
                    continue;
                }

                DrawAssetRecursive(
                    item.MeshAssetId, item.Position,
                    item.Rotation,
                    item.Scale * scaleMul, item.Color,
                    camera,
                    meshes,
                    item.MaterialId);
            }
        }

        private void DrawSnapshotDynamicLanes(
            ReadOnlySpan<PrimitiveDrawItem> span,
            Camera3D camera,
            IRenderMeshAssets meshes,
            float scaleMul,
            bool skinnedBatchActive)
        {
            EnsureInitialized();

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (TryDrawTypedPresenterChild(in item, camera, meshes, scaleMul, instancedPrimitives: true))
                {
                    continue;
                }

                if (skinnedBatchActive && item.RenderPath.IsSkinnedLane())
                {
                    LastImmediateSkippedCount++;
                    continue;
                }

                if (ShouldSkipImmediateDraw(item, persistentStaticLanesActive: true))
                {
                    LastImmediateSkippedCount++;
                    continue;
                }

                if (TryDrawPrototypeSkinned(item, meshes, scaleMul))
                {
                    continue;
                }

                SubmitAssetRecursive(
                    item.MeshAssetId,
                    item.Position,
                    item.Rotation,
                    item.Scale * scaleMul,
                    item.Color,
                    camera,
                    meshes,
                    item.MaterialId);
            }

            FlushInstancedBatches();
        }

        internal bool ShouldSkipImmediateDraw(in PrimitiveDrawItem item, bool persistentStaticLanesActive)
        {
            if (!persistentStaticLanesActive ||
                item.StableId <= 0 ||
                !StaticMeshLaneKey.Supports(item))
            {
                return false;
            }

            return _ismBridge.ActiveBindings.ContainsKey(item.StableId);
        }

        private void DrawHybridInstanced(ReadOnlySpan<PrimitiveDrawItem> span, Camera3D camera, IRenderMeshAssets meshes, float scaleMul)
        {
            EnsureInitialized();

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (TryDrawTypedPresenterChild(in item, camera, meshes, scaleMul, instancedPrimitives: true))
                {
                    continue;
                }

                if (TryDrawPrototypeSkinned(item, meshes, scaleMul))
                {
                    continue;
                }

                SubmitAssetRecursive(
                    item.MeshAssetId,
                    item.Position,
                    item.Rotation,
                    item.Scale * scaleMul,
                    item.Color,
                    camera,
                    meshes,
                    item.MaterialId);
            }

            FlushInstancedBatches();
        }

        private bool TryDrawTypedPresenterChild(
            in PrimitiveDrawItem item,
            Camera3D camera,
            IRenderMeshAssets meshes,
            float scaleMul,
            bool instancedPrimitives)
        {
            if (TryDrawDecalItem(in item, scaleMul))
            {
                return true;
            }

            if (item.AssetKind == AssetKind.VFX)
            {
                LastVfxVisualCount++;
                TotalVfxVisualCount++;
                _vfxRenderer.Draw(in item, meshes, camera, _frameTimeSeconds, scaleMul);
                return true;
            }

            if (item.AssetKind == AssetKind.Surface)
            {
                if (item.MeshAssetId <= 0)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} Surface stableId={item.StableId} requires a mesh asset. Author a Presenter Surface child instead of skipping the draw.");
                }

                LastSurfaceVisualCount++;
                TotalSurfaceVisualCount++;
                DrawLeafAsset(
                    item.MeshAssetId,
                    item.Position,
                    item.Rotation,
                    item.Scale * scaleMul,
                    item.Color,
                    camera,
                    meshes,
                    item.MaterialId,
                    instancedPrimitives: false,
                    countAsMesh: false);
                if (DrawSurfaceWireBoxes)
                {
                    DrawWireBox(
                        item.Position,
                        item.Scale * scaleMul,
                        VisualMath.NormalizeOrIdentity(item.Rotation),
                        MultiplyColor(item.Color, 1.18f, 1.08f, 0.86f, 0.96f));
                }

                return true;
            }

            if (item.RenderPath.IsSurfaceLane())
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} renderPath Surface stableId={item.StableId} assetKind={item.AssetKind} is not drawable. Author AssetKind.Surface Presenter children.");
            }

            return false;
        }

        private bool TryDrawDecalItem(in PrimitiveDrawItem item, float scaleMul)
        {
            if (item.AssetKind != AssetKind.Decal)
            {
                return false;
            }

            IRaylibReceiverMeshProjector projector = RequireBoundReceiverMeshProjector(
                _receiverMeshProjector,
                item.StableId);
            Vector3 scaled = item.Scale * scaleMul;
            ProjectedDecalVolume volume = ProjectedDecalVolume.FromVisualScale(scaled);
            EnsureInitialized();
            LastDecalVisualCount++;
            TotalDecalVisualCount++;
            _decalRenderer.Draw(
                item.Position,
                item.Rotation,
                in volume,
                item.Color,
                item.MaterialId,
                item.StableId,
                projector);
            return true;
        }

        private void SubmitAssetRecursive(int meshAssetId, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color, Camera3D camera, IRenderMeshAssets meshes, int materialId = 0)
        {
            DrawLeafAsset(meshAssetId, position, rotation, scale, color, camera, meshes, materialId, instancedPrimitives: true);
        }

        private void SubmitPrimitive(PrimitiveMeshKind kind, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color, int materialId = 0)
        {
            uint key = RaylibInstancedMaterialPipeline.PackRgba(color);
            var matrix = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(rotation)) *
                Matrix4x4.CreateTranslation(position));

            if (kind == PrimitiveMeshKind.Cube)
            {
                AddInstance(_cubeBatches, key, materialId, matrix);
                return;
            }

            if (kind == PrimitiveMeshKind.Sphere)
            {
                AddInstance(_sphereBatches, key, materialId, matrix);
                return;
            }

            DrawPrimitive(kind, position, rotation, scale, color);
        }

        private bool TryDrawPrototypeSkinned(in PrimitiveDrawItem item, IRenderMeshAssets meshes, float scaleMul)
        {
            if (!item.RenderPath.IsSkinnedLane() ||
                !meshes.TryGetDescriptor(item.MeshAssetId, out var descriptor) ||
                descriptor.Type != MeshAssetType.Primitive)
            {
                return false;
            }

            Vector3 scale = item.Scale * scaleMul;
            float baseYaw = ExtractYawRad(item.Rotation);
            AnimationOverlayRequest sourceOverlay = item.AnimationOverlay;
            AnimatorPackedState sourceAnimator = item.Animator;
            AnimationOverlayRequest overlay = ResolvePrototypeOverlay(in sourceOverlay, in sourceAnimator);

            switch (descriptor.PrimitiveKind)
            {
                case PrimitiveMeshKind.Cube:
                    DrawTankPrototype(item.Position, scale, item.Color, baseYaw, in overlay);
                    return true;

                case PrimitiveMeshKind.Sphere:
                    DrawHumanoidPrototype(item.Position, scale, item.Color, baseYaw, in overlay);
                    return true;

                default:
                    return false;
            }
        }

        private void DrawSkinnedBatch(ISkinnedVisualBatchSnapshot skinnedBatch, Camera3D camera, IRenderMeshAssets meshes, float scaleMul)
        {
            var span = skinnedBatch.GetSpan();
            if (!_gpuSkinned.BatchesPreparedForShadow)
            {
                _gpuSkinned.Prepare();
            }
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (item.Visibility != VisualVisibility.Visible)
                {
                    continue;
                }

                if (_gpuSkinned.TrySubmit(in item, meshes, scaleMul, out RaylibGpuSkinnedSubmitOutcome submitOutcome))
                {
                    continue;
                }

                if (submitOutcome == RaylibGpuSkinnedSubmitOutcome.InFlight)
                {
                    continue;
                }

                if (TryDrawPrototypeSkinned(item, meshes, scaleMul))
                {
                    continue;
                }

                DrawAssetRecursive(
                    item.MeshAssetId,
                    item.Position,
                    item.Rotation,
                    item.Scale * scaleMul,
                    item.Color,
                    camera,
                    meshes,
                    item.MaterialId);
            }

            if (_gpuSkinned.HasActiveBatches)
            {
                EnsureInitialized();
            }
            _gpuSkinned.Flush(_shader, in _instancingPbrLocs, _skyIbl);
            LastGpuSkinnedInstances = _gpuSkinned.LastInstances;
            LastGpuSkinnedBatches = _gpuSkinned.LastBatches;
            LastGpuSkinnedMatrixBuildMs = _gpuSkinned.LastMatrixBuildMs;
            LastGpuSkinnedMeshDrawMs = _gpuSkinned.LastMeshDrawMs;
        }
        private bool TryDrawPrototypeSkinned(in SkinnedVisualBatchItem item, IRenderMeshAssets meshes, float scaleMul)
        {
            if (!item.RenderPath.IsSkinnedLane() ||
                !meshes.TryGetDescriptor(item.MeshAssetId, out var descriptor) ||
                descriptor.Type != MeshAssetType.Primitive)
            {
                return false;
            }

            Vector3 scale = item.Scale * scaleMul;
            float baseYaw = ExtractYawRad(item.Rotation);
            AnimationOverlayRequest sourceOverlay = item.AnimationOverlay;
            AnimatorPackedState sourceAnimator = item.Animator;
            AnimationOverlayRequest overlay = ResolvePrototypeOverlay(in sourceOverlay, in sourceAnimator);

            switch (descriptor.PrimitiveKind)
            {
                case PrimitiveMeshKind.Cube:
                    DrawTankPrototype(item.Position, scale, item.Color, baseYaw, in overlay);
                    return true;

                case PrimitiveMeshKind.Sphere:
                    DrawHumanoidPrototype(item.Position, scale, item.Color, baseYaw, in overlay);
                    return true;

                default:
                    return false;
            }
        }

        private AnimationOverlayRequest ResolvePrototypeOverlay(
            in AnimationOverlayRequest overlay,
            in AnimatorPackedState animator)
        {
            if (overlay.HasAnyClip)
            {
                return overlay;
            }

            if ((animator.GetFlags() & AnimatorPackedStateFlags.Active) == 0)
            {
                return default;
            }

            return new AnimationOverlayRequest
            {
                BaseClip = AnimationChannelState.Create(
                    _channelRegistrar(WellKnownAnimationChannelNames.Locomotion),
                    animator.GetNormalizedTime01(),
                    weight01: 1f),
            };
        }

        private void DrawAssetRecursive(int meshAssetId, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color, Camera3D camera, IRenderMeshAssets meshes, int materialId = 0)
        {
            DrawLeafAsset(meshAssetId, position, rotation, scale, color, camera, meshes, materialId, instancedPrimitives: false);
        }

        private void DrawLeafAsset(
            int meshAssetId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Vector4 color,
            Camera3D camera,
            IRenderMeshAssets meshes,
            int materialId,
            bool instancedPrimitives,
            bool countAsMesh = true)
        {
            if (!meshes.TryGetDescriptor(meshAssetId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} cannot draw unknown meshAssetId={meshAssetId}.");
            }

            switch (descriptor.Type)
            {
                case MeshAssetType.Primitive:
                    if (countAsMesh)
                    {
                        CountMeshVisual();
                    }

                    if (instancedPrimitives)
                    {
                        SubmitPrimitive(descriptor.PrimitiveKind, position, rotation, scale, color, materialId);
                    }
                    else
                    {
                        DrawPrimitive(descriptor.PrimitiveKind, position, rotation, scale, color);
                    }

                    return;
                case MeshAssetType.Model:
                    if (countAsMesh)
                    {
                        CountMeshVisual();
                    }

                    DrawModel(meshAssetId, descriptor, position, rotation, scale, color, materialId);
                    return;
                case MeshAssetType.Billboard:
                    if (countAsMesh)
                    {
                        CountMeshVisual();
                    }

                    DrawBillboard(meshAssetId, descriptor, position, scale, color, camera, materialId);
                    return;
                case MeshAssetType.ProceduralMesh:
                    if (countAsMesh)
                    {
                        CountMeshVisual();
                    }

                    DrawProceduralMesh(meshAssetId, in descriptor, position, rotation, scale, materialId);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} refuses composite mesh type '{descriptor.Type}' for meshAssetId={meshAssetId}. Author Presenter children instead of Prefab.");
            }
        }

        private void DrawShadowLeafAsset(
            int meshAssetId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Camera3D camera,
            IRenderMeshAssets meshes,
            RaylibDirectionalShadowMap shadow,
            int materialId)
        {
            if (!meshes.TryGetDescriptor(meshAssetId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} cannot shadow unknown meshAssetId={meshAssetId}.");
            }

            MaterialBlendMode blendMode = RaylibMaterialDrawState.ResolveBlendMode(
                _materials,
                materialId,
                MaterialBlendMode.Opaque,
                $"{nameof(RaylibPrimitiveRenderer)} shadow");
            if (!RaylibMaterialDrawState.CastsShadow(blendMode))
            {
                return;
            }

            switch (descriptor.Type)
            {
                case MeshAssetType.Primitive:
                    DrawPrimitiveShadow(descriptor.PrimitiveKind, position, rotation, scale, shadow);
                    return;
                case MeshAssetType.Model:
                    DrawModelShadow(meshAssetId, in descriptor, position, rotation, scale, shadow);
                    return;
                case MeshAssetType.Billboard:
                    DrawBillboardShadow(meshAssetId, in descriptor, position, scale, camera, shadow, blendMode);
                    return;
                case MeshAssetType.ProceduralMesh:
                    DrawProceduralMeshShadow(meshAssetId, in descriptor, position, rotation, scale, shadow);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} refuses composite shadow mesh type '{descriptor.Type}' for meshAssetId={meshAssetId}. Author Presenter children instead of Prefab.");
            }
        }

        private void CountMeshVisual()
        {
            LastMeshVisualCount++;
            TotalMeshVisualCount++;
        }


        public string BuildVisualKindDiagnosticSummary()
        {
            return $"typed-visual-counts lastFrame(mesh={LastMeshVisualCount},decal={LastDecalVisualCount},vfx={LastVfxVisualCount},surface={LastSurfaceVisualCount}) total(mesh={TotalMeshVisualCount},decal={TotalDecalVisualCount},vfx={TotalVfxVisualCount},surface={TotalSurfaceVisualCount}) vfx-draws(last={_vfxRenderer.LastDrawnVfxCount},total={_vfxRenderer.TotalDrawnVfxCount})";
        }

        public string BuildPrimitiveLaneDiagnosticSummary(IRenderMeshAssets meshes)
        {
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            int bucketCount = _ismBridge.ActiveBuckets.Count;
            if (bucketCount == 0)
            {
                return "primitive-lane bucketCount=0";
            }

            RaylibIsmRenderBridge.Bucket bucket = _ismBridge.ActiveBuckets[0];
            int itemCount = bucket.Items.Count;
            if (itemCount == 0)
            {
                return $"primitive-lane bucketCount={bucketCount} firstBucketItems=0";
            }

            PrimitiveDrawItem item = bucket.Items[0];
            string mesh = meshes.TryGetDescriptor(item.MeshAssetId, out MeshAssetDescriptor descriptor)
                ? $"{descriptor.Type}/{descriptor.PrimitiveKind}"
                : "missing";
            return $"primitive-lane bucketCount={bucketCount} firstBucketItems={itemCount} mesh={mesh} meshAssetId={item.MeshAssetId} stable={item.StableId} renderPath={item.RenderPath} mobility={item.Mobility} visibility={item.Visibility} pos=({item.Position.X:F2},{item.Position.Y:F2},{item.Position.Z:F2}) scale=({item.Scale.X:F2},{item.Scale.Y:F2},{item.Scale.Z:F2}) color=({item.Color.X:F2},{item.Color.Y:F2},{item.Color.Z:F2},{item.Color.W:F2})";
        }

        private void DrawPrimitive(PrimitiveMeshKind kind, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color)
        {
            EnsureInitialized();
            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(rotation)) *
                Matrix4x4.CreateTranslation(position));
            EnsureImmediateLitFrame();

            if (kind == PrimitiveMeshKind.Cube)
            {
                _immediateLit!.DrawMesh(_cubeMesh, transform, color);
            }
            else if (kind == PrimitiveMeshKind.Sphere)
            {
                _immediateLit!.DrawMesh(_sphereMesh, transform, color);
            }
        }

        private void DrawPrimitiveShadow(
            PrimitiveMeshKind kind,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            RaylibDirectionalShadowMap shadow)
        {
            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(rotation)) *
                Matrix4x4.CreateTranslation(position));

            if (kind == PrimitiveMeshKind.Cube)
            {
                shadow.DrawMeshShadow(_cubeMesh, transform);
            }
            else if (kind == PrimitiveMeshKind.Sphere)
            {
                shadow.DrawMeshShadow(_sphereMesh, transform);
            }
        }

        private static void DrawTransformedPrimitive(in RaylibMatrix transform, PrimitiveMeshKind kind, Color color)
        {
            Rl.rlDrawRenderBatchActive();
            Rl.rlPushMatrix();
            try
            {
                MultMatrix(in transform);
                if (kind == PrimitiveMeshKind.Cube)
                {
                    Rl.DrawCube(Vector3.Zero, 1f, 1f, 1f, color);
                }
                else if (kind == PrimitiveMeshKind.Sphere)
                {
                    Rl.DrawSphere(Vector3.Zero, 0.5f, color);
                }
            }
            finally
            {
                Rl.rlDrawRenderBatchActive();
                Rl.rlPopMatrix();
            }
        }

        private static unsafe void MultMatrix(in RaylibMatrix matrix)
        {
            float* values = stackalloc float[16]
            {
                matrix.m0, matrix.m1, matrix.m2, matrix.m3,
                matrix.m4, matrix.m5, matrix.m6, matrix.m7,
                matrix.m8, matrix.m9, matrix.m10, matrix.m11,
                matrix.m12, matrix.m13, matrix.m14, matrix.m15
            };
            Rl.rlMultMatrixf(values);
        }

        private void DrawTankPrototype(Vector3 position, Vector3 scale, Vector4 color, float baseYaw, in AnimationOverlayRequest overlay)
        {
            float unit = MathF.Max(0.12f, MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)) * 0.45f);
            float locomotionPhase = ResolveClipTime01(overlay.BaseClip, WellKnownAnimationChannelNames.Locomotion);
            float locomotionWeight = ResolveClipWeight01(overlay.BaseClip, WellKnownAnimationChannelNames.Locomotion);
            float aimYaw = ResolveClipScalar0(overlay.LayerClip, WellKnownAnimationChannelNames.AimYaw) * ResolveClipWeight01(overlay.LayerClip, WellKnownAnimationChannelNames.AimYaw);
            float recoilPulse = ResolvePulse(overlay.OverlayClip, WellKnownAnimationChannelNames.Recoil);
            float treadBob = MathF.Sin(locomotionPhase * MathF.Tau) * unit * (0.03f + locomotionWeight * 0.08f);
            float turretYaw = baseYaw + aimYaw;
            float recoil = recoilPulse * unit * 0.35f;

            Vector4 hullColor = MultiplyColor(color, 0.72f, 0.78f, 0.84f, 1f);
            Vector4 turretColor = MultiplyColor(color, 0.95f, 0.95f, 0.82f, 1f);
            Vector4 accentColor = recoilPulse > 0.01f
                ? new Vector4(1f, 0.45f, 0.2f, 1f)
                : new Vector4(0.95f, 0.9f, 0.4f, 1f);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, PrototypeLocal(0f, unit * 0.52f + treadBob, 0f)),
                PrototypeSize(unit * 2.2f, unit * 0.7f, unit * 3.0f),
                baseYaw,
                hullColor);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, PrototypeLocal(unit * 0.92f, unit * 0.26f + treadBob, 0f)),
                PrototypeSize(unit * 0.38f, unit * 0.25f, unit * 2.7f),
                baseYaw,
                MultiplyColor(hullColor, 0.8f, 0.8f, 0.8f, 1f));

            DrawOrientedCube(
                TransformLocal(position, baseYaw, PrototypeLocal(-unit * 0.92f, unit * 0.26f + treadBob, 0f)),
                PrototypeSize(unit * 0.38f, unit * 0.25f, unit * 2.7f),
                baseYaw,
                MultiplyColor(hullColor, 0.8f, 0.8f, 0.8f, 1f));

            Vector3 turretCenter = TransformLocal(position, baseYaw, PrototypeLocal(0f, unit * 1.0f, 0f));
            DrawOrientedCube(
                turretCenter,
                PrototypeSize(unit * 1.1f, unit * 0.42f, unit * 1.3f),
                turretYaw,
                turretColor);

            DrawOrientedCube(
                TransformLocal(turretCenter, turretYaw, PrototypeLocal(0f, unit * 0.02f, unit * 1.15f - recoil)),
                PrototypeSize(unit * 0.18f, unit * 0.18f, unit * 2.25f),
                turretYaw,
                accentColor);

            DrawPrototypeSphere(
                TransformLocal(turretCenter, turretYaw, PrototypeLocal(0f, unit * 0.18f, unit * 2.15f - recoil)),
                unit * (0.1f + recoilPulse * 0.1f),
                accentColor);
        }

        private void DrawHumanoidPrototype(Vector3 position, Vector3 scale, Vector4 color, float baseYaw, in AnimationOverlayRequest overlay)
        {
            float unit = MathF.Max(0.1f, MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)) * 0.42f);
            float locomotionPhase = ResolveClipTime01(overlay.BaseClip, WellKnownAnimationChannelNames.Locomotion);
            float locomotionWeight = ResolveClipWeight01(overlay.BaseClip, WellKnownAnimationChannelNames.Locomotion);
            float lowerPhase = locomotionPhase * MathF.Tau;
            float stride = MathF.Sin(lowerPhase) * unit * (0.08f + locomotionWeight * 0.34f);
            float aimWeight = ResolveClipWeight01(overlay.LayerClip, WellKnownAnimationChannelNames.AimYaw);
            float upperYaw = baseYaw + ResolveClipScalar0(overlay.LayerClip, WellKnownAnimationChannelNames.AimYaw) * aimWeight;
            float recoilPulse = ResolvePulse(overlay.OverlayClip, WellKnownAnimationChannelNames.Recoil);
            float chestLift = recoilPulse * unit * 0.08f;

            Vector4 legColor = MultiplyColor(color, 0.72f, 0.85f, 1f, 1f);
            Vector4 torsoColor = LerpColor(
                MultiplyColor(color, 0.95f, 0.8f, 0.75f, 1f),
                new Vector4(1f, 0.45f, 0.25f, 1f),
                Math.Clamp(aimWeight * 0.6f, 0f, 1f));
            Vector4 weaponColor = recoilPulse > 0.01f
                ? new Vector4(1f, 0.5f, 0.25f, 1f)
                : new Vector4(0.9f, 0.9f, 0.95f, 1f);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, PrototypeLocal(0f, unit * 0.55f, 0f)),
                PrototypeSize(unit * 0.75f, unit * 0.55f, unit * 0.45f),
                baseYaw,
                legColor);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, PrototypeLocal(unit * 0.2f, unit * 0.18f, stride)),
                PrototypeSize(unit * 0.2f, unit * 0.78f, unit * 0.2f),
                baseYaw,
                legColor);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, PrototypeLocal(-unit * 0.2f, unit * 0.18f, -stride)),
                PrototypeSize(unit * 0.2f, unit * 0.78f, unit * 0.2f),
                baseYaw,
                legColor);

            Vector3 chestCenter = TransformLocal(position, upperYaw, PrototypeLocal(0f, unit * 1.3f + chestLift, 0f));
            DrawOrientedCube(
                chestCenter,
                PrototypeSize(unit * 0.82f, unit * 0.92f, unit * 0.4f),
                upperYaw,
                torsoColor);

            DrawPrototypeSphere(
                TransformLocal(chestCenter, upperYaw, PrototypeLocal(0f, unit * 0.82f, 0f)),
                unit * 0.28f,
                MultiplyColor(color, 1f, 0.92f, 0.86f, 1f));

            DrawOrientedCube(
                TransformLocal(chestCenter, upperYaw, PrototypeLocal(-unit * 0.48f, unit * 0.05f, unit * 0.05f)),
                PrototypeSize(unit * 0.16f, unit * 0.75f, unit * 0.16f),
                upperYaw - aimWeight * 0.15f,
                torsoColor);

            DrawOrientedCube(
                TransformLocal(chestCenter, upperYaw, PrototypeLocal(unit * 0.5f, unit * 0.02f, unit * (0.18f + aimWeight * 0.25f))),
                PrototypeSize(unit * 0.16f, unit * 0.7f, unit * 0.16f),
                upperYaw + aimWeight * 0.35f,
                torsoColor);

            Vector3 weaponCenter = TransformLocal(chestCenter, upperYaw, PrototypeLocal(unit * 0.18f, -unit * 0.02f, unit * 0.7f));
            DrawOrientedCube(
                weaponCenter,
                PrototypeSize(unit * 0.14f, unit * 0.14f, unit * 0.95f),
                upperYaw,
                weaponColor);

            if (recoilPulse > 0.01f)
            {
                DrawPrototypeSphere(
                    TransformLocal(weaponCenter, upperYaw, PrototypeLocal(0f, 0f, unit * 0.68f)),
                    unit * 0.14f,
                    new Vector4(1f, 0.62f, 0.2f, 1f));
            }
        }

        private float ResolveClipTime01(in AnimationChannelState clip, string channelName)
        {
            return MatchesChannel(clip, channelName) ? clip.NormalizedTime01 : 0f;
        }

        private float ResolveClipWeight01(in AnimationChannelState clip, string channelName)
        {
            return MatchesChannel(clip, channelName) ? clip.Weight01 : 0f;
        }

        private float ResolveClipScalar0(in AnimationChannelState clip, string channelName)
        {
            return MatchesChannel(clip, channelName) ? clip.Scalar0 : 0f;
        }

        private float ResolvePulse(in AnimationChannelState clip, string channelName)
        {
            if (!MatchesChannel(clip, channelName) || clip.Weight01 <= 0.001f)
            {
                return 0f;
            }

            return MathF.Sin(clip.NormalizedTime01 * MathF.PI) * clip.Weight01;
        }

        private bool MatchesChannel(in AnimationChannelState clip, string channelName)
        {
            int expectedId = _channelRegistrar(channelName);
            return expectedId > 0 && clip.ChannelId == expectedId;
        }

        private void DrawOrientedCube(Vector3 center, Vector3 size, float yawRad, Vector4 color)
        {
            DrawWireBox(center, size, yawRad, color);
        }

        private static Vector3 PrototypeLocal(float right, float up, float forward)
        {
            return new Vector3(forward, up, right);
        }

        private static Vector3 PrototypeSize(float rightWidth, float height, float forwardDepth)
        {
            return new Vector3(forwardDepth, height, rightWidth);
        }

        private static void DrawPrototypeSphere(Vector3 center, float radius, Vector4 color)
        {
            Rl.DrawSphere(center, radius, ToRaylibColor(color));
        }

        private static Vector3 TransformLocal(Vector3 origin, Quaternion rotation, Vector3 local)
        {
            return origin + Vector3.Transform(local, VisualMath.NormalizeOrIdentity(rotation));
        }

        private static Vector3 TransformLocal(Vector3 origin, float yawRad, Vector3 local)
        {
            return VisualMath.TransformVisualLocal2D(origin, yawRad, in local);
        }

        private static float ExtractYawRad(Quaternion rotation)
        {
            return VisualMath.TryExtractFacingRadFromVisualYRotation(rotation, out float facingRad)
                ? facingRad
                : 0f;
        }

        private static Vector4 MultiplyColor(Vector4 color, float r, float g, float b, float a)
        {
            return new Vector4(
                Math.Clamp(color.X * r, 0f, 1f),
                Math.Clamp(color.Y * g, 0f, 1f),
                Math.Clamp(color.Z * b, 0f, 1f),
                Math.Clamp(color.W * a, 0f, 1f));
        }

        private static Vector4 LerpColor(Vector4 from, Vector4 to, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vector4(
                from.X + (to.X - from.X) * t,
                from.Y + (to.Y - from.Y) * t,
                from.Z + (to.Z - from.Z) * t,
                from.W + (to.W - from.W) * t);
        }

        private void DrawModel(int meshAssetId, in MeshAssetDescriptor desc, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color, int materialId)
        {
            if (!TryGetOrLoadModel(meshAssetId, desc, out var cached))
            {
                WarnMissingModelSkipped(meshAssetId, stableId: 0, "model draw");
                return;
            }

            var tint = ToRaylibColor(color);
            var model = cached.Model;
            RaylibMaterialDrawState.RequireLaneShaderKey(_materials, materialId, $"{nameof(RaylibPrimitiveRenderer)} immediate model");
            ApplyHostMapsToModel(ref model, materialId);
            ToAxisAngleDegrees(rotation, out Vector3 axis, out float angleDegrees);
            RaylibInstancedMaterialPipeline.RestoreOpaqueModelState();
            Rl.DrawModelEx(model, position, axis, angleDegrees, scale, tint);
        }

        private void DrawModelShadow(
            int meshAssetId,
            in MeshAssetDescriptor desc,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            RaylibDirectionalShadowMap shadow)
        {
            if (!TryGetOrLoadModel(meshAssetId, desc, out CachedModel cached))
            {
                WarnMissingModelSkipped(meshAssetId, stableId: 0, "model shadow draw");
                return;
            }

            ToAxisAngleDegrees(rotation, out Vector3 axis, out float angleDegrees);
            shadow.DrawModelShadow(cached.Model, position, axis, angleDegrees, scale);
        }

        private void DrawBillboard(int meshAssetId, in MeshAssetDescriptor desc, Vector3 position, Vector3 scale, Vector4 color, Camera3D camera, int materialId)
        {
            if (!TryGetOrLoadTexture(meshAssetId, desc, out var cached, out bool textureInFlight))
            {
                if (textureInFlight)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"{BillboardTextureRequiredError}: meshAssetId={meshAssetId}, sourceUris={FormatSourceUris(desc.SourceUris)}.");
            }

            float height = MathF.Max(scale.Y, 0.05f);
            float width = height * cached.AspectRatio;
            var billboardPosition = new Vector3(position.X, position.Y + height * 0.5f, position.Z);
            var source = new Rectangle(0f, 0f, cached.Texture.width, cached.Texture.height);

            // Billboard art ships pre-colored; multiply once by frame lighting so night/dusk dims vegetation.
            byte alpha = Clamp01ToByte(color.W);
            Vector3 litRgb = ResolveBillboardLitTintRgb();
            MaterialBlendMode blendMode = RaylibMaterialDrawState.ResolveBlendMode(_materials, materialId, MaterialBlendMode.Opaque, nameof(RaylibPrimitiveRenderer));
            RaylibMaterialDrawState.RequireLaneShaderKey(_materials, materialId, $"{nameof(RaylibPrimitiveRenderer)} billboard");
            // DrawBillboardRec multiplies by tint; keep cutout colDiffuse at identity to avoid double-dim.
            Color tint = new Color(
                Clamp01ToByte(litRgb.X),
                Clamp01ToByte(litRgb.Y),
                Clamp01ToByte(litRgb.Z),
                alpha);
            bool doubleSided = IsMaterialDoubleSided(materialId);
            if (RenderDiagnostics.FileSinkEnabled)
            {
                RenderDiagnostics.Detail(
                    "billboard-draw",
                    meshAssetId,
                    $"pos=({billboardPosition.X:F2},{billboardPosition.Y:F2},{billboardPosition.Z:F2}) scale=({scale.X:F2},{scale.Y:F2},{scale.Z:F2}) size=({width:F2}x{height:F2}) alpha={alpha} blend={blendMode} materialId={materialId} cameraPos=({camera.position.X:F2},{camera.position.Y:F2},{camera.position.Z:F2}) cameraTarget=({camera.target.X:F2},{camera.target.Y:F2},{camera.target.Z:F2})");
            }

            if (doubleSided)
            {
                Rl.rlDisableBackfaceCulling();
            }

            bool blending = RaylibMaterialDrawState.TryBeginAuthorBlendMode(blendMode, nameof(RaylibPrimitiveRenderer));
            try
            {
                if (blendMode == MaterialBlendMode.Cutout)
                {
                    _vegetationCutout.DrawBillboard(
                        in camera,
                        cached.Texture,
                        in source,
                        billboardPosition,
                        new Vector2(width, height),
                        tint,
                        DefaultVegetationAlphaCutoff);
                }
                else
                {
                    Rl.DrawBillboardRec(camera, cached.Texture, source, billboardPosition, new Vector2(width, height), tint);
                }
            }
            finally
            {
                if (blending)
                {
                    Rl.EndBlendMode();
                }

                if (doubleSided)
                {
                    Rl.rlEnableBackfaceCulling();
                }
            }
        }

        private void DrawBillboardShadow(
            int meshAssetId,
            in MeshAssetDescriptor desc,
            Vector3 position,
            Vector3 scale,
            Camera3D camera,
            RaylibDirectionalShadowMap shadow,
            MaterialBlendMode blendMode)
        {
            if (!TryGetOrLoadTexture(meshAssetId, desc, out CachedTexture cached, out bool textureInFlight))
            {
                if (textureInFlight)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"{BillboardTextureRequiredError}: meshAssetId={meshAssetId}, sourceUris={FormatSourceUris(desc.SourceUris)}.");
            }

            float height = MathF.Max(scale.Y, 0.05f);
            float width = height * cached.AspectRatio;
            Vector3 center = new(position.X, position.Y + height * 0.5f, position.Z);
            Vector3 toCamera = camera.position - center;
            toCamera.Y = 0f;
            if (toCamera.LengthSquared() <= 0.0001f)
            {
                toCamera = Vector3.UnitZ;
            }

            float yaw = MathF.Atan2(toCamera.X, toCamera.Z);
            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(width, height, 1f) *
                Matrix4x4.CreateRotationY(yaw) *
                Matrix4x4.CreateTranslation(center));
            if (blendMode == MaterialBlendMode.Cutout)
            {
                shadow.DrawMeshShadowCutout(_billboardShadowMesh, transform, cached.Texture, DefaultVegetationAlphaCutoff);
                return;
            }

            shadow.DrawMeshShadow(_billboardShadowMesh, transform);
        }

        private void DrawProceduralMesh(
            int meshAssetId,
            in MeshAssetDescriptor descriptor,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            int instanceMaterialOverrideId)
        {
            if (!TryGetOrBuildProceduralMesh(meshAssetId, descriptor, out var cached))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} cannot draw procedural meshAssetId={meshAssetId} because it has no committed procedural payload.");
            }

            int[] materialIds = ResolveProceduralMaterialIds(meshAssetId, in descriptor, cached.SubmeshCount, instanceMaterialOverrideId);
            EnsureProceduralMeshMaterial();

            var transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(rotation)) *
                Matrix4x4.CreateTranslation(position));

            Rl.rlDisableBackfaceCulling();
            if (cached.SubmeshMeshes == null || cached.SubmeshMeshes.Length == 0)
            {
                Material material = ResolveProceduralDrawMaterial(materialIds[0]);
                Rl.DrawMesh(cached.Mesh, material, transform);
            }
            else
            {
                for (int i = 0; i < cached.SubmeshMeshes.Length; i++)
                {
                    Material material = ResolveProceduralDrawMaterial(materialIds[i]);
                    Rl.DrawMesh(cached.SubmeshMeshes[i], material, transform);
                }
            }
            Rl.rlEnableBackfaceCulling();
        }

        private void DrawProceduralMeshShadow(
            int meshAssetId,
            in MeshAssetDescriptor descriptor,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            RaylibDirectionalShadowMap shadow)
        {
            if (!TryGetOrBuildProceduralMesh(meshAssetId, descriptor, out CachedProceduralMesh cached))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} cannot shadow procedural meshAssetId={meshAssetId} because it has no committed procedural payload.");
            }

            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(rotation)) *
                Matrix4x4.CreateTranslation(position));

            if (cached.SubmeshMeshes == null || cached.SubmeshMeshes.Length == 0)
            {
                shadow.DrawMeshShadow(cached.Mesh, transform);
                return;
            }

            for (int i = 0; i < cached.SubmeshMeshes.Length; i++)
            {
                shadow.DrawMeshShadow(cached.SubmeshMeshes[i], transform);
            }
        }

        private Material ResolveProceduralDrawMaterial(int materialAssetId)
        {
            Material material = _proceduralMeshMaterial;
            _materialPipeline.ApplyHostMaterialMaps(ref material, materialAssetId, _proceduralMeshMaterial.shader, in _instancingPbrLocs);
            BindFrameShadow(ref material);
            return material;
        }

        private int[] ResolveProceduralMaterialIds(
            int meshAssetId,
            in MeshAssetDescriptor descriptor,
            int cachedSubmeshCount,
            int instanceMaterialOverrideId)
        {
            if (descriptor.Type != MeshAssetType.ProceduralMesh || descriptor.ProceduralMeshData == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} received meshAssetId={meshAssetId} without procedural mesh payload.");
            }

            ProceduralMeshAssetData procedural = descriptor.ProceduralMeshData;
            if (procedural.SubmeshCount <= 0)
            {
                throw new InvalidOperationException($"Procedural mesh assetId={meshAssetId} must commit at least one submesh.");
            }

            if (cachedSubmeshCount != procedural.SubmeshCount)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} requires one material per committed submesh for meshAssetId={meshAssetId}.");
            }

            if (instanceMaterialOverrideId > 0 && procedural.SubmeshCount > 1)
            {
                throw new InvalidOperationException(
                    $"Procedural mesh assetId={meshAssetId} uses {procedural.SubmeshCount} submeshes and cannot receive an instance material override.");
            }

            if (_materials == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} requires {nameof(IRenderMaterialAssets)} to validate procedural mesh material bindings.");
            }

            var materialIds = new int[procedural.SubmeshCount];
            for (int i = 0; i < procedural.SubmeshCount; i++)
            {
                int materialId = instanceMaterialOverrideId > 0
                    ? instanceMaterialOverrideId
                    : procedural.Submeshes[i].MaterialAssetId;
                if (!_materials.TryGet(materialId, out MaterialAssetDescriptor material))
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} received procedural meshAssetId={meshAssetId} with unknown materialId={materialId} for submesh {i}.");
                }

                if (material.Domain != MaterialAssetDomain.Surface)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} only supports surface-domain procedural mesh materials (meshAssetId={meshAssetId}, materialId={materialId}, domain={material.Domain}).");
                }

                RaylibMaterialDrawState.RequireLaneShaderKey(_materials, materialId, $"{nameof(RaylibPrimitiveRenderer)} procedural mesh");

                materialIds[i] = materialId;
            }

            return materialIds;
        }

        private bool TryGetOrLoadModel(int meshAssetId, in MeshAssetDescriptor desc, out CachedModel cached)
        {
            if (_modelCache.TryGetValue(meshAssetId, out cached))
                return cached.Loaded;

            cached = new CachedModel { Loaded = false };

            if (_vfs == null || desc.SourceUris == null || desc.SourceUris.Length == 0)
            {
                _modelCache[meshAssetId] = cached;
                return false;
            }

            List<string>? failures = null;
            foreach (string uri in desc.SourceUris)
            {
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                RaylibAssetAcquireOutcome outcome = _modelStore.TryAcquireOrBegin(uri, out RaylibAssetStore<Model>.Lease? lease, out string? status);
                if (outcome == RaylibAssetAcquireOutcome.InFlight)
                {
                    // 两阶段装载进行中：本帧不绘制、不记负缓存，下一帧重问（#1328）。
                    return false;
                }

                if (outcome == RaylibAssetAcquireOutcome.Failed)
                {
                    failures ??= new List<string>();
                    failures.Add($"'{uri}': {status}");
                    continue;
                }

                try
                {
                    Mesh[] modelMeshes = CopyModelMeshes(lease!.Resource);
                    ComputeModelLocalAabbMeters(modelMeshes, out Vector3 localMin, out Vector3 localMax);
                    cached = new CachedModel
                    {
                        Lease = lease,
                        Meshes = modelMeshes,
                        LocalMin = localMin,
                        LocalMax = localMax,
                        Loaded = true,
                    };
                    _modelCache[meshAssetId] = cached;
                    return true;
                }
                catch
                {
                    lease!.Dispose();
                    throw;
                }
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibPrimitiveRenderer)} meshAssetId={meshAssetId} could not load any sourceUri. Attempts: [{string.Join("; ", failures ?? new List<string>())}]");
        }
        private static Mesh[] CopyModelMeshes(Model model)
        {
            var modelMeshes = new Mesh[model.meshCount];
            for (int i = 0; i < modelMeshes.Length; i++)
            {
                modelMeshes[i] = model.meshes[i];
            }

            return modelMeshes;
        }

        internal static void ComputeModelLocalAabbMeters(Mesh[] modelMeshes, out Vector3 localMin, out Vector3 localMax)
        {
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;
            bool anyVertex = false;
            for (int i = 0; i < modelMeshes.Length; i++)
            {
                Mesh mesh = modelMeshes[i];
                if (mesh.vertices == null || mesh.vertexCount <= 0)
                {
                    continue;
                }

                int floatCount = mesh.vertexCount * 3;
                for (int f = 0; f < floatCount; f += 3)
                {
                    anyVertex = true;
                    float x = mesh.vertices[f];
                    float y = mesh.vertices[f + 1];
                    float z = mesh.vertices[f + 2];
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (z < minZ) minZ = z;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                    if (z > maxZ) maxZ = z;
                }
            }

            if (!anyVertex)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} static receiver model has no vertices; the local AABB is undefined.");
            }

            localMin = new Vector3(minX, minY, minZ);
            localMax = new Vector3(maxX, maxY, maxZ);
        }

        private bool TryGetOrBuildProceduralMesh(int meshAssetId, in MeshAssetDescriptor desc, out CachedProceduralMesh cached)
        {
            ProceduralMeshAssetData? proceduralMesh = desc.ProceduralMeshData;
            if (proceduralMesh == null || proceduralMesh.VertexCount <= 0)
            {
                cached = default;
                return false;
            }

            if (_proceduralMeshCache.TryGetValue(meshAssetId, out cached) &&
                cached.Loaded &&
                cached.Generation == proceduralMesh.Generation)
            {
                return true;
            }

            UnloadProceduralMeshCache(in cached);

            cached = new CachedProceduralMesh
            {
                Mesh = CreateProceduralMesh(proceduralMesh),
                SubmeshMeshes = proceduralMesh.SubmeshCount > 1
                    ? CreateProceduralSubmeshMeshes(proceduralMesh)
                    : null,
                Generation = proceduralMesh.Generation,
                SubmeshCount = proceduralMesh.SubmeshCount,
                Loaded = true,
            };

            _proceduralMeshCache[meshAssetId] = cached;
            return true;
        }

        private bool TryGetOrLoadTexture(int meshAssetId, in MeshAssetDescriptor desc, out CachedTexture cached, out bool inFlight)
        {
            inFlight = false;
            if (_textureCache.TryGetValue(meshAssetId, out cached))
                return cached.Loaded;

            cached = new CachedTexture { Loaded = false, AspectRatio = 1f };

            if (_vfs == null || desc.SourceUris == null || desc.SourceUris.Length == 0)
            {
                RenderDiagnostics.Detail("texture", meshAssetId, $"texture-load skipped; vfsMissing={_vfs == null}; uriCount={desc.SourceUris?.Length ?? 0}");
                _textureCache[meshAssetId] = cached;
                return false;
            }

            List<string>? failures = null;
            foreach (string uri in desc.SourceUris)
            {
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                RaylibAssetAcquireOutcome outcome = _textureStore.TryAcquireOrBegin(uri, out RaylibAssetStore<Texture2D>.Lease? lease, out string? status);
                if (outcome == RaylibAssetAcquireOutcome.InFlight)
                {
                    inFlight = true;
                    return false;
                }

                if (outcome == RaylibAssetAcquireOutcome.Failed)
                {
                    failures ??= new List<string>();
                    failures.Add($"'{uri}': {status}");
                    continue;
                }

                Texture2D texture = lease!.Resource;
                cached = new CachedTexture
                {
                    Lease = lease,
                    Loaded = true,
                    AspectRatio = texture.height > 0 ? (float)texture.width / texture.height : 1f,
                };
                _textureCache[meshAssetId] = cached;
                return true;
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibPrimitiveRenderer)} texture meshAssetId={meshAssetId} could not load any sourceUri. Attempts: [{string.Join("; ", failures ?? new List<string>())}]");
        }
        private static string FormatSourceUris(string[]? sourceUris)
        {
            return sourceUris == null || sourceUris.Length == 0
                ? "(none)"
                : string.Join("|", sourceUris);
        }

        private void WarnMissingModelSkipped(int meshAssetId, int stableId, string path)
        {
            if (!_reportedMissingModelDraws.Add(meshAssetId))
            {
                return;
            }

            string stableText = stableId > 0 ? $" stableId={stableId}" : string.Empty;
            RenderDiagnostics.Warn($"Raylib renderer skipped {path}{stableText}: meshAssetId={meshAssetId} could not be loaded. No placeholder model is drawn.");
        }

        private static Mesh CreateProceduralMesh(ProceduralMeshAssetData proceduralMesh)
        {
            return CreateProceduralMeshSlice(proceduralMesh, indexStart: 0, indexCount: proceduralMesh.IndexCount);
        }

        private static Mesh[] CreateProceduralSubmeshMeshes(ProceduralMeshAssetData proceduralMesh)
        {
            var submeshMeshes = new Mesh[proceduralMesh.SubmeshCount];
            for (int i = 0; i < submeshMeshes.Length; i++)
            {
                ProceduralSubmeshDescriptor submesh = proceduralMesh.Submeshes[i];
                submeshMeshes[i] = CreateProceduralMeshSlice(proceduralMesh, submesh.IndexStart, submesh.IndexCount);
            }

            return submeshMeshes;
        }

        private static Mesh CreateProceduralMeshSlice(ProceduralMeshAssetData proceduralMesh, int indexStart, int indexCount)
        {
            Mesh mesh = new Mesh();
            mesh.vertexCount = proceduralMesh.VertexCount;
            mesh.triangleCount = indexCount / 3;

            int vertexFloatCount = proceduralMesh.VertexCount * 3;
            int tangentFloatCount = proceduralMesh.VertexCount * 4;
            int uvFloatCount = proceduralMesh.VertexCount * 2;
            int colorByteCount = proceduralMesh.Colors32?.Length >= proceduralMesh.VertexCount * 4
                ? proceduralMesh.VertexCount * 4
                : 0;

            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.tangents = (float*)Rl.MemAlloc(sizeof(float) * tangentFloatCount);
            mesh.texcoords = (float*)Rl.MemAlloc(sizeof(float) * uvFloatCount);
            mesh.indices = (ushort*)Rl.MemAlloc(sizeof(ushort) * indexCount);
            if (colorByteCount > 0)
            {
                mesh.colors = (byte*)Rl.MemAlloc(sizeof(byte) * colorByteCount);
            }

            proceduralMesh.Positions.AsSpan(0, vertexFloatCount).CopyTo(new Span<float>(mesh.vertices, vertexFloatCount));
            proceduralMesh.Normals.AsSpan(0, vertexFloatCount).CopyTo(new Span<float>(mesh.normals, vertexFloatCount));
            proceduralMesh.Tangents.AsSpan(0, tangentFloatCount).CopyTo(new Span<float>(mesh.tangents, tangentFloatCount));
            proceduralMesh.Uv0.AsSpan(0, uvFloatCount).CopyTo(new Span<float>(mesh.texcoords, uvFloatCount));
            for (int i = 0; i < indexCount; i++)
            {
                mesh.indices[i] = checked((ushort)proceduralMesh.Indices[indexStart + i]);
            }

            if (colorByteCount > 0 && proceduralMesh.Colors32 != null)
            {
                proceduralMesh.Colors32.AsSpan(0, colorByteCount).CopyTo(new Span<byte>(mesh.colors, colorByteCount));
            }

            RaylibNativeResources.UploadMesh(ref mesh, false);
            return mesh;
        }

        private static void UnloadProceduralMeshCache(in CachedProceduralMesh cached)
        {
            if (!cached.Loaded)
            {
                return;
            }

            if (cached.Mesh.vertexCount > 0)
            {
                RaylibNativeResources.UnloadMesh(cached.Mesh);
            }

            if (cached.SubmeshMeshes == null)
            {
                return;
            }

            for (int i = 0; i < cached.SubmeshMeshes.Length; i++)
            {
                if (cached.SubmeshMeshes[i].vertexCount > 0)
                {
                    RaylibNativeResources.UnloadMesh(cached.SubmeshMeshes[i]);
                }
            }
        }

        private void EnsureProceduralMeshMaterial()
        {
            if (_proceduralMeshMaterialLoaded)
            {
                return;
            }

            _proceduralMeshMaterial = RaylibNativeResources.LoadMaterialDefault();
            _proceduralMeshMaterialLoaded = true;
        }

        private static void DrawWireBox(Vector3 center, Vector3 size, float yawRad, Vector4 color)
        {
            Vector3 half = size * 0.5f;
            Span<Vector3> corners = stackalloc Vector3[8];
            corners[0] = TransformLocal(center, yawRad, new Vector3(-half.X, -half.Y, -half.Z));
            corners[1] = TransformLocal(center, yawRad, new Vector3(half.X, -half.Y, -half.Z));
            corners[2] = TransformLocal(center, yawRad, new Vector3(half.X, -half.Y, half.Z));
            corners[3] = TransformLocal(center, yawRad, new Vector3(-half.X, -half.Y, half.Z));
            corners[4] = TransformLocal(center, yawRad, new Vector3(-half.X, half.Y, -half.Z));
            corners[5] = TransformLocal(center, yawRad, new Vector3(half.X, half.Y, -half.Z));
            corners[6] = TransformLocal(center, yawRad, new Vector3(half.X, half.Y, half.Z));
            corners[7] = TransformLocal(center, yawRad, new Vector3(-half.X, half.Y, half.Z));

            var lineColor = ToRaylibColor(color);
            DrawWireEdge(corners, 0, 1, lineColor);
            DrawWireEdge(corners, 1, 2, lineColor);
            DrawWireEdge(corners, 2, 3, lineColor);
            DrawWireEdge(corners, 3, 0, lineColor);

            DrawWireEdge(corners, 4, 5, lineColor);
            DrawWireEdge(corners, 5, 6, lineColor);
            DrawWireEdge(corners, 6, 7, lineColor);
            DrawWireEdge(corners, 7, 4, lineColor);

            DrawWireEdge(corners, 0, 4, lineColor);
            DrawWireEdge(corners, 1, 5, lineColor);
            DrawWireEdge(corners, 2, 6, lineColor);
            DrawWireEdge(corners, 3, 7, lineColor);

            // Mark the forward-facing top edge so layer orientation is easy to inspect in motion.
            DrawWireEdge(corners, 6, 7, ToRaylibColor(MultiplyColor(color, 1.2f, 1.2f, 0.8f, 1f)));
        }

        private static void DrawWireBox(Vector3 center, Vector3 size, Quaternion rotation, Vector4 color)
        {
            Vector3 half = new Vector3(
                MathF.Max(0.01f, MathF.Abs(size.X)) * 0.5f,
                MathF.Max(0.01f, MathF.Abs(size.Y)) * 0.5f,
                MathF.Max(0.01f, MathF.Abs(size.Z)) * 0.5f);
            Quaternion normalized = VisualMath.NormalizeOrIdentity(rotation);
            Span<Vector3> corners = stackalloc Vector3[8];
            corners[0] = TransformLocal(center, normalized, new Vector3(-half.X, -half.Y, -half.Z));
            corners[1] = TransformLocal(center, normalized, new Vector3(half.X, -half.Y, -half.Z));
            corners[2] = TransformLocal(center, normalized, new Vector3(half.X, -half.Y, half.Z));
            corners[3] = TransformLocal(center, normalized, new Vector3(-half.X, -half.Y, half.Z));
            corners[4] = TransformLocal(center, normalized, new Vector3(-half.X, half.Y, -half.Z));
            corners[5] = TransformLocal(center, normalized, new Vector3(half.X, half.Y, -half.Z));
            corners[6] = TransformLocal(center, normalized, new Vector3(half.X, half.Y, half.Z));
            corners[7] = TransformLocal(center, normalized, new Vector3(-half.X, half.Y, half.Z));

            var lineColor = ToRaylibColor(color);
            DrawWireEdge(corners, 0, 1, lineColor);
            DrawWireEdge(corners, 1, 2, lineColor);
            DrawWireEdge(corners, 2, 3, lineColor);
            DrawWireEdge(corners, 3, 0, lineColor);
            DrawWireEdge(corners, 4, 5, lineColor);
            DrawWireEdge(corners, 5, 6, lineColor);
            DrawWireEdge(corners, 6, 7, lineColor);
            DrawWireEdge(corners, 7, 4, lineColor);
            DrawWireEdge(corners, 0, 4, lineColor);
            DrawWireEdge(corners, 1, 5, lineColor);
            DrawWireEdge(corners, 2, 6, lineColor);
            DrawWireEdge(corners, 3, 7, lineColor);
            DrawWireEdge(corners, 6, 7, ToRaylibColor(MultiplyColor(color, 1.2f, 1.2f, 0.8f, 1f)));
        }

        private static void DrawWireEdge(ReadOnlySpan<Vector3> corners, int start, int end, Color color)
        {
            Rl.DrawLine3D(corners[start], corners[end], color);
        }

        private static void DrawRotatedRing(Vector3 center, Quaternion rotation, float radius, int segments, Vector4 color)
        {
            if (segments < 3 || radius <= 0f)
            {
                return;
            }

            Quaternion normalized = VisualMath.NormalizeOrIdentity(rotation);
            Color ringColor = ToRaylibColor(color);
            float step = MathF.Tau / segments;
            Vector3 previous = TransformLocal(center, normalized, new Vector3(radius, 0f, 0f));
            for (int index = 1; index <= segments; index++)
            {
                float angle = index * step;
                Vector3 current = TransformLocal(
                    center,
                    normalized,
                    new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius));
                Rl.DrawLine3D(previous, current, ringColor);
                previous = current;
            }
        }


        private bool IsMaterialDoubleSided(int materialId)
        {
            if (materialId <= 0 || _materials == null)
            {
                return false;
            }

            if (!_materials.TryGet(materialId, out MaterialAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} cannot resolve DoubleSided for unknown materialId={materialId}.");
            }

            return (descriptor.Flags & MaterialAssetFlags.DoubleSided) != 0;
        }

        private static void ToAxisAngleDegrees(Quaternion rotation, out Vector3 axis, out float angleDegrees)
        {
            Quaternion normalized = VisualMath.NormalizeOrIdentity(rotation);
            float w = Math.Clamp(normalized.W, -1f, 1f);
            float angleRad = 2f * MathF.Acos(w);
            float sinHalf = MathF.Sqrt(MathF.Max(0f, 1f - (w * w)));

            if (sinHalf < 0.0001f)
            {
                axis = Vector3.UnitY;
                angleDegrees = 0f;
                return;
            }

            axis = new Vector3(
                normalized.X / sinHalf,
                normalized.Y / sinHalf,
                normalized.Z / sinHalf);
            angleDegrees = VisualMath.RadToDegValue(angleRad);
        }

        // ── Instanced rendering (unchanged from original) ──

        public void DrawInstanced(IPrimitiveDrawSnapshot draw, IRenderMeshAssets meshes)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            ResetInstancedStats();

            EnsureInitialized();

            var span = draw.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (!meshes.TryGetPrimitiveKind(item.MeshAssetId, out var kind)) continue;

                SubmitPrimitive(kind, item.Position, item.Rotation, item.Scale, item.Color, item.MaterialId);
            }

            FlushInstancedBatches();
        }

        public void ResetInstancedStats()
        {
            LastInstancedInstances = 0;
            LastInstancedBatches = 0;
            LastInstancedMatrixBuildMs = 0d;
            LastInstancedMeshDrawMs = 0d;
            LastPersistentSyncMs = 0d;
            LastPersistentBucketDrawMs = 0d;
            LastImmediateDrawMs = 0d;
            LastImmediateSkippedCount = 0;
            LastInstancedMatrixCacheHits = 0;
            LastInstancedMatrixCacheMisses = 0;
        }

        public void DrawInstancedBucket(RaylibIsmRenderBridge.Bucket bucket, IRenderMeshAssets meshes, float scaleMul = 1f)
        {
            _textureStore.PumpUploads();
            _modelStore.PumpUploads();
            if (bucket == null) throw new ArgumentNullException(nameof(bucket));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            EnsureInitialized();

            List<PrimitiveDrawItem> items = bucket.Items;
            if (items.Count == 0)
            {
                return;
            }

            if (TryDrawModelInstancedBucket(bucket, items, meshes, scaleMul))
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                PrimitiveDrawItem item = items[i];
                if (!meshes.TryGetDescriptor(item.MeshAssetId, out MeshAssetDescriptor descriptor))
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} cannot draw unknown meshAssetId={item.MeshAssetId}.");
                }

                switch (descriptor.Type)
                {
                    case MeshAssetType.Primitive when descriptor.PrimitiveKind is PrimitiveMeshKind.Cube or PrimitiveMeshKind.Sphere:
                        SubmitPrimitive(descriptor.PrimitiveKind, item.Position, item.Rotation, item.Scale * scaleMul, item.Color, item.MaterialId);
                        break;
                    case MeshAssetType.Model:
                    case MeshAssetType.Billboard:
                    case MeshAssetType.ProceduralMesh:
                        DrawAssetRecursive(
                            item.MeshAssetId,
                            item.Position,
                            item.Rotation,
                            item.Scale * scaleMul,
                            item.Color,
                            default,
                            meshes,
                            item.MaterialId);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"{nameof(RaylibPrimitiveRenderer)} refuses composite mesh type '{descriptor.Type}' for meshAssetId={item.MeshAssetId}. Author Presenter children instead of Prefab.");
                }
            }

            FlushInstancedBatches();
        }

        public void DrawInstancedBucketShadow(
            RaylibIsmRenderBridge.Bucket bucket,
            IRenderMeshAssets meshes,
            RaylibDirectionalShadowMap shadow,
            float scaleMul = 1f)
        {
            _textureStore.PumpUploads();
            _modelStore.PumpUploads();
            if (bucket == null) throw new ArgumentNullException(nameof(bucket));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));
            if (shadow == null) throw new ArgumentNullException(nameof(shadow));

            EnsureInitialized();

            List<PrimitiveDrawItem> items = bucket.Items;
            if (items.Count == 0)
            {
                return;
            }

            PrimitiveDrawItem first = items[0];
            if (!meshes.TryGetDescriptor(first.MeshAssetId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} cannot shadow unknown meshAssetId={first.MeshAssetId}.");
            }

            switch (descriptor.Type)
            {
                case MeshAssetType.Primitive when descriptor.PrimitiveKind is PrimitiveMeshKind.Cube or PrimitiveMeshKind.Sphere:
                    DrawPrimitiveInstancedBucketShadow(bucket, items, descriptor.PrimitiveKind, scaleMul, shadow);
                    return;
                case MeshAssetType.Model:
                    DrawModelInstancedBucketShadow(bucket, items, meshes, scaleMul, shadow);
                    return;
                case MeshAssetType.Billboard:
                case MeshAssetType.ProceduralMesh:
                    for (int i = 0; i < items.Count; i++)
                    {
                        PrimitiveDrawItem item = items[i];
                        DrawShadowLeafAsset(
                            item.MeshAssetId,
                            item.Position,
                            item.Rotation,
                            item.Scale * scaleMul,
                            default,
                            meshes,
                            shadow,
                            item.MaterialId);
                    }

                    return;
                default:
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} refuses composite shadow mesh type '{descriptor.Type}' for meshAssetId={first.MeshAssetId}. Author Presenter children instead of Prefab.");
            }
        }

        private void DrawInstancedBatchLanes(IRenderMeshAssets meshes, float scaleMul)
        {
            IRaylibInstancedBatchLaneSource? source = _instancedBatchLaneSource;
            if (source == null)
            {
                return;
            }

            int laneCount = source.ResidentLaneCount;
            if (laneCount == 0)
            {
                return;
            }

            EnsureInitialized();
            _typedLaneIdsSeen.Clear();
            for (int i = 0; i < laneCount; i++)
            {
                RaylibInstancedBatchLane lane = source.GetResidentLane(i);
                _typedLaneIdsSeen.Add(lane.LaneId);
                if (!lane.Visible || lane.Count <= 0)
                {
                    continue;
                }

                if (!meshes.TryGetDescriptor(lane.MeshAssetId, out MeshAssetDescriptor descriptor))
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} cannot draw typed instanced lane meshAssetId={lane.MeshAssetId}.");
                }

                switch (descriptor.Type)
                {
                    case MeshAssetType.Primitive when descriptor.PrimitiveKind is PrimitiveMeshKind.Cube or PrimitiveMeshKind.Sphere:
                        DrawTypedPrimitiveLane(lane, descriptor.PrimitiveKind, scaleMul);
                        break;
                    case MeshAssetType.Model:
                        DrawTypedModelLane(lane, descriptor, meshes, scaleMul);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"{nameof(RaylibPrimitiveRenderer)} refuses mesh type '{descriptor.Type}' for typed instanced lane meshAssetId={lane.MeshAssetId}. Use a Primitive or Model mesh asset.");
                }
            }

            PruneTypedLaneBatches();
        }

        private void DrawInstancedBatchLaneShadows(IRenderMeshAssets meshes, RaylibDirectionalShadowMap shadow, float scaleMul)
        {
            IRaylibInstancedBatchLaneSource? source = _instancedBatchLaneSource;
            if (source == null)
            {
                return;
            }

            int laneCount = source.ResidentLaneCount;
            if (laneCount == 0)
            {
                return;
            }

            EnsureInitialized();
            for (int i = 0; i < laneCount; i++)
            {
                RaylibInstancedBatchLane lane = source.GetResidentLane(i);
                if (!lane.Visible || lane.Count <= 0)
                {
                    continue;
                }

                if (!meshes.TryGetDescriptor(lane.MeshAssetId, out MeshAssetDescriptor descriptor))
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} cannot shadow typed instanced lane meshAssetId={lane.MeshAssetId}.");
                }

                switch (descriptor.Type)
                {
                    case MeshAssetType.Primitive when descriptor.PrimitiveKind is PrimitiveMeshKind.Cube or PrimitiveMeshKind.Sphere:
                        Mesh primitiveMesh = descriptor.PrimitiveKind == PrimitiveMeshKind.Cube ? _cubeMesh : _sphereMesh;
                        DrawMeshInstancedShadow(
                            primitiveMesh,
                            ResolveTypedLaneBatch(_typedLaneShadowBatches, lane, ShadowColorKey, scaleMul),
                            shadow);
                        break;
                    case MeshAssetType.Model:
                        if (!TryGetOrLoadModel(lane.MeshAssetId, in descriptor, out CachedModel cached))
                        {
                            WarnMissingModelSkipped(lane.MeshAssetId, stableId: 0, "typed instanced batch lane shadow");
                            break;
                        }

                        ModelInstanceBatch shadowBatch = ResolveTypedLaneBatch(_typedLaneShadowBatches, lane, ShadowColorKey, scaleMul);
                        for (int meshIndex = 0; meshIndex < cached.Model.meshCount; meshIndex++)
                        {
                            DrawMeshInstancedShadow(cached.Model.meshes[meshIndex], shadowBatch, shadow);
                        }

                        break;
                    default:
                        throw new InvalidOperationException(
                            $"{nameof(RaylibPrimitiveRenderer)} refuses shadow mesh type '{descriptor.Type}' for typed instanced lane meshAssetId={lane.MeshAssetId}. Use a Primitive or Model mesh asset.");
                }
            }
        }

        private void DrawTypedPrimitiveLane(
            in RaylibInstancedBatchLane lane,
            PrimitiveMeshKind primitiveKind,
            float scaleMul)
        {
            Mesh mesh = primitiveKind == PrimitiveMeshKind.Cube ? _cubeMesh : _sphereMesh;
            uint colorKey = RaylibInstancedMaterialPipeline.PackRgba(Vector4.One);
            ModelInstanceBatch batch = ResolveTypedLaneBatch(_typedLaneBatches, lane, colorKey, scaleMul);
            long drawStart = Stopwatch.GetTimestamp();
            EnsureFrameLightingAppliedForInstancing();
            RaylibInstancedMaterialPipeline.RequireMeshNormals(in mesh, "Instanced typed lane");
            RaylibLaneShader laneShader = ResolveInstancingLaneShader(lane.MaterialAssetId);
            _material.shader = laneShader.Shader;
            SetTintUniform(laneShader, colorKey);
            laneShader.SetColDiffuse(Vector4.One);
            _materialPipeline.ApplyHostMaterialMaps(ref _material, lane.MaterialAssetId, laneShader.Shader, in laneShader.PbrLocs);
            BindFrameShadow(ref _material);
            int drawCalls = 0;
            (RaylibMatrix[] visible, int visibleCount) = CompactVisibleInstances(batch, UnitCubeRadiusMeters);
            fixed (RaylibMatrix* transforms = visible)
            {
                for (int offset = 0; offset < visibleCount; offset += _maxModelInstancesPerDraw)
                {
                    int chunkCount = Math.Min(_maxModelInstancesPerDraw, visibleCount - offset);
                    Rl.DrawMeshInstanced(mesh, _material, transforms + offset, chunkCount);
                    drawCalls++;
                }
            }

            LastInstancedMeshDrawMs += (Stopwatch.GetTimestamp() - drawStart) * 1000.0 / Stopwatch.Frequency;
            LastInstancedInstances += visibleCount;
            LastInstancedBatches += drawCalls;
        }

        private void DrawTypedModelLane(
            in RaylibInstancedBatchLane lane,
            in MeshAssetDescriptor descriptor,
            IRenderMeshAssets meshes,
            float scaleMul)
        {
            if (!TryGetOrLoadModel(lane.MeshAssetId, in descriptor, out CachedModel cached))
            {
                WarnMissingModelSkipped(lane.MeshAssetId, stableId: 0, "typed instanced batch lane");
                return;
            }

            uint colorKey = RaylibInstancedMaterialPipeline.PackRgba(Vector4.One);
            ModelInstanceBatch batch = ResolveTypedLaneBatch(_typedLaneBatches, lane, colorKey, scaleMul);
            Vector3 localExtents = cached.LocalMax - cached.LocalMin;
            float modelRadius = 0.5f * localExtents.Length();
            int drawCalls = DrawModelInstanceBatch(cached.Model, batch, colorKey, lane.MaterialAssetId, modelRadius);
            LastInstancedInstances += batch.Count;
            LastInstancedBatches += drawCalls;
        }

        /// <summary>
        /// 帧级视锥侧平面（#1331）：System.Numerics 行向量约定下 clip = world*(view*proj)，
        /// 平面取列组合 col1±col4 / col2±col4；只做四个侧平面的保守球筛选——近平面（near=0.05 收益可忽略）
        /// 与远平面不参与，深度约定差异（GL -w..w vs D3D 0..w）因此不构成风险。平面构建失败兜底为全可见（保守方向）。
        /// </summary>
        private void BuildFrameFrustum(in Camera3D camera)
        {
            if (_frameFrustumPlanes.Length != 4)
            {
                _frameFrustumPlanes = new Vector4[4];
            }

            float aspect = MathF.Max(0.001f, Rl.GetScreenWidth() / (float)Math.Max(1, Rl.GetScreenHeight()));
            Matrix4x4 view = Matrix4x4.CreateLookAt(camera.position, camera.target, camera.up);
            Matrix4x4 proj = camera.projection == CameraProjection.CAMERA_ORTHOGRAPHIC
                ? Matrix4x4.CreateOrthographic(camera.fovy * aspect, camera.fovy, 0.05f, 100000f)
                : Matrix4x4.CreatePerspectiveFieldOfView(
                    camera.fovy * MathF.PI / 180f,
                    aspect,
                    0.05f,
                    100000f);
            Matrix4x4 p = view * proj;
            _frameFrustumPlanes[0] = NormalizePlane(new Vector4(p.M11 + p.M14, p.M21 + p.M24, p.M31 + p.M34, p.M41 + p.M44));
            _frameFrustumPlanes[1] = NormalizePlane(new Vector4(p.M11 - p.M14, p.M21 - p.M24, p.M31 - p.M34, p.M41 - p.M44));
            _frameFrustumPlanes[2] = NormalizePlane(new Vector4(p.M12 + p.M14, p.M22 + p.M24, p.M32 + p.M34, p.M42 + p.M44));
            _frameFrustumPlanes[3] = NormalizePlane(new Vector4(p.M12 - p.M14, p.M22 - p.M24, p.M32 - p.M34, p.M42 - p.M44));
            _frameFrustumValid = true;
        }

        private static Vector4 NormalizePlane(Vector4 plane)
        {
            float length = MathF.Sqrt(Vector4.Dot(plane, plane));
            return length > 1e-9f ? plane / length : plane;
        }

        private static float InstanceRadiusMeters(in RaylibMatrix matrix, float localRadiusMeters)
        {
            float sx = MathF.Sqrt((matrix.m0 * matrix.m0) + (matrix.m1 * matrix.m1) + (matrix.m2 * matrix.m2));
            float sy = MathF.Sqrt((matrix.m4 * matrix.m4) + (matrix.m5 * matrix.m5) + (matrix.m6 * matrix.m6));
            float sz = MathF.Sqrt((matrix.m8 * matrix.m8) + (matrix.m9 * matrix.m9) + (matrix.m10 * matrix.m10));
            return localRadiusMeters * MathF.Max(sx, MathF.Max(sy, sz));
        }

        private bool IsInstanceWithinFrameFrustum(in RaylibMatrix matrix, float localRadiusMeters)
        {
            if (!_frameFrustumValid)
            {
                return true;
            }

            Vector3 position = new(matrix.m12, matrix.m13, matrix.m14);
            float radius = InstanceRadiusMeters(in matrix, localRadiusMeters);
            Span<Vector4> planes = _frameFrustumPlanes;
            for (int i = 0; i < planes.Length; i++)
            {
                Vector4 plane = planes[i];
                if (plane.X * position.X + plane.Y * position.Y + plane.Z * position.Z + plane.W < -radius)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>主颜色 pass 提交点做逐实例压缩（#1331）：全可见时零拷贝直接用原批次；
        /// 有剔除时压缩进复用 scratch。阴影 pass 不调用（光源视锥与主相机视锥不同，剔除语义不适用）。
        /// revision 矩阵缓存不受剔除结果影响（缓存存原始全量，压缩每帧独立）。</summary>
        private (RaylibMatrix[] Buffer, int Count) CompactVisibleInstances(ModelInstanceBatch batch, float localRadiusMeters)
        {
            Span<RaylibMatrix> source = batch.Transforms.AsSpan(0, batch.Count);
            int firstInvisible = -1;
            for (int i = 0; i < source.Length; i++)
            {
                if (!IsInstanceWithinFrameFrustum(in source[i], localRadiusMeters))
                {
                    firstInvisible = i;
                    break;
                }
            }

            if (firstInvisible < 0)
            {
                return (batch.Transforms, batch.Count);
            }

            if (_laneCullScratch.Length < batch.Count)
            {
                _laneCullScratch = new RaylibMatrix[Math.Max(64, batch.Count * 2)];
            }

            Span<RaylibMatrix> target = _laneCullScratch.AsSpan(0, batch.Count);
            int kept = 0;
            for (int i = 0; i < firstInvisible; i++)
            {
                target[kept++] = source[i];
            }

            int culled = 1;
            for (int i = firstInvisible + 1; i < source.Length; i++)
            {
                if (IsInstanceWithinFrameFrustum(in source[i], localRadiusMeters))
                {
                    target[kept++] = source[i];
                }
                else
                {
                    culled++;
                }
            }

            LastInstancedLaneCullSkippedCount += culled;
            return (_laneCullScratch, kept);
        }

        private ModelInstanceBatch ResolveTypedLaneBatch(
            Dictionary<int, ModelInstanceBatch> cache,
            in RaylibInstancedBatchLane lane,
            uint colorKey,
            float scaleMul)
        {
            if (!cache.TryGetValue(lane.LaneId, out ModelInstanceBatch batch) || batch.ColorKey != colorKey)
            {
                batch = new ModelInstanceBatch(colorKey, Math.Max(4, lane.Count));
            }

            // scaleMul != 1 rescales the world-space matrix basis per frame, so only the exact
            // static scale (acceptance zoom disabled) is cacheable — mirrors bucket lane policy.
            bool canCacheStaticMatrices = MathF.Abs(scaleMul - 1f) <= 0.0001f;
            if (!canCacheStaticMatrices ||
                batch.Revision != lane.Revision ||
                batch.Count != lane.Count)
            {
                LastInstancedMatrixCacheMisses++;
                RebuildTypedLaneBatch(ref batch, in lane, scaleMul);
                cache[lane.LaneId] = batch;
            }
            else
            {
                LastInstancedMatrixCacheHits++;
            }

            return batch;
        }

        private void RebuildTypedLaneBatch(ref ModelInstanceBatch batch, in RaylibInstancedBatchLane lane, float scaleMul)
        {
            long start = Stopwatch.GetTimestamp();
            batch.Count = 0;
            batch.Revision = lane.Revision;
            bool rescale = MathF.Abs(scaleMul - 1f) > 0.0001f;
            for (int i = 0; i < lane.Count; i++)
            {
                Matrix4x4 matrix = lane.Matrices[i];
                if (rescale)
                {
                    matrix.M11 *= scaleMul;
                    matrix.M12 *= scaleMul;
                    matrix.M13 *= scaleMul;
                    matrix.M21 *= scaleMul;
                    matrix.M22 *= scaleMul;
                    matrix.M23 *= scaleMul;
                    matrix.M31 *= scaleMul;
                    matrix.M32 *= scaleMul;
                    matrix.M33 *= scaleMul;
                }

                batch.Add(RaylibMatrix.FromSystemNumerics(in matrix));
            }

            LastInstancedMatrixBuildMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        private void PruneTypedLaneBatches()
        {
            PruneTypedLaneCache(_typedLaneBatches);
            PruneTypedLaneCache(_typedLaneShadowBatches);
        }

        private void PruneTypedLaneCache(Dictionary<int, ModelInstanceBatch> cache)
        {
            // A cache entry whose lane id is absent from this frame's resident enumeration belongs
            // to a removed lane; free it instead of leaking the native-sized matrix array.
            List<int>? stale = null;
            foreach (int laneId in cache.Keys)
            {
                if (!_typedLaneIdsSeen.Contains(laneId))
                {
                    (stale ??= new List<int>()).Add(laneId);
                }
            }

            if (stale == null)
            {
                return;
            }

            for (int i = 0; i < stale.Count; i++)
            {
                cache.Remove(stale[i]);
            }
        }

        private bool TryDrawModelInstancedBucket(RaylibIsmRenderBridge.Bucket bucket, List<PrimitiveDrawItem> items, IRenderMeshAssets meshes, float scaleMul)
        {
            PrimitiveDrawItem first = items[0];
            if (!meshes.TryGetDescriptor(first.MeshAssetId, out MeshAssetDescriptor descriptor) ||
                descriptor.Type != MeshAssetType.Model)
            {
                return false;
            }

            if (!TryGetOrLoadModel(first.MeshAssetId, in descriptor, out CachedModel cached))
            {
                WarnMissingModelSkipped(first.MeshAssetId, first.StableId, "instanced static mesh bucket");
                return true;
            }

            uint colorKey = RaylibInstancedMaterialPipeline.PackRgba(first.Color);
            long batchKey = BuildModelInstanceBatchKey(first.MeshAssetId, colorKey);
            bool canCacheStaticMatrices = bucket.Lane.Mobility == VisualMobility.Static && MathF.Abs(scaleMul - 1f) <= 0.0001f;
            ModelInstanceBatch batch;
            if (canCacheStaticMatrices)
            {
                batch = GetStaticModelInstanceBatch(bucket, colorKey);
                if (batch.Revision != bucket.Revision || batch.Count != items.Count)
                {
                    LastInstancedMatrixCacheMisses++;
                    RebuildModelInstanceBatch(ref batch, items, scaleMul, bucket.Revision);
                    _staticModelInstanceBatches[bucket] = batch;
                }
                else
                {
                    LastInstancedMatrixCacheHits++;
                }
            }
            else
            {
                LastInstancedMatrixCacheMisses++;
                batch = GetModelInstanceBatch(batchKey, colorKey);
                RebuildModelInstanceBatch(ref batch, items, scaleMul, bucket.Revision);
                _modelInstanceBatches[batchKey] = batch;
            }

            int drawCalls = DrawModelInstanceBatch(cached.Model, batch, colorKey, first.MaterialId);
            LastInstancedInstances += batch.Count;
            LastInstancedBatches += drawCalls;
            return true;
        }

        private void DrawPrimitiveInstancedBucketShadow(
            RaylibIsmRenderBridge.Bucket bucket,
            List<PrimitiveDrawItem> items,
            PrimitiveMeshKind primitiveKind,
            float scaleMul,
            RaylibDirectionalShadowMap shadow)
        {
            Mesh mesh = primitiveKind == PrimitiveMeshKind.Cube ? _cubeMesh : _sphereMesh;
            ModelInstanceBatch batch = BuildInstancedShadowBatch(bucket, items, scaleMul);
            DrawMeshInstancedShadow(mesh, batch, shadow);
        }

        private void DrawModelInstancedBucketShadow(
            RaylibIsmRenderBridge.Bucket bucket,
            List<PrimitiveDrawItem> items,
            IRenderMeshAssets meshes,
            float scaleMul,
            RaylibDirectionalShadowMap shadow)
        {
            PrimitiveDrawItem first = items[0];
            if (!meshes.TryGetDescriptor(first.MeshAssetId, out MeshAssetDescriptor descriptor) ||
                descriptor.Type != MeshAssetType.Model)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} shadow bucket meshAssetId={first.MeshAssetId} is not a model.");
            }

            if (!TryGetOrLoadModel(first.MeshAssetId, in descriptor, out CachedModel cached))
            {
                WarnMissingModelSkipped(first.MeshAssetId, first.StableId, "instanced static mesh shadow bucket");
                return;
            }

            ModelInstanceBatch batch = BuildInstancedShadowBatch(bucket, items, scaleMul);
            for (int meshIndex = 0; meshIndex < cached.Model.meshCount; meshIndex++)
            {
                Mesh mesh = cached.Model.meshes[meshIndex];
                DrawMeshInstancedShadow(mesh, batch, shadow);
            }
        }

        private ModelInstanceBatch BuildInstancedShadowBatch(
            RaylibIsmRenderBridge.Bucket bucket,
            List<PrimitiveDrawItem> items,
            float scaleMul)
        {
            const uint shadowColorKey = 0;
            bool canCacheStaticMatrices = bucket.Lane.Mobility == VisualMobility.Static && MathF.Abs(scaleMul - 1f) <= 0.0001f;
            if (!_shadowInstanceBatches.TryGetValue(bucket, out ModelInstanceBatch batch) ||
                batch.ColorKey != shadowColorKey)
            {
                batch = new ModelInstanceBatch(shadowColorKey);
            }

            if (!canCacheStaticMatrices ||
                batch.Revision != bucket.Revision ||
                batch.Count != items.Count)
            {
                RebuildModelInstanceBatch(ref batch, items, scaleMul, bucket.Revision);
                _shadowInstanceBatches[bucket] = batch;
            }

            return batch;
        }

        private void DrawMeshInstancedShadow(Mesh mesh, ModelInstanceBatch batch, RaylibDirectionalShadowMap shadow)
        {
            if (mesh.vertexCount <= 0 || batch.Count <= 0)
            {
                return;
            }

            fixed (RaylibMatrix* transforms = batch.Transforms)
            {
                for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                {
                    int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                    shadow.DrawMeshInstancedShadow(mesh, transforms + offset, chunkCount);
                }
            }
        }

        private ModelInstanceBatch GetStaticModelInstanceBatch(RaylibIsmRenderBridge.Bucket bucket, uint colorKey)
        {
            if (_staticModelInstanceBatches.TryGetValue(bucket, out ModelInstanceBatch batch) &&
                batch.ColorKey == colorKey)
            {
                return batch;
            }

            return new ModelInstanceBatch(colorKey);
        }

        private void RebuildModelInstanceBatch(ref ModelInstanceBatch batch, List<PrimitiveDrawItem> items, float scaleMul, int revision)
        {
            long start = Stopwatch.GetTimestamp();
            batch.Count = 0;
            batch.Revision = revision;
            for (int i = 0; i < items.Count; i++)
            {
                PrimitiveDrawItem item = items[i];
                RaylibMatrix matrix = RaylibMatrix.FromSystemNumerics(
                    Matrix4x4.CreateScale(item.Scale * scaleMul) *
                    Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(item.Rotation)) *
                    Matrix4x4.CreateTranslation(item.Position));
                batch.Add(matrix);
            }

            LastInstancedMatrixBuildMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        private ModelInstanceBatch GetModelInstanceBatch(long batchKey, uint colorKey)
        {
            if (_modelInstanceBatches.TryGetValue(batchKey, out ModelInstanceBatch batch) &&
                batch.ColorKey == colorKey)
            {
                return batch;
            }

            return new ModelInstanceBatch(colorKey);
        }

        private static long BuildModelInstanceBatchKey(int meshAssetId, uint colorKey)
        {
            return ((long)meshAssetId << 32) | colorKey;
        }

        private int DrawModelInstanceBatch(Model model, ModelInstanceBatch batch, uint colorKey, int materialId, float cullLocalRadiusMeters = 0f)
        {
            if (model.meshCount <= 0 || batch.Count <= 0)
            {
                return 0;
            }

            EnsureFrameLightingAppliedForInstancing();
            int drawCalls = 0;
            long drawStart = Stopwatch.GetTimestamp();
            RaylibInstancedMaterialPipeline.RestoreOpaqueModelState();
            RaylibLaneShader lane = ResolveInstancingLaneShader(materialId);
            (RaylibMatrix[] buffer, int count) = cullLocalRadiusMeters > 0f
                ? CompactVisibleInstances(batch, cullLocalRadiusMeters)
                : (batch.Transforms, batch.Count);
            fixed (RaylibMatrix* transforms = buffer)
            {
                for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                {
                    Mesh mesh = model.meshes[meshIndex];
                    RaylibInstancedMaterialPipeline.RequireMeshNormals(in mesh, "Instanced ISM");
                    if (!_materialPipeline.TryResolveInstancedModelMaterial(model, meshIndex, materialId, lane.Shader, in lane.PbrLocs, _skyIbl, _frameShadow, out Material material))
                    {
                        continue;
                    }
                    ApplyInstancedMaterialTint(ref material, colorKey, lane);
                    for (int offset = 0; offset < count; offset += _maxModelInstancesPerDraw)
                    {
                        int chunkCount = Math.Min(_maxModelInstancesPerDraw, count - offset);
                        Rl.DrawMeshInstanced(mesh, material, transforms + offset, chunkCount);
                        drawCalls++;
                    }
                }
            }

            LastInstancedMeshDrawMs += (Stopwatch.GetTimestamp() - drawStart) * 1000.0 / Stopwatch.Frequency;
            return drawCalls;
        }
        private void ApplyHostMapsToModel(ref Model model, int materialId)
        {
            if (_materialLibrary == null || materialId <= 0 || model.materialCount <= 0 || model.materials == null)
            {
                return;
            }

            for (int i = 0; i < model.materialCount; i++)
            {
                ref Material material = ref model.materials[i];
                _materialPipeline.ApplyHostMaterialMaps(ref material, materialId, material.shader.id != 0 ? material.shader : _shader, in _instancingPbrLocs);
            }
        }
        private void ApplyInstancedMaterialTint(ref Material material, uint colorKey, RaylibLaneShader lane)
        {
            SetTintUniform(lane, colorKey);
            if (material.maps != null)
            {
                int albedoIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO;
                Color color = material.maps[albedoIndex].color;
                Vector4 diffuse = new(color.r / 255f, color.g / 255f, color.b / 255f, color.a / 255f);
                lane.SetColDiffuse(diffuse);
            }
        }

        /// <summary>材质 shaderKey → 车道着色程序；默认 lit 走内建实例化着色器，其余 key 必须已注册（fail-loud）。</summary>
        private RaylibLaneShader ResolveInstancingLaneShader(int materialId)
        {
            if (materialId <= 0 || _materialLibrary == null)
            {
                return _instancingLane;
            }

            if (!_materialLibrary.TryGetResolved(materialId, out ResolvedMaterialAsset resolved))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} cannot resolve shaderKey for unknown materialId={materialId}.");
            }

            return string.Equals(resolved.ShaderKey, RaylibShaderKeys.Lit, StringComparison.Ordinal)
                ? _instancingLane
                : _shaderCatalog.RequireInstancing(resolved.ShaderKey);
        }

        /// <summary>给实例化合批车道注册自定义 shaderKey 着色程序（须满足 instancing 接线契约；注册方持有程序生命周期）。</summary>
        public void RegisterInstancingShader(string shaderKey, RaylibLaneShader laneShader)
        {
            _shaderCatalog.RegisterInstancing(shaderKey, laneShader ?? throw new ArgumentNullException(nameof(laneShader)));
        }
        private static int ResolveMaxModelInstancesPerDraw()
        {
            string? raw = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_MAX_MODEL_INSTANCES_PER_DRAW");
            if (int.TryParse(raw, out int configured) && configured > 0)
            {
                return Math.Clamp(configured, 1, HardMaxModelInstancesPerDraw);
            }

            return DefaultMaxModelInstancesPerDraw;
        }

        private void AddInstance(List<Batch> batches, uint colorKey, int materialId, in RaylibMatrix matrix)
        {
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                if (b.ColorKey != colorKey || b.MaterialId != materialId) continue;

                b.Add(matrix);
                batches[i] = b;
                return;
            }

            var nb = new Batch(colorKey, materialId);
            nb.Add(matrix);
            batches.Add(nb);
        }

        private void FlushInstancedBatches()
        {
            int totalInstances = 0;
            int batches = 0;

            FlushMeshBatches(_cubeBatches, ref totalInstances, ref batches, ref _cubeMesh);
            FlushMeshBatches(_sphereBatches, ref totalInstances, ref batches, ref _sphereMesh);

            LastInstancedInstances += totalInstances;
            LastInstancedBatches += batches;
        }

        private void FlushMeshBatches(List<Batch> batches, ref int totalInstances, ref int batchCount, ref Mesh mesh)
        {
            EnsureFrameLightingAppliedForInstancing();
            RaylibInstancedMaterialPipeline.RequireMeshNormals(in mesh, "Instanced primitive");
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                if (b.Count == 0) continue;

                RaylibLaneShader lane = ResolveInstancingLaneShader(b.MaterialId);
                _material.shader = lane.Shader;
                SetTintUniform(lane, b.ColorKey);
                lane.SetColDiffuse(Vector4.One);
                _materialPipeline.ApplyHostMaterialMaps(ref _material, b.MaterialId, lane.Shader, in lane.PbrLocs);
                BindFrameShadow(ref _material);

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

        private static void SetTintUniform(RaylibLaneShader lane, uint colorKey)
        {
            float r = (colorKey & 0xFF) / 255f;
            float g = ((colorKey >> 8) & 0xFF) / 255f;
            float b = ((colorKey >> 16) & 0xFF) / 255f;
            float a = ((colorKey >> 24) & 0xFF) / 255f;
            var cd = new Vector4(r, g, b, a);
            lane.SetTint(cd);
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            _cubeMesh = RaylibNativeResources.GenMeshCube(1f, 1f, 1f);
            if (_cubeMesh.colors == null)
            {
                int bytes = _cubeMesh.vertexCount * 4;
                _cubeMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _cubeMesh.colors[i] = 255;
            }
            RaylibNativeResources.UploadMesh(ref _cubeMesh, false);

            _sphereMesh = RaylibNativeResources.GenMeshSphere(0.5f, 8, 8);
            if (_sphereMesh.colors == null)
            {
                int bytes = _sphereMesh.vertexCount * 4;
                _sphereMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _sphereMesh.colors[i] = 255;
            }
            RaylibNativeResources.UploadMesh(ref _sphereMesh, false);

            _vfxBillboardMesh = RaylibNativeResources.GenMeshCube(1f, 1f, 1f);
            if (_vfxBillboardMesh.colors == null)
            {
                int bytes = _vfxBillboardMesh.vertexCount * 4;
                _vfxBillboardMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _vfxBillboardMesh.colors[i] = 255;
            }
            RaylibNativeResources.UploadMesh(ref _vfxBillboardMesh, false);

            _billboardShadowMesh = CreateBillboardShadowMesh();

            RaylibEffectShader defaultVfx = _effectShaders.GetOrLoad(RaylibEffectShaderRegistry.DefaultUnlitTintKey);
            _vfxMaterial = RaylibNativeResources.LoadMaterialDefault();
            _vfxMaterial.shader = defaultVfx.Shader;
            _vfxMaterialLoaded = true;

            string baseDir = AppContext.BaseDirectory;
            _instancingLane = RaylibLaneShader.LoadInstancing(baseDir, "instancing.vs", "instancing.fs", "instancing");
            _shaderCatalog.RegisterInstancing(RaylibShaderKeys.Lit, _instancingLane);
            _shader = _instancingLane.Shader;

            _material = RaylibNativeResources.LoadMaterialDefault();
            _material.shader = _shader;

            _instancingPbrLocs = _instancingLane.PbrLocs;

            _materialPipeline.ApplyDefaultPbrUniforms(_shader, in _instancingPbrLocs);

            _initialized = true;
            ApplyFrameShadowToInstancingLanes();
            if (_frameLighting != null)
            {
                ApplyLightingToInstancingLanes(_frameLighting, _frameViewPos);
            }
        }

        private static Mesh CreateBillboardShadowMesh()
        {
            float[] vertices =
            {
                -0.5f, -0.5f, 0f,
                 0.5f, -0.5f, 0f,
                 0.5f,  0.5f, 0f,
                -0.5f, -0.5f, 0f,
                 0.5f,  0.5f, 0f,
                -0.5f,  0.5f, 0f,
            };

            // 镂空影子采样整张贴图：UV 与 DrawBillboardRec 全幅 source rect 同向（+Y 世界向上对应 v=0 图像顶部）。
            float[] texcoords =
            {
                0f, 1f,
                1f, 1f,
                1f, 0f,
                0f, 1f,
                1f, 0f,
                0f, 0f,
            };

            Mesh mesh = new()
            {
                vertexCount = 6,
                triangleCount = 2,
            };
            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vertices.Length);
            vertices.AsSpan().CopyTo(new Span<float>(mesh.vertices, vertices.Length));
            mesh.texcoords = (float*)Rl.MemAlloc(sizeof(float) * texcoords.Length);
            texcoords.AsSpan().CopyTo(new Span<float>(mesh.texcoords, texcoords.Length));
            RaylibNativeResources.UploadMesh(ref mesh, false);
            return mesh;
        }
        private void EnsureFrameLightingAppliedForInstancing()
        {
            if (_frameLighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} lit instancing requires {nameof(ApplyFrameLighting)} before draw.");
            }

            if (!_hasFrameViewPos)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} lit instancing requires camera view position before draw.");
            }

            ApplyLightingToInstancingLanes(_frameLighting, _frameViewPos);
            ApplyFrameShadowToInstancingLanes();
        }
        private void EnsureImmediateLitFrame()
        {
            if (_frameLighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} lit immediate primitives require {nameof(ApplyFrameLighting)} before draw.");
            }

            if (!_hasFrameViewPos)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} lit immediate primitives require camera view position before draw.");
            }

            _immediateLit ??= new RaylibLitModel();
            _immediateLit.BeginFrame(_frameLighting, _frameViewPos, _frameShadow, _frameShadowTexelWorld);
        }
        private static Color ToRaylibColor(in Vector4 c) => RaylibColorUtil.ToRaylibColor(in c);

        private Vector3 ResolveBillboardLitTintRgb()
        {
            if (_frameLighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} billboard draw requires {nameof(ApplyFrameLighting)} first.");
            }

            Vector4 ambient = _frameLighting.AmbientRgba;
            float key = _frameLighting.LightIntensity * 0.65f;
            // Midday lands near 1.0; raised night ambient must stay a faint multiply (not neon veg).
            float exposure = Math.Clamp((ambient.W * 1.55f) + key, 0.04f, 1.2f);
            const float albedoFloor = 0.10f;
            return new Vector3(
                Math.Clamp((albedoFloor + ambient.X * 0.72f) * exposure + (_frameLighting.LightColor.X * key * 0.22f), 0f, 1.4f),
                Math.Clamp((albedoFloor + ambient.Y * 0.72f) * exposure + (_frameLighting.LightColor.Y * key * 0.22f), 0f, 1.4f),
                Math.Clamp((albedoFloor + ambient.Z * 0.72f) * exposure + (_frameLighting.LightColor.Z * key * 0.22f), 0f, 1.4f));
        }

        private static byte Clamp01ToByte(float v) => RaylibColorUtil.Clamp01ToByte(v);

        public void Dispose()
        {
            foreach (Stack<IDisposable> leases in _residencyLeases.Values)
            {
                while (leases.Count > 0)
                {
                    leases.Pop().Dispose();
                }
            }

            _residencyLeases.Clear();

            foreach (var kvp in _modelCache)
            {
                if (!kvp.Value.Loaded)
                {
                    continue;
                }

                _materialLibrary?.DetachOwnedMaps(kvp.Value.Model);
                kvp.Value.Lease?.Dispose();
            }
            _modelCache.Clear();

            foreach (var kvp in _proceduralMeshCache)
            {
                CachedProceduralMesh cached = kvp.Value;
                UnloadProceduralMeshCache(in cached);
            }
            _proceduralMeshCache.Clear();

            foreach (var kvp in _textureCache)
            {
                if (kvp.Value.Loaded)
                    kvp.Value.Lease?.Dispose();
            }
            _textureCache.Clear();

            if (_proceduralMeshMaterialLoaded)
            {
                _materialLibrary?.DetachOwnedMaps(ref _proceduralMeshMaterial);
                RaylibShadowSampling.ClearTexture(ref _proceduralMeshMaterial);
                RaylibNativeResources.UnloadMaterial(_proceduralMeshMaterial);
                _proceduralMeshMaterialLoaded = false;
            }

            if (_vfxMaterialLoaded)
            {
                _vfxMaterial.shader = default;
                RaylibNativeResources.UnloadMaterial(_vfxMaterial);
                _vfxMaterialLoaded = false;
            }

            _decalRenderer.Dispose();
            _vegetationCutout.Dispose();
            _gpuSkinned.Dispose();

            _gpuSkinnedModelCache.UnloadAll(model => _materialLibrary?.DetachOwnedMaps(model));
            _gpuSkinnedModelCache.Dispose();
            _vfxRenderer.Dispose();
            _effectShaders.Dispose();
            _materialLibrary?.Dispose();
            _skyIbl?.Dispose();
            _skyIbl = null;
            _immediateLit?.Dispose();
            _immediateLit = null;

            if (!_initialized) return;

            if (_cubeMesh.vertexCount > 0) RaylibNativeResources.UnloadMesh(_cubeMesh);
            if (_sphereMesh.vertexCount > 0) RaylibNativeResources.UnloadMesh(_sphereMesh);
            if (_vfxBillboardMesh.vertexCount > 0) RaylibNativeResources.UnloadMesh(_vfxBillboardMesh);
            if (_billboardShadowMesh.vertexCount > 0) RaylibNativeResources.UnloadMesh(_billboardShadowMesh);
            _material.shader = default;
            // UnloadMaterial 会删除材质槽上的全部纹理；IBL 纹理归 RaylibSkyIbl 所有，先清槽防双删。
            _material.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP].texture = default;
            _material.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_BRDF].texture = default;
            RaylibShadowSampling.ClearTexture(ref _material);
            RaylibNativeResources.UnloadMaterial(_material);
            RaylibNativeResources.UnloadShader(_shader);
            _initialized = false;

            // 存储销毁必须最晚：蒙皮缓存/材质库/VFX 的释放回调（DetachOwnedMaps 等）仍要触碰模型与贴图，
            // 先销毁存储会产生悬空指针；模型先于贴图销毁保持与旧实现一致（模型内部纹理随模型释放）。
            _modelStore.Dispose();
            _textureStore.Dispose();
        }

        private struct CachedModel
        {
            public RaylibAssetStore<Model>.Lease? Lease;
            public Mesh[] Meshes;
            public Vector3 LocalMin;
            public Vector3 LocalMax;
            public bool Loaded;

            public readonly Model Model => Lease!.Resource;
        }
        private struct CachedTexture
        {
            public RaylibAssetStore<Texture2D>.Lease? Lease;
            public bool Loaded;
            public float AspectRatio;

            public readonly Texture2D Texture => Lease!.Resource;
        }
        private struct CachedProceduralMesh
        {
            public Mesh Mesh;
            public Mesh[]? SubmeshMeshes;
            public int Generation;
            public int SubmeshCount;
            public bool Loaded;
        }

        private struct Batch
        {
            public readonly uint ColorKey;
            public readonly int MaterialId;
            public RaylibMatrix[] Transforms;
            public int Count;

            public Batch(uint colorKey, int materialId = 0, int initialCapacity = 256)
            {
                ColorKey = colorKey;
                MaterialId = materialId;
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

        private struct ModelInstanceBatch
        {
            public readonly uint ColorKey;
            public RaylibMatrix[] Transforms;
            public int Count;
            public int Revision;

            public ModelInstanceBatch(uint colorKey, int initialCapacity = 256)
            {
                ColorKey = colorKey;
                Transforms = new RaylibMatrix[Math.Max(4, initialCapacity)];
                Count = 0;
                Revision = int.MinValue;
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
    }
}
