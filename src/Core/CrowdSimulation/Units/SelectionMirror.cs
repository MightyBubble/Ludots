using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 选中态到呈现层的镜像:sim 的 Selected 字节是权威(进指令日志、可回放),
/// 这里把它同步进实体集合仓的 "selected" 集合——presenter 规则监听
/// EntityCollectionMemberAdded/Removed 显隐选中环,校验码不含选中,镜像不进对拍口径。
/// </summary>
public static class SelectionMirror
{
    public const string CollectionKey = "selected";
    public static readonly EntityCollectionDescriptor Descriptor = EntityCollectionDescriptor.Create(
        CollectionKey, EntityCollectionSourceKind.Explicit, EntityCollectionRoleKind.Display);

    /// <summary>把当前入选单位整体替换进选中集合(指令执行末尾调用;无接线时直接返回)。</summary>
    public static void Sync(CrowdSimSession sim)
    {
        var wiring = sim.Presentation;
        if (wiring == null || wiring.SelectionOwner == Entity.Null) return;
        int selected = sim.SelectedCount;
        if (selected <= 0)
        {
            wiring.Collections.Replace(
                wiring.SelectionOwner, wiring.SelectedCollectionKeyId, Descriptor, ReadOnlySpan<Entity>.Empty);
            return;
        }

        var members = new List<Entity>(selected);
        for (int i = 0; i < sim.Units.Count && members.Count < selected; i++)
        {
            var entity = sim.Units.EntityAt(i);
            if (sim.World.Get<CrowdSimulationUnitState>(entity).Selected != 0) members.Add(entity);
        }

        wiring.Collections.Replace(
            wiring.SelectionOwner, wiring.SelectedCollectionKeyId, Descriptor,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(members));
    }
}
