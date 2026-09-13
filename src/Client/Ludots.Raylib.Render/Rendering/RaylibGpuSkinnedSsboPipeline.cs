using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Raylib_cs;
using static Ludots.Raylib.Render.Gl43;

namespace Ludots.Raylib.Render;

/// <summary>
/// 模型静态数据在模型数据 SSBO 中的锚点：关键帧/逆绑定姿势的绝对 vec4 基址与帧步长。
/// </summary>
public readonly struct RaylibGpuSkinnedSsboModelBinding
{
    public required int BoneCount { get; init; }
    public required int FrameStrideVec4 { get; init; }
    public required int BindInvVec4Base { get; init; }
    public required int[] ClipKeyframeVec4Base { get; init; }

    public int KeyframeVec4Base(int clipIndex, int frameIndex)
    {
        return ClipKeyframeVec4Base[clipIndex] + frameIndex * FrameStrideVec4;
    }
}

/// <summary>
/// GPU 驱动蒙皮数据层：模型数据（关键帧全局 TRS + bindPose⁻¹）、行描述、姿势输出、实例表四块 SSBO，
/// 外加 compute 姿势求值程序。CPU 每帧只写行描述与实例数据；骨骼矩阵合成完全在 GPU 侧完成
/// （raylib 5.5 UpdateModelAnimationBones 的 TRS 分解式，离散帧、无插值、无层级遍历）。
/// GL 对象经 RaylibNativeResourceLedger 记账；所有调用必须在渲染线程且 GL 上下文 current。
/// </summary>
public sealed unsafe class RaylibGpuSkinnedSsboPipeline : IDisposable
{
    public const int ModelDataBinding = 0;
    public const int RowMetaBinding = 1;
    public const int PoseMatrixBinding = 2;
    public const int InstanceBinding = 3;
    public const int ComputeLocalSizeX = 64;

    private uint _modelDataBuffer;
    private int _modelDataVec4Capacity;
    private float[] _modelDataMirror;
    private int _modelDataVec4Count;
    private readonly Dictionary<int, RaylibGpuSkinnedSsboModelBinding> _bindingsByPoseAsset = new();

    private uint _rowMetaBuffer;
    private int[] _rowMetaStaging;
    private readonly int _maxPoseRows;

    private uint _poseMatrixBuffer;
    public int PoseStride { get; }

    private uint _instanceBuffer;
    private float[] _instanceStaging;
    private readonly int _maxInstances;

    private uint _computeProgram;
    private int _locPoseStride = -1;
    private bool _disposed;

    public RaylibGpuSkinnedSsboPipeline(int maxPoseRows, int poseStride, int maxInstances)
    {
        if (maxPoseRows <= 0 || poseStride <= 0 || maxInstances <= 0)
        {
            throw new ArgumentOutOfRangeException(
                $"SSBO pipeline requires positive capacity (rows={maxPoseRows}, stride={poseStride}, instances={maxInstances}).");
        }

        _maxPoseRows = maxPoseRows;
        PoseStride = poseStride;
        _maxInstances = maxInstances;
        _modelDataVec4Capacity = 4096;
        _modelDataMirror = new float[_modelDataVec4Capacity * 4];
        _rowMetaStaging = new int[maxPoseRows * 4];
        _instanceStaging = new float[maxInstances * 16];

        _modelDataBuffer = CreateBuffer((nint)(_modelDataVec4Capacity * 16), GL_STATIC_DRAW);
        _rowMetaBuffer = CreateBuffer((nint)(maxPoseRows * 16), GL_DYNAMIC_DRAW);
        _poseMatrixBuffer = CreateBuffer((nint)((long)maxPoseRows * poseStride * 64), GL_DYNAMIC_DRAW);
        _instanceBuffer = CreateBuffer((nint)((long)maxInstances * 64), GL_DYNAMIC_DRAW);
        _computeProgram = LoadComputeProgram();
        InitPoseComputeQueries();
        _locPoseStride = Gl43.GetUniformLocation(_computeProgram, "uPoseStride");
        if (_locPoseStride < 0)
        {
            throw new InvalidOperationException("skinning_pose_compute.glsl 缺少 uPoseStride uniform。");
        }
    }

    public long LastUploadBytes { get; private set; }
    public double LastDispatchCpuMs { get; private set; }

    /// <summary>compute 姿势求值的 GPU 耗时（延迟 N 帧回读，GL 计时 query）。</summary>
    public double LastPoseComputeGpuMs { get; private set; }
    public int RegisteredModelCount => _bindingsByPoseAsset.Count;

    private readonly uint[] _poseComputeQueries = new uint[2];
    private int _poseComputeWarmupFrames;

    private uint PoseComputeStartQuery => _poseComputeQueries[0];
    private uint PoseComputeEndQuery => _poseComputeQueries[1];

    private unsafe void InitPoseComputeQueries()
    {
        fixed (uint* queries = _poseComputeQueries)
        {
            Gl43.GenQueries(2, queries);
        }
    }

    /// <summary>延迟回读 compute 的 GPU 计时：query 结果就绪即上报（滞后约数帧）。</summary>
    private unsafe void ReadBackPoseComputeGpuMs()
    {
        const int WarmupFrames = 3;
        ulong end = 0;
        Gl43.GetQueryObjectui64v(PoseComputeEndQuery, Gl43.GL_QUERY_RESULT_NO_WAIT, &end);
        if (end == 0)
        {
            return;
        }

        ulong start = 0;
        Gl43.GetQueryObjectui64v(PoseComputeStartQuery, Gl43.GL_QUERY_RESULT, &start);
        if (_poseComputeWarmupFrames < WarmupFrames)
        {
            _poseComputeWarmupFrames++;
            return;
        }

        LastPoseComputeGpuMs = (end - start) / 1e6;
    }

    /// <summary>模型驻留后调用一次：关键帧与逆绑定姿势写入模型数据 SSBO（同资产幂等）。</summary>
    public RaylibGpuSkinnedSsboModelBinding RegisterModel(
        int poseAssetId,
        Model poseModel,
        ModelAnimation* animations,
        int animCount)
    {
        if (_bindingsByPoseAsset.TryGetValue(poseAssetId, out RaylibGpuSkinnedSsboModelBinding existing))
        {
            return existing;
        }

        if (animations == null || animCount <= 0)
        {
            throw new InvalidOperationException(
                $"SSBO 姿势求值要求 poseAssetId={poseAssetId} 携带已加载动画。");
        }

        int boneCount = poseModel.boneCount;
        if (boneCount <= 0)
        {
            throw new InvalidOperationException($"SSBO 姿势求值要求 poseAssetId={poseAssetId} 含骨骼。");
        }

        int frameStrideVec4 = boneCount * 3;
        int totalFrameVec4 = 0;
        for (int clip = 0; clip < animCount; clip++)
        {
            totalFrameVec4 += animations[clip].frameCount * frameStrideVec4;
        }

        // 块布局：1 ivec4 头 + 每 clip 1 ivec4 + 关键帧 + 逆绑定姿势
        int blockVec4 = 1 + animCount + totalFrameVec4 + boneCount * 3;
        EnsureModelDataCapacity(_modelDataVec4Count + blockVec4);
        int baseVec4 = _modelDataVec4Count;

        WriteModelIvec4(baseVec4, boneCount, animCount, 0, 0);
        int cursor = baseVec4 + 1;
        int[] clipBases = new int[animCount];
        int clipCursor = baseVec4 + 1 + animCount;
        for (int clip = 0; clip < animCount; clip++)
        {
            clipBases[clip] = clipCursor;
            WriteModelIvec4(cursor + clip, animations[clip].frameCount, clipBases[clip] - baseVec4, 0, 0);
            clipCursor += animations[clip].frameCount * frameStrideVec4;
        }

        int keyframeWrite = baseVec4 + 1 + animCount;
        for (int clip = 0; clip < animCount; clip++)
        {
            ModelAnimation animation = animations[clip];
            for (int frame = 0; frame < animation.frameCount; frame++)
            {
                Transform* framePoses = animation.framePoses[frame];
                for (int bone = 0; bone < boneCount; bone++)
                {
                    Transform pose = framePoses[bone];
                    int dst = (keyframeWrite + bone * 3) * 4;
                    _modelDataMirror[dst + 0] = pose.translation.X;
                    _modelDataMirror[dst + 1] = pose.translation.Y;
                    _modelDataMirror[dst + 2] = pose.translation.Z;
                    _modelDataMirror[dst + 3] = 0f;
                    _modelDataMirror[dst + 4] = pose.rotation.X;
                    _modelDataMirror[dst + 5] = pose.rotation.Y;
                    _modelDataMirror[dst + 6] = pose.rotation.Z;
                    _modelDataMirror[dst + 7] = pose.rotation.W;
                    _modelDataMirror[dst + 8] = pose.scale.X;
                    _modelDataMirror[dst + 9] = pose.scale.Y;
                    _modelDataMirror[dst + 10] = pose.scale.Z;
                    _modelDataMirror[dst + 11] = 1f;
                }

                keyframeWrite += frameStrideVec4;
            }
        }

        int bindInvBase = keyframeWrite;
        for (int bone = 0; bone < boneCount; bone++)
        {
            Transform bind = poseModel.bindPose[bone];
            Vector4 invRotation = new(-bind.rotation.X, -bind.rotation.Y, -bind.rotation.Z, bind.rotation.W);
            Vector3 invTranslation = RotateByQuaternion(-bind.translation, invRotation);
            Vector3 invScale = new(
                1f / bind.scale.X,
                1f / bind.scale.Y,
                1f / bind.scale.Z);
            int dst = (bindInvBase + bone * 3) * 4;
            _modelDataMirror[dst + 0] = invTranslation.X;
            _modelDataMirror[dst + 1] = invTranslation.Y;
            _modelDataMirror[dst + 2] = invTranslation.Z;
            _modelDataMirror[dst + 3] = 0f;
            _modelDataMirror[dst + 4] = invRotation.X;
            _modelDataMirror[dst + 5] = invRotation.Y;
            _modelDataMirror[dst + 6] = invRotation.Z;
            _modelDataMirror[dst + 7] = invRotation.W;
            _modelDataMirror[dst + 8] = invScale.X;
            _modelDataMirror[dst + 9] = invScale.Y;
            _modelDataMirror[dst + 10] = invScale.Z;
            _modelDataMirror[dst + 11] = 0f;
        }

        _modelDataVec4Count += blockVec4;
        UploadModelDataRange(baseVec4, blockVec4);

        RaylibGpuSkinnedSsboModelBinding binding = new()
        {
            BoneCount = boneCount,
            FrameStrideVec4 = frameStrideVec4,
            BindInvVec4Base = bindInvBase,
            ClipKeyframeVec4Base = clipBases,
        };
        _bindingsByPoseAsset.Add(poseAssetId, binding);
        return binding;
    }

    public bool TryGetModelBinding(int poseAssetId, out RaylibGpuSkinnedSsboModelBinding binding)
    {
        return _bindingsByPoseAsset.TryGetValue(poseAssetId, out binding);
    }

    /// <summary>行描述写入（CPU 侧每帧）：该行的关键帧基址由 (clip, frame) 解析为绝对 vec4 基址。</summary>
    public void WriteRowMeta(
        int poseRow,
        in RaylibGpuSkinnedSsboModelBinding binding,
        int clipIndex,
        int frameIndex)
    {
        if ((uint)poseRow >= (uint)_maxPoseRows)
        {
            throw new InvalidOperationException($"SSBO 行描述越界：poseRow={poseRow} ≥ maxPoseRows={_maxPoseRows}。");
        }

        int dst = poseRow * 4;
        _rowMetaStaging[dst + 0] = binding.KeyframeVec4Base(clipIndex, frameIndex);
        _rowMetaStaging[dst + 1] = binding.BindInvVec4Base;
        _rowMetaStaging[dst + 2] = binding.BoneCount;
        _rowMetaStaging[dst + 3] = 0;
    }

    /// <summary>实例数据写入（CPU 侧每帧）：仿射变换 3×vec4 + (poseRow, RGB24, alpha, 0)。</summary>
    public void WriteInstance(int index, in RaylibMatrix transform, int poseRow, Vector4 tint)
    {
        if ((uint)index >= (uint)_maxInstances)
        {
            throw new InvalidOperationException($"SSBO 实例表越界：index={index} ≥ maxInstances={_maxInstances}。");
        }

        if (transform.m3 != 0f || transform.m7 != 0f || transform.m11 != 0f || transform.m15 != 1f)
        {
            throw new InvalidOperationException("SSBO 实例表只接受仿射变换（GL 列 3 必须是 (0,0,0,1)）。");
        }

        // FromSystemNumerics 按行主序写字段（m(4r+c)=M(r+1,c+1)），GL 列 c = 字段 (m_c, m_{4+c}, m_{8+c}, m_{12+c})；
        // 与骨骼矩阵的字段约定不同源，禁止混用 PackAffineBoneMatrix 的 (m0,m1,m2) 列式。
        int dst = index * 16;
        _instanceStaging[dst + 0] = transform.m0;
        _instanceStaging[dst + 1] = transform.m4;
        _instanceStaging[dst + 2] = transform.m8;
        _instanceStaging[dst + 3] = transform.m12;
        _instanceStaging[dst + 4] = transform.m1;
        _instanceStaging[dst + 5] = transform.m5;
        _instanceStaging[dst + 6] = transform.m9;
        _instanceStaging[dst + 7] = transform.m13;
        _instanceStaging[dst + 8] = transform.m2;
        _instanceStaging[dst + 9] = transform.m6;
        _instanceStaging[dst + 10] = transform.m10;
        _instanceStaging[dst + 11] = transform.m14;
        PackInstanceTint(poseRow, tint, _instanceStaging.AsSpan(dst + 12, 4));
    }

    /// <summary>上传行描述与实例数据并派发 compute 姿势求值；屏障保证首个消费者 draw 可读。</summary>
    public void DispatchPoseEvaluation(int poseRowCount, int instanceCount)
    {
        if ((uint)poseRowCount > (uint)_maxPoseRows)
        {
            throw new InvalidOperationException($"SSBO 姿势求值行数越界：{poseRowCount} > {_maxPoseRows}。");
        }

        if ((uint)instanceCount > (uint)_maxInstances)
        {
            throw new InvalidOperationException($"SSBO 实例数越界：{instanceCount} > {_maxInstances}。");
        }

        long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        long uploadBytes = 0;
        if (poseRowCount > 0)
        {
            fixed (int* meta = _rowMetaStaging)
            {
                Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, _rowMetaBuffer);
                Gl43.BufferSubData(GL_SHADER_STORAGE_BUFFER, 0, (nint)(poseRowCount * 16), (IntPtr)meta);
            }

            uploadBytes += poseRowCount * 16L;
        }

        if (instanceCount > 0)
        {
            fixed (float* instances = _instanceStaging)
            {
                Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, _instanceBuffer);
                Gl43.BufferSubData(GL_SHADER_STORAGE_BUFFER, 0, (nint)((long)instanceCount * 64), (IntPtr)instances);
            }

            uploadBytes += (long)instanceCount * 64L;
        }

        Gl43.UseProgram(_computeProgram);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, ModelDataBinding, _modelDataBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, RowMetaBinding, _rowMetaBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, PoseMatrixBinding, _poseMatrixBuffer);
        Gl43.Uniform1i(_locPoseStride, PoseStride);
        if (poseRowCount > 0)
        {
            Gl43.QueryCounter(PoseComputeStartQuery, Gl43.GL_TIMESTAMP);
            Gl43.DispatchCompute(1, (uint)poseRowCount, 1);
            Gl43.MemoryBarrier(GL_SHADER_STORAGE_BARRIER_BIT);
            Gl43.QueryCounter(PoseComputeEndQuery, Gl43.GL_TIMESTAMP);
            ReadBackPoseComputeGpuMs();
        }

        LastUploadBytes = uploadBytes;
        LastDispatchCpuMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000d
            / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>把某姿势行的 GPU 合成矩阵读回（等价性测试与诊断用）。</summary>
    public void ReadBackPoseRow(int poseRow, Span<float> destination)
    {
        if ((uint)poseRow >= (uint)_maxPoseRows)
        {
            throw new ArgumentOutOfRangeException(nameof(poseRow));
        }

        if (destination.Length < PoseStride * 16)
        {
            throw new ArgumentException($"读回目标需要 {PoseStride * 16} 个 float（stride={PoseStride}）。");
        }

        nint length = (nint)((long)PoseStride * 64);
        Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, _poseMatrixBuffer);
        IntPtr mapped = Gl43.MapBufferRange(GL_SHADER_STORAGE_BUFFER, (nint)((long)poseRow * PoseStride * 64), length, GL_MAP_READ_BIT);
        if (mapped == IntPtr.Zero)
        {
            throw new InvalidOperationException("SSBO 姿势输出映射失败。");
        }

        new Span<float>((void*)mapped, PoseStride * 16).CopyTo(destination);
        Gl43.UnmapBuffer(GL_SHADER_STORAGE_BUFFER);
    }

    /// <summary>姿势输出 SSBO 的 GPU 缓冲名（binding 2 消费）。</summary>
    public uint PoseMatrixBuffer => _poseMatrixBuffer;

    /// <summary>顶点着色器消费前的 SSBO 绑定（姿势输出 + 实例表）。</summary>
    public void BindForVertexDraw()
    {
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, PoseMatrixBinding, _poseMatrixBuffer);
        Gl43.BindBufferBase(GL_SHADER_STORAGE_BUFFER, InstanceBinding, _instanceBuffer);
    }

    /// <summary>诊断倾倒：首个实例 staging/GPU 内容 + 姿势行 0 回读（LUDOTS_SSBO_DEBUG=1）。</summary>
    internal void DebugDumpFirstFrame(int poseRowCount, int instanceCount, in RaylibMatrix firstTransform, int firstPoseRow)
    {
        Console.WriteLine(
            $"[ssbo-debug] rows={poseRowCount} instances={instanceCount} stride={PoseStride} " +
            $"firstTransform(m0={firstTransform.m0:F3} m5={firstTransform.m5:F3} m10={firstTransform.m10:F3} " +
            $"m12={firstTransform.m12:F3} m13={firstTransform.m13:F3} m14={firstTransform.m14:F3}) poseRow0={firstPoseRow}");
        Console.WriteLine(
            "[ssbo-debug] instance0 staging: " + string.Join(",", _instanceStaging.AsSpan(0, 16).ToArray().Select(v => v.ToString("F3"))));
        float[] poseRowBack = new float[PoseStride * 16];
        ReadBackPoseRow(0, poseRowBack);
        Console.WriteLine(
            "[ssbo-debug] poseRow0 bone0 gpu: " + string.Join(",", poseRowBack.AsSpan(0, 16).ToArray().Select(v => v.ToString("F3"))));
    }

    private static uint CreateBuffer(nint sizeBytes, uint usage)
    {
        uint buffer;
        Gl43.GenBuffers(1, &buffer);
        Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, buffer);
        Gl43.BufferData(GL_SHADER_STORAGE_BUFFER, sizeBytes, IntPtr.Zero, usage);
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.GlBuffer, buffer, sizeBytes);
        return buffer;
    }

    private static void DeleteBuffer(uint buffer)
    {
        if (buffer != 0)
        {
            Gl43.DeleteBuffers(1, &buffer);
            RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlBuffer, buffer);
        }
    }

    private void EnsureModelDataCapacity(int requiredVec4)
    {
        if (requiredVec4 <= _modelDataVec4Capacity)
        {
            return;
        }

        int newCapacity = _modelDataVec4Capacity;
        while (newCapacity < requiredVec4)
        {
            newCapacity *= 2;
        }

        Array.Resize(ref _modelDataMirror, newCapacity * 4);
        uint newBuffer = CreateBuffer((nint)((long)newCapacity * 16), GL_STATIC_DRAW);
        Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, newBuffer);
        fixed (float* mirror = _modelDataMirror)
        {
            Gl43.BufferSubData(GL_SHADER_STORAGE_BUFFER, 0, (nint)((long)_modelDataVec4Count * 16), (IntPtr)mirror);
        }

        DeleteBuffer(_modelDataBuffer);
        _modelDataBuffer = newBuffer;
        _modelDataVec4Capacity = newCapacity;
    }

    private void UploadModelDataRange(int baseVec4, int vec4Count)
    {
        Gl43.BindBuffer(GL_SHADER_STORAGE_BUFFER, _modelDataBuffer);
        fixed (float* mirror = _modelDataMirror)
        {
            Gl43.BufferSubData(
                GL_SHADER_STORAGE_BUFFER,
                (nint)((long)baseVec4 * 16),
                (nint)((long)vec4Count * 16),
                (IntPtr)(mirror + (long)baseVec4 * 4));
        }
    }

    private void WriteModelIvec4(int vec4Index, int x, int y, int z, int w)
    {
        int dst = vec4Index * 4;
        _modelDataMirror[dst + 0] = BitConverter.Int32BitsToSingle(x);
        _modelDataMirror[dst + 1] = BitConverter.Int32BitsToSingle(y);
        _modelDataMirror[dst + 2] = BitConverter.Int32BitsToSingle(z);
        _modelDataMirror[dst + 3] = BitConverter.Int32BitsToSingle(w);
    }

    private static uint LoadComputeProgram()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "skinning_pose_compute.glsl");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"SSBO 姿势求值缺少 compute 着色器（未找到 '{path}'）。");
        }

        uint shader = Gl43.CreateShader(GL_COMPUTE_SHADER);
        byte[] source = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(path) + "\0");
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
            throw new InvalidOperationException($"skinning_pose_compute.glsl 编译失败：\n{System.Text.Encoding.UTF8.GetString(log).TrimEnd('\0')}");
        }

        uint program = Gl43.CreateProgram();
        Gl43.AttachShader(program, shader);
        Gl43.LinkProgram(program);
        Gl43.DeleteShader(shader);
        int linked;
        Gl43.GetProgramiv(program, GL_LINK_STATUS, &linked);
        if (linked == GL_FALSE)
        {
            int logLength;
            Gl43.GetProgramiv(program, GL_INFO_LOG_LENGTH, &logLength);
            byte[] log = new byte[Math.Max(logLength, 1)];
            fixed (byte* logPtr = log)
            {
                Gl43.GetProgramInfoLog(program, log.Length, null, (IntPtr)logPtr);
            }

            Gl43.DeleteProgram(program);
            throw new InvalidOperationException($"skinning_pose_compute 程序链接失败：\n{System.Text.Encoding.UTF8.GetString(log).TrimEnd('\0')}");
        }

        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.GlProgram, program, 1);
        return program;
    }

    /// <summary>实例 vec4[3] 打包：(poseRow, RGB24, alpha8, 0)——与原纹理实例表合同逐位一致。</summary>
    private static void PackInstanceTint(int poseRow, Vector4 tint, Span<float> destination)
    {
        if (poseRow < 0 || poseRow > 16_777_215)
        {
            throw new ArgumentOutOfRangeException(nameof(poseRow), "Pose row must be exactly representable in an RGBA32F channel.");
        }

        int Quantize(float value)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(tint), "Instance color channels must be finite.");
            }

            return (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);
        }

        destination[0] = poseRow;
        destination[1] = Quantize(tint.X) | (Quantize(tint.Y) << 8) | (Quantize(tint.Z) << 16);
        destination[2] = Quantize(tint.W) / 255f;
        destination[3] = 0f;
    }

    private static Vector3 RotateByQuaternion(Vector3 v, Vector4 q)
    {
        Vector3 axis = new(q.X, q.Y, q.Z);
        return v + 2f * Vector3.Cross(axis, Vector3.Cross(axis, v) + q.W * v);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_modelDataBuffer != 0 || _rowMetaBuffer != 0 || _poseMatrixBuffer != 0 || _instanceBuffer != 0 || _computeProgram != 0)
        {
            DeleteBuffer(_modelDataBuffer);
            DeleteBuffer(_rowMetaBuffer);
            DeleteBuffer(_poseMatrixBuffer);
            DeleteBuffer(_instanceBuffer);
            if (_computeProgram != 0)
            {
                Gl43.DeleteProgram(_computeProgram);
                RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.GlProgram, _computeProgram);
            }
        }

        _modelDataBuffer = 0;
        _rowMetaBuffer = 0;
        _poseMatrixBuffer = 0;
        _instanceBuffer = 0;
        _computeProgram = 0;
        _disposed = true;
    }
}
