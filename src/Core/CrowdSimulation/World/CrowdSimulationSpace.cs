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
    public static int ClearanceOf(Fix64 radiusCm, Fix64 avoidanceRadiusScale, int navCellSizeCm)
    {
        Fix64 ratio = PersonalRadiusCm(radiusCm, avoidanceRadiusScale) / Fix64.FromInt(navCellSizeCm) - Fix64.HalfValue;
        int rings = ratio.CeilToInt();
        return 1 + Math.Max(0, rings);
    }

    /// <summary>
    /// 世界厘米 ÷ 格边长，向下取整得到格坐标，不钳制。
    /// <see cref="Fix64.FloorToInt"/>、<see cref="Fix64.ToLong"/>、<see cref="Fix64.ToInt"/> 都是算术右移，
    /// 与 <c>(worldCm / FromInt(navCellSizeCm)).ToLong()</c> 同值；先 <see cref="Fix64.Floor"/> 再取整也同值。
    /// </summary>
    public static int NavCellCoord(Fix64 worldCm, int navCellSizeCm)
        => Fix64.DivPositiveInt(worldCm, navCellSizeCm).FloorToInt();

    /// <summary>厘米坐标 → 导航格号。越界钳到网格内；负坐标钳到 0。</summary>
    public static int CellAt(Fix64 xCm, Fix64 yCm, int navCellSizeCm, int navCellsPerSide)
    {
        int cx = NavCellCoord(xCm, navCellSizeCm);
        int cy = NavCellCoord(yCm, navCellSizeCm);
        if (cx < 0) cx = 0; else if (cx >= navCellsPerSide) cx = navCellsPerSide - 1;
        if (cy < 0) cy = 0; else if (cy >= navCellsPerSide) cy = navCellsPerSide - 1;
        return cy * navCellsPerSide + cx;
    }

    /// <summary>
    /// 厘米坐标 → 导航格号。越界返回 -1。
    /// 点选和部署必须拒绝界外点；钳制会把界外点击当成边界格。
    /// </summary>
    public static int CellAtStrict(Fix64 xCm, Fix64 yCm, int navCellSizeCm, int navCellsPerSide)
    {
        int cx = NavCellCoord(xCm, navCellSizeCm);
        int cy = NavCellCoord(yCm, navCellSizeCm);
        if (cx < 0 || cy < 0 || cx >= navCellsPerSide || cy >= navCellsPerSide) return -1;
        return cy * navCellsPerSide + cx;
    }

    /// <summary>导航上下文 id = 移动类型下标 × <see cref="CrowdSimulationLimits.NavContextLayerStride"/> + 净空（数值键，禁止字符串拼接）。</summary>
    public static int NavContextId(int layerIndex, int clearanceCells)
    {
        if ((uint)layerIndex > 255u) throw new ArgumentOutOfRangeException(nameof(layerIndex));
        if ((uint)(clearanceCells - 1) > 254u) throw new ArgumentOutOfRangeException(nameof(clearanceCells));
        return layerIndex * CrowdSimulationLimits.NavContextLayerStride + clearanceCells;
    }
}
