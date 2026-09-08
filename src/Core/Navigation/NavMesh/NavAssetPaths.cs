using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ludots.Core.Navigation.NavMesh
{
    public static class NavAssetPaths
    {
        public static string GetNavTileRelativePath(string mapId, int layer, string profileId, int chunkX, int chunkY)
            => GetNavTileRelativePath(mapId, boardId: null, layer, profileId, chunkX, chunkY);

        /// <summary>
        /// Nav tile artifact path. A board-scoped map carries the board id so two boards
        /// can share local tile coordinates without overwriting each other; a single-board
        /// map keeps the historical path so already-baked artifacts stay loadable.
        /// </summary>
        public static string GetNavTileRelativePath(
            string mapId,
            string? boardId,
            int layer,
            string profileId,
            int chunkX,
            int chunkY)
        {
            if (string.IsNullOrWhiteSpace(mapId)) throw new ArgumentException("mapId is required.", nameof(mapId));
            if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("profileId is required.", nameof(profileId));
            string xDir = "x" + chunkX.ToString("00", CultureInfo.InvariantCulture);
            string safe = SanitizePathSegment(profileId);
            string boardSegment = string.IsNullOrWhiteSpace(boardId)
                ? string.Empty
                : $"board_{SanitizePathSegment(boardId)}/";
            return $"assets/Data/Nav/{mapId}/{boardSegment}layer{layer}/profile_{safe}/{xDir}/navtile_{chunkX}_{chunkY}.ntil";
        }

        /// <summary>
        /// Whether a map's nav artifacts are addressed per board. Both the bake writers and
        /// the runtime loader must agree, otherwise one writes under a board segment the other
        /// never reads. A map is board-scoped exactly when it declares more than one board
        /// that carries a NavTileGrid declaration.
        /// </summary>
        public static bool IsBoardScoped(IReadOnlyList<bool> boardHasNavTileGrid)
        {
            if (boardHasNavTileGrid == null) throw new ArgumentNullException(nameof(boardHasNavTileGrid));
            int count = 0;
            for (int i = 0; i < boardHasNavTileGrid.Count; i++)
            {
                if (boardHasNavTileGrid[i] && ++count > 1) return true;
            }

            return false;
        }

        private static string SanitizePathSegment(string raw)
        {
            if (raw == null) return "null";
            raw = raw.Trim();
            if (raw.Length == 0) return "empty";

            Span<char> buf = raw.Length <= 128 ? stackalloc char[raw.Length] : new char[raw.Length];
            int w = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if ((c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '_' || c == '-')
                {
                    buf[w++] = c;
                }
                else
                {
                    buf[w++] = '_';
                }
            }
            return new string(buf[..w]);
        }
    }
}
