using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// 导航格地表视图：逐格地形类型、导航区域、阻挡。
/// 这里只把地形类型映成区域，并按阻挡足迹盖静态阻挡。上层在 UpperLayerBake，setArea 结构的代价变化在重烘分类里。
/// </summary>
public sealed class SurfaceGrid
{
    public required int NavCellsPerSide { get; init; }
    public required int NavCellSizeCm { get; init; }
    /// <summary>逐格地形类型编号（= .navsurface 栅格，类型表下标）。</summary>
    public required byte[] TerrainType { get; init; }
    /// <summary>逐格导航区域编号（地形经 traversal.json 的 terrainAreas 映射的结果）。</summary>
    public required byte[] Area { get; init; }
    /// <summary>逐格阻挡：阻挡足迹覆盖率 ≥ 实体点名模板的 effect.coverage。</summary>
    public required byte[] Blocked { get; init; }
    /// <summary>链接候选（.navsurface 资产原样携带;按几何 profile 过滤成链接是烘焙侧的事）。</summary>
    public required NavSurfaceLinkCandidate[] LinkCandidates { get; init; }

    public static SurfaceGrid Build(
        CrowdSimulationRuntimeConfig config,
        NavSurfaceAsset surface,
        IReadOnlyList<BlockerFootprint> blockers)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(blockers);

        NavSurfaceContract.Validate(surface, config);

        int navCellsPerSide = config.NavCellsPerSide;
        int n2 = navCellsPerSide * navCellsPerSide;
        var area = new byte[n2];
        var terrainToArea = config.TerrainToArea;
        for (int i = 0; i < n2; i++)
        {
            area[i] = (byte)terrainToArea[surface.TerrainCells[i]];
        }

        var blocked = new byte[n2];
        int navCellSizeCm = config.NavCellSizeCm;
        foreach (var b in blockers)
        {
            var coverage = config.Structures.Templates[config.Structures.RequireMapTemplate(b.TemplateId, CrowdStructureEffectKind.Block)].BlockCoverage;
            int minX = FloorDiv(b.XCm - b.HalfSizeCm, navCellSizeCm);
            int minY = FloorDiv(b.YCm - b.HalfSizeCm, navCellSizeCm);
            int maxX = FloorDiv(b.XCm + b.HalfSizeCm, navCellSizeCm) + 1;
            int maxY = FloorDiv(b.YCm + b.HalfSizeCm, navCellSizeCm) + 1;
            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX > navCellsPerSide) maxX = navCellsPerSide;
            if (maxY > navCellsPerSide) maxY = navCellsPerSide;

            int bx0 = b.XCm - b.HalfSizeCm;
            int bx1 = b.XCm + b.HalfSizeCm;
            int by0 = b.YCm - b.HalfSizeCm;
            int by1 = b.YCm + b.HalfSizeCm;
            var csF = Fix64.FromInt(navCellSizeCm);
            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    // 逐轴覆盖份额（定点）：cover = clamp(重叠厘米数) / 格边长；两轴乘积 ≥ 阈值即阻挡。
                    int overlapX = Math.Max(0, Math.Min(bx1, (x + 1) * navCellSizeCm) - Math.Max(bx0, x * navCellSizeCm));
                    int overlapY = Math.Max(0, Math.Min(by1, (y + 1) * navCellSizeCm) - Math.Max(by0, y * navCellSizeCm));
                    Fix64 cover = (Fix64.FromInt(overlapX) / csF) * (Fix64.FromInt(overlapY) / csF);
                    if (cover >= coverage)
                    {
                        blocked[y * navCellsPerSide + x] = 1;
                    }
                }
            }
        }

        return new SurfaceGrid
        {
            NavCellsPerSide = navCellsPerSide,
            NavCellSizeCm = navCellSizeCm,
            TerrainType = surface.TerrainCells,
            Area = area,
            Blocked = blocked,
            LinkCandidates = surface.LinkCandidates,
        };
    }

    private static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        return value < 0 && value % divisor != 0 ? q - 1 : q;
    }
}

/// <summary>地图上的正方形阻挡足迹（Box、halfWidthCm = halfHeightCm，装载契约已校验）和实体点名的结构模板 id。</summary>
public readonly struct BlockerFootprint
{
    public BlockerFootprint(int xCm, int yCm, int halfSizeCm, string templateId)
    {
        XCm = xCm;
        YCm = yCm;
        HalfSizeCm = halfSizeCm;
        TemplateId = templateId ?? throw new ArgumentNullException(nameof(templateId));
    }

    public int XCm { get; }
    public int YCm { get; }
    public int HalfSizeCm { get; }
    public string TemplateId { get; }
}
