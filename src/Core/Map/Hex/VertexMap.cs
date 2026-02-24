using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Hex
{
    /// <summary>
    /// Manager for the global vertex-based dual map.
    /// Holds the sparse collection of VertexChunks.
    /// </summary>
    public class VertexMap
    {
        private readonly Dictionary<long, VertexChunk> _chunks = new Dictionary<long, VertexChunk>();

        // Cache the last accessed chunk for performance
        private long _lastChunkKey = -1;
        private VertexChunk _lastChunk = null;
        private ILoadedChunks _loadedChunks;

        // Map dimensions in chunks (optional bounds checking)
        public int WidthInChunks { get; private set; } = 64;
        public int HeightInChunks { get; private set; } = 64;

        /// <summary>
        /// Subscribe to an ILoadedChunks source. When chunks are unloaded,
        /// the corresponding VertexChunk is released to save memory.
        /// </summary>
        public void SubscribeToLoadedChunks(ILoadedChunks source)
        {
            UnsubscribeFromLoadedChunks();

            _loadedChunks = source;
            if (_loadedChunks != null)
            {
                _loadedChunks.ChunkUnloaded += OnChunkUnloaded;
            }
        }

        /// <summary>
        /// Detach from the current ILoadedChunks source to prevent event subscription leaks.
        /// Call this before the VertexMap is abandoned or replaced.
        /// </summary>
        public void UnsubscribeFromLoadedChunks()
        {
            if (_loadedChunks != null)
            {
                _loadedChunks.ChunkUnloaded -= OnChunkUnloaded;
                _loadedChunks = null;
            }
        }

        private void OnChunkUnloaded(long chunkKey)
        {
            _chunks.Remove(chunkKey);
            if (_lastChunkKey == chunkKey)
            {
                _lastChunkKey = -1;
                _lastChunk = null;
            }
        }

        public void Initialize(int widthInChunks, int heightInChunks)
        {
            WidthInChunks = widthInChunks;
            HeightInChunks = heightInChunks;
            Clear();
        }

        public bool IsValidChunk(int chunkX, int chunkY)
        {
            return chunkX >= 0 && chunkX < WidthInChunks && chunkY >= 0 && chunkY < HeightInChunks;
        }

        public VertexChunk GetChunk(int q, int r, bool createIfMissing = false)
        {
            int chunkX = q >> VertexChunk.ChunkSizeShift;
            int chunkY = r >> VertexChunk.ChunkSizeShift;

            // Optional: Bounds check
            if (!IsValidChunk(chunkX, chunkY)) return null;

            long key = HexCoordinates.GetChunkKey(chunkX, chunkY);

            if (key == _lastChunkKey && _lastChunk != null)
            {
                return _lastChunk;
            }

            if (_chunks.TryGetValue(key, out var chunk))
            {
                _lastChunkKey = key;
                _lastChunk = chunk;
                return chunk;
            }

            if (createIfMissing)
            {
                chunk = new VertexChunk();
                _chunks[key] = chunk;
                _lastChunkKey = key;
                _lastChunk = chunk;
                return chunk;
            }

            return null;
        }

        public byte GetHeight(int q, int r)
        {
            var chunk = GetChunk(q, r);
            if (chunk == null) return 0; // Default height

            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            return chunk.GetHeight(localX, localY);
        }

        public void SetHeight(int q, int r, byte height)
        {
            var chunk = GetChunk(q, r, true);
            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            chunk.SetHeight(localX, localY, height);
        }

        // --- New Layered API ---

        public byte GetBiome(int q, int r)
        {
            var chunk = GetChunk(q, r);
            if (chunk == null) return 0;

            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            return chunk.GetBiome(localX, localY);
        }

        public void SetBiome(int q, int r, byte biome)
        {
            var chunk = GetChunk(q, r, true);
            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            chunk.SetBiome(localX, localY, biome);
        }

        public byte GetWaterHeight(int q, int r)
        {
            var chunk = GetChunk(q, r);
            if (chunk == null) return 0;

            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            return chunk.GetWaterHeight(localX, localY);
        }

        public void SetWaterHeight(int q, int r, byte height)
        {
            var chunk = GetChunk(q, r, true);
            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            chunk.SetWaterHeight(localX, localY, height);
        }

        public byte GetVegetation(int q, int r)
        {
            var chunk = GetChunk(q, r);
            if (chunk == null) return 0;

            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            return chunk.GetVegetation(localX, localY);
        }

        public void SetVegetation(int q, int r, byte veg)
        {
            var chunk = GetChunk(q, r, true);
            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            chunk.SetVegetation(localX, localY, veg);
        }


        public bool IsBlocked(int q, int r)
        {
            var chunk = GetChunk(q, r);
            if (chunk == null) return false; // Default unblocked? Or blocked?

            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            return chunk.GetFlag(localX, localY);
        }

        public void SetBlocked(int q, int r, bool blocked)
        {
            var chunk = GetChunk(q, r, true);
            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            chunk.SetFlag(localX, localY, blocked);
        }

        public bool IsRamp(int q, int r)
        {
            var chunk = GetChunk(q, r);
            if (chunk == null) return false;

            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            return chunk.GetRamp(localX, localY);
        }

        public void SetRamp(int q, int r, bool isRamp)
        {
            var chunk = GetChunk(q, r, true);
            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            chunk.SetRamp(localX, localY, isRamp);
        }

        public byte GetFaction(int q, int r)
        {
            var chunk = GetChunk(q, r);
            if (chunk == null) return 0;

            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            return chunk.GetFaction(localX, localY);
        }

        public void SetFaction(int q, int r, byte factionId)
        {
            var chunk = GetChunk(q, r, true);
            int localX = q & VertexChunk.ChunkSizeMask;
            int localY = r & VertexChunk.ChunkSizeMask;
            chunk.SetFaction(localX, localY, factionId);
        }

        /// <summary>
        /// Gets the logical height at a world position using interpolation.
        /// Current implementation uses Nearest Neighbor (Vertex Height).
        /// TODO: Implement barycentric interpolation on the triangle grid.
        /// </summary>
        public float GetLogicHeight(Vector3 worldPos)
        {
            float size = HexCoordinates.EdgeLength;
            float fracQ = (0.577350269f * worldPos.X - 0.333333333f * worldPos.Z) / size;
            float fracR = (0.666666667f * worldPos.Z) / size;

            int baseQ = (int)MathF.Floor(fracQ);
            int baseR = (int)MathF.Floor(fracR);

            float u = fracQ - baseQ;
            float v = fracR - baseR;

            int q0, r0, q1, r1, q2, r2;
            if (u + v <= 1f)
            {
                q0 = baseQ;
                r0 = baseR;
                q1 = baseQ + 1;
                r1 = baseR;
                q2 = baseQ;
                r2 = baseR + 1;
            }
            else
            {
                q0 = baseQ + 1;
                r0 = baseR + 1;
                q1 = baseQ + 1;
                r1 = baseR;
                q2 = baseQ;
                r2 = baseR + 1;
            }

            Vector2 p0 = AxialToWorldXZ(q0, r0);
            Vector2 p1 = AxialToWorldXZ(q1, r1);
            Vector2 p2 = AxialToWorldXZ(q2, r2);
            Vector2 p = new Vector2(worldPos.X, worldPos.Z);

            float denom = (p1.Y - p2.Y) * (p0.X - p2.X) + (p2.X - p1.X) * (p0.Y - p2.Y);
            if (MathF.Abs(denom) < 1e-6f)
            {
                HexCoordinates coords = HexCoordinates.FromWorldPosition(worldPos);
                return GetHeight(coords.Q, coords.R);
            }

            float w0 = ((p1.Y - p2.Y) * (p.X - p2.X) + (p2.X - p1.X) * (p.Y - p2.Y)) / denom;
            float w1 = ((p2.Y - p0.Y) * (p.X - p2.X) + (p0.X - p2.X) * (p.Y - p2.Y)) / denom;
            float w2 = 1f - w0 - w1;

            float h0 = GetHeight(q0, r0);
            float h1 = GetHeight(q1, r1);
            float h2 = GetHeight(q2, r2);
            return w0 * h0 + w1 * h1 + w2 * h2;
        }

        private static Vector2 AxialToWorldXZ(int q, int r)
        {
            float x = HexCoordinates.EdgeLength * 1.7320508f * (q + r / 2.0f);
            float z = HexCoordinates.EdgeLength * 1.5f * r;
            return new Vector2(x, z);
        }

        /// <summary>
        /// Clears all map data.
        /// </summary>
        public void Clear()
        {
            _chunks.Clear();
            _lastChunk = null;
            _lastChunkKey = -1;
        }
        
        /// <summary>
        /// Returns the total number of allocated chunks.
        /// </summary>
        public int ChunkCount => _chunks.Count;
    }
}
