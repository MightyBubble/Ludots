namespace Ludots.Platform.Abstractions
{
    public enum ContinuousHeightmapStorageLayout : byte
    {
        None = 0,
        RowMajorInt16Centimeters = 1,
        RowMajorUInt16Scaled = 2,
        ChunkedRowMajorInt16Centimeters = 3,
        ChunkedRowMajorUInt16Scaled = 4,
    }
}
