using System;

namespace Ludots.Raylib.Render
{
    public readonly record struct RaylibGpuSkinnedCapacity(
        int MaxInstances,
        int MaxBatches,
        int MaxUniquePoses,
        int MaxBoneSlots)
    {
        /// <summary>全模型累计骨骼槽位硬上限（姿势 SSBO 每行 stride 合同）。</summary>
        public const int MaxBoneSlotCapacity = 2048;

        public void Validate()
        {
            if (MaxInstances <= 0) throw new ArgumentOutOfRangeException(nameof(MaxInstances));
            if (MaxBatches <= 0) throw new ArgumentOutOfRangeException(nameof(MaxBatches));
            if (MaxUniquePoses <= 0) throw new ArgumentOutOfRangeException(nameof(MaxUniquePoses));
            if (MaxBoneSlots <= 0 || MaxBoneSlots > MaxBoneSlotCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxBoneSlots),
                    MaxBoneSlots,
                    $"GPU-skinned bone slots must be in [1, {MaxBoneSlotCapacity}].");
            }
        }
    }
}
