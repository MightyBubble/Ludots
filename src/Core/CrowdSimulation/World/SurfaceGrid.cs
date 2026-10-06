using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// 导航格地表视图（RT-01 / RT-02 的 S1 子集）：逐格地形类型、导航区域、阻挡。
/// 区域栅格 = 地形类型经 terrainAreas 映射；区域覆盖实体（道路 / 泛洪 / 火场）与
/// 分层桥面在后续阶段接入（这里只建静态地图的阻挡栅格）。
/// </summary>
public sealed class SurfaceGrid
{
    public required int CellCount { get; init; }
    public required int CellSizeCm { get; init; }
    /// <summary>逐格地形类型编号（= .navsurface 栅格，类型表下标）。</summary>
    public required byte[] TerrainType { get; init; }
    /// <summary>逐格导航区域编号（terrainAreas 映射结果）。</summary>
    public required byte[] Area { get; init; }
    /// <summary>逐格阻挡：阻挡足迹覆盖率 ≥ structures.blockCoverage。</summary>
    public required byte[] Blocked { get; init; }
    /// <summary>跳跃候选（.navsurface 资产原样携带;按几何 profile 过滤成链接是烘焙侧的事）。</summary>
    public required NavSurfaceJumpCandidate[] JumpCandidates { get; init; }

    public static SurfaceGrid Build(
        CrowdSimulationRuntimeConfig config,
        NavSurfaceAsset surface,
        IReadOnlyList<BlockerFootprint> blockers)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(blockers);

        NavSurfaceContract.Validate(surface, config);

        int n = config.NavCellCount;
        int n2 = n * n;
        var area = new byte[n2];
        var terrainToArea = config.TerrainToArea;
        for (int i = 0; i < n2; i++)
        {
            area[i] = (byte)terrainToArea[surface.TerrainCells[i]];
        }

        var blocked = new byte[n2];
        int cs = config.NavCellSizeCm;
        var coverage = config.Structures.BlockCoverage;
        foreach (var b in blockers)
        {
            int minX = FloorDiv(b.XCm - b.HalfSizeCm, cs);
            int minY = FloorDiv(b.YCm - b.HalfSizeCm, cs);
            int maxX = FloorDiv(b.XCm + b.HalfSizeCm, cs) + 1;
            int maxY = FloorDiv(b.YCm + b.HalfSizeCm, cs) + 1;
            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX > n) maxX = n;
            if (maxY > n) maxY = n;

            int bx0 = b.XCm - b.HalfSizeCm;
            int bx1 = b.XCm + b.HalfSizeCm;
            int by0 = b.YCm - b.HalfSizeCm;
            int by1 = b.YCm + b.HalfSizeCm;
            var csF = Fix64.FromInt(cs);
            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    // 逐轴覆盖份额（定点）：cover = clamp(重叠厘米数) / 格边长；两轴乘积 ≥ 阈值即阻挡。
                    int overlapX = Math.Max(0, Math.Min(bx1, (x + 1) * cs) - Math.Max(bx0, x * cs));
                    int overlapY = Math.Max(0, Math.Min(by1, (y + 1) * cs) - Math.Max(by0, y * cs));
                    Fix64 cover = (Fix64.FromInt(overlapX) / csF) * (Fix64.FromInt(overlapY) / csF);
                    if (cover >= coverage)
                    {
                        blocked[y * n + x] = 1;
                    }
                }
            }
        }

        return new SurfaceGrid
        {
            CellCount = n,
            CellSizeCm = cs,
            TerrainType = surface.TerrainCells,
            Area = area,
            Blocked = blocked,
            JumpCandidates = surface.JumpCandidates,
        };
    }

    private static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        return value < 0 && value % divisor != 0 ? q - 1 : q;
    }
}

/// <summary>正方形阻挡足迹（Box、halfWidthCm = halfHeightCm，装载契约已校验）。</summary>
public readonly struct BlockerFootprint
{
    public BlockerFootprint(int xCm, int yCm, int halfSizeCm)
    {
        XCm = xCm;
        YCm = yCm;
        HalfSizeCm = halfSizeCm;
    }

    public int XCm { get; }
    public int YCm { get; }
    public int HalfSizeCm { get; }
}
