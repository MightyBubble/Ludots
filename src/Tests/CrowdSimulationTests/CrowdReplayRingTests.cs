using System.Reflection;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Tests;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

/// <summary>校验码环转满以后，回放仍按 tick 对上环里还在的记录。</summary>
public sealed class CrowdReplayRingTests
{
    [Test]
    public void ReplayPastChecksumRing_StaysConsistent()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s7");
        var host = new CrowdSimulationRuntime();
        Bind(host, session);

        int capacity = HashTicks(host).Length;
        int ticks = capacity + 1;
        for (int i = 0; i < ticks; i++) host.Tick();

        Assert.That(session.TickCount, Is.EqualTo(ticks));
        Assert.That(HashCount(host), Is.EqualTo(capacity));
        Assert.That(HashHead(host), Is.EqualTo(1));

        host.RunReplay();
        Assert.That(host.ReplayStatus, Is.EqualTo(1));
        Assert.That(host.ReplayDivergenceTick, Is.EqualTo(-1));
    }

    private static void Bind(CrowdSimulationRuntime host, CrowdSimSession session)
    {
        var relations = TeamRelationTestHarness.Create(session.World);
        var rules = CrowdPairRules.Matrices(1, new CrowdPushResponse[1], new bool[1]);
        var guard = new CrowdPairRulesSessionGuard(
            relations.Query,
            session.World,
            Array.Empty<Entity>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            relations.HostileTypeId,
            relations.FriendlyTypeId,
            rules,
            relations.Query.Revision);
        Set(host, "_session", session);
        Set(host, "_pairRulesGuard", guard);
    }

    private static void Set(CrowdSimulationRuntime host, string field, object value)
        => typeof(CrowdSimulationRuntime).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);

    private static int[] HashTicks(CrowdSimulationRuntime host)
        => (int[])typeof(CrowdSimulationRuntime).GetField("_hashTicks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    private static int HashCount(CrowdSimulationRuntime host)
        => (int)typeof(CrowdSimulationRuntime).GetField("_hashCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    private static int HashHead(CrowdSimulationRuntime host)
        => (int)typeof(CrowdSimulationRuntime).GetField("_hashHead", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
}
