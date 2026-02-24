using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Map.Hex
{
    /// <summary>
    /// Represents a coordinate in a hexagonal grid system using Axial coordinates (q, r).
    /// This struct is designed to be immutable and memory efficient.
    /// </summary>
    public readonly struct HexCoordinates : IEquatable<HexCoordinates>
    {
        public readonly int Q;
        public readonly int R;

        public int S => -Q - R;

        // Grid Constants
        // Assuming 4 meters per edge as per requirements
        public const float EdgeLength = 4.0f; 

        public const int EdgeLengthCm = 400;
        
        // Pointy-topped hex orientation constants
        // Width = sqrt(3) * size
        // Height = 2 * size
        // Horizontal spacing = Width
        // Vertical spacing = 3/4 * Height = 1.5 * size
        public const float HexWidth = 6.92820323f; // sqrt(3) * 4
        public const float HexHeight = 8.0f;       // 2 * 4
        
        // Horizontal distance between adjacent column centers
        public const float ColSpacing = HexWidth; 
        // Vertical distance between adjacent row centers
        public const float RowSpacing = 6.0f; // 1.5 * 4

        public HexCoordinates(int q, int r)
        {
            Q = q;
            R = r;
        }

        public static HexCoordinates Zero => new HexCoordinates(0, 0);

        // ── 6 hex direction offsets (pointy-top, axial coordinates) ──

        public static ReadOnlySpan<HexCoordinates> Directions => new[]
        {
            new HexCoordinates(+1,  0),  // East
            new HexCoordinates(+1, -1),  // NE
            new HexCoordinates( 0, -1),  // NW
            new HexCoordinates(-1,  0),  // West
            new HexCoordinates(-1, +1),  // SW
            new HexCoordinates( 0, +1),  // SE
        };

        /// <summary>Hex distance (cube-coordinate Manhattan / 2).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Distance(HexCoordinates a, HexCoordinates b)
        {
            int dq = a.Q - b.Q;
            int dr = a.R - b.R;
            int ds = dq + dr; // a.S - b.S = -(a.Q+a.R) + (b.Q+b.R) = -(dq+dr)
            return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(ds)) / 2;
        }

        /// <summary>Writes the 6 neighbors of <paramref name="center"/> into <paramref name="output"/>. Output must have length >= 6.</summary>
        public static void GetNeighbors(HexCoordinates center, Span<HexCoordinates> output)
        {
            var dirs = Directions;
            for (int i = 0; i < 6; i++)
                output[i] = center + dirs[i];
        }

        /// <summary>
        /// Writes hex coordinates on a ring at exactly <paramref name="radius"/> steps from center.
        /// Returns the number written. radius=0 → writes center, returns 1.
        /// Output must have length >= 6 * radius (or 1 for radius 0).
        /// </summary>
        public static int GetRing(HexCoordinates center, int radius, Span<HexCoordinates> output)
        {
            if (radius <= 0)
            {
                output[0] = center;
                return 1;
            }

            var dirs = Directions;
            // Start at direction 4 (SW) scaled by radius — standard hex ring walk
            var cursor = new HexCoordinates(center.Q + dirs[4].Q * radius, center.R + dirs[4].R * radius);
            int idx = 0;
            for (int side = 0; side < 6; side++)
            {
                for (int step = 0; step < radius; step++)
                {
                    output[idx++] = cursor;
                    cursor = cursor + dirs[side];
                }
            }
            return idx;
        }

        /// <summary>
        /// Writes all hex coordinates within <paramref name="radius"/> of center (inclusive).
        /// Returns the number written. Total = 1 + 3*radius*(radius+1).
        /// Output must have sufficient capacity.
        /// </summary>
        public static int GetRange(HexCoordinates center, int radius, Span<HexCoordinates> output)
        {
            int idx = 0;
            for (int q = -radius; q <= radius; q++)
            {
                int r1 = Math.Max(-radius, -q - radius);
                int r2 = Math.Min(radius, -q + radius);
                for (int r = r1; r <= r2; r++)
                {
                    output[idx++] = new HexCoordinates(center.Q + q, center.R + r);
                }
            }
            return idx;
        }

        /// <summary>Returns the maximum number of hexes in a range of given radius: 1 + 3*r*(r+1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RangeCount(int radius) => radius <= 0 ? 1 : 1 + 3 * radius * (radius + 1);

        /// <summary>Returns the number of hexes on a ring: 6*r (or 1 for r=0).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RingCount(int radius) => radius <= 0 ? 1 : 6 * radius;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static HexCoordinates FromOffsetCoordinates(int col, int row)
        {
            // Convert "Odd-r" offset to Axial
            // q = col - (row - (row&1)) / 2
            // r = row
            var q = col - (row - (row & 1)) / 2;
            var r = row;
            return new HexCoordinates(q, r);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (int col, int row) ToOffsetCoordinates()
        {
            // Convert Axial to "Odd-r" offset
            // col = q + (r - (r&1)) / 2
            // row = r
            var col = Q + (R - (R & 1)) / 2;
            var row = R;
            return (col, row);
        }

        /// <summary>
        /// Converts the Hex coordinate to a World Position (Vector3).
        /// Assumes Y is up, mapping (q, r) to (x, z).
        /// </summary>
        public Vector3 ToWorldPosition()
        {
            // Pointy-topped hex to pixel:
            // x = size * sqrt(3) * (q + r/2)
            // z = size * 3/2 * r
            
            float x = EdgeLength * 1.7320508f * (Q + R / 2.0f);
            float z = EdgeLength * 1.5f * R;
            
            return new Vector3(x, 0, z);
        }

        public Vector3 ToWorldPositionCm()
        {
            float x = EdgeLengthCm * 1.7320508f * (Q + R / 2.0f);
            float z = EdgeLengthCm * 1.5f * R;
            return new Vector3(x, 0, z);
        }

        /// <summary>
        /// Converts a World Position to the nearest Hex Coordinate.
        /// </summary>
        public static HexCoordinates FromWorldPosition(Vector3 position)
        {
            float q = (1.7320508f / 3.0f * position.X - 1.0f / 3.0f * position.Z) / EdgeLength;
            float r = (2.0f / 3.0f * position.Z) / EdgeLength;
            
            return Round(q, r);
        }

        public static HexCoordinates FromWorldPositionCm(Vector3 positionCm)
        {
            float q = (1.7320508f / 3.0f * positionCm.X - 1.0f / 3.0f * positionCm.Z) / EdgeLengthCm;
            float r = (2.0f / 3.0f * positionCm.Z) / EdgeLengthCm;
            return Round(q, r);
        }

        /// <summary>Hex cube-coordinate rounding from fractional axial coordinates.</summary>
        internal static HexCoordinates Round(float fracQ, float fracR)
        {
            float fracS = -fracQ - fracR;
            int q = (int)Math.Round(fracQ);
            int r = (int)Math.Round(fracR);
            int s = (int)Math.Round(fracS);

            float qDiff = Math.Abs(q - fracQ);
            float rDiff = Math.Abs(r - fracR);
            float sDiff = Math.Abs(s - fracS);

            if (qDiff > rDiff && qDiff > sDiff)
            {
                q = -r - s;
            }
            else if (rDiff > sDiff)
            {
                r = -q - s;
            }
            
            return new HexCoordinates(q, r);
        }

        /// <summary>
        /// Generates a unique chunk key for the given chunk coordinates.
        /// Packs two shorts (chunkX, chunkY) into a long.
        /// </summary>
        public static long GetChunkKey(int chunkX, int chunkY)
        {
            return (long)chunkX & 0xFFFFFFFF | ((long)chunkY << 32);
        }
        
        public static (int x, int y) GetChunkCoordinatesFromKey(long key)
        {
            int x = (int)(key & 0xFFFFFFFF);
            int y = (int)(key >> 32);
            return (x, y);
        }

        public bool Equals(HexCoordinates other)
        {
            return Q == other.Q && R == other.R;
        }

        public override bool Equals(object obj)
        {
            return obj is HexCoordinates other && Equals(other);
        }

        public override int GetHashCode()
        {
            // Optimized hash code for small integers
            return HashCode.Combine(Q, R);
        }

        public static bool operator ==(HexCoordinates left, HexCoordinates right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(HexCoordinates left, HexCoordinates right)
        {
            return !left.Equals(right);
        }

        public static HexCoordinates operator +(HexCoordinates a, HexCoordinates b)
        {
            return new HexCoordinates(a.Q + b.Q, a.R + b.R);
        }

        public static HexCoordinates operator -(HexCoordinates a, HexCoordinates b)
        {
            return new HexCoordinates(a.Q - b.Q, a.R - b.R);
        }
        
        public override string ToString()
        {
            return $"({Q}, {R})";
        }
    }
}
