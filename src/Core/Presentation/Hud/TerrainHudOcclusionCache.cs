using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// 世界 HUD 地形遮挡的量化/缓存配置。
    /// <see cref="CacheCapacity"/>为 0 时退回逐项精确 raycast（不缓存）。
    /// </summary>
    public readonly struct TerrainHudOcclusionConfig
    {
        public TerrainHudOcclusionConfig(int cacheCapacity, int cellDivisor, int heightBucketCm)
        {
            CacheCapacity = cacheCapacity;
            CellDivisor = cellDivisor;
            HeightBucketCm = heightBucketCm;
        }

        public int CacheCapacity { get; }
        public int CellDivisor { get; }
        public int HeightBucketCm { get; }

        public static TerrainHudOcclusionConfig Disabled => new(0, 8, 100);
        public static TerrainHudOcclusionConfig Default => new(8192, 8, 100);
    }

    /// <summary>
    /// 有界、零分配的跨帧地形遮挡缓存。世界 HUD 的遮挡判定只依赖
    /// (heightmap 版本, 相机位置所在的方格, 锚点所在的方格, 锚点高度桶)。
    /// 密集人群（大量锚点共享同一格）与相对静止的相机使命中率极高：
    /// 稳态下每帧只需为"新出现的键"做一次真遮挡射线。
    /// 每个键的可见性由首次触碰该键时的那一次精确 raycast 决定并复用。
    ///
    /// 容量在构造时显式配置；表满时做一次显式全清（计数到
    /// <see cref="OverflowClearCount"/>）并重算——缓存清空只增加少量重算，
    /// 不改变任何可见性结果，不是静默失败。查询/写入路径零分配。
    /// </summary>
    public sealed class TerrainHudOcclusionCache
    {
        private readonly long[] _keys;
        private readonly byte[] _visible;
        private readonly int _mask;
        private readonly int _capacity;

        public int OverflowClearCount { get; private set; }
        public int EntryCount { get; private set; }

        public TerrainHudOcclusionCache(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "TerrainHudOcclusionCache requires capacity > 0.");
            }

            int size = 1;
            while (size < capacity)
            {
                size <<= 1;
            }

            size <<= 1; // 装载因子上限 ~0.5，避免退化成线性扫表
            _keys = new long[size];
            _visible = new byte[size];
            _mask = size - 1;
            _capacity = capacity;
        }

        /// <summary>
        /// 组合缓存键。分量各自压缩到 16 位：
        /// heightmapRevision(16) | camX(10) | camZ(10) | anchorX(11) | anchorZ(11) | heightBucket(6)
        /// 分量按配置的格宽取模后纳入（在调用方量化）。
        /// </summary>
        public static long ComposeKey(
            int heightmapRevision,
            int cameraCellX,
            int cameraCellZ,
            int anchorCellX,
            int anchorCellZ,
            int heightBucket)
        {
            return ((long)(uint)(heightmapRevision & 0xFFFF) << 48) |
                   ((long)(uint)(cameraCellX & 0x3FF) << 38) |
                   ((long)(uint)(cameraCellZ & 0x3FF) << 28) |
                   ((long)(uint)(anchorCellX & 0x7FF) << 17) |
                   ((long)(uint)(anchorCellZ & 0x7FF) << 6) |
                   ((uint)(heightBucket & 0x3F));
        }

        public bool TryGet(long key, out bool visible)
        {
            int index = (int)key & _mask;
            long[] keys = _keys;
            byte[] visibleArr = _visible;
            while (keys[index] != 0L)
            {
                if (keys[index] == key)
                {
                    visible = visibleArr[index] != 0;
                    return true;
                }

                index = (index + 1) & _mask;
            }

            visible = false;
            return false;
        }

        public void Set(long key, bool visible)
        {
            int index = (int)key & _mask;
            long[] keys = _keys;
            byte[] visibleArr = _visible;
            while (keys[index] != 0L)
            {
                if (keys[index] == key)
                {
                    visibleArr[index] = visible ? (byte)1 : (byte)0;
                    return;
                }

                index = (index + 1) & _mask;
            }

            if (EntryCount >= _capacity)
            {
                // 明确的全清：缓存不满是冷启动，重算与未命中等价，结果不变。
                Array.Clear(keys, 0, keys.Length);
                Array.Clear(visibleArr, 0, visibleArr.Length);
                EntryCount = 0;
                OverflowClearCount++;
                index = (int)key & _mask;
            }

            keys[index] = key;
            visibleArr[index] = visible ? (byte)1 : (byte)0;
            EntryCount++;
        }

        /// <summary>
        /// 从渲染源高度图推导采样格宽（cm）。非块状运行时等价于单块。
        /// </summary>
        public static int ResolveTerrainCellSizeCm(IContinuousHeightmap heightmap)
        {
            if (heightmap is IContinuousHeightmapRenderSource source)
            {
                int samplesX = Math.Max(2, source.ChunkColumns * source.SamplesPerChunkColumn);
                float cell = source.Bounds.Width / (float)(samplesX - 1);
                if (float.IsFinite(cell) && cell > 0f)
                {
                    return (int)MathF.Round(cell);
                }
            }

            return 6250; // 与 mass_navigation relief 一致的安全缺省（可被配置覆盖）
        }
    }
}
