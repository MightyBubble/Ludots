using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
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
    public void Runner_ReservedOp_IsListedAndNotExecuted()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s7");
        var held = new CrowdFuzzOp { Id = "hold", ProbeHandles = true, Reserved = true };
        var result = CrowdFuzzRunner.Run(session, Plan(seed: 9, opCount: 3, spawn, held));
        Assert.That(result.Passed, Is.True, result.Error);
        Assert.That(result.Reserved, Does.Contain("hold"));
        Assert.That(session.Units.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Runner_Kill_ProbesHandlesWithoutFreezingPositions()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s7");
        Spawn(session, 6);
        var source = CatalogPlan();
        var kill = source.Ops.Single(op => op.Id == "kill");
        Assert.That(kill.Reserved, Is.False);
        Assert.That(kill.ProbeHandles, Is.True);
        var plan = new CrowdFuzzPlan
        {
            Seed = 4,
            OpCount = 1,
            StepsPerOp = source.StepsPerOp,
            ProbeHandleCount = source.ProbeHandleCount,
            SpawnCount = source.SpawnCount,
            PlaceTemplate = source.PlaceTemplate,
            PlaceSizeCm = source.PlaceSizeCm,
            OrderShape = source.OrderShape,
            OrderMemberCap = source.OrderMemberCap,
            StallGuard = source.StallGuard,
            BoomRadiusCm = source.BoomRadiusCm,
            RingScale = source.RingScale,
            Impulse = source.Impulse,
            Ops = new[] { kill },
        };
        int before = session.Units.Count;
        var result = CrowdFuzzRunner.Run(session, plan);
        Assert.That(result.Passed, Is.True, result.Error);
        Assert.That(result.Reserved, Is.Empty);
        Assert.That(session.Units.Count, Is.LessThan(before));
    }

    [Test]
    [Explicit("seeds 1..10 × 200，不进默认套件")]
    public void Seeds1To10_TwoHundredOps()
    {
        var catalog = CatalogPlan();
        for (long seed = 1; seed <= 10; seed++)
        {
            var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
            using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
            session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
            session.BlockOnDueReplies = true;
            var plan = new CrowdFuzzPlan
            {
                Seed = seed,
                OpCount = 200,
                StepsPerOp = catalog.StepsPerOp,
                ProbeHandleCount = catalog.ProbeHandleCount,
                SpawnCount = catalog.SpawnCount,
                PlaceTemplate = catalog.PlaceTemplate,
                PlaceSizeCm = catalog.PlaceSizeCm,
                OrderShape = catalog.OrderShape,
                OrderMemberCap = catalog.OrderMemberCap,
                StallGuard = catalog.StallGuard,
                PathTemplate = catalog.PathTemplate,
                PathWidthCm = catalog.PathWidthCm,
                PathLengthCm = catalog.PathLengthCm,
                BoomRadiusCm = catalog.BoomRadiusCm,
                RingScale = catalog.RingScale,
                Impulse = catalog.Impulse,
                Ops = catalog.Ops,
            };
            var result = CrowdFuzzRunner.Run(session, plan);
            TestContext.Out.WriteLine($"seed {seed} passed={result.Passed} step={result.Step} op={result.OpId} error={result.Error}");
            Assert.That(result.Passed, Is.True, $"seed {seed} step {result.Step} op {result.OpId}: {result.Error}");
        }
    }

    [Test]
    public void Remove_RestoresTerrainBlockInsideClearedRect()
    {
        using var world = CrowdBeliefTerrainTests.LandWorld.Open();
        int cell = world.Config.NavCellSizeCm;
        int x = cell / 2;
        int y = cell / 2;
        world.Session.Commands.Submit<object?>(world.Session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = "building",
            XCm = x,
            YCm = y,
            SizeCm = cell * 4,
        }, CrowdSimCommands.Exec);
        world.Session.Commands.Submit<object?>(world.Session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.RemoveStructureAt,
            XCm = x,
            YCm = y,
        }, CrowdSimCommands.Exec);
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateStructures(world.Session));
    }

    [Test]
    public void Runner_PlaceAndRemove_MatchesStructuresAndNav()
    {
        using var world = CrowdBeliefTerrainTests.LandWorld.Open(withFog: false);
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
        BoomRadiusCm = 1,
        RingScale = Fix64.FromInt(2),
        Impulse = Fix64.Zero,
        Ops = ops,
    };

    private static CrowdFuzzPlan CatalogPlan()
    {
        string root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        string path = Path.Combine(root, "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/catalog.json");
        return CrowdSimRegressCatalog.Parse(JsonNode.Parse(File.ReadAllText(path))!.AsObject()).CreateFuzzPlan(1, 1);
    }

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
