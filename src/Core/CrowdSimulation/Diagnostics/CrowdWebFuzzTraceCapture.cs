using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>
/// 从活会话抓逐 tick 只读快照，与 Web <c>scripts/exportFuzzTrace.mjs</c> 的 captureTick 同构：
/// 单位行按稠密序读（比较按句柄），导航按 id 升序，领队按指令簿顺序 + 指令内下标。
/// 位置换算米制与 <see cref="CrowdWebFuzzRunner"/> 的口径一致（Fix64 厘米 → double 米）。
/// </summary>
public static class CrowdWebFuzzTraceCapture
{
    private const double CmPerMeter = CrowdReferenceMeters.CentimetersPerMeter;

    public static CrowdWebFuzzTickTrace Capture(CrowdSimSession session, int opIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        int n = session.Units.Count;
        var units = new List<CrowdWebFuzzUnitTick>(n);
        var movement = session.Movement;
        var playerIndex = session.Config.PairRules.IndexByPlayerId;
        for (int i = 0; i < n; i++)
        {
            Fix64Vec2 position;
            CrowdSimulationUnitState state;
            byte player;
            if (movement != null && i < movement.Positions.Length)
            {
                position = movement.Positions[i];
                state = movement.States[i];
                player = (byte)movement.PlayerIdx[i];
            }
            else
            {
                var entity = session.Units.EntityAt(i);
                position = session.World.Get<WorldPositionCm>(entity).Value;
                state = session.World.Get<CrowdSimulationUnitState>(entity);
                player = (byte)playerIndex[session.World.Get<PlayerOwner>(entity).PlayerId];
            }

            units.Add(new CrowdWebFuzzUnitTick(
                session.Units.HandleAt(i),
                position.X.ToDouble() / CmPerMeter,
                position.Y.ToDouble() / CmPerMeter,
                state.State,
                state.GroupId,
                player,
                state.Level,
                state.Order,
                state.Mode));
        }

        var navs = session.Navs
            .OrderBy(kv => kv.Key)
            .Select(kv => new CrowdWebFuzzNavTick(kv.Key, kv.Value.Version))
            .ToList();

        var leaders = new List<CrowdWebFuzzLeaderTick>();
        foreach (var order in session.Orders.List)
        {
            for (int k = 0; k < order.Leaders.Count; k++)
            {
                var leader = order.Leaders[k];
                var path = new List<CrowdWebFuzzPathPoint>(leader.PathCount);
                for (int p = 0; p < leader.PathCount; p++)
                {
                    var point = leader.PathAt(p);
                    path.Add(new CrowdWebFuzzPathPoint(point.X.ToDouble() / CmPerMeter, point.Y.ToDouble() / CmPerMeter));
                }

                leaders.Add(new CrowdWebFuzzLeaderTick(
                    order.Id,
                    k,
                    leader.XCm.ToDouble() / CmPerMeter,
                    leader.YCm.ToDouble() / CmPerMeter,
                    leader.Seg,
                    leader.Done,
                    path));
            }
        }

        return new CrowdWebFuzzTickTrace(session.TickCount, opIndex, units, navs, leaders);
    }
}
