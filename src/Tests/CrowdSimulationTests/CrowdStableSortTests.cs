using System.Linq;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 替换掉的排序与 LINQ OrderBy 逐元素一致。键可重复,次序必须是输入序。
/// </summary>
public sealed class CrowdStableSortTests
{
    [Test]
    public void StableInt_DuplicateKeys_MatchOrderBy()
    {
        int[] keys = { 3, 1, 3, 2, 1, 3, 2, 1, 3, 1, 3, 2, 1, 3, 2, 1, 4, 2, 1 };
        var expected = Enumerable.Range(0, keys.Length).OrderBy(i => keys[i]).ToArray();
        var ranks = new StableInt[keys.Length];
        for (int i = 0; i < keys.Length; i++) ranks[i] = new StableInt(keys[i], i);
        StableOrder.Sort(ranks, keys.Length);
        var actual = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++) actual[i] = ranks[i].Seq;
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(ranks.Select(r => r.Key).ToArray(), Is.EqualTo(expected.Select(i => keys[i]).ToArray()));
    }

    [Test]
    public void StableLong_DuplicateKeys_MatchOrderBy()
    {
        long[] keys = { 9, 4, 9, 4, 1, 9, 1, 9, 4, 9, 4, 1, 9, 1, 8, 4, 1, 9 };
        var expected = Enumerable.Range(0, keys.Length).OrderBy(i => keys[i]).ToArray();
        var ranks = new StableLong[keys.Length];
        for (int i = 0; i < keys.Length; i++) ranks[i] = new StableLong(keys[i], i);
        StableOrder.Sort(ranks, keys.Length);
        var actual = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++) actual[i] = ranks[i].Seq;
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void FormationMembers_DuplicateDiameterAndForward_MatchOrderBy()
    {
        var diameter = new[] { Fix64.FromInt(2), Fix64.FromInt(2), Fix64.FromInt(1), Fix64.FromInt(2), Fix64.FromInt(1) };
        var forward = new[] { Fix64.FromInt(5), Fix64.FromInt(9), Fix64.FromInt(1), Fix64.FromInt(5), Fix64.FromInt(3) };
        int n = diameter.Length;
        var order = new int[n];
        var ranks = new CrowdFormations.FormationRank[n];
        CrowdFormations.RankMembers(false, diameter, forward, n, ranks, order);
        var expected = Enumerable.Range(0, n).OrderBy(k => diameter[k].RawValue).ThenByDescending(k => forward[k].RawValue).ToArray();
        Assert.That(order, Is.EqualTo(expected));

        CrowdFormations.RankMembers(true, diameter, forward, n, ranks, order);
        var mirrored = Enumerable.Range(0, n).OrderByDescending(k => diameter[k].RawValue).ThenByDescending(k => forward[k].RawValue).ToArray();
        Assert.That(order, Is.EqualTo(mirrored));
    }

    [Test]
    public void FormationRow_DuplicateLateral_KeepsPriorOrder()
    {
        var lateral = new[] { Fix64.FromInt(3), Fix64.FromInt(1), Fix64.FromInt(3), Fix64.FromInt(1), Fix64.FromInt(2) };
        int[] incoming = { 2, 0, 4, 3, 1 };
        var ranks = new CrowdFormations.RowRank[incoming.Length];
        CrowdFormations.RankRow(lateral, incoming, 0, incoming.Length, ranks);
        var expected = Enumerable.Range(0, incoming.Length).OrderBy(i => lateral[incoming[i]].RawValue).Select(i => incoming[i]).ToArray();
        var actual = new int[incoming.Length];
        for (int i = 0; i < incoming.Length; i++) actual[i] = ranks[i].Member;
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void BeliefRank_DuplicateIds_MatchOrderBy()
    {
        var src = new[]
        {
            new BeliefRank(2, 0, 7, default),
            new BeliefRank(1, 1, 3, default),
            new BeliefRank(2, 2, 9, default),
            new BeliefRank(1, 3, 4, default),
            new BeliefRank(2, 4, 1, default),
        };
        var expected = src.OrderBy(r => r.Id).ThenBy(r => r.Seq).Select(r => r.TplIndex).ToArray();
        var actualRanks = (BeliefRank[])src.Clone();
        Array.Sort(actualRanks);
        var actual = actualRanks.Select(r => r.TplIndex).ToArray();
        Assert.That(actual, Is.EqualTo(expected));
    }
}
