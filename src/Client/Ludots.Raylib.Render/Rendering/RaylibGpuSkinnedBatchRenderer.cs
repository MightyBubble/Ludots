using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    public enum RaylibGpuSkinnedSubmitOutcome : byte
    {
        Unsupported = 0,
        Submitted = 1,
        InFlight = 2,
    }

    internal sealed unsafe class RaylibGpuSkinnedBatchRenderer : IDisposable
    {
        private readonly RaylibGpuSkinnedModelCache _modelCache;
        private readonly RaylibInstancedMaterialPipeline _materials;
        private readonly int _maxModelInstancesPerDraw;
        private readonly RaylibGpuSkinnedCapacity? _capacity;

        private Shader _skinningShader;
        private bool _skinningShaderReady;
        private int _locSkinningColDiffuse;
        private int _locSkinningRoughness;
        private int _locSkinningMetallic;
        private int _locSkinningHasRoughnessMap;
        private int _locSkinningHasMetallicMap;
        private int _locSkyZenith = -1;
        private int _locSkyGround = -1;
        private int _locEnvSpecular = -1;
        private RaylibPbrUniformLocations _skinningPbrLocs;
        private RaylibFrameLightingLocations _skinningLightingLocs;
        private RaylibShadowSamplingLocations _skinningShadowLocs;

        private readonly Dictionary<GpuSkinnedInstanceBatchKey, GpuSkinnedInstanceBatch> _gpuSkinnedInstanceBatches;
        private readonly GpuSkinnedInstanceBatch[] _gpuSkinnedInstanceBatchSlots;
        private readonly Dictionary<GpuSkinnedBatchFamilyKey, GpuSkinnedBatchFamily> _gpuSkinnedBatchFamilies;
        private readonly GpuSkinnedBatchFamily[] _gpuSkinnedBatchFamilySlots;
        private readonly List<GpuSkinnedInstanceBatch> _activeGpuSkinnedInstanceBatches;
        private readonly RaylibMatrix[] _collectedTransforms;
        private readonly int[] _collectedPoseRows;
        private readonly Vector4[] _collectedTints;
        private readonly int[] _collectedBatchIndices;
        private readonly RaylibMatrix[] _packedTransforms;
        private readonly int[] _packedPoseRows;
        private readonly Vector4[] _packedTints;
        private readonly StableIdFrameIndex? _stableIds;
        private int _collectedInstanceCount;
        private int _registeredBatchCount;
        private int _registeredFamilyCount;
        private long _prepareStartTimestamp;
        private bool _frameCollecting;
        private bool _frameSealed;
        private readonly uint[] _drawTimingQueries = new uint[4];
        private bool _drawTimingQueriesReady;
        public double LastMainDrawGpuMs { get; private set; }
        public double LastShadowDrawGpuMs { get; private set; }

        private unsafe void EnsureDrawTimingQueries()
        {
            if (_drawTimingQueriesReady)
            {
                return;
            }

            fixed (uint* queries = _drawTimingQueries)
            {
                Gl43.GenQueries(4, queries);
            }

            _drawTimingQueriesReady = true;
        }

        private unsafe void ReadBackDrawTiming()
        {
            ulong mainEnd = 0, shadowEnd = 0;
            Gl43.GetQueryObjectui64v(_drawTimingQueries[1], Gl43.GL_QUERY_RESULT_NO_WAIT, &mainEnd);
            Gl43.GetQueryObjectui64v(_drawTimingQueries[3], Gl43.GL_QUERY_RESULT_NO_WAIT, &shadowEnd);
            if (mainEnd != 0)
            {
                ulong mainStart = 0;
                Gl43.GetQueryObjectui64v(_drawTimingQueries[0], Gl43.GL_QUERY_RESULT, &mainStart);
                LastMainDrawGpuMs = (mainEnd - mainStart) / 1e6;
            }

            if (shadowEnd != 0)
            {
                ulong shadowStart = 0;
                Gl43.GetQueryObjectui64v(_drawTimingQueries[2], Gl43.GL_QUERY_RESULT, &shadowStart);
                LastShadowDrawGpuMs = (shadowEnd - shadowStart) / 1e6;
            }
        }

        public double LastPoseComputeGpuMs => _ssbo?.LastPoseComputeGpuMs ?? 0d;

        private bool _mainDirectPrimedThisFrame;
        private bool _shadowDirectPrimedThisFrame;

        private RaylibFrameLighting? _frameLighting;
        private Vector3 _frameViewPos;
        private bool _hasFrameViewPos;
        private RaylibDirectionalShadowMap? _frameShadow;
        private float _frameShadowTexelWorld = 0.04f;
        private RaylibGpuSkinnedSsboPipeline? _ssbo;
        private readonly Dictionary<(int MeshAssetId, int ClipIndex, int FrameIndex), int> _poseRowByKey;
        private readonly List<(int PoseRow, GpuSkinnedInstanceBatch Batch, int ClipIndex, int FrameIndex)> _dirtyPoseRows;
        private int _locInstanceBase = -1;
        private int _locBoneBase = -1;
        private int _locPoseStride = -1;
        private int _locMvp = -1;
        private int _posePhaseBuckets;

        internal Func<int, int, IReadOnlyDictionary<int, int>?>? AnimationStateMapResolver { get; set; }

        internal int PosePhaseBuckets
        {
            get => _posePhaseBuckets;
            set
            {
                if (value <= 0)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires a positive pose phase bucket count.");
                }
                if (_registeredBatchCount != 0 || _registeredFamilyCount != 0)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} pose phase buckets must be configured before the first submission.");
                }

                _posePhaseBuckets = value;
            }
        }

        public RaylibGpuSkinnedBatchRenderer(
            RaylibGpuSkinnedModelCache modelCache,
            RaylibInstancedMaterialPipeline materials,
            int maxModelInstancesPerDraw,
            RaylibGpuSkinnedCapacity? capacity)
        {
            _modelCache = modelCache ?? throw new ArgumentNullException(nameof(modelCache));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
            _maxModelInstancesPerDraw = maxModelInstancesPerDraw;
            _capacity = capacity;
            capacity?.Validate();
            int batchCapacity = capacity?.MaxBatches ?? 0;
            int poseCapacity = capacity?.MaxUniquePoses ?? 0;
            int instanceCapacity = capacity?.MaxInstances ?? 0;
            _gpuSkinnedInstanceBatches = new Dictionary<GpuSkinnedInstanceBatchKey, GpuSkinnedInstanceBatch>(batchCapacity);
            _gpuSkinnedInstanceBatchSlots = new GpuSkinnedInstanceBatch[batchCapacity];
            _gpuSkinnedBatchFamilies = new Dictionary<GpuSkinnedBatchFamilyKey, GpuSkinnedBatchFamily>(batchCapacity);
            _gpuSkinnedBatchFamilySlots = new GpuSkinnedBatchFamily[batchCapacity];
            for (int i = 0; i < _gpuSkinnedInstanceBatchSlots.Length; i++)
            {
                _gpuSkinnedInstanceBatchSlots[i] = new GpuSkinnedInstanceBatch();
                _gpuSkinnedBatchFamilySlots[i] = new GpuSkinnedBatchFamily();
            }
            _activeGpuSkinnedInstanceBatches = new List<GpuSkinnedInstanceBatch>(batchCapacity);
            _poseRowByKey = new Dictionary<(int MeshAssetId, int ClipIndex, int FrameIndex), int>(poseCapacity);
            _dirtyPoseRows = new List<(int PoseRow, GpuSkinnedInstanceBatch Batch, int ClipIndex, int FrameIndex)>(poseCapacity);
            _collectedTransforms = new RaylibMatrix[instanceCapacity];
            _collectedPoseRows = new int[instanceCapacity];
            _collectedTints = new Vector4[instanceCapacity];
            _collectedBatchIndices = new int[instanceCapacity];
            _packedTransforms = new RaylibMatrix[instanceCapacity];
            _packedPoseRows = new int[instanceCapacity];
            _packedTints = new Vector4[instanceCapacity];
            _stableIds = capacity.HasValue
                ? new StableIdFrameIndex(instanceCapacity)
                : null;
        }

        public int LastInstances => LastMainInstancesSubmitted;
        public int LastBatches => LastMainDrawCalls;
        public double LastMatrixBuildMs => LastPrepareCpuMs;
        public int LastCollectedUniqueInstances { get; private set; }
        public int LastMainInstancesSubmitted { get; private set; }
        public int LastMainDrawCalls { get; private set; }
        public long LastMainTrianglesSubmitted { get; private set; }
        public int LastShadowInstancesSubmitted { get; private set; }
        public int LastShadowDrawCalls { get; private set; }
        public long LastShadowTrianglesSubmitted { get; private set; }
        public int LastCollectedHighLodInstances { get; private set; }
        public int LastCollectedMediumLodInstances { get; private set; }
        public int LastCollectedLowLodInstances { get; private set; }
        public double LastPrepareCpuMs { get; private set; }
        public double LastMeshDrawMs { get; private set; }
        public double LastPoseBuildCpuMs { get; private set; }
        public double LastTextureUploadCpuMs { get; private set; }
        public double LastShadowSubmitCpuMs { get; private set; }
        public int LastUniquePoses { get; private set; }
        public long LastTextureUploadBytes { get; private set; }
        public int LastValidatedStableIds => _stableIds?.Count ?? 0;

        public bool DeviceResourcesInitialized => _ssbo != null && _skinningShaderReady;

        internal int BatchSlotCapacity => _gpuSkinnedInstanceBatchSlots.Length;

        internal int RegisteredBatchCount => _registeredBatchCount;

        public bool FramePrepared => _frameSealed;

        public bool HasActiveBatches => _activeGpuSkinnedInstanceBatches.Count > 0;

        public void ResetStats()
        {
            LastCollectedUniqueInstances = 0;
            LastMainInstancesSubmitted = 0;
            LastMainDrawCalls = 0;
            LastMainTrianglesSubmitted = 0;
            LastShadowInstancesSubmitted = 0;
            LastShadowDrawCalls = 0;
            LastShadowTrianglesSubmitted = 0;
            LastCollectedHighLodInstances = 0;
            LastCollectedMediumLodInstances = 0;
            LastCollectedLowLodInstances = 0;
            LastPrepareCpuMs = 0d;
            LastMeshDrawMs = 0d;
            LastPoseBuildCpuMs = 0d;
            LastTextureUploadCpuMs = 0d;
            LastShadowSubmitCpuMs = 0d;
            LastUniquePoses = 0;
            LastTextureUploadBytes = 0;
        }

        public void InitializeDeviceResources()
        {
            if (DeviceResourcesInitialized)
            {
                return;
            }

            RaylibGpuSkinnedCapacity capacity = _capacity
                ?? throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires explicit capacity configuration before device resource initialization.");
            if (_frameCollecting || _frameSealed)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} device resources must be initialized outside an active frame.");
            }
            if (Rl.GetWindowHandle() == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} device resource initialization requires an active Raylib window and GL context.");
            }

            Gl43.Initialize();
            _ssbo ??= new RaylibGpuSkinnedSsboPipeline(
                capacity.MaxUniquePoses,
                capacity.MaxBoneSlots,
                capacity.MaxInstances);
            EnsureShaderInitialized();
        }

        public void ApplyFrameLighting(RaylibFrameLighting lighting, Vector3 viewPos, RaylibDirectionalShadowMap? shadow, float shadowTexelWorld)
        {
            _frameLighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            _frameViewPos = viewPos;
            _hasFrameViewPos = true;
            _frameShadow = shadow;
            _frameShadowTexelWorld = shadowTexelWorld;
            if (_skinningShaderReady)
            {
                ApplySkinningFrameLighting();
            }
        }

        public void BeginFrame()
        {
            if (_frameCollecting || _frameSealed)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} already has an active frame.");
            }

            long prepareStartTimestamp = Stopwatch.GetTimestamp();

            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                batch.Count = 0;
                batch.ActiveIndex = -1;
                batch.WriteCursor = 0;
            }

            _activeGpuSkinnedInstanceBatches.Clear();
            _collectedInstanceCount = 0;
            _stableIds?.BeginFrame();
            EnsureDrawTimingQueries();
            ReadBackDrawTiming();
            _mainDirectPrimedThisFrame = false;
            _shadowDirectPrimedThisFrame = false;
            _poseRowByKey.Clear();
            _dirtyPoseRows.Clear();
            ResetStats();
            _prepareStartTimestamp = prepareStartTimestamp;
            _frameCollecting = true;
        }

        public bool TryCollect(in SkinnedVisualBatchItem item, IRenderMeshAssets meshes, float scaleMul)
        {
            return TryCollect(in item, meshes, scaleMul, out _);
        }

        public bool TryCollect(
            in SkinnedVisualBatchItem item,
            IRenderMeshAssets meshes,
            float scaleMul,
            out RaylibGpuSkinnedSubmitOutcome outcome)
        {
            if (!_frameCollecting)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires BeginFrame before collection.");
            }

            outcome = RaylibGpuSkinnedSubmitOutcome.Unsupported;
            if (item.RenderPath != VisualRenderPath.GpuSkinnedInstance)
            {
                return false;
            }

            RaylibGpuSkinnedCapacity capacity = _capacity
                ?? throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires explicit capacity configuration before GpuSkinnedInstance submission.");
            RequireDeviceResourcesInitialized();
            RequireSupportedLod(item.LOD);
            if (_collectedInstanceCount >= capacity.MaxInstances)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires another instance slot; configured maxInstances={capacity.MaxInstances}.");
            }

            var familyKey = new GpuSkinnedBatchFamilyKey(
                item.MeshAssetId,
                item.MaterialId,
                item.LOD,
                item.AnimationProfileId);
            if (!_gpuSkinnedBatchFamilies.TryGetValue(familyKey, out GpuSkinnedBatchFamily? family))
            {
                if (_registeredFamilyCount >= _gpuSkinnedBatchFamilySlots.Length)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires another batch family for meshAssetId={item.MeshAssetId}, materialId={item.MaterialId}; configured maxBatches={capacity.MaxBatches}.");
                }

                if (!meshes.TryGetDescriptor(item.MeshAssetId, out MeshAssetDescriptor logicalDescriptor) ||
                    logicalDescriptor.Type != MeshAssetType.Model)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} logical meshAssetId={item.MeshAssetId} is not a registered Model.");
                }

                int poseAssetId = item.MeshAssetId;
                int mainAssetId = item.MeshAssetId;
                int shadowAssetId = item.MeshAssetId;
                if (logicalDescriptor.GpuSkinnedLod.IsConfigured)
                {
                    poseAssetId = logicalDescriptor.GpuSkinnedLod.Main.High;
                    mainAssetId = logicalDescriptor.GpuSkinnedLod.ResolveMain(item.LOD);
                    shadowAssetId = logicalDescriptor.GpuSkinnedLod.ResolveShadow(item.LOD);
                }

                if (!TryAcquireEntry(meshes, poseAssetId, "pose", out RaylibGpuSkinnedModelCache.Entry poseEntry, out outcome) ||
                    !TryAcquireEntry(meshes, mainAssetId, "main", out RaylibGpuSkinnedModelCache.Entry mainEntry, out outcome) ||
                    !TryAcquireEntry(meshes, shadowAssetId, "shadow", out RaylibGpuSkinnedModelCache.Entry shadowEntry, out outcome))
                {
                    return false;
                }

                ValidateCompatibleModel(item.MeshAssetId, poseAssetId, in poseEntry, mainAssetId, in mainEntry, "main");
                ValidateCompatibleModel(item.MeshAssetId, poseAssetId, in poseEntry, shadowAssetId, in shadowEntry, "shadow");

                IReadOnlyDictionary<int, int>? stateToClipMap = RaylibSkinnedPlayback.ResolveStateMap(
                    item.AnimationProfileId,
                    item.MeshAssetId,
                    AnimationStateMapResolver);
                bool castsShadow = ResolveCastsShadow(item.MaterialId);

                family = _gpuSkinnedBatchFamilySlots[_registeredFamilyCount];
                family.Bind(
                    familyKey,
                    poseAssetId,
                    in poseEntry,
                    in mainEntry,
                    in shadowEntry,
                    stateToClipMap,
                    castsShadow);
                (_ssbo ?? throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinnedInstance collection requires initialized SSBO pose pipeline."))
                    .RegisterModel(poseAssetId, poseEntry.Model, poseEntry.Animations, poseEntry.AnimCount);
                _gpuSkinnedBatchFamilies.Add(familyKey, family);
                _registeredFamilyCount++;
            }

            AnimatorPackedState animator = item.Animator;
            RaylibSkinnedPlayback.ResolveFromAnimator(
                in animator,
                family.Animations,
                family.AnimCount,
                family.StateToClipMap,
                out int clipIndex,
                out int frameIndex);
            int poseFrame = RaylibSkinnedPlayback.QuantizeFrameIndex(
                frameIndex,
                family.Animations[clipIndex].frameCount,
                PosePhaseBuckets);

            var key = new GpuSkinnedInstanceBatchKey(
                item.MeshAssetId,
                item.MaterialId,
                item.LOD,
                item.AnimationProfileId,
                clipIndex,
                poseFrame);
            if (!_gpuSkinnedInstanceBatches.TryGetValue(key, out GpuSkinnedInstanceBatch? batch))
            {
                if (_registeredBatchCount >= _gpuSkinnedInstanceBatchSlots.Length)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires another pose batch for meshAssetId={item.MeshAssetId}, materialId={item.MaterialId}, clipIndex={clipIndex}, frameIndex={poseFrame}; configured maxBatches={capacity.MaxBatches}.");
                }

                batch = _gpuSkinnedInstanceBatchSlots[_registeredBatchCount];
                batch.Bind(key, family, clipIndex, poseFrame);
                _gpuSkinnedInstanceBatches.Add(key, batch);
                _registeredBatchCount++;
            }

            // All validated LOD variants share the canonical pose asset and one palette row.
            var poseKey = (item.MeshAssetId, clipIndex, poseFrame);
            bool isNewPose = !_poseRowByKey.TryGetValue(poseKey, out int poseRow);
            if (isNewPose)
            {
                if (_poseRowByKey.Count >= capacity.MaxUniquePoses)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires another pose row for meshAssetId={item.MeshAssetId}, clipIndex={clipIndex}, frameIndex={frameIndex}; configured maxUniquePoses={capacity.MaxUniquePoses}.");
                }

                poseRow = _poseRowByKey.Count;
            }

            (_stableIds ?? throw new InvalidOperationException(
                $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires a configured StableId index before GpuSkinnedInstance submission."))
                .Add(item.StableId);

            if (batch.Count == 0)
            {
                batch.ActiveIndex = _activeGpuSkinnedInstanceBatches.Count;
                _activeGpuSkinnedInstanceBatches.Add(batch);
            }

            if (isNewPose)
            {
                _poseRowByKey[poseKey] = poseRow;
                _dirtyPoseRows.Add((poseRow, batch, clipIndex, poseFrame));
            }

            int collectedIndex = _collectedInstanceCount++;
            _collectedTransforms[collectedIndex] = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(item.Scale * scaleMul) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(item.Rotation)) *
                Matrix4x4.CreateTranslation(item.Position));
            _collectedPoseRows[collectedIndex] = poseRow;
            _collectedTints[collectedIndex] = item.Color;
            _collectedBatchIndices[collectedIndex] = batch.ActiveIndex;
            batch.Count++;
            LastCollectedUniqueInstances++;
            RecordCollectedLod(item.LOD);
            outcome = RaylibGpuSkinnedSubmitOutcome.Submitted;
            return true;
        }

        public void SealFrame()
        {
            if (!_frameCollecting || _frameSealed)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires one active collection before SealFrame.");
            }

            EvaluatePosesOnGpu();
            if (LastCollectedUniqueInstances != LastValidatedStableIds)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} collected {LastCollectedUniqueInstances} unique instances but validated {LastValidatedStableIds} StableIds.");
            }
            int collectedLodInstances = checked(
                LastCollectedHighLodInstances + LastCollectedMediumLodInstances + LastCollectedLowLodInstances);
            if (collectedLodInstances != LastCollectedUniqueInstances)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} collected {LastCollectedUniqueInstances} unique instances but classified {collectedLodInstances} LOD instances.");
            }

            LastPrepareCpuMs = (Stopwatch.GetTimestamp() - _prepareStartTimestamp) * 1000d / Stopwatch.Frequency;
            _frameCollecting = false;
            _frameSealed = true;
        }

        public void DrawMain(Shader instancingShader, in RaylibPbrUniformLocations instancingPbrLocs, RaylibSkyIbl? skyIbl)
        {
            RequireSealedFrame();
            if (_activeGpuSkinnedInstanceBatches.Count > 0)
            {
                RequireDeviceResourcesInitialized();
                long drawStart = Stopwatch.GetTimestamp();
                Gl43.QueryCounter(_drawTimingQueries[0], Gl43.GL_TIMESTAMP);
                for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
                {
                    GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                    if (batch.Count == 0)
                    {
                        continue;
                    }

                    GpuSkinnedMeshSubmission submission = DrawBatch(
                        batch,
                        instancingShader,
                        in instancingPbrLocs,
                        skyIbl);
                    if (submission.DrawCalls > 0)
                    {
                        LastMainInstancesSubmitted = checked(LastMainInstancesSubmitted + batch.Count);
                        LastMainDrawCalls = checked(LastMainDrawCalls + submission.DrawCalls);
                        LastMainTrianglesSubmitted = checked(LastMainTrianglesSubmitted + submission.Triangles);
                    }
                }

                Gl43.QueryCounter(_drawTimingQueries[1], Gl43.GL_TIMESTAMP);
                LastMeshDrawMs += (Stopwatch.GetTimestamp() - drawStart) * 1000d / Stopwatch.Frequency;
            }

        }

        /// <summary>
        /// GPU 姿势求值：行描述（关键帧/逆绑定姿势基址）与实例数据写入 SSBO staging 并上传，
        /// 随后派发 compute 合成骨骼矩阵；CPU 不再逐行求值 UpdateModelAnimationBones。
        /// 实例分组与 GlobalInstanceBase 合同由 PackInstancesByBatch 保证（阴影与主 pass 共用同一寻址）。
        /// </summary>
        private void EvaluatePosesOnGpu()
        {
            if (_ssbo == null)
            {
                // 无 GL 上下文的空帧（托管合同测试路径）：无内容即无事可做；有内容必须 fail-loud
                if (_dirtyPoseRows.Count > 0 || _collectedInstanceCount > 0)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} GPU pose evaluation requires the SSBO pipeline.");
                }

                return;
            }

            RaylibGpuSkinnedSsboPipeline ssbo = _ssbo;
            long buildStart = Stopwatch.GetTimestamp();
            PackInstancesByBatch();

            for (int i = 0; i < _dirtyPoseRows.Count; i++)
            {
                (int poseRow, GpuSkinnedInstanceBatch batch, int clipIndex, int frameIndex) = _dirtyPoseRows[i];
                if (!ssbo.TryGetModelBinding(batch.PoseAssetId, out RaylibGpuSkinnedSsboModelBinding binding))
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} poseAssetId={batch.PoseAssetId} has no registered SSBO model binding.");
                }

                ssbo.WriteRowMeta(poseRow, binding, clipIndex, frameIndex);
            }

            for (int i = 0; i < _collectedInstanceCount; i++)
            {
                ssbo.WriteInstance(i, _packedTransforms[i], _packedPoseRows[i], _packedTints[i]);
            }

            ssbo.DispatchPoseEvaluation(_poseRowByKey.Count, _collectedInstanceCount);
            ssbo.BindForVertexDraw();

            LastUniquePoses = _poseRowByKey.Count;
            LastTextureUploadBytes = ssbo.LastUploadBytes;
            LastTextureUploadCpuMs = 0;
            LastPoseBuildCpuMs = ssbo.LastDispatchCpuMs + (Stopwatch.GetTimestamp() - buildStart) * 1000d / Stopwatch.Frequency;
        }

        private void PackInstancesByBatch()
        {
            int instanceBase = 0;
            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                batch.GlobalInstanceBase = instanceBase;
                batch.WriteCursor = 0;
                instanceBase = checked(instanceBase + batch.Count);
            }

            if (instanceBase != _collectedInstanceCount)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} collected {_collectedInstanceCount} instances but batch counts total {instanceBase}.");
            }

            for (int i = 0; i < _collectedInstanceCount; i++)
            {
                int activeBatchIndex = _collectedBatchIndices[i];
                if ((uint)activeBatchIndex >= (uint)_activeGpuSkinnedInstanceBatches.Count)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} collected invalid active batch index {activeBatchIndex}.");
                }

                GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[activeBatchIndex];
                int packedIndex = batch.GlobalInstanceBase + batch.WriteCursor++;
                _packedTransforms[packedIndex] = _collectedTransforms[i];
                _packedPoseRows[packedIndex] = _collectedPoseRows[i];
                _packedTints[packedIndex] = _collectedTints[i];
            }

            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                if (batch.WriteCursor != batch.Count)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedBatchRenderer)} packed {batch.WriteCursor} of {batch.Count} instances for meshAssetId={batch.Key.MeshAssetId}.");
                }
            }
        }

        public void DrawShadow(RaylibDirectionalShadowMap shadow)
        {
            RequireSealedFrame();
            if (_activeGpuSkinnedInstanceBatches.Count == 0)
            {
                return;
            }

            RequireDeviceResourcesInitialized();

            long shadowStart = Stopwatch.GetTimestamp();
            Gl43.QueryCounter(_drawTimingQueries[2], Gl43.GL_TIMESTAMP);
            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                if (batch.Count == 0 || !batch.CastsShadow)
                {
                    continue;
                }

                GpuSkinnedMeshSubmission submission = DrawBatchShadow(batch, shadow);
                if (submission.DrawCalls > 0)
                {
                    LastShadowInstancesSubmitted = checked(LastShadowInstancesSubmitted + batch.Count);
                    LastShadowDrawCalls = checked(LastShadowDrawCalls + submission.DrawCalls);
                    LastShadowTrianglesSubmitted = checked(LastShadowTrianglesSubmitted + submission.Triangles);
                }
            }

            Gl43.QueryCounter(_drawTimingQueries[3], Gl43.GL_TIMESTAMP);
            LastShadowSubmitCpuMs += (Stopwatch.GetTimestamp() - shadowStart) * 1000d / Stopwatch.Frequency;
        }

        public void EndFrame()
        {
            if (!_frameCollecting && !_frameSealed)
            {
                return;
            }

            _frameCollecting = false;
            _frameSealed = false;
        }

        private bool TryAcquireEntry(
            IRenderMeshAssets meshes,
            int assetId,
            string pass,
            out RaylibGpuSkinnedModelCache.Entry entry,
            out RaylibGpuSkinnedSubmitOutcome outcome)
        {
            entry = default;
            outcome = RaylibGpuSkinnedSubmitOutcome.Unsupported;
            if (!meshes.TryGetDescriptor(assetId, out MeshAssetDescriptor descriptor) ||
                descriptor.Type != MeshAssetType.Model)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} {pass} LOD asset id={assetId} is not a registered Model.");
            }

            RaylibGpuSkinnedModelAcquireOutcome acquire = _modelCache.TryGetOrLoad(
                assetId,
                in descriptor,
                out entry,
                out string? status);
            if (acquire == RaylibGpuSkinnedModelAcquireOutcome.InFlight)
            {
                outcome = RaylibGpuSkinnedSubmitOutcome.InFlight;
                return false;
            }

            if (acquire == RaylibGpuSkinnedModelAcquireOutcome.Failed)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} {pass} LOD asset id={assetId} failed to load: {status}");
            }

            outcome = RaylibGpuSkinnedSubmitOutcome.Submitted;
            return true;
        }

        private bool ResolveCastsShadow(int materialId)
        {
            if (materialId <= 0)
            {
                return true;
            }

            if (!_materials.TryGetResolvedForLane(materialId, out ResolvedMaterialAsset material))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} cannot resolve material id={materialId} during collection.");
            }

            return RaylibMaterialDrawState.CastsShadow(material.BlendMode);
        }

        private static unsafe void ValidateCompatibleModel(
            int logicalAssetId,
            int poseAssetId,
            in RaylibGpuSkinnedModelCache.Entry poseEntry,
            int drawAssetId,
            in RaylibGpuSkinnedModelCache.Entry drawEntry,
            string pass)
        {
            if (poseAssetId == drawAssetId)
            {
                return;
            }

            Model poseModel = poseEntry.Model;
            Model drawModel = drawEntry.Model;
            string prefix =
                $"GPU-skinned logical asset id={logicalAssetId} {pass} LOD asset id={drawAssetId} is incompatible with pose asset id={poseAssetId}";
            if (poseModel.boneCount != drawModel.boneCount ||
                poseModel.bones == null || drawModel.bones == null ||
                poseModel.bindPose == null || drawModel.bindPose == null)
            {
                throw new InvalidOperationException(
                    $"{prefix}: bone contract differs (pose={poseModel.boneCount}, draw={drawModel.boneCount}).");
            }

            if (poseModel.meshCount != drawModel.meshCount)
            {
                throw new InvalidOperationException(
                    $"{prefix}: mesh count differs (pose={poseModel.meshCount}, draw={drawModel.meshCount}).");
            }

            for (int meshIndex = 0; meshIndex < poseModel.meshCount; meshIndex++)
            {
                int poseBones = poseModel.meshes[meshIndex].boneCount;
                int drawBones = drawModel.meshes[meshIndex].boneCount;
                if (poseBones != drawBones)
                {
                    throw new InvalidOperationException(
                        $"{prefix}: mesh[{meshIndex}] bone slot count differs (pose={poseBones}, draw={drawBones}).");
                }
            }

            for (int boneIndex = 0; boneIndex < poseModel.boneCount; boneIndex++)
            {
                BoneInfo* poseBone = poseModel.bones + boneIndex;
                BoneInfo* drawBone = drawModel.bones + boneIndex;
                if (poseBone->parent != drawBone->parent || !BoneNameEquals(poseBone, drawBone))
                {
                    throw new InvalidOperationException(
                        $"{prefix}: bone[{boneIndex}] name or parent differs.");
                }

                Transform pose = poseModel.bindPose[boneIndex];
                Transform draw = drawModel.bindPose[boneIndex];
                if (!pose.translation.Equals(draw.translation) ||
                    !pose.rotation.Equals(draw.rotation) ||
                    !pose.scale.Equals(draw.scale))
                {
                    throw new InvalidOperationException(
                        $"{prefix}: bone[{boneIndex}] bind pose differs.");
                }
            }

            for (int animationIndex = 0; animationIndex < poseEntry.AnimCount; animationIndex++)
            {
                if (!Rl.IsModelAnimationValid(drawModel, poseEntry.Animations[animationIndex]))
                {
                    throw new InvalidOperationException(
                        $"{prefix}: canonical animation[{animationIndex}] is invalid for the draw model.");
                }
            }
        }

        private static unsafe bool BoneNameEquals(BoneInfo* left, BoneInfo* right)
        {
            for (int i = 0; i < 32; i++)
            {
                if (left->name[i] != right->name[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal static GpuSkinnedMeshSubmission CalculateMeshSubmission(
            int instanceCount,
            int triangleCount,
            int maxInstancesPerDraw)
        {
            if (instanceCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instanceCount));
            }
            if (triangleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(triangleCount));
            }
            if (maxInstancesPerDraw <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxInstancesPerDraw));
            }
            if (instanceCount == 0)
            {
                return default;
            }

            int drawCalls = 1 + ((instanceCount - 1) / maxInstancesPerDraw);
            long triangles = checked((long)instanceCount * triangleCount);
            return new GpuSkinnedMeshSubmission(drawCalls, triangles);
        }

        private static void RequireSupportedLod(LODLevel lod)
        {
            if (lod is not LODLevel.High and not LODLevel.Medium and not LODLevel.Low)
            {
                throw new InvalidOperationException($"GPU-skinned submission received unsupported LOD value {(byte)lod}.");
            }
        }

        private void RecordCollectedLod(LODLevel lod)
        {
            switch (lod)
            {
                case LODLevel.High:
                    LastCollectedHighLodInstances++;
                    break;
                case LODLevel.Medium:
                    LastCollectedMediumLodInstances++;
                    break;
                case LODLevel.Low:
                    LastCollectedLowLodInstances++;
                    break;
                default:
                    throw new InvalidOperationException($"GPU-skinned submission received unsupported LOD value {(byte)lod}.");
            }
        }

        private void RequireDeviceResourcesInitialized()
        {
            if (!DeviceResourcesInitialized)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires {nameof(InitializeDeviceResources)} after the Raylib GL context is created and before GPU-skinned frame collection.");
            }
        }

        private void RequireSealedFrame()
        {
            if (!_frameSealed)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} requires SealFrame before drawing.");
            }
        }

        public void Dispose()
        {
            _ssbo?.Dispose();
            _ssbo = null;
            if (_skinningShaderReady)
            {
                RaylibNativeResources.UnloadShader(_skinningShader);
                _skinningShader = default;
                _skinningShaderReady = false;
            }
        }

        private GpuSkinnedMeshSubmission DrawBatch(
            GpuSkinnedInstanceBatch batch,
            Shader instancingShader,
            in RaylibPbrUniformLocations instancingPbrLocs,
            RaylibSkyIbl? skyIbl)
        {
            Model model = batch.MainModel;
            if (model.meshCount <= 0 || batch.Count <= 0)
            {
                return default;
            }

            if (batch.Animations == null || batch.AnimCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinned batch meshAssetId={batch.Key.MeshAssetId} has no animations; silent static draw is forbidden.");
            }

            EnsureFrameLightingApplied();
            int drawCalls = 0;
            long triangles = 0;
            int materialId = batch.Key.MaterialId;
            if (_materials.TryGetResolvedForLane(materialId, out ResolvedMaterialAsset skinnedResolved))
            {
                RaylibMaterialDrawState.RequireLaneShaderKey(in skinnedResolved, materialId, "GpuSkinnedInstance");
            }
            RaylibInstancedMaterialPipeline.RestoreOpaqueModelState();
            int boneBaseMain = 0;
            for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
            {
                Mesh mesh = model.meshes[meshIndex];
                // boneBase 累计必须覆盖全部 mesh（与 EvaluatePosesOnGpu 一致），跳过绘制不跳过累计
                int nextBoneBase = boneBaseMain + mesh.boneCount;
                if (mesh.vertexCount > 0)
                {
                    RaylibInstancedMaterialPipeline.RequireMeshNormals(in mesh, "GpuSkinnedInstance");
                    if (_materials.TryResolveInstancedModelMaterial(model, meshIndex, materialId, instancingShader, in instancingPbrLocs, skyIbl, _frameShadow, out Material material))
                    {                        GpuSkinnedMeshSubmission meshSubmission = CalculateMeshSubmission(
                            batch.Count,
                            mesh.triangleCount,
                            _maxModelInstancesPerDraw);
                        material.shader = _skinningShader;
                        _materials.ApplyHostMaterialMaps(ref material, materialId, _skinningShader, in _skinningPbrLocs);
                        RaylibInstancedMaterialPipeline.BindFrameShadow(ref material, _frameShadow);
                        // Tint is read from the per-instance SSBO.

                        // SSBO 蒙皮：骨骼矩阵由 compute 写入姿势 SSBO，实例数据驻实例 SSBO——零逐实例上传
                        SetPoseStrideUniform();
                        SetBoneBaseUniform(boneBaseMain);
                        fixed (RaylibMatrix* packed = _packedTransforms)
                        {
                            // NVIDIA GL 驱动对该形态 program 按首次调用模式特化（实验锁定）：
                            // 直绘前须有一次 raylib DrawMeshInstanced priming，否则几何涂抹。
                            if (!_mainDirectPrimedThisFrame)
                            {
                                _mainDirectPrimedThisFrame = true;
                                Rl.DrawMeshInstanced(mesh, material, packed + batch.GlobalInstanceBase, 1);
                            }

                            for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                            {
                                int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                                SetInstanceBaseUniform(batch.GlobalInstanceBase + offset);
                                DrawMeshInstancedSsboDirect(mesh, in material, chunkCount);
                            }
                        }

                        drawCalls = checked(drawCalls + meshSubmission.DrawCalls);
                        triangles = checked(triangles + meshSubmission.Triangles);
                    }
                }

                boneBaseMain = nextBoneBase;
            }

            return new GpuSkinnedMeshSubmission(drawCalls, triangles);
        }

        private GpuSkinnedMeshSubmission DrawBatchShadow(
            GpuSkinnedInstanceBatch batch,
            RaylibDirectionalShadowMap shadow)
        {
            Model model = batch.ShadowModel;
            if (model.meshCount <= 0 || batch.Count <= 0)
            {
                return default;
            }

            if (batch.Animations == null || batch.AnimCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinned shadow batch meshAssetId={batch.Key.MeshAssetId} has no animations; silent static shadow is forbidden.");
            }

            if (_ssbo == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinned shadow requires the SSBO pose pipeline; silent uniform shadow is forbidden.");
            }

            int drawCalls = 0;
            long triangles = 0;
            {
                int boneBase = 0;
                for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                {
                    Mesh mesh = model.meshes[meshIndex];
                    int nextBoneBase = boneBase + mesh.boneCount;
                    if (mesh.vertexCount > 0)
                    {
                        GpuSkinnedMeshSubmission meshSubmission = CalculateMeshSubmission(
                            batch.Count,
                            mesh.triangleCount,
                            _maxModelInstancesPerDraw);
                        fixed (RaylibMatrix* packedShadow = _packedTransforms)
                        {
                            for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                            {
                                int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                                shadow.DrawSkinnedMeshSsboShadow(
                                    mesh,
                                    packedShadow + batch.GlobalInstanceBase + offset,
                                    chunkCount,
                                    batch.GlobalInstanceBase + offset,
                                    boneBase,
                                    _ssbo.PoseStride);
                            }
                        }

                        drawCalls = checked(drawCalls + meshSubmission.DrawCalls);
                        triangles = checked(triangles + meshSubmission.Triangles);
                    }

                    boneBase = nextBoneBase;
                }
            }

            return new GpuSkinnedMeshSubmission(drawCalls, triangles);
        }

        private void EnsureFrameLightingApplied()
        {
            if (_frameLighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} lit GpuSkinnedInstance requires ApplyFrameLighting before draw.");
            }

            if (!_hasFrameViewPos)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} lit GpuSkinnedInstance requires camera view position before draw.");
            }

            RequireDeviceResourcesInitialized();
            ApplySkinningFrameLighting();
        }

        private void ApplySkinningFrameLighting()
        {
            _frameLighting!.Apply(_skinningShader, in _skinningLightingLocs);
            _frameLighting.ApplyViewPosition(_skinningShader, in _skinningLightingLocs, _frameViewPos);
            _frameLighting.ApplySkyIrradiance(_skinningShader, _locSkyZenith, _locSkyGround);
            float envSpecular = 1f;
            Rl.SetShaderValue(_skinningShader, _locEnvSpecular, &envSpecular, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            _skinningShadowLocs.ApplyUniforms(_skinningShader, _frameShadow, _frameShadowTexelWorld);
        }

        private void EnsureShaderInitialized()
        {
            if (_skinningShaderReady)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            string vsPath = Path.Combine(baseDir, "skinning_instanced_ssbo.vs");
            string fsPath = Path.Combine(baseDir, "skinning_instanced.fs");
            if (!File.Exists(vsPath) || !File.Exists(fsPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinnedInstance requires skinning_instanced_ssbo.vs/.fs beside the binary (missing under '{baseDir}').");
            }

            _skinningShader = RaylibShaderLoader.Load(baseDir, "skinning_instanced_ssbo.vs", "skinning_instanced.fs", "skinning_instanced");

            _locInstanceBase = Rl.GetShaderLocation(_skinningShader, "uInstanceBase");
            _locBoneBase = Rl.GetShaderLocation(_skinningShader, "uBoneBase");
            _locPoseStride = Rl.GetShaderLocation(_skinningShader, "uPoseStride");
            _locSkinningColDiffuse = Rl.GetShaderLocation(_skinningShader, "colDiffuse");
            _locSkinningRoughness = Rl.GetShaderLocation(_skinningShader, "uRoughness");
            _locSkinningMetallic = Rl.GetShaderLocation(_skinningShader, "uMetallic");
            _locSkinningHasRoughnessMap = Rl.GetShaderLocation(_skinningShader, "uHasRoughnessMap");
            _locSkinningHasMetallicMap = Rl.GetShaderLocation(_skinningShader, "uHasMetallicMap");
            int locMapAlbedo = Rl.GetShaderLocation(_skinningShader, "texture0");
            int locMapMetalness = Rl.GetShaderLocation(_skinningShader, "texture1");
            int locMapRoughness = Rl.GetShaderLocation(_skinningShader, "texture3");
            _locMvp = Rl.GetShaderLocation(_skinningShader, "mvp");
            int locVertexPosition = Rl.GetShaderLocationAttrib(_skinningShader, "vertexPosition");
            int locVertexTexCoord = Rl.GetShaderLocationAttrib(_skinningShader, "vertexTexCoord");
            int locVertexNormal = Rl.GetShaderLocationAttrib(_skinningShader, "vertexNormal");
            int locVertexColor = Rl.GetShaderLocationAttrib(_skinningShader, "vertexColor");
            int locBoneIds = Rl.GetShaderLocationAttrib(_skinningShader, "vertexBoneIds");
            int locBoneWeights = Rl.GetShaderLocationAttrib(_skinningShader, "vertexBoneWeights");
            _locSkyZenith = RaylibShaderBindingGuard.RequireUniform(_skinningShader, "uSkyZenith", "skinning_instanced");
            _locSkyGround = RaylibShaderBindingGuard.RequireUniform(_skinningShader, "uSkyGround", "skinning_instanced");
            _locEnvSpecular = RaylibShaderBindingGuard.RequireUniform(_skinningShader, "uEnvSpecular", "skinning_instanced");
            _skinningLightingLocs = RaylibFrameLightingLocations.ResolveOrThrow(_skinningShader, "skinning_instanced");
            _skinningShadowLocs = RaylibShadowSamplingLocations.ResolveOrThrow(
                _skinningShader,
                "skinning_instanced",
                RaylibShadowSampling.ShaderTextureSlot);
            _skinningPbrLocs = new RaylibPbrUniformLocations(
                _locSkinningRoughness,
                _locSkinningMetallic,
                _locSkinningHasRoughnessMap,
                _locSkinningHasMetallicMap);

            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locVertexNormal;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = locVertexColor;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEIDS] = locBoneIds;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEWEIGHTS] = locBoneWeights;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = _locMvp;
            // SSBO 路径实例变换驻实例表（binding 3），无实例矩阵顶点属性
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] =
                Environment.GetEnvironmentVariable("LUDOTS_SSBO_NO_INST_ATTRIB") == "1" ? -1 : 9; // SSBO 路径无实例矩阵属性；置 9 让 raylib 绘制路径的属性管线合法
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = _locSkinningColDiffuse;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_METALNESS] = locMapMetalness;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_NORMAL] = -1;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ROUGHNESS] = locMapRoughness;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_EMISSION] = _skinningShadowLocs.ShadowMap;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_OCCLUSION] = -1;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_HEIGHT] = -1;
            if (Environment.GetEnvironmentVariable("LUDOTS_SSBO_NO_LOC0_UPLOADS") == "1")
            {
                _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_VIEW] = -1;
                _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_PROJECTION] = -1;
                _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_NORMAL] = -1;
            }

            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_CUBEMAP] =
                RaylibShaderBindingGuard.RequireUniform(_skinningShader, "uPrefilteredEnv", "skinning_instanced");
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_BRDF] =
                RaylibShaderBindingGuard.RequireUniform(_skinningShader, "uBrdfLut", "skinning_instanced");

            // SSBO skinning reads matrices and instance data from shader storage buffers.
            if (_locInstanceBase < 0) throw new InvalidOperationException("Skinning shader uniform 'uInstanceBase' not found.");
            if (_locBoneBase < 0) throw new InvalidOperationException("Skinning shader uniform 'uBoneBase' not found.");
            if (_locPoseStride < 0) throw new InvalidOperationException("Skinning shader uniform 'uPoseStride' not found.");
            if (_locMvp < 0) throw new InvalidOperationException("Skinning shader uniform 'mvp' not found.");
            if (locVertexPosition < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexPosition' not found.");
            if (locVertexNormal < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexNormal' not found.");
            if (locBoneIds < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexBoneIds' not found.");
            if (locBoneWeights < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexBoneWeights' not found.");
            if (_locSkinningColDiffuse < 0) throw new InvalidOperationException("Skinning shader uniform 'colDiffuse' not found.");
            if (locMapAlbedo < 0) throw new InvalidOperationException("Skinning shader uniform 'texture0' not found.");
            if (_locSkinningRoughness < 0) throw new InvalidOperationException("Skinning shader uniform 'uRoughness' not found.");
            if (_locSkinningMetallic < 0) throw new InvalidOperationException("Skinning shader uniform 'uMetallic' not found.");
            if (_locSkinningHasRoughnessMap < 0) throw new InvalidOperationException("Skinning shader uniform 'uHasRoughnessMap' not found.");
            if (_locSkinningHasMetallicMap < 0) throw new InvalidOperationException("Skinning shader uniform 'uHasMetallicMap' not found.");
            if (locMapMetalness < 0) throw new InvalidOperationException("Skinning shader uniform 'texture1' not found.");
            if (locMapRoughness < 0) throw new InvalidOperationException("Skinning shader uniform 'texture3' not found.");

            _materials.ApplyDefaultPbrUniforms(_skinningShader, in _skinningPbrLocs);

            _skinningShaderReady = true;
            _skinningShadowLocs.ApplyUniforms(_skinningShader, _frameShadow, _frameShadowTexelWorld);
            if (_frameLighting != null && _hasFrameViewPos)
            {
                ApplySkinningFrameLighting();
            }
        }

        private unsafe void SetPoseStrideUniform()
        {
            if (_locPoseStride < 0)
            {
                return;
            }

            float stride = _ssbo?.PoseStride ?? 0;
            Rl.SetShaderValue(_skinningShader, _locPoseStride, &stride, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        private unsafe void SetInstanceBaseUniform(int baseValue)
        {
            if (_locInstanceBase < 0)
            {
                return;
            }

            float value = baseValue;
            Rl.SetShaderValue(_skinningShader, _locInstanceBase, &value, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        private unsafe void SetBoneBaseUniform(int boneBase)
        {
            if (_locBoneBase < 0)
            {
                return;
            }

            float value = boneBase;
            Rl.SetShaderValue(_skinningShader, _locBoneBase, &value, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        /// <summary>直接 GL 实例化绘制（零逐实例上传）：实例数据全驻 SSBO，
        /// mvp/纹理槽按 raylib DrawMeshInstanced 内部合同逐位复刻。</summary>
        /// <summary>直接 GL 实例化绘制（零逐实例上传）：实例数据全驻 SSBO，
        /// mvp/纹理槽按 raylib DrawMeshInstanced 内部合同逐位复刻。
        /// NVIDIA GL 对该形态 program 按首次调用模式特化：直绘前须经一次 raylib
        /// DrawMeshInstanced priming（见 DrawBatch），否则几何涂抹——实验锁定，勿删。</summary>
        private unsafe void DrawMeshInstancedSsboDirect(Mesh mesh, in Material material, int count)
        {
            if (mesh.vaoId == 0)
            {
                return;
            }

            Gl43.UseProgram(material.shader.id);
            Rl.SetShaderValueMatrix(_skinningShader, _locMvp, RaylibNativeResources.ComputeDrawMvp());
            BindMaterialTextureSlotsDirect(in material);
            Gl43.BindVertexArray(mesh.vaoId);
            int indexCount = mesh.indices != null ? checked(mesh.triangleCount * 3) : mesh.vertexCount;
            Gl43.DrawElementsInstanced(Gl43.GL_TRIANGLES, indexCount, Gl43.GL_UNSIGNED_SHORT, IntPtr.Zero, count);
            Gl43.UseProgram(0);
        }

        /// <summary>诊断/兼容：在当前 VAO 上按 raylib DrawMeshInstanced 的实例属性布局
        /// （location 9..12 组成 mat4、divisor 1）挂一个恒等矩阵假缓冲——着色器不消费该属性，
        /// 仅复刻其 GL 状态，用于隔离“实例属性状态是否承载绘制正确性”。</summary>

        /// <summary>诊断：按 raylib DrawMeshInstanced 的实例属性布局挂真实变换数据（着色器不消费该属性）。</summary>

        /// <summary>复刻 raylib DrawMeshInstanced 的收尾：删除实例 VBO（VAO 仍引用其属性）。
        /// 该 create→attach→draw→delete 周期是直绘正确性的承载件（NVIDIA GL 驱动交互，实验锁定）。</summary>

        private static void BindMaterialTextureSlotsDirect(in Material material)
        {
            const int MaxMaterialMaps = 16;
            for (int slot = 0; slot < MaxMaterialMaps; slot++)
            {
                Texture2D texture = material.maps[slot].texture;
                if (texture.id == 0)
                {
                    continue;
                }

                int loc = material.shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO + slot];
                if (loc < 0)
                {
                    continue;
                }

                bool cubemap = slot == (int)Rl.MaterialMapIndex.MATERIAL_MAP_IRRADIANCE
                            || slot == (int)Rl.MaterialMapIndex.MATERIAL_MAP_PREFILTER
                            || slot == (int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP;
                Gl43.ActiveTexture(Gl43.GL_TEXTURE0 + (uint)slot);
                Gl43.BindTexture(cubemap ? Gl43.GL_TEXTURE_CUBE_MAP : Gl43.GL_TEXTURE_2D, texture.id);
                Gl43.Uniform1i(loc, slot);
            }
        }

        private readonly record struct GpuSkinnedInstanceBatchKey(            int MeshAssetId,
            int MaterialId,
            LODLevel Lod,
            int AnimationProfileId,
            int ClipIndex,
            int PoseFrame);

        private readonly record struct GpuSkinnedBatchFamilyKey(
            int MeshAssetId,
            int MaterialId,
            LODLevel Lod,
            int AnimationProfileId);

        internal readonly record struct GpuSkinnedMeshSubmission(int DrawCalls, long Triangles);

        internal sealed class StableIdFrameIndex
        {
            private readonly int[] _keys;
            private readonly long[] _stamps;
            private readonly int _mask;
            private readonly int _maxEntries;
            private long _generation;

            public StableIdFrameIndex(int maxEntries)
            {
                if (maxEntries <= 0 || maxEntries > (1 << 29))
                {
                    throw new ArgumentOutOfRangeException(nameof(maxEntries));
                }

                int tableSize = 1;
                int required = checked(maxEntries * 2);
                while (tableSize < required)
                {
                    tableSize <<= 1;
                }

                _keys = new int[tableSize];
                _stamps = new long[tableSize];
                _mask = tableSize - 1;
                _maxEntries = maxEntries;
            }

            public int Count { get; private set; }

            public void BeginFrame()
            {
                _generation = checked(_generation + 1);
                Count = 0;
            }

            public void Add(int stableId)
            {
                if (stableId <= 0)
                {
                    throw new InvalidOperationException(
                        $"GPU-skinned submission requires a positive StableId; received {stableId}.");
                }

                int slot = (int)(unchecked((uint)stableId * 2654435761u) & (uint)_mask);
                for (int probe = 0; probe < _keys.Length; probe++)
                {
                    if (_stamps[slot] != _generation)
                    {
                        if (Count >= _maxEntries)
                        {
                            throw new InvalidOperationException(
                                $"GPU-skinned StableId index requires another entry; configured maxEntries={_maxEntries}.");
                        }

                        _stamps[slot] = _generation;
                        _keys[slot] = stableId;
                        Count++;
                        return;
                    }

                    if (_keys[slot] == stableId)
                    {
                        throw new InvalidOperationException(
                            $"GPU-skinned StableId={stableId} was submitted more than once in the same frame.");
                    }

                    slot = (slot + 1) & _mask;
                }

                throw new InvalidOperationException(
                    $"GPU-skinned StableId index exhausted its fixed table of {_keys.Length} slots.");
            }
        }

        /// <summary>
        /// 同一 (mesh, material, LOD, profile) 的资产绑定：pose/main/shadow 三套模型入口、
        /// 规范动画数组与状态映射，一次注册跨帧复用；批次只补 (clip, poseFrame) 维度。
        /// </summary>
        private sealed class GpuSkinnedBatchFamily
        {
            public GpuSkinnedBatchFamilyKey Key;
            public int PoseAssetId;
            public Model PoseModel;
            public Model MainModel;
            public Model ShadowModel;
            public ModelAnimation* Animations;
            public int AnimCount;
            public IReadOnlyDictionary<int, int>? StateToClipMap;
            public bool CastsShadow;
            public bool IsBound;

            public void Bind(
                GpuSkinnedBatchFamilyKey key,
                int poseAssetId,
                in RaylibGpuSkinnedModelCache.Entry poseEntry,
                in RaylibGpuSkinnedModelCache.Entry mainEntry,
                in RaylibGpuSkinnedModelCache.Entry shadowEntry,
                IReadOnlyDictionary<int, int>? stateToClipMap,
                bool castsShadow)
            {
                if (IsBound)
                {
                    throw new InvalidOperationException(
                        $"{nameof(GpuSkinnedBatchFamily)} slot is already bound to meshAssetId={Key.MeshAssetId}.");
                }

                Key = key;
                PoseAssetId = poseAssetId;
                PoseModel = poseEntry.Model;
                MainModel = mainEntry.Model;
                ShadowModel = shadowEntry.Model;
                Animations = poseEntry.Animations;
                AnimCount = poseEntry.AnimCount;
                StateToClipMap = stateToClipMap;
                CastsShadow = castsShadow;
                IsBound = true;
            }
        }

        private sealed class GpuSkinnedInstanceBatch
        {
            public GpuSkinnedInstanceBatchKey Key;
            public GpuSkinnedBatchFamily Family = null!;
            public int ClipIndex;
            public int PoseFrame;
            public bool IsBound;
            public int Count;
            public int ActiveIndex = -1;
            public int GlobalInstanceBase;
            public int WriteCursor;

            public int PoseAssetId => Family.PoseAssetId;
            public Model PoseModel => Family.PoseModel;
            public Model MainModel => Family.MainModel;
            public Model ShadowModel => Family.ShadowModel;
            public ModelAnimation* Animations => Family.Animations;
            public int AnimCount => Family.AnimCount;
            public IReadOnlyDictionary<int, int>? StateToClipMap => Family.StateToClipMap;
            public bool CastsShadow => Family.CastsShadow;

            public void Bind(
                GpuSkinnedInstanceBatchKey key,
                GpuSkinnedBatchFamily family,
                int clipIndex,
                int poseFrame)
            {
                if (IsBound)
                {
                    throw new InvalidOperationException(
                        $"{nameof(GpuSkinnedInstanceBatch)} slot is already bound to meshAssetId={Key.MeshAssetId}.");
                }

                Key = key;
                Family = family;
                ClipIndex = clipIndex;
                PoseFrame = poseFrame;
                IsBound = true;
            }
        }
    }
}
