using System;
using System.Collections.Generic;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.NavBake.Recast
{
    public static class RecastNavTileBaker
    {
        public static bool TryBake(
            VertexMap map,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig legacyConfig,
            NavAgentProfileConfig profile,
            int layer,
            NavObstacleSet obstacles,
            out NavTile tile,
            out NavBakeArtifact artifact)
        {
            tile = null!;
            artifact = default;

            if (!NavTileBuilder.TryBuildTile(map, chunkX, chunkY, tileVersion, legacyConfig, out var baseTile, out var baseArtifact))
            {
                artifact = baseArtifact;
                return false;
            }

            try
            {
                BuildRecastTriangleMesh(baseTile, obstacles, layer, out var verts, out var tris);
                if (tris.Count == 0)
                {
                    artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.Triangulate, NavBakeErrorCode.NoWalkableDomain, "No triangles after obstacle filtering.", 0, 0, 0, 0);
                    return false;
                }

                var geom = new RcSampleInputGeomProvider(verts.ToArray(), tris.ToArray());
                var rcCfg = BuildRcConfig(profile);
                var bcfg = new RcBuilderConfig(rcCfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax());
                var rcBuilder = new RcBuilder();
                var rcResult = rcBuilder.Build(geom, bcfg, keepInterResults: false);

                if (rcResult?.MeshDetail == null || rcResult.MeshDetail.ntris <= 0)
                {
                    artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.Triangulate, NavBakeErrorCode.TriangulationFailed, "Recast produced empty detail mesh.", 0, 0, 0, 0);
                    return false;
                }

                BuildNavTileFromDetailMesh(
                    baseTile,
                    layer,
                    tileVersion,
                    legacyConfig.ComputeHash(),
                    rcResult.MeshDetail,
                    out tile);

                artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "", baseArtifact.WalkableTriangleCount, tile.VertexCount, tile.TriangleCount, tile.Portals.Length);
                return true;
            }
            catch (Exception ex)
            {
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.Serialize, NavBakeErrorCode.SerializationFailed, ex.Message, 0, 0, 0, 0);
                tile = null!;
                return false;
            }
        }

        private static RcConfig BuildRcConfig(NavAgentProfileConfig profile)
        {
            float radius = profile.RadiusCm / 100f;
            float height = profile.HeightCm / 100f;
            float maxClimb = profile.MaxClimbCm / 100f;
            float maxSlope = profile.MaxSlopeDeg;

            float cellSize = MathF.Max(0.05f, MathF.Min(0.5f, radius / 3f));
            float cellHeight = cellSize * 0.5f;

            return new RcConfig(
                DotRecast.Recast.RcPartition.WATERSHED,
                cellSize, cellHeight,
                maxSlope, height, radius, maxClimb,
                8, 20,
                12f, 1.3f,
                6,
                6f, 1f,
                true, true, true,
                new RcAreaModification(0), true);
        }

        private static void BuildRecastTriangleMesh(NavTile baseTile, NavObstacleSet obstacles, int layer, out List<float> verts, out List<int> tris)
        {
            int vCount = baseTile.VertexCount;
            verts = new List<float>(vCount * 3);
            for (int i = 0; i < vCount; i++)
            {
                verts.Add((baseTile.OriginXcm + baseTile.VertexXcm[i]) / 100f);
                verts.Add(baseTile.VertexYcm[i] / 100f);
                verts.Add((baseTile.OriginZcm + baseTile.VertexZcm[i]) / 100f);
            }

            tris = new List<int>(baseTile.TriangleCount * 3);
            for (int i = 0; i < baseTile.TriangleCount; i++)
            {
                int a = baseTile.TriA[i];
                int b = baseTile.TriB[i];
                int c = baseTile.TriC[i];

                if (IsTriangleBlockedByObstacles(baseTile, a, b, c, obstacles, layer))
                {
                    continue;
                }

                tris.Add(a);
                tris.Add(b);
                tris.Add(c);
            }
        }

        private static bool IsTriangleBlockedByObstacles(NavTile tile, int a, int b, int c, NavObstacleSet obstacles, int layer)
        {
            if (obstacles?.Obstacles == null || obstacles.Obstacles.Count == 0) return false;

            int ax = tile.OriginXcm + tile.VertexXcm[a];
            int az = tile.OriginZcm + tile.VertexZcm[a];
            int bx = tile.OriginXcm + tile.VertexXcm[b];
            int bz = tile.OriginZcm + tile.VertexZcm[b];
            int cx = tile.OriginXcm + tile.VertexXcm[c];
            int cz = tile.OriginZcm + tile.VertexZcm[c];

            int mx = (ax + bx + cx) / 3;
            int mz = (az + bz + cz) / 3;

            for (int i = 0; i < obstacles.Obstacles.Count; i++)
            {
                var o = obstacles.Obstacles[i];
                if (!o.Enabled) continue;
                if (ResolveLayerId(o.LayerId) != layer) continue;

                if (o.Kind == NavObstacleKind.Circle)
                {
                    int dx = mx - o.Center.Xcm;
                    int dz = mz - o.Center.Zcm;
                    long d2 = (long)dx * dx + (long)dz * dz;
                    long r2 = (long)o.RadiusCm * o.RadiusCm;
                    if (d2 <= r2) return true;
                }
                else if (o.Kind == NavObstacleKind.Polygon)
                {
                    if (PointInPolygon(mx, mz, o.Points)) return true;
                }
            }

            return false;
        }

        private static int ResolveLayerId(string layerId)
        {
            if (string.Equals(layerId, "Ground", StringComparison.OrdinalIgnoreCase)) return 0;
            return 0;
        }

        private static bool PointInPolygon(int xcm, int zcm, List<NavPointCm> poly)
        {
            if (poly == null || poly.Count < 3) return false;

            bool inside = false;
            int j = poly.Count - 1;
            for (int i = 0; i < poly.Count; j = i++)
            {
                int xi = poly[i].Xcm;
                int zi = poly[i].Zcm;
                int xj = poly[j].Xcm;
                int zj = poly[j].Zcm;

                if ((zi > zcm) == (zj > zcm)) continue;
                double xInt = (double)(xj - xi) * (zcm - zi) / (double)(zj - zi) + xi;
                if (xcm < xInt) inside = !inside;
            }

            return inside;
        }

        private static void BuildNavTileFromDetailMesh(
            NavTile baseTile,
            int layer,
            uint tileVersion,
            ulong buildHash,
            RcPolyMeshDetail detail,
            out NavTile tile)
        {
            var vertexIndex = new Dictionary<(int X, int Y, int Z), int>(detail.nverts);
            var vx = new List<int>(detail.nverts);
            var vy = new List<int>(detail.nverts);
            var vz = new List<int>(detail.nverts);
            var triA = new List<int>(detail.ntris);
            var triB = new List<int>(detail.ntris);
            var triC = new List<int>(detail.ntris);

            for (int m = 0; m < detail.nmeshes; m++)
            {
                int baseVert = detail.meshes[m * 4 + 0];
                int triBase = detail.meshes[m * 4 + 2];
                int triCount = detail.meshes[m * 4 + 3];

                for (int t = 0; t < triCount; t++)
                {
                    int triIndex = (triBase + t) * 4;
                    int da = detail.tris[triIndex + 0] + baseVert;
                    int db = detail.tris[triIndex + 1] + baseVert;
                    int dc = detail.tris[triIndex + 2] + baseVert;

                    int ia = GetOrAddVertex(detail, da, baseTile, vertexIndex, vx, vy, vz);
                    int ib = GetOrAddVertex(detail, db, baseTile, vertexIndex, vx, vy, vz);
                    int ic = GetOrAddVertex(detail, dc, baseTile, vertexIndex, vx, vy, vz);

                    if (ia == ib || ib == ic || ia == ic) continue;

                    triA.Add(ia);
                    triB.Add(ib);
                    triC.Add(ic);
                }
            }

            var n0 = new int[triA.Count];
            var n1 = new int[triA.Count];
            var n2 = new int[triA.Count];
            Array.Fill(n0, -1);
            Array.Fill(n1, -1);
            Array.Fill(n2, -1);
            BuildAdjacency(triA, triB, triC, n0, n1, n2);

            var tmp = new NavTile(
                new NavTileId(baseTile.TileId.ChunkX, baseTile.TileId.ChunkY, layer),
                tileVersion,
                buildHash,
                0UL,
                baseTile.OriginXcm,
                baseTile.OriginZcm,
                vx.ToArray(),
                vy.ToArray(),
                vz.ToArray(),
                triA.ToArray(),
                triB.ToArray(),
                triC.ToArray(),
                n0,
                n1,
                n2,
                baseTile.Portals);

            using var ms = new System.IO.MemoryStream();
            NavTileBinary.Write(ms, tmp);
            ms.Position = 0;
            tile = NavTileBinary.Read(ms);
        }

        private static int GetOrAddVertex(
            RcPolyMeshDetail detail,
            int detailVertexIndex,
            NavTile baseTile,
            Dictionary<(int X, int Y, int Z), int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz)
        {
            int vi = detailVertexIndex * 3;
            float wx = detail.verts[vi + 0];
            float wy = detail.verts[vi + 1];
            float wz = detail.verts[vi + 2];

            int worldXcm = (int)MathF.Round(wx * 100f);
            int worldYcm = (int)MathF.Round(wy * 100f);
            int worldZcm = (int)MathF.Round(wz * 100f);

            int localXcm = worldXcm - baseTile.OriginXcm;
            int localZcm = worldZcm - baseTile.OriginZcm;

            var key = (localXcm, worldYcm, localZcm);
            if (vertexIndex.TryGetValue(key, out int existing)) return existing;

            int id = vx.Count;
            vx.Add(localXcm);
            vy.Add(worldYcm);
            vz.Add(localZcm);
            vertexIndex[key] = id;
            return id;
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int A;
            public readonly int B;

            public EdgeKey(int a, int b)
            {
                if (a <= b)
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B);
        }

        private static void BuildAdjacency(List<int> triA, List<int> triB, List<int> triC, int[] n0, int[] n1, int[] n2)
        {
            var map = new Dictionary<EdgeKey, (int tri, int edge)>(triA.Count * 3);
            for (int i = 0; i < triA.Count; i++)
            {
                AddEdge(i, 0, triA[i], triB[i]);
                AddEdge(i, 1, triB[i], triC[i]);
                AddEdge(i, 2, triC[i], triA[i]);
            }

            void AddEdge(int tri, int edge, int va, int vb)
            {
                var key = new EdgeKey(va, vb);
                if (!map.TryGetValue(key, out var other))
                {
                    map[key] = (tri, edge);
                    return;
                }

                SetNeighbor(tri, edge, other.tri);
                SetNeighbor(other.tri, other.edge, tri);
            }

            void SetNeighbor(int tri, int edge, int neighbor)
            {
                if (edge == 0) n0[tri] = neighbor;
                else if (edge == 1) n1[tri] = neighbor;
                else n2[tri] = neighbor;
            }
        }
    }
}
