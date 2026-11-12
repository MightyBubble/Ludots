using System;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

public sealed class CrowdDeploymentAllocTests
{
    [Test]
    public void RepeatedSpawnCycle_ReusesCenterListsAndAllocatesNothing()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        int players = session.Config.Deploy.Bases.Count;
        var buffers = session.DeploymentBuffers;
        RunCycle(buffers, players);
        int pooled = buffers.PooledCenterLists;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        RunCycle(buffers, players);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"second spawn cycle allocated {delta} bytes, pooled {buffers.PooledCenterLists}");
        Assert.That(delta, Is.EqualTo(0), $"重复部署分配了 {delta} 字节");

        for (int i = 0; i < 8; i++) RunCycle(buffers, players);
        Assert.That(buffers.PooledCenterLists, Is.EqualTo(pooled), "中心列表池随部署次数增长");
    }

    [Test]
    public void BeginSpawn_ReclaimsListsThatNeverEnteredCenters()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        var buffers = session.DeploymentBuffers;
        int players = session.Config.Deploy.Bases.Count;
        buffers.BeginSpawn(players);
        var discarded = buffers.RentCenterList();
        discarded.Add(7);
        buffers.BeginSpawn(players);
        var reused = buffers.RentCenterList();
        Assert.That(reused, Is.SameAs(discarded), "未进 Centers 的列表也要在下次部署收回复用");
        Assert.That(reused, Is.Empty);
        Assert.That(buffers.PooledCenterLists, Is.EqualTo(1));
    }

    private static void RunCycle(CrowdDeploymentBuffers buffers, int players)
    {
        buffers.BeginSpawn(players);
        for (int g = 0; g < 4; g++)
        {
            var list = buffers.RentCenterList();
            list.Add(g);
            buffers.Centers[g] = list;
            buffers.TypeOf[g] = 0;
        }

        buffers.RentCenterList().Add(99);
    }
}
