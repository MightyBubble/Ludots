using System;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Triangle-level walkability mask for a tile.
    /// Each cell has 2 triangles indexed as [cellIndex * 2 + triIndex].
    /// </summary>
    public readonly struct TriWalkMask
    {
        public readonly int TileWidth;
        public readonly int TileHeight;
        public readonly bool[] Walkable;

        public TriWalkMask(int tileWidth, int tileHeight, bool[] walkable)
        {
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            Walkable = walkable ?? throw new ArgumentNullException(nameof(walkable));
        }

        public bool IsWalkable(int localCol, int localRow, int triIndex)
        {
            if ((uint)localCol >= (uint)TileWidth || (uint)localRow >= (uint)TileHeight || (uint)triIndex >= 2)
                return false;
            int idx = (localRow * TileWidth + localCol) * 2 + triIndex;
            return Walkable[idx];
        }

        public int WalkableTriangleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Walkable.Length; i++)
                    if (Walkable[i]) count++;
                return count;
            }
        }
    }

    /// <summary>
    /// Vertex data extracted from VertexMap for walkability evaluation.
    /// </summary>
    internal readonly struct WalkVertex
    {
        public readonly int Col;
        public readonly int Row;
        public readonly byte Height;
        public readonly byte WaterHeight;
        public readonly bool IsRamp;
        public readonly bool IsBlocked;

        public WalkVertex(int col, int row, byte height, byte waterHeight, bool isRamp, bool isBlocked)
        {
            Col = col;
            Row = row;
            Height = height;
            WaterHeight = waterHeight;
            IsRamp = isRamp;
            IsBlocked = isBlocked;
        }
    }

    /// <summary>
    /// Builds triangle-level walkability masks from VertexMap data.
    /// Integrates cliff-straightening marks to produce clean cliff boundaries
    /// instead of jagged diagonals caused by hex-row staggering.
    /// </summary>
    public static class WalkMaskBuilder
    {
        /// <summary>
        /// Builds a TriWalkMask for the specified tile.
        /// </summary>
        /// <param name="map">The vertex map containing terrain data.</param>
        /// <param name="chunkX">Tile X coordinate (chunk index).</param>
        /// <param name="chunkY">Tile Y coordinate (chunk index).</param>
        /// <param name="config">Build configuration with walkability thresholds.</param>
        /// <returns>A TriWalkMask containing walkability for each triangle.</returns>
        public static TriWalkMask Build(VertexMap map, int chunkX, int chunkY, in NavBuildConfig config)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));

            int tileWidth = VertexChunk.ChunkSize;
            int tileHeight = VertexChunk.ChunkSize;
            int triCount = tileWidth * tileHeight * 2;
            var walkable = new bool[triCount];

            int startC = chunkX * VertexChunk.ChunkSize;
            int startR = chunkY * VertexChunk.ChunkSize;
            int mapWidth = map.WidthInChunks * VertexChunk.ChunkSize;
            int mapHeight = map.HeightInChunks * VertexChunk.ChunkSize;

            // Pass 1: Basic walkability from vertex properties
            for (int localR = 0; localR < tileHeight; localR++)
            {
                for (int localC = 0; localC < tileWidth; localC++)
                {
                    int globalC = startC + localC;
                    int globalR = startR + localR;

                    // Skip edge cells that don't have complete triangles
                    if (globalR >= mapHeight - 1 || globalC >= mapWidth - 1)
                        continue;

                    int cellIndex = localR * tileWidth + localC;
                    bool isOdd = (globalR & 1) == 1;

                    // Get vertices for this cell's triangles
                    var v00 = GetVertex(map, mapWidth, mapHeight, globalC, globalR);
                    var v10 = GetVertex(map, mapWidth, mapHeight, globalC + 1, globalR);
                    var v01 = GetVertex(map, mapWidth, mapHeight, globalC, globalR + 1);
                    var v11 = GetVertex(map, mapWidth, mapHeight, globalC + 1, globalR + 1);

                    // Triangle layout depends on row parity (hex grid staggering)
                    // Even rows: Tri0 = (v00, v10, v01), Tri1 = (v10, v11, v01)
                    // Odd rows:  Tri0 = (v00, v10, v11), Tri1 = (v00, v11, v01)
                    WalkVertex t0a, t0b, t0c;
                    WalkVertex t1a, t1b, t1c;

                    if (!isOdd)
                    {
                        t0a = v00; t0b = v10; t0c = v01;
                        t1a = v10; t1b = v11; t1c = v01;
                    }
                    else
                    {
                        t0a = v00; t0b = v10; t0c = v11;
                        t1a = v00; t1b = v11; t1c = v01;
                    }

                    walkable[cellIndex * 2 + 0] = IsTriangleWalkable(t0a, t0b, t0c, config);
                    walkable[cellIndex * 2 + 1] = IsTriangleWalkable(t1a, t1b, t1c, config);
                }
            }

            // Pass 2: Apply cliff-straightening corrections.
            // When a cliff edge has a straighten mark, the diagonal triangle that
            // protrudes into the cliff face is forced unwalkable, producing a clean
            // axis-aligned boundary instead of a jagged staircase.
            ApplyCliffStraightening(map, walkable, tileWidth, tileHeight, startC, startR, mapWidth, mapHeight);

            return new TriWalkMask(tileWidth, tileHeight, walkable);
        }

        /// <summary>
        /// Reads cliff-straighten flags from VertexChunk and forces the protruding
        /// diagonal triangle unwalkable at each marked edge.
        ///
        /// Cliff straighten edges per cell (3 per cell):
        ///   Edge 0: horizontal right  (c,r) → (c+1,r)
        ///   Edge 1: bottom diagonal   (c,r) → (isOdd ? c+1 : c, r+1)
        ///   Edge 2: bottom diagonal   (c,r) → (isOdd ? c : c-1, r+1)
        ///
        /// When edge 0 is marked (vertical cliff running north-south), the cell's
        /// diagonal triangle that crosses the cliff line is forced unwalkable.
        /// </summary>
        private static void ApplyCliffStraightening(
            VertexMap map,
            bool[] walkable,
            int tileWidth,
            int tileHeight,
            int startC,
            int startR,
            int mapWidth,
            int mapHeight)
        {
            for (int localR = 0; localR < tileHeight; localR++)
            {
                for (int localC = 0; localC < tileWidth; localC++)
                {
                    int globalC = startC + localC;
                    int globalR = startR + localR;

                    if (globalR >= mapHeight - 1 || globalC >= mapWidth - 1)
                        continue;

                    var chunk = map.GetChunk(globalC, globalR, false);
                    if (chunk == null) continue;

                    int lx = globalC & VertexChunk.ChunkSizeMask;
                    int ly = globalR & VertexChunk.ChunkSizeMask;
                    bool isOdd = (globalR & 1) == 1;
                    int cellIndex = localR * tileWidth + localC;

                    // Edge 0: horizontal right edge (c,r)↔(c+1,r).
                    // A cliff along this edge runs north-south.
                    // The cell's diagonal crosses this cliff, so the triangle
                    // containing the diagonal on the cliff side should be unwalkable.
                    if (chunk.GetCliffStraightenEdge(lx, ly, 0))
                    {
                        // Determine which side is high and which is low
                        byte hHere = GetHeightSafe(map, mapWidth, mapHeight, globalC, globalR);
                        byte hRight = GetHeightSafe(map, mapWidth, mapHeight, globalC + 1, globalR);

                        if (hHere != hRight)
                        {
                            // The diagonal triangle that protrudes across this horizontal edge:
                            // Even row: diagonal goes v10↔v01 → Tri0 contains both v00 and v10 on the top edge
                            //           Tri0's third vertex (v01) protrudes downward
                            //           Tri1's third vertex (v11) protrudes downward-right
                            // Odd row:  diagonal goes v00↔v11 → Tri0 contains v00,v10,v11 (top+right)
                            //           Tri1 contains v00,v11,v01 (left+bottom)
                            //
                            // For a horizontal cliff edge, the triangle touching the cliff's
                            // lower side through the diagonal should be marked unwalkable.
                            // We mark BOTH triangles of the cell as they straddle the cliff.
                            // The triangle on the non-walkable side is already unwalkable from Pass 1;
                            // we only need to catch the diagonal triangle that "leaks" into the wrong side.

                            // Simple approach: if height differs across this edge and it's marked
                            // for straightening, ensure the cell containing the cliff edge has
                            // consistent walkability (both tris match the majority side).
                            ForceConsistentCliffWalkability(walkable, cellIndex, hHere > hRight);
                        }
                    }

                    // Edge 1: diagonal bottom edge (c,r)↔(n1c, r+1)
                    if (chunk.GetCliffStraightenEdge(lx, ly, 1))
                    {
                        int n1c = isOdd ? globalC + 1 : globalC;
                        byte hHere = GetHeightSafe(map, mapWidth, mapHeight, globalC, globalR);
                        byte hNeighbor = GetHeightSafe(map, mapWidth, mapHeight, n1c, globalR + 1);

                        if (hHere != hNeighbor)
                        {
                            ForceConsistentCliffWalkability(walkable, cellIndex, hHere > hNeighbor);
                        }
                    }

                    // Edge 2: diagonal bottom edge (c,r)↔(n2c, r+1)
                    if (chunk.GetCliffStraightenEdge(lx, ly, 2))
                    {
                        int n2c = isOdd ? globalC : globalC - 1;
                        byte hHere = GetHeightSafe(map, mapWidth, mapHeight, globalC, globalR);
                        byte hNeighbor = GetHeightSafe(map, mapWidth, mapHeight, n2c, globalR + 1);

                        if (hHere != hNeighbor)
                        {
                            ForceConsistentCliffWalkability(walkable, cellIndex, hHere > hNeighbor);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// At a cliff-straightened edge, forces the two triangles of a cell to have
        /// consistent walkability. If one is walkable and the other isn't (the jagged case),
        /// the triangle that protrudes across the cliff is forced unwalkable.
        /// </summary>
        /// <param name="walkable">Walkability array.</param>
        /// <param name="cellIndex">Cell index in tile.</param>
        /// <param name="currentIsHighSide">True if this cell is the high side of the cliff.</param>
        private static void ForceConsistentCliffWalkability(bool[] walkable, int cellIndex, bool currentIsHighSide)
        {
            bool tri0 = walkable[cellIndex * 2 + 0];
            bool tri1 = walkable[cellIndex * 2 + 1];

            // If both triangles agree, no jagged edge to fix
            if (tri0 == tri1) return;

            // One is walkable, one isn't → the walkable one protrudes across the cliff.
            // Force it unwalkable to straighten the boundary.
            // On the HIGH side of a cliff, we keep walkable triangles (the cliff top is walkable).
            // On the LOW side, we also keep walkable triangles (the cliff bottom is walkable).
            // The jagged triangle is the one that is walkable on the WRONG side.
            //
            // For cliff straightening, the safe correction is: if the cell straddles a
            // marked cliff edge and the two triangles disagree, force both to match the
            // MAJORITY (the triangle that should be walkable based on its own vertices).
            // Since Pass 1 already determined walkability from vertex heights, the
            // "protruding" triangle is actually already marked correctly in most cases.
            // The issue is only when the diagonal leaks - in that case, force unwalkable.
            walkable[cellIndex * 2 + 0] = false;
            walkable[cellIndex * 2 + 1] = false;

            // Restore the one that should be walkable: the triangle whose vertices
            // are all on the same height level (determined by Pass 1).
            // Since one was already walkable, restore it only if it's on the high side.
            if (currentIsHighSide)
            {
                // High side: keep the triangle that was walkable (it's on the plateau)
                if (tri0) walkable[cellIndex * 2 + 0] = true;
                if (tri1) walkable[cellIndex * 2 + 1] = true;
            }
            // else: low side with mixed walkability at cliff edge → both unwalkable
            // creates a clean gap at the cliff base
        }

        private static byte GetHeightSafe(VertexMap map, int mapWidth, int mapHeight, int c, int r)
        {
            if ((uint)c >= (uint)mapWidth || (uint)r >= (uint)mapHeight) return 0;
            var chunk = map.GetChunk(c, r, false);
            if (chunk == null) return 0;
            int lx = c & VertexChunk.ChunkSizeMask;
            int lr = r & VertexChunk.ChunkSizeMask;
            return chunk.GetHeight(lx, lr);
        }

        private static WalkVertex GetVertex(VertexMap map, int mapWidth, int mapHeight, int c, int r)
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

            return new WalkVertex(c, r, h, w, ramp, blocked);
        }

        /// <summary>
        /// Determines if a triangle is walkable based on vertex properties and config.
        /// </summary>
        private static bool IsTriangleWalkable(in WalkVertex a, in WalkVertex b, in WalkVertex c, in NavBuildConfig config)
        {
            // Blocked vertices make triangle unwalkable
            if (a.IsBlocked || b.IsBlocked || c.IsBlocked)
                return false;

            // Water covering any vertex makes triangle unwalkable
            // Note: WaterHeight > Height means submerged
            if (a.WaterHeight > a.Height || b.WaterHeight > b.Height || c.WaterHeight > c.Height)
                return false;

            // Ramp vertices allow height differences (explicit slope marker)
            if (a.IsRamp || b.IsRamp || c.IsRamp)
                return true;

            // Check height difference against cliff threshold
            byte minH = Math.Min(a.Height, Math.Min(b.Height, c.Height));
            byte maxH = Math.Max(a.Height, Math.Max(b.Height, c.Height));
            return (maxH - minH) <= config.CliffHeightThreshold;
        }

        /// <summary>
        /// Gets the triangle vertices for a specific cell and triangle index.
        /// Returns vertex indices in local grid coordinates (relative to tile origin).
        /// </summary>
        public static void GetTriangleVertexOffsets(int localCol, int localRow, int triIndex, bool isOddRow,
            out (int dc, int dr) va, out (int dc, int dr) vb, out (int dc, int dr) vc)
        {
            // Base cell is at (localCol, localRow)
            // Vertices are at grid corners: (c,r), (c+1,r), (c,r+1), (c+1,r+1)
            var v00 = (0, 0);
            var v10 = (1, 0);
            var v01 = (0, 1);
            var v11 = (1, 1);

            if (!isOddRow)
            {
                if (triIndex == 0) { va = v00; vb = v10; vc = v01; }
                else { va = v10; vb = v11; vc = v01; }
            }
            else
            {
                if (triIndex == 0) { va = v00; vb = v10; vc = v11; }
                else { va = v00; vb = v11; vc = v01; }
            }
        }
    }
}
