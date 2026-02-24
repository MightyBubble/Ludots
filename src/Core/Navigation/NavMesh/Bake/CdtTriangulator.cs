using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Constrained Delaunay Triangulation using Bowyer-Watson incremental insertion
    /// with constraint-edge recovery via edge flipping and flood-fill exterior removal.
    ///
    /// Designed for the hex-grid NavMesh bake pipeline where input polygons are
    /// integer-coordinate "staircase" shapes from ContourExtractor.
    ///
    /// Workflow:
    ///   1. Create a super-triangle that encloses all input points.
    ///   2. Insert each boundary vertex using Bowyer-Watson (maintain Delaunay property).
    ///   3. Recover constraint edges that are missing from the triangulation (edge flipping).
    ///   4. Flood-fill from the super-triangle to mark exterior triangles.
    ///   5. Remove exterior + super-triangle triangles.
    ///   6. Return the interior mesh.
    /// </summary>
    public sealed class CdtTriangulator : ITriangulator
    {
        // ───────────────── Internal triangle representation ─────────────────

        private struct Tri
        {
            public int A, B, C; // vertex indices (CCW)
            public bool Alive;

            public Tri(int a, int b, int c)
            {
                A = a; B = b; C = c;
                Alive = true;
            }
        }

        // ───────────────── ITriangulator interface ─────────────────

        public bool TryTriangulate(Polygon polygon, out TriMesh mesh, out string error)
        {
            mesh = TriMesh.Empty;
            error = null;

            if (polygon.Outer == null || polygon.Outer.Length < 3)
            {
                error = "Polygon outer boundary must have at least 3 points.";
                return false;
            }

            try
            {
                mesh = TriangulatePolygon(polygon);
                if (mesh.TriangleCount == 0)
                {
                    error = "CDT produced no interior triangles.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"CDT failed: {ex.Message}";
                return false;
            }
        }

        public bool TryTriangulate(ValidPolygonSet polygonSet, out TriMesh mesh, out string error)
        {
            mesh = TriMesh.Empty;
            error = null;

            if (polygonSet.Polygons == null || polygonSet.Polygons.Length == 0)
            {
                error = "No polygons to triangulate.";
                return false;
            }

            var allVertices = new List<Vector2>();
            var allTriangles = new List<int>();

            foreach (var polygon in polygonSet.Polygons)
            {
                if (!TryTriangulate(polygon, out var polyMesh, out var polyError))
                {
                    error = polyError;
                    return false;
                }

                int baseIndex = allVertices.Count;
                allVertices.AddRange(polyMesh.Vertices);
                foreach (var idx in polyMesh.Triangles)
                    allTriangles.Add(idx + baseIndex);
            }

            mesh = new TriMesh(allVertices.ToArray(), allTriangles.ToArray());
            return true;
        }

        // ───────────────── Core CDT algorithm ─────────────────

        private TriMesh TriangulatePolygon(Polygon polygon)
        {
            // Collect all unique points from outer + holes
            var points = new List<Vector2>();
            var constraintEdges = new List<(int a, int b)>();

            // Add outer boundary
            int outerStart = points.Count;
            foreach (var p in polygon.Outer)
                points.Add(new Vector2(p.X, p.Y));

            // Ensure CCW winding for outer boundary
            if (SignedArea2(points, outerStart, polygon.Outer.Length) < 0)
                ReverseRange(points, outerStart, polygon.Outer.Length);

            for (int i = 0; i < polygon.Outer.Length; i++)
            {
                int a = outerStart + i;
                int b = outerStart + (i + 1) % polygon.Outer.Length;
                constraintEdges.Add((a, b));
            }

            // Add holes
            if (polygon.Holes != null)
            {
                foreach (var hole in polygon.Holes)
                {
                    if (hole == null || hole.Length < 3) continue;

                    int holeStart = points.Count;
                    foreach (var p in hole)
                        points.Add(new Vector2(p.X, p.Y));

                    // Ensure CW winding for holes
                    if (SignedArea2(points, holeStart, hole.Length) > 0)
                        ReverseRange(points, holeStart, hole.Length);

                    for (int i = 0; i < hole.Length; i++)
                    {
                        int a = holeStart + i;
                        int b = holeStart + (i + 1) % hole.Length;
                        constraintEdges.Add((a, b));
                    }
                }
            }

            int inputCount = points.Count;
            if (inputCount < 3)
                return TriMesh.Empty;

            // Step 1: Create super-triangle
            CreateSuperTriangle(points, out int stA, out int stB, out int stC);

            var tris = new List<Tri> { new Tri(stA, stB, stC) };

            // Step 2: Insert points using Bowyer-Watson
            for (int i = 0; i < inputCount; i++)
            {
                InsertPoint(points, tris, i);
            }

            // Step 3: Recover constraint edges
            foreach (var (ca, cb) in constraintEdges)
            {
                RecoverConstraintEdge(points, tris, ca, cb);
            }

            // Step 4: Mark exterior triangles via flood-fill
            var constraintSet = new HashSet<long>();
            foreach (var (ca, cb) in constraintEdges)
                constraintSet.Add(EdgeKey(ca, cb));

            RemoveExteriorTriangles(points, tris, constraintSet, stA, stB, stC);

            // Step 5: Remove super-triangle vertices, compact, and return
            return BuildResult(points, tris, inputCount);
        }

        // ───────────────── Bowyer-Watson point insertion ─────────────────

        private static void InsertPoint(List<Vector2> points, List<Tri> tris, int pi)
        {
            var p = points[pi];

            // Find all triangles whose circumcircle contains p
            var badIndices = new List<int>();
            for (int t = 0; t < tris.Count; t++)
            {
                if (!tris[t].Alive) continue;
                var tri = tris[t];
                if (InCircumcircle(points[tri.A], points[tri.B], points[tri.C], p))
                    badIndices.Add(t);
            }

            if (badIndices.Count == 0)
            {
                // Point is exactly on a circumcircle edge or outside all circles.
                // Find the containing triangle and split it.
                for (int t = 0; t < tris.Count; t++)
                {
                    if (!tris[t].Alive) continue;
                    var tri = tris[t];
                    if (PointInTriangle(p, points[tri.A], points[tri.B], points[tri.C]))
                    {
                        badIndices.Add(t);
                        break;
                    }
                }
                if (badIndices.Count == 0) return; // degenerate - skip
            }

            // Find the boundary polygon of the "bad" region.
            // An edge is on the boundary if it's used by exactly one bad triangle.
            var edgeCount = new Dictionary<long, (int v1, int v2, int count)>();

            foreach (int ti in badIndices)
            {
                var tri = tris[ti];
                CountEdge(edgeCount, tri.A, tri.B);
                CountEdge(edgeCount, tri.B, tri.C);
                CountEdge(edgeCount, tri.C, tri.A);
            }

            // Boundary edges: count == 1
            var boundary = new List<(int v1, int v2)>();
            foreach (var kv in edgeCount)
            {
                if (kv.Value.count == 1)
                    boundary.Add((kv.Value.v1, kv.Value.v2));
            }

            // Kill bad triangles
            foreach (int ti in badIndices)
            {
                var tri = tris[ti];
                tri.Alive = false;
                tris[ti] = tri;
            }

            // Create new triangles connecting p to each boundary edge
            foreach (var (v1, v2) in boundary)
            {
                tris.Add(new Tri(pi, v1, v2));
            }
        }

        private static void CountEdge(Dictionary<long, (int v1, int v2, int count)> map, int a, int b)
        {
            long key = EdgeKey(a, b);
            if (map.TryGetValue(key, out var val))
                map[key] = (val.v1, val.v2, val.count + 1);
            else
                map[key] = (a, b, 1);
        }

        // ───────────────── Constraint edge recovery ─────────────────

        private static void RecoverConstraintEdge(List<Vector2> points, List<Tri> tris, int ca, int cb)
        {
            // Check if constraint edge already exists
            if (FindEdgeTriangle(tris, ca, cb) >= 0)
                return;

            // Find triangles crossed by the constraint edge and flip until recovered
            int maxAttempts = tris.Count * 4;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (FindEdgeTriangle(tris, ca, cb) >= 0)
                    return; // recovered

                // Find a crossing edge and flip it
                bool flipped = false;
                for (int t = 0; t < tris.Count; t++)
                {
                    if (!tris[t].Alive) continue;
                    var tri = tris[t];

                    // Check each edge of this triangle
                    if (TryFlipCrossingEdge(points, tris, t, tri.A, tri.B, ca, cb)) { flipped = true; break; }
                    if (TryFlipCrossingEdge(points, tris, t, tri.B, tri.C, ca, cb)) { flipped = true; break; }
                    if (TryFlipCrossingEdge(points, tris, t, tri.C, tri.A, ca, cb)) { flipped = true; break; }
                }

                if (!flipped) break; // can't make progress
            }
        }

        private static bool TryFlipCrossingEdge(
            List<Vector2> points, List<Tri> tris,
            int triIdx, int ea, int eb, int ca, int cb)
        {
            // Skip edges that share a vertex with the constraint
            if (ea == ca || ea == cb || eb == ca || eb == cb)
                return false;

            // Check if edge (ea, eb) intersects constraint (ca, cb)
            if (!EdgesIntersectProperly(points[ea], points[eb], points[ca], points[cb]))
                return false;

            // Find the triangle on the other side of edge (ea, eb)
            int otherTri = FindAdjacentTriangle(tris, triIdx, ea, eb);
            if (otherTri < 0) return false;

            // Get the opposite vertices
            int ec = GetThirdVertex(tris[triIdx], ea, eb);
            int ed = GetThirdVertex(tris[otherTri], ea, eb);

            // Check if the quadrilateral (ec, ea, ed, eb) is convex
            if (!IsConvexQuad(points[ec], points[ea], points[ed], points[eb]))
                return false;

            // Flip: replace (ea,eb) diagonal with (ec,ed) diagonal
            var t1 = tris[triIdx]; t1.Alive = false; tris[triIdx] = t1;
            var t2 = tris[otherTri]; t2.Alive = false; tris[otherTri] = t2;

            tris.Add(new Tri(ec, ea, ed));
            tris.Add(new Tri(ec, ed, eb));

            return true;
        }

        // ───────────────── Exterior triangle removal ─────────────────

        private static void RemoveExteriorTriangles(
            List<Vector2> points, List<Tri> tris,
            HashSet<long> constraintEdges,
            int stA, int stB, int stC)
        {
            // Build adjacency: edge → list of alive triangle indices
            var edgeToTris = new Dictionary<long, List<int>>();
            for (int t = 0; t < tris.Count; t++)
            {
                if (!tris[t].Alive) continue;
                var tri = tris[t];
                AddEdgeTri(edgeToTris, tri.A, tri.B, t);
                AddEdgeTri(edgeToTris, tri.B, tri.C, t);
                AddEdgeTri(edgeToTris, tri.C, tri.A, t);
            }

            // First: remove any triangle that uses a super-triangle vertex
            var seedExterior = new HashSet<int>();
            for (int t = 0; t < tris.Count; t++)
            {
                if (!tris[t].Alive) continue;
                var tri = tris[t];
                if (tri.A >= stA || tri.B >= stA || tri.C >= stA)
                {
                    seedExterior.Add(t);
                }
            }

            // Flood-fill: from seed triangles, cross non-constraint edges to find all exterior
            var exterior = new HashSet<int>(seedExterior);
            var queue = new Queue<int>(seedExterior);

            while (queue.Count > 0)
            {
                int ti = queue.Dequeue();
                var tri = tris[ti];
                FloodEdge(tri.A, tri.B);
                FloodEdge(tri.B, tri.C);
                FloodEdge(tri.C, tri.A);

                void FloodEdge(int v1, int v2)
                {
                    // Don't cross constraint edges
                    if (constraintEdges.Contains(EdgeKey(v1, v2)))
                        return;

                    long ek = EdgeKey(v1, v2);
                    if (!edgeToTris.TryGetValue(ek, out var neighbors)) return;
                    foreach (int nt in neighbors)
                    {
                        if (nt != ti && tris[nt].Alive && exterior.Add(nt))
                            queue.Enqueue(nt);
                    }
                }
            }

            // Kill all exterior triangles
            foreach (int ti in exterior)
            {
                var tri = tris[ti];
                tri.Alive = false;
                tris[ti] = tri;
            }
        }

        private static void AddEdgeTri(Dictionary<long, List<int>> map, int a, int b, int triIdx)
        {
            long key = EdgeKey(a, b);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                map[key] = list;
            }
            list.Add(triIdx);
        }

        // ───────────────── Result building ─────────────────

        private static TriMesh BuildResult(List<Vector2> points, List<Tri> tris, int inputVertexCount)
        {
            // Remap vertex indices (only keep input vertices, not super-triangle vertices)
            var usedVertices = new HashSet<int>();
            foreach (var tri in tris)
            {
                if (!tri.Alive) continue;
                usedVertices.Add(tri.A);
                usedVertices.Add(tri.B);
                usedVertices.Add(tri.C);
            }

            var vertexRemap = new int[points.Count];
            Array.Fill(vertexRemap, -1);
            var outVertices = new List<Vector2>();

            foreach (int vi in usedVertices)
            {
                if (vi >= inputVertexCount) continue; // super-triangle vertex, skip
                vertexRemap[vi] = outVertices.Count;
                outVertices.Add(points[vi]);
            }

            var outTriangles = new List<int>();
            foreach (var tri in tris)
            {
                if (!tri.Alive) continue;
                int a = vertexRemap[tri.A];
                int b = vertexRemap[tri.B];
                int c = vertexRemap[tri.C];
                if (a < 0 || b < 0 || c < 0) continue; // references super vertex
                outTriangles.Add(a);
                outTriangles.Add(b);
                outTriangles.Add(c);
            }

            if (outTriangles.Count == 0)
                return TriMesh.Empty;

            return new TriMesh(outVertices.ToArray(), outTriangles.ToArray());
        }

        // ───────────────── Geometry helpers ─────────────────

        private static void CreateSuperTriangle(List<Vector2> points, out int iA, out int iB, out int iC)
        {
            // Find bounding box of all points
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            float dx = maxX - minX;
            float dy = maxY - minY;
            float dmax = MathF.Max(dx, dy);
            float midX = (minX + maxX) * 0.5f;
            float midY = (minY + maxY) * 0.5f;

            // Super-triangle vertices far enough to contain all points
            float margin = dmax * 20f;
            iA = points.Count;
            points.Add(new Vector2(midX - margin, midY - margin));
            iB = points.Count;
            points.Add(new Vector2(midX + margin, midY - margin));
            iC = points.Count;
            points.Add(new Vector2(midX, midY + margin));
        }

        /// <summary>
        /// Returns true if point p is inside the circumcircle of triangle (a, b, c).
        /// Uses the standard determinant test (exact for our integer-coordinate inputs).
        /// </summary>
        private static bool InCircumcircle(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            // The point is inside the circumcircle if the determinant is positive
            // (assuming CCW triangle winding).
            double ax = a.X - p.X, ay = a.Y - p.Y;
            double bx = b.X - p.X, by = b.Y - p.Y;
            double cx = c.X - p.X, cy = c.Y - p.Y;

            double det =
                (ax * ax + ay * ay) * (bx * cy - cx * by) -
                (bx * bx + by * by) * (ax * cy - cx * ay) +
                (cx * cx + cy * cy) * (ax * by - bx * ay);

            // For CCW triangles, det > 0 means p is inside circumcircle
            // Handle near-zero winding by checking triangle orientation
            double triArea2 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (triArea2 < 0) det = -det; // CW triangle → flip sign

            return det > 0;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            double d1 = Cross2D(p, a, b);
            double d2 = Cross2D(p, b, c);
            double d3 = Cross2D(p, c, a);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        private static double Cross2D(Vector2 o, Vector2 a, Vector2 b)
        {
            return (double)(a.X - o.X) * (b.Y - o.Y) - (double)(a.Y - o.Y) * (b.X - o.X);
        }

        /// <summary>
        /// Tests proper intersection of segments (a1,a2) and (b1,b2).
        /// Returns true only if segments cross each other (not just touch at endpoints).
        /// </summary>
        private static bool EdgesIntersectProperly(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            double d1 = Cross2D(b1, b2, a1);
            double d2 = Cross2D(b1, b2, a2);
            double d3 = Cross2D(a1, a2, b1);
            double d4 = Cross2D(a1, a2, b2);

            // Proper crossing: the endpoints straddle each other
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;

            return false;
        }

        private static bool IsConvexQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            // Quad vertices in order: a, b, c, d
            // Check that all cross products have the same sign
            double c1 = Cross2D(a, b, c);
            double c2 = Cross2D(b, c, d);
            double c3 = Cross2D(c, d, a);
            double c4 = Cross2D(d, a, b);
            bool allPos = c1 > 0 && c2 > 0 && c3 > 0 && c4 > 0;
            bool allNeg = c1 < 0 && c2 < 0 && c3 < 0 && c4 < 0;
            return allPos || allNeg;
        }

        private static long SignedArea2(List<Vector2> points, int start, int count)
        {
            long area2 = 0;
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                var pi = points[start + i];
                var pj = points[start + j];
                area2 += (long)(pi.X * pj.Y) - (long)(pj.X * pi.Y);
            }
            return area2;
        }

        private static void ReverseRange(List<Vector2> list, int start, int count)
        {
            int end = start + count - 1;
            while (start < end)
            {
                var tmp = list[start];
                list[start] = list[end];
                list[end] = tmp;
                start++;
                end--;
            }
        }

        /// <summary>Canonical undirected edge key (order-independent).</summary>
        private static long EdgeKey(int a, int b)
        {
            if (a > b) { int t = a; a = b; b = t; }
            return ((long)a << 32) | (uint)b;
        }

        /// <summary>Finds any alive triangle containing edge (a, b). Returns -1 if none.</summary>
        private static int FindEdgeTriangle(List<Tri> tris, int a, int b)
        {
            for (int t = 0; t < tris.Count; t++)
            {
                if (!tris[t].Alive) continue;
                var tri = tris[t];
                if (HasEdge(tri, a, b)) return t;
            }
            return -1;
        }

        private static bool HasEdge(in Tri tri, int a, int b)
        {
            return (tri.A == a && tri.B == b) || (tri.B == a && tri.C == b) || (tri.C == a && tri.A == b) ||
                   (tri.A == b && tri.B == a) || (tri.B == b && tri.C == a) || (tri.C == b && tri.A == a);
        }

        /// <summary>Finds the triangle adjacent to triIdx across edge (ea, eb). Returns -1 if boundary.</summary>
        private static int FindAdjacentTriangle(List<Tri> tris, int triIdx, int ea, int eb)
        {
            for (int t = 0; t < tris.Count; t++)
            {
                if (t == triIdx || !tris[t].Alive) continue;
                if (HasEdge(tris[t], ea, eb)) return t;
            }
            return -1;
        }

        /// <summary>Returns the vertex of the triangle that is neither ea nor eb.</summary>
        private static int GetThirdVertex(in Tri tri, int ea, int eb)
        {
            if (tri.A != ea && tri.A != eb) return tri.A;
            if (tri.B != ea && tri.B != eb) return tri.B;
            return tri.C;
        }

        private static double Cross2D(double ax, double ay, double bx, double by)
        {
            return ax * by - ay * bx;
        }
    }
}
