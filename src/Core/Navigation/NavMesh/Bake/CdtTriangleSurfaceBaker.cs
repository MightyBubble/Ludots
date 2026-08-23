using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Geometry;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    internal readonly struct CdtTriangleSurfaceBakeRequest
    {
        public CdtTriangleSurfaceBakeRequest(
            NavTriangleSurfaceTileIndex surfaceIndex,
            NavBakeTileCoord target,
            NavTileId tileId,
            uint tileVersion,
            ulong buildConfigHash,
            string layerId,
            int maxClimbCm,
            int minWalkableUpDotQ1M,
            int agentHeightCm,
            int agentRadiusCm,
            INavObstacleSource obstacles,
            int maxLawsonFlipCount)
        {
            SurfaceIndex = surfaceIndex;
            Target = target;
            TileId = tileId;
            TileVersion = tileVersion;
            BuildConfigHash = buildConfigHash;
            LayerId = layerId;
            MaxClimbCm = maxClimbCm;
            MinWalkableUpDotQ1M = minWalkableUpDotQ1M;
            AgentHeightCm = agentHeightCm;
            AgentRadiusCm = agentRadiusCm;
            Obstacles = obstacles;
            MaxLawsonFlipCount = maxLawsonFlipCount;
        }

        public NavTriangleSurfaceTileIndex SurfaceIndex { get; }

        public NavBakeTileCoord Target { get; }

        public NavTileId TileId { get; }

        public uint TileVersion { get; }

        public ulong BuildConfigHash { get; }

        public string LayerId { get; }

        public int MaxClimbCm { get; }

        public int MinWalkableUpDotQ1M { get; }

        public int AgentHeightCm { get; }

        public int AgentRadiusCm { get; }

        public INavObstacleSource Obstacles { get; }

        public int MaxLawsonFlipCount { get; }
    }

    /// <summary>
    /// Direct 3D constrained-Delaunay triangle-surface baker: sheet split, walkability, clip, triangulation, adjacency, portals.
    /// </summary>
    internal static class CdtTriangleSurfaceBaker
    {
        private const string InputOwner = "triangleSurface";

        private readonly struct VertexKey : IEquatable<VertexKey>, IComparable<VertexKey>
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Z;

            public VertexKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public int CompareTo(VertexKey other)
            {
                int c = X.CompareTo(other.X);
                if (c != 0) return c;
                c = Y.CompareTo(other.Y);
                if (c != 0) return c;
                return Z.CompareTo(other.Z);
            }

            public bool Equals(VertexKey other) => X == other.X && Y == other.Y && Z == other.Z;

            public override bool Equals(object obj) => obj is VertexKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly VertexKey A;
            public readonly VertexKey B;

            public EdgeKey(VertexKey a, VertexKey b)
            {
                if (a.CompareTo(b) <= 0)
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

            public bool Equals(EdgeKey other) => A.Equals(other.A) && B.Equals(other.B);

            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(A, B);
        }

        private sealed class SheetBuilder
        {
            private readonly int[] _parent;
            private readonly int[] _rank;

            public SheetBuilder(int count)
            {
                _parent = new int[count];
                _rank = new int[count];
                for (int i = 0; i < count; i++)
                {
                    _parent[i] = i;
                }
            }

            public int Find(int x)
            {
                while (_parent[x] != x)
                {
                    _parent[x] = _parent[_parent[x]];
                    x = _parent[x];
                }

                return x;
            }

            public void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra == rb)
                {
                    return;
                }

                if (_rank[ra] < _rank[rb])
                {
                    _parent[ra] = rb;
                }
                else if (_rank[ra] > _rank[rb])
                {
                    _parent[rb] = ra;
                }
                else
                {
                    _parent[rb] = ra;
                    _rank[ra]++;
                }
            }
        }

        private struct BoundarySegment
        {
            public NavPortalSide Side;
            public int Along0;
            public int Along1;
            public int LeftY0;
            public int LeftY1;
            public int LeftX0;
            public int LeftZ0;
            public int LeftX1;
            public int LeftZ1;
            public int ClearanceCm;
            public int SheetId;
        }

        public static NavTile Bake(in CdtTriangleSurfaceBakeRequest request)
        {
            NavTriangleSurfaceTileIndex surfaceIndex = request.SurfaceIndex;
            NavTriangleSurfaceSnapshot surface = surfaceIndex.Surface;
            NavTriangleSurfaceTileGrid grid = surfaceIndex.Grid;
            NavBakeTileCoord target = request.Target;

            int targetMinX = checked(grid.OriginXcm + checked(target.ChunkX * grid.TileWidthCm));
            int targetMinZ = checked(grid.OriginZcm + checked(target.ChunkY * grid.TileHeightCm));
            int targetMaxX = checked(targetMinX + grid.TileWidthCm);
            int targetMaxZ = checked(targetMinZ + grid.TileHeightCm);

            ReadOnlySpan<int> selection = surfaceIndex.GetTriangleIndices(target);
            if (selection.Length == 0)
            {
                return NavValidEmptyTile.Create(
                    request.TileId,
                    request.TileVersion,
                    request.BuildConfigHash,
                    targetMinX,
                    targetMinZ);
            }

            ReadOnlySpan<int> vertexX = surface.VertexXcm;
            ReadOnlySpan<int> vertexY = surface.VertexYcm;
            ReadOnlySpan<int> vertexZ = surface.VertexZcm;
            ReadOnlySpan<int> triA = surface.TriA;
            ReadOnlySpan<int> triB = surface.TriB;
            ReadOnlySpan<int> triC = surface.TriC;
            ReadOnlySpan<byte> triAreas = surface.TriAreaIds;
            ReadOnlySpan<NavTriangleSurfaceFlags> triFlags = surface.TriFlags;

            int[] localTriIndices = selection.ToArray();
            int[] sheetIds = BuildSheets(
                localTriIndices,
                vertexX,
                vertexY,
                vertexZ,
                triA,
                triB,
                triC);

            ValidateSheetProjections(
                localTriIndices,
                sheetIds,
                vertexX,
                vertexY,
                vertexZ,
                triA,
                triB,
                triC);

            bool[] walkable = new bool[localTriIndices.Length];
            for (int i = 0; i < localTriIndices.Length; i++)
            {
                walkable[i] = TriangleSurfaceWalkability.IsWalkableTriangle(
                    localTriIndices[i],
                    vertexX,
                    vertexY,
                    vertexZ,
                    triA,
                    triB,
                    triC,
                    triFlags,
                    request.MinWalkableUpDotQ1M,
                    request.AgentHeightCm,
                    request.Obstacles,
                    request.LayerId,
                    request.AgentRadiusCm,
                    localTriIndices);
            }

            var outX = new List<int>();
            var outY = new List<int>();
            var outZ = new List<int>();
            var outA = new List<int>();
            var outB = new List<int>();
            var outC = new List<int>();
            var outN0 = new List<int>();
            var outN1 = new List<int>();
            var outN2 = new List<int>();
            var outArea = new List<byte>();
            var outSheet = new List<int>();

            var vertexMap = new Dictionary<VertexKey, int>();
            int maxSheet = -1;
            for (int i = 0; i < sheetIds.Length; i++)
            {
                if (sheetIds[i] > maxSheet)
                {
                    maxSheet = sheetIds[i];
                }
            }

            for (int sheet = 0; sheet <= maxSheet; sheet++)
            {
                EmitSheet(
                    sheet,
                    localTriIndices,
                    sheetIds,
                    walkable,
                    surface,
                    targetMinX,
                    targetMinZ,
                    targetMaxX,
                    targetMaxZ,
                    request.MaxLawsonFlipCount,
                    vertexMap,
                    outX,
                    outY,
                    outZ,
                    outA,
                    outB,
                    outC,
                    outArea,
                    outSheet);
            }

            if (outA.Count == 0)
            {
                return NavValidEmptyTile.Create(
                    request.TileId,
                    request.TileVersion,
                    request.BuildConfigHash,
                    targetMinX,
                    targetMinZ);
            }

            BuildAdjacency(outA, outB, outC, outSheet, outX, outY, outZ, outN0, outN1, outN2);

            NavBorderPortal[] portals = BuildPortals(
                request,
                grid,
                target,
                targetMinX,
                targetMinZ,
                targetMaxX,
                targetMaxZ,
                localTriIndices,
                walkable,
                sheetIds,
                outX,
                outY,
                outZ,
                outA,
                outB,
                outC,
                outSheet);

            var vertexXLocal = new int[outX.Count];
            var vertexYLocal = new int[outY.Count];
            var vertexZLocal = new int[outZ.Count];
            for (int i = 0; i < outX.Count; i++)
            {
                vertexXLocal[i] = checked(outX[i] - targetMinX);
                vertexYLocal[i] = outY[i];
                vertexZLocal[i] = checked(outZ[i] - targetMinZ);
            }

            var tmp = new NavTile(
                request.TileId,
                request.TileVersion,
                request.BuildConfigHash,
                checksum: 0UL,
                targetMinX,
                targetMinZ,
                vertexXLocal,
                vertexYLocal,
                vertexZLocal,
                outA.ToArray(),
                outB.ToArray(),
                outC.ToArray(),
                outN0.ToArray(),
                outN1.ToArray(),
                outN2.ToArray(),
                outArea.ToArray(),
                portals);

            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, tmp);
            ms.Position = 0;
            return NavTileBinary.Read(ms);
        }

        private static int[] BuildSheets(
            int[] localTriIndices,
            ReadOnlySpan<int> vertexX,
            ReadOnlySpan<int> vertexY,
            ReadOnlySpan<int> vertexZ,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC)
        {
            var edgeToLocal = new Dictionary<EdgeKey, List<int>>();
            var builder = new SheetBuilder(localTriIndices.Length);
            for (int li = 0; li < localTriIndices.Length; li++)
            {
                int tri = localTriIndices[li];
                RegisterEdge(li, tri, triA[tri], triB[tri], vertexX, vertexY, vertexZ, edgeToLocal, builder);
                RegisterEdge(li, tri, triB[tri], triC[tri], vertexX, vertexY, vertexZ, edgeToLocal, builder);
                RegisterEdge(li, tri, triC[tri], triA[tri], vertexX, vertexY, vertexZ, edgeToLocal, builder);
            }

            var rootToSheet = new Dictionary<int, int>();
            var sheetIds = new int[localTriIndices.Length];
            int nextSheet = 0;
            for (int li = 0; li < localTriIndices.Length; li++)
            {
                int root = builder.Find(li);
                if (!rootToSheet.TryGetValue(root, out int sheet))
                {
                    sheet = nextSheet++;
                    rootToSheet[root] = sheet;
                }

                sheetIds[li] = sheet;
            }

            return sheetIds;
        }

        private static void RegisterEdge(
            int localIndex,
            int tri,
            int va,
            int vb,
            ReadOnlySpan<int> vertexX,
            ReadOnlySpan<int> vertexY,
            ReadOnlySpan<int> vertexZ,
            Dictionary<EdgeKey, List<int>> edgeToLocal,
            SheetBuilder builder)
        {
            _ = tri;
            var key = new EdgeKey(
                new VertexKey(vertexX[va], vertexY[va], vertexZ[va]),
                new VertexKey(vertexX[vb], vertexY[vb], vertexZ[vb]));
            if (!edgeToLocal.TryGetValue(key, out List<int> owners))
            {
                owners = new List<int>(2);
                edgeToLocal[key] = owners;
            }

            for (int i = 0; i < owners.Count; i++)
            {
                builder.Union(localIndex, owners[i]);
            }

            owners.Add(localIndex);
        }

        private static void ValidateSheetProjections(
            int[] localTriIndices,
            int[] sheetIds,
            ReadOnlySpan<int> vertexX,
            ReadOnlySpan<int> vertexY,
            ReadOnlySpan<int> vertexZ,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC)
        {
            var bySheet = new Dictionary<int, List<int>>();
            for (int i = 0; i < localTriIndices.Length; i++)
            {
                if (!bySheet.TryGetValue(sheetIds[i], out List<int> list))
                {
                    list = new List<int>();
                    bySheet[sheetIds[i]] = list;
                }

                list.Add(i);
            }

            foreach (KeyValuePair<int, List<int>> kv in bySheet)
            {
                List<int> members = kv.Value;
                for (int i = 0; i < members.Count; i++)
                {
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        int ti = localTriIndices[members[i]];
                        int tj = localTriIndices[members[j]];
                        if (SharesExactEdge(ti, tj, vertexX, vertexY, vertexZ, triA, triB, triC))
                        {
                            continue;
                        }

                        if (TrianglesHaveInteriorXzOverlap(
                                ti,
                                tj,
                                vertexX,
                                vertexY,
                                vertexZ,
                                triA,
                                triB,
                                triC))
                        {
                            throw new NavBakeUnsupportedInputException(
                                NavBakeAlgorithmKind.Cdt,
                                InputOwner,
                                $"sheet {kv.Key} contains triangles {ti} and {tj} with overlapping XZ interiors that are not connected by an exact 3D edge (non-manifold projection).");
                        }
                    }
                }
            }
        }

        private static bool SharesExactEdge(
            int ta,
            int tb,
            ReadOnlySpan<int> vertexX,
            ReadOnlySpan<int> vertexY,
            ReadOnlySpan<int> vertexZ,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC)
        {
            int[] a = { triA[ta], triB[ta], triC[ta] };
            int[] b = { triA[tb], triB[tb], triC[tb] };
            for (int i = 0; i < 3; i++)
            {
                int ai0 = a[i];
                int ai1 = a[(i + 1) % 3];
                var edgeA = new EdgeKey(
                    new VertexKey(vertexX[ai0], vertexY[ai0], vertexZ[ai0]),
                    new VertexKey(vertexX[ai1], vertexY[ai1], vertexZ[ai1]));
                for (int j = 0; j < 3; j++)
                {
                    int bi0 = b[j];
                    int bi1 = b[(j + 1) % 3];
                    var edgeB = new EdgeKey(
                        new VertexKey(vertexX[bi0], vertexY[bi0], vertexZ[bi0]),
                        new VertexKey(vertexX[bi1], vertexY[bi1], vertexZ[bi1]));
                    if (edgeA.Equals(edgeB))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TrianglesHaveInteriorXzOverlap(
            int ta,
            int tb,
            ReadOnlySpan<int> vertexX,
            ReadOnlySpan<int> vertexY,
            ReadOnlySpan<int> vertexZ,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC)
        {
            int ia = triA[ta];
            int ib = triB[ta];
            int ic = triC[ta];
            int ja = triA[tb];
            int jb = triB[tb];
            int jc = triC[tb];

            int[] samples =
            {
                vertexX[ia], vertexZ[ia],
                vertexX[ib], vertexZ[ib],
                vertexX[ic], vertexZ[ic],
                (vertexX[ia] + vertexX[ib] + vertexX[ic]) / 3,
                (vertexZ[ia] + vertexZ[ib] + vertexZ[ic]) / 3,
                vertexX[ja], vertexZ[ja],
                vertexX[jb], vertexZ[jb],
                vertexX[jc], vertexZ[jc],
                (vertexX[ja] + vertexX[jb] + vertexX[jc]) / 3,
                (vertexZ[ja] + vertexZ[jb] + vertexZ[jc]) / 3
            };

            for (int s = 0; s < samples.Length; s += 2)
            {
                int px = samples[s];
                int pz = samples[s + 1];
                bool inA = ExactPredicates2D.PointInTriangleStrict(
                    px, pz,
                    vertexX[ia], vertexZ[ia],
                    vertexX[ib], vertexZ[ib],
                    vertexX[ic], vertexZ[ic]);
                bool inB = ExactPredicates2D.PointInTriangleStrict(
                    px, pz,
                    vertexX[ja], vertexZ[ja],
                    vertexX[jb], vertexZ[jb],
                    vertexX[jc], vertexZ[jc]);
                if (inA && inB)
                {
                    int yA = SampleYOnTrianglePlane(px, pz, ia, ib, ic, vertexX, vertexY, vertexZ);
                    int yB = SampleYOnTrianglePlane(px, pz, ja, jb, jc, vertexX, vertexY, vertexZ);
                    if (yA != yB)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void EmitSheet(
            int sheetId,
            int[] localTriIndices,
            int[] sheetIds,
            bool[] walkable,
            NavTriangleSurfaceSnapshot surface,
            int targetMinX,
            int targetMinZ,
            int targetMaxX,
            int targetMaxZ,
            int maxLawsonFlipCount,
            Dictionary<VertexKey, int> vertexMap,
            List<int> outX,
            List<int> outY,
            List<int> outZ,
            List<int> outA,
            List<int> outB,
            List<int> outC,
            List<byte> outArea,
            List<int> outSheet)
        {
            ReadOnlySpan<int> vertexX = surface.VertexXcm;
            ReadOnlySpan<int> vertexY = surface.VertexYcm;
            ReadOnlySpan<int> vertexZ = surface.VertexZcm;
            ReadOnlySpan<int> triA = surface.TriA;
            ReadOnlySpan<int> triB = surface.TriB;
            ReadOnlySpan<int> triC = surface.TriC;
            ReadOnlySpan<byte> triAreas = surface.TriAreaIds;

            var keptLocal = new List<int>();
            for (int i = 0; i < localTriIndices.Length; i++)
            {
                if (sheetIds[i] != sheetId || !walkable[i])
                {
                    continue;
                }

                int tri = localTriIndices[i];
                if (!TriangleIntersectsTarget(
                        tri,
                        vertexX,
                        vertexZ,
                        triA,
                        triB,
                        triC,
                        targetMinX,
                        targetMinZ,
                        targetMaxX,
                        targetMaxZ))
                {
                    continue;
                }

                keptLocal.Add(i);
            }

            if (keptLocal.Count == 0)
            {
                return;
            }

            var polyX = new List<int>();
            var polyZ = new List<int>();
            var polyY = new List<int>();
            var polyIndex = new Dictionary<VertexKey, int>();
            var sheetTriA = new List<int>();
            var sheetTriB = new List<int>();
            var sheetTriC = new List<int>();
            var sheetAreas = new List<byte>();

            for (int k = 0; k < keptLocal.Count; k++)
            {
                int li = keptLocal[k];
                int tri = localTriIndices[li];
                int ia = triA[tri];
                int ib = triB[tri];
                int ic = triC[tri];
                int ax = vertexX[ia];
                int ay = vertexY[ia];
                int az = vertexZ[ia];
                int bx = vertexX[ib];
                int by = vertexY[ib];
                int bz = vertexZ[ib];
                int cx = vertexX[ic];
                int cy = vertexY[ic];
                int cz = vertexZ[ic];

                if (TriangleFullyInsideTarget(ax, az, bx, bz, cx, cz, targetMinX, targetMinZ, targetMaxX, targetMaxZ))
                {
                    int va = AddVertex(ax, ay, az, polyIndex, polyX, polyY, polyZ);
                    int vb = AddVertex(bx, by, bz, polyIndex, polyX, polyY, polyZ);
                    int vc = AddVertex(cx, cy, cz, polyIndex, polyX, polyY, polyZ);
                    sheetTriA.Add(va);
                    sheetTriB.Add(vb);
                    sheetTriC.Add(vc);
                    sheetAreas.Add(triAreas[tri]);
                    continue;
                }

                int beforeCount = sheetTriA.Count;
                ClipTriangleToTarget(
                    ax, ay, az,
                    bx, by, bz,
                    cx, cy, cz,
                    targetMinX,
                    targetMinZ,
                    targetMaxX,
                    targetMaxZ,
                    polyIndex,
                    polyX,
                    polyY,
                    polyZ,
                    sheetTriA,
                    sheetTriB,
                    sheetTriC,
                    maxLawsonFlipCount);
                for (int t = beforeCount; t < sheetTriA.Count; t++)
                {
                    sheetAreas.Add(triAreas[tri]);
                }
            }

            if (sheetTriA.Count == 0)
            {
                return;
            }

            if (sheetTriA.Count == keptLocal.Count)
            {
                LawsonFlipExisting(
                    polyX,
                    polyZ,
                    maxLawsonFlipCount,
                    sheetTriA,
                    sheetTriB,
                    sheetTriC);
            }

            for (int t = 0; t < sheetTriA.Count; t++)
            {
                int a = sheetTriA[t];
                int b = sheetTriB[t];
                int c = sheetTriC[t];
                byte area = t < sheetAreas.Count ? sheetAreas[t] : sheetAreas[sheetAreas.Count - 1];
                int ga = MapGlobalVertex(polyX[a], polyY[a], polyZ[a], vertexMap, outX, outY, outZ);
                int gb = MapGlobalVertex(polyX[b], polyY[b], polyZ[b], vertexMap, outX, outY, outZ);
                int gc = MapGlobalVertex(polyX[c], polyY[c], polyZ[c], vertexMap, outX, outY, outZ);
                outA.Add(ga);
                outB.Add(gb);
                outC.Add(gc);
                outArea.Add(area);
                outSheet.Add(sheetId);
            }
        }

        private static int AddVertex(
            int x,
            int y,
            int z,
            Dictionary<VertexKey, int> map,
            List<int> xs,
            List<int> ys,
            List<int> zs)
        {
            var key = new VertexKey(x, y, z);
            if (map.TryGetValue(key, out int index))
            {
                return index;
            }

            index = xs.Count;
            map[key] = index;
            xs.Add(x);
            ys.Add(y);
            zs.Add(z);
            return index;
        }

        private static int MapGlobalVertex(
            int x,
            int y,
            int z,
            Dictionary<VertexKey, int> map,
            List<int> outX,
            List<int> outY,
            List<int> outZ)
        {
            var key = new VertexKey(x, y, z);
            if (map.TryGetValue(key, out int index))
            {
                return index;
            }

            index = outX.Count;
            map[key] = index;
            outX.Add(x);
            outY.Add(y);
            outZ.Add(z);
            return index;
        }

        private static bool TriangleIntersectsTarget(
            int tri,
            ReadOnlySpan<int> vertexX,
            ReadOnlySpan<int> vertexZ,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC,
            int minX,
            int minZ,
            int maxX,
            int maxZ)
        {
            int ia = triA[tri];
            int ib = triB[tri];
            int ic = triC[tri];
            int tMinX = Min3(vertexX[ia], vertexX[ib], vertexX[ic]);
            int tMaxX = Max3(vertexX[ia], vertexX[ib], vertexX[ic]);
            int tMinZ = Min3(vertexZ[ia], vertexZ[ib], vertexZ[ic]);
            int tMaxZ = Max3(vertexZ[ia], vertexZ[ib], vertexZ[ic]);
            return !(tMaxX < minX || tMinX > maxX || tMaxZ < minZ || tMinZ > maxZ);
        }

        private static bool TriangleFullyInsideTarget(
            int ax, int az, int bx, int bz, int cx, int cz,
            int minX, int minZ, int maxX, int maxZ)
            => ax >= minX && ax <= maxX && bx >= minX && bx <= maxX && cx >= minX && cx <= maxX &&
               az >= minZ && az <= maxZ && bz >= minZ && bz <= maxZ && cz >= minZ && cz <= maxZ;

        private static void ClipTriangleToTarget(
            int ax, int ay, int az,
            int bx, int by, int bz,
            int cx, int cy, int cz,
            int minX,
            int minZ,
            int maxX,
            int maxZ,
            Dictionary<VertexKey, int> polyIndex,
            List<int> polyX,
            List<int> polyY,
            List<int> polyZ,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            int maxLawsonFlipCount)
        {
            var xs = new List<int> { ax, bx, cx };
            var ys = new List<int> { ay, by, cy };
            var zs = new List<int> { az, bz, cz };
            ClipPolygonAxis(xs, ys, zs, minX, maxX, clipX: true);
            if (xs.Count < 3)
            {
                return;
            }

            ClipPolygonAxis(xs, ys, zs, minZ, maxZ, clipX: false);
            if (xs.Count < 3)
            {
                return;
            }

            RemoveDuplicateConsecutiveVertices(xs, ys, zs);
            if (xs.Count < 3)
            {
                return;
            }

            EnsureCounterClockwise(xs, ys, zs);

            if (xs.Count == 3)
            {
                int ia = AddVertex(xs[0], ys[0], zs[0], polyIndex, polyX, polyY, polyZ);
                int ib = AddVertex(xs[1], ys[1], zs[1], polyIndex, polyX, polyY, polyZ);
                int ic = AddVertex(xs[2], ys[2], zs[2], polyIndex, polyX, polyY, polyZ);
                if (ia != ib && ib != ic && ia != ic &&
                    ExactPredicates2D.Orient2(xs[0], zs[0], xs[1], zs[1], xs[2], zs[2]) != 0)
                {
                    triA.Add(ia);
                    triB.Add(ib);
                    triC.Add(ic);
                }

                return;
            }

            // Triangle ∩ AABB is always convex — fan triangulation is exact and avoids ear-clip failure modes.
            var mapped = new int[xs.Count];
            for (int i = 0; i < xs.Count; i++)
            {
                mapped[i] = AddVertex(xs[i], ys[i], zs[i], polyIndex, polyX, polyY, polyZ);
            }

            for (int i = 1; i + 1 < mapped.Length; i++)
            {
                int ia = mapped[0];
                int ib = mapped[i];
                int ic = mapped[i + 1];
                if (ia == ib || ib == ic || ia == ic)
                {
                    continue;
                }

                // Skip zero-area fans.
                if (ExactPredicates2D.Orient2(xs[0], zs[0], xs[i], zs[i], xs[i + 1], zs[i + 1]) == 0)
                {
                    continue;
                }

                triA.Add(ia);
                triB.Add(ib);
                triC.Add(ic);
            }
        }

        private static void RemoveDuplicateConsecutiveVertices(List<int> xs, List<int> ys, List<int> zs)
        {
            if (xs.Count == 0)
            {
                return;
            }

            int write = 0;
            for (int read = 0; read < xs.Count; read++)
            {
                if (write > 0 &&
                    xs[write - 1] == xs[read] &&
                    ys[write - 1] == ys[read] &&
                    zs[write - 1] == zs[read])
                {
                    continue;
                }

                xs[write] = xs[read];
                ys[write] = ys[read];
                zs[write] = zs[read];
                write++;
            }

            if (write > 1 &&
                xs[0] == xs[write - 1] &&
                ys[0] == ys[write - 1] &&
                zs[0] == zs[write - 1])
            {
                write--;
            }

            if (write < xs.Count)
            {
                xs.RemoveRange(write, xs.Count - write);
                ys.RemoveRange(write, ys.Count - write);
                zs.RemoveRange(write, zs.Count - write);
            }
        }

        private static void EnsureCounterClockwise(List<int> xs, List<int> ys, List<int> zs)
        {
            Int128 area2 = 0;
            for (int i = 0; i < xs.Count; i++)
            {
                int j = i + 1 == xs.Count ? 0 : i + 1;
                area2 += ((Int128)xs[i] * zs[j]) - ((Int128)xs[j] * zs[i]);
            }

            if (area2 >= 0)
            {
                return;
            }

            xs.Reverse();
            ys.Reverse();
            zs.Reverse();
        }

        private static void ClipPolygonAxis(
            List<int> xs,
            List<int> ys,
            List<int> zs,
            int min,
            int max,
            bool clipX)
        {
            ClipHalfPlane(xs, ys, zs, min, isMin: true, clipX);
            ClipHalfPlane(xs, ys, zs, max, isMin: false, clipX);
        }

        private static void ClipHalfPlane(
            List<int> xs,
            List<int> ys,
            List<int> zs,
            int plane,
            bool isMin,
            bool clipX)
        {
            var nx = new List<int>();
            var ny = new List<int>();
            var nz = new List<int>();
            int count = xs.Count;
            if (count == 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int j = i + 1 == count ? 0 : i + 1;
                int xi = xs[i];
                int yi = ys[i];
                int zi = zs[i];
                int xj = xs[j];
                int yj = ys[j];
                int zj = zs[j];
                int vi = clipX ? xi : zi;
                int vj = clipX ? xj : zj;
                bool insideI = isMin ? vi >= plane : vi <= plane;
                bool insideJ = isMin ? vj >= plane : vj <= plane;

                if (insideI)
                {
                    nx.Add(xi);
                    ny.Add(yi);
                    nz.Add(zi);
                }

                if (insideI == insideJ)
                {
                    continue;
                }

                InterpolateClipVertex(xi, yi, zi, xj, yj, zj, plane, clipX, out int cx, out int cy, out int cz);
                nx.Add(cx);
                ny.Add(cy);
                nz.Add(cz);
            }

            xs.Clear();
            ys.Clear();
            zs.Clear();
            xs.AddRange(nx);
            ys.AddRange(ny);
            zs.AddRange(nz);
        }

        private static void InterpolateClipVertex(
            int xi,
            int yi,
            int zi,
            int xj,
            int yj,
            int zj,
            int plane,
            bool clipX,
            out int cx,
            out int cy,
            out int cz)
        {
            int vi = clipX ? xi : zi;
            int vj = clipX ? xj : zj;
            if (vi > vj)
            {
                (xi, xj) = (xj, xi);
                (yi, yj) = (yj, yi);
                (zi, zj) = (zj, zi);
                (vi, vj) = (vj, vi);
            }

            int denom = vj - vi;
            if (denom == 0)
            {
                cx = xi;
                cy = yi;
                cz = zi;
                if (clipX)
                {
                    cx = plane;
                }
                else
                {
                    cz = plane;
                }

                return;
            }

            long tNum = (long)plane - vi;

            cx = xi + (int)((tNum * (xj - xi)) / denom);
            cy = yi + (int)((tNum * (yj - yi)) / denom);
            cz = zi + (int)((tNum * (zj - zi)) / denom);
            if (clipX)
            {
                cx = plane;
            }
            else
            {
                cz = plane;
            }
        }

        private static void LawsonFlipExisting(
            List<int> polyX,
            List<int> polyZ,
            int maxLawsonFlipCount,
            List<int> triA,
            List<int> triB,
            List<int> triC)
        {
            int flipCount = 0;
            bool flipped = true;
            while (flipped)
            {
                flipped = false;
                int triCount = triA.Count;
                for (int t0 = 0; t0 < triCount; t0++)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int v0 = EdgeVertex(t0, e, triA, triB, triC);
                        int v1 = EdgeVertex(t0, e + 1, triA, triB, triC);
                        int opp0 = OppositeVertex(t0, v0, v1, triA, triB, triC);
                        int t1 = FindMate(triCount, t0, v0, v1, triA, triB, triC);
                        if (t1 < 0)
                        {
                            continue;
                        }

                        int opp1 = OppositeVertex(t1, v0, v1, triA, triB, triC);
                        if (ExactPredicates2D.InCircleSign(
                                polyX[v0], polyZ[v0],
                                polyX[v1], polyZ[v1],
                                polyX[opp0], polyZ[opp0],
                                polyX[opp1], polyZ[opp1]) <= 0)
                        {
                            continue;
                        }

                        FlipEdge(t0, t1, v0, v1, opp0, opp1, triA, triB, triC);
                        flipCount++;
                        if (flipCount > maxLawsonFlipCount)
                        {
                            throw new InvalidOperationException(
                                $"CdtTriangleSurfaceBaker exceeded maxLawsonFlipCount ({maxLawsonFlipCount}).");
                        }

                        flipped = true;
                    }
                }
            }
        }

        private static int EdgeVertex(int tri, int edge, List<int> triA, List<int> triB, List<int> triC)
            => edge switch
            {
                0 => triA[tri],
                1 => triB[tri],
                _ => triC[tri]
            };

        private static int OppositeVertex(int tri, int v0, int v1, List<int> triA, List<int> triB, List<int> triC)
        {
            int a = triA[tri];
            int b = triB[tri];
            int c = triC[tri];
            if (a != v0 && a != v1) return a;
            if (b != v0 && b != v1) return b;
            return c;
        }

        private static int FindMate(int triCount, int self, int v0, int v1, List<int> triA, List<int> triB, List<int> triC)
        {
            for (int t = 0; t < triCount; t++)
            {
                if (t == self)
                {
                    continue;
                }

                int a = triA[t];
                int b = triB[t];
                int c = triC[t];
                if ((a == v0 || b == v0 || c == v0) && (a == v1 || b == v1 || c == v1))
                {
                    return t;
                }
            }

            return -1;
        }

        private static void FlipEdge(
            int t0,
            int t1,
            int v0,
            int v1,
            int opp0,
            int opp1,
            List<int> triA,
            List<int> triB,
            List<int> triC)
        {
            triA[t0] = opp0;
            triB[t0] = v0;
            triC[t0] = opp1;
            triA[t1] = opp0;
            triB[t1] = opp1;
            triC[t1] = v1;
        }

        private static void BuildAdjacency(
            List<int> triA,
            List<int> triB,
            List<int> triC,
            List<int> triSheet,
            List<int> vtxX,
            List<int> vtxY,
            List<int> vtxZ,
            List<int> n0,
            List<int> n1,
            List<int> n2)
        {
            int triCount = triA.Count;
            for (int i = 0; i < triCount; i++)
            {
                n0.Add(-1);
                n1.Add(-1);
                n2.Add(-1);
            }

            var edges = new List<(int tri, int opp, int a, int b)>(triCount * 3);
            for (int t = 0; t < triCount; t++)
            {
                AddEdge(edges, t, 0, triA[t], triB[t], triSheet[t], triA, triB, triC, triSheet, vtxX, vtxY, vtxZ);
                AddEdge(edges, t, 1, triB[t], triC[t], triSheet[t], triA, triB, triC, triSheet, vtxX, vtxY, vtxZ);
                AddEdge(edges, t, 2, triC[t], triA[t], triSheet[t], triA, triB, triC, triSheet, vtxX, vtxY, vtxZ);
            }

            edges.Sort(static (x, y) =>
            {
                int c = CompareVertexKey(x.a, x.b, y.a, y.b);
                if (c != 0) return c;
                if (x.tri != y.tri) return x.tri.CompareTo(y.tri);
                return x.opp.CompareTo(y.opp);
            });

            for (int i = 0; i < edges.Count; i++)
            {
                if (i + 1 >= edges.Count)
                {
                    break;
                }

                var e0 = edges[i];
                var e1 = edges[i + 1];
                if (!SameUndirectedEdge(e0.a, e0.b, e1.a, e1.b, vtxX, vtxY, vtxZ))
                {
                    continue;
                }

                if (triSheet[e0.tri] != triSheet[e1.tri])
                {
                    continue;
                }

                SetNeighbor(e0.tri, e0.opp, e1.tri, n0, n1, n2);
                SetNeighbor(e1.tri, e1.opp, e0.tri, n0, n1, n2);
                i++;
            }
        }

        private static void AddEdge(
            List<(int tri, int opp, int a, int b)> edges,
            int tri,
            int opp,
            int a,
            int b,
            int sheet,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            List<int> triSheet,
            List<int> vtxX,
            List<int> vtxY,
            List<int> vtxZ)
        {
            _ = sheet;
            _ = triA;
            _ = triB;
            _ = triC;
            _ = triSheet;
            if (CompareVertexIndex(a, b, vtxX, vtxY, vtxZ) > 0)
            {
                (a, b) = (b, a);
            }

            edges.Add((tri, opp, a, b));
        }

        private static int CompareVertexIndex(int a, int b, List<int> x, List<int> y, List<int> z)
        {
            int c = x[a].CompareTo(x[b]);
            if (c != 0) return c;
            c = y[a].CompareTo(y[b]);
            if (c != 0) return c;
            return z[a].CompareTo(z[b]);
        }

        private static int CompareVertexKey(int a0, int a1, int b0, int b1)
        {
            int c = a0.CompareTo(b0);
            if (c != 0) return c;
            return a1.CompareTo(b1);
        }

        private static bool SameUndirectedEdge(int a0, int a1, int b0, int b1, List<int> x, List<int> y, List<int> z)
        {
            return x[a0] == x[b0] && y[a0] == y[b0] && z[a0] == z[b0] &&
                   x[a1] == x[b1] && y[a1] == y[b1] && z[a1] == z[b1];
        }

        private static void SetNeighbor(int tri, int opp, int mate, List<int> n0, List<int> n1, List<int> n2)
        {
            switch (opp)
            {
                case 0: n0[tri] = mate; break;
                case 1: n1[tri] = mate; break;
                default: n2[tri] = mate; break;
            }
        }

        private static NavBorderPortal[] BuildPortals(
            in CdtTriangleSurfaceBakeRequest request,
            NavTriangleSurfaceTileGrid grid,
            NavBakeTileCoord target,
            int targetMinX,
            int targetMinZ,
            int targetMaxX,
            int targetMaxZ,
            int[] localTriIndices,
            bool[] walkable,
            int[] sheetIds,
            List<int> outX,
            List<int> outY,
            List<int> outZ,
            List<int> outA,
            List<int> outB,
            List<int> outC,
            List<int> outSheet)
        {
            var targetSegments = CollectBoundarySegments(
                targetMinX,
                targetMinZ,
                targetMaxX,
                targetMaxZ,
                outX,
                outY,
                outZ,
                outA,
                outB,
                outC,
                outSheet);

            var accepted = new List<BoundarySegment>();
            TryAddNeighborPortals(
                request,
                grid,
                target,
                targetMinX,
                targetMinZ,
                targetMaxX,
                targetMaxZ,
                localTriIndices,
                walkable,
                sheetIds,
                targetSegments,
                accepted,
                dx: -1,
                dz: 0,
                side: NavPortalSide.West,
                boundaryCoord: targetMinX);
            TryAddNeighborPortals(
                request,
                grid,
                target,
                targetMinX,
                targetMinZ,
                targetMaxX,
                targetMaxZ,
                localTriIndices,
                walkable,
                sheetIds,
                targetSegments,
                accepted,
                dx: 1,
                dz: 0,
                side: NavPortalSide.East,
                boundaryCoord: targetMaxX);
            TryAddNeighborPortals(
                request,
                grid,
                target,
                targetMinX,
                targetMinZ,
                targetMaxX,
                targetMaxZ,
                localTriIndices,
                walkable,
                sheetIds,
                targetSegments,
                accepted,
                dx: 0,
                dz: -1,
                side: NavPortalSide.North,
                boundaryCoord: targetMinZ);
            TryAddNeighborPortals(
                request,
                grid,
                target,
                targetMinX,
                targetMinZ,
                targetMaxX,
                targetMaxZ,
                localTriIndices,
                walkable,
                sheetIds,
                targetSegments,
                accepted,
                dx: 0,
                dz: 1,
                side: NavPortalSide.South,
                boundaryCoord: targetMaxZ);

            accepted.Sort(static (a, b) =>
            {
                int c = a.Side.CompareTo(b.Side);
                if (c != 0) return c;
                c = a.SheetId.CompareTo(b.SheetId);
                if (c != 0) return c;
                c = a.Along0.CompareTo(b.Along0);
                if (c != 0) return c;
                c = a.LeftY0.CompareTo(b.LeftY0);
                if (c != 0) return c;
                c = a.Along1.CompareTo(b.Along1);
                if (c != 0) return c;
                return a.LeftY1.CompareTo(b.LeftY1);
            });

            NavBorderPortalCoordinateContract.RequireTileExtentFitsPortalCoordinates(
                grid.TileWidthCm,
                grid.TileHeightCm,
                "CdtTriangleSurfaceBaker.BuildPortals");

            var portals = new List<NavBorderPortal>();
            for (int i = 0; i < accepted.Count; i++)
            {
                BoundarySegment seg = accepted[i];
                if (seg.ClearanceCm < request.AgentRadiusCm)
                {
                    continue;
                }

                int lx0 = checked(seg.LeftX0 - targetMinX);
                int lz0 = checked(seg.LeftZ0 - targetMinZ);
                int lx1 = checked(seg.LeftX1 - targetMinX);
                int lz1 = checked(seg.LeftZ1 - targetMinZ);
                short u0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(lx0, "CdtTriangleSurfaceBaker.BuildPortals.u0");
                short v0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(lz0, "CdtTriangleSurfaceBaker.BuildPortals.v0");
                short u1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(lx1, "CdtTriangleSurfaceBaker.BuildPortals.u1");
                short v1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(lz1, "CdtTriangleSurfaceBaker.BuildPortals.v1");

                portals.Add(new NavBorderPortal(
                    seg.Side,
                    u0,
                    v0,
                    u1,
                    v1,
                    lx0,
                    seg.LeftY0,
                    lz0,
                    lx1,
                    seg.LeftY1,
                    lz1,
                    seg.ClearanceCm));
            }

            return portals.ToArray();
        }

        private static List<BoundarySegment> CollectBoundarySegments(
            int targetMinX,
            int targetMinZ,
            int targetMaxX,
            int targetMaxZ,
            List<int> outX,
            List<int> outY,
            List<int> outZ,
            List<int> outA,
            List<int> outB,
            List<int> outC,
            List<int> outSheet)
        {
            var segments = new List<BoundarySegment>();
            int triCount = outA.Count;
            for (int t = 0; t < triCount; t++)
            {
                int sheet = outSheet[t];
                TryAddBoundaryEdge(outX, outY, outZ, outA[t], outB[t], NavPortalSide.West, targetMinX, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outB[t], outC[t], NavPortalSide.West, targetMinX, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outC[t], outA[t], NavPortalSide.West, targetMinX, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outA[t], outB[t], NavPortalSide.East, targetMaxX, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outB[t], outC[t], NavPortalSide.East, targetMaxX, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outC[t], outA[t], NavPortalSide.East, targetMaxX, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outA[t], outB[t], NavPortalSide.North, targetMinZ, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outB[t], outC[t], NavPortalSide.North, targetMinZ, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outC[t], outA[t], NavPortalSide.North, targetMinZ, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outA[t], outB[t], NavPortalSide.South, targetMaxZ, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outB[t], outC[t], NavPortalSide.South, targetMaxZ, sheet, segments);
                TryAddBoundaryEdge(outX, outY, outZ, outC[t], outA[t], NavPortalSide.South, targetMaxZ, sheet, segments);
            }

            return segments;
        }

        private static void TryAddBoundaryEdge(
            List<int> x,
            List<int> y,
            List<int> z,
            int i0,
            int i1,
            NavPortalSide side,
            int boundaryCoord,
            int sheetId,
            List<BoundarySegment> dst)
        {
            int x0 = x[i0];
            int y0 = y[i0];
            int z0 = z[i0];
            int x1 = x[i1];
            int y1 = y[i1];
            int z1 = z[i1];
            bool onBoundary = side switch
            {
                NavPortalSide.West => x0 == boundaryCoord && x1 == boundaryCoord,
                NavPortalSide.East => x0 == boundaryCoord && x1 == boundaryCoord,
                NavPortalSide.North => z0 == boundaryCoord && z1 == boundaryCoord,
                NavPortalSide.South => z0 == boundaryCoord && z1 == boundaryCoord,
                _ => false
            };
            if (!onBoundary)
            {
                return;
            }

            int along0;
            int along1;
            switch (side)
            {
                case NavPortalSide.West:
                case NavPortalSide.East:
                    along0 = z0;
                    along1 = z1;
                    break;
                default:
                    along0 = x0;
                    along1 = x1;
                    break;
            }

            if (along0 == along1)
            {
                return;
            }

            if (along0 > along1)
            {
                (along0, along1) = (along1, along0);
                (x0, x1) = (x1, x0);
                (y0, y1) = (y1, y0);
                (z0, z1) = (z1, z0);
            }

            // Clearance is half the positive along span (same contract as Recast). Using the tile
            // extent here poisons Detour EdgeAlignedWithPortalSideBand so opposite-side portals
            // falsely claim every border edge and flip external-link direction on interior tiles.
            int clearance = checked((along1 - along0) / 2);
            dst.Add(new BoundarySegment
            {
                Side = side,
                Along0 = along0,
                Along1 = along1,
                LeftY0 = y0,
                LeftY1 = y1,
                LeftX0 = x0,
                LeftZ0 = z0,
                LeftX1 = x1,
                LeftZ1 = z1,
                ClearanceCm = clearance,
                SheetId = sheetId
            });
        }

        private static void TryAddNeighborPortals(
            in CdtTriangleSurfaceBakeRequest request,
            NavTriangleSurfaceTileGrid grid,
            NavBakeTileCoord target,
            int targetMinX,
            int targetMinZ,
            int targetMaxX,
            int targetMaxZ,
            int[] localTriIndices,
            bool[] walkable,
            int[] sheetIds,
            List<BoundarySegment> targetSegments,
            List<BoundarySegment> accepted,
            int dx,
            int dz,
            NavPortalSide side,
            int boundaryCoord)
        {
            int nx = target.ChunkX + dx;
            int nz = target.ChunkY + dz;
            if (nx < 0 || nz < 0 || nx >= grid.TileCountX || nz >= grid.TileCountZ)
            {
                return;
            }

            var neighborSegments = CollectNeighborBoundarySegments(
                request,
                grid,
                new NavBakeTileCoord(nx, nz),
                boundaryCoord,
                side);

            for (int i = 0; i < targetSegments.Count; i++)
            {
                BoundarySegment a = targetSegments[i];
                if (a.Side != side)
                {
                    continue;
                }

                for (int j = 0; j < neighborSegments.Count; j++)
                {
                    BoundarySegment b = neighborSegments[j];
                    if (TryMergePortalSegments(a, b, request.MaxClimbCm, out BoundarySegment merged))
                    {
                        accepted.Add(merged);
                    }
                }
            }
        }

        private static List<BoundarySegment> CollectNeighborBoundarySegments(
            in CdtTriangleSurfaceBakeRequest request,
            NavTriangleSurfaceTileGrid grid,
            NavBakeTileCoord neighbor,
            int boundaryCoord,
            NavPortalSide side)
        {
            var segments = new List<BoundarySegment>();
            int neighborMinX = checked(grid.OriginXcm + checked(neighbor.ChunkX * grid.TileWidthCm));
            int neighborMinZ = checked(grid.OriginZcm + checked(neighbor.ChunkY * grid.TileHeightCm));
            int neighborMaxX = checked(neighborMinX + grid.TileWidthCm);
            int neighborMaxZ = checked(neighborMinZ + grid.TileHeightCm);
            NavTriangleSurfaceSnapshot surface = request.SurfaceIndex.Surface;
            ReadOnlySpan<int> selection = request.SurfaceIndex.GetTriangleIndices(neighbor);
            ReadOnlySpan<int> vx = surface.VertexXcm;
            ReadOnlySpan<int> vy = surface.VertexYcm;
            ReadOnlySpan<int> vz = surface.VertexZcm;
            ReadOnlySpan<int> triA = surface.TriA;
            ReadOnlySpan<int> triB = surface.TriB;
            ReadOnlySpan<int> triC = surface.TriC;

            for (int si = 0; si < selection.Length; si++)
            {
                int tri = selection[si];
                if (!TriangleSurfaceWalkability.IsWalkableTriangle(
                        tri,
                        vx,
                        vy,
                        vz,
                        triA,
                        triB,
                        triC,
                        surface.TriFlags,
                        request.MinWalkableUpDotQ1M,
                        request.AgentHeightCm,
                        request.Obstacles,
                        request.LayerId,
                        request.AgentRadiusCm,
                        selection))
                {
                    continue;
                }

                int sheet = 0;
                int ia = triA[tri];
                int ib = triB[tri];
                int ic = triC[tri];
                int ax = vx[ia];
                int ay = vy[ia];
                int az = vz[ia];
                int bx = vx[ib];
                int by = vy[ib];
                int bz = vz[ib];
                int cx = vx[ic];
                int cy = vy[ic];
                int cz = vz[ic];
                if (TriangleFullyInsideTarget(
                        ax, az, bx, bz, cx, cz,
                        neighborMinX,
                        neighborMinZ,
                        neighborMaxX,
                        neighborMaxZ))
                {
                    AddSurfaceBoundaryEdge(vx, vy, vz, ia, ib, ic, side, boundaryCoord, sheet, segments);
                    AddSurfaceBoundaryEdge(vx, vy, vz, ib, ic, ia, side, boundaryCoord, sheet, segments);
                    AddSurfaceBoundaryEdge(vx, vy, vz, ic, ia, ib, side, boundaryCoord, sheet, segments);
                    continue;
                }

                var clippedX = new List<int>(6);
                var clippedY = new List<int>(6);
                var clippedZ = new List<int>(6);
                var clippedIndex = new Dictionary<VertexKey, int>();
                var clippedA = new List<int>(4);
                var clippedB = new List<int>(4);
                var clippedC = new List<int>(4);
                ClipTriangleToTarget(
                    ax, ay, az,
                    bx, by, bz,
                    cx, cy, cz,
                    neighborMinX,
                    neighborMinZ,
                    neighborMaxX,
                    neighborMaxZ,
                    clippedIndex,
                    clippedX,
                    clippedY,
                    clippedZ,
                    clippedA,
                    clippedB,
                    clippedC,
                    maxLawsonFlipCount: 0);
                int[] clippedXArray = clippedX.ToArray();
                int[] clippedYArray = clippedY.ToArray();
                int[] clippedZArray = clippedZ.ToArray();
                for (int clipped = 0; clipped < clippedA.Count; clipped++)
                {
                    AddSurfaceBoundaryEdge(
                        clippedXArray, clippedYArray, clippedZArray,
                        clippedA[clipped], clippedB[clipped], clippedC[clipped],
                        side, boundaryCoord, sheet, segments);
                    AddSurfaceBoundaryEdge(
                        clippedXArray, clippedYArray, clippedZArray,
                        clippedB[clipped], clippedC[clipped], clippedA[clipped],
                        side, boundaryCoord, sheet, segments);
                    AddSurfaceBoundaryEdge(
                        clippedXArray, clippedYArray, clippedZArray,
                        clippedC[clipped], clippedA[clipped], clippedB[clipped],
                        side, boundaryCoord, sheet, segments);
                }
            }

            return segments;
        }

        private static void AddSurfaceBoundaryEdge(
            ReadOnlySpan<int> x,
            ReadOnlySpan<int> y,
            ReadOnlySpan<int> z,
            int i0,
            int i1,
            int iOpposite,
            NavPortalSide side,
            int boundaryCoord,
            int sheetId,
            List<BoundarySegment> dst)
        {
            int x0 = x[i0];
            int y0 = y[i0];
            int z0 = z[i0];
            int x1 = x[i1];
            int y1 = y[i1];
            int z1 = z[i1];
            bool onBoundary = side switch
            {
                NavPortalSide.West or NavPortalSide.East => x0 == boundaryCoord && x1 == boundaryCoord,
                NavPortalSide.North or NavPortalSide.South => z0 == boundaryCoord && z1 == boundaryCoord,
                _ => false
            };
            if (!onBoundary)
            {
                return;
            }

            // Halo CSR also lists the target-side triangle on the neighbor tile. Only the opposite
            // half-space triangle is neighbor evidence; otherwise the same seam is merged twice and
            // clearance/portal direction contracts become ambiguous.
            int ox = x[iOpposite];
            int oz = z[iOpposite];
            bool neighborHalfSpace = side switch
            {
                NavPortalSide.West => ox < boundaryCoord,
                NavPortalSide.East => ox > boundaryCoord,
                NavPortalSide.North => oz < boundaryCoord,
                NavPortalSide.South => oz > boundaryCoord,
                _ => false
            };
            if (!neighborHalfSpace)
            {
                return;
            }

            int along0;
            int along1;
            switch (side)
            {
                case NavPortalSide.West:
                case NavPortalSide.East:
                    along0 = z0;
                    along1 = z1;
                    break;
                default:
                    along0 = x0;
                    along1 = x1;
                    break;
            }

            if (along0 == along1)
            {
                return;
            }

            if (along0 > along1)
            {
                (along0, along1) = (along1, along0);
                (x0, x1) = (x1, x0);
                (y0, y1) = (y1, y0);
                (z0, z1) = (z1, z0);
            }

            int clearance = checked((along1 - along0) / 2);
            dst.Add(new BoundarySegment
            {
                Side = side,
                Along0 = along0,
                Along1 = along1,
                LeftY0 = y0,
                LeftY1 = y1,
                LeftX0 = x0,
                LeftZ0 = z0,
                LeftX1 = x1,
                LeftZ1 = z1,
                ClearanceCm = clearance,
                SheetId = sheetId
            });
        }

        private static bool TryMergePortalSegments(
            BoundarySegment a,
            BoundarySegment b,
            int maxClimbCm,
            out BoundarySegment merged)
        {
            merged = default;
            int along0 = Math.Max(a.Along0, b.Along0);
            int along1 = Math.Min(a.Along1, b.Along1);
            if (along1 <= along0)
            {
                return false;
            }

            int yA0 = InterpolateY(a, along0);
            int yA1 = InterpolateY(a, along1);
            int yB0 = InterpolateY(b, along0);
            int yB1 = InterpolateY(b, along1);
            if (Math.Abs(yA0 - yB0) > maxClimbCm || Math.Abs(yA1 - yB1) > maxClimbCm)
            {
                return false;
            }

            merged = a;
            merged.Along0 = along0;
            merged.Along1 = along1;
            merged.LeftY0 = yA0;
            merged.LeftY1 = yA1;
            merged.ClearanceCm = Math.Min(a.ClearanceCm, b.ClearanceCm);
            if (a.Side is NavPortalSide.West or NavPortalSide.East)
            {
                merged.LeftX0 = a.LeftX0;
                merged.LeftX1 = a.LeftX1;
                merged.LeftZ0 = along0;
                merged.LeftZ1 = along1;
            }
            else
            {
                merged.LeftZ0 = a.LeftZ0;
                merged.LeftZ1 = a.LeftZ1;
                merged.LeftX0 = along0;
                merged.LeftX1 = along1;
            }

            return true;
        }

        private static int InterpolateY(BoundarySegment seg, int along)
        {
            int along0 = seg.Along0;
            int along1 = seg.Along1;
            if (along1 == along0)
            {
                return seg.LeftY0;
            }

            long num = (long)along - along0;
            long denom = along1 - along0;
            return seg.LeftY0 + (int)((num * (seg.LeftY1 - seg.LeftY0)) / denom);
        }

        private static int SampleYOnTrianglePlane(
            int px,
            int pz,
            int ia,
            int ib,
            int ic,
            ReadOnlySpan<int> vertexX,
            ReadOnlySpan<int> vertexY,
            ReadOnlySpan<int> vertexZ)
        {
            int ax = vertexX[ia];
            int ay = vertexY[ia];
            int az = vertexZ[ia];
            int bx = vertexX[ib];
            int by = vertexY[ib];
            int bz = vertexZ[ib];
            int cx = vertexX[ic];
            int cy = vertexY[ic];
            int cz = vertexZ[ic];
            Int128 v0x = (Int128)bx - ax;
            Int128 v0y = (Int128)by - ay;
            Int128 v0z = (Int128)bz - az;
            Int128 v1x = (Int128)cx - ax;
            Int128 v1y = (Int128)cy - ay;
            Int128 v1z = (Int128)cz - az;
            Int128 nx = (v0y * v1z) - (v0z * v1y);
            Int128 ny = (v0z * v1x) - (v0x * v1z);
            Int128 nz = (v0x * v1y) - (v0y * v1x);
            if (ny == 0)
            {
                return ay;
            }

            Int128 dx = (Int128)px - ax;
            Int128 dz = (Int128)pz - az;
            Int128 numer = -(nx * dx + nz * dz);
            Int128 y = ay + numer / ny;
            if (numer < 0 && (numer % ny) != 0)
            {
                y--;
            }

            if (y < int.MinValue || y > int.MaxValue)
            {
                throw new InvalidOperationException("CdtTriangleSurfaceBaker SampleYOnTrianglePlane result outside int range.");
            }

            return (int)y;
        }

        private static int Min3(int a, int b, int c)
        {
            int m = a;
            if (b < m) m = b;
            if (c < m) m = c;
            return m;
        }

        private static int Max3(int a, int b, int c)
        {
            int m = a;
            if (b > m) m = b;
            if (c > m) m = c;
            return m;
        }
    }
}
