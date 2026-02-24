using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Result of triangulation containing vertices and triangle indices.
    /// </summary>
    public readonly struct TriMesh
    {
        /// <summary>Vertices in XZ plane (Y is up in world space).</summary>
        public readonly Vector2[] Vertices;

        /// <summary>Triangle indices (3 indices per triangle).</summary>
        public readonly int[] Triangles;

        public TriMesh(Vector2[] vertices, int[] triangles)
        {
            Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
            Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));
        }

        public int VertexCount => Vertices.Length;
        public int TriangleCount => Triangles.Length / 3;

        public static TriMesh Empty => new TriMesh(Array.Empty<Vector2>(), Array.Empty<int>());
    }

    /// <summary>
    /// Interface for polygon triangulation implementations.
    /// </summary>
    public interface ITriangulator
    {
        /// <summary>
        /// Triangulates a polygon with optional holes.
        /// </summary>
        /// <param name="polygon">The polygon to triangulate.</param>
        /// <param name="mesh">The resulting triangle mesh.</param>
        /// <param name="error">Error message if triangulation fails.</param>
        /// <returns>True if triangulation succeeded.</returns>
        bool TryTriangulate(Polygon polygon, out TriMesh mesh, out string error);

        /// <summary>
        /// Triangulates multiple polygons into a single mesh.
        /// </summary>
        bool TryTriangulate(ValidPolygonSet polygonSet, out TriMesh mesh, out string error);
    }

    /// <summary>
    /// Factory for creating triangulator instances.
    /// </summary>
    public static class TriangulatorFactory
    {
        /// <summary>
        /// Creates the default triangulator (Constrained Delaunay Triangulation).
        /// </summary>
        public static ITriangulator CreateDefault()
        {
            return new CdtTriangulator();
        }

        /// <summary>
        /// Creates the legacy ear-clipping triangulator (kept for reference/testing).
        /// </summary>
        public static ITriangulator CreateEarClipping()
        {
            return new EarClippingTriangulator();
        }
    }

    /// <summary>
    /// Simple ear-clipping triangulator for convex and simple concave polygons.
    /// Falls back gracefully for complex cases.
    /// </summary>
    public sealed class EarClippingTriangulator : ITriangulator
    {
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
                // Convert IntPoints to Vector2
                var outerPoints = ConvertToVector2(polygon.Outer);
                
                // Ensure CCW winding for outer
                if (!IsCounterClockwise(outerPoints))
                {
                    Array.Reverse(outerPoints);
                }

                // For polygons without holes, use simple ear clipping
                if (polygon.Holes == null || polygon.Holes.Length == 0)
                {
                    return TriangulateSimplePolygon(outerPoints, out mesh, out error);
                }

                // For polygons with holes, merge holes into outer boundary
                var mergedPolygon = MergeHolesIntoOuter(outerPoints, polygon.Holes);
                return TriangulateSimplePolygon(mergedPolygon, out mesh, out error);
            }
            catch (Exception ex)
            {
                error = $"Triangulation failed: {ex.Message}";
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

                // Offset triangle indices for merged mesh
                int baseIndex = allVertices.Count;
                allVertices.AddRange(polyMesh.Vertices);

                foreach (var idx in polyMesh.Triangles)
                {
                    allTriangles.Add(idx + baseIndex);
                }
            }

            mesh = new TriMesh(allVertices.ToArray(), allTriangles.ToArray());
            return true;
        }

        private static Vector2[] ConvertToVector2(IntPoint[] points)
        {
            var result = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                result[i] = new Vector2(points[i].X, points[i].Y);
            }
            return result;
        }

        private static bool IsCounterClockwise(Vector2[] points)
        {
            float sum = 0;
            int n = points.Length;
            for (int i = 0; i < n; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % n];
                sum += (p2.X - p1.X) * (p2.Y + p1.Y);
            }
            return sum < 0;
        }

        private bool TriangulateSimplePolygon(Vector2[] points, out TriMesh mesh, out string error)
        {
            mesh = TriMesh.Empty;
            error = null;

            int n = points.Length;
            if (n < 3)
            {
                error = "Need at least 3 points.";
                return false;
            }

            if (n == 3)
            {
                mesh = new TriMesh(points, new[] { 0, 1, 2 });
                return true;
            }

            // Create vertex index list
            var indices = new List<int>(n);
            for (int i = 0; i < n; i++)
                indices.Add(i);

            var triangles = new List<int>();
            int maxIterations = n * n; // Safety limit
            int iterations = 0;

            while (indices.Count > 3 && iterations < maxIterations)
            {
                iterations++;
                bool earFound = false;

                for (int i = 0; i < indices.Count; i++)
                {
                    int prev = (i + indices.Count - 1) % indices.Count;
                    int next = (i + 1) % indices.Count;

                    int iPrev = indices[prev];
                    int iCur = indices[i];
                    int iNext = indices[next];

                    var pPrev = points[iPrev];
                    var pCur = points[iCur];
                    var pNext = points[iNext];

                    // Check if this is a convex vertex (ear candidate)
                    if (!IsConvexVertex(pPrev, pCur, pNext))
                        continue;

                    // Check if any other vertex is inside this triangle
                    bool isEar = true;
                    for (int j = 0; j < indices.Count; j++)
                    {
                        if (j == prev || j == i || j == next)
                            continue;

                        if (PointInTriangle(points[indices[j]], pPrev, pCur, pNext))
                        {
                            isEar = false;
                            break;
                        }
                    }

                    if (isEar)
                    {
                        // Add triangle (CCW order)
                        triangles.Add(iPrev);
                        triangles.Add(iCur);
                        triangles.Add(iNext);

                        // Remove the ear vertex
                        indices.RemoveAt(i);
                        earFound = true;
                        break;
                    }
                }

                if (!earFound)
                {
                    // No ear found - polygon might be malformed
                    // Try to continue with remaining vertices
                    if (indices.Count >= 3)
                    {
                        // Force add remaining as triangle(s)
                        for (int i = 1; i < indices.Count - 1; i++)
                        {
                            triangles.Add(indices[0]);
                            triangles.Add(indices[i]);
                            triangles.Add(indices[i + 1]);
                        }
                    }
                    break;
                }
            }

            // Add final triangle
            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }

            if (triangles.Count == 0)
            {
                error = "Failed to generate any triangles.";
                return false;
            }

            mesh = new TriMesh(points, triangles.ToArray());
            return true;
        }

        private static bool IsConvexVertex(Vector2 prev, Vector2 cur, Vector2 next)
        {
            // Cross product of (cur - prev) and (next - cur)
            float cross = (cur.X - prev.X) * (next.Y - cur.Y) - (cur.Y - prev.Y) * (next.X - cur.X);
            return cross > 0; // CCW = convex
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);

            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        }

        /// <summary>
        /// Merges holes into the outer boundary by creating bridge edges.
        /// </summary>
        private Vector2[] MergeHolesIntoOuter(Vector2[] outer, IntPoint[][] holes)
        {
            var result = new List<Vector2>(outer);

            foreach (var hole in holes)
            {
                if (hole == null || hole.Length < 3)
                    continue;

                var holePoints = ConvertToVector2(hole);

                // Ensure CW winding for holes
                if (IsCounterClockwise(holePoints))
                {
                    Array.Reverse(holePoints);
                }

                // Find the rightmost vertex in the hole
                int rightmostHole = 0;
                for (int i = 1; i < holePoints.Length; i++)
                {
                    if (holePoints[i].X > holePoints[rightmostHole].X)
                        rightmostHole = i;
                }

                var holeVertex = holePoints[rightmostHole];

                // Find the closest visible vertex in the outer boundary
                int closestOuter = FindClosestVisibleVertex(result.ToArray(), holeVertex);
                if (closestOuter < 0)
                    closestOuter = 0; // Fallback

                // Insert hole into outer boundary with bridge edges
                var merged = new List<Vector2>();

                // Add outer vertices up to and including the bridge point
                for (int i = 0; i <= closestOuter; i++)
                    merged.Add(result[i]);

                // Add hole vertices starting from rightmost, wrapping around
                for (int i = 0; i <= holePoints.Length; i++)
                {
                    int idx = (rightmostHole + i) % holePoints.Length;
                    merged.Add(holePoints[idx]);
                }

                // Bridge back to outer
                merged.Add(result[closestOuter]);

                // Add remaining outer vertices
                for (int i = closestOuter + 1; i < result.Count; i++)
                    merged.Add(result[i]);

                result = merged;
            }

            return result.ToArray();
        }

        private int FindClosestVisibleVertex(Vector2[] polygon, Vector2 point)
        {
            int closest = 0;
            float minDist = float.MaxValue;

            for (int i = 0; i < polygon.Length; i++)
            {
                float dist = Vector2.DistanceSquared(polygon[i], point);
                if (dist < minDist)
                {
                    // Simple visibility check - the point should be to the left of the outer boundary
                    if (polygon[i].X <= point.X)
                    {
                        minDist = dist;
                        closest = i;
                    }
                }
            }

            // Fallback to absolute closest if no visible vertex found
            if (minDist == float.MaxValue)
            {
                for (int i = 0; i < polygon.Length; i++)
                {
                    float dist = Vector2.DistanceSquared(polygon[i], point);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closest = i;
                    }
                }
            }

            return closest;
        }
    }
}
