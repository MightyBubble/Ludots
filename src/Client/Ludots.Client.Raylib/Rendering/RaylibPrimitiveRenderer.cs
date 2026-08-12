using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Ludots.Core.Diagnostics;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.AdapterSync;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public enum RaylibPrimitiveRenderMode : byte
    {
        Immediate = 0,
        Instanced = 1
    }

    public sealed unsafe class RaylibPrimitiveRenderer : IDisposable
    {
        private readonly RaylibPrimitiveRenderMode _mode;
        private readonly IVirtualFileSystem? _vfs;
        private readonly PresentationMaterialRegistry? _materials;
        private readonly RaylibMaterialHostBinder? _materialHostBinder;
        private readonly string? _diagnosticPath;
        private readonly PrefabFinalizedVisualBuffer _prefabVisuals = new PrefabFinalizedVisualBuffer();
        private const int DefaultMaxModelInstancesPerDraw = 32768;
        private const int HardMaxModelInstancesPerDraw = 131072;

        private bool _initialized;
        private const float DefaultVegetationAlphaCutoff = 0.9f;

        private Mesh _cubeMesh;
        private Mesh _sphereMesh;
        private Mesh _vfxBillboardMesh;
        private Shader _shader;
        private Shader _skinningShader;
        private Shader _vegetationCutoutShader;
        private Material _material;
        private Material _vfxMaterial;
        private bool _vfxMaterialLoaded;
        private bool _vegetationCutoutShaderReady;
        private readonly RaylibEffectShaderRegistry _effectShaders = new RaylibEffectShaderRegistry();
        private int _locColDiffuse;
        private int _locTint;
        private int _locVegetationCutoutColDiffuse;
        private int _locVegetationCutoutAlphaCutoff;
        private int _locSkinningColDiffuse;
        private int _locSkinningTint;
        private int _locBoneMatrices;
        private RaylibFrameLightingLocations _instancingLightingLocs;
        private RaylibFrameLightingLocations _skinningLightingLocs;
        private RaylibFrameLighting? _frameLighting;
        private Vector3 _frameViewPos;
        private bool _hasFrameViewPos;
        private bool _skinningShaderReady;

        private readonly List<Batch> _cubeBatches = new List<Batch>(16);
        private readonly List<Batch> _sphereBatches = new List<Batch>(16);
        private readonly Dictionary<long, ModelInstanceBatch> _modelInstanceBatches = new Dictionary<long, ModelInstanceBatch>();
        private readonly Dictionary<RaylibIsmRenderBridge.Bucket, ModelInstanceBatch> _staticModelInstanceBatches = new();
        private readonly Dictionary<GpuSkinnedInstanceBatchKey, GpuSkinnedInstanceBatch> _gpuSkinnedInstanceBatches = new();
        private readonly List<GpuSkinnedInstanceBatch> _activeGpuSkinnedInstanceBatches = new(64);
        private readonly RaylibIsmRenderBridge _ismBridge = new RaylibIsmRenderBridge();
        private readonly RaylibGpuSkinnedModelCache _gpuSkinnedModelCache;

        private readonly Dictionary<int, CachedModel> _modelCache = new Dictionary<int, CachedModel>();
        private readonly Dictionary<int, CachedProceduralMesh> _proceduralMeshCache = new Dictionary<int, CachedProceduralMesh>();
        private readonly Dictionary<int, CachedTexture> _textureCache = new Dictionary<int, CachedTexture>();
        private readonly HashSet<int> _loggedTextureDiagnostics = new HashSet<int>();
        private readonly HashSet<int> _loggedBillboardDrawDiagnostics = new HashSet<int>();
        private readonly HashSet<int> _reportedMissingModelDraws = new HashSet<int>();
        private readonly HashSet<int> _reportedInvalidInstancedMaterials = new HashSet<int>();
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

        public RaylibIsmRenderBridge IsmBridge => _ismBridge;

        public void ApplyFrameLighting(RaylibFrameLighting lighting, Vector3 viewPos)
        {
            _frameLighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            _frameViewPos = viewPos;
            _hasFrameViewPos = true;
            if (_initialized)
            {
                lighting.Apply(_shader, in _instancingLightingLocs);
                lighting.ApplyViewPosition(_shader, in _instancingLightingLocs, viewPos);
            }

            if (_skinningShaderReady)
            {
                lighting.Apply(_skinningShader, in _skinningLightingLocs);
                lighting.ApplyViewPosition(_skinningShader, in _skinningLightingLocs, viewPos);
            }
        }

        public RaylibPrimitiveRenderer(
            RaylibPrimitiveRenderMode mode = RaylibPrimitiveRenderMode.Immediate,
            IVirtualFileSystem? vfs = null,
            PresentationMaterialRegistry? materials = null)
        {
            _mode = mode;
            _vfs = vfs;
            _materials = materials;
            _materialHostBinder = vfs != null && materials != null
                ? new RaylibMaterialHostBinder(vfs, materials)
                : null;
            _diagnosticPath = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_DIAGNOSTIC_PATH");
            _maxModelInstancesPerDraw = ResolveMaxModelInstancesPerDraw();
            _gpuSkinnedModelCache = new RaylibGpuSkinnedModelCache(vfs);
        }

        public void Draw(PrimitiveDrawBuffer draw, Camera3D camera, MeshAssetRegistry meshes, float scaleMul = 1f, IVisualHeightmap? visualHeightmap = null)
        {
            Draw(draw, camera, snapshot: null, skinnedBatch: null, meshes, scaleMul, visualHeightmap);
        }

        public void Draw(PrimitiveDrawBuffer draw, Camera3D camera, PrimitiveDrawBuffer? snapshot, MeshAssetRegistry meshes, float scaleMul = 1f, IVisualHeightmap? visualHeightmap = null)
        {
            Draw(draw, camera, snapshot, skinnedBatch: null, meshes, scaleMul, visualHeightmap);
        }

        public void Draw(
            PrimitiveDrawBuffer draw,
            Camera3D camera,
            PrimitiveDrawBuffer? snapshot,
            SkinnedVisualBatchBuffer? skinnedBatch,
            MeshAssetRegistry meshes,
            float scaleMul = 1f,
            IVisualHeightmap? visualHeightmap = null)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            _frameViewPos = camera.position;
            _hasFrameViewPos = true;

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
            LastPersistentCreates = 0;
            LastPersistentUpdates = 0;
            LastPersistentRemoves = 0;
            LastGpuSkinnedInstances = 0;
            LastGpuSkinnedBatches = 0;
            LastGpuSkinnedMatrixBuildMs = 0d;
            LastGpuSkinnedMeshDrawMs = 0d;
            LastMeshVisualCount = 0;
            LastDecalVisualCount = 0;
            LastVfxVisualCount = 0;
            LastSurfaceVisualCount = 0;
            var finalizationContext = new PrefabFinalizationContext(visualHeightmap);

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
                DrawPersistentStaticLanes(camera, meshes, scaleMul, in finalizationContext);
                LastPersistentBucketDrawMs = (Stopwatch.GetTimestamp() - bucketStart) * 1000d / Stopwatch.Frequency;
                if (skinnedBatch != null)
                {
                    DrawSkinnedBatch(skinnedBatch, camera, meshes, scaleMul, in finalizationContext);
                }

                long dynamicLaneStart = Stopwatch.GetTimestamp();
                DrawSnapshotDynamicLanes(span, camera, meshes, scaleMul, skinnedBatchActive: skinnedBatch != null, in finalizationContext);
                LastImmediateDrawMs = (Stopwatch.GetTimestamp() - dynamicLaneStart) * 1000d / Stopwatch.Frequency;

                return;
            }

            if (_mode == RaylibPrimitiveRenderMode.Instanced)
            {
                DrawHybridInstanced(span, camera, meshes, scaleMul, in finalizationContext);
                return;
            }

            long immediateDrawStart = Stopwatch.GetTimestamp();
            DrawImmediateWithDescriptors(span, camera, meshes, scaleMul, persistentStaticLanesActive: false, skinnedBatchActive: false, in finalizationContext);
            LastImmediateDrawMs = (Stopwatch.GetTimestamp() - immediateDrawStart) * 1000d / Stopwatch.Frequency;
        }

        private void DrawPersistentStaticLanes(Camera3D camera, MeshAssetRegistry meshes, float scaleMul, in PrefabFinalizationContext finalizationContext)
        {
            foreach (RaylibIsmRenderBridge.Bucket bucket in _ismBridge.ActiveBuckets)
            {
                DrawInstancedBucket(bucket, meshes, scaleMul);
            }
        }

        private void DrawImmediateWithDescriptors(
            ReadOnlySpan<PrimitiveDrawItem> span,
            Camera3D camera,
            MeshAssetRegistry meshes,
            float scaleMul,
            bool persistentStaticLanesActive,
            bool skinnedBatchActive,
            in PrefabFinalizationContext finalizationContext)
        {
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (IsHostSurfaceLane(in item))
                {
                    LastImmediateSkippedCount++;
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
                    in finalizationContext,
                    item.MaterialId);
            }
        }

        private void DrawSnapshotDynamicLanes(
            ReadOnlySpan<PrimitiveDrawItem> span,
            Camera3D camera,
            MeshAssetRegistry meshes,
            float scaleMul,
            bool skinnedBatchActive,
            in PrefabFinalizationContext finalizationContext)
        {
            EnsureInitialized();

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (IsHostSurfaceLane(in item))
                {
                    LastImmediateSkippedCount++;
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
                    in finalizationContext,
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

        private void DrawHybridInstanced(ReadOnlySpan<PrimitiveDrawItem> span, Camera3D camera, MeshAssetRegistry meshes, float scaleMul, in PrefabFinalizationContext finalizationContext)
        {
            EnsureInitialized();

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (IsHostSurfaceLane(in item))
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
                    in finalizationContext,
                    item.MaterialId);
            }

            FlushInstancedBatches();
        }

        private void SubmitAssetRecursive(int meshAssetId, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color, Camera3D camera, MeshAssetRegistry meshes, in PrefabFinalizationContext finalizationContext, int materialId = 0)
        {
            _prefabVisuals.Clear();
            PrefabFinalizationPipeline.FinalizeVisuals(
                meshes,
                meshAssetId,
                stableId: 0,
                position,
                rotation,
                scale,
                color,
                finalizationContext,
                _prefabVisuals,
                instanceMaterialOverrideId: materialId);

            foreach (ref readonly var visual in _prefabVisuals.GetSpan())
            {
                SubmitFinalizedVisual(in visual, camera);
            }
        }

        private static bool IsHostSurfaceLane(in PrimitiveDrawItem item)
        {
            return item.AssetKind == AssetKind.Surface || item.RenderPath.IsSurfaceLane();
        }

        private void SubmitFinalizedVisual(in PrefabFinalizedVisual visual, Camera3D camera)
        {
            TrackVisualKind(visual.Kind);

            switch (visual.Kind)
            {
                case PrefabVisualPartKind.Mesh:
                    SubmitMeshVisual(in visual, camera);
                    break;
                case PrefabVisualPartKind.ProceduralMesh:
                    DrawProceduralMesh(in visual);
                    break;
                case PrefabVisualPartKind.Decal:
                    DrawDecalVisual(in visual);
                    break;
                case PrefabVisualPartKind.Vfx:
                    DrawVfxVisual(in visual, camera);
                    break;
                case PrefabVisualPartKind.Surface:
                    DrawSurfaceVisual(in visual, camera);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} does not recognize finalized visual kind '{visual.Kind}' (stableId={visual.StableId}).");
            }
        }

        private void SubmitPrimitive(PrimitiveMeshKind kind, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color)
        {
            uint key = PackRgba(color);
            var matrix = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(WorldPlane2D.NormalizeOrIdentity(rotation)) *
                Matrix4x4.CreateTranslation(position));

            if (kind == PrimitiveMeshKind.Cube)
            {
                AddInstance(_cubeBatches, key, matrix);
                return;
            }

            if (kind == PrimitiveMeshKind.Sphere)
            {
                AddInstance(_sphereBatches, key, matrix);
                return;
            }

            DrawPrimitive(kind, position, rotation, scale, color);
        }

        private bool TryDrawPrototypeSkinned(in PrimitiveDrawItem item, MeshAssetRegistry meshes, float scaleMul)
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

        private void DrawSkinnedBatch(SkinnedVisualBatchBuffer skinnedBatch, Camera3D camera, MeshAssetRegistry meshes, float scaleMul, in PrefabFinalizationContext finalizationContext)
        {
            var span = skinnedBatch.GetSpan();
            PrepareGpuSkinnedInstanceBatches();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (item.Visibility != VisualVisibility.Visible)
                {
                    continue;
                }

                if (TrySubmitGpuSkinnedInstance(item, meshes, scaleMul))
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
                    in finalizationContext,
                    item.MaterialId);
            }

            FlushGpuSkinnedInstanceBatches();
        }

        private void PrepareGpuSkinnedInstanceBatches()
        {
            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                _activeGpuSkinnedInstanceBatches[i].Count = 0;
            }

            _activeGpuSkinnedInstanceBatches.Clear();
        }

        private bool TrySubmitGpuSkinnedInstance(in SkinnedVisualBatchItem item, MeshAssetRegistry meshes, float scaleMul)
        {
            if (item.RenderPath != VisualRenderPath.GpuSkinnedInstance ||
                !meshes.TryGetDescriptor(item.MeshAssetId, out MeshAssetDescriptor descriptor) ||
                descriptor.Type != MeshAssetType.Model)
            {
                return false;
            }

            RaylibGpuSkinnedModelCache.Entry entry = _gpuSkinnedModelCache.GetOrLoad(item.MeshAssetId, in descriptor);
            AnimatorPackedState animator = item.Animator;
            RaylibSkinnedPlayback.ResolveFromAnimator(
                in animator,
                entry.Animations,
                entry.AnimCount,
                stateToClipMap: null,
                out int clipIndex,
                out int frameIndex);

            long start = Stopwatch.GetTimestamp();
            uint colorKey = PackRgba(item.Color);
            var key = new GpuSkinnedInstanceBatchKey(
                item.MeshAssetId,
                item.MaterialId,
                colorKey,
                clipIndex,
                frameIndex);
            if (!_gpuSkinnedInstanceBatches.TryGetValue(key, out GpuSkinnedInstanceBatch? batch))
            {
                batch = new GpuSkinnedInstanceBatch(key);
                _gpuSkinnedInstanceBatches.Add(key, batch);
            }

            if (batch.Count == 0)
            {
                batch.Model = entry.Model;
                batch.Animations = entry.Animations;
                batch.AnimCount = entry.AnimCount;
                _activeGpuSkinnedInstanceBatches.Add(batch);
            }

            batch.Add(RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(item.Scale * scaleMul) *
                Matrix4x4.CreateFromQuaternion(WorldPlane2D.NormalizeOrIdentity(item.Rotation)) *
                Matrix4x4.CreateTranslation(item.Position)));
            LastGpuSkinnedMatrixBuildMs += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            return true;
        }

        private void FlushGpuSkinnedInstanceBatches()
        {
            if (_activeGpuSkinnedInstanceBatches.Count == 0)
            {
                return;
            }

            EnsureInitialized();
            EnsureSkinningShaderInitialized();
            long drawStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                if (batch.Count == 0)
                {
                    continue;
                }

                LastGpuSkinnedInstances += batch.Count;
                LastGpuSkinnedBatches += DrawGpuSkinnedInstanceBatch(batch);
            }

            LastGpuSkinnedMeshDrawMs += (Stopwatch.GetTimestamp() - drawStart) * 1000d / Stopwatch.Frequency;
        }

        private bool TryDrawPrototypeSkinned(in SkinnedVisualBatchItem item, MeshAssetRegistry meshes, float scaleMul)
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

        private static AnimationOverlayRequest ResolvePrototypeOverlay(
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
                BaseClip = new AnimatorBuiltinClipState
                {
                    ClipId = AnimatorBuiltinClipId.LocomotionCycle,
                    NormalizedTime01 = animator.GetNormalizedTime01(),
                    Weight01 = 1f,
                },
            };
        }

        private void DrawAssetRecursive(int meshAssetId, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color, Camera3D camera, MeshAssetRegistry meshes, in PrefabFinalizationContext finalizationContext, int materialId = 0)
        {
            _prefabVisuals.Clear();
            PrefabFinalizationPipeline.FinalizeVisuals(
                meshes,
                meshAssetId,
                stableId: 0,
                position,
                rotation,
                scale,
                color,
                finalizationContext,
                _prefabVisuals,
                instanceMaterialOverrideId: materialId);

            foreach (ref readonly var visual in _prefabVisuals.GetSpan())
            {
                DrawFinalizedVisual(in visual, camera);
            }
        }

        private void DrawFinalizedVisual(in PrefabFinalizedVisual visual, Camera3D camera)
        {
            TrackVisualKind(visual.Kind);

            switch (visual.Kind)
            {
                case PrefabVisualPartKind.Mesh:
                    DrawMeshVisual(in visual, camera);
                    break;
                case PrefabVisualPartKind.ProceduralMesh:
                    DrawProceduralMesh(in visual);
                    break;
                case PrefabVisualPartKind.Decal:
                    DrawDecalVisual(in visual);
                    break;
                case PrefabVisualPartKind.Vfx:
                    DrawVfxVisual(in visual, camera);
                    break;
                case PrefabVisualPartKind.Surface:
                    DrawSurfaceVisual(in visual, camera);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} does not recognize finalized visual kind '{visual.Kind}' (stableId={visual.StableId}).");
            }
        }

        public string BuildVisualKindDiagnosticSummary()
        {
            return $"prefab-visual-counts lastFrame(mesh={LastMeshVisualCount},decal={LastDecalVisualCount},vfx={LastVfxVisualCount},surface={LastSurfaceVisualCount}) total(mesh={TotalMeshVisualCount},decal={TotalDecalVisualCount},vfx={TotalVfxVisualCount},surface={TotalSurfaceVisualCount})";
        }

        public string BuildPrimitiveLaneDiagnosticSummary(MeshAssetRegistry meshes)
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

        private void SubmitMeshVisual(in PrefabFinalizedVisual visual, Camera3D camera)
        {
            switch (visual.MeshDescriptor.Type)
            {
                case MeshAssetType.Primitive:
                    SubmitPrimitive(visual.MeshDescriptor.PrimitiveKind, visual.Position, visual.Rotation, visual.Scale, visual.Color);
                    break;
                case MeshAssetType.Model:
                    DrawModel(visual.MeshAssetId, visual.MeshDescriptor, visual.Position, visual.Rotation, visual.Scale, visual.Color, visual.MaterialId);
                    break;
                case MeshAssetType.Billboard:
                    DrawBillboard(visual.MeshAssetId, visual.MeshDescriptor, visual.Position, visual.Scale, visual.Color, camera, visual.MaterialId);
                    break;
                case MeshAssetType.ProceduralMesh:
                    DrawProceduralMesh(in visual);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} does not support mesh descriptor type '{visual.MeshDescriptor.Type}' for stableId={visual.StableId}.");
            }
        }

        private void DrawMeshVisual(in PrefabFinalizedVisual visual, Camera3D camera)
        {
            switch (visual.MeshDescriptor.Type)
            {
                case MeshAssetType.Primitive:
                    DrawPrimitive(visual.MeshDescriptor.PrimitiveKind, visual.Position, visual.Rotation, visual.Scale, visual.Color);
                    break;
                case MeshAssetType.Model:
                    DrawModel(visual.MeshAssetId, visual.MeshDescriptor, visual.Position, visual.Rotation, visual.Scale, visual.Color, visual.MaterialId);
                    break;
                case MeshAssetType.Billboard:
                    DrawBillboard(visual.MeshAssetId, visual.MeshDescriptor, visual.Position, visual.Scale, visual.Color, camera, visual.MaterialId);
                    break;
                case MeshAssetType.ProceduralMesh:
                    DrawProceduralMesh(in visual);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} does not support mesh descriptor type '{visual.MeshDescriptor.Type}' for stableId={visual.StableId}.");
            }
        }

        private void DrawDecalVisual(in PrefabFinalizedVisual visual)
        {
            Vector2 size = ResolveDecalSize(in visual);
            Quaternion rotation = WorldPlane2D.NormalizeOrIdentity(visual.Rotation);
            Vector4 materialAccent = BlendSemanticColor(visual.Color, visual.MaterialId, 0.45f);
            Vector4 edgeColor = MultiplyColor(materialAccent, 1.18f, 1.18f, 0.92f, 0.92f);
            Vector4 crossColor = MultiplyColor(materialAccent, 0.86f, 1.05f, 1.18f, 0.78f);
            float lift = MathF.Max(0.01f, MathF.Max(MathF.Abs(visual.Scale.Y), 0.05f) * 0.04f);
            Vector3 center = visual.Position + Vector3.Transform(Vector3.UnitY * lift, rotation);
            float halfWidth = MathF.Max(0.05f, size.X * 0.5f);
            float halfDepth = MathF.Max(0.05f, size.Y * 0.5f);

            Span<Vector3> corners = stackalloc Vector3[4];
            corners[0] = TransformLocal(center, rotation, new Vector3(-halfWidth, 0f, -halfDepth));
            corners[1] = TransformLocal(center, rotation, new Vector3(halfWidth, 0f, -halfDepth));
            corners[2] = TransformLocal(center, rotation, new Vector3(halfWidth, 0f, halfDepth));
            corners[3] = TransformLocal(center, rotation, new Vector3(-halfWidth, 0f, halfDepth));

            Color edge = ToRaylibColor(edgeColor);
            Rl.DrawLine3D(corners[0], corners[1], edge);
            Rl.DrawLine3D(corners[1], corners[2], edge);
            Rl.DrawLine3D(corners[2], corners[3], edge);
            Rl.DrawLine3D(corners[3], corners[0], edge);

            Color cross = ToRaylibColor(crossColor);
            Rl.DrawLine3D(corners[0], corners[2], cross);
            Rl.DrawLine3D(corners[1], corners[3], cross);

            Vector3 up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, rotation));
            Vector3 markerTop = center + (up * MathF.Max(0.04f, MathF.Min(size.X, size.Y) * 0.08f));
            Rl.DrawLine3D(center, markerTop, edge);
        }

        private void DrawVfxVisual(in PrefabFinalizedVisual visual, Camera3D camera)
        {
            EnsureInitialized();

            float baseExtent = MathF.Max(
                0.18f,
                MathF.Max(MathF.Abs(visual.Scale.X), MathF.Max(MathF.Abs(visual.Scale.Y), MathF.Abs(visual.Scale.Z))) * 0.9f);
            float size = visual.VfxSpawnMode == PrefabVfxSpawnMode.Loop
                ? baseExtent * 1.25f
                : baseExtent;
            Vector4 effectColor = BlendSemanticColor(visual.Color, visual.EffectAssetId, 0.6f);
            effectColor.W = Math.Clamp(MathF.Max(effectColor.W, 0.55f), 0.55f, 1f);

            MaterialBlendMode blendMode = ResolveMaterialBlendMode(visual.MaterialId, MaterialBlendMode.AlphaBlend);
            if (blendMode == MaterialBlendMode.Cutout)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} VFX stableId={visual.StableId} materialId={visual.MaterialId} requested Cutout; VFX uses AlphaBlend/Additive/Opaque only.");
            }

            RaylibEffectShader effect = _effectShaders.GetOrLoad(RaylibEffectShaderRegistry.DefaultUnlitTintKey);
            _vfxMaterial.shader = effect.Shader;

            Vector4 colDiffuse = Vector4.One;
            Vector4 tint = effectColor;
            float time = (float)Rl.GetTime();
            Rl.SetShaderValue(effect.Shader, effect.LocColDiffuse, &colDiffuse, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            Rl.SetShaderValue(effect.Shader, effect.LocTint, &tint, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            Rl.SetShaderValue(effect.Shader, effect.LocTime, &time, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

            Vector3 cameraForward = camera.target - camera.position;
            if (cameraForward.LengthSquared() <= 1e-8f)
            {
                cameraForward = -Vector3.UnitZ;
            }
            else
            {
                cameraForward = Vector3.Normalize(cameraForward);
            }

            Matrix4x4 billboard = Matrix4x4.CreateBillboard(
                visual.Position,
                camera.position,
                camera.up,
                cameraForward);
            Matrix4x4 transform =
                Matrix4x4.CreateScale(size, size, MathF.Max(0.04f, size * 0.08f)) *
                billboard;

            bool blending = TryBeginAuthorBlendMode(blendMode);
            try
            {
                Rl.DrawMesh(_vfxBillboardMesh, _vfxMaterial, RaylibMatrix.FromSystemNumerics(transform));
            }
            finally
            {
                if (blending)
                {
                    Rl.EndBlendMode();
                }
            }
        }

        private void DrawSurfaceVisual(in PrefabFinalizedVisual visual, Camera3D camera)
        {
            Vector4 surfaceColor = BlendSemanticColor(visual.Color, visual.MaterialId, 0.38f);
            Vector4 overlayColor = MultiplyColor(surfaceColor, 1.18f, 1.08f, 0.86f, 0.96f);

            switch (visual.MeshDescriptor.Type)
            {
                case MeshAssetType.Primitive:
                    DrawPrimitive(visual.MeshDescriptor.PrimitiveKind, visual.Position, visual.Rotation, visual.Scale, surfaceColor);
                    break;
                case MeshAssetType.Model:
                    DrawModel(visual.MeshAssetId, visual.MeshDescriptor, visual.Position, visual.Rotation, visual.Scale, surfaceColor, visual.MaterialId);
                    break;
                case MeshAssetType.Billboard:
                    DrawBillboard(visual.MeshAssetId, visual.MeshDescriptor, visual.Position, visual.Scale, surfaceColor, camera, visual.MaterialId);
                    break;
                case MeshAssetType.ProceduralMesh:
                    DrawProceduralMesh(in visual);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} does not support surface mesh descriptor type '{visual.MeshDescriptor.Type}' for stableId={visual.StableId}.");
            }

            DrawWireBox(visual.Position, ResolveSurfaceOverlaySize(in visual), WorldPlane2D.NormalizeOrIdentity(visual.Rotation), overlayColor);

            if (visual.TerrainFacing)
            {
                Quaternion rotation = WorldPlane2D.NormalizeOrIdentity(visual.Rotation);
                Vector3 up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, rotation));
                float normalLength = MathF.Max(0.12f, MathF.Max(MathF.Abs(visual.Scale.X), MathF.Abs(visual.Scale.Z)) * 0.4f);
                Rl.DrawLine3D(visual.Position, visual.Position + (up * normalLength), ToRaylibColor(MultiplyColor(overlayColor, 1f, 1f, 1f, 0.8f)));
            }
        }

        private void TrackVisualKind(PrefabVisualPartKind kind)
        {
            switch (kind)
            {
                case PrefabVisualPartKind.Mesh:
                    LastMeshVisualCount++;
                    TotalMeshVisualCount++;
                    break;
                case PrefabVisualPartKind.Decal:
                    LastDecalVisualCount++;
                    TotalDecalVisualCount++;
                    break;
                case PrefabVisualPartKind.Vfx:
                    LastVfxVisualCount++;
                    TotalVfxVisualCount++;
                    break;
                case PrefabVisualPartKind.Surface:
                    LastSurfaceVisualCount++;
                    TotalSurfaceVisualCount++;
                    break;
                case PrefabVisualPartKind.ProceduralMesh:
                    LastMeshVisualCount++;
                    TotalMeshVisualCount++;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported finalized visual kind '{kind}'.");
            }
        }

        private void DrawPrimitive(PrimitiveMeshKind kind, Vector3 position, Quaternion rotation, Vector3 scale, Vector4 color)
        {
            EnsureInitialized();
            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(WorldPlane2D.NormalizeOrIdentity(rotation)) *
                Matrix4x4.CreateTranslation(position));
            Color rayColor = ToRaylibColor(color);

            if (kind == PrimitiveMeshKind.Cube)
            {
                DrawTransformedPrimitive(in transform, PrimitiveMeshKind.Cube, rayColor);
            }
            else if (kind == PrimitiveMeshKind.Sphere)
            {
                DrawTransformedPrimitive(in transform, PrimitiveMeshKind.Sphere, rayColor);
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
            float locomotionPhase = ResolveClipTime01(overlay.BaseClip, AnimatorBuiltinClipId.LocomotionCycle);
            float locomotionWeight = ResolveClipWeight01(overlay.BaseClip, AnimatorBuiltinClipId.LocomotionCycle);
            float aimYaw = ResolveClipScalar0(overlay.LayerClip, AnimatorBuiltinClipId.AimYawOffset) * ResolveClipWeight01(overlay.LayerClip, AnimatorBuiltinClipId.AimYawOffset);
            float recoilPulse = ResolvePulse(overlay.OverlayClip, AnimatorBuiltinClipId.RecoilPulse);
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
            float locomotionPhase = ResolveClipTime01(overlay.BaseClip, AnimatorBuiltinClipId.LocomotionCycle);
            float locomotionWeight = ResolveClipWeight01(overlay.BaseClip, AnimatorBuiltinClipId.LocomotionCycle);
            float lowerPhase = locomotionPhase * MathF.Tau;
            float stride = MathF.Sin(lowerPhase) * unit * (0.08f + locomotionWeight * 0.34f);
            float aimWeight = ResolveClipWeight01(overlay.LayerClip, AnimatorBuiltinClipId.AimYawOffset);
            float upperYaw = baseYaw + ResolveClipScalar0(overlay.LayerClip, AnimatorBuiltinClipId.AimYawOffset) * aimWeight;
            float recoilPulse = ResolvePulse(overlay.OverlayClip, AnimatorBuiltinClipId.RecoilPulse);
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

        private static float ResolveClipTime01(in AnimatorBuiltinClipState clip, AnimatorBuiltinClipId expectedId)
        {
            return clip.ClipId == expectedId ? clip.NormalizedTime01 : 0f;
        }

        private static float ResolveClipWeight01(in AnimatorBuiltinClipState clip, AnimatorBuiltinClipId expectedId)
        {
            return clip.ClipId == expectedId ? clip.Weight01 : 0f;
        }

        private static float ResolveClipScalar0(in AnimatorBuiltinClipState clip, AnimatorBuiltinClipId expectedId)
        {
            return clip.ClipId == expectedId ? clip.Scalar0 : 0f;
        }

        private static float ResolvePulse(in AnimatorBuiltinClipState clip, AnimatorBuiltinClipId expectedId)
        {
            if (clip.ClipId != expectedId || clip.Weight01 <= 0.001f)
            {
                return 0f;
            }

            return MathF.Sin(clip.NormalizedTime01 * MathF.PI) * clip.Weight01;
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
            return origin + Vector3.Transform(local, WorldPlane2D.NormalizeOrIdentity(rotation));
        }

        private static Vector3 TransformLocal(Vector3 origin, float yawRad, Vector3 local)
        {
            return WorldPlane2D.TransformVisualLocal2D(origin, yawRad, in local);
        }

        private static float ExtractYawRad(Quaternion rotation)
        {
            return WorldPlane2D.TryExtractFacingRadFromVisualYRotation(rotation, out float facingRad)
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
            ApplyHostAlbedoToModel(ref model, materialId);
            ToAxisAngleDegrees(rotation, out Vector3 axis, out float angleDegrees);
            RestoreOpaqueModelState();
            Rl.DrawModelEx(model, position, axis, angleDegrees, scale, tint);
        }

        private void DrawBillboard(int meshAssetId, in MeshAssetDescriptor desc, Vector3 position, Vector3 scale, Vector4 color, Camera3D camera, int materialId)
        {
            if (!TryGetOrLoadTexture(meshAssetId, desc, out var cached))
            {
                return;
            }

            float height = MathF.Max(scale.Y, 0.05f);
            float width = height * cached.AspectRatio;
            var billboardPosition = new Vector3(position.X, position.Y + height * 0.5f, position.Z);
            var source = new Rectangle(0f, 0f, cached.Texture.width, cached.Texture.height);

            // Billboard art ships pre-colored; multiply once by frame lighting so night/dusk dims vegetation.
            byte alpha = Clamp01ToByte(color.W);
            Vector3 litRgb = ResolveBillboardLitTintRgb();
            MaterialBlendMode blendMode = ResolveMaterialBlendMode(materialId, MaterialBlendMode.Opaque);
            Color tint = blendMode == MaterialBlendMode.Cutout
                ? new Color(255, 255, 255, alpha)
                : new Color(
                    Clamp01ToByte(litRgb.X),
                    Clamp01ToByte(litRgb.Y),
                    Clamp01ToByte(litRgb.Z),
                    alpha);
            bool doubleSided = IsMaterialDoubleSided(materialId);
            LogBillboardDrawDiagnostic(
                meshAssetId,
                $"billboard-draw pos=({billboardPosition.X:F2},{billboardPosition.Y:F2},{billboardPosition.Z:F2}) scale=({scale.X:F2},{scale.Y:F2},{scale.Z:F2}) size=({width:F2}x{height:F2}) alpha={alpha} blend={blendMode} materialId={materialId} cameraPos=({camera.position.X:F2},{camera.position.Y:F2},{camera.position.Z:F2}) cameraTarget=({camera.target.X:F2},{camera.target.Y:F2},{camera.target.Z:F2})");

            if (doubleSided)
            {
                Rl.rlDisableBackfaceCulling();
            }

            bool blending = TryBeginAuthorBlendMode(blendMode);
            bool cutoutShader = false;
            try
            {
                if (blendMode == MaterialBlendMode.Cutout)
                {
                    EnsureVegetationCutoutShader();
                    float cutoff = DefaultVegetationAlphaCutoff;
                    Vector4 colDiffuse = new(litRgb.X, litRgb.Y, litRgb.Z, 1f);
                    Rl.SetShaderValue(
                        _vegetationCutoutShader,
                        _locVegetationCutoutColDiffuse,
                        &colDiffuse,
                        (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
                    Rl.SetShaderValue(
                        _vegetationCutoutShader,
                        _locVegetationCutoutAlphaCutoff,
                        &cutoff,
                        (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.BeginShaderMode(_vegetationCutoutShader);
                    cutoutShader = true;
                }

                Rl.DrawBillboardRec(camera, cached.Texture, source, billboardPosition, new Vector2(width, height), tint);
            }
            finally
            {
                if (cutoutShader)
                {
                    Rl.EndShaderMode();
                }

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

        private void DrawProceduralMesh(in PrefabFinalizedVisual visual)
        {
            if (!TryGetOrBuildProceduralMesh(visual.MeshAssetId, visual.MeshDescriptor, out var cached))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} cannot draw finalized procedural visual stableId={visual.StableId} because meshAssetId={visual.MeshAssetId} has no committed procedural payload.");
            }

            ValidateProceduralMaterialContract(in visual, cached.SubmeshCount);
            EnsureProceduralMeshMaterial();

            var transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(visual.Scale) *
                Matrix4x4.CreateFromQuaternion(WorldPlane2D.NormalizeOrIdentity(visual.Rotation)) *
                Matrix4x4.CreateTranslation(visual.Position));

            PrefabMaterialBinding[] bindings = visual.MaterialBindings!;
            Rl.rlDisableBackfaceCulling();
            if (cached.SubmeshMeshes == null || cached.SubmeshMeshes.Length == 0)
            {
                Material material = ResolveProceduralDrawMaterial(bindings[0].MaterialAssetId);
                Rl.DrawMesh(cached.Mesh, material, transform);
            }
            else
            {
                for (int i = 0; i < cached.SubmeshMeshes.Length; i++)
                {
                    Material material = ResolveProceduralDrawMaterial(bindings[i].MaterialAssetId);
                    Rl.DrawMesh(cached.SubmeshMeshes[i], material, transform);
                }
            }
            Rl.rlEnableBackfaceCulling();
        }

        private Material ResolveProceduralDrawMaterial(int materialAssetId)
        {
            Material material = _proceduralMeshMaterial;
            ApplyHostMaterialAlbedo(ref material, materialAssetId);
            return material;
        }

        private void ValidateProceduralMaterialContract(in PrefabFinalizedVisual visual, int cachedSubmeshCount)
        {
            if (visual.MeshDescriptor.Type != MeshAssetType.ProceduralMesh || visual.MeshDescriptor.ProceduralMeshData == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} received procedural visual stableId={visual.StableId} without procedural mesh payload.");
            }

            PrefabMaterialBinding[]? bindings = visual.MaterialBindings;
            ProceduralMeshAssetData procedural = visual.MeshDescriptor.ProceduralMeshData;
            if (bindings == null || bindings.Length != procedural.SubmeshCount || cachedSubmeshCount != procedural.SubmeshCount)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} requires finalized procedural visual stableId={visual.StableId} to provide one material binding per committed submesh.");
            }

            if (_materials == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} requires {nameof(PresentationMaterialRegistry)} to validate procedural mesh material bindings.");
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                PrefabMaterialBinding binding = bindings[i];
                if (binding.SubmeshIndex != i)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} requires procedural material bindings to stay aligned with submesh order (stableId={visual.StableId}, expectedSubmesh={i}, actualSubmesh={binding.SubmeshIndex}).");
                }

                if (!_materials.TryGet(binding.MaterialAssetId, out MaterialAssetDescriptor descriptor))
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} received procedural visual stableId={visual.StableId} with unknown materialId={binding.MaterialAssetId} for submesh {binding.SubmeshIndex}.");
                }

                if (descriptor.Domain != MaterialAssetDomain.Surface)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} only supports surface-domain procedural mesh materials (stableId={visual.StableId}, materialId={binding.MaterialAssetId}, domain={descriptor.Domain}).");
                }
            }
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

            for (int u = 0; u < desc.SourceUris.Length; u++)
            {
                string uri = desc.SourceUris[u];
                if (string.IsNullOrWhiteSpace(uri)) continue;

                if (!_vfs.TryResolveFullPath(uri, out string fullPath)) continue;
                if (!File.Exists(fullPath)) continue;

                var model = Rl.LoadModel(fullPath);
                if (model.meshCount > 0)
                {
                    cached = new CachedModel { Model = model, Loaded = true };
                    _modelCache[meshAssetId] = cached;
                    return true;
                }

                Rl.UnloadModel(model);
            }

            _modelCache[meshAssetId] = cached;
            return false;
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

        private bool TryGetOrLoadTexture(int meshAssetId, in MeshAssetDescriptor desc, out CachedTexture cached)
        {
            if (_textureCache.TryGetValue(meshAssetId, out cached))
                return cached.Loaded;

            cached = new CachedTexture { Loaded = false, AspectRatio = 1f };

            if (_vfs == null || desc.SourceUris == null || desc.SourceUris.Length == 0)
            {
                LogTextureDiagnostic(meshAssetId, $"texture-load skipped; vfsMissing={_vfs == null}; uriCount={desc.SourceUris?.Length ?? 0}");
                _textureCache[meshAssetId] = cached;
                return false;
            }

            for (int u = 0; u < desc.SourceUris.Length; u++)
            {
                string uri = desc.SourceUris[u];
                if (string.IsNullOrWhiteSpace(uri)) continue;

                if (!_vfs.TryResolveFullPath(uri, out string fullPath))
                {
                    LogTextureDiagnostic(meshAssetId, $"texture-resolve failed; uri={uri}");
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    LogTextureDiagnostic(meshAssetId, $"texture-file missing; uri={uri}; fullPath={fullPath}");
                    continue;
                }

                var texture = Rl.LoadTexture(fullPath);
                if (texture.id != 0 && texture.width > 0 && texture.height > 0)
                {
                    cached = new CachedTexture
                    {
                        Texture = texture,
                        Loaded = true,
                        AspectRatio = texture.height > 0 ? (float)texture.width / texture.height : 1f,
                    };
                    _textureCache[meshAssetId] = cached;
                    LogTextureDiagnostic(meshAssetId, $"texture-load success; uri={uri}; fullPath={fullPath}; size={texture.width}x{texture.height}");
                    return true;
                }

                LogTextureDiagnostic(meshAssetId, $"texture-load failed; uri={uri}; fullPath={fullPath}; textureId={texture.id}; size={texture.width}x{texture.height}");

                if (texture.id != 0)
                    Rl.UnloadTexture(texture);
            }

            _textureCache[meshAssetId] = cached;
            return false;
        }

        private void LogTextureDiagnostic(int meshAssetId, string message)
        {
            if (string.IsNullOrWhiteSpace(_diagnosticPath))
                return;

            if (!_loggedTextureDiagnostics.Add(meshAssetId))
                return;

            string fullPath = Path.GetFullPath(_diagnosticPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(fullPath, $"[{DateTime.UtcNow:O}] meshAssetId={meshAssetId} {message}{Environment.NewLine}");
        }

        private void LogBillboardDrawDiagnostic(int meshAssetId, string message)
        {
            if (string.IsNullOrWhiteSpace(_diagnosticPath))
                return;

            if (!_loggedBillboardDrawDiagnostics.Add(meshAssetId))
                return;

            string fullPath = Path.GetFullPath(_diagnosticPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(fullPath, $"[{DateTime.UtcNow:O}] meshAssetId={meshAssetId} {message}{Environment.NewLine}");
        }

        private void WarnMissingModelSkipped(int meshAssetId, int stableId, string path)
        {
            if (!_reportedMissingModelDraws.Add(meshAssetId))
            {
                return;
            }

            string stableText = stableId > 0 ? $" stableId={stableId}" : string.Empty;
            Log.Warn(
                in LogChannels.Presentation,
                $"Raylib renderer skipped {path}{stableText}: meshAssetId={meshAssetId} could not be loaded. No placeholder model is drawn.");
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

            Rl.UploadMesh(ref mesh, false);
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
                Rl.UnloadMesh(cached.Mesh);
            }

            if (cached.SubmeshMeshes == null)
            {
                return;
            }

            for (int i = 0; i < cached.SubmeshMeshes.Length; i++)
            {
                if (cached.SubmeshMeshes[i].vertexCount > 0)
                {
                    Rl.UnloadMesh(cached.SubmeshMeshes[i]);
                }
            }
        }

        private void EnsureProceduralMeshMaterial()
        {
            if (_proceduralMeshMaterialLoaded)
            {
                return;
            }

            _proceduralMeshMaterial = Rl.LoadMaterialDefault();
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
            Quaternion normalized = WorldPlane2D.NormalizeOrIdentity(rotation);
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

            Quaternion normalized = WorldPlane2D.NormalizeOrIdentity(rotation);
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

        private static Vector2 ResolveDecalSize(in PrefabFinalizedVisual visual)
        {
            float width = MathF.Max(0.1f, MathF.Abs(visual.Size.X));
            float depth = MathF.Max(0.1f, MathF.Abs(visual.Size.Y));
            float scaleX = MathF.Max(0.1f, MathF.Abs(visual.Scale.X));
            float scaleZ = MathF.Max(0.1f, MathF.Abs(visual.Scale.Z));
            return new Vector2(width * scaleX, depth * scaleZ);
        }

        private static Vector3 ResolveSurfaceOverlaySize(in PrefabFinalizedVisual visual)
        {
            float x = MathF.Max(0.12f, MathF.Abs(visual.Scale.X));
            float y = MathF.Max(0.04f, MathF.Abs(visual.Scale.Y));
            float z = MathF.Max(0.12f, MathF.Abs(visual.Scale.Z));
            if (visual.MeshDescriptor.Type == MeshAssetType.Billboard)
            {
                z = MathF.Max(z, 0.04f);
            }

            return new Vector3(x, y, z);
        }


        private MaterialBlendMode ResolveMaterialBlendMode(int materialId, MaterialBlendMode defaultWhenMissing)
        {
            if (materialId <= 0)
            {
                return defaultWhenMissing;
            }

            if (_materials == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} received materialId={materialId} but no {nameof(PresentationMaterialRegistry)} was provided.");
            }

            if (!_materials.TryGet(materialId, out MaterialAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} cannot resolve blend mode for unknown materialId={materialId}.");
            }

            return MaterialBlendModeResolver.Resolve(descriptor.Flags);
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

        private static bool TryBeginAuthorBlendMode(MaterialBlendMode blendMode)
        {
            switch (blendMode)
            {
                case MaterialBlendMode.Opaque:
                case MaterialBlendMode.Cutout:
                    return false;
                case MaterialBlendMode.AlphaBlend:
                    Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
                    return true;
                case MaterialBlendMode.Additive:
                    Rl.BeginBlendMode(BlendMode.BLEND_ADDITIVE);
                    return true;
                default:
                    throw new InvalidOperationException(
                        $"{nameof(RaylibPrimitiveRenderer)} does not recognize material blend mode '{blendMode}'.");
            }
        }

        private void EnsureVegetationCutoutShader()
        {
            if (_vegetationCutoutShaderReady)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            string vsPath = Path.Combine(baseDir, "vegetation_cutout.vs");
            string fsPath = Path.Combine(baseDir, "vegetation_cutout.fs");
            if (!File.Exists(vsPath))
            {
                throw new FileNotFoundException(
                    $"{nameof(RaylibPrimitiveRenderer)} vegetation cutout vertex shader missing under BaseDirectory '{baseDir}'. Expected '{vsPath}'.",
                    vsPath);
            }

            if (!File.Exists(fsPath))
            {
                throw new FileNotFoundException(
                    $"{nameof(RaylibPrimitiveRenderer)} vegetation cutout fragment shader missing under BaseDirectory '{baseDir}'. Expected '{fsPath}'.",
                    fsPath);
            }

            _vegetationCutoutShader = Rl.LoadShader(vsPath, fsPath);
            if (_vegetationCutoutShader.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} failed to compile vegetation_cutout from '{vsPath}' + '{fsPath}' (shader.id == 0).");
            }

            int locVertexPosition = Rl.GetShaderLocationAttrib(_vegetationCutoutShader, "vertexPosition");
            int locVertexTexCoord = Rl.GetShaderLocationAttrib(_vegetationCutoutShader, "vertexTexCoord");
            int locVertexColor = Rl.GetShaderLocationAttrib(_vegetationCutoutShader, "vertexColor");
            int locMvp = Rl.GetShaderLocation(_vegetationCutoutShader, "mvp");
            int locMapDiffuse = Rl.GetShaderLocation(_vegetationCutoutShader, "texture0");
            _locVegetationCutoutColDiffuse = Rl.GetShaderLocation(_vegetationCutoutShader, "colDiffuse");
            _locVegetationCutoutAlphaCutoff = Rl.GetShaderLocation(_vegetationCutoutShader, "alphaCutoff");

            if (locVertexPosition < 0 || locMvp < 0 || locMapDiffuse < 0 ||
                _locVegetationCutoutColDiffuse < 0 || _locVegetationCutoutAlphaCutoff < 0)
            {
                Rl.UnloadShader(_vegetationCutoutShader);
                _vegetationCutoutShader = default;
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} vegetation_cutout is missing required attribs/uniforms (vertexPosition/mvp/texture0/colDiffuse/alphaCutoff).");
            }

            if (_vegetationCutoutShader.locs != null)
            {
                _vegetationCutoutShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
                _vegetationCutoutShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
                _vegetationCutoutShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = locVertexColor;
                _vegetationCutoutShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
                _vegetationCutoutShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapDiffuse;
                _vegetationCutoutShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = _locVegetationCutoutColDiffuse;
            }

            _vegetationCutoutShaderReady = true;
        }

        private static Vector4 BlendSemanticColor(Vector4 baseColor, int semanticId, float semanticWeight)
        {
            Vector4 semanticColor = ResolveSemanticColor(semanticId, baseColor.W);
            Vector4 tinted = LerpColor(baseColor, semanticColor, semanticWeight);
            tinted.W = Math.Max(baseColor.W, semanticColor.W * 0.8f);
            return tinted;
        }

        private static Vector4 ResolveSemanticColor(int semanticId, float alpha)
        {
            uint seed = semanticId == 0
                ? 0x9E3779B9u
                : Hash((uint)semanticId);
            float r = 0.28f + (((seed >> 0) & 0xFF) / 255f) * 0.62f;
            float g = 0.28f + (((seed >> 8) & 0xFF) / 255f) * 0.62f;
            float b = 0.28f + (((seed >> 16) & 0xFF) / 255f) * 0.62f;
            return new Vector4(r, g, b, Math.Clamp(alpha, 0.35f, 1f));
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }

        private static void ToAxisAngleDegrees(Quaternion rotation, out Vector3 axis, out float angleDegrees)
        {
            Quaternion normalized = WorldPlane2D.NormalizeOrIdentity(rotation);
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
            angleDegrees = WorldPlane2D.RadToDegValue(angleRad);
        }

        // ── Instanced rendering (unchanged from original) ──

        public void DrawInstanced(PrimitiveDrawBuffer draw, MeshAssetRegistry meshes)
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

                SubmitPrimitive(kind, item.Position, item.Rotation, item.Scale, item.Color);
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

        public void DrawInstancedBucket(RaylibIsmRenderBridge.Bucket bucket, MeshAssetRegistry meshes, float scaleMul = 1f)
        {
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
                    continue;
                }

                switch (descriptor.Type)
                {
                    case MeshAssetType.Primitive when descriptor.PrimitiveKind is PrimitiveMeshKind.Cube or PrimitiveMeshKind.Sphere:
                        SubmitPrimitive(descriptor.PrimitiveKind, item.Position, item.Rotation, item.Scale * scaleMul, item.Color);
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
                            new PrefabFinalizationContext(null),
                            item.MaterialId);
                        break;
                }
            }

            FlushInstancedBatches();
        }

        private bool TryDrawModelInstancedBucket(RaylibIsmRenderBridge.Bucket bucket, List<PrimitiveDrawItem> items, MeshAssetRegistry meshes, float scaleMul)
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

            uint colorKey = PackRgba(first.Color);
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
                    Matrix4x4.CreateFromQuaternion(WorldPlane2D.NormalizeOrIdentity(item.Rotation)) *
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

        private int DrawModelInstanceBatch(Model model, ModelInstanceBatch batch, uint colorKey, int materialId)
        {
            if (model.meshCount <= 0 || batch.Count <= 0)
            {
                return 0;
            }

            EnsureFrameLightingAppliedForInstancing();
            int drawCalls = 0;
            long drawStart = Stopwatch.GetTimestamp();
            RestoreOpaqueModelState();
            fixed (RaylibMatrix* transforms = batch.Transforms)
            {
                for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                {
                    Mesh mesh = model.meshes[meshIndex];
                    RequireMeshNormals(in mesh, "Instanced ISM");
                    if (!TryResolveInstancedModelMaterial(model, meshIndex, materialId, out Material material))
                    {
                        continue;
                    }
                    ApplyInstancedMaterialTint(ref material, colorKey);
                    for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                    {
                        int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                        Rl.DrawMeshInstanced(mesh, material, transforms + offset, chunkCount);
                        drawCalls++;
                    }
                }
            }

            LastInstancedMeshDrawMs += (Stopwatch.GetTimestamp() - drawStart) * 1000.0 / Stopwatch.Frequency;
            return drawCalls;
        }

        private int DrawGpuSkinnedInstanceBatch(GpuSkinnedInstanceBatch batch)
        {
            Model model = batch.Model;
            if (model.meshCount <= 0 || batch.Count <= 0)
            {
                return 0;
            }

            if (batch.Animations == null || batch.AnimCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} GpuSkinned batch meshAssetId={batch.Key.MeshAssetId} has no animations; silent static draw is forbidden.");
            }

            int clipIndex = batch.Key.ClipIndex;
            int frameIndex = batch.Key.FrameIndex;
            if ((uint)clipIndex >= (uint)batch.AnimCount)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} GpuSkinned batch clipIndex={clipIndex} outside animCount={batch.AnimCount}.");
            }

            ModelAnimation anim = batch.Animations[clipIndex];
            Rl.UpdateModelAnimationBones(model, anim, frameIndex);

            EnsureFrameLightingAppliedForSkinning();
            int drawCalls = 0;
            uint colorKey = batch.Key.ColorKey;
            int materialId = batch.Key.MaterialId;
            RestoreOpaqueModelState();
            fixed (RaylibMatrix* transforms = batch.Transforms)
            {
                for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                {
                    Mesh mesh = model.meshes[meshIndex];
                    if (mesh.vertexCount <= 0)
                    {
                        continue;
                    }

                    RequireMeshNormals(in mesh, "GpuSkinnedInstance");
                    if (!TryResolveInstancedModelMaterial(model, meshIndex, materialId, out Material material))
                    {
                        continue;
                    }

                    material.shader = _skinningShader;
                    ApplyGpuSkinnedMaterialTint(ref material, colorKey);

                    if (mesh.boneMatrices != null && mesh.boneCount > 0)
                    {
                        Rl.rlEnableShader(_skinningShader.id);
                        Rl.rlSetUniformMatrices(_locBoneMatrices, mesh.boneMatrices, mesh.boneCount);
                    }

                    for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                    {
                        int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                        Rl.DrawMeshInstanced(mesh, material, transforms + offset, chunkCount);
                        drawCalls++;
                    }
                }
            }

            return drawCalls;
        }

        private static void RestoreOpaqueModelState()
        {
            Rl.rlEnableDepthTest();
            Rl.rlEnableDepthMask();
            Rl.rlEnableBackfaceCulling();
        }

        private bool TryResolveInstancedModelMaterial(Model model, int meshIndex, int materialId, out Material material)
        {
            if (model.materialCount <= 0 || model.materials == null)
            {
                material = default;
                int reportKey = HashCode.Combine(model.meshCount, meshIndex, model.materialCount);
                if (_reportedInvalidInstancedMaterials.Add(reportKey))
                {
                    Log.Warn(
                        in LogChannels.Presentation,
                        $"Raylib instanced model skipped meshIndex={meshIndex}: imported model has no material. Host asset material import must provide an explicit material.");
                }

                return false;
            }

            int materialIndex = 0;
            if (model.meshMaterial != null && meshIndex >= 0 && meshIndex < model.meshCount)
            {
                materialIndex = model.meshMaterial[meshIndex];
            }

            if (materialIndex < 0 || materialIndex >= model.materialCount)
            {
                int reportKey = HashCode.Combine(model.meshCount, meshIndex, materialIndex);
                if (_reportedInvalidInstancedMaterials.Add(reportKey))
                {
                    Log.Warn(
                        in LogChannels.Presentation,
                        $"Raylib instanced model skipped meshIndex={meshIndex}: meshMaterial index {materialIndex} is outside materialCount={model.materialCount}.");
                }

                material = default;
                return false;
            }

            material = model.materials[materialIndex];
            material.shader = _shader;
            ApplyHostMaterialAlbedo(ref material, materialId);
            return true;
        }

        private void ApplyHostMaterialAlbedo(ref Material material, int materialId)
        {
            _materialHostBinder?.TryApplyAlbedo(ref material, materialId);
        }

        private void ApplyHostAlbedoToModel(ref Model model, int materialId)
        {
            if (_materialHostBinder == null || materialId <= 0 || model.materialCount <= 0 || model.materials == null)
            {
                return;
            }

            for (int i = 0; i < model.materialCount; i++)
            {
                ref Material material = ref model.materials[i];
                ApplyHostMaterialAlbedo(ref material, materialId);
            }
        }

        private void ApplyInstancedMaterialTint(ref Material material, uint colorKey)
        {
            SetTintUniform(colorKey);
            if (_locColDiffuse >= 0 && material.maps != null)
            {
                int albedoIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO;
                Color color = material.maps[albedoIndex].color;
                Vector4 diffuse = new(color.r / 255f, color.g / 255f, color.b / 255f, color.a / 255f);
                Rl.SetShaderValue(_shader, _locColDiffuse, &diffuse, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            }
        }

        private void ApplyGpuSkinnedMaterialTint(ref Material material, uint colorKey)
        {
            if (_locSkinningTint >= 0)
            {
                float r = (colorKey & 0xFF) / 255f;
                float g = ((colorKey >> 8) & 0xFF) / 255f;
                float b = ((colorKey >> 16) & 0xFF) / 255f;
                float a = ((colorKey >> 24) & 0xFF) / 255f;
                var tint = new Vector4(r, g, b, a);
                Rl.SetShaderValue(_skinningShader, _locSkinningTint, &tint, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            }

            if (_locSkinningColDiffuse >= 0 && material.maps != null)
            {
                int albedoIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO;
                Color color = material.maps[albedoIndex].color;
                Vector4 diffuse = new(color.r / 255f, color.g / 255f, color.b / 255f, color.a / 255f);
                Rl.SetShaderValue(_skinningShader, _locSkinningColDiffuse, &diffuse, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            }
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

        private void AddInstance(List<Batch> batches, uint colorKey, in RaylibMatrix matrix)
        {
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                if (b.ColorKey != colorKey) continue;

                b.Add(matrix);
                batches[i] = b;
                return;
            }

            var nb = new Batch(colorKey);
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
            RequireMeshNormals(in mesh, "Instanced primitive");
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                if (b.Count == 0) continue;

                SetTintUniform(b.ColorKey);
                SetColDiffuseUniform(Vector4.One);

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

        private void SetTintUniform(uint colorKey)
        {
            if (_locTint < 0) return;

            float r = (colorKey & 0xFF) / 255f;
            float g = ((colorKey >> 8) & 0xFF) / 255f;
            float b = ((colorKey >> 16) & 0xFF) / 255f;
            float a = ((colorKey >> 24) & 0xFF) / 255f;
            var cd = new Vector4(r, g, b, a);
            Rl.SetShaderValue(_shader, _locTint, &cd, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        private void SetColDiffuseUniform(Vector4 color)
        {
            if (_locColDiffuse < 0) return;
            Rl.SetShaderValue(_shader, _locColDiffuse, &color, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            _cubeMesh = Rl.GenMeshCube(1f, 1f, 1f);
            if (_cubeMesh.colors == null)
            {
                int bytes = _cubeMesh.vertexCount * 4;
                _cubeMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _cubeMesh.colors[i] = 255;
            }
            Rl.UploadMesh(ref _cubeMesh, false);

            _sphereMesh = Rl.GenMeshSphere(0.5f, 8, 8);
            if (_sphereMesh.colors == null)
            {
                int bytes = _sphereMesh.vertexCount * 4;
                _sphereMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _sphereMesh.colors[i] = 255;
            }
            Rl.UploadMesh(ref _sphereMesh, false);

            _vfxBillboardMesh = Rl.GenMeshCube(1f, 1f, 1f);
            if (_vfxBillboardMesh.colors == null)
            {
                int bytes = _vfxBillboardMesh.vertexCount * 4;
                _vfxBillboardMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _vfxBillboardMesh.colors[i] = 255;
            }
            Rl.UploadMesh(ref _vfxBillboardMesh, false);

            RaylibEffectShader defaultVfx = _effectShaders.GetOrLoad(RaylibEffectShaderRegistry.DefaultUnlitTintKey);
            _vfxMaterial = Rl.LoadMaterialDefault();
            _vfxMaterial.shader = defaultVfx.Shader;
            _vfxMaterialLoaded = true;

            string baseDir = AppContext.BaseDirectory;
            _shader = Rl.LoadShader(Path.Combine(baseDir, "instancing.vs"), Path.Combine(baseDir, "instancing.fs"));
            if (_shader.id == 0) throw new InvalidOperationException("Failed to load instancing shader (shader.id == 0).");

            _material = Rl.LoadMaterialDefault();
            _material.shader = _shader;

            _locColDiffuse = Rl.GetShaderLocation(_shader, "colDiffuse");
            _locTint = Rl.GetShaderLocation(_shader, "tint");
            int locMapAlbedo = Rl.GetShaderLocation(_shader, "texture0");
            int locMvp = Rl.GetShaderLocation(_shader, "mvp");
            int locInstance = Rl.GetShaderLocationAttrib(_shader, "instanceTransform");
            int locVertexPosition = Rl.GetShaderLocationAttrib(_shader, "vertexPosition");
            int locVertexTexCoord = Rl.GetShaderLocationAttrib(_shader, "vertexTexCoord");
            int locVertexNormal = Rl.GetShaderLocationAttrib(_shader, "vertexNormal");
            _instancingLightingLocs = RaylibFrameLightingLocations.ResolveOrThrow(_shader, "instancing");

            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD02] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locVertexNormal;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TANGENT] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_VIEW] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_PROJECTION] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locInstance;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_NORMAL] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VECTOR_VIEW] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = _locColDiffuse;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_SPECULAR] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_AMBIENT] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_METALNESS] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_NORMAL] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ROUGHNESS] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_OCCLUSION] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_EMISSION] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_HEIGHT] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_CUBEMAP] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_IRRADIANCE] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_PREFILTER] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_BRDF] = -1;

            if (locVertexPosition < 0) throw new InvalidOperationException("Shader attrib 'vertexPosition' not found.");
            if (locVertexTexCoord < 0) throw new InvalidOperationException("Shader attrib 'vertexTexCoord' not found.");
            if (locVertexNormal < 0) throw new InvalidOperationException("Shader attrib 'vertexNormal' not found.");
            if (locMvp < 0) throw new InvalidOperationException("Shader uniform 'mvp' not found.");
            if (locInstance < 0) throw new InvalidOperationException("Shader attrib 'instanceTransform' not found.");
            if (_locColDiffuse < 0) throw new InvalidOperationException("Shader uniform 'colDiffuse' not found.");
            if (_locTint < 0) throw new InvalidOperationException("Shader uniform 'tint' not found.");
            if (locMapAlbedo < 0) throw new InvalidOperationException("Shader uniform 'texture0' not found.");

            _initialized = true;
            if (_frameLighting != null)
            {
                _frameLighting.Apply(_shader, in _instancingLightingLocs);
                if (_hasFrameViewPos)
                {
                    _frameLighting.ApplyViewPosition(_shader, in _instancingLightingLocs, _frameViewPos);
                }
            }
        }

        private void EnsureSkinningShaderInitialized()
        {
            if (_skinningShaderReady)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            string vsPath = Path.Combine(baseDir, "skinning_instanced.vs");
            string fsPath = Path.Combine(baseDir, "skinning_instanced.fs");
            if (!File.Exists(vsPath) || !File.Exists(fsPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} GpuSkinnedInstance requires skinning_instanced.vs/.fs beside the binary (missing under '{baseDir}').");
            }

            _skinningShader = Rl.LoadShader(vsPath, fsPath);
            if (_skinningShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load skinning_instanced shader (shader.id == 0).");
            }

            _locBoneMatrices = Rl.GetShaderLocation(_skinningShader, "boneMatrices");
            _locSkinningTint = Rl.GetShaderLocation(_skinningShader, "tint");
            _locSkinningColDiffuse = Rl.GetShaderLocation(_skinningShader, "colDiffuse");
            int locMapAlbedo = Rl.GetShaderLocation(_skinningShader, "texture0");
            int locMvp = Rl.GetShaderLocation(_skinningShader, "mvp");
            int locInstance = Rl.GetShaderLocationAttrib(_skinningShader, "instanceTransform");
            int locVertexPosition = Rl.GetShaderLocationAttrib(_skinningShader, "vertexPosition");
            int locVertexTexCoord = Rl.GetShaderLocationAttrib(_skinningShader, "vertexTexCoord");
            int locVertexNormal = Rl.GetShaderLocationAttrib(_skinningShader, "vertexNormal");
            int locVertexColor = Rl.GetShaderLocationAttrib(_skinningShader, "vertexColor");
            int locBoneIds = Rl.GetShaderLocationAttrib(_skinningShader, "vertexBoneIds");
            int locBoneWeights = Rl.GetShaderLocationAttrib(_skinningShader, "vertexBoneWeights");
            _skinningLightingLocs = RaylibFrameLightingLocations.ResolveOrThrow(_skinningShader, "skinning_instanced");

            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locVertexNormal;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = locVertexColor;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEIDS] = locBoneIds;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEWEIGHTS] = locBoneWeights;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locInstance;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = _locSkinningColDiffuse;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_BONE_MATRICES] = _locBoneMatrices;

            if (_locBoneMatrices < 0) throw new InvalidOperationException("Skinning shader uniform 'boneMatrices' not found.");
            if (locMvp < 0) throw new InvalidOperationException("Skinning shader uniform 'mvp' not found.");
            if (locInstance < 0) throw new InvalidOperationException("Skinning shader attrib 'instanceTransform' not found.");
            if (locVertexPosition < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexPosition' not found.");
            if (locVertexNormal < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexNormal' not found.");
            if (locBoneIds < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexBoneIds' not found.");
            if (locBoneWeights < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexBoneWeights' not found.");
            if (_locSkinningColDiffuse < 0) throw new InvalidOperationException("Skinning shader uniform 'colDiffuse' not found.");
            if (_locSkinningTint < 0) throw new InvalidOperationException("Skinning shader uniform 'tint' not found.");
            if (locMapAlbedo < 0) throw new InvalidOperationException("Skinning shader uniform 'texture0' not found.");

            _skinningShaderReady = true;
            if (_frameLighting != null)
            {
                _frameLighting.Apply(_skinningShader, in _skinningLightingLocs);
                if (_hasFrameViewPos)
                {
                    _frameLighting.ApplyViewPosition(_skinningShader, in _skinningLightingLocs, _frameViewPos);
                }
            }
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

            _frameLighting.Apply(_shader, in _instancingLightingLocs);
            _frameLighting.ApplyViewPosition(_shader, in _instancingLightingLocs, _frameViewPos);
        }

        private void EnsureFrameLightingAppliedForSkinning()
        {
            if (_frameLighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} lit GpuSkinnedInstance requires {nameof(ApplyFrameLighting)} before draw.");
            }

            if (!_hasFrameViewPos)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} lit GpuSkinnedInstance requires camera view position before draw.");
            }

            EnsureSkinningShaderInitialized();
            _frameLighting.Apply(_skinningShader, in _skinningLightingLocs);
            _frameLighting.ApplyViewPosition(_skinningShader, in _skinningLightingLocs, _frameViewPos);
        }

        private static void RequireMeshNormals(in Mesh mesh, string lane)
        {
            if (mesh.normals == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPrimitiveRenderer)} {lane} lit path requires mesh normals (vertexCount={mesh.vertexCount}); silent flat shading is forbidden.");
            }
        }

        private static uint PackRgba(in Vector4 c)
        {
            uint r = Clamp01ToByte(c.X);
            uint g = Clamp01ToByte(c.Y);
            uint b = Clamp01ToByte(c.Z);
            uint a = Clamp01ToByte(c.W);
            return r | (g << 8) | (b << 16) | (a << 24);
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
            float key = _frameLighting.LightIntensity * 0.55f;
            float exposure = Math.Clamp((ambient.W * 3.2f) + key, 0.08f, 1.35f);
            return new Vector3(
                Math.Clamp(ambient.X * exposure + (_frameLighting.LightColor.X * key * 0.35f), 0f, 1.5f),
                Math.Clamp(ambient.Y * exposure + (_frameLighting.LightColor.Y * key * 0.35f), 0f, 1.5f),
                Math.Clamp(ambient.Z * exposure + (_frameLighting.LightColor.Z * key * 0.35f), 0f, 1.5f));
        }

        private static byte Clamp01ToByte(float v) => RaylibColorUtil.Clamp01ToByte(v);

        public void Dispose()
        {
            foreach (var kvp in _modelCache)
            {
                if (!kvp.Value.Loaded)
                {
                    continue;
                }

                Model model = kvp.Value.Model;
                _materialHostBinder?.DetachOwnedAlbedoMaps(model);
                Rl.UnloadModel(model);
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
                    Rl.UnloadTexture(kvp.Value.Texture);
            }
            _textureCache.Clear();

            if (_proceduralMeshMaterialLoaded)
            {
                _materialHostBinder?.DetachOwnedAlbedoMap(ref _proceduralMeshMaterial);
                Rl.UnloadMaterial(_proceduralMeshMaterial);
                _proceduralMeshMaterialLoaded = false;
            }

            if (_vfxMaterialLoaded)
            {
                _vfxMaterial.shader = default;
                Rl.UnloadMaterial(_vfxMaterial);
                _vfxMaterialLoaded = false;
            }

            _gpuSkinnedModelCache.UnloadAll(model => _materialHostBinder?.DetachOwnedAlbedoMaps(model));
            _gpuSkinnedModelCache.Dispose();
            _effectShaders.Dispose();
            _materialHostBinder?.Dispose();

            if (!_initialized) return;

            if (_cubeMesh.vertexCount > 0) Rl.UnloadMesh(_cubeMesh);
            if (_sphereMesh.vertexCount > 0) Rl.UnloadMesh(_sphereMesh);
            if (_vfxBillboardMesh.vertexCount > 0) Rl.UnloadMesh(_vfxBillboardMesh);
            _material.shader = default;
            Rl.UnloadMaterial(_material);
            Rl.UnloadShader(_shader);
            if (_vegetationCutoutShaderReady)
            {
                Rl.UnloadShader(_vegetationCutoutShader);
                _vegetationCutoutShaderReady = false;
            }
            if (_skinningShaderReady)
            {
                Rl.UnloadShader(_skinningShader);
                _skinningShaderReady = false;
            }
            _initialized = false;
        }

        private struct CachedModel
        {
            public Model Model;
            public bool Loaded;
        }

        private struct CachedTexture
        {
            public Texture2D Texture;
            public bool Loaded;
            public float AspectRatio;
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
            public RaylibMatrix[] Transforms;
            public int Count;

            public Batch(uint colorKey, int initialCapacity = 256)
            {
                ColorKey = colorKey;
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

        private readonly record struct GpuSkinnedInstanceBatchKey(
            int MeshAssetId,
            int MaterialId,
            uint ColorKey,
            int ClipIndex,
            int FrameIndex);

        private sealed class GpuSkinnedInstanceBatch
        {
            public readonly GpuSkinnedInstanceBatchKey Key;
            public RaylibMatrix[] Transforms;
            public int Count;
            public Model Model;
            public ModelAnimation* Animations;
            public int AnimCount;

            public GpuSkinnedInstanceBatch(GpuSkinnedInstanceBatchKey key, int initialCapacity = 256)
            {
                Key = key;
                Transforms = new RaylibMatrix[Math.Max(4, initialCapacity)];
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
