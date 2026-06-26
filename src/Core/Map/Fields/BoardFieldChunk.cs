namespace Ludots.Core.Map.Fields
{
    public abstract class BoardFieldChunk<T> where T : struct
    {
        protected BoardFieldChunk(int cellCount)
        {
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            CellCount = cellCount;
        }

        public int CellCount { get; }

        internal bool Dirty { get; set; }

        public abstract T GetCell(int index);

        public abstract void SetCell(int index, T value);

        public abstract void Fill(T value);
    }

    public interface IBoardFieldChunkCodec<T> where T : struct
    {
        BoardFieldChunk<T> CreateChunk(int cellCount, T defaultValue);
    }
}
