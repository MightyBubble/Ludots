using System.Text.Json.Nodes;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>仿真停留、领队和增量导航不变量。关闭时步进不扫描。</summary>
public sealed class CrowdSimulationValidatorTests
{
    [Test]
    public void DebugSwitch_DefaultsOff_AndRejectsNegative()
    {
        var absent = CrowdSimulationDebugConfig.Load(JsonNode.Parse("""{"session":{}}""")!.AsObject());
        Assert.That(absent.Session!.ValidateEveryNTicks, Is.EqualTo(0));

        var zero = CrowdSimulationDebugConfig.Load(
            JsonNode.Parse("""{"session":{"validateEveryNTicks":0}}""")!.AsObject());
        Assert.That(zero.Session!.ValidateEveryNTicks, Is.EqualTo(0));

        var on = CrowdSimulationDebugConfig.Load(
            JsonNode.Parse("""{"session":{"validateEveryNTicks":12}}""")!.AsObject());
        Assert.That(on.Session!.ValidateEveryNTicks, Is.EqualTo(12));

        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(
            JsonNode.Parse("""{"session":{"validateEveryNTicks":-1}}""")!.AsObject()));
        Assert.That(ex!.Message, Does.Contain("validateEveryNTicks"));
    }

    [Test]
    public void ValidateSim_BlockedCell_NamesUnitAndCell()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(session.ValidateEveryNTicks, Is.EqualTo(0));
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSim(session));

        var blocked = session.Structures!.Blocked;
        int n = session.Config.NavCellsPerSide;
        int cell = IndexOf(blocked, 1);
        Assert.That(cell, Is.GreaterThanOrEqualTo(0), "种子图上应有阻挡格");
        int cx = cell % n, cy = cell / n;
        var entity = session.Units.EntityAt(0);
        var state = session.World.Get<CrowdSimulationUnitState>(entity);
        PlaceAtCell(session, entity, cx, cy);

        var ex = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        TestContext.Out.WriteLine(ex!.Message);
        Assert.That(ex.Message, Does.Contain($"单位槽 {state.Slot}"));
        Assert.That(ex.Message, Does.Contain($"实体 {entity.Id}"));
        Assert.That(ex.Message, Does.Contain($"停在阻挡格 ({cx},{cy})"));
        Assert.That(ex.Tick, Is.EqualTo(0));
        Assert.That(ex.UnitSlot, Is.EqualTo(state.Slot));
        Assert.That(ex.EntityId, Is.EqualTo(entity.Id));
        Assert.That(ex.CellX, Is.EqualTo(cx));
        Assert.That(ex.CellY, Is.EqualTo(cy));
    }

    [Test]
    public void ValidateSim_DeckOffUpPass_NamesCell()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        var entity = session.Units.EntityAt(0);
        var state = session.World.Get<CrowdSimulationUnitState>(entity);
        var group = session.Groups.Groups[state.GroupId]!;
        var nav = session.Navs[group.BodyNavId];
        int n = session.Config.NavCellsPerSide;
        int cell = IndexOf(nav.UpPass, 0);
        Assert.That(cell, Is.GreaterThanOrEqualTo(0), "桥面可通行格不是满图");
        int cx = cell % n, cy = cell / n;
        PlaceAtCell(session, entity, cx, cy);
        state.Level = 1;
        session.World.Set(entity, state);

        var ex = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        TestContext.Out.WriteLine(ex!.Message);
        Assert.That(ex.Message, Does.Contain($"单位槽 {state.Slot}"));
        Assert.That(ex.Message, Does.Contain($"实体 {entity.Id}"));
        Assert.That(ex.Message, Does.Contain($"停在桥面不可通行格 ({cx},{cy})"));
        Assert.That(ex.CellX, Is.EqualTo(cx));
        Assert.That(ex.CellY, Is.EqualTo(cy));
    }

    [Test]
    public void ValidateSim_FlowWithoutLeader_NamesGroup()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        var state = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(0));
        var group = session.Groups.Groups[state.GroupId]!;
        Assert.That(group.Leader, Is.Null);
        group.Flow = new FlowField
        {
            Integ = Array.Empty<Fix64>(),
            Len = Array.Empty<Fix64>(),
            Wp = Array.Empty<int>(),
        };

        var ex = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        TestContext.Out.WriteLine(ex!.Message);
        Assert.That(ex.Message, Does.Contain($"tick {session.TickCount} 组 {group.Id} 有流场但没有领队"));
        Assert.That(ex.GroupId, Is.EqualTo(group.Id));
        Assert.That(ex.Tick, Is.EqualTo(session.TickCount));
    }

    [Test]
    public void ValidateNav_CorruptCost_NamesNavAndTile()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var nav = FirstNav(session);
        const int cell = 0;
        nav.Cost[cell] += Fix64.OneValue;

        Assert.That(session.ValidateEveryNTicks, Is.EqualTo(0));
        Assert.DoesNotThrow(() => Assert.That(session.Step(out _), Is.True));

        var ex = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateNav(session));
        TestContext.Out.WriteLine(ex!.Message);
        Assert.That(ex.NavId, Is.EqualTo(nav.Id));
        Assert.That(ex.Tile, Is.EqualTo(0));
        Assert.That(ex.CellX, Is.EqualTo(0));
        Assert.That(ex.CellY, Is.EqualTo(0));
        Assert.That(ex.Tick, Is.EqualTo(session.TickCount));
        Assert.That(ex.Message, Does.Contain($"nav {nav.Id}"));
        Assert.That(ex.Message, Does.Contain("tile 0:"));
        Assert.That(ex.Message, Does.Contain("(0,0)"));

        session.ValidateEveryNTicks = 1;
        var stepped = Assert.Throws<CrowdSimulationInvariantException>(() => session.Step(out _));
        TestContext.Out.WriteLine(stepped!.Message);
        Assert.That(stepped.Tick, Is.EqualTo(session.TickCount));
        Assert.That(stepped.Message, Does.Contain($"nav {nav.Id}"));
        Assert.That(stepped.Message, Does.Contain("tile 0:"));
    }

    [Test]
    public void StructureScript_ValidationEveryNTicks_BlockedRestsStayZero()
    {
        const int ticks = 160;
        const int every = 80;
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true;
        session.ValidateEveryNTicks = every;

        (int Tick, string Json)[] entries =
        {
            (0, """{"type":"spawnAt","xCm":480000,"yCm":560000,"count":120,"player":1,"unitType":0,"rIdx":0}"""),
            (2, """{"type":"order","xCm":1200000,"yCm":800000,"player":1,"shape":"box"}"""),
            (30, """{"type":"placeStructure","template":"building","xCm":503125,"yCm":565625,"sizeCm":14000}"""),
            (60, """{"type":"removeStructureAt","xCm":503125,"yCm":565625}"""),
            (90, """{"type":"placeStructure","template":"s7barrier","xCm":521875,"yCm":571875,"sizeCm":7000}"""),
            (120, """{"type":"placeStructure","template":"road","xCm":496875,"yCm":565625,"sizeCm":3000,"toXCm":521875,"toYCm":565625}"""),
        };
        var script = new List<CrowdCommand>();
        foreach (var (tick, json) in entries)
        {
            script.Add(new CrowdCommand(tick, CrowdSimCommand.Parse(JsonNode.Parse(json)!)));
        }

        session.Commands.Schedule(session, script);
        int dwell = 0;
        int guard = ticks * CrowdSimulationLimits.AdvanceStallGuardFactor + CrowdSimulationLimits.AdvanceStallGuardFactor;
        int ran = 0;
        while (ran < ticks)
        {
            if (guard-- <= 0) Assert.Fail("会话推进停摆过长");
            if (!session.Step(out _)) continue;
            ran++;
            dwell += CrowdSimulationValidator.CountBlockedRests(session);
        }

        TestContext.Out.WriteLine($"ticks={session.TickCount} validateEveryNTicks={every} blockedRests={dwell}");
        Assert.That(session.TickCount, Is.EqualTo(ticks));
        Assert.That(session.TickCount % every, Is.EqualTo(0));
        Assert.That(dwell, Is.EqualTo(0));
    }

    private static NavContext FirstNav(CrowdSimSession session)
    {
        foreach (var nav in session.Navs.Values) return nav;
        throw new InvalidOperationException("会话没有导航。");
    }

    private static int IndexOf(byte[] grid, byte value)
    {
        for (int i = 0; i < grid.Length; i++)
        {
            if (grid[i] == value) return i;
        }

        return -1;
    }

    private static void PlaceAtCell(CrowdSimSession session, Arch.Core.Entity entity, int cx, int cy)
    {
        int size = session.Config.NavCellSizeCm;
        session.World.Set(entity, new WorldPositionCm
        {
            Value = new Fix64Vec2(Fix64.FromInt(cx * size + size / 2), Fix64.FromInt(cy * size + size / 2)),
        });
    }
}
