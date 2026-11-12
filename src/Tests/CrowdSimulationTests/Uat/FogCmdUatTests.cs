using System.Collections.Generic;
using System.Linq;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using static CrowdSimulationTests.Uat.CrowdFogUatTools;

namespace CrowdSimulationTests.Uat;

/// <summary>reveal / obscure / forget / fogShare 各自真的改了视野组的探索、可见、认知和认知槽，形状按格心取格、坏形状被拒，四种命令混排的指令流两次运行逐分区相同。</summary>
public sealed class FogCmdUatTests
{
    private const int P0 = CrowdUatKit.Player;
    private const int P1 = CrowdUatKit.Player + 1;
    private const string Building = "building";

    private sealed class Setup : System.IDisposable
    {
        public required CrowdUatScene Scene { get; init; }
        public CrowdSimSession Session => Scene.Session;
        public CrowdFog Fog => Session.Fog!;
        public required int W { get; init; }
        public required int Bx { get; init; }
        public required int By { get; init; }
        public double[] R => new[] { W * 0.86, W * 0.86, W * 0.97, W * 0.97 };
        public int Size => (int)(W * 0.01);
        public void Dispose() => Scene.Dispose();
    }

    private static Setup Open()
    {
        var scene = CrowdUatKit.Scene();
        var s = scene.Session;
        int w = WorldCm(s);
        var (bx, by) = Base(s, P0);
        CrowdUatKit.SpawnAt(s, Fix64.FromInt(bx), Fix64.FromInt(by), 20, rIdx: 0);
        Order(s, P0, bx + 5000, by, fogTerrain: true);
        CrowdUatKit.Step(s, s.Fog!.Period * 2);
        return new Setup { Scene = scene, W = w, Bx = bx, By = by };
    }

    private static void Settle(CrowdSimSession s) => CrowdUatKit.Step(s, s.Config.Fog.RevealTicks + s.Fog!.Period * 2);

    [Test]
    public void Reveal_ExploresTilesAndSeesEntitiesInTheArea()
    {
        using var u = Open();
        var s = u.Session;
        var fog = u.Fog;
        int g = Group(s, P0);
        var r = u.R;
        var want = TilesIn(fog, r[0], r[1], r[2], r[3]);
        Assert.That(want, Is.Not.Empty, "矩形内无完整 nav tile（用例前提不成立）");
        Assert.That(want.All(t => fog.Tiles[g].Contains(t)), Is.False, "揭示前该区域已被探索（用例前提不成立）");
        int slot0 = s.Belief!.Slot[g];
        int id = Place(s, Building, (int)((r[0] + r[2]) / 2), (int)((r[1] + r[3]) / 2), u.Size);
        Assert.That(fog.Belief[g].ContainsKey(id), Is.False, "揭示前已认知区域内新实体（用例前提不成立）");

        Reveal(s, P0, "rect", Rect(r[0], r[1], r[2], r[3]));
        Assert.That(fog.Belief[g].ContainsKey(id), Is.True, "揭示后未认知区域内实体（reveal 应\"看了一眼\"）");
        int miss = want.Count(t => !fog.Tiles[g].Contains(t));
        Assert.That(miss, Is.Zero, $"揭示后仍有 {miss}/{want.Length} 个 tile 未知");
        Assert.That(RectCells(fog, r[0], r[1], r[2], r[3]).All(c => Explored(fog, g, c)), Is.True, "揭示后迷雾格未标记为已探索");

        Settle(s);
        var un = new HashSet<int>(fog.Unexplored(g) ?? new List<int>());
        Assert.That(want.Any(un.Contains), Is.False, "揭示区域仍被乐观寻路假设为可通行");
        Assert.That(s.Belief.Slot[g], Is.Not.EqualTo(slot0), "揭示后认知槽位未更新");
        TestContext.Out.WriteLine($"reveal: tiles {want.Length}, slot {slot0} → {s.Belief.Slot[g]}");
    }

    [Test]
    public void Obscure_HidesTheAreaUntilExpiryAndKeepsMemory()
    {
        using var u = Open();
        var s = u.Session;
        var fog = u.Fog;
        int g = Group(s, P0);
        double d = fog.FogCellSizeCm * 2.0;
        var rect = Rect(u.Bx - d, u.By - d, u.Bx + d, u.By + d);
        var cells = RectCells(fog, u.Bx - d, u.By - d, u.Bx + d, u.By + d);
        Assert.That(cells.Any(c => Visible(fog, g, c)), Is.True, "遮蔽前单位周围不可见（用例前提不成立）");
        int ticks = fog.Period * 3;

        Obscure(s, P0, "rect", rect, ticks);
        Assert.That(cells.Any(c => Visible(fog, g, c)), Is.False, "遮蔽后区域仍可见");
        Assert.That(cells.All(c => Explored(fog, g, c)), Is.True, "遮蔽清掉了已探索记忆");
        CrowdUatKit.Step(s, fog.Period * 2);
        Assert.That(cells.Any(c => Visible(fog, g, c)), Is.False, "遮蔽期内刷新后区域重新可见");
        CrowdUatKit.Step(s, ticks + fog.Period);
        Assert.That(fog.ObsN.All(n => n == 0), Is.True, "遮蔽到期未移除");
        Assert.That(cells.Any(c => Visible(fog, g, c)), Is.True, "遮蔽到期后区域未恢复可见");
        TestContext.Out.WriteLine($"obscure: cells {cells.Length}, ticks {ticks}, visible after expiry {cells.Count(c => Visible(fog, g, c))}");
    }

    [Test]
    public void Forget_ClearsAreaKnowledgeButKeepsStraddlers()
    {
        using var u = Open();
        var s = u.Session;
        var fog = u.Fog;
        int g = Group(s, P0);
        var r = u.R;
        int cy = (int)((r[1] + r[3]) / 2);
        int id = Place(s, Building, (int)((r[0] + r[2]) / 2), cy, u.Size);
        int edge = Place(s, Building, (int)r[0], cy, u.Size);
        Reveal(s, P0, "rect", Rect(r[0], r[1], r[2], r[3]));
        Settle(s);
        var rectCells = RectCells(fog, r[0], r[1], r[2], r[3]);
        Assert.That(fog.Belief[g].ContainsKey(id), Is.True, "揭示后未认知到区域内实体（用例前提不成立）");
        Assert.That(fog.Belief[g].ContainsKey(edge), Is.True, "揭示后未认知到跨边界实体（用例前提不成立）");
        Assert.That(FootprintCells(s, edge).All(rectCells.Contains), Is.False, "跨边界实体未跨出矩形（用例前提不成立）");
        int slot1 = s.Belief!.Slot[g];
        var want = TilesIn(fog, r[0], r[1], r[2], r[3]);

        Forget(s, P0, "rect", Rect(r[0], r[1], r[2], r[3]));
        Assert.That(rectCells.Any(c => Explored(fog, g, c)), Is.False, "遗忘后迷雾格仍为已探索");
        int kept = want.Count(t => fog.Tiles[g].Contains(t));
        Assert.That(kept, Is.Zero, $"遗忘后仍保留 {kept}/{want.Length} 个认知 tile");
        var un = new HashSet<int>(fog.Unexplored(g) ?? new List<int>());
        Assert.That(want.All(un.Contains), Is.True, "遗忘区域未回到乐观可通行假设");
        Assert.That(fog.Belief[g].ContainsKey(id), Is.False, "遗忘后仍认知区域内实体");
        Assert.That(fog.Belief[g].ContainsKey(edge), Is.True, "遗忘误删了跨出矩形的实体（应保留）");
        Settle(s);
        Assert.That(s.Belief.Slot[g], Is.Not.EqualTo(slot1), "遗忘后认知变体未重建（槽位不变）");
        TestContext.Out.WriteLine($"forget: tiles {want.Length}, slot {slot1} → {s.Belief.Slot[g]}");
    }

    [Test]
    public void FogShare_MergesBothWaysAndMovesPlayerOntoTheMergedBelief()
    {
        using var u = Open();
        var s = u.Session;
        var fog = u.Fog;
        int a = Group(s, P0), b = Group(s, P1);
        Assert.That(a, Is.Not.EqualTo(b), "玩家 0 / 1 默认已共享视野（用例前提不成立）");
        var (b1x, b1y) = Base(s, P1);
        CrowdUatKit.SpawnAt(s, Fix64.FromInt(b1x), Fix64.FromInt(b1y), 10, rIdx: 0, player: P1);
        var r = u.R;
        int id = Place(s, Building, (int)((r[0] + r[2]) / 2), (int)((r[1] + r[3]) / 2), u.Size);
        Reveal(s, P0, "rect", Rect(r[0], r[1], r[2], r[3]));
        Settle(s);
        int w = u.W;
        double[] rb = { w * 0.03, w * 0.86, w * 0.14, w * 0.97 };
        var cellsB = RectCells(fog, rb[0], rb[1], rb[2], rb[3]);
        Reveal(s, P1, "rect", Rect(rb[0], rb[1], rb[2], rb[3]));
        Assert.That(cellsB.All(c => Explored(fog, a, c)), Is.False, "合并前玩家 0 已探索区域 B（用例前提不成立）");
        uint keyB = fog.BeliefKey(b);
        var cells = RectCells(fog, r[0], r[1], r[2], r[3]);

        int to = FogShare(s, P1, P0);
        int g0 = Group(s, P0);
        Assert.That(cellsB.All(c => Explored(fog, g0, c)), Is.True, "共享后玩家 0 未获得区域 B 的探索（并集方向性）");
        int g1 = Group(s, P1);
        Assert.That(to == System.Math.Min(a, b) && g1 == g0, Is.True, $"共享后玩家 1 视野组 {g1}，应与玩家 0 相同");
        Assert.That(cells.All(c => Explored(fog, g1, c)), Is.True, "共享后玩家 1 未获得区域 A 的探索");
        Assert.That(fog.Belief[g1].ContainsKey(id), Is.True, "共享后玩家 1 未获得区域 A 的实体认知");
        Assert.That(fog.BeliefKey(g1), Is.Not.EqualTo(keyB), "共享后玩家 1 的认知键未变化");
        Settle(s);
        int empty = System.Math.Max(a, b);
        Assert.That(s.Belief!.Slot[empty], Is.Zero, "合并后空视野组仍占用认知槽位");
        int p1Index = s.Config.PairRules.IndexByPlayerId[P1];
        foreach (var grp in s.Groups.Groups)
        {
            if (grp == null || grp.Player != p1Index) continue;
            Assert.That(grp.NavId, Is.EqualTo(s.NavIdFor(p1Index, grp.LayerIdx, grp.RIdx)), "玩家 1 的导航组未切换到合并后的认知");
        }

        TestContext.Out.WriteLine($"fogShare: groups {a}+{b} → {to}, slot[{empty}] {s.Belief.Slot[empty]}, slot[{to}] {s.Belief.Slot[to]}");
    }

    [Test]
    public void Shapes_TakeCellsByCentreAndRejectBadAreas()
    {
        using var u = Open();
        var s = u.Session;
        var fog = u.Fog;
        int g = Group(s, P0);
        int f = fog.FogCellsPerSide, fcs = fog.FogCellSizeCm, w = u.W;
        bool[] Snap() => Enumerable.Range(0, f * f).Select(c => Explored(fog, g, c)).ToArray();
        int[] Newly(bool[] before) => Enumerable.Range(0, f * f).Where(c => Explored(fog, g, c) && !before[c]).ToArray();

        double cx = w * 0.9, cy = w * 0.9, r = fcs * 3.3;
        var before = Snap();
        Reveal(s, P0, "circle", Circle(cx, cy, r));
        var disc = CentreCells(fog, (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r).Where(c => !before[c]).ToArray();
        var gotDisc = Newly(before);
        Assert.That(disc.Length > 0 && gotDisc.SequenceEqual(disc), Is.True, $"圆形揭示格集不符：得 {gotDisc.Length}，应 {disc.Length}");

        double[][] tri = { new[] { w * 0.05, w * 0.86 }, new[] { w * 0.14, w * 0.86 }, new[] { w * 0.05, w * 0.97 } };
        before = Snap();
        Reveal(s, P0, "poly", Poly(tri));
        var want = CentreCells(fog, PnPoly(tri)).Where(c => !before[c]).ToArray();
        var gotTri = Newly(before);
        Assert.That(want.Length > 0 && gotTri.SequenceEqual(want), Is.True, $"多边形揭示格集不符：得 {gotTri.Length}，应 {want.Length}");

        double px = w * 0.5 + fcs * 0.3, py = w * 0.08 + fcs * 0.3;
        int anchor = (int)System.Math.Floor(py / fcs) * f + (int)System.Math.Floor(px / fcs);
        Reveal(s, P0, "circle", Circle(px, py, fcs * 0.1));
        Assert.That(Explored(fog, g, anchor), Is.True, "小于一格的圆未覆盖其所在格");

        Forget(s, P0, "circle", Circle(cx, cy, r));
        Assert.That(disc.Any(c => Explored(fog, g, c)), Is.False, "圆形遗忘后仍有已探索格");
        Assert.That(want.All(c => Explored(fog, g, c)), Is.True, "圆形遗忘误清了多边形区域");

        int bx = u.Bx, by = u.By;
        var od = CentreCells(fog, (x, y) => (x - bx) * (double)(x - bx) + (y - by) * (double)(y - by) <= fcs * 2.0 * fcs * 2.0);
        Obscure(s, P0, "circle", Circle(bx, by, fcs * 2.0), fog.Period * 3);
        CrowdUatKit.Step(s, fog.Period * 2);
        Assert.That(od.Any(c => Visible(fog, g, c)), Is.False, "圆形遮蔽期内区域可见");

        var bad = new (string Name, System.Action<System.Text.Json.Nodes.JsonObject> Fill)[]
        {
            ("circle r=0", o => o["circle"] = Circle(100, 100, 0)),
            ("poly 两点", o => o["poly"] = Poly(new[] { 0.0, 0.0 }, new[] { 100.0, 100.0 })),
            ("rect + circle", o => { o["rect"] = Rect(0, 0, 100, 100); o["circle"] = Circle(100, 100, 100); }),
            ("无区域", _ => { }),
        };
        foreach (var (name, fill) in bad)
        {
            var cmd = new System.Text.Json.Nodes.JsonObject { ["type"] = "reveal", ["player"] = P0 };
            fill(cmd);
            Assert.Throws<System.InvalidOperationException>(() => Fog(s, cmd), $"非法区域未被拒绝：{name}");
        }

        TestContext.Out.WriteLine($"shapes: disc {disc.Length}, triangle {want.Length}, obscure disc {od.Length}");
    }

    [Test]
    public void FogCommandStream_IsDeterministicAcrossRuns()
    {
        var x = RunStream();
        var y = RunStream();
        Assert.That(y.Checksum, Is.EqualTo(x.Checksum), $"checksum 不一致 {x.Checksum} vs {y.Checksum}");
        var diff = new List<string>();
        CrowdSimPartitions.CollectDifferences(x.Partitions, y.Partitions, diff);
        Assert.That(diff, Is.Empty, $"分区摘要不一致：{string.Join(", ", diff)}");
        TestContext.Out.WriteLine($"determinism: checksum {x.Checksum:X16}, partitions {x.Partitions.Count}");
    }

    private static (ulong Checksum, List<CrowdSimPartition> Partitions) RunStream()
    {
        using var u = Open();
        var s = u.Session;
        var r = u.R;
        double d = u.Fog.FogCellSizeCm * 2.0;
        Reveal(s, P0, "rect", Rect(r[0], r[1], r[2], r[3]));
        CrowdUatKit.Step(s, 5);
        Obscure(s, P0, "rect", Rect(u.Bx - d, u.By - d, u.Bx + d, u.By + d), 20);
        CrowdUatKit.Step(s, 5);
        Forget(s, P0, "rect", Rect(r[0], r[1], r[2], r[3]));
        CrowdUatKit.Step(s, 5);
        Reveal(s, P0, "poly", Poly(new[] { r[0], r[1] }, new[] { r[2], r[1] }, new[] { r[0], r[3] }));
        Obscure(s, P0, "circle", Circle(u.Bx, u.By, d), 15);
        CrowdUatKit.Step(s, 5);
        FogShare(s, P1, P0);
        CrowdUatKit.Step(s, 60);
        var partitions = new List<CrowdSimPartition>();
        CrowdSimPartitions.Copy(s, partitions);
        return (s.LastDigest.Total, partitions);
    }
}
