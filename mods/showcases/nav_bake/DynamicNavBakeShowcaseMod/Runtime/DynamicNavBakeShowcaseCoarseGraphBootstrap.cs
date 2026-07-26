using System;
using System.Collections.Generic;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Spatial;

namespace DynamicNavBakeShowcaseMod.Runtime;

internal static class DynamicNavBakeShowcaseCoarseGraphBootstrap
{
    public sealed class CoarseGraphState
    {
        public CoarseGraphState(LoadedGraphView fullView, long[] allChunkKeys, in NavTriangleSurfaceTileGrid grid)
        {
            FullView = fullView ?? throw new ArgumentNullException(nameof(fullView));
            AllChunkKeys = allChunkKeys ?? throw new ArgumentNullException(nameof(allChunkKeys));
            Grid = grid;
        }

        public LoadedGraphView FullView { get; }
        public long[] AllChunkKeys { get; }
        public NavTriangleSurfaceTileGrid Grid { get; }
        public int NodeCount => FullView.Graph.NodeCount;
    }

    public static CoarseGraphState BuildAndInstall(
        NodeGraphBoard board,
        DynamicNavBakeShowcaseConfig config,
        in NavTriangleSurfaceTileGrid grid)
    {
        if (board == null)
        {
            throw new ArgumentNullException(nameof(board));
        }

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        ValidateConfigMatchesGrid(config, grid);

        int widthChunks = grid.TileCountX;
        int heightChunks = grid.TileCountZ;
        var allChunkKeys = new long[checked(widthChunks * heightChunks)];
        int keyIndex = 0;
        for (int chunkZ = 0; chunkZ < heightChunks; chunkZ++)
        {
            for (int chunkX = 0; chunkX < widthChunks; chunkX++)
            {
                long chunkKey = GraphChunkKey.Pack(chunkX, chunkZ);
                allChunkKeys[keyIndex++] = chunkKey;
                GraphChunkData chunk = BuildSingleNodeChunk(chunkX, chunkZ, grid, widthChunks, heightChunks);
                board.GraphStore.AddOrReplace(chunkKey, chunk);
            }
        }

        LoadedGraphView fullView = board.GraphStore.BuildLoadedView(allChunkKeys);
        if (fullView.Graph.NodeCount != allChunkKeys.Length)
        {
            throw new InvalidOperationException(
                $"Coarse graph bootstrap expected {allChunkKeys.Length} nodes, got {fullView.Graph.NodeCount}.");
        }

        return new CoarseGraphState(fullView, allChunkKeys, grid);
    }

    internal static void ValidateConfigMatchesGrid(DynamicNavBakeShowcaseConfig config, in NavTriangleSurfaceTileGrid grid)
    {
        if (grid.TileCountX != config.WidthChunks || grid.TileCountZ != config.HeightChunks)
        {
            throw new InvalidOperationException(
                $"Open-world coarse graph requires NavTriangleSurfaceTileGrid tile counts {config.WidthChunks}x{config.HeightChunks}, " +
                $"got {grid.TileCountX}x{grid.TileCountZ}.");
        }

        if (grid.TileWidthCm != config.ChunkSizeCm || grid.TileHeightCm != config.ChunkSizeCm)
        {
            throw new InvalidOperationException(
                $"Open-world coarse graph requires NavTriangleSurfaceTileGrid tile size {config.ChunkSizeCm}x{config.ChunkSizeCm}cm, " +
                $"got {grid.TileWidthCm}x{grid.TileHeightCm}cm.");
        }

        int expectedOriginXcm = checked(-config.WorldWidthCm / 2);
        int expectedOriginZcm = checked(-config.WorldHeightCm / 2);
        if (grid.OriginXcm != expectedOriginXcm || grid.OriginZcm != expectedOriginZcm)
        {
            throw new InvalidOperationException(
                $"Open-world coarse graph requires centered NavTriangleSurfaceTileGrid origin ({expectedOriginXcm},{expectedOriginZcm})cm, " +
                $"got ({grid.OriginXcm},{grid.OriginZcm})cm.");
        }

        int expectedMaxXcm = checked(expectedOriginXcm + config.WorldWidthCm);
        int expectedMaxZcm = checked(expectedOriginZcm + config.WorldHeightCm);
        int gridMaxXcm = checked(grid.OriginXcm + checked(grid.TileCountX * grid.TileWidthCm));
        int gridMaxZcm = checked(grid.OriginZcm + checked(grid.TileCountZ * grid.TileHeightCm));
        if (gridMaxXcm != expectedMaxXcm || gridMaxZcm != expectedMaxZcm)
        {
            throw new InvalidOperationException(
                $"Open-world coarse graph requires centered extents " +
                $"[{expectedOriginXcm},{expectedMaxXcm}) x [{expectedOriginZcm},{expectedMaxZcm}), " +
                $"got [{grid.OriginXcm},{gridMaxXcm}) x [{grid.OriginZcm},{gridMaxZcm}).");
        }
    }

    internal static void ComputeResidentOriginForWorldPoint(
        in NavTriangleSurfaceTileGrid grid,
        int worldXCm,
        int worldZCm,
        int residentWidthChunks,
        int residentHeightChunks,
        out int originChunkX,
        out int originChunkZ)
    {
        if (residentWidthChunks <= 0 || residentHeightChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(residentWidthChunks),
                "Resident window dimensions must be positive.");
        }

        if (residentWidthChunks > grid.TileCountX || residentHeightChunks > grid.TileCountZ)
        {
            throw new InvalidOperationException(
                $"Resident window {residentWidthChunks}x{residentHeightChunks} exceeds grid " +
                $"{grid.TileCountX}x{grid.TileCountZ}.");
        }

        originChunkX = MathUtil.FloorDiv(checked(worldXCm - grid.OriginXcm), grid.TileWidthCm) - residentWidthChunks / 2;
        originChunkZ = MathUtil.FloorDiv(checked(worldZCm - grid.OriginZcm), grid.TileHeightCm) - residentHeightChunks / 2;
        originChunkX = Math.Clamp(originChunkX, 0, grid.TileCountX - residentWidthChunks);
        originChunkZ = Math.Clamp(originChunkZ, 0, grid.TileCountZ - residentHeightChunks);
    }

    internal static void ResolveWindowWorldBounds(
        in NavTriangleSurfaceTileGrid grid,
        ReadOnlySpan<NavBakeTileCoord> tiles,
        out int minX,
        out int minZ,
        out int maxX,
        out int maxZ)
    {
        if (tiles.Length <= 0)
        {
            minX = 0;
            minZ = 0;
            maxX = -1;
            maxZ = -1;
            return;
        }

        int minChunkX = int.MaxValue;
        int minChunkZ = int.MaxValue;
        int maxChunkX = int.MinValue;
        int maxChunkZ = int.MinValue;
        for (int i = 0; i < tiles.Length; i++)
        {
            NavBakeTileCoord tile = tiles[i];
            minChunkX = Math.Min(minChunkX, tile.ChunkX);
            minChunkZ = Math.Min(minChunkZ, tile.ChunkY);
            maxChunkX = Math.Max(maxChunkX, tile.ChunkX);
            maxChunkZ = Math.Max(maxChunkZ, tile.ChunkY);
        }

        minX = checked(grid.OriginXcm + checked(minChunkX * grid.TileWidthCm));
        minZ = checked(grid.OriginZcm + checked(minChunkZ * grid.TileHeightCm));
        maxX = checked(grid.OriginXcm + checked((maxChunkX + 1) * grid.TileWidthCm));
        maxZ = checked(grid.OriginZcm + checked((maxChunkZ + 1) * grid.TileHeightCm));
    }

    private static GraphChunkData BuildSingleNodeChunk(
        int chunkX,
        int chunkZ,
        in NavTriangleSurfaceTileGrid grid,
        int widthChunks,
        int heightChunks)
    {
        int centerX = checked(grid.OriginXcm + checked(chunkX * grid.TileWidthCm) + grid.TileWidthCm / 2);
        int centerY = checked(grid.OriginZcm + checked(chunkZ * grid.TileHeightCm) + grid.TileHeightCm / 2);
        int edgeCost = grid.TileWidthCm;
        var crossEdges = new List<GraphCrossEdge>(4);
        if (chunkX + 1 < widthChunks)
        {
            crossEdges.Add(new GraphCrossEdge(
                fromLocalNodeId: 0,
                toChunkKey: GraphChunkKey.Pack(chunkX + 1, chunkZ),
                toLocalNodeId: 0,
                baseCost: edgeCost,
                tagSetId: 0));
        }

        if (chunkX - 1 >= 0)
        {
            crossEdges.Add(new GraphCrossEdge(
                fromLocalNodeId: 0,
                toChunkKey: GraphChunkKey.Pack(chunkX - 1, chunkZ),
                toLocalNodeId: 0,
                baseCost: edgeCost,
                tagSetId: 0));
        }

        if (chunkZ + 1 < heightChunks)
        {
            crossEdges.Add(new GraphCrossEdge(
                fromLocalNodeId: 0,
                toChunkKey: GraphChunkKey.Pack(chunkX, chunkZ + 1),
                toLocalNodeId: 0,
                baseCost: edgeCost,
                tagSetId: 0));
        }

        if (chunkZ - 1 >= 0)
        {
            crossEdges.Add(new GraphCrossEdge(
                fromLocalNodeId: 0,
                toChunkKey: GraphChunkKey.Pack(chunkX, chunkZ - 1),
                toLocalNodeId: 0,
                baseCost: edgeCost,
                tagSetId: 0));
        }

        var builder = new NodeGraphBuilder(initialNodeCapacity: 1, initialEdgeCapacity: 0);
        builder.AddNode(centerX, centerY);
        return new GraphChunkData(builder.Build(), crossEdges.ToArray());
    }

    public static int FindNearestNodeId(CoarseGraphState state, int worldXCm, int worldYCm)
    {
        NodeGraph graph = state.FullView.Graph;
        if (graph.NodeCount <= 0)
        {
            throw new InvalidOperationException("Coarse graph has no nodes.");
        }

        int bestNode = 0;
        long bestDistSq = long.MaxValue;
        ReadOnlySpan<int> xs = graph.PosXcm;
        ReadOnlySpan<int> ys = graph.PosYcm;
        for (int i = 0; i < graph.NodeCount; i++)
        {
            long dx = xs[i] - worldXCm;
            long dy = ys[i] - worldYCm;
            long distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestNode = i;
            }
        }

        return bestNode;
    }
}
