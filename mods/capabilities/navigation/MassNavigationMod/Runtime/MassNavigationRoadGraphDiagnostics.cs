using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;

namespace MassNavigationMod.Runtime;

internal sealed class MassNavigationRoadGraphDiagnostics
{
    private readonly int _chunkSizeCm;
    private readonly Dictionary<long, GraphChunkData> _chunks = new();

    public MassNavigationRoadGraphDiagnostics(int chunkSizeCm)
    {
        if (chunkSizeCm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeCm));
        }

        _chunkSizeCm = chunkSizeCm;
    }

    public bool TryGetChunk(long chunkKey, out GraphChunkData chunk)
    {
        if (_chunks.TryGetValue(chunkKey, out chunk!))
        {
            return true;
        }

        (int chunkX, int chunkY) = GraphChunkKey.Unpack(chunkKey);
        chunk = BuildChunk(chunkX, chunkY);
        _chunks.Add(chunkKey, chunk);
        return true;
    }

    private GraphChunkData BuildChunk(int chunkX, int chunkY)
    {
        int originX = chunkX * _chunkSizeCm;
        int originY = chunkY * _chunkSizeCm;
        int centerX = originX + (_chunkSizeCm / 2);
        int centerY = originY + (_chunkSizeCm / 2);

        var builder = new NodeGraphBuilder(initialNodeCapacity: 5, initialEdgeCapacity: 16);
        int center = builder.AddNode(centerX, centerY);
        int west = builder.AddNode(originX, centerY);
        int east = builder.AddNode(originX + _chunkSizeCm, centerY);
        int north = builder.AddNode(centerX, originY);
        int south = builder.AddNode(centerX, originY + _chunkSizeCm);

        AddBidirectional(builder, center, west, _chunkSizeCm * 0.5f);
        AddBidirectional(builder, center, east, _chunkSizeCm * 0.5f);
        AddBidirectional(builder, center, north, _chunkSizeCm * 0.5f);
        AddBidirectional(builder, center, south, _chunkSizeCm * 0.5f);

        var crossEdges = new[]
        {
            new GraphCrossEdge((ushort)west, GraphChunkKey.Pack(chunkX - 1, chunkY), 2, _chunkSizeCm, 0),
            new GraphCrossEdge((ushort)east, GraphChunkKey.Pack(chunkX + 1, chunkY), 1, _chunkSizeCm, 0),
            new GraphCrossEdge((ushort)north, GraphChunkKey.Pack(chunkX, chunkY - 1), 4, _chunkSizeCm, 0),
            new GraphCrossEdge((ushort)south, GraphChunkKey.Pack(chunkX, chunkY + 1), 3, _chunkSizeCm, 0),
        };

        return new GraphChunkData(builder.Build(), crossEdges);
    }

    private static void AddBidirectional(NodeGraphBuilder builder, int a, int b, float cost)
    {
        builder.AddEdge(a, b, cost);
        builder.AddEdge(b, a, cost);
    }
}
