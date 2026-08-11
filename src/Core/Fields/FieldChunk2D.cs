using System;

namespace Ludots.Core.Fields
{
    public sealed class FieldChunk2D<T>
        where T : struct
    {
        private readonly IFieldValueCodec<T> _codec;
        private readonly Array[] _channels;
        private readonly byte[] _dirtyMask;

        public FieldChunk2D(int chunkX, int chunkY, int chunkSizeCells, T defaultValue, IFieldValueCodec<T> codec)
        {
            if (chunkSizeCells <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizeCells));
            }

            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            ChunkX = chunkX;
            ChunkY = chunkY;
            int cellCount = chunkSizeCells * chunkSizeCells;
            CellCount = cellCount;
            _channels = _codec.CreateChannels(cellCount, defaultValue);
            DirtyLocals = new int[cellCount];
            _dirtyMask = new byte[cellCount];
        }

        public int ChunkX { get; }
        public int ChunkY { get; }
        public int CellCount { get; }
        public int ChannelCount => _codec.ChannelCount;
        public FieldChannelKind ChannelKind => _codec.ChannelKind;
        public int[] DirtyLocals { get; }
        public int DirtyCount { get; private set; }

        public T Get(int localIndex)
        {
            ValidateLocalIndex(localIndex);
            return _codec.Read(_channels, localIndex);
        }

        public void Set(int localIndex, T value)
        {
            ValidateLocalIndex(localIndex);
            _codec.Write(_channels, localIndex, value);
        }

        public ReadOnlySpan<float> GetFloatChannel(int channelIndex)
        {
            return _codec.GetFloatChannel(_channels, channelIndex);
        }

        public Span<float> GetMutableFloatChannel(int channelIndex)
        {
            if (_codec is not FloatFieldValueCodec floatCodec)
            {
                throw new InvalidOperationException("Mutable float channel requires scalar float field storage.");
            }

            return floatCodec.GetMutableFloatChannel(_channels, channelIndex);
        }

        public bool TryMarkDirty(int localIndex)
        {
            if (_dirtyMask[localIndex] != 0)
            {
                return false;
            }

            _dirtyMask[localIndex] = 1;
            DirtyLocals[DirtyCount++] = localIndex;
            return true;
        }

        public void ClearDirty()
        {
            for (int i = 0; i < DirtyCount; i++)
            {
                _dirtyMask[DirtyLocals[i]] = 0;
            }

            DirtyCount = 0;
        }

        private void ValidateLocalIndex(int localIndex)
        {
            if ((uint)localIndex >= (uint)CellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(localIndex));
            }
        }
    }
}
