using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Fields
{
    public interface IBoardField<T> where T : struct
    {
        int WidthCells { get; }

        int HeightCells { get; }

        int CellSizeCm { get; }

        int ChunkSizeCells { get; }

        int WidthChunks { get; }

        int HeightChunks { get; }

        T DefaultValue { get; }

        int ResidentChunkCount { get; }

        T GetCell(int col, int row);

        bool TryGetCell(int col, int row, out T value);

        void SetCell(int col, int row, T value);

        T SampleWorldCm(float worldXCm, float worldYCm);

        bool TrySampleWorldCm(float worldXCm, float worldYCm, out T value);

        bool IsChunkResident(int chunkX, int chunkY);

        bool IsChunkDirty(int chunkX, int chunkY);

        void ClearChunkDirty(int chunkX, int chunkY);

        void ClearDirty();

        bool RemoveChunk(int chunkX, int chunkY);

        void SubscribeToLoadedChunks(ILoadedChunks source);

        void UnsubscribeFromLoadedChunks();
    }
}
