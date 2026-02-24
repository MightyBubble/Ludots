using System;
using System.Globalization;

namespace Ludots.Core.Navigation.NavMesh
{
    public static class NavAssetPaths
    {
        public static string GetNavTileRelativePath(string mapId, int layer, string profileId, int chunkX, int chunkY)
        {
            if (string.IsNullOrWhiteSpace(mapId)) throw new ArgumentException("mapId is required.", nameof(mapId));
            if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("profileId is required.", nameof(profileId));
            string xDir = "x" + chunkX.ToString("00", CultureInfo.InvariantCulture);
            string safe = SanitizePathSegment(profileId);
            return $"assets/Data/Nav/{mapId}/layer{layer}/profile_{safe}/{xDir}/navtile_{chunkX}_{chunkY}.ntil";
        }

        public static string GetNavTileRelativePathLegacy(string mapId, int layer, int profileIndex, int chunkX, int chunkY)
        {
            if (string.IsNullOrWhiteSpace(mapId)) throw new ArgumentException("mapId is required.", nameof(mapId));
            string xDir = "x" + chunkX.ToString("00", CultureInfo.InvariantCulture);
            return $"assets/Data/Nav/{mapId}/layer{layer}/profile{profileIndex}/{xDir}/navtile_{chunkX}_{chunkY}.ntil";
        }

        public static string GetObstacleRelativePath(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId)) throw new ArgumentException("mapId is required.", nameof(mapId));
            return $"assets/Data/Maps/{mapId}.obstacles.json";
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
