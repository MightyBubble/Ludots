using System;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>
/// 仿真与导航不变量。不在步进热路径上：会话的 <see cref="CrowdSimSession.ValidateEveryNTicks"/> 为 0 时调用方不进入这里。
/// </summary>
public static class CrowdSimulationValidator
{
    /// <summary>
    /// 单位没有停在阻挡格上，且每个有流场的导航组都有领队。
    /// 地面看结构仓的活阻挡栅格和该单位导航的可通行格；桥面看 <see cref="NavContext.UpPass"/>。
    /// 跳跃单位离网直线可越阻挡，不查。重烘阶段 0 当帧挤离还没跑，这一帧不查停留。
    /// </summary>
    public static void ValidateSim(CrowdSimSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
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
    }

    /// <summary>
    /// 增量导航与同一份地表的全量烘焙一致。比较就是 <see cref="CrowdNavRebake.VerifyAgainstFullBake"/>。
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
