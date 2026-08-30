using System;
using System.Runtime.InteropServices;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 姿势纹理蒙皮的 GPU 资源管理（#1395）：骨骼调色板（RGBA32F，宽=槽位容量*4 texel，高=姿势数）
    /// 与实例表（RGBA32F，宽 1024，每实例 2 texel）两张纹理的创建、脏行更新与销毁。
    /// 槽位容量按模型全部 mesh 的 boneCount 累计动态扩容（多 mesh 各持局部骨骼集，允许重叠）。
    /// 纹理创建经 LoadTextureFromImage（format=10 即 R32G32B32A32）；更新经 UpdateTextureRec 脏矩形。
    /// POINT 采样 + 无 mipmap（浮点纹理禁 mipmap）。
    /// </summary>
    public sealed unsafe class RaylibPoseTexturePalette : IDisposable
    {
        public const int MaxBoneCount = RaylibGpuSkinnedModelCache.MaxBones; // 单 mesh 骨骼上限 128
        public const int MaxBoneSlotCapacity = 512;                          // 全模型累计槽位硬上限
        public const int InstanceTableWidth = 1024;
        public const int TexelsPerInstance = 2;
        public const int InstancesPerRow = InstanceTableWidth / TexelsPerInstance; // 512
        private const int PixelFormatR32G32B32A32 = 10;
        private const int TexelsPerBoneSlot = 4;

        private Texture2D _bonePalette;
        private Texture2D _instanceTable;
        private float[] _paletteStaging;
        private float[] _instanceStaging;
        private int _boneSlotCapacity;
        private int _poseRowCapacity;
        private int _instanceCapacity;
        private bool _disposed;

        public Texture2D BonePalette => _bonePalette;
        public Texture2D InstanceTable => _instanceTable;
        public int PaletteWidthTexels => _boneSlotCapacity * TexelsPerBoneSlot;

        public RaylibPoseTexturePalette(int initialPoseRows = 64, int initialInstances = 4096)
        {
            _boneSlotCapacity = MaxBoneCount * 2;
            _poseRowCapacity = Math.Max(16, initialPoseRows);
            _instanceCapacity = Math.Max(1024, initialInstances);
            _paletteStaging = new float[PaletteWidthTexels * _poseRowCapacity * 4]; // RGBA per texel
            _instanceStaging = new float[InstanceTableWidth * InstanceTableHeight(_instanceCapacity) * 4];

            _bonePalette = CreateFloatTexture(PaletteWidthTexels, _poseRowCapacity);
            _instanceTable = CreateFloatTexture(InstanceTableWidth, InstanceTableHeight(_instanceCapacity));
        }

        /// <summary>扩容骨骼槽位（多 mesh 模型的累计 boneCount 超过当前容量时）。
        /// 重建纹理会丢既有行内容——必须在帧内任何行上传之前一次性定容（与 EnsurePoseRowCapacity 同款约束）。</summary>
        public void EnsureBoneSlotCapacity(int minSlots)
        {
            if (minSlots <= _boneSlotCapacity)
            {
                return;
            }

            if (minSlots > MaxBoneSlotCapacity)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibPoseTexturePalette)} requested bone slot capacity {minSlots} exceeds hard cap {MaxBoneSlotCapacity}.");
            }

            int newCapacity = Math.Max(minSlots, _boneSlotCapacity * 2);
            Array.Resize(ref _paletteStaging, newCapacity * TexelsPerBoneSlot * _poseRowCapacity * 4);
            RaylibNativeResources.UnloadTexture(_bonePalette);
            _bonePalette = CreateFloatTexture(newCapacity * TexelsPerBoneSlot, _poseRowCapacity);
            _boneSlotCapacity = newCapacity;
        }

        /// <summary>
        /// 把一个骨骼矩阵（raylib native RaylibMatrix）写入调色板 staging 的指定槽位。
        /// texel 布局必须与 raylib 自身的骨骼矩阵上传语义逐位一致（#1395 排障结论）：
        /// rlSetUniformMatrices 走 glUniformMatrix4fv(transpose=true)，故 GLSL 的
        /// mat4 第 k 列 = RaylibMatrix 字段序的第 k 组 4 个分量，即
        /// texel0=(m0,m1,m2,m3), texel1=(m4,m5,m6,m7), texel2=(m8,m9,m10,m11), texel3=(m3..m15)。
        /// 按"内存连续序"复制反而得到转置（w 分量被 t·v 污染，透视除法融毁几何）。
        /// </summary>
        public void WriteBoneMatrix(int poseRow, int boneSlot, in RaylibMatrix matrix)
        {
            int baseIdx = (poseRow * PaletteWidthTexels + boneSlot * 4) * 4;
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
        /// 不与相邻实例共享 texel——借用"下一 texel"会被下一个实例的 poseRow 覆盖（#1395 排障结论）。
        /// </summary>
        public void WriteInstance(int globalInstance, int poseRow, float r, float g, float b, float a)
        {
            int baseIdx = globalInstance * TexelsPerInstance * 4;
            _instanceStaging[baseIdx + 0] = poseRow;
            _instanceStaging[baseIdx + 1] = r;
            _instanceStaging[baseIdx + 2] = g;
            _instanceStaging[baseIdx + 3] = b;
            _instanceStaging[baseIdx + 4] = a;
        }

        /// <summary>姿势行容量前置保障。容量判定必须先于本帧任何调色板行上传：
        /// 扩容会重建纹理，若发生在部分行已上传之后，已上传行内容即被丢弃（#1395 codex 复审结论）。</summary>
        public void EnsurePoseRowCapacity(int minRows)
        {
            if (minRows > _poseRowCapacity)
            {
                ResizePalette(minRows);
            }
        }

        /// <summary>把 staging 中的脏姿势行上传到 GPU（整行 UpdateTextureRec）。</summary>
        public void FlushPaletteRow(int poseRow)
        {
            fixed (float* src = &_paletteStaging[poseRow * PaletteWidthTexels * 4])
            {
                Rl.UpdateTextureRec(
                    _bonePalette,
                    new Rectangle(0, poseRow, PaletteWidthTexels, 1),
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

        private void ResizePalette(int minRows)
        {
            int newCapacity = Math.Max(minRows, _poseRowCapacity * 2);
            Array.Resize(ref _paletteStaging, PaletteWidthTexels * newCapacity * 4);
            RaylibNativeResources.UnloadTexture(_bonePalette);
            _bonePalette = CreateFloatTexture(PaletteWidthTexels, newCapacity);
            _poseRowCapacity = newCapacity;
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
