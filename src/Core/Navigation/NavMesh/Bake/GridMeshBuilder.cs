using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Builds a TriMesh directly from the grid-based WalkMask, bypassing
    /// the contour → polygon → triangulate pipeline.
    ///
    /// For a hex grid, the base topology (2 triangles per cell with alternating
    /// diagonal direction per row parity) already produces well-shaped, near-Delaunay
    /// triangles. Re-triangulation via ear-clipping or CDT is unnecessary and
    /// introduces long-skinny triangle artifacts.
    ///
    /// This builder:
    ///   1. Iterates all walkable triangles from the mask.
    ///   2. Maps grid-vertex positions to a deduplicated vertex array.
    ///   3. Outputs a TriMesh whose vertices are in tile-local grid coordinates
    ///      (matching what ConvertTriMeshToNavTile expects).
    /// </summary>
    public static class GridMeshBuilder
    {
        /// <summary>
        /// Builds a triangle mesh from all walkable triangles in the mask.
        /// Vertices are tile-local integer grid coordinates stored as Vector2(col, row).
        /// </summary>
        /// <param name="mask">Triangle-level walkability mask for one tile.</param>
        /// <param name="originCol">Global column of tile origin (for row-parity calculation).</param>
        /// <param name="originRow">Global row of tile origin (for row-parity calculation).</param>
        /// <param name="mesh">Resulting triangle mesh.</param>
        /// <param name="error">Error message if building fails.</param>
        /// <returns>True on success.</returns>
        public static bool TryBuild(
            in TriWalkMask mask,
            int originCol,
            int originRow,
            out TriMesh mesh,
            out string error)
        {
            mesh = TriMesh.Empty;
            error = null;

            int tileW = mask.TileWidth;
            int tileH = mask.TileHeight;

            // Vertex deduplication: grid point (localCol, localRow) → vertex index.
            // Max possible unique vertices = (tileW+1) * (tileH+1) = 65*65 = 4225.
            // Using a flat array indexed by (row * stride + col) is faster than a dictionary.
            int stride = tileW + 1;
            int maxVertices = stride * (tileH + 1);
            var vertexIndex = new int[maxVertices];
            Array.Fill(vertexIndex, -1);

            var vertices = new List<Vector2>(maxVertices);
            var triangles = new List<int>(mask.WalkableTriangleCount * 3);

            for (int localR = 0; localR < tileH; localR++)
            {
                int globalR = originRow + localR;
                bool isOddRow = (globalR & 1) == 1;

                for (int localC = 0; localC < tileW; localC++)
                {
                    for (int triIdx = 0; triIdx < 2; triIdx++)
                    {
                        if (!mask.IsWalkable(localC, localR, triIdx))
                            continue;

                        // Get the three vertex grid positions for this triangle.
                        GetTriangleVertexPositions(localC, localR, triIdx, isOddRow,
                            out int c0, out int r0,
                            out int c1, out int r1,
                            out int c2, out int r2);

                        int i0 = GetOrAddVertex(vertexIndex, vertices, stride, c0, r0);
                        int i1 = GetOrAddVertex(vertexIndex, vertices, stride, c1, r1);
                        int i2 = GetOrAddVertex(vertexIndex, vertices, stride, c2, r2);

                        triangles.Add(i0);
                        triangles.Add(i1);
                        triangles.Add(i2);
                    }
                }
            }

            if (triangles.Count == 0)
            {
                error = "No walkable triangles to build mesh from.";
                return false;
            }

            mesh = new TriMesh(vertices.ToArray(), triangles.ToArray());
            return true;
        }

        /// <summary>
        /// Returns the three vertex grid positions (col, row) for a triangle
        /// in the hex-staggered dual grid.
        ///
        /// Layout (consistent with WalkMaskBuilder and ContourExtractor):
        ///   Even row: Tri0 = (v00, v10, v01)  Tri1 = (v10, v11, v01)  diagonal = v10↔v01
        ///   Odd  row: Tri0 = (v00, v10, v11)  Tri1 = (v00, v11, v01)  diagonal = v00↔v11
        ///
        /// where vXY = (localC + X, localR + Y).
        /// </summary>
        private static void GetTriangleVertexPositions(
            int localC, int localR, int triIdx, bool isOddRow,
            out int c0, out int r0,
            out int c1, out int r1,
            out int c2, out int r2)
        {
            // v00
            int v00c = localC, v00r = localR;
            // v10
            int v10c = localC + 1, v10r = localR;
            // v01
            int v01c = localC, v01r = localR + 1;
            // v11
            int v11c = localC + 1, v11r = localR + 1;

            if (!isOddRow)
            {
                if (triIdx == 0) { c0 = v00c; r0 = v00r; c1 = v10c; r1 = v10r; c2 = v01c; r2 = v01r; }
                else             { c0 = v10c; r0 = v10r; c1 = v11c; r1 = v11r; c2 = v01c; r2 = v01r; }
            }
            else
            {
                if (triIdx == 0) { c0 = v00c; r0 = v00r; c1 = v10c; r1 = v10r; c2 = v11c; r2 = v11r; }
                else             { c0 = v00c; r0 = v00r; c1 = v11c; r1 = v11r; c2 = v01c; r2 = v01r; }
            }
        }

        /// <summary>
        /// Gets or creates a vertex index for the grid point (col, row).
        /// </summary>
        private static int GetOrAddVertex(int[] indexMap, List<Vector2> vertices, int stride, int col, int row)
        {
            int flatIdx = row * stride + col;
            int idx = indexMap[flatIdx];
            if (idx >= 0)
                return idx;

            idx = vertices.Count;
            vertices.Add(new Vector2(col, row));
            indexMap[flatIdx] = idx;
            return idx;
        }
    }
}
