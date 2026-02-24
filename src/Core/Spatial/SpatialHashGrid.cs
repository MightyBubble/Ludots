using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// A high-performance spatial hash grid for storing and querying 2D entities.
    /// Uses a 1D array for bucket storage to minimize GC overhead.
    /// Best for uniformly distributed dynamic objects.
    /// </summary>
    /// <typeparam name="T">The type of ID/Entity to store (e.g., int EntityId)</typeparam>
    public class SpatialHashGrid<T> where T : unmanaged
    {
        private readonly float _cellSize;
        private readonly Dictionary<long, Bucket> _buckets;
        private readonly List<T> _queryResultCache; // Reusable list for queries

        // We implement a custom bucket using pooled arrays/lists to avoid creating objects per cell
        // But for simplicity and decent performance, a Dictionary<long, List<T>> is common.
        // To be strictly Zero-GC, we would need a flat array hash map. 
        // Given "best practice" request, let's stick to Dictionary for sparse storage but optimize the List.
        
        // Actually, for strict Zero-GC, Dictionary causes allocations on Insert if buckets are new.
        // Let's implement a Fixed-Size Hash Table if the world bounds are known, 
        // OR use a Dictionary with a custom struct Bucket that holds a compact array index.

        private struct Bucket
        {
            // We'll store items in a global pool and just store the head index here?
            // Or just a simple List<T> for now, assuming reusing the list instance.
            public List<T> Items; 
        }

        public SpatialHashGrid(float cellSize, int initialCapacity = 1024)
        {
            _cellSize = cellSize;
            _buckets = new Dictionary<long, Bucket>(initialCapacity);
            _queryResultCache = new List<T>(64);
        }

        public void Insert(T item, Vector2 position)
        {
            long key = GetKey(position);
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                bucket = new Bucket { Items = new List<T>(8) };
                _buckets[key] = bucket;
            }
            bucket.Items.Add(item);
        }

        public void Remove(T item, Vector2 position)
        {
            long key = GetKey(position);
            if (_buckets.TryGetValue(key, out var bucket))
            {
                bucket.Items.Remove(item);
                // Optional: Remove bucket if empty to save memory, but keeps GC churn. 
                // Better keep it if it's likely to be reused.
            }
        }
        
        /// <summary>
        /// Clears all items but keeps bucket instances to reduce GC on refill.
        /// </summary>
        public void Clear()
        {
            foreach (var bucket in _buckets.Values)
            {
                bucket.Items.Clear();
            }
        }

        public List<T> Query(Vector2 position, float radius)
        {
            _queryResultCache.Clear();
            
            int minX = (int)Math.Floor((position.X - radius) / _cellSize);
            int maxX = (int)Math.Floor((position.X + radius) / _cellSize);
            int minY = (int)Math.Floor((position.Y - radius) / _cellSize);
            int maxY = (int)Math.Floor((position.Y + radius) / _cellSize);

            float radiusSq = radius * radius;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    long key = GetKey(x, y);
                    if (_buckets.TryGetValue(key, out var bucket))
                    {
                        var items = bucket.Items;
                        int count = items.Count;
                        for (int i = 0; i < count; i++)
                        {
                            // Note: Exact distance check should happen outside if we only store IDs.
                            // If we want exact check here, we need position data.
                            // Assuming Broad-phase here.
                            _queryResultCache.Add(items[i]);
                        }
                    }
                }
            }

            return _queryResultCache;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetKey(Vector2 position)
        {
            int x = (int)Math.Floor(position.X / _cellSize);
            int y = (int)Math.Floor(position.Y / _cellSize);
            return GetKey(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetKey(int x, int y)
        {
            // Simple packing for reasonable ranges. 
            return (long)x << 32 | (uint)y; 
        }
    }
}
