using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Navigation.NavMesh
{
    public static class NavTileBuilder
    {
        private readonly struct Vtx
        {
            public readonly int C;
            public readonly int R;
            public readonly Vector3 Pos;
            public readonly float WaterY;
            public readonly byte H;
            public readonly byte W;
            public readonly bool IsRamp;
            public readonly bool IsBlocked;

            public Vtx(int c, int r, Vector3 pos, float waterY, byte h, byte w, bool isRamp, bool isBlocked)
            {
                C = c;
                R = r;
                Pos = pos;
                WaterY = waterY;
                H = h;
                W = w;
                IsRamp = isRamp;
                IsBlocked = isBlocked;
            }
        }

        private readonly struct SplitPoints
        {
            public readonly Vector3 HighExt;
            public readonly float HighWaterY;
            public readonly Vector3 LowExt;
            public readonly float LowWaterY;

            public SplitPoints(Vector3 highExt, float highWaterY, Vector3 lowExt, float lowWaterY)
            {
                HighExt = highExt;
                HighWaterY = highWaterY;
                LowExt = lowExt;
                LowWaterY = lowWaterY;
            }
        }

        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            public readonly int Xcm;
            public readonly int Ycm;
            public readonly int Zcm;

            public VertexKey(int xcm, int ycm, int zcm)
            {
                Xcm = xcm;
                Ycm = ycm;
                Zcm = zcm;
            }

            public bool Equals(VertexKey other) => Xcm == other.Xcm && Ycm == other.Ycm && Zcm == other.Zcm;
            public override bool Equals(object obj) => obj is VertexKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Xcm, Ycm, Zcm);
        }

        public static bool TryBuildTile(
            VertexMap map,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig config,
            out NavTile tile,
            out NavBakeArtifact artifact)
        {
            tile = null;
            artifact = default;

            if (map == null)
            {
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, 0), tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "VertexMap is null.", 0, 0, 0, 0);
                return false;
            }

            int startC = chunkX * VertexChunk.ChunkSize;
            int startR = chunkY * VertexChunk.ChunkSize;
            int mapWidth = map.WidthInChunks * VertexChunk.ChunkSize;
            int mapHeight = map.HeightInChunks * VertexChunk.ChunkSize;
            if (startC < 0 || startR < 0 || startC >= mapWidth || startR >= mapHeight)
            {
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, 0), tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "Tile out of range.", 0, 0, 0, 0);
                return false;
            }

            float originXm = startC * HexCoordinates.HexWidth;
            float originZm = startR * HexCoordinates.RowSpacing;
            int originXcm = (int)MathF.Round(originXm * 100f);
            int originZcm = (int)MathF.Round(originZm * 100f);

            var vertexIndex = new Dictionary<VertexKey, int>(4096);
            var vx = new List<int>(4096);
            var vy = new List<int>(4096);
            var vz = new List<int>(4096);
            var triA = new List<int>(8192);
            var triB = new List<int>(8192);
            var triC = new List<int>(8192);
            var cellWalkable = new bool[VertexChunk.ChunkSize * VertexChunk.ChunkSize];

            int walkableTriCount = 0;

            for (int r = startR; r < startR + VertexChunk.ChunkSize; r++)
            {
                for (int c = startC; c < startC + VertexChunk.ChunkSize; c++)
                {
                    if (r >= mapHeight - 1 || c >= mapWidth - 1) continue;

                    int lr = r - startR;
                    int lc = c - startC;
                    cellWalkable[lr * VertexChunk.ChunkSize + lc] = IsCellAnyTriangleWalkable(map, mapWidth, mapHeight, c, r, config);

                    bool isOdd = (r & 1) == 1;
                    var v1 = GetVertex(map, mapWidth, mapHeight, c, r, originXm, originZm, config.HeightScaleMeters);

                    Vtx t1p1, t1p2, t1p3;
                    Vtx t2p1, t2p2, t2p3;

                    if (!isOdd)
                    {
                        t1p1 = v1;
                        t1p2 = GetVertex(map, mapWidth, mapHeight, c + 1, r, originXm, originZm, config.HeightScaleMeters);
                        t1p3 = GetVertex(map, mapWidth, mapHeight, c, r + 1, originXm, originZm, config.HeightScaleMeters);

                        t2p1 = t1p2;
                        t2p2 = GetVertex(map, mapWidth, mapHeight, c + 1, r + 1, originXm, originZm, config.HeightScaleMeters);
                        t2p3 = t1p3;
                    }
                    else
                    {
                        t1p1 = v1;
                        t1p2 = GetVertex(map, mapWidth, mapHeight, c + 1, r, originXm, originZm, config.HeightScaleMeters);
                        t1p3 = GetVertex(map, mapWidth, mapHeight, c + 1, r + 1, originXm, originZm, config.HeightScaleMeters);

                        t2p1 = v1;
                        t2p2 = t1p3;
                        t2p3 = GetVertex(map, mapWidth, mapHeight, c, r + 1, originXm, originZm, config.HeightScaleMeters);
                    }

                    AddFace(map, mapWidth, mapHeight, originXm, originZm, config, t1p1, t1p2, t1p3, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);
                    AddFace(map, mapWidth, mapHeight, originXm, originZm, config, t2p1, t2p2, t2p3, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);
                }
            }

            if (triA.Count == 0)
            {
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, 0), tileVersion, NavBakeStage.Triangulate, NavBakeErrorCode.NoWalkableDomain, "No walkable triangles.", 0, 0, 0, 0);
                return false;
            }

            var n0 = new int[triA.Count];
            var n1 = new int[triA.Count];
            var n2 = new int[triA.Count];
            Array.Fill(n0, -1);
            Array.Fill(n1, -1);
            Array.Fill(n2, -1);
            BuildAdjacency(triA, triB, triC, n0, n1, n2);

            var clearanceCm = ComputeClearanceCmField(cellWalkable);
            var portals = BuildPortals(map, mapWidth, mapHeight, startC, startR, originXm, originZm, clearanceCm, config);

            ulong buildHash = config.ComputeHash();
            var tmpTile = new NavTile(
                new NavTileId(chunkX, chunkY, 0),
                tileVersion,
                buildHash,
                0UL,
                originXcm,
                originZcm,
                vx.ToArray(),
                vy.ToArray(),
                vz.ToArray(),
                triA.ToArray(),
                triB.ToArray(),
                triC.ToArray(),
                n0,
                n1,
                n2,
                portals);

            using (var ms = new System.IO.MemoryStream())
            {
                NavTileBinary.Write(ms, tmpTile);
                ms.Position = 0;
                var readBack = NavTileBinary.Read(ms);
                tile = readBack;
            }

            artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "", walkableTriCount, tile.VertexCount, tile.TriangleCount, tile.Portals.Length);
            return true;
        }

        private static Vtx GetVertex(VertexMap map, int mapWidth, int mapHeight, int c, int r, float originXm, float originZm, float heightScale)
        {
            byte h = 0;
            byte w = 0;
            bool ramp = false;
            bool blocked = false;

            if ((uint)c < (uint)mapWidth && (uint)r < (uint)mapHeight)
            {
                var chunk = map.GetChunk(c, r, false);
                if (chunk != null)
                {
                    int lx = c & VertexChunk.ChunkSizeMask;
                    int lr = r & VertexChunk.ChunkSizeMask;
                    h = chunk.GetHeight(lx, lr);
                    w = chunk.GetWaterHeight(lx, lr);
                    ramp = chunk.GetRamp(lx, lr);
                    blocked = chunk.GetFlag(lx, lr);
                }
            }

            float x = HexCoordinates.HexWidth * (c + 0.5f * (r & 1)) - originXm;
            float z = HexCoordinates.RowSpacing * r - originZm;
            float y = h * heightScale;
            float waterY = w * heightScale;
            return new Vtx(c, r, new Vector3(x, y, z), waterY, h, w, ramp, blocked);
        }

        private static void AddFace(
            VertexMap map,
            int mapWidth,
            int mapHeight,
            float originXm,
            float originZm,
            in NavBuildConfig config,
            in Vtx p1,
            in Vtx p2,
            in Vtx p3,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            ref int walkableTriCount)
        {
            byte minH = Math.Min(p1.H, Math.Min(p2.H, p3.H));
            byte maxH = Math.Max(p1.H, Math.Max(p2.H, p3.H));

            if (minH == maxH)
            {
                AppendWalkableTri(config, p1.Pos, p1.WaterY, p2.Pos, p2.WaterY, p3.Pos, p3.WaterY, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);
                return;
            }

            bool isRamp = p1.IsRamp || p2.IsRamp || p3.IsRamp;
            if (isRamp)
            {
                AppendWalkableTri(config, p1.Pos, p1.WaterY, p2.Pos, p2.WaterY, p3.Pos, p3.WaterY, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);
                return;
            }

            if (p1.H != p2.H && p1.H != p3.H && p2.H != p3.H) return;

            Vtx a = p1;
            Vtx b = p2;
            Vtx c = p3;
            if (a.H < b.H) (a, b) = (b, a);
            if (a.H < c.H) (a, c) = (c, a);
            if (b.H < c.H) (b, c) = (c, b);

            if (a.H == b.H)
            {
                Vtx h1 = a;
                Vtx h2 = b;
                Vtx l = c;

                if (TryGetSplit(map, mapWidth, mapHeight, originXm, originZm, config.HeightScaleMeters, h1, l, out var m1) &&
                    TryGetSplit(map, mapWidth, mapHeight, originXm, originZm, config.HeightScaleMeters, h2, l, out var m2))
                {
                    AppendWalkableTri(config, h1.Pos, h1.WaterY, h2.Pos, h2.WaterY, m1.HighExt, m1.HighWaterY, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);
                    AppendWalkableTri(config, h2.Pos, h2.WaterY, m2.HighExt, m2.HighWaterY, m1.HighExt, m1.HighWaterY, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);

                    AppendWalkableTri(config, l.Pos, l.WaterY, m2.LowExt, m2.LowWaterY, m1.LowExt, m1.LowWaterY, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);
                }

                return;
            }

            Vtx h = a;
            Vtx l1 = b;
            Vtx l2 = c;

            if (TryGetSplit(map, mapWidth, mapHeight, originXm, originZm, config.HeightScaleMeters, h, l1, out var s1) &&
                TryGetSplit(map, mapWidth, mapHeight, originXm, originZm, config.HeightScaleMeters, h, l2, out var s2))
            {
                AppendWalkableTri(config, h.Pos, h.WaterY, s1.HighExt, s1.HighWaterY, s2.HighExt, s2.HighWaterY, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);

                AppendWalkableTri(config, l1.Pos, l1.WaterY, l2.Pos, l2.WaterY, s1.LowExt, s1.LowWaterY, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);
                AppendWalkableTri(config, l2.Pos, l2.WaterY, s2.LowExt, s2.LowWaterY, s1.LowExt, s1.LowWaterY, vertexIndex, vx, vy, vz, triA, triB, triC, ref walkableTriCount);
            }
        }

        private static void AppendWalkableTri(
            in NavBuildConfig config,
            Vector3 a,
            float wa,
            Vector3 b,
            float wb,
            Vector3 c,
            float wc,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            ref int walkableTriCount)
        {
            if (wa > a.Y || wb > b.Y || wc > c.Y) return;

            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 n = Vector3.Cross(ab, ac);
            float len = n.Length();
            if (len <= 1e-6f) return;
            n /= len;
            if (n.Y < 0f) n = -n;
            if (n.Y < config.MinWalkableUpDot) return;

            int ia = GetOrAddVertex(a, vertexIndex, vx, vy, vz);
            int ib = GetOrAddVertex(b, vertexIndex, vx, vy, vz);
            int ic = GetOrAddVertex(c, vertexIndex, vx, vy, vz);
            if (ia == ib || ib == ic || ia == ic) return;

            triA.Add(ia);
            triB.Add(ib);
            triC.Add(ic);
            walkableTriCount++;
        }

        private static int GetOrAddVertex(Vector3 p, Dictionary<VertexKey, int> vertexIndex, List<int> vx, List<int> vy, List<int> vz)
        {
            int xcm = (int)MathF.Round(p.X * 100f);
            int ycm = (int)MathF.Round(p.Y * 100f);
            int zcm = (int)MathF.Round(p.Z * 100f);
            var key = new VertexKey(xcm, ycm, zcm);
            if (vertexIndex.TryGetValue(key, out int idx)) return idx;
            idx = vx.Count;
            vertexIndex.Add(key, idx);
            vx.Add(xcm);
            vy.Add(ycm);
            vz.Add(zcm);
            return idx;
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int A;
            public readonly int B;

            public EdgeKey(int a, int b)
            {
                if (a < b) { A = a; B = b; }
                else { A = b; B = a; }
            }

            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B);
        }

        private readonly struct EdgeRef
        {
            public readonly int TriId;
            public readonly int EdgeId;

            public EdgeRef(int triId, int edgeId)
            {
                TriId = triId;
                EdgeId = edgeId;
            }
        }

        private static void BuildAdjacency(List<int> triA, List<int> triB, List<int> triC, int[] n0, int[] n1, int[] n2)
        {
            var map = new Dictionary<EdgeKey, EdgeRef>(triA.Count * 2);
            for (int t = 0; t < triA.Count; t++)
            {
                int a = triA[t];
                int b = triB[t];
                int c = triC[t];
                AddEdge(map, n0, n1, n2, t, 0, a, b);
                AddEdge(map, n0, n1, n2, t, 1, b, c);
                AddEdge(map, n0, n1, n2, t, 2, c, a);
            }
        }

        private static void AddEdge(Dictionary<EdgeKey, EdgeRef> map, int[] n0, int[] n1, int[] n2, int triId, int edgeId, int va, int vb)
        {
            var key = new EdgeKey(va, vb);
            if (map.TryGetValue(key, out var other))
            {
                SetNeighbor(n0, n1, n2, triId, edgeId, other.TriId);
                SetNeighbor(n0, n1, n2, other.TriId, other.EdgeId, triId);
            }
            else
            {
                map.Add(key, new EdgeRef(triId, edgeId));
            }
        }

        private static void SetNeighbor(int[] n0, int[] n1, int[] n2, int triId, int edgeId, int neighborTriId)
        {
            if (edgeId == 0) n0[triId] = neighborTriId;
            else if (edgeId == 1) n1[triId] = neighborTriId;
            else n2[triId] = neighborTriId;
        }

        private static bool TryGetSplit(
            VertexMap map,
            int mapWidth,
            int mapHeight,
            float originXm,
            float originZm,
            float heightScale,
            in Vtx high,
            in Vtx low,
            out SplitPoints split)
        {
            split = default;
            if (high.H == low.H) return false;

            float midX = (high.Pos.X + low.Pos.X) * 0.5f;
            float midZ = (high.Pos.Z + low.Pos.Z) * 0.5f;
            float midWaterY = (high.WaterY + low.WaterY) * 0.5f;

            float highExtX = midX;
            float lowExtX = midX;

            bool shouldStraighten = GetCliffStraighten(map, mapWidth, mapHeight, high.C, high.R, low.C, low.R);

            if (shouldStraighten)
            {
                float dirX = MathF.Sign(low.Pos.X - high.Pos.X);
                float smoothedX = HexCoordinates.HexWidth * (high.C + 0.25f) - originXm;
                float bias = HexCoordinates.HexWidth * 0.5f;
                if (dirX != 0f) smoothedX += dirX * bias;
                highExtX = smoothedX;
                lowExtX = smoothedX;
            }

            Vector3 highExt = new Vector3(highExtX, high.Pos.Y, midZ);
            Vector3 lowExt = new Vector3(lowExtX, low.Pos.Y, midZ);
            split = new SplitPoints(highExt, midWaterY, lowExt, midWaterY);
            return true;
        }

        private static bool GetCliffStraighten(VertexMap map, int mapWidth, int mapHeight, int cA, int rA, int cB, int rB)
        {
            int baseC;
            int baseR;
            int edgeIndex;

            if (rA == rB)
            {
                if (cA + 1 == cB)
                {
                    baseC = cA;
                    baseR = rA;
                    edgeIndex = 0;
                }
                else if (cB + 1 == cA)
                {
                    baseC = cB;
                    baseR = rB;
                    edgeIndex = 0;
                }
                else
                {
                    return false;
                }
            }
            else if (rA + 1 == rB || rB + 1 == rA)
            {
                bool aUpper = rA < rB;
                int upC = aUpper ? cA : cB;
                int upR = aUpper ? rA : rB;
                int downC = aUpper ? cB : cA;
                int downR = aUpper ? rB : rA;

                if (downR != upR + 1) return false;

                bool isOdd = (upR & 1) == 1;
                int brC = isOdd ? upC + 1 : upC;
                int brR = upR + 1;
                int blC = isOdd ? upC : upC - 1;
                int blR = upR + 1;

                if (downC == brC && downR == brR)
                {
                    baseC = upC;
                    baseR = upR;
                    edgeIndex = 1;
                }
                else if (downC == blC && downR == blR)
                {
                    baseC = upC;
                    baseR = upR;
                    edgeIndex = 2;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            if ((uint)baseC >= (uint)mapWidth || (uint)baseR >= (uint)mapHeight) return false;
            var chunk = map.GetChunk(baseC, baseR, false);
            if (chunk == null) return false;
            return chunk.GetCliffStraightenEdge(baseC & VertexChunk.ChunkSizeMask, baseR & VertexChunk.ChunkSizeMask, edgeIndex);
        }

        private static NavBorderPortal[] BuildPortals(VertexMap map, int mapWidth, int mapHeight, int startC, int startR, float originXm, float originZm, int[] clearanceCm, in NavBuildConfig config)
        {
            var portals = new List<NavBorderPortal>(64);
            int endC = startC + VertexChunk.ChunkSize;
            int endR = startR + VertexChunk.ChunkSize;

            AddVerticalPortals(map, mapWidth, mapHeight, startC, startR, endR, originXm, originZm, clearanceCm, config, NavPortalSide.West, insideC: startC, outsideC: startC - 1, portals);
            AddVerticalPortals(map, mapWidth, mapHeight, endC, startR, endR, originXm, originZm, clearanceCm, config, NavPortalSide.East, insideC: endC - 1, outsideC: endC, portals);
            AddHorizontalPortals(map, mapWidth, mapHeight, startR, startC, endC, originXm, originZm, clearanceCm, config, NavPortalSide.North, insideR: startR, outsideR: startR - 1, portals);
            AddHorizontalPortals(map, mapWidth, mapHeight, endR, startC, endC, originXm, originZm, clearanceCm, config, NavPortalSide.South, insideR: endR - 1, outsideR: endR, portals);

            return portals.ToArray();
        }

        private static void AddVerticalPortals(
            VertexMap map,
            int mapWidth,
            int mapHeight,
            int boundaryCol,
            int startR,
            int endR,
            float originXm,
            float originZm,
            int[] clearanceCm,
            in NavBuildConfig config,
            NavPortalSide side,
            int insideC,
            int outsideC,
            List<NavBorderPortal> dst)
        {
            int segStart = -1;
            for (int r = startR; r < endR; r++)
            {
                bool inside = IsCellAnyTriangleWalkable(map, mapWidth, mapHeight, insideC, r, config);
                bool outside = IsCellAnyTriangleWalkable(map, mapWidth, mapHeight, outsideC, r, config);
                bool passable = inside && outside;

                int localV = r - startR;
                if (passable)
                {
                    if (segStart < 0) segStart = localV;
                }
                else
                {
                    if (segStart >= 0)
                    {
                        AddVerticalPortalSegment(boundaryCol, startR, segStart, localV, originXm, originZm, clearanceCm, side, dst);
                        segStart = -1;
                    }
                }
            }

            if (segStart >= 0)
            {
                AddVerticalPortalSegment(boundaryCol, startR, segStart, endR - startR, originXm, originZm, clearanceCm, side, dst);
            }
        }

        private static void AddVerticalPortalSegment(int boundaryCol, int startR, int v0, int v1, float originXm, float originZm, int[] clearanceCm, NavPortalSide side, List<NavBorderPortal> dst)
        {
            short u = side == NavPortalSide.West ? (short)0 : (short)VertexChunk.ChunkSize;
            short sv0 = (short)v0;
            short sv1 = (short)v1;

            int r0 = startR + v0;
            int r1 = startR + v1;
            float x0m = HexCoordinates.HexWidth * (boundaryCol + 0.5f * (r0 & 1)) - originXm;
            float z0m = HexCoordinates.RowSpacing * r0 - originZm;
            float x1m = HexCoordinates.HexWidth * (boundaryCol + 0.5f * (r1 & 1)) - originXm;
            float z1m = HexCoordinates.RowSpacing * r1 - originZm;

            int x0cm = (int)MathF.Round(x0m * 100f);
            int z0cm = (int)MathF.Round(z0m * 100f);
            int x1cm = (int)MathF.Round(x1m * 100f);
            int z1cm = (int)MathF.Round(z1m * 100f);
            int dx = x1cm - x0cm;
            int dz = z1cm - z0cm;
            int len = (int)MathF.Round(MathF.Sqrt(dx * dx + dz * dz));
            int minClearance = int.MaxValue;
            int lc = side == NavPortalSide.West ? 0 : (VertexChunk.ChunkSize - 1);
            for (int rr = v0; rr < v1; rr++)
            {
                int idx = rr * VertexChunk.ChunkSize + lc;
                int ccm = clearanceCm[idx];
                if (ccm < minClearance) minClearance = ccm;
            }
            int clearance = Math.Max(0, Math.Min(len / 2, minClearance));
            dst.Add(new NavBorderPortal(side, u, sv0, u, sv1, x0cm, z0cm, x1cm, z1cm, clearance));
        }

        private static void AddHorizontalPortals(
            VertexMap map,
            int mapWidth,
            int mapHeight,
            int boundaryRow,
            int startC,
            int endC,
            float originXm,
            float originZm,
            int[] clearanceCm,
            in NavBuildConfig config,
            NavPortalSide side,
            int insideR,
            int outsideR,
            List<NavBorderPortal> dst)
        {
            int segStart = -1;
            for (int c = startC; c < endC; c++)
            {
                bool inside = IsCellAnyTriangleWalkable(map, mapWidth, mapHeight, c, insideR, config);
                bool outside = IsCellAnyTriangleWalkable(map, mapWidth, mapHeight, c, outsideR, config);
                bool passable = inside && outside;

                int localU = c - startC;
                if (passable)
                {
                    if (segStart < 0) segStart = localU;
                }
                else
                {
                    if (segStart >= 0)
                    {
                        AddHorizontalPortalSegment(boundaryRow, startC, segStart, localU, originXm, originZm, clearanceCm, side, dst);
                        segStart = -1;
                    }
                }
            }

            if (segStart >= 0)
            {
                AddHorizontalPortalSegment(boundaryRow, startC, segStart, endC - startC, originXm, originZm, clearanceCm, side, dst);
            }
        }

        private static void AddHorizontalPortalSegment(int boundaryRow, int startC, int u0, int u1, float originXm, float originZm, int[] clearanceCm, NavPortalSide side, List<NavBorderPortal> dst)
        {
            short v = side == NavPortalSide.North ? (short)0 : (short)VertexChunk.ChunkSize;
            short su0 = (short)u0;
            short su1 = (short)u1;

            int c0 = startC + u0;
            int c1 = startC + u1;
            float x0m = HexCoordinates.HexWidth * (c0 + 0.5f * (boundaryRow & 1)) - originXm;
            float z0m = HexCoordinates.RowSpacing * boundaryRow - originZm;
            float x1m = HexCoordinates.HexWidth * (c1 + 0.5f * (boundaryRow & 1)) - originXm;
            float z1m = z0m;

            int x0cm = (int)MathF.Round(x0m * 100f);
            int z0cm = (int)MathF.Round(z0m * 100f);
            int x1cm = (int)MathF.Round(x1m * 100f);
            int z1cm = (int)MathF.Round(z1m * 100f);
            int dx = x1cm - x0cm;
            int dz = z1cm - z0cm;
            int len = (int)MathF.Round(MathF.Sqrt(dx * dx + dz * dz));
            int minClearance = int.MaxValue;
            int lr = side == NavPortalSide.North ? 0 : (VertexChunk.ChunkSize - 1);
            for (int cc = u0; cc < u1; cc++)
            {
                int idx = lr * VertexChunk.ChunkSize + cc;
                int ccm = clearanceCm[idx];
                if (ccm < minClearance) minClearance = ccm;
            }
            int clearance = Math.Max(0, Math.Min(len / 2, minClearance));
            dst.Add(new NavBorderPortal(side, su0, v, su1, v, x0cm, z0cm, x1cm, z1cm, clearance));
        }

        private static bool IsCellAnyTriangleWalkable(VertexMap map, int mapWidth, int mapHeight, int c, int r, in NavBuildConfig config)
        {
            if (r < 0 || c < 0 || r >= mapHeight - 1 || c >= mapWidth - 1) return false;
            bool isOdd = (r & 1) == 1;

            var v1 = GetVertex(map, mapWidth, mapHeight, c, r, 0f, 0f, config.HeightScaleMeters);

            Vtx t1p1, t1p2, t1p3;
            Vtx t2p1, t2p2, t2p3;

            if (!isOdd)
            {
                t1p1 = v1;
                t1p2 = GetVertex(map, mapWidth, mapHeight, c + 1, r, 0f, 0f, config.HeightScaleMeters);
                t1p3 = GetVertex(map, mapWidth, mapHeight, c, r + 1, 0f, 0f, config.HeightScaleMeters);

                t2p1 = t1p2;
                t2p2 = GetVertex(map, mapWidth, mapHeight, c + 1, r + 1, 0f, 0f, config.HeightScaleMeters);
                t2p3 = t1p3;
            }
            else
            {
                t1p1 = v1;
                t1p2 = GetVertex(map, mapWidth, mapHeight, c + 1, r, 0f, 0f, config.HeightScaleMeters);
                t1p3 = GetVertex(map, mapWidth, mapHeight, c + 1, r + 1, 0f, 0f, config.HeightScaleMeters);

                t2p1 = v1;
                t2p2 = t1p3;
                t2p3 = GetVertex(map, mapWidth, mapHeight, c, r + 1, 0f, 0f, config.HeightScaleMeters);
            }

            return IsBaseTriWalkable(t1p1, t1p2, t1p3, config) || IsBaseTriWalkable(t2p1, t2p2, t2p3, config);
        }

        private static bool IsBaseTriWalkable(in Vtx a, in Vtx b, in Vtx c, in NavBuildConfig config)
        {
            if (a.IsBlocked || b.IsBlocked || c.IsBlocked) return false;
            if (a.WaterY > a.Pos.Y || b.WaterY > b.Pos.Y || c.WaterY > c.Pos.Y) return false;
            if (a.IsRamp || b.IsRamp || c.IsRamp) return true;
            byte min = Math.Min(a.H, Math.Min(b.H, c.H));
            byte max = Math.Max(a.H, Math.Max(b.H, c.H));
            return (max - min) <= config.CliffHeightThreshold;
        }

        private static int[] ComputeClearanceCmField(bool[] cellWalkable)
        {
            int n = VertexChunk.ChunkSize * VertexChunk.ChunkSize;
            int[] dist = new int[n];
            var q = new Queue<int>(n);
            int stepCm = (int)MathF.Round(MathF.Min(HexCoordinates.HexWidth, HexCoordinates.RowSpacing) * 100f);
            if (stepCm < 1) stepCm = 1;

            for (int i = 0; i < n; i++)
            {
                if (cellWalkable[i])
                {
                    dist[i] = int.MaxValue;
                }
                else
                {
                    dist[i] = 0;
                    q.Enqueue(i);
                }
            }

            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int baseD = dist[cur];
                int x = cur % VertexChunk.ChunkSize;
                int y = cur / VertexChunk.ChunkSize;

                int nd = baseD + stepCm;

                if (x > 0) Relax(cur - 1, nd);
                if (x + 1 < VertexChunk.ChunkSize) Relax(cur + 1, nd);
                if (y > 0) Relax(cur - VertexChunk.ChunkSize, nd);
                if (y + 1 < VertexChunk.ChunkSize) Relax(cur + VertexChunk.ChunkSize, nd);
            }

            return dist;

            void Relax(int idx, int nd)
            {
                if (!cellWalkable[idx]) return;
                if (nd >= dist[idx]) return;
                dist[idx] = nd;
                q.Enqueue(idx);
            }
        }
    }
}
