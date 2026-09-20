using System;

namespace Ludots.Core.Fields
{
    /// <summary>
    /// Independent change cursor over a <see cref="ChunkedField2D{T}"/>. Each cursor
    /// remembers the per-chunk change stamps it has already delivered, so multiple
    /// consumers observe the same increments without clearing each other's pending
    /// set and without interfering with the single-reader dirty mask
    /// (<see cref="ChunkedField2D{T}.EnumerateDirtyCells"/> / ClearDirty).
    /// Delivery granularity is a whole chunk: take the chunk, read every cell.
    /// </summary>
    public sealed class FieldDirtyCursor<T>
        where T : struct
    {
        private readonly ChunkedField2D<T> _field;
        private long[] _deliveredStamps = Array.Empty<long>();

        internal FieldDirtyCursor(ChunkedField2D<T> field)
        {
            _field = field;
            EnsureCapacity();
            for (int i = 0; i < field.ChunkCount; i++)
            {
                _deliveredStamps[i] = field.GetChunkAt(i).ChangeStamp;
            }
        }

        /// <summary>Chunks changed since this cursor last drained them (not yet taken).</summary>
        public int PendingChunkCount
        {
            get
            {
                EnsureCapacity();
                int pending = 0;
                for (int i = 0; i < _field.ChunkCount; i++)
                {
                    if (_field.GetChunkAt(i).ChangeStamp > _deliveredStamps[i])
                    {
                        pending++;
                    }
                }

                return pending;
            }
        }

        public bool TryTakeChangedChunk(out FieldChunk2D<T> chunk)
        {
            EnsureCapacity();
            for (int i = 0; i < _field.ChunkCount; i++)
            {
                FieldChunk2D<T> candidate = _field.GetChunkAt(i);
                if (candidate.ChangeStamp > _deliveredStamps[i])
                {
                    _deliveredStamps[i] = candidate.ChangeStamp;
                    chunk = candidate;
                    return true;
                }
            }

            chunk = null!;
            return false;
        }

        private void EnsureCapacity()
        {
            int chunkCount = _field.ChunkCount;
            if (_deliveredStamps.Length >= chunkCount)
            {
                return;
            }

            int next = Math.Max(4, _deliveredStamps.Length);
            while (next < chunkCount)
            {
                next *= 2;
            }

            Array.Resize(ref _deliveredStamps, next);
        }
    }
}
