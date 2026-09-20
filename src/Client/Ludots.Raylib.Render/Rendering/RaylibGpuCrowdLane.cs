using System;
using System.Diagnostics;
using System.Numerics;
using Ludots.Platform.Abstractions;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render;

/// <summary>
/// GPU 人群车道配置：LOD 家族 + 动画剪辑映射 + 分级/阴影/imposter 参数。
/// ClipSlotMap 把 AnimatorPackedState.PrimaryStateIndex（presenter 动画状态槽）映射到
/// 高模 ModelAnimation 索引——车道按 剪辑 × 相位桶 建姿势行，GPU 侧共享预蒙皮。
/// </summary>
public sealed record RaylibGpuCrowdLaneConfig(
    int FamilyMeshAssetId,
    string HighModelSource,
    string MediumModelSource,
    string LowModelSource,
    int[] ClipSlotMap,
    int MaxInstances,
    float LodMediumDist = 20f,
    float LodLowDist = 60f,
    float LodImposterDist = 120f,
    float InstanceRadius = 2.5f,
    float ShadowCasterRadius = 240f,
    int PhaseBucketsPerClip = 8,
    int PoseStride = 256,
    float ClipPlaybackFps = 60f,
    float Roughness = 0.55f,
    float Metallic = 0.10f,
    float ShadowTexelWorld = 0.35f,
    bool StaticBatch = false,
    bool GpuWander = false)
{
    public int PoseRowCount => ClipSlotMap.Length * PhaseBucketsPerClip;
}

/// <summary>
/// GPU 人群车道的正式消费链路：消费 presenter 体系 emit 的蒙皮批次快照
/// （ISkinnedVisualBatchSnapshot——游戏宿主由 Core 的 SkinnedVisualBatchBuffer 注入，
/// 画廊场景由 GallerySkinnedBatch 注入），打包进 GPU 实例表后走完整 GPU 管线：
/// 姿势 compute → 预蒙皮烘焙 → imposter 图集重烘 → 投射体剔除 → 视锥/LOD 剔除
/// → indirect draw（主 4 + 阴影 1）。CPU 零逐实例 GL 调用。
/// 条目合同：RenderPath 必须是 GpuSkinnedInstance 且 MeshAssetId 必须是本 LOD 家族
/// （fail-closed——车道不做静默降级）；Animator 非 Active 的条目跳过。
/// </summary>
public sealed unsafe class RaylibGpuCrowdLane : IDisposable
{
    private readonly RaylibGpuCrowdLaneConfig _config;
    private readonly float[] _instancePacking;
    private RaylibGpuSkinnedSsboPipeline _posePipeline = null!;
    private GpuCrowdIndirectRenderer _crowd = null!;
    private GpuCrowdPbrUniforms _uniforms = new();
    private Shader _skinShader;
    private Model _poseModel;
    private ModelAnimation* _animations;
    private int _animationCount;
    private readonly int[] _clipFrameCounts;
    private bool _staticPacked;
    private int _activeCount;
    private bool _disposed;

    public double LastPackMs { get; private set; }
    public double LastPoseMs { get; private set; }
    public int LastPreparedCount { get; private set; }
    public long LastVisible => _crowd?.LastVisibleTotal ?? 0;
    public int LastHighCount => _crowd?.LastHighCount ?? 0;
    public int LastMediumCount => _crowd?.LastMediumCount ?? 0;
    public int LastLowCount => _crowd?.LastLowCount ?? 0;
    public int LastImposterCount => _crowd?.LastImposterCount ?? 0;
    public int LastShadowCasterCount => _crowd?.LastShadowCasterCount ?? 0;
    public int LastDrawCalls => _crowd?.LastDrawCalls ?? 0;

    public RaylibGpuCrowdLane(RaylibGpuCrowdLaneConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (config.ClipSlotMap.Length == 0)
        {
            throw new ArgumentException("ClipSlotMap 至少要有一个剪辑槽。", nameof(config));
        }

        _instancePacking = new float[config.MaxInstances * 16];
        _clipFrameCounts = new int[config.ClipSlotMap.Length];
    }

    /// <summary>初始化。StaticBatch 模式必须在此提供初始批次（打包一次后实例表按 STATIC 驻 GPU，
    /// 此后 PrepareFrame 只推进姿势）；动态模式逐帧经 PrepareFrame 整表重传。</summary>
    public void Initialize(ISkinnedVisualBatchSnapshot? initialBatch = null)
    {
        if (_config.StaticBatch && initialBatch == null)
        {
            throw new ArgumentException("StaticBatch 模式必须在 Initialize 提供初始批次。", nameof(initialBatch));
        }

        (Mesh highMesh, uint highAlbedo) = LoadModelMeshWithAlbedo(_config.HighModelSource);
        (Mesh mediumMesh, uint mediumAlbedo) = LoadModelMeshWithAlbedo(_config.MediumModelSource);
        (Mesh lowMesh, uint lowAlbedo) = LoadModelMeshWithAlbedo(_config.LowModelSource);
        Mesh highWelded = GpuCrowdMeshFactory.WeldToIndexed(highMesh);
        Mesh mediumWelded = GpuCrowdMeshFactory.WeldToIndexed(mediumMesh);
        Mesh lowWelded = GpuCrowdMeshFactory.WeldToIndexed(lowMesh);

        if (initialBatch != null)
        {
            _activeCount = PackBatch(initialBatch);
            LastPreparedCount = _activeCount;
            _staticPacked = _config.StaticBatch;
        }

        _crowd = new GpuCrowdIndirectRenderer(_config.MaxInstances, _config.PoseRowCount, _config.PoseStride);
        ReadOnlySpan<float> initialInstances = _activeCount > 0
            ? _instancePacking.AsSpan(0, _activeCount * 16)
            : _instancePacking;
        _crowd.Initialize(
            highWelded, mediumWelded, lowWelded, initialInstances,
            _config.LodMediumDist, _config.LodLowDist, _config.LodImposterDist, _config.InstanceRadius,
            highAlbedo > 1 ? highAlbedo : 0u,
            mediumAlbedo > 1 ? mediumAlbedo : 0u,
            lowWelded.texcoords != null && lowAlbedo > 1 ? lowAlbedo : 0u,
            dynamicInstances: !_config.StaticBatch,
            gpuWander: _config.GpuWander);

        _poseModel = RaylibNativeResources.LoadModel(_config.HighModelSource);
        _animations = Rl.LoadModelAnimations(_config.HighModelSource, out _animationCount);
        for (int slot = 0; slot < _config.ClipSlotMap.Length; slot++)
        {
            int modelAnim = _config.ClipSlotMap[slot];
            if ((uint)modelAnim >= (uint)_animationCount)
            {
                throw new ArgumentOutOfRangeException(nameof(_config), $"ClipSlotMap[{slot}]={modelAnim} 超出模型动画数 {_animationCount}。");
            }

            _clipFrameCounts[slot] = _animations[modelAnim].frameCount;
        }

        _posePipeline = new RaylibGpuSkinnedSsboPipeline(
            maxPoseRows: _config.PoseRowCount,
            poseStride: _config.PoseStride,
            maxInstances: 1);
        _posePipeline.RegisterModel(_config.FamilyMeshAssetId, _poseModel, _animations, _animationCount);

        string shaderDir = AppContext.BaseDirectory;
        _skinShader = RaylibShaderLoader.Load(shaderDir, "gpu_crowd_preskin.vs", "gpu_crowd_pbr.fs", "gpu_crowd_pbr_lane");
        _uniforms = new GpuCrowdPbrUniforms();
        _uniforms.Resolve(_skinShader, _crowd.ImposterShader);
        _uniforms.ApplyStatic(
            _skinShader, _crowd.ImposterShader,
            _crowd.ImposterSize.X, _crowd.ImposterSize.Y, 1f, _config.ShadowTexelWorld);
    }

    /// <summary>每帧第一拍：批次条目 → 实例表打包上传 + 姿势行推进 + 预蒙皮 + imposter 图集重烘。
    /// StaticBatch 配置下首帧打包一次，此后仅推进姿势（发射条目内容不再读）。</summary>
    public void PrepareFrame(ISkinnedVisualBatchSnapshot batch, double timeSeconds)
    {
        if (!_staticPacked)
        {
            long packStart = Stopwatch.GetTimestamp();
            int count = PackBatch(batch);
            _crowd.UploadInstances(_instancePacking.AsSpan(0, count * 16));
            _activeCount = count;
            LastPreparedCount = count;
            LastPackMs = (Stopwatch.GetTimestamp() - packStart) * 1000d / Stopwatch.Frequency;
            _staticPacked = true;
        }

        long poseStart = Stopwatch.GetTimestamp();
        AdvancePoseRows(timeSeconds);
        _posePipeline.DispatchPoseEvaluation(_config.PoseRowCount, 0);
        _crowd.DispatchPreskin(_posePipeline.PoseMatrixBuffer);
        _crowd.CaptureImposterAtlas();
        LastPoseMs = (Stopwatch.GetTimestamp() - poseStart) * 1000d / Stopwatch.Frequency;
    }

    /// <summary>阴影拍（须在 shadowMap.BeginFrame 与 EndFrame 之间）：投射体 GPU 剔除 + indirect 深度绘制。</summary>
    public void DrawShadowCasters(Vector3 cameraPosition, double timeSeconds)
    {
        _crowd.CullShadowCasters(cameraPosition, _config.ShadowCasterRadius, _activeCount, (float)timeSeconds);
        _crowd.DrawShadowIndirect((float)timeSeconds);
    }

    /// <summary>主拍：IBL/阴影纹理绑定 + PBR uniform + GPU 剔除 + indirect draw。须在 BeginMode3D 内调用。</summary>
    public void DrawMain(
        in Camera3D camera,
        RaylibFrameLighting lighting,
        RaylibDirectionalShadowMap shadowMap,
        RaylibSkyIbl skyIbl,
        double timeSeconds)
    {
        skyIbl.Ensure(lighting);
        _crowd.BindIbl(skyIbl.EnvCubemap.id, skyIbl.BrdfLut.id);
        _crowd.BindShadowTexture(shadowMap.DepthTexture.id, GpuCrowdIndirectRenderer.ShadowTextureUnit);
        _uniforms.ApplyFrame(_skinShader, _crowd.ImposterShader, lighting, shadowMap, camera, (float)timeSeconds);
        RaylibMatrix viewProj = RaylibNativeResources.ComputeDrawMvp();
        _crowd.CullAndDraw(viewProj, camera.position, _activeCount, _skinShader.id, Rl.GetShaderLocation(_skinShader, "mvp"), (float)timeSeconds);
    }

    private int PackBatch(ISkinnedVisualBatchSnapshot batch)
    {
        ReadOnlySpan<SkinnedVisualBatchItem> items = batch.GetSpan();
        int cursor = 0;
        for (int i = 0; i < items.Length; i++)
        {
            ref readonly SkinnedVisualBatchItem item = ref items[i];
            if (item.Payload.RenderPath != VisualRenderPath.GpuSkinnedInstance)
            {
                continue;
            }

            if (item.MeshAssetId != _config.FamilyMeshAssetId)
            {
                throw new InvalidOperationException(
                    $"GPU 人群车道收到非本家族条目 MeshAssetId={item.MeshAssetId}（期望 {_config.FamilyMeshAssetId}）——车道不做静默降级。");
            }

            AnimatorPackedState animator = item.Payload.Animator;
            if ((animator.GetFlags() & AnimatorPackedStateFlags.Active) == 0)
            {
                continue;
            }

            if (cursor >= _config.MaxInstances)
            {
                throw new InvalidOperationException($"批次条目超过车道容量 {_config.MaxInstances}。");
            }

            int clipSlot = animator.GetPrimaryStateIndex();
            if ((uint)clipSlot >= (uint)_config.ClipSlotMap.Length)
            {
                throw new InvalidOperationException($"动画状态槽 {clipSlot} 未在 ClipSlotMap 配置。");
            }

            // S·R·T → GL 三列（与 FromSystemNumerics 列约定一致）
            Matrix4x4 m = Matrix4x4.CreateScale(item.Payload.Scale)
                * Matrix4x4.CreateFromQuaternion(item.Payload.Rotation)
                * Matrix4x4.CreateTranslation(item.Payload.Position);
            int dst = cursor * 16;
            _instancePacking[dst + 0] = m.M11; _instancePacking[dst + 1] = m.M21; _instancePacking[dst + 2] = m.M31; _instancePacking[dst + 3] = m.M41;
            _instancePacking[dst + 4] = m.M12; _instancePacking[dst + 5] = m.M22; _instancePacking[dst + 6] = m.M32; _instancePacking[dst + 7] = m.M42;
            _instancePacking[dst + 8] = m.M13; _instancePacking[dst + 9] = m.M23; _instancePacking[dst + 10] = m.M33; _instancePacking[dst + 11] = m.M43;

            int phaseBucket = Math.Clamp((int)(animator.GetNormalizedTime01() * _config.PhaseBucketsPerClip), 0, _config.PhaseBucketsPerClip - 1);
            _instancePacking[dst + 12] = clipSlot * _config.PhaseBucketsPerClip + phaseBucket;
            Vector4 color = item.Payload.Color;
            int r = (int)MathF.Round(Math.Clamp(color.X, 0, 1) * 255);
            int g = (int)MathF.Round(Math.Clamp(color.Y, 0, 1) * 255);
            int b = (int)MathF.Round(Math.Clamp(color.Z, 0, 1) * 255);
            _instancePacking[dst + 13] = r | (g << 8) | (b << 16);
            _instancePacking[dst + 14] = 1f;
            _instancePacking[dst + 15] = cursor;
            cursor++;
        }

        return cursor;
    }

    private void AdvancePoseRows(double timeSeconds)
    {
        if (!_posePipeline.TryGetModelBinding(_config.FamilyMeshAssetId, out var binding))
        {
            throw new InvalidOperationException("姿势管线模型绑定缺失（RegisterModel 未生效）。");
        }

        int playFrame = (int)(timeSeconds * _config.ClipPlaybackFps);
        for (int slot = 0; slot < _config.ClipSlotMap.Length; slot++)
        {
            int frames = _clipFrameCounts[slot];
            for (int bucket = 0; bucket < _config.PhaseBucketsPerClip; bucket++)
            {
                int phaseFrame = (int)(((bucket + 0.5f) / _config.PhaseBucketsPerClip) * frames);
                int frame = (playFrame + phaseFrame) % frames;
                _posePipeline.WriteRowMeta(slot * _config.PhaseBucketsPerClip + bucket, binding, _config.ClipSlotMap[slot], frame);
            }
        }
    }

    private static (Mesh Mesh, uint AlbedoTexture) LoadModelMeshWithAlbedo(string path)
    {
        Model model = RaylibNativeResources.LoadModel(path);
        if (model.meshCount <= 0 || model.meshes == null || model.materialCount <= 0)
        {
            throw new InvalidOperationException($"GPU 人群车道无法加载模型 '{path}'。");
        }

        uint albedo = model.materials[0].maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO].texture.id;
        return (model.meshes[0], albedo);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _crowd?.Dispose();
        _posePipeline?.Dispose();
        if (_skinShader.id != 0) { RaylibNativeResources.UnloadShader(_skinShader); _skinShader = default; }
        if (_animations != null) { Rl.UnloadModelAnimations(_animations, _animationCount); _animations = null; }
        if (_poseModel.meshes != null) { RaylibNativeResources.UnloadModel(_poseModel); _poseModel = default; }
        _disposed = true;
    }
}
