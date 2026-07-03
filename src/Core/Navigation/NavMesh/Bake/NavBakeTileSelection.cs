using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Navigation.Terrain;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public static class NavBakeTileSelection
    {
        public static IReadOnlyList<NavBakeTileCoord> Resolve(
            LogicTerrainField terrain,
            string? dirtyJson,
            bool includeNeighbors,
            bool dirtyOnly)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));

            bool hasDirty = !string.IsNullOrWhiteSpace(dirtyJson);
            if (!hasDirty)
            {
                return dirtyOnly ? Array.Empty<NavBakeTileCoord>() : AllTiles(terrain);
            }

            string[] keys = JsonSerializer.Deserialize<string[]>(dirtyJson!)
                ?? Array.Empty<string>();
            if (keys.Length == 0)
            {
                return dirtyOnly ? Array.Empty<NavBakeTileCoord>() : AllTiles(terrain);
            }

            var set = new HashSet<NavBakeTileCoord>();
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                string[] parts = key.Split(',');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], out int cx) ||
                    !int.TryParse(parts[1], out int cy))
                {
                    throw new InvalidOperationException($"Dirty tile key '{key}' must use 'chunkX,chunkY'.");
                }

                AddIfInRange(set, terrain, cx, cy);
                if (includeNeighbors)
                {
                    AddIfInRange(set, terrain, cx - 1, cy);
                    AddIfInRange(set, terrain, cx + 1, cy);
                    AddIfInRange(set, terrain, cx, cy - 1);
                    AddIfInRange(set, terrain, cx, cy + 1);
                }
            }

            var result = new List<NavBakeTileCoord>(set);
            result.Sort(static (a, b) =>
            {
                int y = a.ChunkY.CompareTo(b.ChunkY);
                return y != 0 ? y : a.ChunkX.CompareTo(b.ChunkX);
            });
            return result;
        }

        public static IReadOnlyList<NavBakeTileCoord> AllTiles(LogicTerrainField terrain)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            var targets = new List<NavBakeTileCoord>(checked(terrain.WidthChunks * terrain.HeightChunks));
            for (int cy = 0; cy < terrain.HeightChunks; cy++)
            {
                for (int cx = 0; cx < terrain.WidthChunks; cx++)
                {
                    targets.Add(new NavBakeTileCoord(cx, cy));
                }
            }

            return targets;
        }

        private static void AddIfInRange(HashSet<NavBakeTileCoord> set, LogicTerrainField terrain, int cx, int cy)
        {
            if (cx < 0 || cy < 0 || cx >= terrain.WidthChunks || cy >= terrain.HeightChunks)
            {
                return;
            }

            set.Add(new NavBakeTileCoord(cx, cy));
        }
    }
}
