using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Navigation.NavMesh;

namespace MassNavigationMod.Runtime;

public readonly record struct MassNavigationRuntimeWorldPathEndpointResult(
    Vector2 StartWorldCm,
    Vector2 GoalWorldCm,
    int StartChunkX,
    int StartChunkY,
    int GoalChunkX,
    int GoalChunkY,
    int MacroRouteChunkCount,
    int ComponentTileCount,
    string Source);

internal static class MassNavigationRuntimeWorldPathEndpointResolver
{
    private const int MaxEndpointComponentCandidates = 32;
    private const int MaxEndpointSamplesPerTile = 16;
    private const int MaxCentroidEndpointSamplesPerTile = 6;
    private const int MaxTileComponentEndpointVerificationAttempts = 32;
    private const int PortalGraphSampleDenominator = 16;

    public static bool TryRevalidate(
        MassNavigationBakeDataDiagnostics? diagnostics,
        NavQueryServiceRegistry? navRegistry,
        NavMeshProfileRegistry? navProfiles,
        MassNavigationRuntimeWorldPathEndpointResult cached,
        out MassNavigationRuntimeWorldPathEndpointResult result)
    {
        result = default;
        if (diagnostics == null ||
            navRegistry == null ||
            navProfiles == null ||
            cached.MacroRouteChunkCount <= 0 ||
            !TryResolveStore(diagnostics, navRegistry, navProfiles, out int layer, out int profileIndex, out string profileId, out NavTileStore store))
        {
            return false;
        }

        var query = new NavQueryService(store, layer, NavAreaCostTable.CreateDefault());
        int maxPortals = Math.Clamp(Math.Max(16_384, cached.MacroRouteChunkCount * 64), 16_384, 262_144);
        NavPathResult path = query.TryFindPath(
            (int)MathF.Round(cached.StartWorldCm.X),
            (int)MathF.Round(cached.StartWorldCm.Y),
            (int)MathF.Round(cached.GoalWorldCm.X),
            (int)MathF.Round(cached.GoalWorldCm.Y),
            maxPortals);
        if (path.Status != NavPathStatus.Ok || path.PathXcm.Length < 2)
        {
            return false;
        }

        result = cached with
        {
            Source = $"runtime_navmesh_cached_endpoint_revalidated:layer={layer};profile={profileId};profileIndex={profileIndex};routeChunks={cached.MacroRouteChunkCount};componentTiles={cached.ComponentTileCount};verified=NavQueryService;revision={store.Revision};previous={cached.Source}"
        };
        return true;
    }

    public static bool TryResolve(
        MassNavigationBakeDataDiagnostics? diagnostics,
        NavQueryServiceRegistry? navRegistry,
        NavMeshProfileRegistry? navProfiles,
        out MassNavigationRuntimeWorldPathEndpointResult result)
    {
        result = default;
        if (diagnostics == null ||
            navRegistry == null ||
            navProfiles == null ||
            diagnostics.MacroChunkColumns <= 0 ||
            diagnostics.MacroChunkRows <= 0)
        {
            return false;
        }

        if (!TryResolveStore(diagnostics, navRegistry, navProfiles, out int layer, out int profileIndex, out string profileId, out NavTileStore store))
        {
            return false;
        }

        int minX = diagnostics.HasActiveNavMeshWindow ? diagnostics.ActiveNavMeshMinChunkX : 0;
        int minY = diagnostics.HasActiveNavMeshWindow ? diagnostics.ActiveNavMeshMinChunkY : 0;
        int maxX = diagnostics.HasActiveNavMeshWindow ? diagnostics.ActiveNavMeshMaxChunkX : diagnostics.MacroChunkColumns - 1;
        int maxY = diagnostics.HasActiveNavMeshWindow ? diagnostics.ActiveNavMeshMaxChunkY : diagnostics.MacroChunkRows - 1;
        minX = Math.Clamp(minX, 0, diagnostics.MacroChunkColumns - 1);
        minY = Math.Clamp(minY, 0, diagnostics.MacroChunkRows - 1);
        maxX = Math.Clamp(maxX, minX, diagnostics.MacroChunkColumns - 1);
        maxY = Math.Clamp(maxY, minY, diagnostics.MacroChunkRows - 1);

        List<PortalComponentCandidate> candidates = BuildPortalComponentCandidates(
            diagnostics,
            store,
            layer,
            minX,
            minY,
            maxX,
            maxY);

        var query = new NavQueryService(store, layer, NavAreaCostTable.CreateDefault());
        int minimumFullWorldRoute = ResolveMinimumFullWorldRouteChunkCount(minX, minY, maxX, maxY);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].MacroRouteChunkCount < minimumFullWorldRoute)
            {
                break;
            }

            if (TryResolveVerifiedEndpointPair(
                    store,
                    query,
                    candidates[i],
                    layer,
                    profileId,
                    profileIndex,
                    out result))
            {
                return true;
            }
        }

        List<ComponentCandidate> tileCandidates = BuildTileComponentCandidates(
            store,
            layer,
            minX,
            minY,
            maxX,
            maxY);
        for (int i = 0; i < tileCandidates.Count; i++)
        {
            if (tileCandidates[i].MacroRouteChunkCount < minimumFullWorldRoute)
            {
                break;
            }

            if (TryResolveVerifiedEndpointPair(
                    diagnostics,
                    store,
                    query,
                    tileCandidates[i],
                    layer,
                    profileId,
                    profileIndex,
                    out result))
            {
                return true;
            }
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (TryResolveVerifiedEndpointPair(
                    store,
                    query,
                    candidates[i],
                    layer,
                    profileId,
                    profileIndex,
                    out result))
            {
                return true;
            }
        }

        result = default;
        return false;
    }

    private static int ResolveMinimumFullWorldRouteChunkCount(int minX, int minY, int maxX, int maxY)
    {
        int fullRoute = Math.Max(1, (maxX - minX) + (maxY - minY) + 1);
        return Math.Max(1, (fullRoute * 4) / 5);
    }

    private static List<ComponentCandidate> BuildTileComponentCandidates(
        NavTileStore store,
        int layer,
        int minX,
        int minY,
        int maxX,
        int maxY)
    {
        int width = Math.Max(0, maxX - minX + 1);
        int height = Math.Max(0, maxY - minY + 1);
        if (width == 0 || height == 0)
        {
            return new List<ComponentCandidate>(0);
        }

        var candidates = new List<ComponentCandidate>(MaxEndpointComponentCandidates);
        var visited = new bool[checked(width * height)];
        var queue = new Queue<NavTileId>(4096);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int index = IndexOf(x, y, minX, minY, width);
                if (visited[index])
                {
                    continue;
                }

                visited[index] = true;
                var seed = new NavTileId(x, y, layer);
                if (!TryLoadTraversableTile(store, seed, out _))
                {
                    continue;
                }

                ComponentCandidate component = FloodComponent(
                    store,
                    layer,
                    minX,
                    minY,
                    maxX,
                    maxY,
                    width,
                    visited,
                    queue,
                    seed);
                component.FinalizeRoute();
                InsertComponentCandidate(candidates, component);
            }
        }

        return candidates;
    }

    private static void InsertComponentCandidate(List<ComponentCandidate> candidates, ComponentCandidate component)
    {
        if (!component.Available || component.StartChunk.Equals(component.GoalChunk))
        {
            return;
        }

        int insertAt = candidates.Count;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (component.IsBetterThan(candidates[i]))
            {
                insertAt = i;
                break;
            }
        }

        if (insertAt >= MaxEndpointComponentCandidates)
        {
            return;
        }

        candidates.Insert(insertAt, component);
        if (candidates.Count > MaxEndpointComponentCandidates)
        {
            candidates.RemoveAt(candidates.Count - 1);
        }
    }

    private static bool TryResolveVerifiedEndpointPair(
        NavTileStore store,
        NavQueryService query,
        PortalComponentCandidate component,
        int layer,
        string profileId,
        int profileIndex,
        out MassNavigationRuntimeWorldPathEndpointResult result)
    {
        result = default;
        var pairs = new List<PortalEndpointPair>(8);
        component.AddEndpointPairs(pairs);

        for (int i = 0; i < pairs.Count; i++)
        {
            PortalEndpointPair pair = pairs[i];
            if (pair.Start.NodeIndex == pair.Goal.NodeIndex)
            {
                continue;
            }

            int maxPortals = Math.Clamp(Math.Max(16_384, pair.RouteChunkCount * 64), 16_384, 262_144);
            NavPathResult path = query.TryFindPath(
                (int)MathF.Round(pair.Start.WorldCm.X),
                (int)MathF.Round(pair.Start.WorldCm.Y),
                (int)MathF.Round(pair.Goal.WorldCm.X),
                (int)MathF.Round(pair.Goal.WorldCm.Y),
                maxPortals);
            if (path.Status != NavPathStatus.Ok || path.PathXcm.Length < 2)
            {
                continue;
            }

            result = new MassNavigationRuntimeWorldPathEndpointResult(
                pair.Start.WorldCm,
                pair.Goal.WorldCm,
                pair.Start.TileId.ChunkX,
                pair.Start.TileId.ChunkY,
                pair.Goal.TileId.ChunkX,
                pair.Goal.TileId.ChunkY,
                pair.RouteChunkCount,
                component.TileCount,
                $"runtime_navmesh_portal_component_endpoint:layer={layer};profile={profileId};profileIndex={profileIndex};tiles={component.TileCount};nodes={component.NodeCount};routeChunks={pair.RouteChunkCount};bounds={component.MinX},{component.MinY}->{component.MaxX},{component.MaxY};startPoint={pair.Start.Source};goalPoint={pair.Goal.Source};verified=NavQueryService;revision={store.Revision}");
            return true;
        }

        return false;
    }

    private static bool TryResolveVerifiedEndpointPair(
        MassNavigationBakeDataDiagnostics diagnostics,
        NavTileStore store,
        NavQueryService query,
        ComponentCandidate component,
        int layer,
        string profileId,
        int profileIndex,
        out MassNavigationRuntimeWorldPathEndpointResult result)
    {
        result = default;
        var pairs = new List<RouteEndpointPair>(8);
        component.AddEndpointPairs(pairs);
        int attempts = 0;

        for (int i = 0; i < pairs.Count; i++)
        {
            RouteEndpointPair pair = pairs[i];
            EndpointSample[] startSamples = ResolveEndpointSamples(diagnostics, store, component, pair.Start);
            EndpointSample[] goalSamples = ResolveEndpointSamples(diagnostics, store, component, pair.Goal);
            if (startSamples.Length == 0 || goalSamples.Length == 0)
            {
                continue;
            }

            for (int s = 0; s < startSamples.Length && attempts < MaxTileComponentEndpointVerificationAttempts; s++)
            {
                EndpointSample start = startSamples[s];
                for (int g = 0; g < goalSamples.Length && attempts < MaxTileComponentEndpointVerificationAttempts; g++)
                {
                    EndpointSample goal = goalSamples[g];
                    attempts++;
                    int maxPortals = Math.Clamp(Math.Max(16_384, pair.RouteChunkCount * 64), 16_384, 262_144);
                    NavPathResult path = query.TryFindPath(
                        (int)MathF.Round(start.WorldCm.X),
                        (int)MathF.Round(start.WorldCm.Y),
                        (int)MathF.Round(goal.WorldCm.X),
                        (int)MathF.Round(goal.WorldCm.Y),
                        maxPortals);
                    if (path.Status != NavPathStatus.Ok || path.PathXcm.Length < 2)
                    {
                        continue;
                    }

                    result = new MassNavigationRuntimeWorldPathEndpointResult(
                        start.WorldCm,
                        goal.WorldCm,
                        pair.Start.ChunkX,
                        pair.Start.ChunkY,
                        pair.Goal.ChunkX,
                        pair.Goal.ChunkY,
                        pair.RouteChunkCount,
                        component.TileCount,
                        $"runtime_navmesh_tile_component_endpoint:layer={layer};profile={profileId};profileIndex={profileIndex};tiles={component.TileCount};routeChunks={pair.RouteChunkCount};bounds={component.MinX},{component.MinY}->{component.MaxX},{component.MaxY};startPoint={start.Source};goalPoint={goal.Source};attempts={attempts};verified=NavQueryService;revision={store.Revision}");
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveStore(
        MassNavigationBakeDataDiagnostics diagnostics,
        NavQueryServiceRegistry navRegistry,
        NavMeshProfileRegistry navProfiles,
        out int layer,
        out int profileIndex,
        out string profileId,
        out NavTileStore store)
    {
        for (int i = 0; i < diagnostics.Profiles.Length; i++)
        {
            MassNavigationBakeDataProfileSummary profile = diagnostics.Profiles[i];
            if (string.IsNullOrWhiteSpace(profile.NavProfileId) ||
                !navProfiles.TryGetIndex(profile.NavProfileId, out profileIndex))
            {
                continue;
            }

            if (navRegistry.TryGetStore(profile.Layer, profileIndex, out store))
            {
                layer = profile.Layer;
                profileId = profile.NavProfileId;
                return true;
            }
        }

        layer = 0;
        profileIndex = -1;
        profileId = string.Empty;
        store = null!;
        return false;
    }

    private static List<PortalComponentCandidate> BuildPortalComponentCandidates(
        MassNavigationBakeDataDiagnostics diagnostics,
        NavTileStore store,
        int layer,
        int minX,
        int minY,
        int maxX,
        int maxY)
    {
        var nodes = new List<PortalGraphNode>(8192);
        var adjacency = new List<List<int>>(8192);
        var nodeIndexByKey = new Dictionary<TilePortalComponentKey, int>(8192);
        var triangleComponentsByTile = new Dictionary<NavTileId, int[]>(8192);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                NavTileId tileId = new(x, y, layer);
                if (!TryLoadTraversableTile(store, tileId, out NavTile tile) ||
                    !TryCreateMapper(diagnostics, tile, out MassNavigationNavMeshRuntimeCoordinateMapper mapper))
                {
                    continue;
                }

                int[] triangleComponents = BuildTriangleComponents(tile);
                triangleComponentsByTile[tile.TileId] = triangleComponents;
                int firstNode = nodes.Count;
                for (int portalIndex = 0; portalIndex < tile.Portals.Length; portalIndex++)
                {
                    AddPortalGraphNodes(
                        tile,
                        mapper,
                        portalIndex,
                        triangleComponents,
                        nodes,
                        adjacency,
                        nodeIndexByKey);
                }

                for (int a = firstNode; a < nodes.Count; a++)
                {
                    for (int b = a + 1; b < nodes.Count; b++)
                    {
                        if (nodes[a].TileId.Equals(tile.TileId) &&
                            nodes[a].TriangleComponent == nodes[b].TriangleComponent)
                        {
                            AddGraphEdge(adjacency, a, b);
                        }
                    }
                }
            }
        }

        if (nodes.Count == 0)
        {
            return new List<PortalComponentCandidate>(0);
        }

        for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            PortalGraphNode node = nodes[nodeIndex];
            if (!TryLoadTraversableTile(store, node.TileId, out NavTile tile))
            {
                continue;
            }

            NavBorderPortal portal = tile.Portals[node.PortalIndex];
            NavTileId neighborId = GetNeighborTileId(node.TileId, portal.Side);
            if (neighborId.ChunkX < minX ||
                neighborId.ChunkY < minY ||
                neighborId.ChunkX > maxX ||
                neighborId.ChunkY > maxY ||
                !TryLoadTraversableTile(store, neighborId, out NavTile neighborTile) ||
                !TryResolveMatchingOppositePortal(
                    portal,
                    neighborTile,
                    out int neighborPortalIndex,
                    out int overlapStart,
                    out int overlapEnd) ||
                !TryGetTriangleComponents(triangleComponentsByTile, tile, out int[] triangleComponents) ||
                !TryGetTriangleComponents(triangleComponentsByTile, neighborTile, out int[] neighborTriangleComponents))
            {
                continue;
            }

            NavBorderPortal neighborPortal = neighborTile.Portals[neighborPortalIndex];
            for (int sample = 0; sample <= PortalGraphSampleDenominator; sample++)
            {
                int intervalValue = InterpolateInt(overlapStart, overlapEnd, 0, PortalGraphSampleDenominator, sample);
                if (!TryResolvePortalSampleComponent(
                        tile,
                        portal,
                        intervalValue,
                        triangleComponents,
                        out int component) ||
                    component != node.TriangleComponent ||
                    !TryResolvePortalSampleComponent(
                        neighborTile,
                        neighborPortal,
                        intervalValue,
                        neighborTriangleComponents,
                        out int neighborComponent))
                {
                    continue;
                }

                if (nodeIndexByKey.TryGetValue(
                        new TilePortalComponentKey(neighborId, neighborPortalIndex, neighborComponent),
                        out int neighborNodeIndex))
                {
                    AddGraphEdge(adjacency, nodeIndex, neighborNodeIndex);
                }
            }
        }

        return BuildPortalComponentCandidates(nodes, adjacency);
    }

    private static List<PortalComponentCandidate> BuildPortalComponentCandidates(
        List<PortalGraphNode> nodes,
        List<List<int>> adjacency)
    {
        var candidates = new List<PortalComponentCandidate>(MaxEndpointComponentCandidates);
        var visited = new bool[nodes.Count];
        var queue = new Queue<int>(4096);

        for (int i = 0; i < nodes.Count; i++)
        {
            if (visited[i])
            {
                continue;
            }

            visited[i] = true;
            queue.Clear();
            queue.Enqueue(i);
            PortalComponentCandidate component = PortalComponentCandidate.Create();

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                component.Include(nodes[current]);
                List<int> neighbors = adjacency[current];
                for (int n = 0; n < neighbors.Count; n++)
                {
                    int next = neighbors[n];
                    if ((uint)next >= (uint)nodes.Count || visited[next])
                    {
                        continue;
                    }

                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }

            component.FinalizeRoute();
            InsertPortalComponentCandidate(candidates, component);
        }

        return candidates;
    }

    private static void InsertPortalComponentCandidate(
        List<PortalComponentCandidate> candidates,
        PortalComponentCandidate component)
    {
        if (!component.Available ||
            component.NodeCount < 2 ||
            component.StartNode.NodeIndex == component.GoalNode.NodeIndex)
        {
            return;
        }

        int insertAt = candidates.Count;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (component.IsBetterThan(candidates[i]))
            {
                insertAt = i;
                break;
            }
        }

        if (insertAt >= MaxEndpointComponentCandidates)
        {
            return;
        }

        candidates.Insert(insertAt, component);
        if (candidates.Count > MaxEndpointComponentCandidates)
        {
            candidates.RemoveAt(candidates.Count - 1);
        }
    }

    private static bool TryCreateMapper(
        MassNavigationBakeDataDiagnostics diagnostics,
        NavTile tile,
        out MassNavigationNavMeshRuntimeCoordinateMapper mapper)
    {
        mapper = MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(diagnostics, tile);
        return mapper.Available;
    }

    private static void AddPortalGraphNodes(
        NavTile tile,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper,
        int portalIndex,
        int[] triangleComponents,
        List<PortalGraphNode> nodes,
        List<List<int>> adjacency,
        Dictionary<TilePortalComponentKey, int> nodeIndexByKey)
    {
        if ((uint)portalIndex >= (uint)tile.Portals.Length)
        {
            return;
        }

        NavBorderPortal portal = tile.Portals[portalIndex];
        GetPortalInterval(portal, out int intervalStart, out int intervalEnd);
        for (int sample = 0; sample <= PortalGraphSampleDenominator; sample++)
        {
            int intervalValue = InterpolateInt(intervalStart, intervalEnd, 0, PortalGraphSampleDenominator, sample);
            if (!TryBuildPortalGraphNode(
                    tile,
                    mapper,
                    portalIndex,
                    portal,
                    intervalValue,
                    sample,
                    triangleComponents,
                    nodes.Count,
                    out PortalGraphNode node))
            {
                continue;
            }

            var key = new TilePortalComponentKey(tile.TileId, portalIndex, node.TriangleComponent);
            if (nodeIndexByKey.ContainsKey(key))
            {
                continue;
            }

            nodeIndexByKey[key] = nodes.Count;
            nodes.Add(node);
            adjacency.Add(new List<int>(4));
        }
    }

    private static bool TryBuildPortalGraphNode(
        NavTile tile,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper,
        int portalIndex,
        NavBorderPortal portal,
        int intervalValue,
        int sampleIndex,
        int[] triangleComponents,
        int nodeIndex,
        out PortalGraphNode node)
    {
        node = default;
        int localX = InterpolatePortalLocalX(portal, intervalValue);
        int localZ = InterpolatePortalLocalZ(portal, intervalValue);
        ApplyPortalInset(tile, portal.Side, ref localX, ref localZ);

        localX = Math.Clamp(localX, 0, Math.Max(0, ResolveTileLocalExtentCm(tile, axisX: true)));
        localZ = Math.Clamp(localZ, 0, Math.Max(0, ResolveTileLocalExtentCm(tile, axisX: false)));
        if (!TryProjectLocalPointToTriangle(
                tile,
                localX,
                localZ,
                ResolvePortalProjectionDistanceCm(tile),
                out int triangleId,
                out int projectedLocalX,
                out int projectedLocalZ) ||
            (uint)triangleId >= (uint)triangleComponents.Length)
        {
            return false;
        }

        Vector2 worldCm = mapper.BakedTileLocalToWorldCm(tile, projectedLocalX, projectedLocalZ);
        if (!float.IsFinite(worldCm.X) || !float.IsFinite(worldCm.Y))
        {
            return false;
        }

        node = new PortalGraphNode(
            nodeIndex,
            tile.TileId,
            portalIndex,
            sampleIndex,
            triangleComponents[triangleId],
            worldCm,
            $"portal_{portal.Side}:{portalIndex}:sample={sampleIndex}/{PortalGraphSampleDenominator}:tri={triangleId}:component={triangleComponents[triangleId]}");
        return true;
    }

    private static bool TryGetTriangleComponents(
        Dictionary<NavTileId, int[]> componentsByTile,
        NavTile tile,
        out int[] triangleComponents)
    {
        if (componentsByTile.TryGetValue(tile.TileId, out int[]? cached))
        {
            triangleComponents = cached;
            return true;
        }

        triangleComponents = BuildTriangleComponents(tile);
        componentsByTile[tile.TileId] = triangleComponents;
        return triangleComponents.Length == tile.TriangleCount;
    }

    private static bool TryResolvePortalSampleComponent(
        NavTile tile,
        NavBorderPortal portal,
        int intervalValue,
        int[] triangleComponents,
        out int component)
    {
        component = -1;
        int localX = InterpolatePortalLocalX(portal, intervalValue);
        int localZ = InterpolatePortalLocalZ(portal, intervalValue);
        ApplyPortalInset(tile, portal.Side, ref localX, ref localZ);
        localX = Math.Clamp(localX, 0, Math.Max(0, ResolveTileLocalExtentCm(tile, axisX: true)));
        localZ = Math.Clamp(localZ, 0, Math.Max(0, ResolveTileLocalExtentCm(tile, axisX: false)));
        if (!TryProjectLocalPointToTriangle(
                tile,
                localX,
                localZ,
                ResolvePortalProjectionDistanceCm(tile),
                out int triangleId,
                out _,
                out _) ||
            (uint)triangleId >= (uint)triangleComponents.Length)
        {
            return false;
        }

        component = triangleComponents[triangleId];
        return component >= 0;
    }

    private static void ApplyPortalInset(NavTile tile, NavPortalSide side, ref int localX, ref int localZ)
    {
        int inset = ResolveEndpointSampleInsetCm(tile);
        switch (side)
        {
            case NavPortalSide.West:
                localX += inset;
                break;
            case NavPortalSide.East:
                localX -= inset;
                break;
            case NavPortalSide.North:
                localZ += inset;
                break;
            case NavPortalSide.South:
                localZ -= inset;
                break;
        }
    }

    private static int ResolvePortalProjectionDistanceCm(NavTile tile)
    {
        int extent = Math.Min(
            ResolveTileLocalExtentCm(tile, axisX: true),
            ResolveTileLocalExtentCm(tile, axisX: false));
        return Math.Clamp(extent / 3, 512, 4096);
    }

    private static void AddGraphEdge(List<List<int>> adjacency, int a, int b)
    {
        if ((uint)a >= (uint)adjacency.Count || (uint)b >= (uint)adjacency.Count || a == b)
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

    private static int[] BuildTriangleComponents(NavTile tile)
    {
        int triCount = Math.Max(0, tile.TriangleCount);
        var components = new int[triCount];
        Array.Fill(components, -1);
        if (triCount == 0)
        {
            return components;
        }

        List<int>[] adjacency = BuildTriangleAdjacency(tile);
        var queue = new Queue<int>(triCount);
        int componentId = 0;
        for (int tri = 0; tri < triCount; tri++)
        {
            if (components[tri] >= 0)
            {
                continue;
            }

            components[tri] = componentId;
            queue.Clear();
            queue.Enqueue(tri);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                List<int> neighbors = adjacency[current];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int candidate = neighbors[i];
                    if ((uint)candidate >= (uint)triCount || components[candidate] >= 0)
                    {
                        continue;
                    }

                    components[candidate] = componentId;
                    queue.Enqueue(candidate);
                }
            }

            componentId++;
        }

        return components;
    }

    private static List<int>[] BuildTriangleAdjacency(NavTile tile)
    {
        int triCount = Math.Max(0, tile.TriangleCount);
        var adjacency = new List<int>[triCount];
        for (int i = 0; i < triCount; i++)
        {
            adjacency[i] = new List<int>(3);
        }

        for (int tri = 0; tri < triCount; tri++)
        {
            AddStoredTriangleNeighbor(tile, adjacency[tri], tri, 0);
            AddStoredTriangleNeighbor(tile, adjacency[tri], tri, 1);
            AddStoredTriangleNeighbor(tile, adjacency[tri], tri, 2);
        }

        var edgeOwners = new Dictionary<TriangleEdgeKey, int>(triCount * 3);
        for (int tri = 0; tri < triCount; tri++)
        {
            for (int edge = 0; edge < 3; edge++)
            {
                GetTriangleEdgeVertices(tile, tri, edge, out int a, out int b);
                var key = new TriangleEdgeKey(a, b);
                if (!edgeOwners.TryGetValue(key, out int otherTri))
                {
                    edgeOwners[key] = tri;
                    continue;
                }

                AddTriangleNeighbor(adjacency[tri], otherTri);
                AddTriangleNeighbor(adjacency[otherTri], tri);
            }
        }

        return adjacency;
    }

    private static void AddStoredTriangleNeighbor(NavTile tile, List<int> neighbors, int tri, int edge)
    {
        int neighbor = GetStoredTriangleNeighbor(tile, tri, edge);
        if ((uint)neighbor >= (uint)tile.TriangleCount || neighbor == tri)
        {
            return;
        }

        AddTriangleNeighbor(neighbors, neighbor);
    }

    private static void AddTriangleNeighbor(List<int> neighbors, int candidate)
    {
        if (!neighbors.Contains(candidate))
        {
            neighbors.Add(candidate);
        }
    }

    private readonly struct TriangleEdgeKey : IEquatable<TriangleEdgeKey>
    {
        private readonly int _a;
        private readonly int _b;

        public TriangleEdgeKey(int a, int b)
        {
            if (a <= b)
            {
                _a = a;
                _b = b;
            }
            else
            {
                _a = b;
                _b = a;
            }
        }

        public bool Equals(TriangleEdgeKey other) => _a == other._a && _b == other._b;
        public override bool Equals(object? obj) => obj is TriangleEdgeKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_a, _b);
    }

    private static int GetStoredTriangleNeighbor(NavTile tile, int tri, int edge)
    {
        if ((uint)tri >= (uint)tile.TriangleCount)
        {
            return -1;
        }

        return edge switch
        {
            0 when tile.N0 != null && tri < tile.N0.Length => tile.N0[tri],
            1 when tile.N1 != null && tri < tile.N1.Length => tile.N1[tri],
            2 when tile.N2 != null && tri < tile.N2.Length => tile.N2[tri],
            _ => -1
        };
    }

    private static void GetTriangleEdgeVertices(NavTile tile, int triId, int edge, out int va, out int vb)
    {
        if (edge == 0)
        {
            va = tile.TriA[triId];
            vb = tile.TriB[triId];
            return;
        }

        if (edge == 1)
        {
            va = tile.TriB[triId];
            vb = tile.TriC[triId];
            return;
        }

        va = tile.TriC[triId];
        vb = tile.TriA[triId];
    }

    private static bool TryProjectLocalPointToTriangle(
        NavTile tile,
        int localXcm,
        int localZcm,
        int maxSurfaceDistanceCm,
        out int triangleId,
        out int projectedLocalXcm,
        out int projectedLocalZcm)
    {
        triangleId = -1;
        projectedLocalXcm = 0;
        projectedLocalZcm = 0;
        long maxD2 = (long)Math.Max(0, maxSurfaceDistanceCm) * Math.Max(0, maxSurfaceDistanceCm);
        long bestD2 = long.MaxValue;

        for (int tri = 0; tri < tile.TriangleCount; tri++)
        {
            int a = tile.TriA[tri];
            int b = tile.TriB[tri];
            int c = tile.TriC[tri];
            FindClosestPointOnTriangle(
                localXcm,
                localZcm,
                tile.VertexXcm[a],
                tile.VertexZcm[a],
                tile.VertexXcm[b],
                tile.VertexZcm[b],
                tile.VertexXcm[c],
                tile.VertexZcm[c],
                out int candidateX,
                out int candidateZ,
                out long candidateD2);

            if (candidateD2 >= bestD2)
            {
                continue;
            }

            bestD2 = candidateD2;
            triangleId = tri;
            projectedLocalXcm = candidateX;
            projectedLocalZcm = candidateZ;
        }

        return triangleId >= 0 && bestD2 <= maxD2;
    }

    private static void FindClosestPointOnTriangle(
        int px,
        int pz,
        int ax,
        int az,
        int bx,
        int bz,
        int cx,
        int cz,
        out int closestX,
        out int closestZ,
        out long distanceSquared)
    {
        if (PointInTriangle(px, pz, ax, az, bx, bz, cx, cz))
        {
            closestX = px;
            closestZ = pz;
            distanceSquared = 0;
            return;
        }

        ClosestPointOnSegment(px, pz, ax, az, bx, bz, out int abX, out int abZ, out long abD2);
        ClosestPointOnSegment(px, pz, bx, bz, cx, cz, out int bcX, out int bcZ, out long bcD2);
        ClosestPointOnSegment(px, pz, cx, cz, ax, az, out int caX, out int caZ, out long caD2);

        closestX = abX;
        closestZ = abZ;
        distanceSquared = abD2;
        if (bcD2 < distanceSquared)
        {
            closestX = bcX;
            closestZ = bcZ;
            distanceSquared = bcD2;
        }

        if (caD2 < distanceSquared)
        {
            closestX = caX;
            closestZ = caZ;
            distanceSquared = caD2;
        }
    }

    private static void ClosestPointOnSegment(
        int px,
        int pz,
        int ax,
        int az,
        int bx,
        int bz,
        out int closestX,
        out int closestZ,
        out long distanceSquared)
    {
        long dx = (long)bx - ax;
        long dz = (long)bz - az;
        long len2 = (dx * dx) + (dz * dz);
        if (len2 <= 0)
        {
            closestX = ax;
            closestZ = az;
            distanceSquared = DistanceSquared(px, pz, ax, az);
            return;
        }

        long dot = (((long)px - ax) * dx) + (((long)pz - az) * dz);
        if (dot <= 0)
        {
            closestX = ax;
            closestZ = az;
            distanceSquared = DistanceSquared(px, pz, ax, az);
            return;
        }

        if (dot >= len2)
        {
            closestX = bx;
            closestZ = bz;
            distanceSquared = DistanceSquared(px, pz, bx, bz);
            return;
        }

        closestX = ax + (int)DivRound(dx * dot, len2);
        closestZ = az + (int)DivRound(dz * dot, len2);
        distanceSquared = DistanceSquared(px, pz, closestX, closestZ);
    }

    private static bool TryResolveMatchingOppositePortal(
        NavBorderPortal portal,
        NavTile neighborTile,
        out int neighborPortalIndex,
        out int bestOverlapStart,
        out int bestOverlapEnd)
    {
        neighborPortalIndex = -1;
        bestOverlapStart = 0;
        bestOverlapEnd = 0;
        NavPortalSide opposite = GetOppositeSide(portal.Side);
        GetPortalInterval(portal, out int start, out int end);
        int bestOverlap = 0;
        for (int i = 0; i < neighborTile.Portals.Length; i++)
        {
            NavBorderPortal neighbor = neighborTile.Portals[i];
            if (neighbor.Side != opposite)
            {
                continue;
            }

            GetPortalInterval(neighbor, out int neighborStart, out int neighborEnd);
            int overlap = Math.Min(end, neighborEnd) - Math.Max(start, neighborStart);
            if (overlap <= bestOverlap)
            {
                continue;
            }

            bestOverlap = overlap;
            bestOverlapStart = Math.Max(start, neighborStart);
            bestOverlapEnd = Math.Min(end, neighborEnd);
            neighborPortalIndex = i;
        }

        return neighborPortalIndex >= 0;
    }

    private static ComponentCandidate FloodComponent(
        NavTileStore store,
        int layer,
        int minX,
        int minY,
        int maxX,
        int maxY,
        int width,
        bool[] visited,
        Queue<NavTileId> queue,
        NavTileId seed)
    {
        queue.Clear();
        queue.Enqueue(seed);
        ComponentCandidate component = ComponentCandidate.Create();

        while (queue.Count > 0)
        {
            NavTileId currentId = queue.Dequeue();
            if (!TryLoadTraversableTile(store, currentId, out NavTile currentTile))
            {
                continue;
            }

            component.Include(currentId);

            for (int i = 0; i < currentTile.Portals.Length; i++)
            {
                NavTileId neighborId = GetNeighborTileId(currentId, currentTile.Portals[i].Side);
                if (neighborId.ChunkX < minX ||
                    neighborId.ChunkY < minY ||
                    neighborId.ChunkX > maxX ||
                    neighborId.ChunkY > maxY)
                {
                    continue;
                }

                int neighborIndex = IndexOf(neighborId.ChunkX, neighborId.ChunkY, minX, minY, width);
                if (visited[neighborIndex])
                {
                    continue;
                }

                if (!TryLoadTraversableTile(store, neighborId, out NavTile neighborTile))
                {
                    visited[neighborIndex] = true;
                    continue;
                }

                if (!HasOverlappingOppositePortal(currentTile.Portals[i], neighborTile))
                {
                    continue;
                }

                visited[neighborIndex] = true;
                queue.Enqueue(neighborId);
            }
        }

        component.FinalizeRoute();
        return component;
    }

    private static bool TryLoadTraversableTile(NavTileStore store, NavTileId id, out NavTile tile)
    {
        tile = null!;
        try
        {
            tile = store.GetOrLoad(id);
            return tile.TriangleCount > 0 && tile.Portals.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static EndpointSample[] ResolveEndpointSamples(
        MassNavigationBakeDataDiagnostics diagnostics,
        NavTileStore store,
        ComponentCandidate component,
        NavTileId tileId)
    {
        if (!TryLoadTraversableTile(store, tileId, out NavTile tile) || tile.TriangleCount <= 0)
        {
            return Array.Empty<EndpointSample>();
        }

        MassNavigationNavMeshRuntimeCoordinateMapper mapper =
            MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(diagnostics, tile);
        if (!mapper.Available)
        {
            return Array.Empty<EndpointSample>();
        }

        var samples = new List<EndpointSample>(MaxEndpointSamplesPerTile);
        AddPortalEndpointSamples(store, component, tile, mapper, samples);
        AddLargestTriangleCentroidSamples(tile, mapper, samples);
        return samples.ToArray();
    }

    private static void AddPortalEndpointSamples(
        NavTileStore store,
        ComponentCandidate component,
        NavTile tile,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper,
        List<EndpointSample> samples)
    {
        int inset = ResolveEndpointSampleInsetCm(tile);
        int maxLocalX = ResolveTileLocalExtentCm(tile, axisX: true);
        int maxLocalZ = ResolveTileLocalExtentCm(tile, axisX: false);
        for (int i = 0; i < tile.Portals.Length && samples.Count < MaxEndpointSamplesPerTile; i++)
        {
            NavBorderPortal portal = tile.Portals[i];
            NavTileId neighborId = GetNeighborTileId(tile.TileId, portal.Side);
            if (!component.Contains(neighborId) ||
                !TryLoadTraversableTile(store, neighborId, out NavTile neighborTile) ||
                !HasOverlappingOppositePortal(portal, neighborTile))
            {
                continue;
            }

            int localX = (portal.LeftXcm + portal.RightXcm) / 2;
            int localZ = (portal.LeftZcm + portal.RightZcm) / 2;
            switch (portal.Side)
            {
                case NavPortalSide.West:
                    localX += inset;
                    break;
                case NavPortalSide.East:
                    localX -= inset;
                    break;
                case NavPortalSide.North:
                    localZ += inset;
                    break;
                case NavPortalSide.South:
                    localZ -= inset;
                    break;
            }

            localX = Math.Clamp(localX, 0, Math.Max(0, maxLocalX));
            localZ = Math.Clamp(localZ, 0, Math.Max(0, maxLocalZ));
            AddEndpointSample(tile, mapper, localX, localZ, $"portal_{portal.Side}", samples);
        }
    }

    private static void AddLargestTriangleCentroidSamples(
        NavTile tile,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper,
        List<EndpointSample> samples)
    {
        Span<int> bestTri = stackalloc int[MaxCentroidEndpointSamplesPerTile];
        Span<long> bestArea = stackalloc long[MaxCentroidEndpointSamplesPerTile];
        bestTri.Fill(-1);

        for (int tri = 0; tri < tile.TriangleCount; tri++)
        {
            long area2 = ComputeTriangleArea2(tile, tri);
            if (area2 <= 0)
            {
                continue;
            }

            for (int slot = 0; slot < bestTri.Length; slot++)
            {
                if (area2 <= bestArea[slot])
                {
                    continue;
                }

                for (int move = bestTri.Length - 1; move > slot; move--)
                {
                    bestTri[move] = bestTri[move - 1];
                    bestArea[move] = bestArea[move - 1];
                }

                bestTri[slot] = tri;
                bestArea[slot] = area2;
                break;
            }
        }

        for (int i = 0; i < bestTri.Length && samples.Count < MaxEndpointSamplesPerTile; i++)
        {
            int tri = bestTri[i];
            if (tri < 0)
            {
                continue;
            }

            int a = tile.TriA[tri];
            int b = tile.TriB[tri];
            int c = tile.TriC[tri];
            AddEndpointSample(
                tile,
                mapper,
                (tile.VertexXcm[a] + tile.VertexXcm[b] + tile.VertexXcm[c]) / 3,
                (tile.VertexZcm[a] + tile.VertexZcm[b] + tile.VertexZcm[c]) / 3,
                $"triangle_centroid_{tri}",
                samples);
        }
    }

    private static void AddEndpointSample(
        NavTile tile,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper,
        int localXcm,
        int localZcm,
        string source,
        List<EndpointSample> samples)
    {
        Vector2 worldCm = mapper.BakedTileLocalToWorldCm(tile, localXcm, localZcm);
        if (!float.IsFinite(worldCm.X) || !float.IsFinite(worldCm.Y))
        {
            return;
        }

        for (int i = 0; i < samples.Count; i++)
        {
            Vector2 existing = samples[i].WorldCm;
            if (Math.Abs(existing.X - worldCm.X) < 1f && Math.Abs(existing.Y - worldCm.Y) < 1f)
            {
                return;
            }
        }

        samples.Add(new EndpointSample(worldCm, source));
    }

    private static int ResolveEndpointSampleInsetCm(NavTile tile)
    {
        int extent = Math.Min(
            ResolveTileLocalExtentCm(tile, axisX: true),
            ResolveTileLocalExtentCm(tile, axisX: false));
        return Math.Clamp(extent / 32, 32, 512);
    }

    private static int ResolveTileLocalExtentCm(NavTile tile, bool axisX)
    {
        int max = 0;
        int[] vertices = axisX ? tile.VertexXcm : tile.VertexZcm;
        for (int i = 0; i < vertices.Length; i++)
        {
            max = Math.Max(max, vertices[i]);
        }

        for (int i = 0; i < tile.Portals.Length; i++)
        {
            NavBorderPortal portal = tile.Portals[i];
            max = Math.Max(max, axisX ? portal.LeftXcm : portal.LeftZcm);
            max = Math.Max(max, axisX ? portal.RightXcm : portal.RightZcm);
        }

        return max;
    }

    private static bool HasOverlappingOppositePortal(NavBorderPortal portal, NavTile neighborTile)
    {
        NavPortalSide opposite = GetOppositeSide(portal.Side);
        GetPortalInterval(portal, out int start, out int end);
        for (int i = 0; i < neighborTile.Portals.Length; i++)
        {
            NavBorderPortal neighbor = neighborTile.Portals[i];
            if (neighbor.Side != opposite)
            {
                continue;
            }

            GetPortalInterval(neighbor, out int neighborStart, out int neighborEnd);
            if (Math.Max(start, neighborStart) < Math.Min(end, neighborEnd))
            {
                return true;
            }
        }

        return false;
    }

    private static void GetPortalInterval(NavBorderPortal portal, out int start, out int end)
    {
        if (portal.Side == NavPortalSide.West || portal.Side == NavPortalSide.East)
        {
            start = Math.Min(portal.V0, portal.V1);
            end = Math.Max(portal.V0, portal.V1);
        }
        else
        {
            start = Math.Min(portal.U0, portal.U1);
            end = Math.Max(portal.U0, portal.U1);
        }
    }

    private static int InterpolatePortalLocalX(NavBorderPortal portal, int intervalValue)
    {
        GetPortalRawInterval(portal, out int start, out int end);
        return InterpolateInt(portal.LeftXcm, portal.RightXcm, start, end, intervalValue);
    }

    private static int InterpolatePortalLocalZ(NavBorderPortal portal, int intervalValue)
    {
        GetPortalRawInterval(portal, out int start, out int end);
        return InterpolateInt(portal.LeftZcm, portal.RightZcm, start, end, intervalValue);
    }

    private static void GetPortalRawInterval(NavBorderPortal portal, out int start, out int end)
    {
        if (portal.Side == NavPortalSide.West || portal.Side == NavPortalSide.East)
        {
            start = portal.V0;
            end = portal.V1;
        }
        else
        {
            start = portal.U0;
            end = portal.U1;
        }
    }

    private static int InterpolateInt(int startValue, int endValue, int start, int end, int value)
    {
        int span = end - start;
        if (span == 0)
        {
            return startValue;
        }

        long numerator = (long)(endValue - startValue) * (value - start);
        long half = Math.Abs(span) / 2;
        long rounded = numerator >= 0
            ? (numerator + half) / span
            : (numerator - half) / span;
        return startValue + (int)rounded;
    }

    private static NavPortalSide GetOppositeSide(NavPortalSide side)
    {
        return side switch
        {
            NavPortalSide.West => NavPortalSide.East,
            NavPortalSide.East => NavPortalSide.West,
            NavPortalSide.North => NavPortalSide.South,
            NavPortalSide.South => NavPortalSide.North,
            _ => side
        };
    }

    private static NavTileId GetNeighborTileId(NavTileId id, NavPortalSide side)
    {
        return side switch
        {
            NavPortalSide.West => new NavTileId(id.ChunkX - 1, id.ChunkY, id.Layer),
            NavPortalSide.East => new NavTileId(id.ChunkX + 1, id.ChunkY, id.Layer),
            NavPortalSide.North => new NavTileId(id.ChunkX, id.ChunkY - 1, id.Layer),
            NavPortalSide.South => new NavTileId(id.ChunkX, id.ChunkY + 1, id.Layer),
            _ => id
        };
    }

    private static bool TryResolveTileTriangleCentroidWorldCm(
        MassNavigationBakeDataDiagnostics diagnostics,
        NavTileStore store,
        NavTileId tileId,
        out Vector2 worldCm)
    {
        worldCm = default;
        if (!TryLoadTraversableTile(store, tileId, out NavTile tile) || tile.TriangleCount <= 0)
        {
            return false;
        }

        int bestTri = ResolveLargestTriangle(tile);
        if (bestTri < 0)
        {
            return false;
        }

        int a = tile.TriA[bestTri];
        int b = tile.TriB[bestTri];
        int c = tile.TriC[bestTri];
        int localX = (tile.VertexXcm[a] + tile.VertexXcm[b] + tile.VertexXcm[c]) / 3;
        int localZ = (tile.VertexZcm[a] + tile.VertexZcm[b] + tile.VertexZcm[c]) / 3;
        MassNavigationNavMeshRuntimeCoordinateMapper mapper =
            MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(diagnostics, tile);
        if (!mapper.Available)
        {
            return false;
        }

        worldCm = mapper.BakedTileLocalToWorldCm(tile, localX, localZ);
        return float.IsFinite(worldCm.X) && float.IsFinite(worldCm.Y);
    }

    private static int ResolveLargestTriangle(NavTile tile)
    {
        int bestTri = -1;
        long bestArea2 = 0;
        for (int i = 0; i < tile.TriangleCount; i++)
        {
            long area2 = ComputeTriangleArea2(tile, i);
            if (area2 > bestArea2)
            {
                bestArea2 = area2;
                bestTri = i;
            }
        }

        return bestTri;
    }

    private static long ComputeTriangleArea2(NavTile tile, int tri)
    {
        int a = tile.TriA[tri];
        int b = tile.TriB[tri];
        int c = tile.TriC[tri];
        return Math.Abs(Orient2D(
            tile.VertexXcm[a],
            tile.VertexZcm[a],
            tile.VertexXcm[b],
            tile.VertexZcm[b],
            tile.VertexXcm[c],
            tile.VertexZcm[c]));
    }

    private static long Orient2D(int ax, int az, int bx, int bz, int cx, int cz)
    {
        return ((long)bx - ax) * ((long)cz - az) - (((long)bz - az) * ((long)cx - ax));
    }

    private static bool PointInTriangle(int px, int pz, int ax, int az, int bx, int bz, int cx, int cz)
    {
        long area = Orient2D(ax, az, bx, bz, cx, cz);
        if (area == 0)
        {
            return false;
        }

        long ab = Orient2D(ax, az, bx, bz, px, pz);
        long bc = Orient2D(bx, bz, cx, cz, px, pz);
        long ca = Orient2D(cx, cz, ax, az, px, pz);
        return area > 0
            ? ab >= 0 && bc >= 0 && ca >= 0
            : ab <= 0 && bc <= 0 && ca <= 0;
    }

    private static long DistanceSquared(int ax, int az, int bx, int bz)
    {
        long dx = (long)bx - ax;
        long dz = (long)bz - az;
        return (dx * dx) + (dz * dz);
    }

    private static long DivRound(long numerator, long denominator)
    {
        if (denominator <= 0)
        {
            return 0;
        }

        return numerator >= 0
            ? (numerator + (denominator / 2)) / denominator
            : (numerator - (denominator / 2)) / denominator;
    }

    private static long BuildComponentTileKey(NavTileId id)
    {
        unchecked
        {
            return ((long)(uint)id.Layer << 42) ^
                ((long)(uint)id.ChunkY << 21) ^
                (uint)id.ChunkX;
        }
    }

    private static int IndexOf(int x, int y, int minX, int minY, int width)
    {
        return ((y - minY) * width) + (x - minX);
    }

    private readonly record struct RouteEndpointPair(
        NavTileId Start,
        NavTileId Goal,
        int RouteChunkCount);

    private readonly record struct EndpointSample(
        Vector2 WorldCm,
        string Source);

    private readonly record struct TilePortalComponentKey(
        NavTileId TileId,
        int PortalIndex,
        int TriangleComponent);

    private readonly record struct PortalGraphNode(
        int NodeIndex,
        NavTileId TileId,
        int PortalIndex,
        int SampleIndex,
        int TriangleComponent,
        Vector2 WorldCm,
        string Source);

    private readonly record struct PortalEndpointPair(
        PortalGraphNode Start,
        PortalGraphNode Goal,
        int RouteChunkCount);

    private struct PortalComponentCandidate
    {
        private int _minSumScore;
        private int _maxSumScore;
        private int _minDiffScore;
        private int _maxDiffScore;
        private PortalGraphNode _minSumNode;
        private PortalGraphNode _maxSumNode;
        private PortalGraphNode _minDiffNode;
        private PortalGraphNode _maxDiffNode;
        private HashSet<long> _tiles;

        public bool Available;
        public int NodeCount;
        public int TileCount;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public PortalGraphNode StartNode;
        public PortalGraphNode GoalNode;
        public int MacroRouteChunkCount;

        public static PortalComponentCandidate Create()
        {
            return new PortalComponentCandidate
            {
                Available = true,
                MinX = int.MaxValue,
                MinY = int.MaxValue,
                MaxX = int.MinValue,
                MaxY = int.MinValue,
                _minSumScore = int.MaxValue,
                _maxSumScore = int.MinValue,
                _minDiffScore = int.MaxValue,
                _maxDiffScore = int.MinValue,
                _tiles = new HashSet<long>()
            };
        }

        public void Include(PortalGraphNode node)
        {
            NodeCount++;
            _tiles ??= new HashSet<long>();
            if (_tiles.Add(BuildComponentTileKey(node.TileId)))
            {
                TileCount++;
                MinX = Math.Min(MinX, node.TileId.ChunkX);
                MinY = Math.Min(MinY, node.TileId.ChunkY);
                MaxX = Math.Max(MaxX, node.TileId.ChunkX);
                MaxY = Math.Max(MaxY, node.TileId.ChunkY);
            }

            int sumScore = node.TileId.ChunkX + node.TileId.ChunkY;
            if (sumScore < _minSumScore)
            {
                _minSumScore = sumScore;
                _minSumNode = node;
            }

            if (sumScore > _maxSumScore)
            {
                _maxSumScore = sumScore;
                _maxSumNode = node;
            }

            int diffScore = node.TileId.ChunkX - node.TileId.ChunkY;
            if (diffScore < _minDiffScore)
            {
                _minDiffScore = diffScore;
                _minDiffNode = node;
            }

            if (diffScore > _maxDiffScore)
            {
                _maxDiffScore = diffScore;
                _maxDiffNode = node;
            }
        }

        public void FinalizeRoute()
        {
            SetRoute(_minSumNode, _maxSumNode);
            TrySetBetterRoute(_minDiffNode, _maxDiffNode);
            TrySetBetterRoute(_maxDiffNode, _minDiffNode);
            TrySetBetterRoute(_maxSumNode, _minSumNode);
        }

        public void AddEndpointPairs(List<PortalEndpointPair> pairs)
        {
            AddPair(pairs, StartNode, GoalNode);
            AddPair(pairs, GoalNode, StartNode);
            AddPair(pairs, _minDiffNode, _maxDiffNode);
            AddPair(pairs, _maxDiffNode, _minDiffNode);
            AddPair(pairs, _minSumNode, _maxSumNode);
            AddPair(pairs, _maxSumNode, _minSumNode);
        }

        public bool IsBetterThan(PortalComponentCandidate other)
        {
            if (!Available)
            {
                return false;
            }

            if (!other.Available)
            {
                return true;
            }

            if (MacroRouteChunkCount != other.MacroRouteChunkCount)
            {
                return MacroRouteChunkCount > other.MacroRouteChunkCount;
            }

            if (TileCount != other.TileCount)
            {
                return TileCount > other.TileCount;
            }

            return NodeCount > other.NodeCount;
        }

        private static void AddPair(List<PortalEndpointPair> pairs, PortalGraphNode start, PortalGraphNode goal)
        {
            if (start.NodeIndex == goal.NodeIndex)
            {
                return;
            }

            var pair = new PortalEndpointPair(start, goal, ComputeRouteChunkCount(start.TileId, goal.TileId));
            for (int i = 0; i < pairs.Count; i++)
            {
                PortalEndpointPair existing = pairs[i];
                if (existing.Start.NodeIndex == pair.Start.NodeIndex && existing.Goal.NodeIndex == pair.Goal.NodeIndex)
                {
                    return;
                }
            }

            pairs.Add(pair);
        }

        private void TrySetBetterRoute(PortalGraphNode start, PortalGraphNode goal)
        {
            int route = ComputeRouteChunkCount(start.TileId, goal.TileId);
            if (route > MacroRouteChunkCount)
            {
                StartNode = start;
                GoalNode = goal;
                MacroRouteChunkCount = route;
            }
        }

        private void SetRoute(PortalGraphNode start, PortalGraphNode goal)
        {
            StartNode = start;
            GoalNode = goal;
            MacroRouteChunkCount = ComputeRouteChunkCount(start.TileId, goal.TileId);
        }

        private static int ComputeRouteChunkCount(NavTileId start, NavTileId goal)
        {
            return Math.Abs(goal.ChunkX - start.ChunkX) +
                Math.Abs(goal.ChunkY - start.ChunkY) +
                1;
        }
    }

    private struct ComponentCandidate
    {
        private int _minSumScore;
        private int _maxSumScore;
        private int _minDiffScore;
        private int _maxDiffScore;
        private NavTileId _minSumChunk;
        private NavTileId _maxSumChunk;
        private NavTileId _minDiffChunk;
        private NavTileId _maxDiffChunk;
        private HashSet<long> _tiles;

        public bool Available;
        public int TileCount;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public NavTileId StartChunk;
        public NavTileId GoalChunk;
        public int MacroRouteChunkCount;

        public static ComponentCandidate Create()
        {
            return new ComponentCandidate
            {
                Available = true,
                MinX = int.MaxValue,
                MinY = int.MaxValue,
                MaxX = int.MinValue,
                MaxY = int.MinValue,
                _minSumScore = int.MaxValue,
                _maxSumScore = int.MinValue,
                _minDiffScore = int.MaxValue,
                _maxDiffScore = int.MinValue,
                _tiles = new HashSet<long>()
            };
        }

        public void Include(NavTileId id)
        {
            _tiles ??= new HashSet<long>();
            _tiles.Add(BuildComponentTileKey(id));
            TileCount++;
            MinX = Math.Min(MinX, id.ChunkX);
            MinY = Math.Min(MinY, id.ChunkY);
            MaxX = Math.Max(MaxX, id.ChunkX);
            MaxY = Math.Max(MaxY, id.ChunkY);
            int sumScore = id.ChunkX + id.ChunkY;
            if (sumScore < _minSumScore)
            {
                _minSumScore = sumScore;
                _minSumChunk = id;
            }

            if (sumScore > _maxSumScore)
            {
                _maxSumScore = sumScore;
                _maxSumChunk = id;
            }

            int diffScore = id.ChunkX - id.ChunkY;
            if (diffScore < _minDiffScore)
            {
                _minDiffScore = diffScore;
                _minDiffChunk = id;
            }

            if (diffScore > _maxDiffScore)
            {
                _maxDiffScore = diffScore;
                _maxDiffChunk = id;
            }
        }

        public void FinalizeRoute()
        {
            SetRoute(_minSumChunk, _maxSumChunk);
            TrySetBetterRoute(_minDiffChunk, _maxDiffChunk);
            TrySetBetterRoute(_maxDiffChunk, _minDiffChunk);
            TrySetBetterRoute(_maxSumChunk, _minSumChunk);
        }

        public void AddEndpointPairs(List<RouteEndpointPair> pairs)
        {
            AddPair(pairs, StartChunk, GoalChunk);
            AddPair(pairs, GoalChunk, StartChunk);
            AddPair(pairs, _minDiffChunk, _maxDiffChunk);
            AddPair(pairs, _maxDiffChunk, _minDiffChunk);
            AddPair(pairs, _minSumChunk, _maxSumChunk);
            AddPair(pairs, _maxSumChunk, _minSumChunk);
        }

        private static void AddPair(List<RouteEndpointPair> pairs, NavTileId start, NavTileId goal)
        {
            if (start.Equals(goal))
            {
                return;
            }

            var pair = new RouteEndpointPair(start, goal, ComputeRouteChunkCount(start, goal));
            for (int i = 0; i < pairs.Count; i++)
            {
                RouteEndpointPair existing = pairs[i];
                if (existing.Start.Equals(pair.Start) && existing.Goal.Equals(pair.Goal))
                {
                    return;
                }
            }

            pairs.Add(pair);
        }

        private void TrySetBetterRoute(NavTileId start, NavTileId goal)
        {
            int route = ComputeRouteChunkCount(start, goal);
            if (route > MacroRouteChunkCount)
            {
                StartChunk = start;
                GoalChunk = goal;
                MacroRouteChunkCount = route;
            }
        }

        private void SetRoute(NavTileId start, NavTileId goal)
        {
            StartChunk = start;
            GoalChunk = goal;
            MacroRouteChunkCount = ComputeRouteChunkCount(start, goal);
        }

        private static int ComputeRouteChunkCount(NavTileId start, NavTileId goal)
        {
            return Math.Abs(goal.ChunkX - start.ChunkX) +
                Math.Abs(goal.ChunkY - start.ChunkY) +
                1;
        }

        public bool IsBetterThan(ComponentCandidate other)
        {
            if (!Available)
            {
                return false;
            }

            if (!other.Available)
            {
                return true;
            }

            if (MacroRouteChunkCount != other.MacroRouteChunkCount)
            {
                return MacroRouteChunkCount > other.MacroRouteChunkCount;
            }

            return TileCount > other.TileCount;
        }

        public bool Contains(NavTileId id)
        {
            return _tiles != null && _tiles.Contains(BuildComponentTileKey(id));
        }

        private static long BuildComponentTileKey(NavTileId id)
        {
            unchecked
            {
                return ((long)(uint)id.Layer << 42) ^
                    ((long)(uint)id.ChunkY << 21) ^
                    (uint)id.ChunkX;
            }
        }
    }
}
