using System;
using System.Linq;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Recast;
using Ludots.Core.CrowdSimulation.World;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>S3 调试期固化的回归用例(定点精度与轮廓链路的临界行为)。</summary>
[TestFixture]
public class S3RegressionTests
{
    [Test]
    public void BakeTile_DeckStrip_ProducesExactQuad()
    {
        // 16×16 全空 + x∈[0,6]、y∈[2,4] 的桥面(区域码 7)条带 → 唯一矩形多边形
        const int T = 16;
        var win = new ushort[T * T];
        for (int y = 2; y <= 4; y++)
        {
            for (int x = 0; x <= 6; x++) win[y * T + x] = 7;
        }

        var entry = NavTileBaker.Bake(win, T, 12, 1.3, 12, 6);
        Assert.That(entry.Count, Is.EqualTo(1));
        Assert.That(entry.Vx, Is.EqualTo(new[] { 0, 0, 7, 7 }));
        Assert.That(entry.Vy, Is.EqualTo(new[] { 5, 2, 2, 5 }));
    }

    [Test]
    public void DeckRasterize_EndpointCell_IsCovered()
    {
        // 轴对齐桥的端点格:t == len 恰好落在 EPS 边界上——定点 Sqrt 的近似误差曾把这个格丢掉。
        var runtime = TestDefaults.Assemble();
        var deck = UpperLayerBake.RasterizeDecks(
            new[]
            {
                new BridgeDeckRecord(
                    new CrowdSimulationBridgeSpan { X0Cm = 1078125, Y0Cm = 1321875, X1Cm = 1140625, Y1Cm = 1321875, WidthCm = 18750 },
                    "deck"),
            },
            runtime);

        int n = runtime.NavCellCount;
        // 端点格 (182, 210) 必须被覆盖;段外一格 (183, 210) 不覆盖
        Assert.That(deck.AreaIndex[210 * n + 182], Is.Not.EqualTo(0), "端点格(投影 t == 路径长)必须覆盖");
        Assert.That(deck.AreaIndex[210 * n + 183], Is.EqualTo(0), "端点格外一格不得覆盖");
        // portal:端点格距端 0 < portalCells → 标记
        Assert.That(deck.PortalFlag[210 * n + 182], Is.EqualTo(1));
    }
}
