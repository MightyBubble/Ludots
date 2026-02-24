using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Blackboard buffer for spatial data (positions, waypoints).
    /// Each entry can store multiple points (e.g., for path waypoints).
    /// </summary>
    public unsafe struct BlackboardSpatialBuffer
    {
        public const int MAX_ENTRIES = 8;
        public const int MAX_POINTS_PER_ENTRY = 16;
        
        public int EntryCount;
        public fixed int Keys[MAX_ENTRIES];
        public fixed int PointCounts[MAX_ENTRIES];
        public fixed float PointsX[MAX_ENTRIES * MAX_POINTS_PER_ENTRY];
        public fixed float PointsY[MAX_ENTRIES * MAX_POINTS_PER_ENTRY];
        public fixed float PointsZ[MAX_ENTRIES * MAX_POINTS_PER_ENTRY];

        /// <summary>
        /// Get the index of an entry by key, or -1 if not found.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindEntry(int key)
        {
            fixed (int* keys = Keys)
            {
                for (int i = 0; i < EntryCount; i++)
                {
                    if (keys[i] == key) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Try to get a single point by key.
        /// Returns the first point if multiple exist.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetPoint(int key, out Vector3 point)
        {
            int idx = FindEntry(key);
            if (idx < 0)
            {
                point = default;
                return false;
            }
            
            fixed (int* pointCounts = PointCounts)
            {
                if (pointCounts[idx] == 0)
                {
                    point = default;
                    return false;
                }
            }
            
            int offset = idx * MAX_POINTS_PER_ENTRY;
            fixed (float* px = PointsX)
            fixed (float* py = PointsY)
            fixed (float* pz = PointsZ)
            {
                point = new Vector3(px[offset], py[offset], pz[offset]);
            }
            return true;
        }

        /// <summary>
        /// Get the number of points for a key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetPointCount(int key)
        {
            int idx = FindEntry(key);
            if (idx < 0) return 0;
            
            fixed (int* pointCounts = PointCounts)
            {
                return pointCounts[idx];
            }
        }

        /// <summary>
        /// Get a point by key and index.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetPointAt(int key, int pointIndex, out Vector3 point)
        {
            int idx = FindEntry(key);
            if (idx < 0)
            {
                point = default;
                return false;
            }
            
            fixed (int* pointCounts = PointCounts)
            {
                if (pointIndex < 0 || pointIndex >= pointCounts[idx])
                {
                    point = default;
                    return false;
                }
            }
            
            int offset = idx * MAX_POINTS_PER_ENTRY + pointIndex;
            fixed (float* px = PointsX)
            fixed (float* py = PointsY)
            fixed (float* pz = PointsZ)
            {
                point = new Vector3(px[offset], py[offset], pz[offset]);
            }
            return true;
        }

        /// <summary>
        /// Set a single point for a key (clears existing points).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPoint(int key, Vector3 point)
        {
            int idx = FindEntry(key);
            if (idx < 0)
            {
                if (EntryCount >= MAX_ENTRIES) return;
                idx = EntryCount;
                fixed (int* keys = Keys)
                {
                    keys[idx] = key;
                }
                EntryCount++;
            }
            
            int offset = idx * MAX_POINTS_PER_ENTRY;
            fixed (int* pointCounts = PointCounts)
            fixed (float* px = PointsX)
            fixed (float* py = PointsY)
            fixed (float* pz = PointsZ)
            {
                pointCounts[idx] = 1;
                px[offset] = point.X;
                py[offset] = point.Y;
                pz[offset] = point.Z;
            }
        }

        /// <summary>
        /// Append a point to an entry (for waypoint queues).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AppendPoint(int key, Vector3 point)
        {
            int idx = FindEntry(key);
            if (idx < 0)
            {
                if (EntryCount >= MAX_ENTRIES) return false;
                idx = EntryCount;
                fixed (int* keys = Keys)
                fixed (int* pointCounts = PointCounts)
                {
                    keys[idx] = key;
                    pointCounts[idx] = 0;
                }
                EntryCount++;
            }
            
            fixed (int* pointCounts = PointCounts)
            {
                if (pointCounts[idx] >= MAX_POINTS_PER_ENTRY) return false;
                
                int offset = idx * MAX_POINTS_PER_ENTRY + pointCounts[idx];
                fixed (float* px = PointsX)
                fixed (float* py = PointsY)
                fixed (float* pz = PointsZ)
                {
                    px[offset] = point.X;
                    py[offset] = point.Y;
                    pz[offset] = point.Z;
                }
                pointCounts[idx]++;
            }
            return true;
        }

        /// <summary>
        /// Remove the first point from an entry (pop from waypoint queue).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool PopFirstPoint(int key, out Vector3 point)
        {
            int idx = FindEntry(key);
            if (idx < 0)
            {
                point = default;
                return false;
            }
            
            fixed (int* pointCounts = PointCounts)
            {
                if (pointCounts[idx] == 0)
                {
                    point = default;
                    return false;
                }
                
                int offset = idx * MAX_POINTS_PER_ENTRY;
                fixed (float* px = PointsX)
                fixed (float* py = PointsY)
                fixed (float* pz = PointsZ)
                {
                    point = new Vector3(px[offset], py[offset], pz[offset]);
                    
                    // Shift remaining points
                    for (int i = 0; i < pointCounts[idx] - 1; i++)
                    {
                        px[offset + i] = px[offset + i + 1];
                        py[offset + i] = py[offset + i + 1];
                        pz[offset + i] = pz[offset + i + 1];
                    }
                }
                pointCounts[idx]--;
            }
            return true;
        }

        /// <summary>
        /// Clear all points for a key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearPoints(int key)
        {
            int idx = FindEntry(key);
            if (idx < 0) return;
            
            fixed (int* pointCounts = PointCounts)
            {
                pointCounts[idx] = 0;
            }
        }

        /// <summary>
        /// Remove an entry entirely.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RemoveEntry(int key)
        {
            int idx = FindEntry(key);
            if (idx < 0) return false;
            
            // Shift remaining entries
            fixed (int* keys = Keys)
            fixed (int* pointCounts = PointCounts)
            fixed (float* px = PointsX)
            fixed (float* py = PointsY)
            fixed (float* pz = PointsZ)
            {
                for (int i = idx; i < EntryCount - 1; i++)
                {
                    keys[i] = keys[i + 1];
                    pointCounts[i] = pointCounts[i + 1];
                    
                    int srcOffset = (i + 1) * MAX_POINTS_PER_ENTRY;
                    int dstOffset = i * MAX_POINTS_PER_ENTRY;
                    for (int j = 0; j < MAX_POINTS_PER_ENTRY; j++)
                    {
                        px[dstOffset + j] = px[srcOffset + j];
                        py[dstOffset + j] = py[srcOffset + j];
                        pz[dstOffset + j] = pz[srcOffset + j];
                    }
                }
            }
            EntryCount--;
            return true;
        }

        /// <summary>
        /// Check if a key exists.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasKey(int key)
        {
            return FindEntry(key) >= 0;
        }

        /// <summary>
        /// Clear all entries.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            EntryCount = 0;
        }
    }
}
