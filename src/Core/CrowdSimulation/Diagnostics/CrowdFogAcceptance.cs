using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Vision;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>
/// UAT-FOG-01..03 和迷雾隐藏核对。判定走 <see cref="CrowdPlayerVisibility"/>。
/// 坐标、模板和步数上限在场景资产里。
/// </summary>
public static class CrowdFogAcceptance
{
    public static void RunHide(CrowdSimSession session, JsonObject scene)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scene);
        var fog = session.Fog ?? throw new InvalidOperationException("UAT-FOG: 会话没有迷雾。");
        session.BlockOnDueReplies = true;
        int viewer = Int(scene, "viewerPlayer");
        int ally = Int(scene, "allyPlayer");
        int enemy = Int(scene, "enemyPlayer");
        int viewerGroup = Group(session, viewer);
        int allyGroup = Group(session, ally);
        int enemyGroup = Group(session, enemy);
        if (viewerGroup != allyGroup)
            throw new InvalidOperationException("UAT-FOG: 观察方和友方不在同一个视野组。");
        if (viewerGroup == enemyGroup)
            throw new InvalidOperationException("UAT-FOG: 敌方和观察方在同一个视野组。");

        int count = Int(scene, "spawnCount");
        int unitType = Int(scene, "unitType");
        int rIdx = Int(scene, "rIdx");
        string shape = Text(scene, "orderShape");
        int visionTicks = Int(scene, "visionTicks");
        int travelTicks = Int(scene, "travelTicks");
        Spawn(session, viewer, Int(scene, "viewerXCm"), Int(scene, "viewerYCm"), count, unitType, rIdx);
        Spawn(session, ally, Int(scene, "viewerXCm"), Int(scene, "viewerYCm"), count, unitType, rIdx);
        Step(session, visionTicks);

        Spawn(session, enemy, Int(scene, "enemyXCm"), Int(scene, "enemyYCm"), 1, unitType, rIdx);
        Step(session, visionTicks);
        int far = DenseOf(session, enemy);
        if (CrowdPlayerVisibility.UnitShown(session, viewerGroup, far, hideFog: true))
            throw new InvalidOperationException("UAT-FOG: 视野外的敌方单位被画出来了。");
        if (!CrowdPlayerVisibility.UnitShown(session, viewerGroup, far, hideFog: false))
            throw new InvalidOperationException("UAT-FOG: 关闭隐藏后视野外的敌方单位仍不显示。");

        var anchor = Position(session, DenseOf(session, viewer));
        int cellCm = fog.FogCellSizeCm;
        int fx = (int)(anchor.X / Fix64.FromInt(cellCm)).ToLong();
        int fy = (int)(anchor.Y / Fix64.FromInt(cellCm)).ToLong();
        int wallX = (fx + Int(scene, "wallCellsEast")) * cellCm + cellCm / 2;
        int wallY = fy * cellCm + cellCm / 2;
        int behindX = (fx + Int(scene, "behindCellsEast")) * cellCm + cellCm / 2;
        int behindY = wallY;
        Place(session, Text(scene, "wallTemplate"), wallX, wallY, Int(scene, "wallSizeCm"));
        Spawn(session, enemy, behindX, behindY, 1, unitType, rIdx);
        Step(session, visionTicks);
        int behind = DenseNear(session, enemy, behindX, behindY, cellCm);
        if (fog.Los)
            throw new InvalidOperationException("UAT-FOG: 场景开始时视线遮挡应是关的。");
        if (!CrowdPlayerVisibility.UnitShown(session, viewerGroup, behind, hideFog: true))
            throw new InvalidOperationException("UAT-FOG: 视线遮挡关闭时，墙后的敌方单位被隐藏了。");
        session.Commands.Submit(session, new JsonObject { ["type"] = "fogSight", ["on"] = true }, CrowdSimCommands.Exec);
        Step(session, visionTicks * 2);
        if (!fog.Los)
            throw new InvalidOperationException("UAT-FOG: fogSight 没有打开视线遮挡。");
        if (CrowdPlayerVisibility.UnitShown(session, viewerGroup, behind, hideFog: true))
            throw new InvalidOperationException("UAT-FOG: 视线遮挡打开时，墙后的敌方单位仍显示。");
        session.Commands.Submit(session, new JsonObject { ["type"] = "fogSight", ["on"] = false }, CrowdSimCommands.Exec);
        Step(session, visionTicks * 2);
        if (CrowdPlayerVisibility.UnitShown(session, viewerGroup, behind, hideFog: true) == false)
            throw new InvalidOperationException("UAT-FOG: 视线遮挡再次关闭后，墙后的敌方单位没有回来。");

        int unseenId = Place(session, Text(scene, "unseenTemplate"), Int(scene, "unseenXCm"), Int(scene, "unseenYCm"), Int(scene, "unseenSizeCm"));
        Step(session, visionTicks);
        if (fog.Belief[viewerGroup].ContainsKey(unseenId))
            throw new InvalidOperationException("UAT-FOG-01: 未看见的新结构进了认知。");
        if (CrowdPlayerVisibility.StructureShown(session, viewerGroup, unseenId, hideFog: true))
            throw new InvalidOperationException("UAT-FOG-01: 未发现的新实体被画出来了。");
        if (session.Belief == null || session.Belief.Slot[viewerGroup] == 0)
            throw new InvalidOperationException("UAT-FOG-01: 看不见的新结构仍在用真实导航。");
        if (VariantContains(session, unseenId))
            throw new InvalidOperationException("UAT-FOG-01: 发现前的认知场包含这个结构。");

        Order(session, viewer, Int(scene, "unseenXCm"), Int(scene, "unseenYCm"), shape);
        Order(session, ally, Int(scene, "unseenXCm"), Int(scene, "unseenYCm"), shape);
        StepUntil(session, travelTicks, () =>
            fog.CellVisibilityOf(viewerGroup, FogCell(fog, Int(scene, "unseenXCm"), Int(scene, "unseenYCm"))) == CellVisibility.Visible);
        if (!fog.Belief[viewerGroup].ContainsKey(unseenId))
            throw new InvalidOperationException("UAT-FOG-01: 看见之后认知里没有这个结构。");
        if (!CrowdPlayerVisibility.StructureShown(session, viewerGroup, unseenId, hideFog: true))
            throw new InvalidOperationException("UAT-FOG-01: 进入视野的实体没有显示。");

        int memoryX = Int(scene, "unseenXCm");
        int memoryY = Int(scene, "unseenYCm") + cellCm;
        int memoryId = Place(session, Text(scene, "unseenTemplate"), memoryX, memoryY, Int(scene, "unseenSizeCm"));
        StepUntil(session, visionTicks * 2, () => fog.Belief[viewerGroup].ContainsKey(memoryId));
        if (!fog.Belief[viewerGroup].ContainsKey(memoryId))
            throw new InvalidOperationException("UAT-FOG-02: 视野里的新结构没有进入认知。");
        uint goneBefore = fog.Gone[viewerGroup];
        uint enemyGone = fog.Gone[enemyGroup];
        int enemyGhosts = fog.GhostCount[enemyGroup];
        Order(session, viewer, Int(scene, "leaveXCm"), Int(scene, "leaveYCm"), shape);
        Order(session, ally, Int(scene, "leaveXCm"), Int(scene, "leaveYCm"), shape);
        int memoryCell = FogCell(fog, memoryX, memoryY);
        StepUntil(session, travelTicks, () => fog.CellVisibilityOf(viewerGroup, memoryCell) != CellVisibility.Visible);
        if (fog.CellVisibilityOf(viewerGroup, memoryCell) == CellVisibility.Visible)
            throw new InvalidOperationException("UAT-FOG-02: 离开后结构格仍可见。");
        Remove(session, memoryX, memoryY);
        Step(session, 1);
        if (fog.Gone[viewerGroup] != goneBefore)
            throw new InvalidOperationException("UAT-FOG-02: 视野外拆除时已消失计数增加了。");
        if (!fog.Belief[viewerGroup].ContainsKey(memoryId))
            throw new InvalidOperationException("UAT-FOG-02: 视野外拆除后没有留下残影。");
        if (fog.GhostCount[enemyGroup] != enemyGhosts || fog.Gone[enemyGroup] != enemyGone)
            throw new InvalidOperationException("UAT-FOG-02: 别的视野组收到了这次拆除。");
        Order(session, ally, memoryX, memoryY, shape);
        StepUntil(session, travelTicks, () => !fog.Belief[viewerGroup].ContainsKey(memoryId));
        if (fog.Belief[viewerGroup].ContainsKey(memoryId))
            throw new InvalidOperationException("UAT-FOG-02: 重新看见那几格后残影还在。");
        if (fog.Gone[viewerGroup] != goneBefore + 1)
            throw new InvalidOperationException("UAT-FOG-02: 重新看见后已消失计数没有加一。");
        if (fog.Belief[enemyGroup].ContainsKey(memoryId))
            throw new InvalidOperationException("UAT-FOG-02: 敌方视野组得到了友方的认知。");

        Order(session, enemy, Int(scene, "leaveXCm"), Int(scene, "leaveYCm"), shape);
        int entered = DenseOf(session, enemy);
        StepUntil(session, travelTicks, () => CrowdPlayerVisibility.UnitShown(session, viewerGroup, entered, hideFog: true));
        if (!CrowdPlayerVisibility.UnitShown(session, viewerGroup, entered, hideFog: true))
            throw new InvalidOperationException("UAT-FOG: 进入视野的敌方单位没有显示。");
    }

    public static void RunTerrain(CrowdSimSession session, JsonObject scene, bool optimistic)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scene);
        var fog = session.Fog ?? throw new InvalidOperationException("UAT-FOG-03: 会话没有迷雾。");
        session.BlockOnDueReplies = true;
        int viewer = Int(scene, "viewerPlayer");
        int unitType = Int(scene, "unitType");
        int rIdx = Int(scene, "rIdx");
        Spawn(session, viewer, Int(scene, "viewerXCm"), Int(scene, "viewerYCm"), Int(scene, "spawnCount"), unitType, rIdx);
        Step(session, Int(scene, "visionTicks"));
        int layer = session.Config.UnitTypes[unitType].AgentTypeIndex;
        var nav = session.NavFor(layer, rIdx);
        int side = session.Config.NavCellsPerSide;
        int navCell = session.Config.NavCellSizeCm;
        int cx = Int(scene, "probeXCm") / navCell;
        int cy = Int(scene, "probeYCm") / navCell;
        int cell = cy * side + cx;
        if (nav.Passable[cell] != 0)
            throw new InvalidOperationException("UAT-FOG-03: 场景里的探针格在真实导航上可通行。");
        int group = Group(session, viewer);
        int fogCell = FogCell(fog, Int(scene, "probeXCm"), Int(scene, "probeYCm"));
        if (fog.CellVisibilityOf(group, fogCell) != CellVisibility.Unseen)
            throw new InvalidOperationException("UAT-FOG-03: 探针格所在迷雾格在下令前已被探索。");

        int n = session.Units.CopyOwnHandles(viewer, session.MemberCapture);
        var members = new uint[n];
        Array.Copy(session.MemberCapture, members, n);
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = viewer,
            XCm = Int(scene, "goalXCm"),
            YCm = Int(scene, "goalYCm"),
            ShapeId = Text(scene, "orderShape"),
            Members = members,
            HasFogTerrain = true,
            FogTerrain = optimistic,
        }, CrowdSimCommands.Exec);
        Step(session, Int(scene, "visionTicks"));
        if (fog.Terrain != optimistic)
            throw new InvalidOperationException("UAT-FOG-03: 指令没有把未探索地形开关写成场景要求的值。");
        int slot = session.Belief?.Slot[group] ?? 0;
        if (!optimistic)
        {
            if (slot != 0)
                throw new InvalidOperationException("UAT-FOG-03: 关闭乐观迷雾后第一次规划没有用真实导航。");
            return;
        }

        if (slot == 0)
            throw new InvalidOperationException("UAT-FOG-03: 未探索探针格仍在用真实导航。");
        var built = new List<(Nav.NavContext Nav, CrowdStructuresStore Field)>();
        session.Beliefs!.CopyMaterialized(built);
        bool optimisticCell = false;
        for (int i = 0; i < built.Count; i++)
        {
            if (built[i].Nav.Id / CrowdBeliefNavs.BeliefStride != slot) continue;
            var field = built[i].Field;
            if (field.Area[cell] != field.TerrainArea[cell]) optimisticCell = true;
        }

        if (!optimisticCell)
            throw new InvalidOperationException("UAT-FOG-03: 乐观认知没有把未探索探针格换成可通行假设。");
    }

    private static bool VariantContains(CrowdSimSession session, int structureId)
    {
        if (session.Beliefs == null || session.Planner == null) return false;
        var built = new List<(Nav.NavContext Nav, CrowdStructuresStore Field)>();
        session.Beliefs.CopyMaterialized(built);
        for (int i = 0; i < built.Count; i++)
        {
            var ids = built[i].Field.EntityIds;
            for (int k = 0; k < ids.Count; k++)
            {
                if (ids[k] == structureId) return true;
            }
        }

        return false;
    }

    private static int Place(CrowdSimSession session, string template, int xCm, int yCm, int sizeCm)
    {
        int before = session.Structures!.EntityIds.Count;
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = template,
            XCm = xCm,
            YCm = yCm,
            SizeCm = sizeCm,
        }, CrowdSimCommands.Exec);
        var ids = session.Structures.EntityIds;
        if (ids.Count == before) throw new InvalidOperationException($"UAT-FOG: 放置 {template} 没有产生实体。");
        return ids[ids.Count - 1];
    }

    private static void Remove(CrowdSimSession session, int xCm, int yCm)
    {
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.RemoveStructureAt,
            XCm = xCm,
            YCm = yCm,
        }, CrowdSimCommands.Exec);
    }

    private static void Spawn(CrowdSimSession session, int player, int xCm, int yCm, int count, int unitType, int rIdx)
    {
        int made = CrowdDeployment.SpawnAt(session, Fix64.FromInt(xCm), Fix64.FromInt(yCm), count, player, unitType, rIdx);
        if (made != count)
            throw new InvalidOperationException($"UAT-FOG: 玩家 {player} 只生成了 {made} 个，场景要 {count} 个。");
    }

    private static void Order(CrowdSimSession session, int player, int xCm, int yCm, string shape)
    {
        int n = session.Units.CopyOwnHandles(player, session.MemberCapture);
        if (n <= 0) throw new InvalidOperationException($"UAT-FOG: 玩家 {player} 没有单位可下令。");
        var members = new uint[n];
        Array.Copy(session.MemberCapture, members, n);
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = player,
            XCm = xCm,
            YCm = yCm,
            ShapeId = shape,
            Members = members,
        }, CrowdSimCommands.Exec);
    }

    private static int Group(CrowdSimSession session, int player)
    {
        int index = session.Config.PairRules.IndexByPlayerId[player];
        return session.Fog!.GroupOf[index];
    }

    private static int DenseOf(CrowdSimSession session, int player)
    {
        var world = session.World;
        for (int i = 0; i < session.Units.Count; i++)
        {
            if (world.Get<PlayerOwner>(session.Units.EntityAt(i)).PlayerId == player) return i;
        }

        throw new InvalidOperationException($"UAT-FOG: 找不到玩家 {player} 的单位。");
    }

    private static int DenseNear(CrowdSimSession session, int player, int xCm, int yCm, int cellCm)
    {
        var world = session.World;
        var target = new Fix64Vec2(Fix64.FromInt(xCm), Fix64.FromInt(yCm));
        var limit = Fix64.FromInt(cellCm);
        for (int i = 0; i < session.Units.Count; i++)
        {
            if (world.Get<PlayerOwner>(session.Units.EntityAt(i)).PlayerId != player) continue;
            var pos = Position(session, i);
            if (Fix64.Abs(pos.X - target.X) <= limit && Fix64.Abs(pos.Y - target.Y) <= limit) return i;
        }

        throw new InvalidOperationException("UAT-FOG: 找不到墙后的敌方单位。");
    }

    private static Fix64Vec2 Position(CrowdSimSession session, int dense)
    {
        if (session.Movement != null) return session.Movement.Positions[dense];
        return session.World.Get<WorldPositionCm>(session.Units.EntityAt(dense)).Value;
    }

    private static int FogCell(CrowdFog fog, int xCm, int yCm)
    {
        int x = xCm / fog.FogCellSizeCm;
        int y = yCm / fog.FogCellSizeCm;
        return y * fog.FogCellsPerSide + x;
    }

    private static void Step(CrowdSimSession session, int ticks)
    {
        int ran = 0;
        int guard = ticks * CrowdSimulationLimits.AdvanceStallGuardFactor + CrowdSimulationLimits.AdvanceStallGuardFactor;
        while (ran < ticks)
        {
            if (guard-- <= 0) throw new InvalidOperationException("UAT-FOG: 会话推进停摆过长。");
            if (session.Step(out _)) ran++;
        }
    }

    private static void StepUntil(CrowdSimSession session, int ticks, Func<bool> done)
    {
        int ran = 0;
        int guard = ticks * CrowdSimulationLimits.AdvanceStallGuardFactor + CrowdSimulationLimits.AdvanceStallGuardFactor;
        while (ran < ticks && !done())
        {
            if (guard-- <= 0) throw new InvalidOperationException("UAT-FOG: 会话推进停摆过长。");
            if (session.Step(out _)) ran++;
        }
    }

    private static int Int(JsonObject scene, string key)
    {
        if (scene[key] is not JsonValue value || !value.TryGetValue<int>(out int number))
            throw new InvalidOperationException($"UAT-FOG 场景缺少整数 {key}。");
        return number;
    }

    private static string Text(JsonObject scene, string key)
    {
        if (scene[key] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"UAT-FOG 场景缺少 {key}。");
        return text;
    }
}
