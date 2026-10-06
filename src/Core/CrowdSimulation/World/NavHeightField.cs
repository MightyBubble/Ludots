using System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// 导航分辨率的高度场与坡度（RT-01 的高度栅格派生）。
/// 从 .height 资产（uint16 缩放样本）推导：样本解码用定点整数路径
/// （offset + raw × num / den），不走浮点 Decode；盒式平均降到导航格、
/// 中心差分得坡度——与导出器在量化输入上的推导一一对应。
/// </summary>
public sealed class NavHeightField
{
    public required int CellCount { get; init; }
    public required int CellSizeCm { get; init; }
    /// <summary>导航格平均高度（厘米，Fix64）。</summary>
    public required Fix64[] HeightsCm { get; init; }
    /// <summary>逐格坡度（无量纲，rise / run）。</summary>
    public required Fix64[] Slope { get; init; }

    public static NavHeightField FromHeightmap(ContinuousHeightmapAsset asset, int navCellCount, int navCellSizeCm)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!asset.UsesRawUInt16Samples)
        {
            throw new InvalidOperationException("NavHeightField 需要 uint16 缩放布局的 .height 资产。");
        }

        int b = asset.SampleColumns;
        if (asset.SampleRows != b)
        {
            throw new InvalidOperationException("NavHeightField 需要正方形高度采样。");
        }

        if (b < navCellCount || b % navCellCount != 0)
        {
            throw new InvalidOperationException($".height 分辨率（{b}）必须 ≥ 且能整除导航格数（{navCellCount}）。");
        }

        var scale = asset.SampleScale;
        int n = navCellCount;
        var raw = asset.HeightSamplesRaw;

        // 逐样本解码先在 double 完成（asset 契约的小数cm值是实数：offset + raw × num / den;
        // uint16 与 int 的小幅面乘积在 double 内精确），盒式平均后一次性转 Fix64——
        // 定点域内 raw × num 会溢出 Fix64 的 Q31.32 上限,必须先除后乘。
        int count = b * b;
        var sampleCm = new double[count];
        for (int i = 0; i < count; i++)
        {
            sampleCm[i] = scale.OffsetCm + (raw[i] * (double)scale.UnitsPerSampleNumeratorCm) / scale.UnitsPerSampleDenominator;
        }

        var heights = new Fix64[n * n];
        for (int y = 0; y < n; y++)
        {
            int by0 = y * b / n;
            int by1 = Math.Max(by0 + 1, (y + 1) * b / n);
            for (int x = 0; x < n; x++)
            {
                int bx0 = x * b / n;
                int bx1 = Math.Max(bx0 + 1, (x + 1) * b / n);
                double sum = 0;
                int cnt = 0;
                for (int by = by0; by < by1; by++)
                {
                    for (int bx = bx0; bx < bx1; bx++)
                    {
                        sum += sampleCm[by * b + bx];
                        cnt++;
                    }
                }

                heights[y * n + x] = Fix64.FromDouble(sum / cnt);
            }
        }

        var slope = new Fix64[n * n];
        Fix64 k2 = Fix64.OneValue / Fix64.FromInt(2 * navCellSizeCm);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int xl = Math.Max(0, x - 1), xr = Math.Min(n - 1, x + 1);
                int yu = Math.Max(0, y - 1), yd = Math.Min(n - 1, y + 1);
                Fix64 gx = (heights[y * n + xr] - heights[y * n + xl]) * k2;
                Fix64 gy = (heights[yd * n + x] - heights[yu * n + x]) * k2;
                slope[y * n + x] = Fix64Math.Sqrt(gx * gx + gy * gy);
            }
        }

        return new NavHeightField
        {
            CellCount = n,
            CellSizeCm = navCellSizeCm,
            HeightsCm = heights,
            Slope = slope,
        };
    }
}
