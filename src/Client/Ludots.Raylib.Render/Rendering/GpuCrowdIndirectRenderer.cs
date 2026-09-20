using System;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using static Ludots.Raylib.Render.Gl43;

namespace Ludots.Raylib.Render;

/// <summary>某一级 LOD 的绘制资源（mesh + SSBO 绑定 + indirect 命令偏移 + 预蒙皮缓冲）。</summary>
public sealed class GpuCrowdLodSlot
{
    public required Mesh Mesh { get; init; }
    public required uint CompactBuffer { get; init; }
    public required uint MeshDataBuffer { get; init; }
    public required uint SkinnedBuffer { get; init; }
    public int IndexCount { get; init; }
    public int IndirectOffsetBytes { get; init; }
    public int LastDrawnInstances;
}

/// <summary>
/// GPU 剔除 + 四级 LOD（high/medium/low/imposter）+ indirect draw + compute 预蒙皮的纯渲染人群渲染器（UE VAT 同思路）。
/// 每帧 GPU 流水线：姿势 compute → 预蒙皮 compute（骨骼矩阵烘焙进顶点）→ imposter 图集重烘（16 相位 × 8 视角）
/// → 阴影投射体剔除 → 主视锥剔除 → 5 条 indirect draw（主 4 + 阴影 1）。CPU 每帧零逐实例工作。
/// 顶点着色器只取 2×vec4 预蒙皮顶点 + 实例变换；远景实例退化为 PBR billboard（图集法线旋回世界）。
/// 可见计数经 GPU 侧 CopyBufferSubData 到环形 staging、按固定帧延迟 GetBufferSubData 读回（零停顿）。
/// </summary>
public sealed unsafe class GpuCrowdIndirectRenderer : IDisposable
{
    private const int ReadbackRingSize = 8;
    private const int ReadbackLagFrames = 6;
    private const int ShadowCommandOffsetBytes = 60;
    private const int ImposterCommandOffsetBytes = 80;
    private const int ImposterCellPx = 128;
    private const int ImposterViewDirs = 8;
    public const int AlbedoTextureUnit = 1;
    public const int ImposterAtlasUnit = 4;
    public const int EnvCubemapUnit = 5;
    public const int BrdfLutUnit = 6;
    public const int ShadowTextureUnit = 7;

    private uint _sourceBuffer;
    private uint _indirectBuffer;
    private uint _shadowCompactBuffer;
    private uint _imposterCompactBuffer;
    private uint _cullProgram;
    private uint _preskinProgram;
    private Shader _shadowDepthShader;
    private Material _shadowDepthPrimingMaterial;
    private int _shadowDepthPrimedFrame = -1;
    private Shader _imposterShader;
    private Shader _imposterCaptureShader;
    private RenderTexture2D _imposterAtlas;
    private Mesh _imposterQuad;
    private readonly uint[] _lodAlbedoTextures = new uint[3];
    private readonly GpuCrowdLodSlot[] _lodSlots = new GpuCrowdLodSlot[3];
    private readonly uint[] _countStaging = new uint[ReadbackRingSize];
    private readonly int _maxInstances;
    private readonly int _poseRows;
    private readonly int _poseStride;
    private int _locFrustum;
    private int _locCameraPos;
    private int _locLodMedium;
    private int _locLodLow;
    private int _locRadius;
    private int _locTotal;
    private int _locShadowPass;
    private int _locShadowCenter;
    private int _locShadowRadius;
    private int _locCullTime;
    private int _locCullWanderScale;
    private int _locShadowDepthWanderScale = int.MinValue;
    private int _locMainWanderScale = int.MinValue;
    private int _locImposterWanderScale;
    private float _gpuWanderScale = 1f;
    private bool _dynamicInstances;
    private int _locPreskinVertexCount;
    private int _locPreskinRows;
    private int _locPreskinStride;
    private int _locShadowDepthMvp;
    private int _locShadowDepthVertexCount;
    private int _locMainPreskinVertexCount = int.MinValue;
    private int _locMainAlbedoMap = int.MinValue;
    private int _locMainHasAlbedoMap = int.MinValue;
    private int _locLodImposter;
    private int _locImposterMvp;
    private int _locImposterAtlas;
    private int _locCaptureMvp;
    private int _locCaptureVertexCount;
    private int _locCapturePhaseRow;
    private float _imposterHalfWidth;
    private float _imposterHeight;
    private bool _disposed;
    private int _frameCounter;

    public int LastDrawCalls { get; private set; }
    public int LastHighCount => _lodSlots[0]?.LastDrawnInstances ?? 0;
    public int LastMediumCount => _lodSlots[1]?.LastDrawnInstances ?? 0;
    public int LastLowCount => _lodSlots[2]?.LastDrawnInstances ?? 0;
    public int LastShadowCasterCount { get; private set; }
    public int LastImposterCount { get; private set; }
    public long LastVisibleTotal => (long)LastHighCount + LastMediumCount + LastLowCount + LastImposterCount;

    public uint IndirectBuffer => _indirectBuffer;
    public int ShadowCommandOffset => ShadowCommandOffsetBytes;

    /// <summary>imposter billboard 着色器（PBR uniform 合同由场景逐帧设置）。</summary>
    public Shader ImposterShader => _imposterShader;

    /// <summary>imposter 图集（RGB 法线 + A 覆盖率），调试/验收取证用。</summary>
    public Texture2D ImposterAtlasTexture => _imposterAtlas.texture;

    public GpuCrowdIndirectRenderer(int maxInstances, int poseRows, int poseStride)
    {
        if (maxInstances <= 0) throw new ArgumentOutOfRangeException(nameof(maxInstances));
        if (poseRows <= 0) throw new ArgumentOutOfRangeException(nameof(poseRows));
        if (poseStride <= 0) throw new ArgumentOutOfRangeException(nameof(poseStride));
        _maxInstances = maxInstances;
        _poseRows = poseRows;
        _poseStride = poseStride;
    }

    /// <summary>初始化 GPU 资源。meshes = [high, medium, low]，全部须带索引缓冲与骨骼属性（非索引网格先焊接索引化）；
    /// albedo 纹理 = 各 LOD 的 GLB albedo（无则 0，焊接低模无 UV 必然 0）。</summary>
    public void Initialize(
        Mesh highMesh,
        Mesh mediumMesh,
        Mesh lowMesh,
        ReadOnlySpan<float> instanceData,
        float lodMediumDist,
        float lodLowDist,
        float lodImposterDist,
        float instanceRadius,
        uint albedoHigh,
        uint albedoMedium,
        uint albedoLow,
        bool dynamicInstances = false,
        bool gpuWander = true)
    {
        Gl43.Initialize();
        _dynamicInstances = dynamicInstances;
        _gpuWanderScale = gpuWander ? 1f : 0f;

        _sourceBuffer = CreateBuffer((nint)((long)_maxInstances * 64), dynamicInstances ? GL_DYNAMIC_DRAW : GL_STATIC_DRAW);
        fixed (float* data = instanceData)
        {
            Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, _sourceBuffer);
            Gl43.BufferData(GL_SHADER_STORAGE_BUFFER, (nint)(instanceData.Length * 4), (IntPtr)data, GL_STATIC_DRAW);
        }

        // Indirect 命令缓冲：5 × (count, instanceCount, firstIndex, baseVertex, baseInstance) = 5 × 20B
        // 主 pass 3 条 + 阴影 1 条（最粗 LOD）+ imposter 1 条（quad）；instanceCount 每帧由 cull shader 原子重建
        _indirectBuffer = CreateBuffer(100, GL_STREAM_DRAW);
        Span<uint> cmd = stackalloc uint[5];
        Gl43.BindBuffer(GL_DRAW_INDIRECT_BUFFER, _indirectBuffer);
        for (int lod = 0; lod < 3; lod++)
        {
            Mesh mesh = lod switch { 0 => highMesh, 1 => mediumMesh, _ => lowMesh };
            if (mesh.indices == null || mesh.triangleCount <= 0 || mesh.boneIds == null || mesh.boneWeights == null)
            {
                throw new InvalidOperationException($"GPU crowd LOD {lod} mesh 必须带索引缓冲与骨骼属性。");
            }

            cmd[0] = (uint)checked(mesh.triangleCount * 3);
            cmd[1] = 0u;
            cmd[2] = 0u;
            cmd[3] = 0u;
            cmd[4] = 0u;
            fixed (uint* p = cmd)
            {
                Gl43.BufferSubData(GL_DRAW_INDIRECT_BUFFER, (nint)(lod * 20), 20, (IntPtr)p);
            }

            _lodAlbedoTextures[lod] = lod switch { 0 => albedoHigh, 1 => albedoMedium, _ => albedoLow };
            _lodSlots[lod] = new GpuCrowdLodSlot
            {
                Mesh = mesh,
                CompactBuffer = CreateBuffer((nint)((long)_maxInstances * 64), GL_STREAM_DRAW),
                MeshDataBuffer = UploadMeshData(mesh),
                SkinnedBuffer = CreateBuffer((nint)((long)mesh.vertexCount * _poseRows * 32), GL_STREAM_COPY),
                IndexCount = (int)cmd[0],
                IndirectOffsetBytes = lod * 20,
            };
        }

        // imposter billboard 包围尺寸（低模 bind 姿势扫描，capture 正交与运行时 quad 共用）
        ComputeImposterBounds(lowMesh);

        cmd[0] = (uint)_lodSlots[2].IndexCount;
        cmd[1] = 0u;
        fixed (uint* p = cmd)
        {
            Gl43.BufferSubData(GL_DRAW_INDIRECT_BUFFER, ShadowCommandOffsetBytes, 20, (IntPtr)p);
        }

        // imposter 命令：4 顶点 quad（6 索引）
        _imposterQuad = BuildImposterQuad();
        cmd[0] = 6;
        cmd[1] = 0u;
        fixed (uint* p = cmd)
        {
            Gl43.BufferSubData(GL_DRAW_INDIRECT_BUFFER, ImposterCommandOffsetBytes, 20, (IntPtr)p);
        }

        _shadowCompactBuffer = CreateBuffer((nint)((long)_maxInstances * 64), GL_STREAM_DRAW);
        _imposterCompactBuffer = CreateBuffer((nint)((long)_maxInstances * 64), GL_STREAM_DRAW);
        for (int i = 0; i < ReadbackRingSize; i++)
        {
            _countStaging[i] = CreateBuffer(20, GL_STREAM_READ);
        }

        // imposter 图集：16 相位列 × 8 视角行，cell 128²（RGB 法线 + A 覆盖率）
        _imposterAtlas = RaylibNativeResources.LoadRenderTexture(_poseRows * ImposterCellPx, ImposterViewDirs * ImposterCellPx);

        _cullProgram = CompileComputeShader("gpu_cull.comp");
        _locFrustum = Gl43.GetUniformLocation(_cullProgram, "uFrustumPlanes");
        _locCameraPos = Gl43.GetUniformLocation(_cullProgram, "uCameraPos");
        _locLodMedium = Gl43.GetUniformLocation(_cullProgram, "uLodMediumDist");
        _locLodLow = Gl43.GetUniformLocation(_cullProgram, "uLodLowDist");
        _locLodImposter = Gl43.GetUniformLocation(_cullProgram, "uLodImposterDist");
        _locRadius = Gl43.GetUniformLocation(_cullProgram, "uInstanceRadius");
        _locTotal = Gl43.GetUniformLocation(_cullProgram, "uTotalInstances");
        _locShadowPass = Gl43.GetUniformLocation(_cullProgram, "uShadowPass");
        _locShadowCenter = Gl43.GetUniformLocation(_cullProgram, "uShadowCenter");
        _locShadowRadius = Gl43.GetUniformLocation(_cullProgram, "uShadowRadius");
        _locCullTime = Gl43.GetUniformLocation(_cullProgram, "uTime");
        _locCullWanderScale = Gl43.GetUniformLocation(_cullProgram, "uWanderScale");
        if (_locFrustum < 0 || _locCameraPos < 0 || _locTotal < 0 || _locShadowPass < 0 || _locShadowCenter < 0 || _locShadowRadius < 0 || _locLodImposter < 0 || _locCullTime < 0 || _locCullWanderScale < 0)
        {
            throw new InvalidOperationException("gpu_cull.comp 缺少必需 uniform。");
        }

        Gl43.UseProgram(_cullProgram);
        SetUniform1f(_locLodMedium, lodMediumDist);
        SetUniform1f(_locLodLow, lodLowDist);
        SetUniform1f(_locLodImposter, lodImposterDist);
        SetUniform1f(_locRadius, instanceRadius);
        Gl43.Uniform1i(_locShadowPass, 0);
        Gl43.UseProgram(0);

        _preskinProgram = CompileComputeShader("gpu_preskin.comp");
        _locPreskinVertexCount = Gl43.GetUniformLocation(_preskinProgram, "uVertexCount");
        _locPreskinRows = Gl43.GetUniformLocation(_preskinProgram, "uPoseRows");
        _locPreskinStride = Gl43.GetUniformLocation(_preskinProgram, "uPoseStride");
        if (_locPreskinVertexCount < 0 || _locPreskinRows < 0 || _locPreskinStride < 0)
        {
            throw new InvalidOperationException("gpu_preskin.comp 缺少必需 uniform。");
        }

        string shaderDir = AppContext.BaseDirectory;
        _shadowDepthShader = RaylibShaderLoader.Load(shaderDir, "shadow_depth_preskin.vs", "shadow_depth.fs", "shadow_depth_preskin");
        _locShadowDepthMvp = Rl.GetShaderLocation(_shadowDepthShader, "mvp");
        _locShadowDepthVertexCount = Rl.GetShaderLocation(_shadowDepthShader, "uPreskinVertexCount");
        if (_locShadowDepthMvp < 0 || _locShadowDepthVertexCount < 0)
        {
            throw new InvalidOperationException("shadow_depth_preskin.vs 缺少必需 uniform。");
        }

        _imposterShader = RaylibShaderLoader.Load(shaderDir, "gpu_imposter.vs", "gpu_imposter.fs", "gpu_imposter");
        _locImposterMvp = Rl.GetShaderLocation(_imposterShader, "mvp");
        _locImposterAtlas = Rl.GetShaderLocation(_imposterShader, "uImposterAtlas");
        if (_locImposterMvp < 0 || _locImposterAtlas < 0)
        {
            throw new InvalidOperationException("gpu_imposter 缺少必需 uniform。");
        }

        _locImposterWanderScale = Rl.GetShaderLocation(_imposterShader, "uWanderScale");
        float viewDirs = ImposterViewDirs;
        float phaseRows = _poseRows;
        Rl.SetShaderValue(_imposterShader, Rl.GetShaderLocation(_imposterShader, "uViewDirs"), &viewDirs, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Rl.SetShaderValue(_imposterShader, Rl.GetShaderLocation(_imposterShader, "uPhaseRows"), &phaseRows, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        int atlasUnit = (int)ImposterAtlasUnit;
        Rl.SetShaderValue(_imposterShader, _locImposterAtlas, &atlasUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);

        _imposterCaptureShader = RaylibShaderLoader.Load(shaderDir, "gpu_imposter_capture.vs", "gpu_imposter_capture.fs", "gpu_imposter_capture");
        _locCaptureMvp = Rl.GetShaderLocation(_imposterCaptureShader, "mvp");
        _locCaptureVertexCount = Rl.GetShaderLocation(_imposterCaptureShader, "uPreskinVertexCount");
        _locCapturePhaseRow = Rl.GetShaderLocation(_imposterCaptureShader, "uPhaseRow");
        if (_locCaptureMvp < 0 || _locCaptureVertexCount < 0 || _locCapturePhaseRow < 0)
        {
            throw new InvalidOperationException("gpu_imposter_capture 缺少必需 uniform。");
        }

        _shadowDepthPrimingMaterial = RaylibNativeResources.LoadMaterialDefault();
    }

    private void ComputeImposterBounds(Mesh lowMesh)
    {
        float minY = float.MaxValue, maxY = float.MinValue, maxAbsXZ = 0f;
        float* v = lowMesh.vertices;
        for (int i = 0; i < lowMesh.vertexCount; i++)
        {
            float y = v[i * 3 + 1];
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
            maxAbsXZ = MathF.Max(maxAbsXZ, MathF.Max(MathF.Abs(v[i * 3]), MathF.Abs(v[i * 3 + 2])));
        }

        // bind 姿势（T-pose 手臂张开）远宽于行走帧剪影——billboard/图集都按行走宽度收，
        // quad 填充率直接减半（图集 cell 被剪影填满，留白即浪费的片元）
        _imposterHalfWidth = maxAbsXZ * 0.5f;
        _imposterHeight = maxY - minY;
    }

    /// <summary>预蒙皮 compute dispatch：把姿势行的骨骼矩阵烘焙进三级 LOD 的预蒙皮顶点缓冲。
    /// 须在 DispatchPoseEvaluation 之后、任何绘制之前每帧调用（姿势行随动画推进）。</summary>
    public void DispatchPreskin(uint poseMatrixSsbo)
    {
        Gl43.UseProgram(_preskinProgram);
        Gl43.Uniform1i(_locPreskinRows, _poseRows);
        Gl43.Uniform1i(_locPreskinStride, _poseStride);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 2, poseMatrixSsbo);
        foreach (GpuCrowdLodSlot slot in _lodSlots)
        {
            Gl43.Uniform1i(_locPreskinVertexCount, slot.Mesh.vertexCount);
            Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 0, slot.MeshDataBuffer);
            Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 8, slot.SkinnedBuffer);
            int threads = slot.Mesh.vertexCount * _poseRows;
            Gl43.DispatchCompute((uint)((threads + 255) / 256), 1, 1);
        }

        // 剔除写出的紧凑化 SSBO 与 indirect 命令都要对后续 draw 可见
        Gl43.MemoryBarrier(GL_SHADER_STORAGE_BARRIER_BIT);
        Gl43.UseProgram(0);
    }

    /// <summary>imposter billboard 尺寸（未缩放）：x = 半宽，y = 全高。场景按实例缩放传入 uImposterSize。</summary>
    public Vector2 ImposterSize => new(_imposterHalfWidth, _imposterHeight);

    /// <summary>每帧重烘 imposter 图集：16 相位 × 8 视角，正交视角渲染预蒙皮低模
    /// （RGB = 世界法线，A = 覆盖率）。须在 DispatchPreskin 之后调用——图集跟随姿势行推进 = billboard 动画。</summary>
    public void CaptureImposterAtlas()
    {
        GpuCrowdLodSlot slot = _lodSlots[2];
        Rl.BeginTextureMode(_imposterAtlas);
        Rl.ClearBackground(new Color(0, 0, 0, 0));
        Rl.rlEnableDepthTest();
        Rl.rlEnableDepthMask();
        Gl43.UseProgram(_imposterCaptureShader.id);
        Gl43.Uniform1i(_locCaptureVertexCount, slot.Mesh.vertexCount);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 8, slot.SkinnedBuffer);
        Gl43.BindVertexArray(slot.Mesh.vaoId);
        for (int phase = 0; phase < _poseRows; phase++)
        {
            Gl43.Uniform1i(_locCapturePhaseRow, phase);
            for (int dir = 0; dir < ImposterViewDirs; dir++)
            {
                float yaw = dir * MathF.PI * 2f / ImposterViewDirs;
                RaylibMatrix viewProj = BuildCaptureViewProj(yaw);
                Rl.SetShaderValueMatrix(_imposterCaptureShader, _locCaptureMvp, viewProj);
                Gl43.Viewport(phase * ImposterCellPx, dir * ImposterCellPx, ImposterCellPx, ImposterCellPx);
                Gl43.DrawElementsInstanced(GL_TRIANGLES, slot.IndexCount, GL_UNSIGNED_SHORT, IntPtr.Zero, 1);
            }
        }

        Gl43.UseProgram(0);
        Gl43.BindVertexArray(0);
        Rl.EndTextureMode();
    }

    /// <summary>capture 视角：方位角 yaw 的正交 lookAt，覆盖 [−half, +half]（垂直含身高）。</summary>
    private RaylibMatrix BuildCaptureViewProj(float yaw)
    {
        float half = MathF.Max(_imposterHalfWidth, _imposterHeight * 0.5f) * 1.15f;
        float midY = _imposterHeight * 0.5f;
        float eyeDist = half * 4f;
        Vector3 eye = new(MathF.Sin(yaw) * eyeDist, midY, MathF.Cos(yaw) * eyeDist);
        return Multiply(BuildLookAt(eye, new Vector3(0f, midY, 0f), Vector3.UnitY), BuildOrtho(-half, half, -half, half, 0.1f, eyeDist * 2f));
    }

    private static Mesh BuildImposterQuad()
    {
        float[] verts = { -0.5f, 0f, 0f, 0.5f, 0f, 0f, 0.5f, 1f, 0f, -0.5f, 1f, 0f };
        float[] uvs = { 0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f };
        ushort[] indices = { 0, 1, 2, 0, 2, 3 };
        Mesh quad = default;
        quad.vertexCount = 4;
        quad.triangleCount = 2;
        quad.vertices = AllocFloats(verts);
        quad.texcoords = AllocFloats(uvs);
        quad.indices = AllocUshorts(indices);
        RaylibNativeResources.UploadMesh(ref quad, false);
        return quad;
    }

    private static float* AllocFloats(float[] values)
    {
        IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(values.Length * sizeof(float));
        fixed (float* src = values)
        {
            System.Buffer.MemoryCopy(src, (void*)block, values.Length * sizeof(float), values.Length * sizeof(float));
        }

        return (float*)block;
    }

    private static ushort* AllocUshorts(ushort[] values)
    {
        IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(values.Length * sizeof(ushort));
        fixed (ushort* src = values)
        {
            System.Buffer.MemoryCopy(src, (void*)block, values.Length * sizeof(ushort), values.Length * sizeof(ushort));
        }

        return (ushort*)block;
    }

    private static RaylibMatrix BuildLookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        Vector3 f = Vector3.Normalize(target - eye);
        Vector3 s = Vector3.Normalize(Vector3.Cross(f, up));
        Vector3 u = Vector3.Cross(s, f);

        return new RaylibMatrix
        {
            m0 = s.X, m4 = s.Y, m8 = s.Z, m12 = -Vector3.Dot(s, eye),
            m1 = u.X, m5 = u.Y, m9 = u.Z, m13 = -Vector3.Dot(u, eye),
            m2 = -f.X, m6 = -f.Y, m10 = -f.Z, m14 = Vector3.Dot(f, eye),
            m3 = 0f, m7 = 0f, m11 = 0f, m15 = 1f,
        };
    }

    private static RaylibMatrix BuildOrtho(float left, float right, float bottom, float top, float near, float far)
    {
        float rl = 1f / (right - left);
        float tb = 1f / (top - bottom);
        float fn = 1f / (far - near);

        return new RaylibMatrix
        {
            m0 = 2f * rl, m4 = 0f, m8 = 0f, m12 = -(right + left) * rl,
            m1 = 0f, m5 = 2f * tb, m9 = 0f, m13 = -(top + bottom) * tb,
            m2 = 0f, m6 = 0f, m10 = -2f * fn, m14 = -(far + near) * fn,
            m3 = 0f, m7 = 0f, m11 = 0f, m15 = 1f,
        };
    }

    private static RaylibMatrix Multiply(in RaylibMatrix a, in RaylibMatrix b)
    {
        return new RaylibMatrix
        {
            m0 = (a.m0 * b.m0) + (a.m1 * b.m4) + (a.m2 * b.m8) + (a.m3 * b.m12),
            m1 = (a.m0 * b.m1) + (a.m1 * b.m5) + (a.m2 * b.m9) + (a.m3 * b.m13),
            m2 = (a.m0 * b.m2) + (a.m1 * b.m6) + (a.m2 * b.m10) + (a.m3 * b.m14),
            m3 = (a.m0 * b.m3) + (a.m1 * b.m7) + (a.m2 * b.m11) + (a.m3 * b.m15),
            m4 = (a.m4 * b.m0) + (a.m5 * b.m4) + (a.m6 * b.m8) + (a.m7 * b.m12),
            m5 = (a.m4 * b.m1) + (a.m5 * b.m5) + (a.m6 * b.m9) + (a.m7 * b.m13),
            m6 = (a.m4 * b.m2) + (a.m5 * b.m6) + (a.m6 * b.m10) + (a.m7 * b.m14),
            m7 = (a.m4 * b.m3) + (a.m5 * b.m7) + (a.m6 * b.m11) + (a.m7 * b.m15),
            m8 = (a.m8 * b.m0) + (a.m9 * b.m4) + (a.m10 * b.m8) + (a.m11 * b.m12),
            m9 = (a.m8 * b.m1) + (a.m9 * b.m5) + (a.m10 * b.m9) + (a.m11 * b.m13),
            m10 = (a.m8 * b.m2) + (a.m9 * b.m6) + (a.m10 * b.m10) + (a.m11 * b.m14),
            m11 = (a.m8 * b.m3) + (a.m9 * b.m7) + (a.m10 * b.m11) + (a.m11 * b.m15),
            m12 = (a.m12 * b.m0) + (a.m13 * b.m4) + (a.m14 * b.m8) + (a.m15 * b.m12),
            m13 = (a.m12 * b.m1) + (a.m13 * b.m5) + (a.m14 * b.m9) + (a.m15 * b.m13),
            m14 = (a.m12 * b.m2) + (a.m13 * b.m6) + (a.m14 * b.m10) + (a.m15 * b.m14),
            m15 = (a.m12 * b.m3) + (a.m13 * b.m7) + (a.m14 * b.m11) + (a.m15 * b.m15),
        };
    }

    /// <summary>阴影投射体剔除：相机半径球内的实例紧凑化到阴影缓冲（第 4 条 indirect 命令计数）。</summary>
    public void CullShadowCasters(Vector3 shadowCenter, float shadowRadius, int totalInstances, float timeSeconds)
    {
        uint zeroCount = 0;
        Gl43.BindBuffer(GL_DRAW_INDIRECT_BUFFER, _indirectBuffer);
        Gl43.BufferSubData(GL_DRAW_INDIRECT_BUFFER, ShadowCommandOffsetBytes + 4, 4, (IntPtr)(&zeroCount));

        Gl43.UseProgram(_cullProgram);
        Gl43.Uniform1i(_locShadowPass, 1);
        Gl43.Uniform1f(_locCullTime, timeSeconds);
        Gl43.Uniform1f(_locCullWanderScale, _gpuWanderScale);
        Gl43.Uniform3f(_locShadowCenter, shadowCenter.X, shadowCenter.Y, shadowCenter.Z);
        Gl43.Uniform1f(_locShadowRadius, shadowRadius);
        Gl43.Uniform1i(_locTotal, totalInstances);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 0, _sourceBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 7, _shadowCompactBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 6, _indirectBuffer);
        int workGroups = (totalInstances + 255) / 256;
        Gl43.DispatchCompute((uint)workGroups, 1, 1);
        Gl43.MemoryBarrier(GL_SHADER_STORAGE_BARRIER_BIT | GL_COMMAND_BARRIER_BIT);
        Gl43.Uniform1i(_locShadowPass, 0);
        Gl43.UseProgram(0);
    }

    /// <summary>阴影深度绘制：预蒙皮最粗 LOD + 阴影紧凑化实例表，实例数读自第 4 条 indirect 命令。
    /// 须在 CullShadowCasters 之后、阴影图 BeginFrame/EndFrame 区间内调用。</summary>
    public void DrawShadowIndirect(float timeSeconds)
    {
        GpuCrowdLodSlot slot = _lodSlots[2];
        if (_shadowDepthPrimedFrame != _frameCounter)
        {
            // 驱动合同：本程序每帧先经 raylib 介质绘 1 实例，随后的直连 GL 深度绘制才被光栅化
            _shadowDepthPrimedFrame = _frameCounter;
            _shadowDepthPrimingMaterial.shader = _shadowDepthShader;
            RaylibMatrix primingTransform = RaylibMatrix.FromSystemNumerics(System.Numerics.Matrix4x4.Identity);
            Rl.DrawMeshInstanced(slot.Mesh, _shadowDepthPrimingMaterial, &primingTransform, 1);
        }

        Gl43.UseProgram(_shadowDepthShader.id);
        Rl.SetShaderValueMatrix(_shadowDepthShader, _locShadowDepthMvp, RaylibNativeResources.ComputeDrawMvp());
        Gl43.Uniform1i(_locShadowDepthVertexCount, slot.Mesh.vertexCount);
        Gl43.Uniform1f(Gl43.GetUniformLocation(_shadowDepthShader.id, "uTime"), timeSeconds);
        Gl43.Uniform1f(Gl43.GetUniformLocation(_shadowDepthShader.id, "uWanderScale"), _gpuWanderScale);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 3, _shadowCompactBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 8, slot.SkinnedBuffer);
        Gl43.BindVertexArray(slot.Mesh.vaoId);
        Gl43.BindBuffer(GL_DRAW_INDIRECT_BUFFER, _indirectBuffer);
        Gl43.MultiDrawElementsIndirect(GL_TRIANGLES, GL_UNSIGNED_SHORT, (IntPtr)ShadowCommandOffsetBytes, 1, 20);
        Gl43.UseProgram(0);
        LastDrawCalls++;
    }

    /// <summary>主 pass：清零计数 → cull dispatch → 4 × indirect draw（3 LOD + imposter）→ 延迟读回计数。</summary>
    public void CullAndDraw(
        in RaylibMatrix viewProj,
        Vector3 cameraPos,
        int totalInstances,
        uint skinningProgramId,
        int locMvp,
        float timeSeconds)
    {
        // 1. 清零 4 个 instanceCount 字段（各命令 instanceCount 在字节偏移 4 / 24 / 44 / 84——分散，不能一段连续清）
        uint zeroCount = 0;
        Gl43.BindBuffer(GL_DRAW_INDIRECT_BUFFER, _indirectBuffer);
        Gl43.BufferSubData(GL_DRAW_INDIRECT_BUFFER, 4, 4, (IntPtr)(&zeroCount));
        Gl43.BufferSubData(GL_DRAW_INDIRECT_BUFFER, 24, 4, (IntPtr)(&zeroCount));
        Gl43.BufferSubData(GL_DRAW_INDIRECT_BUFFER, 44, 4, (IntPtr)(&zeroCount));
        Gl43.BufferSubData(GL_DRAW_INDIRECT_BUFFER, ImposterCommandOffsetBytes + 4, 4, (IntPtr)(&zeroCount));

        // 2. 计算视锥 4 侧平面（从 view·proj 行提取）
        Span<float> planes = stackalloc float[16];
        planes[0] = viewProj.m3 + viewProj.m0;  planes[1] = viewProj.m7 + viewProj.m4;  planes[2] = viewProj.m11 + viewProj.m8;  planes[3] = viewProj.m15 + viewProj.m12;
        planes[4] = viewProj.m3 - viewProj.m0;  planes[5] = viewProj.m7 - viewProj.m4;  planes[6] = viewProj.m11 - viewProj.m8;  planes[7] = viewProj.m15 - viewProj.m12;
        planes[8] = viewProj.m3 + viewProj.m1;  planes[9] = viewProj.m7 + viewProj.m5;  planes[10] = viewProj.m11 + viewProj.m9; planes[11] = viewProj.m15 + viewProj.m13;
        planes[12] = viewProj.m3 - viewProj.m1; planes[13] = viewProj.m7 - viewProj.m5; planes[14] = viewProj.m11 - viewProj.m9; planes[15] = viewProj.m15 - viewProj.m13;

        // 3. Cull compute dispatch（主模式）
        Gl43.UseProgram(_cullProgram);
        Gl43.Uniform1i(_locShadowPass, 0);
        fixed (float* planePtr = planes)
        {
            Gl43.UniformMatrix4fv(_locFrustum, 4, false, planePtr);
        }

        Gl43.Uniform3f(_locCameraPos, cameraPos.X, cameraPos.Y, cameraPos.Z);
        Gl43.Uniform1f(_locCullTime, timeSeconds);
        Gl43.Uniform1f(_locCullWanderScale, _gpuWanderScale);
        Gl43.Uniform1i(_locTotal, totalInstances);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 0, _sourceBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 3, _lodSlots[0].CompactBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 4, _lodSlots[1].CompactBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 5, _lodSlots[2].CompactBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 9, _imposterCompactBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 6, _indirectBuffer);
        int workGroups = (totalInstances + 255) / 256;
        Gl43.DispatchCompute((uint)workGroups, 1, 1);
        Gl43.MemoryBarrier(GL_SHADER_STORAGE_BARRIER_BIT | GL_COMMAND_BARRIER_BIT);
        Gl43.UseProgram(0);

        // 4. 4 × indirect draw（每 LOD 绑自己的预蒙皮缓冲 + albedo + 顶点计数 uniform；imposter 走 quad）
        if (_locMainPreskinVertexCount == int.MinValue)
        {
            _locMainPreskinVertexCount = Gl43.GetUniformLocation(skinningProgramId, "uPreskinVertexCount");
            _locMainAlbedoMap = Gl43.GetUniformLocation(skinningProgramId, "uAlbedoMap");
            _locMainHasAlbedoMap = Gl43.GetUniformLocation(skinningProgramId, "uHasAlbedoMap");
            _locMainWanderScale = Gl43.GetUniformLocation(skinningProgramId, "uWanderScale");
            if (_locMainPreskinVertexCount < 0)
            {
                throw new InvalidOperationException("人群主 pass 着色器缺少 uPreskinVertexCount（须用 gpu_crowd_preskin.vs）。");
            }
        }

        LastDrawCalls = 0;
        for (int lod = 0; lod < 3; lod++)
        {
            GpuCrowdLodSlot slot = _lodSlots[lod];
            Gl43.UseProgram(skinningProgramId);
            if (_locMainWanderScale >= 0)
            {
                Gl43.Uniform1f(_locMainWanderScale, _gpuWanderScale);
            }

            Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 3, slot.CompactBuffer);
            Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 8, slot.SkinnedBuffer);
            Gl43.Uniform1i(_locMainPreskinVertexCount, slot.Mesh.vertexCount);
            if (_locMainHasAlbedoMap >= 0)
            {
                Gl43.Uniform1i(_locMainHasAlbedoMap, _lodAlbedoTextures[lod] != 0 ? 1 : 0);
            }

            if (_lodAlbedoTextures[lod] != 0)
            {
                Gl43.ActiveTexture(GL_TEXTURE0 + (uint)AlbedoTextureUnit);
                Gl43.BindTexture(GL_TEXTURE_2D, _lodAlbedoTextures[lod]);
                Gl43.ActiveTexture(GL_TEXTURE0);
            }

            Rl.SetShaderValueMatrix(
                new Shader { id = skinningProgramId, locs = null },
                locMvp,
                viewProj);
            Gl43.BindVertexArray(slot.Mesh.vaoId);
            Gl43.BindBuffer(GL_DRAW_INDIRECT_BUFFER, _indirectBuffer);
            Gl43.MultiDrawElementsIndirect(GL_TRIANGLES, GL_UNSIGNED_SHORT, (IntPtr)slot.IndirectOffsetBytes, 1, 20);
            Gl43.UseProgram(0);
            LastDrawCalls++;
        }

        // imposter billboard draw（mvp 由渲染器设置；PBR/相机基 uniform 由场景经 ImposterShader 设置）
        Gl43.UseProgram(_imposterShader.id);
        Rl.SetShaderValueMatrix(_imposterShader, _locImposterMvp, RaylibNativeResources.ComputeDrawMvp());
        if (_locImposterWanderScale >= 0)
        {
            Gl43.Uniform1f(Gl43.GetUniformLocation(_imposterShader.id, "uWanderScale"), _gpuWanderScale);
        }
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, 3, _imposterCompactBuffer);
        Gl43.ActiveTexture(GL_TEXTURE0 + (uint)ImposterAtlasUnit);
        Gl43.BindTexture(GL_TEXTURE_2D, _imposterAtlas.texture.id);
        Gl43.ActiveTexture(GL_TEXTURE0);
        Gl43.BindVertexArray(_imposterQuad.vaoId);
        Gl43.BindBuffer(GL_DRAW_INDIRECT_BUFFER, _indirectBuffer);
        Gl43.MultiDrawElementsIndirect(GL_TRIANGLES, GL_UNSIGNED_SHORT, (IntPtr)ImposterCommandOffsetBytes, 1, 20);
        Gl43.UseProgram(0);
        LastDrawCalls++;

        // 5. 延迟读回：本帧 GPU 侧拷贝计数（五个分散偏移汇成 20B），读 ReadbackLagFrames 帧前的拷贝（零停顿）
        int stagingIndex = _frameCounter % ReadbackRingSize;
        Gl43.BindBuffer(GL_COPY_READ_BUFFER, _indirectBuffer);
        Gl43.BindBuffer(GL_COPY_WRITE_BUFFER, _countStaging[stagingIndex]);
        Gl43.CopyBufferSubData(GL_COPY_READ_BUFFER, GL_COPY_WRITE_BUFFER, 4, 0, 4);
        Gl43.CopyBufferSubData(GL_COPY_READ_BUFFER, GL_COPY_WRITE_BUFFER, 24, 4, 4);
        Gl43.CopyBufferSubData(GL_COPY_READ_BUFFER, GL_COPY_WRITE_BUFFER, 44, 8, 4);
        Gl43.CopyBufferSubData(GL_COPY_READ_BUFFER, GL_COPY_WRITE_BUFFER, ShadowCommandOffsetBytes + 4, 12, 4);
        Gl43.CopyBufferSubData(GL_COPY_READ_BUFFER, GL_COPY_WRITE_BUFFER, ImposterCommandOffsetBytes + 4, 16, 4);

        if (_frameCounter >= ReadbackLagFrames)
        {
            int lagged = (_frameCounter - ReadbackLagFrames) % ReadbackRingSize;
            uint* counts = stackalloc uint[5];
            Gl43.BindBuffer(GL_COPY_READ_BUFFER, _countStaging[lagged]);
            Gl43.GetBufferSubData(GL_COPY_READ_BUFFER, 0, 20, (IntPtr)counts);
            _lodSlots[0].LastDrawnInstances = (int)counts[0];
            _lodSlots[1].LastDrawnInstances = (int)counts[1];
            _lodSlots[2].LastDrawnInstances = (int)counts[2];
            LastShadowCasterCount = (int)counts[3];
            LastImposterCount = (int)counts[4];
        }

        _frameCounter++;
    }

    /// <summary>模拟层 presenter 通道：整表重传实例数据（须 Initialize(dynamicInstances: true)）。
    /// CPU 写最终变换/动画行，GPU 侧游走自动关闭由 Initialize(gpuWander:false) 决定。</summary>
    public void UploadInstances(ReadOnlySpan<float> instanceData)
    {
        if (!_dynamicInstances)
        {
            throw new InvalidOperationException("实例表按 STATIC 创建；模拟层通道须 Initialize(dynamicInstances: true)。");
        }

        Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, _sourceBuffer);
        fixed (float* data = instanceData)
        {
            Gl43.BufferSubData(GL_SHADER_STORAGE_BUFFER, 0, (nint)(instanceData.Length * 4), (IntPtr)data);
        }
    }

    /// <summary>绑定 IBL 资源到人群着色器约定的采样单元（env=5 立方图 / lut=6；完成后恢复 unit 0）。</summary>
    public void BindIbl(uint envCubemapId, uint brdfLutId)
    {
        Gl43.ActiveTexture(GL_TEXTURE0 + (uint)EnvCubemapUnit);
        Gl43.BindTexture(GL_TEXTURE_CUBE_MAP, envCubemapId);
        Gl43.ActiveTexture(GL_TEXTURE0 + (uint)BrdfLutUnit);
        Gl43.BindTexture(GL_TEXTURE_2D, brdfLutId);
        Gl43.ActiveTexture(GL_TEXTURE0);
    }

    /// <summary>把阴影深度纹理绑到指定采样单元供蒙皮着色器 PCF 采样（完成后恢复 unit 0）。</summary>
    public void BindShadowTexture(uint glTextureId, int unit)
    {
        Gl43.ActiveTexture(GL_TEXTURE0 + (uint)unit);
        Gl43.BindTexture(GL_TEXTURE_2D, glTextureId);
        Gl43.ActiveTexture(GL_TEXTURE0);
    }


    /// <summary>网格静态数据上传（每顶点 4×vec4：pos|0, nrm|0, boneIds(f4), weights）——预蒙皮 compute 的输入。</summary>
    private static uint UploadMeshData(Mesh mesh)
    {
        int vertexCount = mesh.vertexCount;
        float[] data = new float[vertexCount * 16];
        for (int v = 0; v < vertexCount; v++)
        {
            int dst = v * 16;
            data[dst + 0] = mesh.vertices[v * 3 + 0];
            data[dst + 1] = mesh.vertices[v * 3 + 1];
            data[dst + 2] = mesh.vertices[v * 3 + 2];
            data[dst + 3] = 0f;
            if (mesh.normals != null)
            {
                data[dst + 4] = mesh.normals[v * 3 + 0];
                data[dst + 5] = mesh.normals[v * 3 + 1];
                data[dst + 6] = mesh.normals[v * 3 + 2];
            }

            data[dst + 7] = 0f;
            data[dst + 8] = mesh.boneIds![v * 4 + 0];
            data[dst + 9] = mesh.boneIds[v * 4 + 1];
            data[dst + 10] = mesh.boneIds[v * 4 + 2];
            data[dst + 11] = mesh.boneIds[v * 4 + 3];
            data[dst + 12] = mesh.boneWeights![v * 4 + 0];
            data[dst + 13] = mesh.boneWeights[v * 4 + 1];
            data[dst + 14] = mesh.boneWeights[v * 4 + 2];
            data[dst + 15] = mesh.boneWeights[v * 4 + 3];
        }

        uint buffer = CreateBuffer((nint)data.Length * 4, GL_STATIC_DRAW);
        fixed (float* p = data)
        {
            Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, buffer);
            Gl43.BufferData(GL_SHADER_STORAGE_BUFFER, (nint)(data.Length * 4), (IntPtr)p, GL_STATIC_DRAW);
        }

        return buffer;
    }

    private static uint CreateBuffer(nint size, uint usage)
    {
        uint buffer;
        Gl43.GenBuffers(1, &buffer);
        Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, buffer);
        Gl43.BufferData(GL_SHADER_STORAGE_BUFFER, size, IntPtr.Zero, usage);
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.GlBuffer, buffer, size);
        return buffer;
    }

    private static string ExpandShaderIncludes(string source, string directory, int depth)
    {
        if (depth > 4)
        {
            throw new InvalidOperationException("Shader include 深度超限（循环 include？）。");
        }

        return System.Text.RegularExpressions.Regex.Replace(
            source,
            @"^\s*//\s*ludo:include\s+(\S+)\s*$",
            m =>
            {
                string includePath = Path.Combine(directory, m.Groups[1].Value);
                if (!File.Exists(includePath))
                {
                    throw new InvalidOperationException($"Shader include '{m.Groups[1].Value}' 未找到（{includePath}）。");
                }

                return ExpandShaderIncludes(File.ReadAllText(includePath), directory, depth + 1);
            },
            System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    private static uint CompileComputeShader(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"缺少 compute 着色器（未找到 '{path}'）。");
        }

        uint shader = Gl43.CreateShader(GL_COMPUTE_SHADER);
        byte[] source = System.Text.Encoding.UTF8.GetBytes(ExpandShaderIncludes(File.ReadAllText(path), Path.GetDirectoryName(path)!, 0) + "\0");
        fixed (byte* sourcePtr = source)
        {
            IntPtr sourceIntPtr = (IntPtr)sourcePtr;
            Gl43.ShaderSource(shader, 1, &sourceIntPtr, null);
        }

        Gl43.CompileShader(shader);
        int compiled;
        Gl43.GetShaderiv(shader, GL_COMPILE_STATUS, &compiled);
        if (compiled == GL_FALSE)
        {
            int logLength;
            Gl43.GetShaderiv(shader, GL_INFO_LOG_LENGTH, &logLength);
            byte[] log = new byte[Math.Max(logLength, 1)];
            fixed (byte* logPtr = log)
            {
                Gl43.GetShaderInfoLog(shader, log.Length, null, (IntPtr)logPtr);
            }

            Gl43.DeleteShader(shader);
            throw new InvalidOperationException($"{fileName} 编译失败：\n{System.Text.Encoding.UTF8.GetString(log).TrimEnd('\0')}");
        }

        uint program = Gl43.CreateProgram();
        Gl43.AttachShader(program, shader);
        Gl43.LinkProgram(program);
        Gl43.DeleteShader(shader);
        int linked;
        Gl43.GetProgramiv(program, GL_LINK_STATUS, &linked);
        if (linked == GL_FALSE)
        {
            Gl43.DeleteProgram(program);
            throw new InvalidOperationException($"{fileName} 程序链接失败。");
        }

        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.GlProgram, program, 1);
        return program;
    }

    private static unsafe void SetUniform1f(int location, float value)
    {
        Gl43.Uniform1f(location, value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_sourceBuffer != 0) { uint b = _sourceBuffer; Gl43.DeleteBuffers(1, &b); RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlBuffer, _sourceBuffer); }
        if (_indirectBuffer != 0) { uint b = _indirectBuffer; Gl43.DeleteBuffers(1, &b); RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlBuffer, _indirectBuffer); }
        if (_shadowCompactBuffer != 0) { uint b = _shadowCompactBuffer; Gl43.DeleteBuffers(1, &b); RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlBuffer, _shadowCompactBuffer); }
        for (int i = 0; i < 3; i++)
        {
            if (_lodSlots[i] != null)
            {
                if (_lodSlots[i].CompactBuffer != 0) { uint b = _lodSlots[i].CompactBuffer; Gl43.DeleteBuffers(1, &b); RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlBuffer, _lodSlots[i].CompactBuffer); }
                if (_lodSlots[i].MeshDataBuffer != 0) { uint b = _lodSlots[i].MeshDataBuffer; Gl43.DeleteBuffers(1, &b); RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlBuffer, _lodSlots[i].MeshDataBuffer); }
                if (_lodSlots[i].SkinnedBuffer != 0) { uint b = _lodSlots[i].SkinnedBuffer; Gl43.DeleteBuffers(1, &b); RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlBuffer, _lodSlots[i].SkinnedBuffer); }
            }
        }

        for (int i = 0; i < ReadbackRingSize; i++)
        {
            if (_countStaging[i] != 0)
            {
                uint b = _countStaging[i];
                Gl43.DeleteBuffers(1, &b);
                RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlBuffer, _countStaging[i]);
            }
        }

        if (_imposterCompactBuffer != 0) { uint b = _imposterCompactBuffer; Gl43.DeleteBuffers(1, &b); RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlBuffer, _imposterCompactBuffer); }
        if (_cullProgram != 0) { Gl43.DeleteProgram(_cullProgram); RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlProgram, _cullProgram); }
        if (_preskinProgram != 0) { Gl43.DeleteProgram(_preskinProgram); RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlProgram, _preskinProgram); }
        if (_shadowDepthShader.id != 0) { RaylibNativeResources.UnloadShader(_shadowDepthShader); _shadowDepthShader = default; }
        if (_imposterShader.id != 0) { RaylibNativeResources.UnloadShader(_imposterShader); _imposterShader = default; }
        if (_imposterCaptureShader.id != 0) { RaylibNativeResources.UnloadShader(_imposterCaptureShader); _imposterCaptureShader = default; }
        if (_shadowDepthPrimingMaterial.shader.id != 0)
        {
            _shadowDepthPrimingMaterial.shader = default;
            RaylibNativeResources.UnloadMaterial(_shadowDepthPrimingMaterial);
        }

        if (_imposterAtlas.texture.id != 0) { RaylibNativeResources.UnloadRenderTexture(_imposterAtlas); _imposterAtlas = default; }
        if (_imposterQuad.vaoId != 0) { Mesh quad = _imposterQuad; RaylibNativeResources.UnloadMesh(quad); _imposterQuad = default; }

        _disposed = true;
    }
}
