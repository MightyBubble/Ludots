using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>移动指令的落点模式(order.js 的 ORDER_MODE 移植)。</summary>
public enum CrowdOrderMode : byte
{
    /// <summary>拖线摆阵(或显式关 magic box):按线宽/朝向整队。</summary>
    Formation = 0,
    /// <summary>点在选中包围盒内(加 pad):向该点聚拢。</summary>
    Converge = 1,
    /// <summary>点在包围盒外且展开不超上限:整体平移、保持相对位置。</summary>
    Preserve = 2,
}

/// <summary>
/// magic-box 判定(SC2 式,chooseMode 移植):显式拖线永远整队;点在(加边)包围盒内聚拢;
/// 盒外保持相对位置,除非展开宽度超过上限——那时也整队。
/// </summary>
public static class CrowdOrderModeChooser
{
    public static CrowdOrderMode Choose(
        Fix64 boxMinX, Fix64 boxMinY, Fix64 boxMaxX, Fix64 boxMaxY,
        Fix64 clickX, Fix64 clickY, Fix64 padCm, Fix64 maxSpreadCm,
        bool hasDragLine, bool autoDisabled)
    {
        if (hasDragLine || autoDisabled) return CrowdOrderMode.Formation;
        if (clickX >= boxMinX - padCm && clickX <= boxMaxX + padCm &&
            clickY >= boxMinY - padCm && clickY <= boxMaxY + padCm)
        {
            return CrowdOrderMode.Converge;
        }

        Fix64 dx = boxMaxX - boxMinX, dy = boxMaxY - boxMinY;
        Fix64 spread = CrowdFix.Hypot(dx, dy);
        return spread <= maxSpreadCm ? CrowdOrderMode.Preserve : CrowdOrderMode.Formation;
    }
}

/// <summary>一条玩家移动指令的聚合:语义(模式/阵型/朝向/线宽)与它的虚拟领队列表。组只引用,不复制。</summary>
public sealed class CrowdOrder
{
    public required int Id { get; init; }
    public required int Player { get; init; }
    public required CrowdOrderMode Mode { get; init; }
    /// <summary>阵型 id(配置 formations[].id;未知回退第一项——参考实现同)。</summary>
    public required string ShapeId { get; init; }
    /// <summary>拖线朝向(单位向量,世界系)与线宽(米);无拖线时 face=null。</summary>
    public Fix64Vec2? Face { get; init; }
    public Fix64 FaceWidthM { get; init; }
    /// <summary>点击目标格(去重判定用,-1 表示无)。</summary>
    public int ClickCell { get; set; } = -1;
    /// <summary>落点足印半径(格,规划器按编队宽度设置,到达判定的近邻域)。</summary>
    public int ReachCells { get; set; } = 1;
    /// <summary>去重键(形状/自动/朝向/线宽);同玩家同成员同目标同键的重复指令直接复用。</summary>
    public string DedupKey { get; set; } = string.Empty;
    public List<CrowdOrderGroupLink> Groups { get; } = new();
    public List<CrowdLeader> Leaders { get; } = new();
    public bool Done => Groups.Count == 0;
}

/// <summary>指令↔组反向链接(组注册表的组 id + 该组在此指令下的目标格)。</summary>
public sealed class CrowdOrderGroupLink
{
    public required int GroupId { get; init; }
    public int GoalCell { get; set; }
}

/// <summary>指令簿:创建、修剪(空组指令消失)、清空。序号自增稳定(回放可复现)。</summary>
public sealed class CrowdOrderBook
{
    private readonly List<CrowdOrder> _list = new();
    private int _seq;

    public IReadOnlyList<CrowdOrder> List => _list;

    public CrowdOrder Create(int player, CrowdOrderMode mode, string shapeId, Fix64Vec2? face, Fix64 widthM)
    {
        var o = new CrowdOrder { Id = ++_seq, Player = player, Mode = mode, ShapeId = shapeId, Face = face, FaceWidthM = widthM };
        _list.Add(o);
        return o;
    }

    /// <summary>丢掉已释放/改派的组与用不到的领队;没有活组的指令消失。</summary>
    public void Prune(CrowdNavGroupSet groups)
    {
        int w = 0;
        for (int i = 0; i < _list.Count; i++)
        {
            var o = _list[i];
            o.Groups.RemoveAll(l => !groups.TryGet(l.GroupId, out var g) || g.Count <= 0 || g.OrderId != o.Id);
            if (o.Groups.Count == 0) continue;
            o.Leaders.RemoveAll(l => !o.Groups.Exists(gl => groups.TryGet(gl.GroupId, out var g) && g.Leader == l));
            _list[w++] = o;
        }

        if (w < _list.Count) _list.RemoveRange(w, _list.Count - w);
    }

    public void Clear()
    {
        _list.Clear();
        _seq = 0;
    }
}
