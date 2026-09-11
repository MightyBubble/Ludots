using System;
using System.Runtime.InteropServices;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 姿势纹理蒙皮的 GPU 资源管理：骨骼调色板（RGBA32F）与实例表（RGBA32F）两张纹理。
    /// 调色板按 BoneSlotsPerRow 分页：宽固定 BoneSlotsPerRow*3 texel，Y = poseRow*槽行数 + 槽行，
    /// 槽位容量不再受单行纹理宽（GL 3.3 MAX_TEXTURE_SIZE=2048 最低保证）限制，只受总面积约束。
    /// 实例表宽 1024，每实例 1 texel；槽位容量按模型全部 mesh 的 boneCount 累计定容。
    /// 纹理创建经 LoadTextureFromImage（format=10 即 R32G32B32A32）；更新经 UpdateTextureRec 脏矩形。
    /// POINT 采样 + 无 mipmap（浮点纹理禁 mipmap）。
    /// </summary>
    public sealed unsafe class RaylibPoseTexturePalette : IDisposable
    {
        public const int MaxBoneCount = RaylibGpuSkinnedModelCache.MaxBones; // 单 mesh 骨骼上限 128
        public const int MaxBoneSlotCapacity = 2048;                         // 全模型累计槽位硬上限（分页后受总面积约束，256 槽/页 × 8 页）
        public const int BoneSlotsPerRow = 256;                              // 每槽行容纳的骨位数（宽 = 256*3 = 768 texel，适配 GL 3.3 纹理宽保证）
        public const int InstanceTableWidth = 1024;
        public const int TexelsPerInstance = 1;
        public const int InstancesPerRow = InstanceTableWidth;
        private const int PixelFormatR32G32B32A32 = 10;
        internal const int TexelsPerBoneSlot = 3;

        private Texture2D _bonePalette;
        private Texture2D _instanceTable;
        private float[] _paletteStaging;
        private float[] _instanceStaging;
        private int _boneSlotCapacity;
        private int _slotRowsPerPose = 1;
        private int _poseRowCapacity;
        private int _instanceCapacity;
        private bool _disposed;

        public Texture2D BonePalette => _bonePalette;
        public Texture2D InstanceTable => _instanceTable;
        public int PaletteWidthTexels => BoneSlotsPerRow * TexelsPerBoneSlot;
        public int SlotRowsPerPose => _slotRowsPerPose;

        public RaylibPoseTexturePalette(int poseRows, int instances, int boneSlots)
        {
            if (poseRows <= 0) throw new ArgumentOutOfRangeException(nameof(poseRows));
            if (instances <= 0) throw new ArgumentOutOfRangeException(nameof(instances));
            if (boneSlots <= 0 || boneSlots > MaxBoneSlotCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(boneSlots));
            }

            _boneSlotCapacity = boneSlots;
            _slotRowsPerPose = SlabRowsFor(_boneSlotCapacity);
            _poseRowCapacity = poseRows;
            _instanceCapacity = instances;
            _paletteStaging = new float[PaletteWidthTexels * _poseRowCapacity * _slotRowsPerPose * 4]; // RGBA per texel
            _instanceStaging = new float[InstanceTableWidth * InstanceTableHeight(_instanceCapacity) * 4];

            _bonePalette = CreateFloatTexture(PaletteWidthTexels, _poseRowCapacity * _slotRowsPerPose);
            _instanceTable = CreateFloatTexture(InstanceTableWidth, InstanceTableHeight(_instanceCapacity));
        }

        private static int SlabRowsFor(int boneSlotCapacity)
        {
            return Math.Max(1, (boneSlotCapacity + BoneSlotsPerRow - 1) / BoneSlotsPerRow);
        }

        public void EnsureBoneSlotCapacity(int minSlots)
        {
            if (minSlots > _boneSlotCapacity)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPoseTexturePalette)} requires {minSlots} bone slots, configured capacity is {_boneSlotCapacity}.");
            }
        }

        /// <summary>
        /// 把一个仿射骨骼矩阵写入调色板 staging 的指定槽位。
        /// RaylibMatrix 的 GLSL 列为 (m0,m1,m2,m3)、(m4,m5,m6,m7)、
        /// (m8,m9,m10,m11)、(m12,m13,m14,m15)。仿射矩阵省略固定的末行 (0,0,0,1)，
        /// 三个 texel 的 w 分量保存平移列 xyz。
        /// </summary>
        public void WriteBoneMatrix(int poseRow, int boneSlot, in RaylibMatrix matrix)
        {
            int slabRow = boneSlot / BoneSlotsPerRow;
            int slotInRow = boneSlot - slabRow * BoneSlotsPerRow;
            int baseIdx = ((poseRow * _slotRowsPerPose + slabRow) * PaletteWidthTexels + slotInRow * TexelsPerBoneSlot) * 4;
            PackAffineBoneMatrix(in matrix, _paletteStaging.AsSpan(baseIdx, TexelsPerBoneSlot * 4));
        }

        /// <summary>
        /// 写入实例表：一个 texel = (poseRow, RGB24, alpha8, 0)。
        /// RGB24 的整数范围可由 RGBA32F 分量精确表示，颜色通道按 UNORM8 量化。
        /// </summary>
        public void WriteInstance(int globalInstance, int poseRow, float r, float g, float b, float a)
        {
            int baseIdx = globalInstance * TexelsPerInstance * 4;
            PackInstance(poseRow, r, g, b, a, _instanceStaging.AsSpan(baseIdx, 4));
        }

        internal static void PackAffineBoneMatrix(in RaylibMatrix matrix, Span<float> destination)
        {
            if (destination.Length < TexelsPerBoneSlot * 4)
            {
                throw new ArgumentException("Affine bone matrix destination requires 12 floats.", nameof(destination));
            }
            if (matrix.m3 != 0f || matrix.m7 != 0f || matrix.m11 != 0f || matrix.m15 != 1f)
            {
                throw new InvalidOperationException("GPU-skinned pose palette requires an affine RaylibMatrix with final row (0, 0, 0, 1).");
            }

            destination[0] = matrix.m0;
            destination[1] = matrix.m1;
            destination[2] = matrix.m2;
            destination[3] = matrix.m12;
            destination[4] = matrix.m4;
            destination[5] = matrix.m5;
            destination[6] = matrix.m6;
            destination[7] = matrix.m13;
            destination[8] = matrix.m8;
            destination[9] = matrix.m9;
            destination[10] = matrix.m10;
            destination[11] = matrix.m14;
        }

        internal static void PackInstance(int poseRow, float r, float g, float b, float a, Span<float> destination)
        {
            if (poseRow < 0 || poseRow > 16_777_215)
            {
                throw new ArgumentOutOfRangeException(nameof(poseRow), "Pose row must be exactly representable in an RGBA32F channel.");
            }
            if (destination.Length < 4)
            {
                throw new ArgumentException("Instance destination requires 4 floats.", nameof(destination));
            }

            int red = QuantizeUnorm8(r, nameof(r));
            int green = QuantizeUnorm8(g, nameof(g));
            int blue = QuantizeUnorm8(b, nameof(b));
            int alpha = QuantizeUnorm8(a, nameof(a));
            destination[0] = poseRow;
            destination[1] = red | (green << 8) | (blue << 16);
            destination[2] = alpha / 255f;
            destination[3] = 0f;
        }

        private static int QuantizeUnorm8(float value, string parameterName)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Instance color channels must be finite.");
            }

            return (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);
        }

        /// <summary>姿势行容量前置保障。容量判定必须先于本帧任何调色板行上传：
        /// 扩容会重建纹理，若发生在部分行已上传之后，已上传行内容即被丢弃（#1395 codex 复审结论）。</summary>
        public void EnsurePoseRowCapacity(int minRows)
        {
            if (minRows > _poseRowCapacity)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPoseTexturePalette)} requires {minRows} pose rows, configured capacity is {_poseRowCapacity}.");
            }
        }

        /// <summary>把 staging 中的脏姿势行上传到 GPU（整行 UpdateTextureRec）。</summary>
        public void FlushPaletteRow(int poseRow)
        {
            fixed (float* src = &_paletteStaging[poseRow * _slotRowsPerPose * PaletteWidthTexels * 4])
            {
                Rl.UpdateTextureRec(
                    _bonePalette,
                    new Rectangle(0, poseRow * _slotRowsPerPose, PaletteWidthTexels, _slotRowsPerPose),
                    src);
            }
        }

        /// <summary>把实例表 staging 上传到 GPU（按行脏更新）。</summary>
        public void FlushInstanceRows(int startRow, int rowCount)
        {
            fixed (float* src = &_instanceStaging[startRow * InstanceTableWidth * 4])
            {
                Rl.UpdateTextureRec(
                    _instanceTable,
                    new Rectangle(0, startRow, InstanceTableWidth, rowCount),
                    src);
            }
        }

        public void EnsureInstanceCapacity(int instanceCount)
        {
            if (instanceCount > _instanceCapacity)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPoseTexturePalette)} requires {instanceCount} instances, configured capacity is {_instanceCapacity}.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            RaylibNativeResources.UnloadTexture(_bonePalette);
            RaylibNativeResources.UnloadTexture(_instanceTable);
            _disposed = true;
        }

        private static int InstanceTableHeight(int instanceCount)
        {
            return Math.Max(1, (instanceCount + InstancesPerRow - 1) / InstancesPerRow);
        }

        private static Texture2D CreateFloatTexture(int width, int height)
        {
            // 通过手工构造 Image（data=null 先占位，format=10=R32G32B32A32）创建浮点纹理
            Image image = new Image
            {
                data = null,
                width = width,
                height = height,
                mipmaps = 1,
                format = PixelFormatR32G32B32A32,
            };
            Texture2D texture = RaylibNativeResources.LoadTextureFromImage(image);
            if (texture.id == 0)
            {
                throw new InvalidOperationException(
                    $"RaylibPoseTexturePalette failed to create {width}x{height} R32G32B32A32 texture.");
            }

            Rl.SetTextureFilter(texture, Rl.TextureFilter.TEXTURE_FILTER_POINT);
            return texture;
        }

    }
}
