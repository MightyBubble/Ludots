using System.Reflection;
using System.Text.Json.Nodes;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.Gameplay.Components;
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
    public void DebugSwitch_RejectsTickValidationKey()
    {
        var absent = CrowdSimulationDebugConfig.Load(JsonNode.Parse("""{"session":{}}""")!.AsObject());
        Assert.That(absent.Session, Is.Not.Null);

        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(
            JsonNode.Parse("""{"session":{"validateEveryNTicks":1}}""")!.AsObject()));
        Assert.That(ex!.Message, Does.Contain("validateEveryNTicks"));
    }

    [Test]
    public void ValidateSim_BlockedCell_NamesUnitAndCell()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
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
        Assert.That(ex.Message, Does.Contain($"停在上层不可通行格 ({cx},{cy})"));
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

        Assert.DoesNotThrow(() => Assert.That(session.Step(out _), Is.True));
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
    }

    [Test]
    public void StructureScript_BlockedRestsStayZero()
    {
        const int ticks = 160;
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.Presentation.TruthNavFrozen = true;

        var scriptJson = new JsonArray();
        foreach (var (tick, json) in new (int Tick, string Json)[]
        {
            (0, """{"type":"spawnAt","xCm":480000,"yCm":560000,"count":120,"player":1,"template":"crowd_simulation.unit.infantry.r100"}"""),
            (2, """{"type":"order","xCm":1200000,"yCm":800000,"player":1,"shape":"box"}"""),
            (30, """{"type":"placeStructure","template":"building","xCm":503125,"yCm":565625,"sizeCm":14000}"""),
            (60, """{"type":"removeStructureAt","xCm":503125,"yCm":565625}"""),
            (90, """{"type":"placeStructure","template":"s7barrier","xCm":521875,"yCm":571875,"sizeCm":7000}"""),
            (120, """{"type":"placeStructure","template":"road","xCm":496875,"yCm":565625,"sizeCm":3000,"toXCm":521875,"toYCm":565625}"""),
        })
        {
            scriptJson.Add(new JsonObject { ["tick"] = tick, ["cmd"] = JsonNode.Parse(json) });
        }

        var script = CrowdReferenceScript.Load(scriptJson, session.Units.Capacity);
        int dwell = 0;
        int guard = ticks * CrowdSimulationLimits.AdvanceStallGuardFactor + CrowdSimulationLimits.AdvanceStallGuardFactor;
        int ran = 0;
        while (ran < ticks)
        {
            if (guard-- <= 0) Assert.Fail("会话推进停摆过长");
            if (!script.Step(session, out _)) continue;
            ran++;
            dwell += CrowdSimulationValidator.CountBlockedRests(session);
        }

        TestContext.Out.WriteLine($"ticks={session.TickCount} blockedRests={dwell}");
        Assert.That(session.TickCount, Is.EqualTo(ticks));
        Assert.That(dwell, Is.EqualTo(0));
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSim(session));
    }

    [Test]
    public void ValidateSim_JumpOnBlockedCell_IsAllowed()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        var entity = session.Units.EntityAt(0);
        int n = session.Config.NavCellsPerSide;
        int cell = IndexOf(session.Structures!.Blocked, 1);
        PlaceAtCell(session, entity, cell % n, cell / n);
        var state = session.World.Get<CrowdSimulationUnitState>(entity);
        state.State = (byte)CrowdUnitState.Traversing;
        session.World.Set(entity, state);
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSim(session));
    }

    [Test]
    public void ValidateSim_OutOfBounds_NamesCell()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        var entity = session.Units.EntityAt(0);
        session.World.Set(entity, new WorldPositionCm { Value = new Fix64Vec2(Fix64.FromInt(-10), Fix64.FromInt(10)) });
        var ex = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        Assert.That(ex!.Message, Does.Contain("越界"));
        Assert.That(ex.CellX, Is.LessThan(0));
    }

    [Test]
    public void ValidateSim_HandlePoolLeak_NamesSlot()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSim(session));
        var field = typeof(CrowdSimUnits).GetField("_freeTop", BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(session.Units, session.Units.FreeTop - 1);
        var ex = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        Assert.That(ex!.Message, Does.Contain("句柄池泄漏"));
    }

    [Test]
    public void ValidateSim_UnboundUnit_FailsHandleMapping()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var group = session.Groups.Alloc(1, 0, 0, FirstNav(session).Id);
        session.Units.Add(new CrowdUnitSpawnRequest(
            Fix64.FromInt(480000), Fix64.FromInt(560000), 0, 0, group.Id, 1, Fix64.FromInt(40), Fix64.Zero, 0, 0));
        var ex = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        Assert.That(ex!.Message, Does.Contain("还没绑定实体"));
    }

    [Test]
    public void ValidateSim_GroupCountTeamOrderAndRecycledFlow()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 2, 1, 0, 0), Is.EqualTo(2));
        var entity = session.Units.EntityAt(0);
        var state = session.World.Get<CrowdSimulationUnitState>(entity);
        var group = session.Groups.Groups[state.GroupId]!;
        group.Count--;
        var countEx = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        Assert.That(countEx!.Message, Does.Contain("计数"));
        group.Count++;

        int owner = session.World.Get<PlayerOwner>(entity).PlayerId;
        session.World.Set(entity, new PlayerOwner { PlayerId = owner == 1 ? 2 : 1 });
        var teamEx = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        Assert.That(teamEx!.Message, Does.Contain("玩家序"));
        session.World.Set(entity, new PlayerOwner { PlayerId = owner });

        group.OrderId = 404;
        var orderEx = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        Assert.That(orderEx!.Message, Does.Contain("不在指令簿"));
        Assert.That(orderEx.GroupId, Is.EqualTo(group.Id));
        group.OrderId = 0;

        var pool = new FlowPool(session.Config.NavCellsPerSide, 2);
        var flow = pool.Take();
        flow.NavId = group.NavId;
        group.Leader = new CrowdLeader(1);
        group.Flow = flow;
        pool.Give(flow);
        var flowEx = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        Assert.That(flowEx!.Message, Does.Contain("流场已回收"));
        Assert.That(flowEx.GroupId, Is.EqualTo(group.Id));
    }

    [Test]
    public void ValidateStructures_FreshMatches_CorruptAreaAndBlockerFail()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateStructures(session));
        session.Structures!.Area[0] ^= 1;
        var areaEx = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateStructures(session));
        Assert.That(areaEx!.Message, Does.Contain("结构栅格"));
        session.Structures.Area[0] ^= 1;

        var blocker = (HashSet<int>)typeof(CrowdStructuresStore).GetField("_blocker", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session.Structures)!;
        int id = 0;
        foreach (int entityId in session.Structures.EntityIds)
        {
            if (!blocker.Contains(entityId)) continue;
            id = entityId;
            break;
        }

        Assert.That(blocker.Remove(id), Is.True);
        var componentEx = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateStructures(session));
        Assert.That(componentEx!.Message, Does.Contain("阻挡组件"));
        Assert.That(componentEx.EntityId, Is.EqualTo(id));
    }

    [Test]
    public void SpatialHash_AfterIntegrate_MatchesCurrentPositionsOnceRebuilt()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        session.Advance(2);
        var movement = session.Movement!;
        var pos = movement.Positions[0];
        movement.Positions[0] = new Fix64Vec2(pos.X + movement.Hash.CellSizeCm * Fix64.FromInt(3), pos.Y);
        var stale = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSpatialHash(session));
        Assert.That(stale!.Message, Does.Contain("空间哈希与稠密计数排序不一致"));
        movement.RebuildHash();
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSpatialHash(session));
    }

    [Test]
    public void DebugValidation_WithoutSession_ReportsOnThePanel()
    {
        var runtime = new CrowdSimulationRuntime();
        runtime.RunDebugValidation();
        Assert.That(runtime.DebugValidationRan, Is.True);
        Assert.That(runtime.DebugValidationFailed, Is.True);
        Assert.That(runtime.DebugValidationLine, Does.Contain("无会话"));
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
