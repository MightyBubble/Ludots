using System;
using System.IO;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Tool;

/// <summary>
/// 生成 openworld 海峡演示的 ContinuousHeightmap 地形。
///
/// 地形用确定性的解析函数生成（无随机、无外部资产），使烘焙可重复。
/// 布局为"平地 + 海峡 + 山地"三段，平地与山地之间用台阶而非连续斜坡过渡，
/// 使地面 profile（maxSlopeDeg 受限）在其上确有成片可走面，而不是只剩贴着等高线的窄带。
/// </summary>
internal static class OpenWorldHeightmapGenerator
{
    private const int WorldWidthCm = 204_800;
    private const int WorldHeightCm = 204_800;
    private const int SampleColumns = 201;
    private const int SampleRows = 201;

    /// <summary>平原高度。低于海平面以下的部分为海峡。</summary>
    private const int PlainHeightCm = 800;

    /// <summary>山地台面高度。</summary>
    private const int RidgeHeightCm = 12_000;

    /// <summary>海峡最深处（负值，低于 seaLevel 即不可走）。</summary>
    private const int StraitFloorCm = -2_000;

    public static int Run(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Heightmap generator requires an output path.");
            return 2;
        }

        var samples = new short[SampleColumns * SampleRows];
        for (int row = 0; row < SampleRows; row++)
        {
            for (int column = 0; column < SampleColumns; column++)
            {
                samples[(row * SampleColumns) + column] = (short)SampleHeightCm(column, row);
            }
        }

        var layers = new[]
        {
            new ContinuousHeightmapLayerDefinition(
                layerId: 0,
                name: "height",
                sampleOffset: 0,
                sampleCount: samples.Length)
        };

        var asset = new ContinuousHeightmapAsset(
            new WorldAabbCm(0, 0, WorldWidthCm, WorldHeightCm),
            SampleColumns,
            SampleRows,
            samples,
            layers,
            ContinuousHeightmapStorageLayout.RowMajorInt16Centimeters,
            defaultLayerIndex: 0,
            interpolationMode: ContinuousHeightmapInterpolationMode.BilinearHeightfield);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (FileStream stream = File.Create(outputPath))
        {
            ContinuousHeightmapBinary.Write(stream, asset);
        }

        Console.WriteLine(
            $"Wrote openworld heightmap {outputPath} ({SampleColumns}x{SampleRows}, {WorldWidthCm / 100_000f:F2}km, {new FileInfo(outputPath).Length} bytes).");
        return 0;
    }

    /// <summary>
    /// 解析高度：中央为南北向海峡，两侧为成片平原，再外侧为山地台面。
    /// 所有过渡都用整数格宽的台阶，使每个台面内部完全水平（坡度 0），
    /// 台阶本身窄于一个 flow cell，不形成可行走坡道。
    /// </summary>
    private static int SampleHeightCm(int column, int row)
    {
        double u = column / (double)(SampleColumns - 1);
        double v = row / (double)(SampleRows - 1);
        double xCm = u * WorldWidthCm;
        double yCm = v * WorldHeightCm;

        double distanceFromCenterCm = Math.Abs(xCm - (WorldWidthCm * 0.5));

        // 海峡：中央 800m 宽的南北水道，向中心加深到海平面以下。
        const double StraitHalfWidthCm = 40_000.0;
        if (distanceFromCenterCm < StraitHalfWidthCm)
        {
            double t = 1.0 - (distanceFromCenterCm / StraitHalfWidthCm);
            return (int)Math.Round(StraitFloorCm * t);
        }

        double inlandCm = distanceFromCenterCm - StraitHalfWidthCm;

        // 滩涂窄带：从海峡边缘抬到平原高度，宽度小于一个 flow cell，坡度不产生可行走坡道。
        const double BeachWidthCm = 2_000.0;
        if (inlandCm < BeachWidthCm)
        {
            double t = inlandCm / BeachWidthCm;
            return (int)Math.Round(StraitFloorCm * (1.0 - t) + (PlainHeightCm * t));
        }

        // 平原：完全水平，保证有大量贴地可走面。
        const double PlainEndCm = 55_000.0;
        if (inlandCm < PlainEndCm)
        {
            return PlainHeightCm;
        }

        // 山地台面：一个陡坎直接抬到台面高度，台面自身水平。
        // 陡坎宽度刻意取窄，使 ground profile 无法翻越，只有 mountain profile 可以。
        const double RidgeRampWidthCm = 1_500.0;
        if (inlandCm < PlainEndCm + RidgeRampWidthCm)
        {
            double t = (inlandCm - PlainEndCm) / RidgeRampWidthCm;
            return (int)Math.Round((PlainHeightCm * (1.0 - t)) + (RidgeHeightCm * t));
        }

        // 台面：加一点沿南北向的起伏，使山口位置有地形意义，但台面内部仍比较平缓。
        double plateauWave = 900.0 * Math.Sin((yCm / WorldHeightCm) * Math.PI * 3.0);
        return (int)Math.Round(RidgeHeightCm + plateauWave);
    }
}
