using System;

namespace Ludots.Core.Vision
{
    public interface IVisionStampFilter
    {
        bool Allows(int originX, int originY, int cellX, int cellY);

        void OnFirstExplore(int cellX, int cellY);
    }

    public static class VisionBulkStamp
    {
        public static int[] BuildOffsets(int rangeCm, int cellSizeCm)
        {
            if (rangeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rangeCm));
            }

            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            }

            long rangeSq = (long)rangeCm * rangeCm;
            int radius = (rangeCm + cellSizeCm - 1) / cellSizeCm;
            var offsets = new int[(radius * 2 + 1) * (radius * 2 + 1) * 2];
            int count = 0;
            for (int dy = -radius; dy <= radius; dy++)
            {
                long dyCm = (long)dy * cellSizeCm;
                long dySq = dyCm * dyCm;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    long dxCm = (long)dx * cellSizeCm;
                    if (dxCm * dxCm + dySq > rangeSq)
                    {
                        continue;
                    }

                    offsets[count++] = dx;
                    offsets[count++] = dy;
                }
            }

            if (count == offsets.Length)
            {
                return offsets;
            }

            var packed = new int[count];
            Array.Copy(offsets, packed, count);
            return packed;
        }

        public static void Stamp<TFilter>(
            FogField field,
            int side,
            ReadOnlySpan<int> occupied,
            ReadOnlySpan<int> dxdy,
            ReadOnlySpan<ushort> obscured,
            Span<byte> visibleMask,
            ref TFilter filter)
            where TFilter : struct, IVisionStampFilter
        {
            Fill(field, side, occupied, dxdy, obscured, visibleMask, ref filter);
        }

        public static void StampOpen<TFilter>(
            FogField field,
            int side,
            ReadOnlySpan<int> occupied,
            ReadOnlySpan<int> dxdy,
            ReadOnlySpan<ushort> obscured,
            Span<byte> visibleMask,
            ref TFilter filter)
            where TFilter : struct, IVisionStampFilter
        {
            PrepareMask(field, side, visibleMask);
            bool gate = obscured.Length != 0;
            for (int i = 0; i < occupied.Length; i++)
            {
                int cell = occupied[i];
                int cx = cell % side;
                int cy = cell / side;
                for (int k = 0; k < dxdy.Length; k += 2)
                {
                    int x = cx + dxdy[k];
                    int y = cy + dxdy[k + 1];
                    if ((uint)x >= (uint)side || (uint)y >= (uint)side)
                    {
                        continue;
                    }

                    int index = y * side + x;
                    if (gate && obscured[index] != 0)
                    {
                        continue;
                    }

                    visibleMask[index] = 1;
                }
            }

            field.CommitVisibleMask(visibleMask, side, ref filter);
        }

        private static void PrepareMask(FogField field, int side, Span<byte> visibleMask)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (side <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(side));
            }

            if (visibleMask.Length != side * side)
            {
                throw new ArgumentException("Visible mask must cover the fog block.", nameof(visibleMask));
            }

            visibleMask.Clear();
        }

        private static void Fill<TFilter>(
            FogField field,
            int side,
            ReadOnlySpan<int> occupied,
            ReadOnlySpan<int> dxdy,
            ReadOnlySpan<ushort> obscured,
            Span<byte> visibleMask,
            ref TFilter filter)
            where TFilter : struct, IVisionStampFilter
        {
            PrepareMask(field, side, visibleMask);
            bool gate = obscured.Length != 0;
            for (int i = 0; i < occupied.Length; i++)
            {
                int cell = occupied[i];
                int cx = cell % side;
                int cy = cell / side;
                for (int k = 0; k < dxdy.Length; k += 2)
                {
                    int x = cx + dxdy[k];
                    int y = cy + dxdy[k + 1];
                    if ((uint)x >= (uint)side || (uint)y >= (uint)side)
                    {
                        continue;
                    }

                    int index = y * side + x;
                    if (gate && obscured[index] != 0)
                    {
                        continue;
                    }

                    if (!filter.Allows(cx, cy, x, y))
                    {
                        continue;
                    }

                    visibleMask[index] = 1;
                }
            }

            field.CommitVisibleMask(visibleMask, side, ref filter);
        }
    }
}
