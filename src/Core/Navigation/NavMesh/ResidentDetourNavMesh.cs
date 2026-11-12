using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Ludots.Core.Navigation.NavMesh
{
    internal sealed class DetourQueryScratch
    {
        public long[] PathRefs = Array.Empty<long>();
        public long[] RayRefs = Array.Empty<long>();
        public DtStraightPath[] Straight = Array.Empty<DtStraightPath>();

        public void Ensure(int capacity)
        {
            if (PathRefs.Length < capacity)
            {
                PathRefs = new long[capacity];
            }

            if (RayRefs.Length < capacity)
            {
                RayRefs = new long[capacity];
            }

            if (Straight.Length < capacity)
            {
                Straight = new DtStraightPath[capacity];
            }
        }
    }

    internal readonly struct SurfaceSnap
    {
        public readonly bool Ok;
        public readonly int Xcm;
        public readonly int Zcm;

        public SurfaceSnap(bool ok, int xcm, int zcm)
        {
            Ok = ok;
            Xcm = xcm;
            Zcm = zcm;
        }
    }

    /// <summary>
    /// 发布（Replace / Unload / Reload）和查询都在游戏线程，这份网格不另加锁。
    /// 修订号、已加载数、层和瓦片尺寸都没变就复用；变了按瓦片引用差量 AddTile/RemoveTile，槽位不够或原点变了才整网重建一次。
    /// </summary>
    internal static class ResidentDetourNavMesh
    {
        private static readonly ConditionalWeakTable<NavTileStore, Slot> Slots = new();

        public static NavPathResult FindPath(
            NavTileStore store,
            int layer,
            IDtQueryFilter filter,
            int tileWidthCm,
            int tileHeightCm,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPortals)
        {
            Slot slot = Slots.GetValue(store, static _ => new Slot());
            DtNavMesh? mesh = slot.Ensure(store, layer, tileWidthCm, tileHeightCm);
            if (mesh == null || slot.Query == null)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Mathematics.FixedPoint.Fix64.Zero);
            }

            return DetourNavQueryEngine.QueryExisting(
                slot.Query,
                filter,
                slot.Scratch,
                tileWidthCm / 100f,
                tileHeightCm / 100f,
                startXcm,
                startZcm,
                goalXcm,
                goalZcm,
                maxPortals);
        }

        public static SurfaceSnap Snap(
            NavTileStore store,
            int layer,
            IDtQueryFilter filter,
            int tileWidthCm,
            int tileHeightCm,
            int worldXcm,
            int worldZcm)
        {
            Slot slot = Slots.GetValue(store, static _ => new Slot());
            DtNavMesh? mesh = slot.Ensure(store, layer, tileWidthCm, tileHeightCm);
            if (mesh == null || slot.Query == null)
            {
                return default;
            }

            // ClosestPointOnPoly，不用 Boundary：后者总是投到边上，队形间距会被拉到三角形边缘。

            float tileWidthM = tileWidthCm / 100f;
            float tileHeightM = tileHeightCm / 100f;
            var extents = new RcVec3f(
                MathF.Max(1f, tileWidthM * 0.5f),
                256f,
                MathF.Max(1f, tileHeightM * 0.5f));
            var probe = new RcVec3f(worldXcm / 100f, 0f, worldZcm / 100f);
            DtStatus status = slot.Query.FindNearestPoly(probe, extents, filter, out long polyRef, out _, out _);
            if (status.Failed() || polyRef == 0)
            {
                return default;
            }

            status = slot.Query.ClosestPointOnPoly(polyRef, probe, out RcVec3f closest, out _);
            if (status.Failed())
            {
                return default;
            }

            return new SurfaceSnap(
                true,
                (int)MathF.Round(closest.X * 100f),
                (int)MathF.Round(closest.Z * 100f));
        }

        public static DtNavMesh? MeshOrNull(NavTileStore store)
        {
            if (store == null || !Slots.TryGetValue(store, out Slot? slot) || slot == null)
            {
                return null;
            }

            return slot.Mesh;
        }

        public static int BuildCount(NavTileStore store)
        {
            if (store == null || !Slots.TryGetValue(store, out Slot? slot) || slot == null)
            {
                return 0;
            }

            return slot.BuildCount;
        }

        internal static void ReserveTiles(NavTileStore store, int maxTiles)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (maxTiles < 0) throw new ArgumentOutOfRangeException(nameof(maxTiles));
            if (!Slots.TryAdd(store, new Slot(maxTiles)))
            {
                throw new InvalidOperationException("Resident mesh for this store already exists.");
            }
        }

        private static int CapacityFor(int kept)
        {
            int doubled = kept <= int.MaxValue / 2 ? kept * 2 : kept;
            int padded = kept <= int.MaxValue - 8 ? kept + 8 : kept;
            return Math.Max(1, Math.Max(doubled, padded));
        }

        private struct InstalledTile
        {
            public NavTile Tile;
            public long Ref;
        }

        private sealed class Slot
        {
            public bool Synced;
            public DtNavMesh? Mesh;
            public DtNavMeshQuery? Query;
            public uint Revision;
            public int LoadedCount;
            public int Layer;
            public int TileWidthCm;
            public int TileHeightCm;
            public long BaseOriginXcm;
            public long BaseOriginZcm;
            public int MaxTiles;
            public int BuildCount;
            public readonly DetourQueryScratch Scratch = new();
            public readonly Dictionary<NavTileId, InstalledTile> Installed = new();
            public readonly List<NavTile> Filtered = new();
            public readonly HashSet<NavTileId> Seen = new();
            public readonly List<NavTileId> RemoveIds = new();
            public NavTile[] ScratchTiles = Array.Empty<NavTile>();
            // 创建时给定。未给定则按瓦片数留余量。给定 0 时没有空槽，AddTile 拒绝。
            private readonly int? _maxTiles;

            public Slot()
            {
            }

            public Slot(int maxTiles)
            {
                _maxTiles = maxTiles;
            }

            public DtNavMesh? Ensure(NavTileStore store, int layer, int tileWidthCm, int tileHeightCm)
            {
                store.ReadPublication(out uint revision, out int loadedCount);
                if (Synced &&
                    Revision == revision &&
                    LoadedCount == loadedCount &&
                    Layer == layer &&
                    TileWidthCm == tileWidthCm &&
                    TileHeightCm == tileHeightCm)
                {
                    return Mesh;
                }

                if (ScratchTiles.Length < loadedCount)
                {
                    ScratchTiles = new NavTile[Math.Max(loadedCount, 4)];
                }

                int copied = store.CopyLoadedTiles(ScratchTiles, out uint copiedRevision);
                Collect(ScratchTiles, copied, layer, tileWidthCm, tileHeightCm, out long originXcm, out long originZcm, out int maxPolys);
                if (Mesh != null &&
                    Layer == layer &&
                    TileWidthCm == tileWidthCm &&
                    TileHeightCm == tileHeightCm &&
                    (Filtered.Count == 0 || (BaseOriginXcm == originXcm && BaseOriginZcm == originZcm)) &&
                    TryIncremental(tileWidthCm, tileHeightCm))
                {
                    PublishSync(copiedRevision, copied, layer, tileWidthCm, tileHeightCm);
                    return Mesh;
                }

                if (Filtered.Count == 0)
                {
                    Mesh = null;
                    Query = null;
                    Installed.Clear();
                    MaxTiles = 0;
                    PublishSync(copiedRevision, copied, layer, tileWidthCm, tileHeightCm);
                    return null;
                }

                TryRebuild(originXcm, originZcm, maxPolys, tileWidthCm, tileHeightCm);
                PublishSync(copiedRevision, copied, layer, tileWidthCm, tileHeightCm);
                return Mesh;
            }

            private void PublishSync(uint revision, int loadedCount, int layer, int tileWidthCm, int tileHeightCm)
            {
                Synced = true;
                Revision = revision;
                LoadedCount = loadedCount;
                Layer = layer;
                TileWidthCm = tileWidthCm;
                TileHeightCm = tileHeightCm;
            }

            private void Collect(
                NavTile[] tiles,
                int count,
                int layer,
                int tileWidthCm,
                int tileHeightCm,
                out long originXcm,
                out long originZcm,
                out int maxPolys)
            {
                Filtered.Clear();
                originXcm = long.MaxValue;
                originZcm = long.MaxValue;
                maxPolys = 0;
                for (int i = 0; i < count; i++)
                {
                    NavTile tile = tiles[i];
                    if (tile == null || tile.TileId.Layer != layer || tile.TriangleCount == 0 || tile.VertexCount == 0)
                    {
                        continue;
                    }

                    Filtered.Add(tile);
                    maxPolys = Math.Max(maxPolys, tile.TriangleCount);
                    originXcm = Math.Min(originXcm, (long)tile.OriginXcm - (long)tile.TileId.ChunkX * tileWidthCm);
                    originZcm = Math.Min(originZcm, (long)tile.OriginYcm - (long)tile.TileId.ChunkY * tileHeightCm);
                }
            }

            private bool TryIncremental(int tileWidthCm, int tileHeightCm)
            {
                DtNavMesh? mesh = Mesh;
                if (mesh == null)
                {
                    return false;
                }

                Seen.Clear();
                int brandNew = 0;
                for (int i = 0; i < Filtered.Count; i++)
                {
                    NavTile tile = Filtered[i];
                    Seen.Add(tile.TileId);
                    if (Installed.TryGetValue(tile.TileId, out InstalledTile prev) && ReferenceEquals(prev.Tile, tile))
                    {
                        continue;
                    }

                    if (!Installed.ContainsKey(tile.TileId))
                    {
                        brandNew++;
                    }
                }

                int toRemove = 0;
                foreach (KeyValuePair<NavTileId, InstalledTile> pair in Installed)
                {
                    if (!Seen.Contains(pair.Key))
                    {
                        toRemove++;
                    }
                }

                int finalCount = Installed.Count - toRemove + brandNew;
                if (finalCount > MaxTiles)
                {
                    return false;
                }

                RemoveIds.Clear();
                foreach (KeyValuePair<NavTileId, InstalledTile> pair in Installed)
                {
                    if (!Seen.Contains(pair.Key))
                    {
                        RemoveIds.Add(pair.Key);
                    }
                }

                for (int i = 0; i < RemoveIds.Count; i++)
                {
                    RemoveInstalled(mesh, RemoveIds[i]);
                }

                for (int i = 0; i < Filtered.Count; i++)
                {
                    NavTile tile = Filtered[i];
                    if (Installed.TryGetValue(tile.TileId, out InstalledTile prev) && ReferenceEquals(prev.Tile, tile))
                    {
                        continue;
                    }

                    if (Installed.ContainsKey(tile.TileId))
                    {
                        RemoveInstalled(mesh, tile.TileId);
                    }

                    DtMeshData data = DetourNavQueryEngine.BuildTileData(tile, tileWidthCm, tileHeightCm);
                    DtStatus addStatus = mesh.AddTile(data, 0, 0, out long tileRef);
                    if (addStatus.Failed() || tileRef == 0)
                    {
                        throw new InvalidOperationException(
                            $"Detour AddTile rejected NavTile {tile.TileId} (status {addStatus.Value}).");
                    }

                    Installed[tile.TileId] = new InstalledTile { Tile = tile, Ref = tileRef };
                }

                return true;
            }

            private void RemoveInstalled(DtNavMesh mesh, NavTileId id)
            {
                if (!Installed.Remove(id, out InstalledTile installed))
                {
                    return;
                }

                mesh.RemoveTile(installed.Ref);
            }

            private void TryRebuild(long originXcm, long originZcm, int maxPolys, int tileWidthCm, int tileHeightCm)
            {
                int maxTiles = _maxTiles ?? CapacityFor(Filtered.Count);
                var navMesh = new DtNavMesh();
                DtStatus initStatus = navMesh.Init(new DtNavMeshParams
                {
                    orig = new RcVec3f(originXcm / 100f, 0f, originZcm / 100f),
                    tileWidth = tileWidthCm / 100f,
                    tileHeight = tileHeightCm / 100f,
                    maxTiles = maxTiles,
                    maxPolys = Math.Max(1, maxPolys)
                }, DtDetour.DT_VERTS_PER_POLYGON);
                if (initStatus.Failed())
                {
                    throw new InvalidOperationException(
                        $"Detour NavMesh.Init rejected the mesh for NavTile {Filtered[0].TileId} (status {initStatus.Value}).");
                }

                var installed = new Dictionary<NavTileId, InstalledTile>(Filtered.Count);
                for (int i = 0; i < Filtered.Count; i++)
                {
                    NavTile tile = Filtered[i];
                    DtMeshData data = DetourNavQueryEngine.BuildTileData(tile, tileWidthCm, tileHeightCm);
                    DtStatus addStatus = navMesh.AddTile(data, 0, 0, out long tileRef);
                    if (addStatus.Failed() || tileRef == 0)
                    {
                        throw new InvalidOperationException(
                            $"Detour AddTile rejected NavTile {tile.TileId} (status {addStatus.Value}).");
                    }

                    installed[tile.TileId] = new InstalledTile { Tile = tile, Ref = tileRef };
                }

                Mesh = navMesh;
                Query = new DtNavMeshQuery(navMesh);
                Installed.Clear();
                foreach (KeyValuePair<NavTileId, InstalledTile> pair in installed)
                {
                    Installed.Add(pair.Key, pair.Value);
                }

                MaxTiles = maxTiles;
                BaseOriginXcm = originXcm;
                BaseOriginZcm = originZcm;
                BuildCount++;
            }
        }
    }
}
