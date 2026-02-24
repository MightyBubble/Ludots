using System;
using System.Collections.Generic;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Integer point for contour operations (grid coordinates).
    /// </summary>
    public readonly struct IntPoint : IEquatable<IntPoint>
    {
        public readonly int X;
        public readonly int Y;

        public IntPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(IntPoint other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is IntPoint other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(IntPoint left, IntPoint right) => left.Equals(right);
        public static bool operator !=(IntPoint left, IntPoint right) => !left.Equals(right);
    }

    /// <summary>
    /// A closed ring of integer points representing a contour boundary.
    /// </summary>
    public readonly struct IntRing
    {
        public readonly IntPoint[] Points;
        public readonly bool IsCCW;
        public readonly long SignedArea2;

        public IntRing(IntPoint[] points, bool isCCW, long signedArea2)
        {
            Points = points ?? throw new ArgumentNullException(nameof(points));
            IsCCW = isCCW;
            SignedArea2 = signedArea2;
        }

        public bool IsOuter => IsCCW;
        public bool IsHole => !IsCCW;
        public long Area2 => Math.Abs(SignedArea2);
    }

    /// <summary>
    /// Directed edge segment for contour assembly.
    /// </summary>
    internal readonly struct DirectedEdge : IEquatable<DirectedEdge>
    {
        public readonly IntPoint From;
        public readonly IntPoint To;

        public DirectedEdge(IntPoint from, IntPoint to)
        {
            From = from;
            To = to;
        }

        public bool Equals(DirectedEdge other) => From.Equals(other.From) && To.Equals(other.To);
        public override bool Equals(object obj) => obj is DirectedEdge other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(From, To);
    }

    /// <summary>
    /// Extracts closed boundary rings from a triangle walkability mask.
    /// Uses edge-segment collection and multi-adjacency ring assembly.
    /// </summary>
    public static class ContourExtractor
    {
        /// <summary>
        /// Extracts all boundary rings from the walkability mask.
        /// </summary>
        /// <param name="mask">Triangle-level walkability mask.</param>
        /// <param name="originCol">Global column origin of the tile.</param>
        /// <param name="originRow">Global row origin of the tile.</param>
        /// <returns>List of closed rings (may include outer boundaries and holes).</returns>
        public static List<IntRing> Extract(TriWalkMask mask, int originCol, int originRow)
        {
            // Step 1: Collect all boundary edges
            var boundaryEdges = CollectBoundaryEdges(mask, originCol, originRow);
            if (boundaryEdges.Count == 0)
                return new List<IntRing>();

            // Step 2: Build multi-adjacency map (from -> list of to)
            var adjacency = BuildAdjacency(boundaryEdges);

            // Step 3: Assemble closed rings
            return AssembleRings(boundaryEdges, adjacency);
        }

        /// <summary>
        /// Collects all directed boundary edges from walkable triangles.
        /// A boundary edge is where one side is walkable and the other is not (or out of bounds).
        /// </summary>
        private static List<DirectedEdge> CollectBoundaryEdges(TriWalkMask mask, int originCol, int originRow)
        {
            var edges = new List<DirectedEdge>(1024);
            int tileW = mask.TileWidth;
            int tileH = mask.TileHeight;

            for (int localR = 0; localR < tileH; localR++)
            {
                for (int localC = 0; localC < tileW; localC++)
                {
                    int globalR = originRow + localR;
                    bool isOddRow = (globalR & 1) == 1;

                    for (int triIdx = 0; triIdx < 2; triIdx++)
                    {
                        if (!mask.IsWalkable(localC, localR, triIdx))
                            continue;

                        // Get triangle vertex grid positions (relative to cell)
                        GetTriangleVertices(localC, localR, triIdx, isOddRow,
                            out var va, out var vb, out var vc);

                        // Check each edge of this triangle
                        // Edge 0: va -> vb
                        if (IsBoundaryEdge(mask, localC, localR, triIdx, 0, originCol, originRow))
                        {
                            // Emit CCW-wound edge (va -> vb for exterior boundary)
                            edges.Add(new DirectedEdge(va, vb));
                        }

                        // Edge 1: vb -> vc
                        if (IsBoundaryEdge(mask, localC, localR, triIdx, 1, originCol, originRow))
                        {
                            edges.Add(new DirectedEdge(vb, vc));
                        }

                        // Edge 2: vc -> va
                        if (IsBoundaryEdge(mask, localC, localR, triIdx, 2, originCol, originRow))
                        {
                            edges.Add(new DirectedEdge(vc, va));
                        }
                    }
                }
            }

            return edges;
        }

        /// <summary>
        /// Gets the grid coordinates of triangle vertices.
        /// </summary>
        private static void GetTriangleVertices(int localC, int localR, int triIdx, bool isOddRow,
            out IntPoint va, out IntPoint vb, out IntPoint vc)
        {
            // Grid vertex positions for cell at (localC, localR)
            var v00 = new IntPoint(localC, localR);
            var v10 = new IntPoint(localC + 1, localR);
            var v01 = new IntPoint(localC, localR + 1);
            var v11 = new IntPoint(localC + 1, localR + 1);

            if (!isOddRow)
            {
                if (triIdx == 0) { va = v00; vb = v10; vc = v01; }
                else { va = v10; vb = v11; vc = v01; }
            }
            else
            {
                if (triIdx == 0) { va = v00; vb = v10; vc = v11; }
                else { va = v00; vb = v11; vc = v01; }
            }
        }

        /// <summary>
        /// Checks if an edge of a triangle is a boundary edge.
        /// </summary>
        private static bool IsBoundaryEdge(TriWalkMask mask, int localC, int localR, int triIdx, int edgeIdx,
            int originCol, int originRow)
        {
            // Find the adjacent triangle across this edge
            if (!TryGetAdjacentTriangle(localC, localR, triIdx, edgeIdx, originCol, originRow, mask.TileWidth, mask.TileHeight,
                out int adjC, out int adjR, out int adjTri))
            {
                // Out of bounds = boundary
                return true;
            }

            // Check if adjacent triangle is walkable
            return !mask.IsWalkable(adjC, adjR, adjTri);
        }

        /// <summary>
        /// Finds the adjacent triangle across a given edge.
        /// Returns false if the adjacent triangle is outside tile bounds.
        /// </summary>
        private static bool TryGetAdjacentTriangle(int localC, int localR, int triIdx, int edgeIdx,
            int originCol, int originRow, int tileW, int tileH,
            out int adjC, out int adjR, out int adjTri)
        {
            adjC = adjR = adjTri = 0;
            int globalR = originRow + localR;
            bool isOddRow = (globalR & 1) == 1;

            // Determine which edge we're checking and find the adjacent triangle
            // Triangle layout varies by row parity
            // Edge indices: 0 = va->vb, 1 = vb->vc, 2 = vc->va

            if (!isOddRow)
            {
                // Even row triangles:
                // Tri0: (v00, v10, v01) - edges: 0=right, 1=diag, 2=left
                // Tri1: (v10, v11, v01) - edges: 0=right, 1=bottom, 2=diag
                if (triIdx == 0)
                {
                    switch (edgeIdx)
                    {
                        case 0: // v00->v10: shared with cell (c, r-1) if exists
                            if (localR == 0) return false;
                            adjC = localC; adjR = localR - 1; adjTri = 1;
                            break;
                        case 1: // v10->v01: shared with Tri1 of same cell
                            adjC = localC; adjR = localR; adjTri = 1;
                            break;
                        case 2: // v01->v00: shared with cell (c-1, r)
                            if (localC == 0) return false;
                            adjC = localC - 1; adjR = localR; adjTri = 1;
                            break;
                    }
                }
                else // triIdx == 1
                {
                    switch (edgeIdx)
                    {
                        case 0: // v10->v11: shared with cell (c+1, r)
                            if (localC + 1 >= tileW) return false;
                            adjC = localC + 1; adjR = localR; adjTri = 0;
                            break;
                        case 1: // v11->v01: shared with cell (c, r+1)
                            if (localR + 1 >= tileH) return false;
                            adjC = localC; adjR = localR + 1; adjTri = 0;
                            break;
                        case 2: // v01->v10: shared with Tri0 of same cell
                            adjC = localC; adjR = localR; adjTri = 0;
                            break;
                    }
                }
            }
            else
            {
                // Odd row triangles:
                // Tri0: (v00, v10, v11) - edges: 0=top, 1=right, 2=diag
                // Tri1: (v00, v11, v01) - edges: 0=diag, 1=bottom, 2=left
                if (triIdx == 0)
                {
                    switch (edgeIdx)
                    {
                        case 0: // v00->v10: shared with cell (c, r-1)
                            if (localR == 0) return false;
                            adjC = localC; adjR = localR - 1; adjTri = 1;
                            break;
                        case 1: // v10->v11: shared with cell (c+1, r)
                            if (localC + 1 >= tileW) return false;
                            adjC = localC + 1; adjR = localR; adjTri = 1;
                            break;
                        case 2: // v11->v00: shared with Tri1 of same cell
                            adjC = localC; adjR = localR; adjTri = 1;
                            break;
                    }
                }
                else // triIdx == 1
                {
                    switch (edgeIdx)
                    {
                        case 0: // v00->v11: shared with Tri0 of same cell
                            adjC = localC; adjR = localR; adjTri = 0;
                            break;
                        case 1: // v11->v01: shared with cell (c, r+1)
                            if (localR + 1 >= tileH) return false;
                            adjC = localC; adjR = localR + 1; adjTri = 0;
                            break;
                        case 2: // v01->v00: shared with cell (c-1, r)
                            if (localC == 0) return false;
                            adjC = localC - 1; adjR = localR; adjTri = 0;
                            break;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Builds multi-adjacency map from directed edges.
        /// </summary>
        private static Dictionary<IntPoint, List<IntPoint>> BuildAdjacency(List<DirectedEdge> edges)
        {
            var adj = new Dictionary<IntPoint, List<IntPoint>>(edges.Count);

            foreach (var e in edges)
            {
                if (!adj.TryGetValue(e.From, out var list))
                {
                    list = new List<IntPoint>(4);
                    adj[e.From] = list;
                }
                list.Add(e.To);
            }

            return adj;
        }

        /// <summary>
        /// Assembles closed rings from directed edges using adjacency traversal.
        /// </summary>
        private static List<IntRing> AssembleRings(List<DirectedEdge> allEdges, Dictionary<IntPoint, List<IntPoint>> adjacency)
        {
            var usedEdges = new HashSet<DirectedEdge>();
            var rings = new List<IntRing>();

            foreach (var startEdge in allEdges)
            {
                if (usedEdges.Contains(startEdge))
                    continue;

                var ring = TraceRing(startEdge, adjacency, usedEdges);
                if (ring != null && ring.Count >= 3)
                {
                    var points = ring.ToArray();
                    long signedArea2 = ComputeSignedArea2(points);
                    bool isCCW = signedArea2 > 0;
                    rings.Add(new IntRing(points, isCCW, signedArea2));
                }
            }

            return rings;
        }

        /// <summary>
        /// Traces a single ring starting from a given edge.
        /// </summary>
        private static List<IntPoint> TraceRing(DirectedEdge startEdge, Dictionary<IntPoint, List<IntPoint>> adjacency, HashSet<DirectedEdge> usedEdges)
        {
            var ring = new List<IntPoint>(64);
            var current = startEdge.From;
            var next = startEdge.To;
            ring.Add(current);
            usedEdges.Add(startEdge);

            int maxIterations = 10000; // Safety limit
            int iteration = 0;

            while (next != startEdge.From && iteration < maxIterations)
            {
                iteration++;
                ring.Add(next);

                if (!adjacency.TryGetValue(next, out var candidates) || candidates.Count == 0)
                {
                    // Dead end - incomplete ring
                    return null;
                }

                // Find the best next point (prefer continuing in same direction, use left-most turn rule)
                IntPoint? best = null;
                double bestAngle = double.MaxValue;
                var prev = current;

                foreach (var candidate in candidates)
                {
                    var edge = new DirectedEdge(next, candidate);
                    if (usedEdges.Contains(edge))
                        continue;

                    if (candidate == prev)
                        continue;

                    // Calculate turn angle (prefer smallest right turn for CCW winding)
                    double angle = ComputeTurnAngle(prev, next, candidate);
                    if (best == null || angle < bestAngle)
                    {
                        best = candidate;
                        bestAngle = angle;
                    }
                }

                if (best == null)
                {
                    // No valid continuation
                    return null;
                }

                usedEdges.Add(new DirectedEdge(next, best.Value));
                current = next;
                next = best.Value;
            }

            if (iteration >= maxIterations)
                return null;

            return ring;
        }

        /// <summary>
        /// Computes the turn angle from prev->current->next.
        /// Returns angle in [-PI, PI] where negative = left turn, positive = right turn.
        /// </summary>
        private static double ComputeTurnAngle(IntPoint prev, IntPoint current, IntPoint next)
        {
            double dx1 = current.X - prev.X;
            double dy1 = current.Y - prev.Y;
            double dx2 = next.X - current.X;
            double dy2 = next.Y - current.Y;

            double angle1 = Math.Atan2(dy1, dx1);
            double angle2 = Math.Atan2(dy2, dx2);

            double turn = angle2 - angle1;

            // Normalize to [-PI, PI]
            while (turn > Math.PI) turn -= 2 * Math.PI;
            while (turn < -Math.PI) turn += 2 * Math.PI;

            return turn;
        }

        /// <summary>
        /// Computes twice the signed area of a polygon using the shoelace formula.
        /// Positive = CCW, Negative = CW.
        /// </summary>
        public static long ComputeSignedArea2(IntPoint[] points)
        {
            if (points == null || points.Length < 3)
                return 0;

            long area2 = 0;
            int n = points.Length;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area2 += (long)points[i].X * points[j].Y;
                area2 -= (long)points[j].X * points[i].Y;
            }

            return area2;
        }
    }
}
