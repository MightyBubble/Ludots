using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Navigation.NavMesh;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class RaylibNavMeshDebugRenderer
    {
        private const float CmToMeters = 0.01f;
        private const float BaseYOffset = 0.08f;
        private const float StoreYOffset = 0.025f;

        public int LastStoreCount { get; private set; }
        public int LastTileCount { get; private set; }
        public int LastTriangleCount { get; private set; }

        public void Draw(IReadOnlyList<NavQueryServiceStoreSnapshot> stores)
        {
            LastStoreCount = 0;
            LastTileCount = 0;
            LastTriangleCount = 0;

            if (stores == null || stores.Count == 0)
            {
                return;
            }

            for (int i = 0; i < stores.Count; i++)
            {
                NavQueryServiceStoreSnapshot snapshot = stores[i];
                NavTileStore store = snapshot.Store;
                LastStoreCount++;
                float yOffset = BaseYOffset + i * StoreYOffset;

                IReadOnlyList<NavTileId> knownTileIds = store.KnownTileIds;
                if (knownTileIds.Count > 0)
                {
                    for (int t = 0; t < knownTileIds.Count; t++)
                    {
                        DrawTile(store.GetOrLoad(knownTileIds[t]), snapshot.Layer, snapshot.Profile, yOffset);
                    }
                }
                else
                {
                    NavTile[] loadedTiles = store.SnapshotLoadedTiles();
                    for (int t = 0; t < loadedTiles.Length; t++)
                    {
                        DrawTile(loadedTiles[t], snapshot.Layer, snapshot.Profile, yOffset);
                    }
                }
            }
        }

        private void DrawTile(NavTile tile, int layer, int profile, float yOffset)
        {
            if (tile == null)
            {
                return;
            }

            LastTileCount++;
            LastTriangleCount += tile.TriangleCount;

            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int a = tile.TriA[i];
                int b = tile.TriB[i];
                int c = tile.TriC[i];
                Vector3 va = ToVector3(tile, a, yOffset);
                Vector3 vb = ToVector3(tile, b, yOffset);
                Vector3 vc = ToVector3(tile, c, yOffset);
                byte areaId = i < tile.TriAreaIds.Length ? tile.TriAreaIds[i] : (byte)0;
                Color edge = ResolveAreaColor(areaId, layer, profile);

                Rl.DrawLine3D(va, vb, edge);
                Rl.DrawLine3D(vb, vc, edge);
                Rl.DrawLine3D(vc, va, edge);
            }
        }

        private static Vector3 ToVector3(NavTile tile, int vertexIndex, float yOffset)
        {
            if ((uint)vertexIndex >= (uint)tile.VertexCount)
            {
                throw new InvalidOperationException($"NavTile {tile.TileId} contains a triangle vertex index outside VertexCount: {vertexIndex}.");
            }

            return new Vector3(
                (tile.OriginXcm + tile.VertexXcm[vertexIndex]) * CmToMeters,
                tile.VertexYcm[vertexIndex] * CmToMeters + yOffset,
                (tile.OriginZcm + tile.VertexZcm[vertexIndex]) * CmToMeters);
        }

        private static Color ResolveAreaColor(byte areaId, int layer, int profile)
        {
            int seed = areaId * 73 + layer * 41 + profile * 29;
            byte red = (byte)(80 + seed % 136);
            byte green = (byte)(110 + (seed * 3 + 37) % 116);
            byte blue = (byte)(120 + (seed * 5 + 19) % 106);
            return new Color(red, green, blue, 230);
        }
    }
}
