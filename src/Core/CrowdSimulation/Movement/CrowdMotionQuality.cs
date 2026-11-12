using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 移动单位的相邻步航向变化和左右转向反复。
/// 航向角只对抽样单位做 Atan2；转向符号每步全员更新，反复率才是相邻步而不是隔很多 tick 的差。
/// 速度缓冲按槽位下标，不按会在退场时压缩的稠密下标。
/// </summary>
internal static class CrowdMotionQuality
{
    public static void Scan(
        int count,
        int sampleOrigin,
        CrowdSimulationUnitState[] states,
        CrowdSimulationKinematics[] kins,
        Fix64[] prevX,
        Fix64[] prevY,
        sbyte[] steer,
        double sinEps2,
        ref double headingSum,
        ref int headingCount,
        ref int flips,
        ref int steerPairs)
    {
        if (count <= 0) return;
        int budget = count < CrowdSimulationLimits.MotionQualitySamplesPerTick
            ? count
            : CrowdSimulationLimits.MotionQualitySamplesPerTick;
        int origin = sampleOrigin % count;
        if (origin < 0) origin += count;
        for (int i = 0; i < count; i++)
        {
            int slot = states[i].Slot;
            if ((uint)slot >= (uint)prevX.Length)
            {
                throw new InvalidOperationException($"运动质量槽位 {slot} 超出缓冲 {prevX.Length}。");
            }

            if (states[i].State != (byte)CrowdUnitState.Moving)
            {
                prevX[slot] = default;
                prevY[slot] = default;
                steer[slot] = 0;
                continue;
            }

            int along = i - origin;
            if (along < 0) along += count;
            var vel = kins[i].Velocity;
            Observe(
                vel.X, vel.Y,
                ref prevX[slot], ref prevY[slot], ref steer[slot],
                sinEps2, along < budget,
                ref headingSum, ref headingCount, ref flips, ref steerPairs);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Observe(
        Fix64 vx, Fix64 vy,
        ref Fix64 px, ref Fix64 py,
        ref sbyte steer,
        double sinEps2,
        bool sampleHeading,
        ref double headingSum,
        ref int headingCount,
        ref int flips,
        ref int steerPairs)
    {
        if (vx.RawValue == 0 && vy.RawValue == 0)
        {
            px = default;
            py = default;
            steer = 0;
            return;
        }

        if (px.RawValue == 0 && py.RawValue == 0)
        {
            px = vx;
            py = vy;
            return;
        }

        double ax = (double)px;
        double ay = (double)py;
        double bx = (double)vx;
        double by = (double)vy;
        double cross = ax * by - ay * bx;
        double dot = ax * bx + ay * by;
        if (sampleHeading)
        {
            headingSum += Math.Abs(Math.Atan2(cross, dot) * (180d / Math.PI));
            headingCount++;
        }

        // 钝角一定超过转角阈值。锐角用 sin 比较，避免对全员做 Atan2。
        bool turned = dot < 0 || cross * cross > sinEps2 * (ax * ax + ay * ay) * (bx * bx + by * by);
        if (turned)
        {
            sbyte sign = cross > 0 ? (sbyte)1 : (sbyte)-1;
            if (steer != 0)
            {
                steerPairs++;
                if (steer != sign) flips++;
            }

            steer = sign;
        }

        px = vx;
        py = vy;
    }
}
