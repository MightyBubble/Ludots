using System;
using System.Collections.Generic;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>
/// 仿真、结构、空间哈希和导航不变量。只由调试入口调用，步进不进入这里。
/// </summary>
public static class CrowdSimulationValidator
{
    public static void RunAll(CrowdSimSession session)
    {
        ValidateSim(session);
        ValidateStructures(session);
        ValidateSpatialHash(session);
        ValidateNav(session);
    }

    /// <summary>
    /// 句柄池、组计数与原型、世界边界、非跳跃单位的停留、流场和指令引用。
    /// 地面看结构仓的活阻挡栅格和该单位导航的可通行格；桥面看 <see cref="NavContext.UpPass"/>。
    /// 跳跃单位离网直线可越阻挡，不查停留。重烘阶段 0 当帧挤离还没跑，这一帧不查停留。
    /// </summary>
    public static void ValidateSim(CrowdSimSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateHandles(session);
        ValidateBounds(session);
        ValidateGroups(session);
        if (!EvictionPending(session))
        {
            RequireStructures(session);
            int count = session.Units.Count;
            for (int i = 0; i < count; i++)
            {
                if (TryIllegalRest(session, i, out int slot, out int entityId, out int cx, out int cy, out int kind))
                {
                    throw Rest(session.TickCount, slot, entityId, cx, cy, kind);
                }
            }
        }

        var groups = session.Groups.Groups;
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group == null || group.Flow == null || group.Leader != null) continue;
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 组 {group.Id} 有流场但没有领队",
                session.TickCount, -1, -1, -1, -1, -1, -1, group.Id);
        }

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group == null) continue;
            ValidateFlow(session, group);
            ValidateOrder(session, group);
        }

        if (session.Planner?.FlowCacheInvalidReason() is { } cacheReason)
        {
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} {cacheReason}",
                session.TickCount, -1, -1, -1, -1, -1, -1, -1);
        }
    }

    /// <summary>实体组件互相覆盖，阻挡格和区域格与按当前实体全量重标的结果一致。</summary>
    public static void ValidateStructures(CrowdSimSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var store = session.Structures ?? throw new InvalidOperationException(
            $"tick {session.TickCount} 会话没有结构仓，无法核对实体栅格。");
        store.VerifyComponentContract(session.TickCount);
        var entities = new List<(int Id, int TplIndex, CrowdStructureFootprint Fp)>(store.EntityIds.Count);
        store.CopyLiveEntities(entities);
        BeliefRank[] ranks = Array.Empty<BeliefRank>();
        var derived = CrowdStructuresStore.BuildBeliefField(session.Config, store.TerrainSurface(), entities, ref ranks);
        int n = store.Area.Length;
        for (int i = 0; i < n; i++)
        {
            if (store.Area[i] == derived.Area[i] && store.Blocked[i] == derived.Blocked[i]) continue;
            int side = store.NavCellsPerSide;
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 结构栅格与全量重标不一致 @格 {i} 区域 {store.Area[i]}/{derived.Area[i]} 阻挡 {store.Blocked[i]}/{derived.Blocked[i]}",
                session.TickCount, -1, -1, i % side, i / side, -1, -1, -1);
        }

        store.VerifyDeckContract(session.TickCount);
    }

    /// <summary>运动内核里的空间哈希与稠密计数排序对照。没有内核时抛错。</summary>
    public static void ValidateSpatialHash(CrowdSimSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var movement = session.Movement ?? throw new InvalidOperationException(
            $"tick {session.TickCount} 会话没有运动内核，无法核对空间哈希。");
        int count = session.Units.Count;
        if (movement.Positions.Length < count || movement.Radii.Length < count)
        {
            throw new InvalidOperationException(
                $"tick {session.TickCount} 运动内核实位表短于单位数，空间哈希无法对照。");
        }

        ValidateSpatialHash(movement.Hash, movement.Positions, movement.Radii, count);
    }

    public static void ValidateSpatialHash(CrowdSpatialHash hash, Fix64Vec2[] positions, Fix64[] radiiCm, int count) =>
        CrowdSpatialHashReference.Compare(hash, positions, radiiCm, count);

    /// <summary>
    /// 真实导航对当前地表全量烘焙。每个已构建的认知变体对它自己的认知场再烤一遍。
    /// 认知变体的跳跃链接只比数量。
    /// </summary>
    public static void ValidateNav(CrowdSimSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Structures == null || session.RebakeSources == null)
        {
            throw new InvalidOperationException(
                $"tick {session.TickCount} 会话没有结构仓或重烘输入，无法核对增量导航。");
        }

        CrowdNavRebake.VerifyAgainstFullBake(session.Navs, session.RebakeSources, session.Structures, session.TickCount);
        var beliefs = session.Beliefs;
        if (beliefs == null || beliefs.VariantCount() == 0) return;
        var built = new List<(NavContext Nav, CrowdStructuresStore Field)>();
        beliefs.CopyMaterialized(built);
        for (int i = 0; i < built.Count; i++)
        {
            var (nav, field) = built[i];
            CrowdNavRebake.VerifyAgainstField(nav, field, session.RebakeSources, session.TickCount, linksByCountOnly: true);
        }
    }

    /// <summary>违反停留不变量的单位数。阶段 0 当帧不计。规则与 <see cref="ValidateSim"/> 的停留检查相同。</summary>
    internal static int CountBlockedRests(CrowdSimSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (EvictionPending(session)) return 0;
        RequireStructures(session);
        int hits = 0;
        int count = session.Units.Count;
        for (int i = 0; i < count; i++)
        {
            if (TryIllegalRest(session, i, out _, out _, out _, out _, out _)) hits++;
        }

        return hits;
    }

    private static void ValidateHandles(CrowdSimSession session)
    {
        var units = session.Units;
        int tick = session.TickCount;
        int capacity = units.Capacity;
        int count = units.Count;
        if (units.PendingCount != 0)
        {
            throw Invariant(tick, -1, -1, $"有 {units.PendingCount} 个单位还没绑定实体");
        }

        if (units.FreeTop + count != capacity)
        {
            throw Invariant(tick, -1, -1, $"句柄池泄漏：空闲 {units.FreeTop} + 存活 {count} ≠ 容量 {capacity}");
        }

        var seen = new bool[capacity];
        for (int i = 0; i < count; i++)
        {
            int slot = units.SlotAt(i);
            var entity = units.EntityAt(i);
            if ((uint)slot >= (uint)capacity || seen[slot] || units.SparseAt(slot) != i)
            {
                throw Invariant(tick, slot, entity.Id, $"单位槽 {slot} 的稠密映射 {i} 不一致");
            }

            seen[slot] = true;
            uint handle = units.HandleAt(i);
            int handleSlot = (int)(handle & CrowdSimUnits.SlotMask);
            int handleGen = (int)(handle >> CrowdSimUnits.SlotBits);
            if (handleSlot != slot || handleGen != units.GenOf(slot) ||
                !units.TryResolveHandle(handle, out int dense) || dense != i)
            {
                throw Invariant(tick, slot, entity.Id, $"单位槽 {slot} 的句柄映射不能往返");
            }

            if (!session.World.IsAlive(entity) ||
                !session.World.Has<CrowdSimulationUnitState>(entity) ||
                session.World.Get<CrowdSimulationUnitState>(entity).Slot != slot)
            {
                throw Invariant(tick, slot, entity.Id, $"单位槽 {slot} 的实体 {entity.Id} 与句柄脱节");
            }
        }

        for (int i = 0; i < units.FreeTop; i++)
        {
            int slot = units.FreeSlotAt(i);
            if ((uint)slot >= (uint)capacity || seen[slot] || units.SparseAt(slot) >= 0)
            {
                throw Invariant(tick, slot, -1, $"空闲槽 {slot} 与存活表重叠或越界");
            }

            seen[slot] = true;
        }

        for (int slot = 0; slot < capacity; slot++)
        {
            if (!seen[slot]) throw Invariant(tick, slot, -1, $"槽 {slot} 不在存活表也不在空闲栈");
        }
    }

    private static void ValidateBounds(CrowdSimSession session)
    {
        var units = session.Units;
        int side = session.Config.NavCellsPerSide;
        int cellSize = session.Config.NavCellSizeCm;
        Fix64 limit = Fix64.FromInt(side * cellSize);
        int count = units.Count;
        for (int i = 0; i < count; i++)
        {
            var entity = units.EntityAt(i);
            var pos = session.World.Get<WorldPositionCm>(entity).Value;
            if (pos.X >= Fix64.Zero && pos.Y >= Fix64.Zero && pos.X < limit && pos.Y < limit) continue;
            int cx = CrowdSimulationSpace.NavCellCoord(pos.X, cellSize);
            int cy = CrowdSimulationSpace.NavCellCoord(pos.Y, cellSize);
            var state = session.World.Get<CrowdSimulationUnitState>(entity);
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 单位槽 {state.Slot} 实体 {entity.Id} 越界 ({cx},{cy})",
                session.TickCount, state.Slot, entity.Id, cx, cy, -1, -1, state.GroupId);
        }
    }

    private static void ValidateGroups(CrowdSimSession session)
    {
        var units = session.Units;
        var groups = session.Groups.Groups;
        var tally = new int[groups.Count];
        int count = units.Count;
        for (int i = 0; i < count; i++)
        {
            var entity = units.EntityAt(i);
            var state = session.World.Get<CrowdSimulationUnitState>(entity);
            if (!session.Groups.TryGet(state.GroupId, out var group))
            {
                throw Invariant(session.TickCount, state.Slot, entity.Id, $"单位槽 {state.Slot} 实体 {entity.Id} 的组 {state.GroupId} 不存在");
            }

            tally[group.Id]++;
            if (!session.World.Has<PlayerOwner>(entity) || !session.World.Has<CrowdSimulationAgent>(entity))
            {
                throw Invariant(session.TickCount, state.Slot, entity.Id, $"单位槽 {state.Slot} 实体 {entity.Id} 缺队伍或原型组件");
            }

            int player = session.World.Get<PlayerOwner>(entity).PlayerId;
            var playerIndex = session.Config.PairRules.IndexByPlayerId;
            if ((uint)player >= (uint)playerIndex.Length || playerIndex[player] != group.Player)
            {
                throw Invariant(
                    session.TickCount, state.Slot, entity.Id,
                    $"单位槽 {state.Slot} 实体 {entity.Id} 玩家 {player} 与组 {group.Id} 的玩家序 {group.Player} 不一致");
            }

            var agent = session.World.Get<CrowdSimulationAgent>(entity);
            int profile = CrowdDeployment.ProfileIndexOf(session.Config, group.LayerIdx, group.RIdx);
            if (agent.AgentTypeIndex != group.LayerIdx || agent.ProfileIndex != profile)
            {
                throw Invariant(
                    session.TickCount, state.Slot, entity.Id,
                    $"单位槽 {state.Slot} 实体 {entity.Id} 原型 ({agent.AgentTypeIndex},{agent.ProfileIndex}) 与组 {group.Id} ({group.LayerIdx},{group.RIdx}) 不一致");
            }
        }

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group == null) continue;
            if (group.Id != i || group.Count != tally[i])
            {
                throw new CrowdSimulationInvariantException(
                    $"tick {session.TickCount} 组 {group.Id} 计数 {group.Count} 与成员 {tally[i]} 不一致",
                    session.TickCount, -1, -1, -1, -1, -1, -1, group.Id);
            }
        }
    }

    private static void ValidateFlow(CrowdSimSession session, CrowdNavGroupSet.Group group)
    {
        var flow = group.Flow;
        if (flow == null) return;
        if (flow.OriginPool != null && flow.OriginPool.Holds(flow))
        {
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 组 {group.Id} 流场已回收",
                session.TickCount, -1, -1, -1, -1, flow.NavId, -1, group.Id);
        }

        bool refreshing = session.Planner != null && session.Planner.Refreshing(group);
        if ((flow.NavId != group.NavId && !refreshing) || !NavExists(session, flow.NavId))
        {
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 组 {group.Id} 流场导航 {flow.NavId} 不属于本组 {group.NavId}",
                session.TickCount, -1, -1, -1, -1, flow.NavId, -1, group.Id);
        }

        if (flow.Refs < 1)
        {
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 组 {group.Id} 流场引用已释放",
                session.TickCount, -1, -1, -1, -1, flow.NavId, -1, group.Id);
        }

        if (flow.Cached && (session.Planner == null || !session.Planner.FlowCacheContains(flow)))
        {
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 组 {group.Id} 流场缓存标记与缓存不一致",
                session.TickCount, -1, -1, -1, -1, flow.NavId, -1, group.Id);
        }
    }

    private static void ValidateOrder(CrowdSimSession session, CrowdNavGroupSet.Group group)
    {
        if (group.Leader != null && group.OrderId == 0)
        {
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 组 {group.Id} 有领队但没有指令",
                session.TickCount, -1, -1, -1, -1, -1, -1, group.Id);
        }

        if (group.OrderId == 0) return;
        CrowdOrder? order = null;
        var list = session.Orders.List;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == group.OrderId)
            {
                order = list[i];
                break;
            }
        }

        if (order == null)
        {
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 组 {group.Id} 指令 {group.OrderId} 不在指令簿",
                session.TickCount, -1, -1, -1, -1, -1, -1, group.Id);
        }

        bool listed = false;
        for (int i = 0; i < order.Groups.Count; i++)
        {
            if (order.Groups[i].GroupId == group.Id)
            {
                listed = true;
                break;
            }
        }

        if (!listed)
        {
            throw new CrowdSimulationInvariantException(
                $"tick {session.TickCount} 组 {group.Id} 不在指令 {group.OrderId} 的组列表",
                session.TickCount, -1, -1, -1, -1, -1, -1, group.Id);
        }

        if (group.Leader == null) return;
        for (int i = 0; i < order.Leaders.Count; i++)
        {
            if (ReferenceEquals(order.Leaders[i], group.Leader)) return;
        }

        throw new CrowdSimulationInvariantException(
            $"tick {session.TickCount} 组 {group.Id} 领队不在指令 {group.OrderId}",
            session.TickCount, -1, -1, -1, -1, -1, -1, group.Id);
    }

    private static bool NavExists(CrowdSimSession session, int navId)
    {
        if (navId < CrowdBeliefNavs.BeliefStride) return session.Navs.ContainsKey(navId);
        return session.Beliefs != null && session.Beliefs.TryGetMaterialized(navId, out _);
    }

    private static CrowdSimulationInvariantException Invariant(int tick, int slot, int entityId, string detail) =>
        new($"tick {tick} {detail}", tick, slot, entityId, -1, -1, -1, -1, -1);

    private static bool EvictionPending(CrowdSimSession session) => session.RebakeJob is { Stage: < 1 };

    private static void RequireStructures(CrowdSimSession session)
    {
        if (session.Structures == null)
        {
            throw new InvalidOperationException($"tick {session.TickCount} 会话没有结构仓，无法核对单位是否停在阻挡格。");
        }
    }

    private static bool TryIllegalRest(
        CrowdSimSession session, int dense, out int slot, out int entityId, out int cx, out int cy, out int kind)
    {
        slot = -1;
        entityId = -1;
        cx = -1;
        cy = -1;
        kind = 0;
        var units = session.Units;
        var entity = units.EntityAt(dense);
        var state = session.World.Get<CrowdSimulationUnitState>(entity);
        if (state.State == (byte)CrowdUnitState.Jump) return false;

        slot = state.Slot;
        entityId = entity.Id;
        var pos = session.World.Get<WorldPositionCm>(entity).Value;
        int n = session.Config.NavCellsPerSide;
        int cellSize = session.Config.NavCellSizeCm;
        cx = CrowdSimulationSpace.NavCellCoord(pos.X, cellSize);
        cy = CrowdSimulationSpace.NavCellCoord(pos.Y, cellSize);
        bool outside = cx < 0 || cy < 0 || cx >= n || cy >= n;
        if (!session.Groups.TryGet(state.GroupId, out var group))
        {
            throw Rest(session.TickCount, slot, entityId, cx, cy, 4);
        }

        if (!session.Navs.TryGetValue(group.BodyNavId, out var nav))
        {
            throw new InvalidOperationException(
                $"tick {session.TickCount} 单位槽 {slot} 实体 {entityId} 的导航 {group.BodyNavId} 不在会话里。");
        }

        if (state.Level != 0)
        {
            if (outside || nav.UpPass[cy * n + cx] == 0)
            {
                kind = 3;
                return true;
            }

            return false;
        }

        if (outside || session.Structures!.Blocked[cy * n + cx] != 0)
        {
            kind = 1;
            return true;
        }

        if (nav.Passable[cy * n + cx] == 0)
        {
            kind = 2;
            return true;
        }

        return false;
    }

    private static CrowdSimulationInvariantException Rest(int tick, int slot, int entityId, int cx, int cy, int kind)
    {
        string why = kind switch
        {
            1 => "停在阻挡格",
            2 => "停在不可通行格",
            3 => "停在桥面不可通行格",
            4 => "没有导航组",
            _ => throw new InvalidOperationException($"未知停留类别 {kind}。"),
        };
        return new CrowdSimulationInvariantException(
            $"tick {tick} 单位槽 {slot} 实体 {entityId} {why} ({cx},{cy})",
            tick, slot, entityId, cx, cy, -1, -1, -1);
    }
}

/// <summary>仿真或导航不变量失败。消息里带 tick；单位停留带槽位、实体和格坐标；导航带 nav 与 tile。</summary>
public sealed class CrowdSimulationInvariantException : InvalidOperationException
{
    public CrowdSimulationInvariantException(
        string message, int tick, int unitSlot, int entityId, int cellX, int cellY, int navId, int tile, int groupId)
        : base(message)
    {
        Tick = tick;
        UnitSlot = unitSlot;
        EntityId = entityId;
        CellX = cellX;
        CellY = cellY;
        NavId = navId;
        Tile = tile;
        GroupId = groupId;
    }

    public int Tick { get; }
    public int UnitSlot { get; }
    public int EntityId { get; }
    public int CellX { get; }
    public int CellY { get; }
    public int NavId { get; }
    public int Tile { get; }
    public int GroupId { get; }
}
