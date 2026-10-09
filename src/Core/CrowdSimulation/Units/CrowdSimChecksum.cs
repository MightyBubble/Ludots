using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// S4 规范校验码(FNV-1a 32,Fix64 原始值口径——甲方体系):
/// 字段顺序与参考实现 unitChecksum 一致(tick、单位数、x/y、vx/vy、slotX/slotY、
/// blend、stall、state、group、id、order、mode、level,再指令与领队),
/// 每个定点字段按原始 int64 的低 32 位 → 高 32 位依次混合(与参考实现逐 f64 词序同构)。
/// 读面:有运动栈的会话在 tick 末从内核 SoA 读(scatter 后同值);纯部署会话(无运动栈)
/// 保留组件直读。两条路径值等价——SoA 即 scatter 落回前的组件值。
/// </summary>
public static class CrowdSimChecksum
{
    private const uint Offset = 2166136261u, Prime = 16777619u;

    public static string Compute(CrowdSimSession sim)
    {
        var units = sim.Units;
        int n = units.Count;
        var movement = sim.Movement;
        uint h = Mix(Mix(Offset, (uint)sim.TickCount), (uint)n);

        // 位置(Fix64 原始值,先低后高;与参考实现 f64 词序同构)
        for (int i = 0; i < n; i++)
        {
            var pos = Position(sim, movement, i);
            h = Mix(h, (uint)pos.X.RawValue);
            h = Mix(h, (uint)(pos.X.RawValue >> 32));
        }

        for (int i = 0; i < n; i++)
        {
            var pos = Position(sim, movement, i);
            h = Mix(h, (uint)pos.Y.RawValue);
            h = Mix(h, (uint)(pos.Y.RawValue >> 32));
        }

        // 运动字段(vx / vy / slotX / slotY / blend / stall,与参考实现同序)
        // 每个定点字段按原始 int64 低 32 → 高 32 两词混入(与导出端 mixI64 同形);
        // 旧口径只混低 32 词,随 S6 校验码混法变更一并作废。
        for (int k = 0; k < 6; k++)
        {
            for (int i = 0; i < n; i++)
            {
                long raw = MotionRaw(sim, movement, i, k);
                h = Mix(h, (uint)raw);
                h = Mix(h, (uint)(raw >> 32));
            }
        }

        // 整型字段:state / group / id / order / mode / level
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).State);
        for (int i = 0; i < n; i++) h = Mix(h, (uint)State(sim, movement, i).GroupId);
        for (int i = 0; i < n; i++) h = Mix(h, units.HandleAt(i));
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).Order);
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).Mode);
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).Level);

        // 避让隐藏状态:分离两轴 + calm 逐单位(mixI64 双词)+ 相位一词——影响下 tick 行为
        // 的状态必须进校验码,否则快照恢复分歧延迟显形且无法定位到避让子系统。
        // 无运动栈(部署会话)时恒零,与导出端词数对齐。
        for (int i = 0; i < n; i++)
        {
            long sx = movement != null ? movement.Separation[i].X.RawValue : 0;
            h = Mix(h, (uint)sx);
            h = Mix(h, (uint)(sx >> 32));
        }

        for (int i = 0; i < n; i++)
        {
            long sy = movement != null ? movement.Separation[i].Y.RawValue : 0;
            h = Mix(h, (uint)sy);
            h = Mix(h, (uint)(sy >> 32));
        }

        for (int i = 0; i < n; i++)
        {
            long calm = movement != null ? movement.Calm[i] : 0;
            h = Mix(h, (uint)calm);
            h = Mix(h, (uint)(calm >> 32));
        }

        h = Mix(h, (uint)(movement?.Phase ?? 0));

        return h.ToString("x8");
    }

    private static Fix64Vec2 Position(CrowdSimSession sim, CrowdMovementKernel? movement, int dense) =>
        movement != null ? movement.Positions[dense]
            : sim.World.Get<WorldPositionCm>(sim.Units.EntityAt(dense)).Value;

    private static CrowdSimulationUnitState State(CrowdSimSession sim, CrowdMovementKernel? movement, int dense) =>
        movement != null ? movement.States[dense]
            : sim.World.Get<CrowdSimulationUnitState>(sim.Units.EntityAt(dense));

    private static uint Mix(uint h, uint v) => (h ^ v) * Prime;

    /// <summary>
    /// 运动字段的定点原始值:Fix64 原始 int64(校验码按低→高两词混入,与导出端 mixI64 同形)。
    /// </summary>
    private static long MotionRaw(CrowdSimSession sim, CrowdMovementKernel? movement, int dense, int field)
    {
        CrowdSimulationKinematics kin;
        if (movement != null)
        {
            kin = movement.Kins[dense];
        }
        else
        {
            var entity = sim.Units.EntityAt(dense);
            if (!sim.World.Has<CrowdSimulationKinematics>(entity)) return 0;
            kin = sim.World.Get<CrowdSimulationKinematics>(entity);
        }

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
