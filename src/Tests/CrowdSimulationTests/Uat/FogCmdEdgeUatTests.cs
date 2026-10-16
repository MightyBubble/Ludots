using System.Linq;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using static CrowdSimulationTests.Uat.CrowdFogUatTools;

namespace CrowdSimulationTests.Uat;

/// <summary>迷雾面命令的边界：格心落在边上算在形状内、凹多边形按偶奇规则取格、遗忘和遮蔽只动形状内的格、并组时在途遮蔽转给存活组、重叠遮蔽按格计数叠加。</summary>
public sealed class FogCmdEdgeUatTests
{
    private const int P0 = CrowdUatKit.Player;
    private const int P1 = CrowdUatKit.Player + 1;

    private sealed class Setup : System.IDisposable
    {
        public required CrowdUatScene Scene { get; init; }
        public CrowdSimSession Session => Scene.Session;
        public CrowdFog Fog => Session.Fog!;
        public required int W { get; init; }
        public required int Bx { get; init; }
        public required int By { get; init; }
        public void Dispose() => Scene.Dispose();
    }

    private static Setup Open()
    {
        var scene = CrowdUatKit.Scene();
        var s = scene.Session;
        var (bx, by) = Base(s, P0);
        CrowdUatKit.SpawnAt(s, Fix64.FromInt(bx), Fix64.FromInt(by), 20, rIdx: 0);
        CrowdUatKit.Step(s, s.Fog!.Period * 2);
        return new Setup { Scene = scene, W = WorldCm(s), Bx = bx, By = by };
    }

    [Test]
    public void SlopeTriangle_CountsCellsCentredOnTheHypotenuse()
    {
        using var u = Open();
        var fog = u.Fog;
        double[][] tri = { new[] { 500000.0, 500000.0 }, new[] { 1000000.0, 500000.0 }, new[] { 500000.0, 1000000.0 } };
        var got = AreaCells(fog, new CrowdFogShape { Poly = tri });
        var onEdge = CentreCells(fog, (x, y) => x >= 500000 && y >= 500000 && System.Math.Abs(x + y - 1500000) < 1e-4);
        var closed = CentreCells(fog, (x, y) => x >= 500000 && y >= 500000 && x + y <= 1500000 + 1e-4);
        Assert.That(onEdge.Length, Is.EqualTo(20), $"斜边上的格心数 {onEdge.Length}，应为 20（用例前提不成立）");
        int miss = onEdge.Count(c => !got.Contains(c));
        Assert.That(miss, Is.Zero, $"斜边上 {miss}/20 格未计入（边界格应闭合包含）");
        Assert.That(SameSet(got, closed), Is.True, $"三角形格集 {got.Length}，闭合参考 {closed.Length}");
        TestContext.Out.WriteLine($"boundary: triangle {got.Length} cells, closed reference {closed.Length}, on edge {onEdge.Length}");
    }

    [Test]
    public void ConcaveStar_MatchesEvenOddReference()
    {
        using var u = Open();
        var fog = u.Fog;
        double cx = u.W * 0.5 + 3700, cy = u.W * 0.5 + 5300;
        var star = new double[10][];
        for (int k = 0; k < 10; k++)
        {
            double a = k * System.Math.PI / 5, r = k % 2 == 1 ? 90000 : 230000;
            star[k] = new[] { cx + r * System.Math.Cos(a), cy + r * System.Math.Sin(a) };
        }

        var got = AreaCells(fog, new CrowdFogShape { Poly = star });
        var want = CentreCells(fog, PnPoly(star));
        Assert.That(SameSet(got, want), Is.True, $"十角星格集 {got.Length}，PNPOLY 参考 {want.Length}");
        var hull = CentreCells(fog, (x, y) => System.Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) <= 230000);
        Assert.That(want.Length, Is.LessThan(hull.Length), "星形与外接圆格数相同（凹部未排除，用例前提不成立）");
        TestContext.Out.WriteLine($"concave: star {got.Length} cells, even-odd reference {want.Length}, circumcircle {hull.Length}");
    }

    [Test]
    public void ForgetByCircle_ClearsExactlyTheShape()
    {
        using var u = Open();
        var s = u.Session;
        var fog = u.Fog;
        int g = Group(s, P0);
        int w = u.W;
        double[] big = { w * 0.8, w * 0.8, w * 0.98, w * 0.98 };
        Reveal(s, P0, "rect", Rect(big[0], big[1], big[2], big[3]));
        double[] circle = { w * 0.89, w * 0.89, fog.FogCellSizeCm * 4.2 };
        var inside = AreaCells(fog, new CrowdFogShape { Circle = circle });
        var ring = AreaCells(fog, new CrowdFogShape { Rect = big }).Where(c => !inside.Contains(c)).ToArray();

        Forget(s, P0, "circle", Circle(circle[0], circle[1], circle[2]));
        Assert.That(inside.Any(c => Explored(fog, g, c)), Is.False, "遗忘后形状内仍有已探索格");
        int lost = ring.Count(c => !Explored(fog, g, c));
        Assert.That(lost, Is.Zero, $"遗忘误清了形状外 {lost} 格");
        TestContext.Out.WriteLine($"forget exact: inside {inside.Length}, ring {ring.Length}, ring lost {lost}");
    }

    [Test]
    public void ObscureByCircle_HidesExactlyTheShape()
    {
        using var u = Open();
        var s = u.Session;
        var fog = u.Fog;
        int g = Group(s, P0);
        double r = fog.FogCellSizeCm * 1.6;
        var inside = AreaCells(fog, new CrowdFogShape { Circle = new[] { (double)u.Bx, u.By, r } });
        var outside = Enumerable.Range(0, CellCount(fog)).Where(c => Visible(fog, g, c) && !inside.Contains(c)).ToArray();
        Assert.That(outside, Is.Not.Empty, "形状外无可见格（用例前提不成立）");

        Obscure(s, P0, "circle", Circle(u.Bx, u.By, r), fog.Period * 6);
        CrowdUatKit.Step(s, fog.Period * 2);
        Assert.That(inside.Any(c => Visible(fog, g, c)), Is.False, "遮蔽期内形状内仍可见");
        int lost = outside.Count(c => !Visible(fog, g, c));
        Assert.That(lost, Is.Zero, $"遮蔽误遮了形状外 {lost} 格");
        TestContext.Out.WriteLine($"obscure exact: inside {inside.Length}, visible outside {outside.Length}, lost {lost}");
    }

    [Test]
    public void FogShare_TransfersPendingObscureToTheSurvivingGroup()
    {
        using var u = Open();
        var s = u.Session;
        var fog = u.Fog;
        Assert.That(Group(s, P0), Is.Not.EqualTo(Group(s, P1)), "玩家 0 / 1 默认已共享视野（用例前提不成立）");
        var (x, y) = Base(s, P1);
        CrowdUatKit.SpawnAt(s, Fix64.FromInt(x), Fix64.FromInt(y), 10, rIdx: 0, player: P1);
        CrowdUatKit.Step(s, fog.Period * 2);
        double d = fog.FogCellSizeCm * 2.0;
        var cells = AreaCells(fog, new CrowdFogShape { Rect = new[] { x - d, y - d, x + d, y + d } });

        Obscure(s, P1, "rect", Rect(x - d, y - d, x + d, y + d), fog.Period * 8);
        FogShare(s, P0, P1);
        int g = Group(s, P0);
        Assert.That(Group(s, P1), Is.EqualTo(g), "合并后两玩家不在同一视野组");
        CrowdUatKit.Step(s, fog.Period * 2);
        int seen = cells.Count(c => Visible(fog, g, c));
        Assert.That(seen, Is.Zero, $"合并后幸存组在遮蔽区仍可见 {seen} 格（遮蔽未转移）");
        CrowdUatKit.Step(s, fog.Period * 8);
        int back = cells.Count(c => Visible(fog, g, c));
        Assert.That(back, Is.GreaterThan(0), "遮蔽到期后幸存组未恢复可见");
        TestContext.Out.WriteLine($"merge obscure: cells {cells.Length}, visible during {seen}, after expiry {back}");
    }

    [Test]
    public void OverlappingObscures_StackPerCell()
    {
        using var u = Open();
        var s = u.Session;
        var fog = u.Fog;
        int g = Group(s, P0);
        double d = fog.FogCellSizeCm * 2.0;
        double[] a = { u.Bx - d, u.By - d, u.Bx + d * 0.4, u.By + d };
        double[] b = { u.Bx - d * 0.4, u.By - d, u.Bx + d, u.By + d };
        var ca = AreaCells(fog, new CrowdFogShape { Rect = a });
        var cb = AreaCells(fog, new CrowdFogShape { Rect = b });
        var both = ca.Where(cb.Contains).ToArray();
        var onlyA = ca.Where(c => !cb.Contains(c)).ToArray();
        Assert.That(both.Length > 0 && onlyA.Length > 0, Is.True, "交叠区或独占区为空（用例前提不成立）");

        Obscure(s, P0, "rect", Rect(a[0], a[1], a[2], a[3]), fog.Period * 2);
        Obscure(s, P0, "rect", Rect(b[0], b[1], b[2], b[3]), fog.Period * 8);
        CrowdUatKit.Step(s, fog.Period * 4);
        Assert.That(both.Any(c => Visible(fog, g, c)), Is.False, "第一层到期后交叠区重新可见（计数未叠加）");
        Assert.That(onlyA.Any(c => Visible(fog, g, c)), Is.True, "第一层到期后其独占区未恢复可见");
        TestContext.Out.WriteLine($"overlap: both {both.Length}, only A {onlyA.Length}, only A visible {onlyA.Count(c => Visible(fog, g, c))}");
    }
}
