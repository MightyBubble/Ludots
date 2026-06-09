using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Diagnostics;

namespace MassNavigationMod.Runtime;

public readonly record struct MassNavigationHpaGraphAssetDiagnostics(
    bool Available,
    int ActiveWindowMinChunkX,
    int ActiveWindowMinChunkY,
    int ActiveWindowMaxChunkX,
    int ActiveWindowMaxChunkY,
    int ActiveWindowChunkCount,
    int LoadedTileCount,
    int PortalCount,
    int IntraTileEdgeCount,
    int CrossTileEdgeCount,
    int GraphNodeCount,
    int GraphEdgeCount,
    bool ActiveWindowRouteAvailable,
    int ActiveWindowRoutePortalCount,
    int ActiveWindowRouteCrossTileStepCount,
    int RouteStartChunkX,
    int RouteStartChunkY,
    int RouteGoalChunkX,
    int RouteGoalChunkY,
    int RouteStartPortalIndex,
    int RouteGoalPortalIndex,
    string RouteSignature,
    string Source,
    string Gap);

internal static class MassNavigationHpaGraphDiagnosticsBuilder
{
    private const int MaxDiagnosticTiles = 289;

    public static MassNavigationHpaGraphAssetDiagnostics Build(
        IVirtualFileSystem vfs,
        IEnumerable<string>? loadedModIds,
        string mapId,
        NavMeshBakeConfig navMeshConfig,
        NavBakeDiagnosticsDocument? diagnostics)
    {
        if (vfs == null) throw new ArgumentNullException(nameof(vfs));
        if (string.IsNullOrWhiteSpace(mapId)) throw new ArgumentException("mapId is required.", nameof(mapId));

        NavBakeLayerProfileSummary? profile = ResolveFirstCompleteProfile(diagnostics);
        if (profile == null)
        {
            return Unavailable("nav_bake_diagnostics_missing_or_incomplete");
        }

        int minX = diagnostics!.ActiveWindowMinChunkX >= 0 ? diagnostics.ActiveWindowMinChunkX : 0;
        int minY = diagnostics.ActiveWindowMinChunkY >= 0 ? diagnostics.ActiveWindowMinChunkY : 0;
        int maxX = diagnostics.ActiveWindowMaxChunkX >= minX ? diagnostics.ActiveWindowMaxChunkX : minX;
        int maxY = diagnostics.ActiveWindowMaxChunkY >= minY ? diagnostics.ActiveWindowMaxChunkY : minY;
        (int X, int Y)[] sampleTiles = BuildDiagnosticTileCoordinates(minX, minY, maxX, maxY, MaxDiagnosticTiles);

        var tiles = new Dictionary<(int X, int Y), NavTile>();
        int loaded = 0;
        int portals = 0;
        int intraEdges = 0;
        var crossEdges = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < sampleTiles.Length; i++)
        {
            (int cx, int cy) = sampleTiles[i];
            if (!TryLoadTile(vfs, loadedModIds, mapId, profile.Layer, profile.ProfileId, cx, cy, out NavTile? loadedTile))
            {
                continue;
            }

            NavTile tile = loadedTile ?? throw new InvalidOperationException("TryLoadTile returned true without a NavTile.");
            loaded++;
            if (tile.TriangleCount <= 0 || tile.Portals.Length <= 0)
            {
                continue;
            }

            tiles[(cx, cy)] = tile;
            portals += tile.Portals.Length;
            intraEdges += Math.Max(0, tile.Portals.Length * Math.Max(0, tile.Portals.Length - 1));
        }

        foreach (NavTile tile in tiles.Values)
        {
            for (int i = 0; i < tile.Portals.Length; i++)
            {
                NavBorderPortal portal = tile.Portals[i];
                (int nx, int ny) = ResolveNeighbor(tile.TileId.ChunkX, tile.TileId.ChunkY, portal.Side);
                if (!tiles.TryGetValue((nx, ny), out NavTile? neighbor))
                {
                    continue;
                }

                for (int n = 0; n < neighbor.Portals.Length; n++)
                {
                    if (IsMatchingPortal(portal, neighbor.Portals[n]))
                    {
                        AddUndirectedCrossEdge(crossEdges, tile.TileId.ChunkX, tile.TileId.ChunkY, i, nx, ny, n);
                        break;
                    }
                }
            }
        }

        BuildPortalGraph(tiles, out List<PortalGraphNode> graphNodes, out List<int>[] adjacency);
        List<int> route = TryBuildActiveWindowRoute(graphNodes, adjacency, minX, minY, maxX, maxY);
        bool routeAvailable = route.Count > 1;
        int routeStartIndex = routeAvailable ? route[0] : -1;
        int routeGoalIndex = routeAvailable ? route[^1] : -1;
        PortalGraphNode routeStart = routeStartIndex >= 0 ? graphNodes[routeStartIndex] : default;
        PortalGraphNode routeGoal = routeGoalIndex >= 0 ? graphNodes[routeGoalIndex] : default;

        bool available = loaded > 0 && portals > 0;
        return new MassNavigationHpaGraphAssetDiagnostics(
            Available: available,
            ActiveWindowMinChunkX: minX,
            ActiveWindowMinChunkY: minY,
            ActiveWindowMaxChunkX: maxX,
            ActiveWindowMaxChunkY: maxY,
            ActiveWindowChunkCount: Math.Max(0, diagnostics.ActiveWindowChunkCount),
            LoadedTileCount: loaded,
            PortalCount: portals,
            IntraTileEdgeCount: intraEdges,
            CrossTileEdgeCount: crossEdges.Count,
            GraphNodeCount: portals,
            GraphEdgeCount: intraEdges + crossEdges.Count,
            ActiveWindowRouteAvailable: routeAvailable,
            ActiveWindowRoutePortalCount: route.Count,
            ActiveWindowRouteCrossTileStepCount: CountCrossTileSteps(graphNodes, route),
            RouteStartChunkX: routeAvailable ? routeStart.ChunkX : -1,
            RouteStartChunkY: routeAvailable ? routeStart.ChunkY : -1,
            RouteGoalChunkX: routeAvailable ? routeGoal.ChunkX : -1,
            RouteGoalChunkY: routeAvailable ? routeGoal.ChunkY : -1,
            RouteStartPortalIndex: routeAvailable ? routeStart.PortalIndex : -1,
            RouteGoalPortalIndex: routeAvailable ? routeGoal.PortalIndex : -1,
            RouteSignature: routeAvailable ? BuildRouteSignature(graphNodes, route) : string.Empty,
            Source: routeAvailable
                ? (sampleTiles.Length < Math.Max(0, diagnostics.ActiveWindowChunkCount) ? "navtile_portal_graph_sampled_window_route" : "navtile_portal_graph_active_window_route")
                : (available ? "navtile_portal_graph_sampled_window" : "navtile_portal_graph_unavailable"),
            Gap: sampleTiles.Length < Math.Max(0, diagnostics.ActiveWindowChunkCount)
                ? "full_world_hpa_diagnostics_sampled_to_avoid_startup_full_tile_scan"
                : (loaded == Math.Max(0, diagnostics.WorldChunkCount)
                    ? "full_world_hpa_route_asset_not_persisted"
                    : "active_window_hpa_graph_route_passed_streaming_contract"));
    }

    private static (int X, int Y)[] BuildDiagnosticTileCoordinates(
        int minX,
        int minY,
        int maxX,
        int maxY,
        int maxTiles)
    {
        if (maxTiles <= 0 || maxX < minX || maxY < minY)
        {
            return Array.Empty<(int X, int Y)>();
        }

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        long total = (long)width * height;
        var result = new List<(int X, int Y)>(Math.Min(maxTiles, (int)Math.Min(total, int.MaxValue)));
        var keys = new HashSet<long>();
        if (total <= maxTiles)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    AddDiagnosticTile(result, keys, x, y, maxTiles);
                }
            }

            return result.ToArray();
        }

        int axisSamples = Math.Max(1, (int)MathF.Floor(MathF.Sqrt(maxTiles)));
        int xSamples = Math.Clamp(axisSamples, 1, width);
        int ySamples = Math.Clamp(Math.Max(1, maxTiles / xSamples), 1, height);
        for (int yIndex = 0; yIndex < ySamples; yIndex++)
        {
            int y = SampleAxis(minY, maxY, ySamples, yIndex);
            for (int xIndex = 0; xIndex < xSamples; xIndex++)
            {
                int x = SampleAxis(minX, maxX, xSamples, xIndex);
                AddDiagnosticTile(result, keys, x, y, maxTiles);
            }
        }

        return result.ToArray();
    }

    private static int SampleAxis(int min, int max, int sampleCount, int sampleIndex)
    {
        if (sampleCount <= 1 || max <= min)
        {
            return min + ((max - min) / 2);
        }

        float t = sampleIndex / (float)(sampleCount - 1);
        return Math.Clamp(min + (int)MathF.Round((max - min) * t), min, max);
    }

    private static void AddDiagnosticTile(
        List<(int X, int Y)> result,
        HashSet<long> keys,
        int x,
        int y,
        int maxTiles)
    {
        if (result.Count >= maxTiles)
        {
            return;
        }

        long key = (((long)x) << 32) ^ (uint)y;
        if (keys.Add(key))
        {
            result.Add((x, y));
        }
    }

    public static MassNavigationHpaGraphAssetDiagnostics Unavailable(string gap)
    {
        return new MassNavigationHpaGraphAssetDiagnostics(
            Available: false,
            ActiveWindowMinChunkX: -1,
            ActiveWindowMinChunkY: -1,
            ActiveWindowMaxChunkX: -1,
            ActiveWindowMaxChunkY: -1,
            ActiveWindowChunkCount: 0,
            LoadedTileCount: 0,
            PortalCount: 0,
            IntraTileEdgeCount: 0,
            CrossTileEdgeCount: 0,
            GraphNodeCount: 0,
            GraphEdgeCount: 0,
            ActiveWindowRouteAvailable: false,
            ActiveWindowRoutePortalCount: 0,
            ActiveWindowRouteCrossTileStepCount: 0,
            RouteStartChunkX: -1,
            RouteStartChunkY: -1,
            RouteGoalChunkX: -1,
            RouteGoalChunkY: -1,
            RouteStartPortalIndex: -1,
            RouteGoalPortalIndex: -1,
            RouteSignature: string.Empty,
            Source: "not_bound",
            Gap: string.IsNullOrWhiteSpace(gap) ? "hpa_graph_diagnostics_not_bound" : gap);
    }

    private static NavBakeLayerProfileSummary? ResolveFirstCompleteProfile(NavBakeDiagnosticsDocument? diagnostics)
    {
        if (diagnostics?.LayerProfiles == null || diagnostics.LayerProfiles.Count == 0)
        {
            return null;
        }

        for (int i = 0; i < diagnostics.LayerProfiles.Count; i++)
        {
            NavBakeLayerProfileSummary profile = diagnostics.LayerProfiles[i];
            if (profile.BakedTiles > 0 && string.Equals(profile.ProfileId, "GroundLight", StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        for (int i = 0; i < diagnostics.LayerProfiles.Count; i++)
        {
            NavBakeLayerProfileSummary profile = diagnostics.LayerProfiles[i];
            if (profile.BakedTiles > 0 && !string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                return profile;
            }
        }

        return null;
    }

    private static bool TryLoadTile(
        IVirtualFileSystem vfs,
        IEnumerable<string>? loadedModIds,
        string mapId,
        int layer,
        string profileId,
        int chunkX,
        int chunkY,
        out NavTile? tile)
    {
        tile = null;
        string relativePath = NavAssetPaths.GetNavTileRelativePath(mapId, layer, profileId, chunkX, chunkY);
        foreach (string uri in EnumerateCandidateUris(loadedModIds, relativePath))
        {
            if (!vfs.TryResolveFullPath(uri, out string fullPath) || !File.Exists(fullPath))
            {
                continue;
            }

            using Stream stream = vfs.GetStream(uri);
            tile = NavTileBinary.Read(stream);
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCandidateUris(IEnumerable<string>? loadedModIds, string relativePath)
    {
        yield return $"Core:{relativePath}";
        if (TryStripAssetsPrefix(relativePath, out string coreRelativePath))
        {
            yield return $"Core:{coreRelativePath}";
        }

        if (loadedModIds == null)
        {
            yield break;
        }

        foreach (string modId in loadedModIds)
        {
            if (!string.IsNullOrWhiteSpace(modId))
            {
                yield return $"{modId}:{relativePath}";
            }
        }
    }

    private static bool TryStripAssetsPrefix(string relativePath, out string stripped)
    {
        stripped = string.Empty;
        const string prefix = "assets/";
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        stripped = normalized[prefix.Length..];
        return stripped.Length > 0;
    }

    private static (int X, int Y) ResolveNeighbor(int x, int y, NavPortalSide side)
    {
        return side switch
        {
            NavPortalSide.West => (x - 1, y),
            NavPortalSide.East => (x + 1, y),
            NavPortalSide.North => (x, y - 1),
            NavPortalSide.South => (x, y + 1),
            _ => (x, y),
        };
    }

    private static bool IsMatchingPortal(NavBorderPortal a, NavBorderPortal b)
    {
        return a.LeftXcm == b.LeftXcm &&
            a.LeftZcm == b.LeftZcm &&
            a.RightXcm == b.RightXcm &&
            a.RightZcm == b.RightZcm;
    }

    private static void BuildPortalGraph(
        Dictionary<(int X, int Y), NavTile> tiles,
        out List<PortalGraphNode> nodes,
        out List<int>[] adjacency)
    {
        nodes = new List<PortalGraphNode>();
        var indexByKey = new Dictionary<PortalGraphNodeKey, int>();
        foreach (NavTile tile in tiles.Values)
        {
            for (int i = 0; i < tile.Portals.Length; i++)
            {
                var key = new PortalGraphNodeKey(tile.TileId.ChunkX, tile.TileId.ChunkY, i);
                indexByKey[key] = nodes.Count;
                nodes.Add(new PortalGraphNode(tile.TileId.ChunkX, tile.TileId.ChunkY, i, tile.Portals[i]));
            }
        }

        adjacency = new List<int>[nodes.Count];
        for (int i = 0; i < adjacency.Length; i++)
        {
            adjacency[i] = new List<int>();
        }

        foreach (NavTile tile in tiles.Values)
        {
            for (int i = 0; i < tile.Portals.Length; i++)
            {
                var a = new PortalGraphNodeKey(tile.TileId.ChunkX, tile.TileId.ChunkY, i);
                if (!indexByKey.TryGetValue(a, out int aIndex))
                {
                    continue;
                }

                for (int j = i + 1; j < tile.Portals.Length; j++)
                {
                    var b = new PortalGraphNodeKey(tile.TileId.ChunkX, tile.TileId.ChunkY, j);
                    if (indexByKey.TryGetValue(b, out int bIndex))
                    {
                        AddUndirectedAdjacency(adjacency, aIndex, bIndex);
                    }
                }

                NavBorderPortal portal = tile.Portals[i];
                (int nx, int ny) = ResolveNeighbor(tile.TileId.ChunkX, tile.TileId.ChunkY, portal.Side);
                if (!tiles.TryGetValue((nx, ny), out NavTile? neighbor))
                {
                    continue;
                }

                for (int n = 0; n < neighbor.Portals.Length; n++)
                {
                    if (!IsMatchingPortal(portal, neighbor.Portals[n]))
                    {
                        continue;
                    }

                    var b = new PortalGraphNodeKey(nx, ny, n);
                    if (indexByKey.TryGetValue(b, out int bIndex))
                    {
                        AddUndirectedAdjacency(adjacency, aIndex, bIndex);
                    }

                    break;
                }
            }
        }
    }

    private static List<int> TryBuildActiveWindowRoute(
        List<PortalGraphNode> nodes,
        List<int>[] adjacency,
        int minX,
        int minY,
        int maxX,
        int maxY)
    {
        if (nodes.Count < 2 || adjacency.Length != nodes.Count)
        {
            return new List<int>();
        }

        int start = FindClosestPortalNode(nodes, minX, minY);
        int goal = FindClosestPortalNode(nodes, maxX, maxY);
        if (start < 0 || goal < 0)
        {
            return new List<int>();
        }

        if (start == goal)
        {
            goal = FindFarthestPortalNode(nodes, start);
            if (goal < 0 || goal == start)
            {
                return new List<int>();
            }
        }

        var previous = new int[nodes.Count];
        var visited = new bool[nodes.Count];
        Array.Fill(previous, -1);

        var queue = new Queue<int>();
        visited[start] = true;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (current == goal)
            {
                break;
            }

            List<int> neighbors = adjacency[current];
            for (int i = 0; i < neighbors.Count; i++)
            {
                int next = neighbors[i];
                if (visited[next])
                {
                    continue;
                }

                visited[next] = true;
                previous[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!visited[goal])
        {
            return new List<int>();
        }

        var route = new List<int>();
        for (int at = goal; at >= 0; at = previous[at])
        {
            route.Add(at);
            if (at == start)
            {
                break;
            }
        }

        route.Reverse();
        return route;
    }

    private static int FindClosestPortalNode(List<PortalGraphNode> nodes, int targetX, int targetY)
    {
        int best = -1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < nodes.Count; i++)
        {
            PortalGraphNode node = nodes[i];
            int distance = Math.Abs(node.ChunkX - targetX) + Math.Abs(node.ChunkY - targetY);
            if (distance < bestDistance ||
                (distance == bestDistance && IsEarlierNode(node, nodes[best])))
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int FindFarthestPortalNode(List<PortalGraphNode> nodes, int startIndex)
    {
        if (startIndex < 0 || startIndex >= nodes.Count)
        {
            return -1;
        }

        PortalGraphNode start = nodes[startIndex];
        int best = -1;
        int bestDistance = -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (i == startIndex)
            {
                continue;
            }

            PortalGraphNode node = nodes[i];
            int distance = Math.Abs(node.ChunkX - start.ChunkX) + Math.Abs(node.ChunkY - start.ChunkY);
            if (distance > bestDistance ||
                (distance == bestDistance && (best < 0 || IsEarlierNode(node, nodes[best]))))
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static bool IsEarlierNode(PortalGraphNode candidate, PortalGraphNode incumbent)
    {
        if (candidate.ChunkY != incumbent.ChunkY)
        {
            return candidate.ChunkY < incumbent.ChunkY;
        }

        if (candidate.ChunkX != incumbent.ChunkX)
        {
            return candidate.ChunkX < incumbent.ChunkX;
        }

        return candidate.PortalIndex < incumbent.PortalIndex;
    }

    private static void AddUndirectedAdjacency(List<int>[] adjacency, int a, int b)
    {
        if (a == b)
        {
            return;
        }

        if (!adjacency[a].Contains(b))
        {
            adjacency[a].Add(b);
        }

        if (!adjacency[b].Contains(a))
        {
            adjacency[b].Add(a);
        }
    }

    private static int CountCrossTileSteps(List<PortalGraphNode> nodes, List<int> route)
    {
        int count = 0;
        for (int i = 1; i < route.Count; i++)
        {
            PortalGraphNode a = nodes[route[i - 1]];
            PortalGraphNode b = nodes[route[i]];
            if (a.ChunkX != b.ChunkX || a.ChunkY != b.ChunkY)
            {
                count++;
            }
        }

        return count;
    }

    private static string BuildRouteSignature(List<PortalGraphNode> nodes, List<int> route)
    {
        if (route.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < route.Count; i++)
        {
            if (i > 0)
            {
                sb.Append("->");
            }

            PortalGraphNode node = nodes[route[i]];
            sb.Append(node.ChunkX);
            sb.Append(',');
            sb.Append(node.ChunkY);
            sb.Append(':');
            sb.Append(node.PortalIndex);
        }

        return sb.ToString();
    }

    private static void AddUndirectedCrossEdge(
        HashSet<string> edges,
        int ax,
        int ay,
        int ai,
        int bx,
        int by,
        int bi)
    {
        string a = $"{ax},{ay}:{ai}";
        string b = $"{bx},{by}:{bi}";
        edges.Add(string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}");
    }

    private readonly record struct PortalGraphNodeKey(int ChunkX, int ChunkY, int PortalIndex);
    private readonly record struct PortalGraphNode(int ChunkX, int ChunkY, int PortalIndex, NavBorderPortal Portal);
}
