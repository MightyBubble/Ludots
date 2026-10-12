using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation
{
    /// <summary>
    /// 区域目录按下标取值时用的是作者写的 areaId，这里只按 areaId 冻结一份稳定列表。
    /// profiles 与 pathing agentTypes 不在这里重排：目录下标是合并后的 JsonObject 属性顺序。
    /// maps.&lt;mapId&gt;.navProfiles 是数组，烘焙和装载瓦片库按这个数组的顺序，也不重排。
    /// </summary>
    public static class NavSchemaOrder
    {
        public static void SortByAreaId(List<NavMeshAreaConfig> areas)
        {
            ArgumentNullException.ThrowIfNull(areas);
            areas.Sort(static (a, b) => a.AreaId.CompareTo(b.AreaId));
        }
    }
}
