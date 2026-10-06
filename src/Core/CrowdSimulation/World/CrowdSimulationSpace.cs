using System;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// 导航网格几何原语（形状对齐参考实现 core/space.js，数学走 Ludots 定点体系）。
/// 内核坐标一律厘米（Fix64），与 WorldPositionCm 同一单位制，ECS 边界不做单位换算。
/// </summary>
public static class CrowdSimulationSpace
{
    /// <summary>个人（避让 / 碰撞）半径（厘米）。</summary>
    public static Fix64 PersonalRadiusCm(Fix64 radiusCm, Fix64 avoidanceRadiusScale)
        => radiusCm * avoidanceRadiusScale;

    /// <summary>
    /// 半径级推导的导航净空（到最近阻挡格的切比雪夫格数）。1 = 本格可走；
    /// 只有圆盘宽过半格才需要更多环。就近取环最多残留半格重叠，由 Motor 的
    /// 圆盘-阻挡格精确接触兜底。净空是几何推导量，不是配置项。
    /// </summary>
    public static int ClearanceOf(Fix64 radiusCm, Fix64 avoidanceRadiusScale, int cellSizeCm)
    {
        Fix64 ratio = PersonalRadiusCm(radiusCm, avoidanceRadiusScale) / Fix64.FromInt(cellSizeCm) - Fix64.HalfValue;
        int rings = ratio.CeilToInt();
        return 1 + Math.Max(0, rings);
    }

    /// <summary>
    /// 厘米坐标 → 格号（钳制在网格内）。向下取整后钳制；参考实现是先向零截断再钳制，
    /// 两种取整在钳制后结果相同（负坐标都被钳到 0）。
    /// </summary>
    public static int CellAt(int cellCount, int cellSizeCm, Fix64 x, Fix64 y)
    {
        int cx = (x / Fix64.FromInt(cellSizeCm)).FloorToInt();
        int cy = (y / Fix64.FromInt(cellSizeCm)).FloorToInt();
        if (cx < 0) cx = 0; else if (cx >= cellCount) cx = cellCount - 1;
        if (cy < 0) cy = 0; else if (cy >= cellCount) cy = cellCount - 1;
        return cy * cellCount + cx;
    }

    /// <summary>导航上下文 id = 移动类型下标 × 256 + 净空（数值键，禁止字符串拼接）。</summary>
    public static int NavContextId(int layerIndex, int clearanceCells)
    {
        if ((uint)layerIndex > 255u) throw new ArgumentOutOfRangeException(nameof(layerIndex));
        if ((uint)(clearanceCells - 1) > 254u) throw new ArgumentOutOfRangeException(nameof(clearanceCells));
        return layerIndex * 256 + clearanceCells;
    }
}
