using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace CrowdSimulationTests.Uat;

/// <summary>
/// 逐 tick 统计朝 (tx, ty) 的背离与到达翻转。每推进一个 tick 调一次 <see cref="Observe"/>；会话怎么推进由调用方决定。
/// 背离 = 沿指向目标方向的位移分量 &lt; −eps，不设距离门；翻转 = 到达与非到达之间的切换。
/// </summary>
internal sealed class CrowdProgressTally
{
    private readonly CrowdSimSession _session;
    private readonly double _eps, _tx, _ty;
    private readonly Dictionary<uint, (double X, double Y, bool Arrived)> _prev = new();
    private int _back;

    public CrowdProgressTally(CrowdSimSession session, double epsCm, Fix64 tx, Fix64 ty)
    {
        _session = session;
        _eps = epsCm;
        _tx = tx.ToDouble();
        _ty = ty.ToDouble();
        Snap();
    }

    public int Ticks { get; private set; }
    public int Moved { get; private set; }
    public int Flips { get; private set; }
    public double Back => (double)_back / Math.Max(1, _session.Units.Count * Ticks);

    public void Observe()
    {
        var sim = _session;
        for (int i = 0; i < sim.Units.Count; i++)
        {
            if (!_prev.TryGetValue(sim.Units.HandleAt(i), out var p)) continue;
            var c = CrowdUatKit.PositionAt(sim, i);
            double dx = c.X.ToDouble() - p.X, dy = c.Y.ToDouble() - p.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > _eps)
            {
                Moved++;
                double gx = _tx - p.X, gy = _ty - p.Y, gl = Math.Sqrt(gx * gx + gy * gy);
                double along = (dx * gx + dy * gy) / (gl > 0 ? gl : 1);
                if (along < -_eps) _back++;
            }

            bool arrived = CrowdUatKit.StateAt(sim, i).State == (byte)CrowdUnitState.Arrived;
            if (arrived != p.Arrived) Flips++;
        }

        Ticks++;
        Snap();
    }

    private void Snap()
    {
        var sim = _session;
        _prev.Clear();
        for (int i = 0; i < sim.Units.Count; i++)
        {
            var p = CrowdUatKit.PositionAt(sim, i);
            _prev[sim.Units.HandleAt(i)] = (p.X.ToDouble(), p.Y.ToDouble(), CrowdUatKit.StateAt(sim, i).State == (byte)CrowdUnitState.Arrived);
        }
    }
}

/// <summary>编队行进类 UAT 的公用读数：集结判定、沿航向的队内投影和纵深。</summary>
internal static class CrowdMotionInvariants
{
    public const int SettledPercent = 95;

    public static bool Settled(CrowdSimSession sim)
    {
        int arrived = 0;
        for (int i = 0; i < sim.Units.Count; i++)
        {
            if (CrowdUatKit.StateAt(sim, i).State == (byte)CrowdUnitState.Arrived) arrived++;
        }

        return arrived * 100 >= sim.Units.Count * SettledPercent;
    }

    /// <summary>每个单位沿航向 (hx, hy) 相对全体质心的投影。</summary>
    public static Dictionary<uint, Fix64> HeadingProjection(CrowdSimSession sim, Fix64 hx, Fix64 hy)
    {
        var m = new Dictionary<uint, Fix64>(sim.Units.Count);
        Fix64 c = Fix64.Zero;
        for (int i = 0; i < sim.Units.Count; i++)
        {
            var p = CrowdUatKit.PositionAt(sim, i);
            c += p.X * hx + p.Y * hy;
        }

        c /= Math.Max(1, sim.Units.Count);
        for (int i = 0; i < sim.Units.Count; i++)
        {
            var p = CrowdUatKit.PositionAt(sim, i);
            m[sim.Units.HandleAt(i)] = p.X * hx + p.Y * hy - c;
        }

        return m;
    }

    public static double DepthCells(Dictionary<uint, Fix64> projection, int cellCm)
    {
        Fix64 lo = Fix64.MaxValue, hi = Fix64.MinValue;
        foreach (var v in projection.Values)
        {
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }

        return (hi - lo).ToDouble() / cellCm;
    }

    /// <summary>同一单位在两份投影之间的最大位移，单位格。</summary>
    public static double WorstShiftCells(Dictionary<uint, Fix64> before, Dictionary<uint, Fix64> now, int cellCm)
    {
        double worst = 0;
        foreach (var (id, v) in now)
        {
            if (before.TryGetValue(id, out var p)) worst = Math.Max(worst, Fix64.Abs(v - p).ToDouble() / cellCm);
        }

        return worst;
    }
}
