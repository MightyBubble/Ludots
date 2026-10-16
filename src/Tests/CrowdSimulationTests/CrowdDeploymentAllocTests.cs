using System;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

public sealed class CrowdDeploymentAllocTests
{
    [Test]
    public void RepeatedBeginSpawn_SecondCallAllocatesNothing()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        int players = session.Config.Deploy.Bases.Count;
        var buffers = session.DeploymentBuffers;
        buffers.BeginSpawn(players);
        buffers.Centers[1] = buffers.RentCenterList();
        buffers.Centers[1].Add(42);
        buffers.TypeOf[1] = 0;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        buffers.BeginSpawn(players);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"second BeginSpawn allocated {delta} bytes");
        Assert.That(delta, Is.EqualTo(0), $"重复 BeginSpawn 分配了 {delta} 字节");
    }
}
