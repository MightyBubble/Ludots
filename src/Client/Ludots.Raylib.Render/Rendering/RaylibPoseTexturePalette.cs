using System;
using System.Runtime.InteropServices;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 姿势纹理蒙皮的 GPU 资源管理（#1395）：骨骼调色板（RGBA32F，宽=boneCount*4 texel，高=姿势数）
    /// 与实例表（RGBA32F，固定宽 1024，高=实例容量/1024）两张纹理的创建、脏行更新与销毁。
    /// 纹理创建经 LoadTextureFromImage（format=10 即 R32G32B32A32）；更新经 UpdateTextureRec 脏矩形。
    /// POINT 采样 + CLAMP + 无 mipmap（浮点纹理禁 mipmap）。
    /// </summary>
    public sealed unsafe class RaylibPoseTexturePalette : IDisposable
    {
        public const int MaxBoneCount = RaylibGpuSkinnedModelCache.MaxBones; // 128
        public const int PaletteWidthTexels = MaxBoneCount * 4;               // 512
        public const int InstanceTableWidth = 1024;
        private const int PixelFormatR32G32B32A32 = 10;

        private Texture2D _bonePalette;
        private Texture2D _instanceTable;
        private float[] _paletteStaging;
        private float[] _instanceStaging;
        private int _poseRowCapacity;
        private int _instanceCapacity;
        private int _usedPoseRows;
        private bool _disposed;

        public Texture2D BonePalette => _bonePalette;
        public Texture2D InstanceTable => _instanceTable;
        public int UsedPoseRows => _usedPoseRows;

        public RaylibPoseTexturePalette(int initialPoseRows = 64, int initialInstances = 4096)
        {
            _poseRowCapacity = Math.Max(16, initialPoseRows);
            _instanceCapacity = Math.Max(1024, initialInstances);
            _paletteStaging = new float[PaletteWidthTexels * _poseRowCapacity * 4]; // RGBA per texel
            _instanceStaging = new float[InstanceTableWidth * InstanceTableHeight(_instanceCapacity) * 4];

            _bonePalette = CreateFloatTexture(PaletteWidthTexels, _poseRowCapacity);
            _instanceTable = CreateFloatTexture(InstanceTableWidth, InstanceTableHeight(_instanceCapacity));
        }

        /// <summary>把一个骨骼矩阵（raylib 列主序 native RaylibMatrix）写入调色板 staging 的指定姿势行。</summary>
        public void WriteBoneMatrix(int poseRow, int boneIndex, in RaylibMatrix matrix)
        {
            // RaylibMatrix 字段按行声明（m0,m4,m8,m12 / m1,m5...），GLSL mat4(c0,c1,c2,c3) 按列读。
            // 列序重排：texel[k*4+0]=(m0,m1,m2,m3), texel[k*4+1]=(m4,m5,m6,m7), ...
            int baseIdx = (poseRow * PaletteWidthTexels + boneIndex * 4) * 4;
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

        /// <summary>写入实例表：poseRow + RGBA tint（4 texel = 4 float，1 texel 即可打包）。</summary>
        public void WriteInstance(int globalInstance, int poseRow, float r, float g, float b, float a)
        {
            int x = globalInstance % InstanceTableWidth;
            int y = globalInstance / InstanceTableWidth;
            int baseIdx = (y * InstanceTableWidth + x) * 4;
            _instanceStaging[baseIdx + 0] = poseRow;
            _instanceStaging[baseIdx + 1] = r;
            _instanceStaging[baseIdx + 2] = g;
            _instanceStaging[baseIdx + 3] = b;
            // alpha 存到下一个 texel（第 2 texel 的 x 分量）
            _instanceStaging[baseIdx + 4] = a;
        }

        public void CommitPoseRow(int poseRow)
        {
            if (poseRow >= _poseRowCapacity)
            {
                ResizePalette(poseRow + 1);
            }

            _usedPoseRows = Math.Max(_usedPoseRows, poseRow + 1);
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

        public void ResetFrame()
        {
            _usedPoseRows = 0;
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
            return Math.Max(1, (instanceCount + InstanceTableWidth - 1) / InstanceTableWidth);
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
