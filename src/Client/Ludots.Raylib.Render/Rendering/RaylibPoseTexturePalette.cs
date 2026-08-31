using System;
using System.Runtime.InteropServices;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 姿势纹理蒙皮的 GPU 资源管理：骨骼调色板（RGBA32F，固定宽 1024、高不超过 1024 texel）。
    /// 每个 pose 的骨骼槽位沿 X 轴每行容纳 256 个，超过一行时继续占用相邻物理行。
    /// 与实例表（RGBA32F，宽 1024，每实例 2 texel）两张纹理的创建、脏行更新与销毁。
    /// 槽位容量按模型全部 mesh 的 boneCount 累计动态扩容（多 mesh 各持局部骨骼集，允许重叠）。
    /// 纹理创建经 LoadTextureFromImage（format=10 即 R32G32B32A32）；更新经 UpdateTextureRec 脏矩形。
    /// POINT 采样 + 无 mipmap（浮点纹理禁 mipmap）。
    /// </summary>
    public sealed unsafe class RaylibPoseTexturePalette : IDisposable
    {
        public const int MaxBoneCount = RaylibGpuSkinnedModelCache.MaxBones; // 单 mesh 骨骼上限 128
        public const int BonePaletteTextureWidthTexels = 1024;
        public const int BonePaletteTextureHeightTexels = 1024;
        public const int TexelsPerBoneSlot = 4;
        public const int BoneSlotsPerTextureRow = BonePaletteTextureWidthTexels / TexelsPerBoneSlot;
        public const int MaxBoneSlotCapacity = BoneSlotsPerTextureRow * BonePaletteTextureHeightTexels;
        public const int InstanceTableWidth = 1024;
        public const int TexelsPerInstance = 2;
        public const int InstancesPerRow = InstanceTableWidth / TexelsPerInstance; // 512
        private const int PixelFormatR32G32B32A32 = 10;

        private Texture2D _bonePalette;
        private Texture2D _instanceTable;
        private float[] _paletteStaging;
        private float[] _instanceStaging;
        private int _boneSlotCapacity;
        private int _poseRowCapacity;
        private int _rowsPerPose;
        private int _instanceCapacity;
        private bool _disposed;

        public Texture2D BonePalette => _bonePalette;
        public Texture2D InstanceTable => _instanceTable;
        public int PaletteWidthTexels => BonePaletteTextureWidthTexels;
        public int PaletteHeightTexels => GetPaletteHeightTexels(_poseRowCapacity, _rowsPerPose);
        public int RowsPerPose => _rowsPerPose;

        public RaylibPoseTexturePalette(int initialPoseRows = 64, int initialInstances = 4096)
        {
            _boneSlotCapacity = MaxBoneCount * 2;
            _rowsPerPose = GetRowsPerPose(_boneSlotCapacity);
            _poseRowCapacity = ResolveInitialPoseRowCapacity(initialPoseRows, _rowsPerPose);
            _instanceCapacity = Math.Max(1024, initialInstances);
            _paletteStaging = new float[PaletteWidthTexels * GetPaletteHeightTexels(_poseRowCapacity, _rowsPerPose) * 4]; // RGBA per texel
            _instanceStaging = new float[InstanceTableWidth * InstanceTableHeight(_instanceCapacity) * 4];

            _bonePalette = CreateFloatTexture(PaletteWidthTexels, GetPaletteHeightTexels(_poseRowCapacity, _rowsPerPose));
            _instanceTable = CreateFloatTexture(InstanceTableWidth, InstanceTableHeight(_instanceCapacity));
        }

        /// <summary>按本帧的骨骼槽位与逻辑 pose 数一次性规划二维调色板。</summary>
        public void EnsureCapacity(int minSlots, int minPoseRows)
        {
            (int boneSlotCapacity, int poseRowCapacity, int rowsPerPose) = ResolvePaletteCapacity(
                _boneSlotCapacity,
                _poseRowCapacity,
                minSlots,
                minPoseRows);
            if (boneSlotCapacity == _boneSlotCapacity && poseRowCapacity == _poseRowCapacity)
            {
                return;
            }

            int newHeight = GetPaletteHeightTexels(poseRowCapacity, rowsPerPose);
            Array.Resize(ref _paletteStaging, PaletteWidthTexels * newHeight * 4);
            RaylibNativeResources.UnloadTexture(_bonePalette);
            _bonePalette = CreateFloatTexture(PaletteWidthTexels, newHeight);
            _boneSlotCapacity = boneSlotCapacity;
            _poseRowCapacity = poseRowCapacity;
            _rowsPerPose = rowsPerPose;
        }

        internal static (int BoneSlotCapacity, int PoseRowCapacity, int RowsPerPose) ResolvePaletteCapacity(
            int currentBoneSlotCapacity,
            int currentPoseRowCapacity,
            int minSlots,
            int minPoseRows)
        {
            if (minSlots <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minSlots), minSlots, "Bone slot capacity must be positive.");
            }

            if (minPoseRows <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minPoseRows), minPoseRows, "Pose row capacity must be positive.");
            }

            int requiredRowsPerPose = GetRowsPerPose(minSlots);
            int maxPoseRows = BonePaletteTextureHeightTexels / requiredRowsPerPose;
            if (minPoseRows > maxPoseRows)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPoseTexturePalette)} requires {minSlots} bone slots and {minPoseRows} pose rows, exceeding the {BonePaletteTextureWidthTexels}x{BonePaletteTextureHeightTexels} 2D palette capacity.");
            }

            int currentRowsPerPose = GetRowsPerPose(currentBoneSlotCapacity);
            if (requiredRowsPerPose <= currentRowsPerPose && minPoseRows <= currentPoseRowCapacity)
            {
                return (currentBoneSlotCapacity, currentPoseRowCapacity, currentRowsPerPose);
            }

            int desiredPoseRows = minPoseRows > currentPoseRowCapacity
                ? Math.Max(minPoseRows, checked(currentPoseRowCapacity * 2))
                : currentPoseRowCapacity;
            int poseRowCapacity = Math.Min(maxPoseRows, Math.Max(16, desiredPoseRows));
            int boneSlotCapacity = requiredRowsPerPose * BoneSlotsPerTextureRow;
            return (boneSlotCapacity, poseRowCapacity, requiredRowsPerPose);
        }

        internal static int GetRowsPerPose(int boneSlots)
        {
            if (boneSlots <= 0 || boneSlots > MaxBoneSlotCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(boneSlots), boneSlots, $"Bone slots must be in [1, {MaxBoneSlotCapacity}].");
            }

            return (boneSlots + BoneSlotsPerTextureRow - 1) / BoneSlotsPerTextureRow;
        }

        internal static int ResolveInitialPoseRowCapacity(int initialPoseRows, int rowsPerPose)
        {
            int requestedRows = Math.Max(16, initialPoseRows);
            int maxRows = BonePaletteTextureHeightTexels / rowsPerPose;
            if (requestedRows > maxRows)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialPoseRows),
                    initialPoseRows,
                    $"Initial pose rows {requestedRows} exceed the 2D palette capacity {maxRows} for {rowsPerPose} rows per pose.");
            }

            return requestedRows;
        }

        internal static int GetPaletteHeightTexels(int poseRows, int rowsPerPose)
        {
            if (poseRows < 0 || rowsPerPose <= 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            int height = checked(poseRows * rowsPerPose);
            if (height > BonePaletteTextureHeightTexels)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPoseTexturePalette)} requires palette height {height}, exceeding the 2D texture limit {BonePaletteTextureHeightTexels}.");
            }

            return height;
        }

        internal static int GetPoseTextureRow(int poseRow, int rowsPerPose)
        {
            if (poseRow < 0 || rowsPerPose <= 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            return checked(poseRow * rowsPerPose);
        }

        internal static (int Row, int TexelX) GetBoneTexelAddress(int poseRow, int boneSlot, int rowsPerPose)
        {
            if (poseRow < 0 || boneSlot < 0 || rowsPerPose <= 0 || boneSlot >= rowsPerPose * BoneSlotsPerTextureRow)
            {
                throw new ArgumentOutOfRangeException();
            }

            int rowWithinPose = boneSlot / BoneSlotsPerTextureRow;
            int slotWithinRow = boneSlot % BoneSlotsPerTextureRow;
            return (checked(poseRow * rowsPerPose + rowWithinPose), slotWithinRow * TexelsPerBoneSlot);
        }

        /// <summary>
        /// 把一个骨骼矩阵（raylib native RaylibMatrix）写入调色板 staging 的指定槽位。
        /// texel 布局必须与 raylib 自身的骨骼矩阵上传语义逐位一致：
        /// rlSetUniformMatrices 走 glUniformMatrix4fv(transpose=true)，故 GLSL 的
        /// mat4 第 k 列 = RaylibMatrix 字段序的第 k 组 4 个分量，即
        /// texel0=(m0,m1,m2,m3), texel1=(m4,m5,m6,m7), texel2=(m8,m9,m10,m11), texel3=(m3..m15)。
        /// 按"内存连续序"复制反而得到转置（w 分量被 t·v 污染，透视除法融毁几何）。
        /// </summary>
        public void WriteBoneMatrix(int poseRow, int boneSlot, in RaylibMatrix matrix)
        {
            (int physicalRow, int texelX) = GetBoneTexelAddress(poseRow, boneSlot, _rowsPerPose);
            int baseIdx = (physicalRow * PaletteWidthTexels + texelX) * 4;
            _paletteStaging[baseIdx + 0] = matrix.m0;
            _paletteStaging[baseIdx + 1] = matrix.m1;
            _paletteStaging[baseIdx + 2] = matrix.m2;
            _paletteStaging[baseIdx + 3] = matrix.m3;
            _paletteStaging[baseIdx + 4] = matrix.m4;
            _paletteStaging[baseIdx + 5] = matrix.m5;
            _paletteStaging[baseIdx + 6] = matrix.m6;
            _paletteStaging[baseIdx + 7] = matrix.m7;
            _paletteStaging[baseIdx + 8] = matrix.m8;
            _paletteStaging[baseIdx + 9] = matrix.m9;
            _paletteStaging[baseIdx + 10] = matrix.m10;
            _paletteStaging[baseIdx + 11] = matrix.m11;
            _paletteStaging[baseIdx + 12] = matrix.m12;
            _paletteStaging[baseIdx + 13] = matrix.m13;
            _paletteStaging[baseIdx + 14] = matrix.m14;
            _paletteStaging[baseIdx + 15] = matrix.m15;
        }

        /// <summary>
        /// 写入实例表（每实例 2 texel）：texelA = (poseRow, tint.rgb)，texelB = (tint.a, 0, 0, 0)。
        /// 不与相邻实例共享 texel；借用下一 texel 会被下一个实例的 poseRow 覆盖。
        /// </summary>
        public void WriteInstance(int globalInstance, int poseRow, float r, float g, float b, float a)
        {
            int baseIdx = globalInstance * TexelsPerInstance * 4;
            // The shader receives the physical first row of the pose. Keeping this
            // conversion here preserves the batch renderer's logical pose-row API.
            _instanceStaging[baseIdx + 0] = GetPoseTextureRow(poseRow, _rowsPerPose);
            _instanceStaging[baseIdx + 1] = r;
            _instanceStaging[baseIdx + 2] = g;
            _instanceStaging[baseIdx + 3] = b;
            _instanceStaging[baseIdx + 4] = a;
        }

        /// <summary>把 staging 中一个 pose 的连续物理行上传到 GPU。</summary>
        public void FlushPaletteRow(int poseRow)
        {
            int physicalRow = GetPoseTextureRow(poseRow, _rowsPerPose);
            fixed (float* src = &_paletteStaging[physicalRow * PaletteWidthTexels * 4])
            {
                Rl.UpdateTextureRec(
                    _bonePalette,
                    new Rectangle(0, physicalRow, PaletteWidthTexels, _rowsPerPose),
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
            int neededHeight = InstanceTableHeight(instanceCount);
            int currentHeight = InstanceTableHeight(_instanceCapacity);
            if (neededHeight > currentHeight)
            {
                ResizeInstanceTable(neededHeight);
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

        private void ResizeInstanceTable(int minHeight)
        {
            int newInstanceCapacity = Math.Max(minHeight * InstanceTableWidth, _instanceCapacity * 2);
            int newHeight = InstanceTableHeight(newInstanceCapacity);
            Array.Resize(ref _instanceStaging, InstanceTableWidth * newHeight * 4);
            RaylibNativeResources.UnloadTexture(_instanceTable);
            _instanceTable = CreateFloatTexture(InstanceTableWidth, newHeight);
            _instanceCapacity = newInstanceCapacity;
        }
    }
}
