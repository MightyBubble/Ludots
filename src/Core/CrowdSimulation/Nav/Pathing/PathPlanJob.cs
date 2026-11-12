using System;
using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Nav.Pathing;

/// <summary>组规划答复的场从哪来。</summary>
internal enum PathPlanReply : byte
{
    None,
    /// <summary>工人新建的场,挂在 <see cref="PathPlanGroup.Flow"/>。</summary>
    New,
    /// <summary>同一指令里同 (导航, 目标) 的另一组的场已经盖住走廊,见 <see cref="PathPlanGroup.SameAs"/>。</summary>
    Same,
    /// <summary>规划时钉住的缓存场已经盖住走廊。</summary>
    Cached,
}

/// <summary>
/// 一条指令里的一组。输入由规划器填,输出由工人写;答复取走前双方都不能动。
/// </summary>
internal class PathPlanGroup
{
    public int NavId;
    public int Goal;
    /// <summary>每 (tile, 层) 一个起点,(格, 层) 成对。</summary>
    public int[] Origins = Array.Empty<int>();
    public int OriginCount;
    /// <summary>领队起点(底层),只挂在桶里净空最严的组上。-1 = 无。</summary>
    public int LeaderStart = -1;
    /// <summary>规划时钉住的同 (导航, 目标) 缓存场的掩码。null = 缓存里没有。</summary>
    public byte[]? CachedMask;
    /// <summary>目标 tile + 各起点走廊 + 领队走廊。工人写。</summary>
    public byte[] Corridor = Array.Empty<byte>();

    public PathPlanReply Reply;
    /// <summary><see cref="PathPlanReply.Same"/> 时指向本指令组表里那一组的下标。</summary>
    public int SameAs = -1;
    public FlowField? Flow;
}

/// <summary>一条指令的组规划:各组按组表顺序,领队按 <see cref="Leaders"/> 顺序。</summary>
internal sealed class PathPlanJob
{
    public PathPlanJob(IReadOnlyList<PathPlanGroup> groups)
    {
        Groups = groups;
    }

    public readonly IReadOnlyList<PathPlanGroup> Groups;
    /// <summary>挂了领队起点的组在 <see cref="Groups"/> 里的下标,按领队请求顺序。</summary>
    public int[] Leaders = new int[4];
    public int LeaderCount;

    public void AddLeader(int groupIndex)
    {
        if (LeaderCount == Leaders.Length) Array.Resize(ref Leaders, Leaders.Length * 2);
        Leaders[LeaderCount++] = groupIndex;
    }
}
