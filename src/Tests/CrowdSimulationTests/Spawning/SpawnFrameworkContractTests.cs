using System;
using Ludots.Core.Gameplay.Spawning;
using NUnit.Framework;

namespace CrowdSimulationTests.Spawning;

[TestFixture]
public class SpawnFrameworkContractTests
{
    [Test]
    public void EnqueueAllOrThrow_RejectsWithoutWriting()
    {
        var queue = new RuntimeEntitySpawnQueue(16);
        var requests = new RuntimeEntitySpawnRequest[17];
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => queue.EnqueueAllOrThrow(requests))!;
        Assert.That(ex.Message, Does.Contain("SPAWN.RUNTIME.ERR.QueueCapacity"));
        Assert.That(queue.Count, Is.EqualTo(0));
    }
}
