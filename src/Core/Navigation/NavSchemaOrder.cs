using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation
{
    /// <summary>
    /// 导航目录的稳定下标只在这里定义。
    /// profiles 与 pathing agentTypes 的目录下标是 id 的序数排序（Ordinal）。
    /// 区域不在这里排：运行时按下标取值时用的是作者写的 areaId。
    /// maps.&lt;mapId&gt;.agentTypes 是数组，下标就是群体 AgentTypeIndex，不再按键名重排——
    /// 重排会把出生脚本里的 unitType 0 从作者写的第一项上挪走。
    /// </summary>
    public static class NavSchemaOrder
    {
        public static void SortByKey<T>(List<T> items, Func<T, string> key)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(key);
            items.Sort((a, b) => string.CompareOrdinal(key(a), key(b)));
        }

        public static void SortByAreaId(List<NavMeshAreaConfig> areas)
        {
            ArgumentNullException.ThrowIfNull(areas);
            areas.Sort(static (a, b) => a.AreaId.CompareTo(b.AreaId));
        }
    }
}
