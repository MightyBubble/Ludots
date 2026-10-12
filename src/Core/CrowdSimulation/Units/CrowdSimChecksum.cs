using System;
using System.Collections.Generic;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 一条 Mix 上的四个断面。后一断面从前一断面的结果继续混。
/// <see cref="Total"/> 等于 <see cref="Avoidance"/>，即整段混合的终态。
/// </summary>
public readonly struct CrowdSimStateDigest
{
    public readonly int Tick;
    public readonly ulong Position;
    public readonly ulong Motion;
    public readonly ulong State;
    public readonly ulong Avoidance;

    public CrowdSimStateDigest(int tick, ulong position, ulong motion, ulong state, ulong avoidance)
    {
        Tick = tick;
        Position = position;
        Motion = motion;
        State = state;
        Avoidance = avoidance;
    }

    public ulong Total => Avoidance;
}

/// <summary>两串逐 tick 摘要的第一处分歧。无分歧时 <see cref="Diverged"/> 为 false，Tick 为 -1。</summary>
public readonly struct CrowdSimDigestDiff
{
    public readonly bool Diverged;
    public readonly int Tick;
    public readonly string Section;

    public CrowdSimDigestDiff(bool diverged, int tick, string section)
    {
        Diverged = diverged;
        Tick = tick;
        Section = section;
    }

    public static CrowdSimDigestDiff None => new(false, -1, "");
}

/// <summary>
/// 规范校验码（FNV-1a 32，Fix64 原始值）。词序：tick、单位数、x/y、vx/vy、slotX/slotY、
/// blend、stall、state、group、句柄、order、mode、level，再分离两轴、calm、相位。
/// 每个定点字段按原始 int64 的低 32 位再高 32 位混合。
/// 有运动栈的会话在 tick 末从内核 SoA 读；纯部署会话读组件。
/// 未执行的指令队列和订单簿不在这条 Mix 里（单位当前的 order 号在 state 断面）。
/// 把队列折进合计会改掉已冻结的校验码；队列是回放输入，执行当 tick 的效果落在 state / motion / position。
/// </summary>
public static class CrowdSimChecksum
{
    public const string PositionSection = "position";
    public const string MotionSection = "motion";
    public const string StateSection = "state";
    public const string AvoidanceSection = "avoidance";

    private const uint Offset = 2166136261u, Prime = 16777619u;

    public static ulong ComputeValue(CrowdSimSession sim) => Compute(sim).Total;

    public static CrowdSimStateDigest Compute(CrowdSimSession sim)
    {
        var units = sim.Units;
        int n = units.Count;
        var movement = sim.Movement;
        uint h = Mix(Mix(Offset, (uint)sim.TickCount), (uint)n);

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

        ulong position = h;

        for (int k = 0; k < 6; k++)
        {
            for (int i = 0; i < n; i++)
            {
                long raw = MotionRaw(sim, movement, i, k);
                h = Mix(h, (uint)raw);
                h = Mix(h, (uint)(raw >> 32));
            }
        }

        ulong motion = h;

        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).State);
        for (int i = 0; i < n; i++) h = Mix(h, (uint)State(sim, movement, i).GroupId);
        for (int i = 0; i < n; i++) h = Mix(h, units.HandleAt(i));
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).Order);
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).Mode);
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).Level);
        ulong state = h;

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
        ulong avoidance = h;
        return new CrowdSimStateDigest(sim.TickCount, position, motion, state, avoidance);
    }

    /// <summary>
    /// 按 tick 对齐两串摘要，返回第一处分歧。共同前缀一致但条数不同时 Tick 为 -2，Section 为空。
    /// 断面顺序：position、motion、state、avoidance。后段接续前段，更早的词变了，后面的断面跟着变，这里只报第一个。
    /// </summary>
    public static CrowdSimDigestDiff DiffDigest(
        IReadOnlyList<CrowdSimStateDigest> left,
        IReadOnlyList<CrowdSimStateDigest> right)
    {
        int common = Math.Min(left.Count, right.Count);
        for (int i = 0; i < common; i++)
        {
            if (TryFirstSection(left[i], right[i], out string section))
                return new CrowdSimDigestDiff(true, left[i].Tick, section);
        }

        if (left.Count != right.Count) return new CrowdSimDigestDiff(true, -2, "");
        return CrowdSimDigestDiff.None;
    }

    public static bool TryFirstSection(in CrowdSimStateDigest left, in CrowdSimStateDigest right, out string section)
    {
        if (left.Position != right.Position) { section = PositionSection; return true; }
        if (left.Motion != right.Motion) { section = MotionSection; return true; }
        if (left.State != right.State) { section = StateSection; return true; }
        if (left.Avoidance != right.Avoidance) { section = AvoidanceSection; return true; }
        section = "";
        return false;
    }

    /// <summary>展示/对拍边界格式（真值帧列是 8 位十六进制）；tick 路径不产字符串。</summary>
    public static string Format(ulong checksum) => checksum.ToString("x8");

    private static Fix64Vec2 Position(CrowdSimSession sim, CrowdMovementKernel? movement, int dense) =>
        movement != null ? movement.Positions[dense]
            : sim.World.Get<WorldPositionCm>(sim.Units.EntityAt(dense)).Value;

    private static CrowdSimulationUnitState State(CrowdSimSession sim, CrowdMovementKernel? movement, int dense) =>
        movement != null ? movement.States[dense]
            : sim.World.Get<CrowdSimulationUnitState>(sim.Units.EntityAt(dense));

    private static uint Mix(uint h, uint v) => (h ^ v) * Prime;

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
