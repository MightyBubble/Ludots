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

        // 运动字段(vx / vy / slotX / slotY / blend / stall,与参考实现同序)
        // L02 重钉:每个定点字段按原始 int64 低 32 → 高 32 两词混入(与导出端 mixI64 同形);
        // 旧口径只混低 32 词,随 S6 校验码混法变更一并作废。
        for (int k = 0; k < 6; k++)
        {
            for (int i = 0; i < n; i++)
            {
                long raw = MotionRaw(sim, i, k);
                h = Mix(h, (uint)raw);
                h = Mix(h, (uint)(raw >> 32));
            }
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

    /// <summary>
    /// 运动字段的定点原始值:Fix64 原始 int64(校验码按低→高两词混入,与导出端 mixI64 同形)。
    /// </summary>
    private static long MotionRaw(CrowdSimSession sim, int dense, int field)
    {
        var entity = sim.Units.EntityAt(dense);
        if (!sim.World.Has<CrowdSimulationKinematics>(entity)) return 0;
        var kin = sim.World.Get<CrowdSimulationKinematics>(entity);
        Fix64 v = field switch
        {
            0 => kin.Velocity.X,
            1 => kin.Velocity.Y,
            2 => kin.SlotOffsetCm.X,
            3 => kin.SlotOffsetCm.Y,
            4 => kin.Blend,
            _ => kin.StallSeconds,
        };
        return v.RawValue;
    }
}
