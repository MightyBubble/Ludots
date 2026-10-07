using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// S4 规范校验码(FNV-1a 32,Fix64 原始值口径——甲方体系):
/// 字段顺序与参考实现 unitChecksum 一致(tick、单位数、x/y、vx/vy、slotX/slotY、
/// blend、stall、state、group、id、order、mode、level,再指令与领队),
/// 每个定点字段按原始 int64 的低 32 位 → 高 32 位依次混合(与参考实现逐 f64 词序同构)。
/// 本阶段没有指令与领队:校验内容完全由生成时刻决定,重放同一份指令流必然逐位一致。
/// </summary>
public static class CrowdSimChecksum
{
    private const uint Offset = 2166136261u, Prime = 16777619u;

    public static string Compute(CrowdSimSession sim)
    {
        var units = sim.Units;
        int n = units.Count;
        uint h = Mix(Mix(Offset, (uint)sim.TickCount), (uint)n);

        // 位置(Fix64 原始值,先低后高;与参考实现 f64 词序同构)
        for (int i = 0; i < n; i++)
        {
            var pos = sim.World.Get<WorldPositionCm>(units.EntityAt(i)).Value;
            h = Mix(h, (uint)pos.X.RawValue);
            h = Mix(h, (uint)(pos.X.RawValue >> 32));
        }

        for (int i = 0; i < n; i++)
        {
            var pos = sim.World.Get<WorldPositionCm>(units.EntityAt(i)).Value;
            h = Mix(h, (uint)pos.Y.RawValue);
            h = Mix(h, (uint)(pos.Y.RawValue >> 32));
        }

        // 生成初值的运动字段(vx / vy / slotX / slotY / blend / stall,本阶段恒 0,与参考实现同序)
        for (int k = 0; k < 6; k++)
        {
            for (int i = 0; i < n; i++) h = Mix(h, 0u);
        }

        // 整型字段:state / group / id / order / mode / level
        for (int i = 0; i < n; i++) h = Mix(h, sim.World.Get<CrowdSimulationUnitState>(units.EntityAt(i)).State);
        for (int i = 0; i < n; i++) h = Mix(h, (uint)sim.World.Get<CrowdSimulationUnitState>(units.EntityAt(i)).GroupId);
        for (int i = 0; i < n; i++) h = Mix(h, units.HandleAt(i));
        for (int i = 0; i < n; i++) h = Mix(h, sim.World.Get<CrowdSimulationUnitState>(units.EntityAt(i)).Order);
        for (int i = 0; i < n; i++) h = Mix(h, sim.World.Get<CrowdSimulationUnitState>(units.EntityAt(i)).Mode);
        for (int i = 0; i < n; i++) h = Mix(h, sim.World.Get<CrowdSimulationUnitState>(units.EntityAt(i)).Level);

        return h.ToString("x8");
    }

    private static uint Mix(uint h, uint v) => (h ^ v) * Prime;
}
