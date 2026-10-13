using System.Collections.Generic;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

/// <summary>随机操作的句柄探测、保留项，以及放置后的栅格和导航对照。</summary>
public sealed class CrowdFuzzRunnerTests
{
    [Test]
    public void CheckHandles_SurvivorsMatchAndDeadResolveMissing()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s7");
        var rng = CrowdSimRng.Create(1);
        Assert.That(CrowdFuzzRunner.Snapshot(session, 32, rng), Is.Empty);

        Spawn(session, 4);
        var live = CrowdFuzzRunner.Snapshot(session, 32, rng);
        Assert.That(live, Has.Length.EqualTo(4));
        Assert.That(CrowdFuzzRunner.CheckHandles(session, live), Is.Null);

        var mismatched = live.ToArray();
        mismatched[0] = mismatched[0] with { EntityId = mismatched[0].EntityId + 99 };
        Assert.That(CrowdFuzzRunner.CheckHandles(session, mismatched), Does.Contain("解析到实体"));

        var beforeClear = CrowdFuzzRunner.Snapshot(session, 32, rng);
        session.Units.Clear();
        Assert.That(CrowdFuzzRunner.CheckHandles(session, beforeClear), Is.Null);
        Assert.That(session.Units.TryResolveHandle(beforeClear[0].Handle, out int dense), Is.False);
        Assert.That(dense, Is.EqualTo(-1));
    }

    [Test]
    public void Runner_SpawnAndOrder_PassesSim()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s7");
        var result = CrowdFuzzRunner.Run(session, Plan(seed: 3, opCount: 4, spawn, order));
        Assert.That(result.Passed, Is.True, result.Error);
        Assert.That(session.Units.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Runner_ReservedKill_IsListedAndNotExecuted()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s7");
        var kill = new CrowdFuzzOp { Id = "kill", ProbeHandles = true, Reserved = true };
        var result = CrowdFuzzRunner.Run(session, Plan(seed: 9, opCount: 3, spawn, kill));
        Assert.That(result.Passed, Is.True, result.Error);
        Assert.That(result.Reserved, Does.Contain("kill"));
        Assert.That(session.Units.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Runner_PluggedClear_ProbesDeadHandles()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s7");
        Spawn(session, 6);
        var kill = new CrowdFuzzOp { Id = "kill", ProbeHandles = true, Reserved = true };
        var extra = new Dictionary<string, CrowdFuzzOpExecutor>
        {
            ["kill"] = (sim, _, _) => sim.Units.Clear(),
        };
        var result = CrowdFuzzRunner.Run(session, Plan(seed: 4, opCount: 1, kill), extra);
        Assert.That(result.Passed, Is.True, result.Error);
        Assert.That(result.Reserved, Is.Empty);
        Assert.That(session.Units.Count, Is.EqualTo(0));
    }

    [Test]
    public void Runner_PlaceAndRemove_MatchesStructuresAndNav()
    {
        using var world = CrowdBeliefTerrainTests.LandWorld.Open();
        world.Session.TruthNavFrozen = true;
        var place = new CrowdFuzzOp { Id = "place", Structure = true };
        var remove = new CrowdFuzzOp { Id = "remove", Structure = true };
        var result = CrowdFuzzRunner.Run(world.Session, Plan(seed: 5, opCount: 2, place, remove));
        Assert.That(result.Passed, Is.True, result.Error);
    }

    private static readonly CrowdFuzzOp spawn = new() { Id = "spawn" };
    private static readonly CrowdFuzzOp order = new() { Id = "order" };

    private static CrowdFuzzPlan Plan(long seed, int opCount, params CrowdFuzzOp[] ops) => new()
    {
        Seed = seed,
        OpCount = opCount,
        StepsPerOp = 2,
        ProbeHandleCount = 32,
        SpawnCount = 4,
        PlaceTemplate = "building",
        PlaceSizeCm = 14000,
        OrderShape = "box",
        OrderMemberCap = 4,
        StallGuard = CrowdSimulationLimits.AdvanceStallGuardFactor,
        Ops = ops,
    };

    private static void Spawn(CrowdSimSession session, int count)
    {
        var basis = session.Config.Deploy.Bases[0];
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = basis.PlayerId,
            XCm = basis.XCm,
            YCm = basis.YCm,
            Count = count,
            UnitType = 0,
            RIdx = 0,
        }, CrowdSimCommands.Exec);
    }
}
