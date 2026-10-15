using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// maps.&lt;mapId&gt;.solver 是这张图跑哪一种移动求解器的唯一声明。
    /// 求解器板写在 maps.&lt;mapId&gt;.boards 里，而且只能有一块；没写时用 <see cref="MapManager.ResolveRootBoard"/>。
    /// </summary>
    public static class NavSolverDeclaration
    {
        public const string NavMesh = "navmesh";
        public const string MassNavigation = "mass-navigation";
        public const string CrowdSimulation = "crowd-simulation";

        public static bool IsKnown(string solver)
            => string.Equals(solver, NavMesh, StringComparison.Ordinal)
                || string.Equals(solver, MassNavigation, StringComparison.Ordinal)
                || string.Equals(solver, CrowdSimulation, StringComparison.Ordinal);

        public static string AllowedList => "navmesh、mass-navigation、crowd-simulation";

        private static readonly string[] NavMeshMapKeys = { "boards", "navProfiles", "navAreas" };
        private static readonly string[] MassNavigationMapKeys = { "boards", "navProfiles", "navAreas" };
        private static readonly string[] CrowdSimulationMapKeys = { "boards", "terrainAreas", "navAreas", "logicTerrainAsset", "jumpLinkBake" };

        /// <summary>
        /// maps.&lt;mapId&gt; 除 solver 外可以写的键。每种求解器只收自己读的键，别的求解器的键写在这张图上是装载错误。
        /// </summary>
        public static IReadOnlyList<string> OptionalMapKeys(string solver) => solver switch
        {
            NavMesh => NavMeshMapKeys,
            MassNavigation => MassNavigationMapKeys,
            CrowdSimulation => CrowdSimulationMapKeys,
            _ => throw new InvalidOperationException($"maps.<mapId>.solver 必须是 {AllowedList}，当前是 \"{solver}\"。"),
        };

        public static bool Is(NavMeshBakeConfig navmesh, string mapId, string solver)
        {
            if (navmesh?.Maps == null || string.IsNullOrWhiteSpace(mapId) || string.IsNullOrWhiteSpace(solver))
            {
                return false;
            }

            return navmesh.Maps.TryGetValue(mapId, out NavMapNavBoardsConfig map)
                && map != null
                && string.Equals(map.Solver, solver, StringComparison.Ordinal);
        }

        public static NavMapNavBoardsConfig RequireMap(NavMeshBakeConfig navmesh, string mapId)
        {
            if (navmesh?.Maps == null ||
                string.IsNullOrWhiteSpace(mapId) ||
                !navmesh.Maps.TryGetValue(mapId, out NavMapNavBoardsConfig map) ||
                map == null)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId} 没有登记。");
            }

            if (!IsKnown(map.Solver))
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.maps.{mapId}.solver 必须是 {AllowedList}。");
            }

            return map;
        }

        /// <summary>
        /// 已声明的那一块板的名字。boards 缺省时返回 null，调用方改用 <see cref="MapManager.ResolveRootBoard"/>。
        /// </summary>
        public static string DeclaredBoardName(NavMapNavBoardsConfig mapNav, string mapId)
        {
            ArgumentNullException.ThrowIfNull(mapNav);
            int count = mapNav.Boards?.Count ?? 0;
            if (count == 0)
            {
                return null;
            }

            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.maps.{mapId}.boards 声明了 {count} 块板。一张图只能有一块求解器板。");
            }

            foreach (KeyValuePair<string, NavTileGridConfig> entry in mapNav.Boards)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.boards 的板名不能为空。");
                }

                return entry.Key;
            }

            return null;
        }

        public static BoardConfig RequireBoard(MapConfig map, NavMapNavBoardsConfig mapNav)
        {
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(mapNav);
            string declared = DeclaredBoardName(mapNav, map.Id);
            if (string.IsNullOrWhiteSpace(declared))
            {
                return MapManager.ResolveRootBoardFor(map, map.Id);
            }

            BoardConfig found = null;
            if (map.Boards != null)
            {
                for (int i = 0; i < map.Boards.Count; i++)
                {
                    BoardConfig board = map.Boards[i];
                    if (board == null || !string.Equals(board.Name, declared, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (found != null)
                    {
                        throw new InvalidOperationException($"地图 \"{map.Id}\" 有多块名为 \"{declared}\" 的板。");
                    }

                    found = board;
                }
            }

            if (found == null)
            {
                throw new InvalidOperationException($"地图 \"{map.Id}\" 的求解器板 \"{declared}\" 不在 Boards 里。");
            }

            return found;
        }
    }
}
