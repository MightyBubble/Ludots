using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 群体到达的接触查询(contact.js 移植):单位是否压到同指令的静止(ARRIVED)单位。
/// 读本子步内建的空间哈希中单位自己的查询环;最多读 maxScan 个候选(与物理趟同上限)。
/// </summary>
public static class CrowdContact
{
    public static bool TouchesRestingPeer(CrowdSpatialHash hash, CrowdSimSession session, int i, int maxScan)
    {
        var units = session.Units;
        var world = session.World;
        int a = hash.Slot[hash.CellOf[i]], baseIdx = a * hash.Width;
        int nearby = hash.RingCount[a * (hash.Rings + 1) + hash.UnitRing[i]];
        var entity = units.EntityAt(i);
        var posI = world.Get<WorldPositionCm>(entity).Value;
        var stateI = world.Get<CrowdSimulationUnitState>(entity);
        Fix64 ri = session.ProfileRadiusCm(world.Get<CrowdSimulationAgent>(entity).ProfileId);
        uint oi = stateI.Order;
        int li = stateI.Level;
        int scanned = 0;
        for (int o = 0; o < nearby && scanned < maxScan; o++)
        {
            int pos = baseIdx + o, end = hash.NeighborEnd[pos];
            for (int kk = hash.NeighborStart[pos]; kk < end && scanned < maxScan; kk++)
            {
                int j = hash.Items[kk];
                if (j == i) continue;
                scanned++;
                var entityJ = units.EntityAt(j);
                var stateJ = world.Get<CrowdSimulationUnitState>(entityJ);
                if (stateJ.State != (byte)CrowdUnitState.Arrived || stateJ.Order != oi || stateJ.Level != li) continue;
                var posJ = world.Get<WorldPositionCm>(entityJ).Value;
                Fix64 dx = posI.X - posJ.X, dy = posI.Y - posJ.Y;
                Fix64 rs = ri + session.ProfileRadiusCm(world.Get<CrowdSimulationAgent>(entityJ).ProfileId);
                if (dx * dx + dy * dy < rs * rs) return true;
            }
        }

        return false;
    }
}
