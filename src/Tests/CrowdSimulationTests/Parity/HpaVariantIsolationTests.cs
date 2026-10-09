using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 认知变体与真相 HPA 的隔离门:CloneForVariant 之后内层数组/簇块/LinkOut 的 List 与真相
/// 共用,唯一合法改写是整体替换变体自有的外层槽位。门走真实变体路径(注册认知槽 → 懒建
/// 全部变体触发分歧矩形重烘 → 原位揭示再重烘),在三个时点对真相各 HPA 做逐位摘要,
/// 断言全程不变;同时断言变体确实分叉,防止隔离门空转。
/// </summary>
public class HpaVariantIsolationTests
{
    [Test]
    public void VariantRebake_LeavesTruthHpaBitIdentical()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));

        var before = TruthDigests(session);

        // 认知槽 1:believed 实体为空(真相每个实体都是分歧)+ 前一半 tile 未探索(乐观假设)
        int clustersPerSide = session.Navs.Values.First().Hpa!.ClustersPerSide;
        var unknown = Enumerable.Range(0, clustersPerSide * clustersPerSide / 2).ToList();
        session.Beliefs!.Register(1, Array.Empty<(int, int, CrowdStructureFootprint)>(), unknown);

        var variants = session.Navs.Keys.OrderBy(v => v)
            .ToDictionary(id => id, id => session.ResolveNavContext(CrowdBeliefNavs.BeliefStride + id));
        Assert.That(TruthDigests(session), Is.EqualTo(before), "懒建变体(分歧矩形重烘)后真相 HPA 摘要变化");

        // 原位揭示一半未探索 tile:已建变体再次进入重烘路径
        session.Beliefs!.Reveal(1, unknown.Take(unknown.Count / 2).ToList());
        Assert.That(TruthDigests(session), Is.EqualTo(before), "原位揭示重烘后真相 HPA 摘要变化");

        int diverged = variants.Count(kv => HpaDigest(kv.Value.Hpa!) != before[kv.Key]);
        Assert.That(diverged, Is.GreaterThan(0), "全部变体 HPA 与真相同摘要——重烘未产生分歧,隔离门空转");
        TestContext.Out.WriteLine($"真相 {before.Count} 个 HPA 摘要全程逐位不变;{diverged}/{variants.Count} 个变体确认分叉");
    }

    private static Dictionary<int, string> TruthDigests(CrowdSimSession session) =>
        session.Navs.ToDictionary(kv => kv.Key, kv => HpaDigest(kv.Value.Hpa!));

    /// <summary>HPA 全量逐位摘要:标量 + 逐簇内层数组/代价 + 簇块(含 Index 迭代序)+ LinkOut/
    /// LinkClusters 键序与内容。同对象未被改写则摘要必然相同;任何共享内层的原地写都会现形。</summary>
    private static string HpaDigest(HpaGraph g)
    {
        var b = new List<byte>();
        void I32(int v) => b.AddRange(BitConverter.GetBytes(v));
        void I64(long v) => b.AddRange(BitConverter.GetBytes(v));
        void Arr(int[] a)
        {
            I32(a.Length);
            foreach (var v in a) I32(v);
        }

        void ArrF(Fix64[] a)
        {
            I32(a.Length);
            foreach (var v in a) I64(v.RawValue);
        }

        I32(g.ClusterSize); I32(g.ClustersPerSide); I32(g.CellCount2); I32(g.MaxEntranceWidth);
        I32(g.NodeCount); I32(g.EdgeCount);
        int cc = g.ClustersPerSide * g.ClustersPerSide;
        for (int cl = 0; cl < cc; cl++)
        {
            Arr(g.BorderE[cl]); Arr(g.BorderS[cl]);
            Arr(g.UpE[cl]); ArrF(g.UpECost[cl]); Arr(g.UpS[cl]); ArrF(g.UpSCost[cl]);
            Arr(g.Cells[cl]); Arr(g.Intra[cl]); ArrF(g.IntraDist[cl]); Arr(g.CrossLayer[cl]);
            var block = g.Blocks[cl];
            Arr(block.Cells); Arr(block.AdjStart); Arr(block.AdjTo); ArrF(block.AdjCost); I32(block.EdgeCount);
            I32(block.Index.Count);
            foreach (var kv in block.Index)
            {
                I32(kv.Key); I32(kv.Value);
            }
        }

        I32(g.LinkOut.Count);
        foreach (var kv in g.LinkOut.OrderBy(k => k.Key))
        {
            I32(kv.Key); I32(kv.Value.Count);
            foreach (var (to, cost) in kv.Value)
            {
                I32(to); I64(cost.RawValue);
            }
        }

        if (g.LinkClusters == null)
        {
            I32(-1);
        }
        else
        {
            I32(g.LinkClusters.Count);
            foreach (var kv in g.LinkClusters.OrderBy(k => k.Key))
            {
                I32(kv.Key); I32(kv.Value.Count);
                foreach (var v in kv.Value) I32(v);
            }
        }

        return S2NavTruthTests.Fnv(b.ToArray());
    }
}
