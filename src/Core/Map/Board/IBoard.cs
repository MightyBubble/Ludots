using System;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Base interface for all Board types. A Board is a spatial domain within a Map.
    /// It provides spatial infrastructure (partition, queries, coordinate conversion)
    /// but has no knowledge of Triggers, Systems, or entity lifecycle.
    /// </summary>
    public interface IBoard : IDisposable
    {
        BoardId Id { get; }
        string Name { get; }
        WorldSizeSpec WorldSize { get; }
        ISpatialCoordinateConverter CoordinateConverter { get; }
        ISpatialPartitionWorld SpatialPartition { get; }
        ISpatialQueryService QueryService { get; }
        ILoadedChunks LoadedChunks { get; }
    }
}
