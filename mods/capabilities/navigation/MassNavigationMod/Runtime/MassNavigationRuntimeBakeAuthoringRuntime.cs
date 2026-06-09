using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;
using Ludots.Core.Navigation.Pathing;
using Ludots.NavBake.Recast;

namespace MassNavigationMod.Runtime;

public enum MassNavigationRuntimeBakeAuthoringMode
{
    None = 0,
    ObstaclePolygon = 1,
}

public enum MassNavigationRuntimeDirtyChunkGrid : byte
{
    Unknown = 0,
    NavTile = 1,
}

public readonly record struct MassNavigationRuntimeDirtyChunk(
    int X,
    int Y,
    int MinWorldXCm,
    int MinWorldYCm,
    int SizeXCm,
    int SizeYCm,
    MassNavigationRuntimeDirtyChunkGrid Grid)
{
    public MassNavigationRuntimeDirtyChunk(int x, int y)
        : this(x, y, 0, 0, 0, 0, MassNavigationRuntimeDirtyChunkGrid.Unknown)
    {
    }

    public bool HasWorldBounds => SizeXCm > 0 && SizeYCm > 0;
    public int MaxWorldXCm => MinWorldXCm + SizeXCm;
    public int MaxWorldYCm => MinWorldYCm + SizeYCm;
    public Vector2 CenterWorldCm => new(MinWorldXCm + (SizeXCm * 0.5f), MinWorldYCm + (SizeYCm * 0.5f));
}

internal readonly record struct MassNavigationRuntimeDirtyTileGrid(
    int WorldMinXCm,
    int WorldMinYCm,
    int TileSizeXCm,
    int TileSizeYCm,
    int Columns,
    int Rows,
    int MinChunkX,
    int MinChunkY,
    int MaxChunkX,
    int MaxChunkY)
{
    public bool ContainsChunk(int x, int y)
    {
        return x >= 0 &&
            y >= 0 &&
            x < Columns &&
            y < Rows &&
            x >= MinChunkX &&
            y >= MinChunkY &&
            x <= MaxChunkX &&
            y <= MaxChunkY;
    }

    public string ActiveWindowLabel => $"{MinChunkX},{MinChunkY}->{MaxChunkX},{MaxChunkY}";
}

public readonly record struct MassNavigationRuntimeAuthoredObstaclePolygon(
    int Id,
    Vector2[] PointsWorldCm,
    int MinChunkX,
    int MinChunkY,
    int MaxChunkX,
    int MaxChunkY);

public readonly record struct MassNavigationRuntimeNavDataUpdateDiagnostics(
    bool Available,
    bool ObstacleAuthoringArmed,
    int DraftPointCount,
    int AuthoredPolygonCount,
    int DirtyChunkCount,
    int ReloadedTileCount,
    int BakedTileCount,
    int ChangedTileCount,
    int BeforeTriangleCount,
    int AfterTriangleCount,
    ulong BeforeChecksumXor,
    ulong AfterChecksumXor,
    ulong BeforeGeometryHashXor,
    ulong AfterGeometryHashXor,
    int NavDataRevision,
    string Status,
    string UpdateSource,
    string QueryStatusAfterUpdate,
    int QueryPathPointCount,
    int QueryTouchedTileCount,
    bool FlowObstacleRefreshQueued,
    string ProductionGap);

public sealed class MassNavigationRuntimeBakeAuthoringRuntime
{
    private const int MaxDraftPointCount = 32;
    private const int MaxAuthoredPolygonCount = 32;
    private const int MaxDirtyChunkCount = 512;
    private const int MaxRuntimeVisibleNavMeshTiles = 256;
    private const float MinVertexSpacingCm = 120f;
    private const string RuntimeBakeBound = "none_runtime_recast_incremental_bake_bound";

    private readonly List<Vector2> _draftPoints = new(MaxDraftPointCount);
    private readonly List<MassNavigationRuntimeAuthoredObstaclePolygon> _polygons = new(MaxAuthoredPolygonCount);
    private readonly List<MassNavigationRuntimeDirtyChunk> _dirtyChunks = new(MaxDirtyChunkCount);
    private readonly List<NavTile> _lastBakedTiles = new(MaxDirtyChunkCount);
    private readonly List<NavTile> _lastVisibleNavMeshTiles = new(MaxDirtyChunkCount);
    private readonly HashSet<long> _dirtyChunkKeys = new();
    private int _nextPolygonId = 1;

    public MassNavigationRuntimeBakeAuthoringMode Mode { get; private set; }
    public bool ObstacleAuthoringArmed { get; private set; }
    public int AuthoringRevision { get; private set; }
    public int UpdateRevision { get; private set; }
    public string LastStatus { get; private set; } = "runtime_navdata_idle";
    public IReadOnlyList<Vector2> DraftPoints => _draftPoints;
    public IReadOnlyList<MassNavigationRuntimeAuthoredObstaclePolygon> AuthoredPolygons => _polygons;
    public IReadOnlyList<MassNavigationRuntimeDirtyChunk> DirtyChunks => _dirtyChunks;
    public IReadOnlyList<NavTile> LastBakedTiles => _lastBakedTiles;
    public IReadOnlyList<NavTile> LastVisibleNavMeshTiles => _lastVisibleNavMeshTiles;
    public int DraftPointCount => _draftPoints.Count;
    public int AuthoredPolygonCount => _polygons.Count;
    public int DirtyChunkCount => _dirtyChunks.Count;

    public void ArmObstaclePolygon()
    {
        Mode = MassNavigationRuntimeBakeAuthoringMode.ObstaclePolygon;
        ObstacleAuthoringArmed = true;
        LastStatus = "runtime_polygon_obstacle_authoring_armed";
        AuthoringRevision++;
    }

    public void CancelObstaclePolygonDraft()
    {
        _draftPoints.Clear();
        ObstacleAuthoringArmed = false;
        Mode = MassNavigationRuntimeBakeAuthoringMode.None;
        LastStatus = "runtime_polygon_obstacle_authoring_cancelled";
        AuthoringRevision++;
    }

    public bool TryAddObstaclePoint(
        Vector2 worldCm,
        MassNavigationBakeDataDiagnostics? diagnostics,
        out MassNavigationRuntimeDirtyChunk dirtyChunk,
        out string message)
    {
        dirtyChunk = default;
        if (!ObstacleAuthoringArmed)
        {
            message = "runtime_polygon_obstacle_authoring_not_armed";
            return false;
        }

        if (!IsFinite(worldCm))
        {
            message = "runtime_polygon_obstacle_point_not_finite";
            return false;
        }

        if (_draftPoints.Count >= MaxDraftPointCount)
        {
            message = $"runtime_polygon_obstacle_point_limit_{MaxDraftPointCount}";
            return false;
        }

        if (_draftPoints.Count > 0 &&
            Vector2.DistanceSquared(_draftPoints[^1], worldCm) < MinVertexSpacingCm * MinVertexSpacingCm)
        {
            message = "runtime_polygon_obstacle_point_too_close";
            return false;
        }

        if (!TryResolveDirtyTile(worldCm, diagnostics, out dirtyChunk, out message))
        {
            LastStatus = message;
            AuthoringRevision++;
            return false;
        }

        _draftPoints.Add(worldCm);
        MarkDirtyChunk(dirtyChunk);
        LastStatus = $"runtime_polygon_obstacle_point_added:{_draftPoints.Count}";
        AuthoringRevision++;
        message = LastStatus;
        return true;
    }

    public bool TryCloseObstaclePolygon(MassNavigationBakeDataDiagnostics? diagnostics, out string message)
    {
        if (_draftPoints.Count < 3)
        {
            message = "runtime_polygon_obstacle_needs_three_points";
            LastStatus = message;
            AuthoringRevision++;
            return false;
        }

        if (_polygons.Count >= MaxAuthoredPolygonCount)
        {
            message = $"runtime_polygon_obstacle_limit_{MaxAuthoredPolygonCount}";
            LastStatus = message;
            AuthoringRevision++;
            return false;
        }

        Vector2[] points = _draftPoints.ToArray();
        if (!TryResolveDirtyTileBounds(
                points,
                diagnostics,
                out int minChunkX,
                out int minChunkY,
                out int maxChunkX,
                out int maxChunkY,
                out MassNavigationRuntimeDirtyTileGrid grid,
                out message))
        {
            LastStatus = message;
            AuthoringRevision++;
            return false;
        }

        MarkDirtyChunkBounds(minChunkX, minChunkY, maxChunkX, maxChunkY, grid);
        var polygon = new MassNavigationRuntimeAuthoredObstaclePolygon(
            _nextPolygonId++,
            points,
            minChunkX,
            minChunkY,
            maxChunkX,
            maxChunkY);
        _polygons.Add(polygon);
        _draftPoints.Clear();
        ObstacleAuthoringArmed = false;
        Mode = MassNavigationRuntimeBakeAuthoringMode.None;
        LastStatus = $"runtime_polygon_obstacle_closed:{polygon.Id}";
        AuthoringRevision++;
        message = LastStatus;
        return true;
    }

    public MassNavigationRuntimeNavDataUpdateDiagnostics RequestRuntimeNavDataUpdate(
        MassNavigationSimulationRuntime simulation,
        NavMeshBakeConfig? navMeshConfig,
        NavQueryServiceRegistry? navRegistry,
        NavMeshProfileRegistry? navProfiles,
        IPathService? pathService,
        PathStore? pathStore)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        RuntimeBakeResult bakeResult = BakeDirtyNavTiles(
            navMeshConfig,
            navRegistry,
            navProfiles,
            simulation.BakeDataDiagnostics);
        simulation.FlowTuning.ForceRefreshFlow = true;
        simulation.FlowTuning.ForceRefreshObstacles = true;
        simulation.MassFlow.RequestFlowRebuild();

        if (bakeResult.BakedTileCount > 0 && pathService is PathServiceRouter router)
        {
            router.ClearCache();
        }

        UpdateRevision++;

        string queryStatus = "path_service_missing";
        int queryPathPointCount = 0;
        int queryTouchedTileCount = 0;
        bool allowEndpointReuse = simulation.AcceptanceDiagnostics.HasReusablePathQueryEndpoints;
        if (pathService != null &&
            pathStore != null &&
            TryResolveQueryEndpoints(simulation, navRegistry, navProfiles, out Vector2 startWorldCm, out Vector2 goalWorldCm))
        {
            simulation.AcceptanceDiagnostics.RecordPathOnlyPreviewQuery(
                pathService,
                pathStore,
                startWorldCm,
                goalWorldCm,
                PathDomain.NavMesh,
                allowEndpointReuse);
            queryStatus = simulation.AcceptanceDiagnostics.PathOnlyQuery.Status;
            queryPathPointCount = simulation.AcceptanceDiagnostics.PathOnlyQuery.PathPointCount;
            queryTouchedTileCount = simulation.AcceptanceDiagnostics.PathOnlyQuery.TouchedTileCount;
        }
        else if (pathService != null && pathStore != null)
        {
            queryStatus = "path_endpoints_missing";
        }

        string status = _dirtyChunks.Count == 0 && _polygons.Count == 0
            ? "runtime_navdata_no_dirty_chunks"
            : bakeResult.BakedTileCount > 0
                ? "runtime_navdata_recast_baked"
                : "runtime_navdata_dirty_marked";
        LastStatus = status;
        AuthoringRevision++;

        var diagnostics = new MassNavigationRuntimeNavDataUpdateDiagnostics(
            Available: true,
            ObstacleAuthoringArmed: ObstacleAuthoringArmed,
            DraftPointCount: _draftPoints.Count,
            AuthoredPolygonCount: _polygons.Count,
            DirtyChunkCount: _dirtyChunks.Count,
            ReloadedTileCount: bakeResult.BakedTileCount,
            BakedTileCount: bakeResult.BakedTileCount,
            ChangedTileCount: bakeResult.ChangedTileCount,
            BeforeTriangleCount: bakeResult.BeforeTriangleCount,
            AfterTriangleCount: bakeResult.AfterTriangleCount,
            BeforeChecksumXor: bakeResult.BeforeChecksumXor,
            AfterChecksumXor: bakeResult.AfterChecksumXor,
            BeforeGeometryHashXor: bakeResult.BeforeGeometryHashXor,
            AfterGeometryHashXor: bakeResult.AfterGeometryHashXor,
            NavDataRevision: UpdateRevision,
            Status: status,
            UpdateSource: bakeResult.Source,
            QueryStatusAfterUpdate: queryStatus,
            QueryPathPointCount: queryPathPointCount,
            QueryTouchedTileCount: queryTouchedTileCount,
            FlowObstacleRefreshQueued: true,
            ProductionGap: bakeResult.BakedTileCount > 0
                ? RuntimeBakeBound
                : bakeResult.ProductionGap);
        simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(diagnostics);
        return diagnostics;
    }

    public MassNavigationRuntimeNavDataUpdateDiagnostics CreateSnapshot()
    {
        return new MassNavigationRuntimeNavDataUpdateDiagnostics(
            Available: true,
            ObstacleAuthoringArmed: ObstacleAuthoringArmed,
            DraftPointCount: _draftPoints.Count,
            AuthoredPolygonCount: _polygons.Count,
            DirtyChunkCount: _dirtyChunks.Count,
            ReloadedTileCount: 0,
            BakedTileCount: 0,
            ChangedTileCount: 0,
            BeforeTriangleCount: 0,
            AfterTriangleCount: 0,
            BeforeChecksumXor: 0UL,
            AfterChecksumXor: 0UL,
            BeforeGeometryHashXor: 0UL,
            AfterGeometryHashXor: 0UL,
            NavDataRevision: UpdateRevision,
            Status: LastStatus,
            UpdateSource: "runtime_authoring_state",
            QueryStatusAfterUpdate: "not_run",
            QueryPathPointCount: 0,
            QueryTouchedTileCount: 0,
            FlowObstacleRefreshQueued: false,
            ProductionGap: "runtime_navdata_authoring_not_baked");
    }

    private RuntimeBakeResult BakeDirtyNavTiles(
        NavMeshBakeConfig? navMeshConfig,
        NavQueryServiceRegistry? navRegistry,
        NavMeshProfileRegistry? navProfiles,
        MassNavigationBakeDataDiagnostics? diagnostics)
    {
        _lastBakedTiles.Clear();
        _lastVisibleNavMeshTiles.Clear();

        if (navRegistry == null || navProfiles == null || diagnostics == null)
        {
            return RuntimeBakeResult.Unavailable("nav_query_registry_missing");
        }

        if (navMeshConfig?.Profiles == null || navMeshConfig.Profiles.Count == 0)
        {
            return RuntimeBakeResult.Unavailable("navmesh_bake_config_missing");
        }

        if (_dirtyChunks.Count == 0 || _polygons.Count == 0)
        {
            return RuntimeBakeResult.Unavailable("runtime_dirty_chunks_or_polygons_missing");
        }

        if (string.IsNullOrWhiteSpace(diagnostics.LogicHeightmapSource) ||
            !File.Exists(diagnostics.LogicHeightmapSource))
        {
            return RuntimeBakeResult.Unavailable("logic_heightmap_source_missing");
        }

        if (!TryResolveProfile(diagnostics, navProfiles, out int layer, out int profileIndex, out string profileId))
        {
            return RuntimeBakeResult.Unavailable("nav_profile_missing");
        }

        if (!TryResolveProfileConfig(navMeshConfig, profileId, out NavAgentProfileConfig? profileConfig))
        {
            return RuntimeBakeResult.Unavailable($"nav_profile_config_missing:{profileId}");
        }

        if (!navRegistry.TryGetStore(layer, profileIndex, out NavTileStore store))
        {
            return RuntimeBakeResult.Unavailable($"nav_store_missing:{layer}/{profileId}");
        }

        var legacyConfig = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
        var result = new RuntimeBakeResult
        {
            Source = $"LogicHeightmap/RecastNavTileBaker/NavTileStore.Replace:{layer}/{profileId}",
            ProductionGap = RuntimeBakeBound
        };

        using LogicHeightmapFileReader reader = LogicHeightmapFileReader.Open(diagnostics.LogicHeightmapSource);
        if (!MassNavigationNavMeshRuntimeCoordinateMapper.TryCreate(diagnostics, reader, out MassNavigationNavMeshRuntimeCoordinateMapper mapper))
        {
            return RuntimeBakeResult.Unavailable("runtime_navmesh_coordinate_mapper_missing");
        }

        NavObstacleSet obstacles = BuildObstacleSet(navMeshConfig, layer, mapper);
        MassNavigationRuntimeDirtyChunk[] bakeDirtyChunks = BuildBakeDirtyChunks(reader, diagnostics);
        for (int i = 0; i < bakeDirtyChunks.Length; i++)
        {
            MassNavigationRuntimeDirtyChunk chunk = bakeDirtyChunks[i];
            if (chunk.X < 0 ||
                chunk.Y < 0 ||
                chunk.X >= reader.WidthInChunks ||
                chunk.Y >= reader.HeightInChunks)
            {
                continue;
            }

            NavTileId id = new(chunk.X, chunk.Y, layer);
            NavTile? before = TryGetCurrentTile(store, id);
            LogicHeightmap tileWindow = reader.ReadTileWindow(chunk.X, chunk.Y, radiusChunks: 1);
            uint tileVersion = NextTileVersion(before, UpdateRevision);
            if (!RecastNavTileBaker.TryBake(
                    tileWindow,
                    chunk.X,
                    chunk.Y,
                    tileVersion,
                    legacyConfig,
                    profileConfig,
                    layer,
                    obstacles,
                    out NavTile bakedTile,
                    out NavBakeArtifact artifact))
            {
                result.FailedTileCount++;
                result.Source = $"{result.Source};failed={result.FailedTileCount}:{artifact.ErrorCode}:{artifact.Stage}";
                continue;
            }

            NavTile normalizedTile = NormalizeTile(bakedTile);
            ulong beforeGeometryHash = ComputeGeometryHash(before);
            ulong afterGeometryHash = ComputeGeometryHash(normalizedTile);
            store.Replace(normalizedTile);
            _lastBakedTiles.Add(normalizedTile);
            result.BakedTileCount++;
            result.BeforeTriangleCount += before?.TriangleCount ?? 0;
            result.AfterTriangleCount += normalizedTile.TriangleCount;
            result.BeforeChecksumXor ^= before?.Checksum ?? 0UL;
            result.AfterChecksumXor ^= normalizedTile.Checksum;
            result.BeforeGeometryHashXor ^= beforeGeometryHash;
            result.AfterGeometryHashXor ^= afterGeometryHash;
            if (before == null || beforeGeometryHash != afterGeometryHash)
            {
                result.ChangedTileCount++;
            }
        }

        CaptureRuntimeVisibleTiles(store, layer, diagnostics, bakeDirtyChunks, ref result);

        if (result.BakedTileCount == 0)
        {
            result.ProductionGap = result.FailedTileCount > 0
                ? "runtime_recast_bake_failed"
                : $"runtime_dirty_chunks_outside_lhtm_window:{layer}/{profileId}";
        }

        result.Source = $"{result.Source};baked={result.BakedTileCount};changed={result.ChangedTileCount};tris={result.BeforeTriangleCount}->{result.AfterTriangleCount};visibleTiles={result.ActiveWindowTileCount};visibleTris={result.ActiveWindowTriangleCount}";
        return result;
    }

    private void CaptureRuntimeVisibleTiles(
        NavTileStore store,
        int layer,
        MassNavigationBakeDataDiagnostics diagnostics,
        IReadOnlyList<MassNavigationRuntimeDirtyChunk> dirtyChunks,
        ref RuntimeBakeResult result)
    {
        var keys = new HashSet<long>();
        for (int i = 0; i < _lastBakedTiles.Count; i++)
        {
            AddVisibleRuntimeTile(_lastBakedTiles[i], keys, ref result);
        }

        for (int i = 0; i < dirtyChunks.Count && _lastVisibleNavMeshTiles.Count < MaxRuntimeVisibleNavMeshTiles; i++)
        {
            MassNavigationRuntimeDirtyChunk dirty = dirtyChunks[i];
            for (int y = dirty.Y - 1; y <= dirty.Y + 1 && _lastVisibleNavMeshTiles.Count < MaxRuntimeVisibleNavMeshTiles; y++)
            {
                for (int x = dirty.X - 1; x <= dirty.X + 1 && _lastVisibleNavMeshTiles.Count < MaxRuntimeVisibleNavMeshTiles; x++)
                {
                    if (!ContainsRuntimeVisibleChunk(diagnostics, x, y))
                    {
                        continue;
                    }

                    long key = BuildDirtyChunkKey(x, y);
                    if (keys.Contains(key))
                    {
                        continue;
                    }

                    try
                    {
                        AddVisibleRuntimeTile(store.GetOrLoad(new NavTileId(x, y, layer)), keys, ref result);
                    }
                    catch (IOException)
                    {
                    }
                    catch (InvalidDataException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
        }
    }

    private void AddVisibleRuntimeTile(
        NavTile tile,
        HashSet<long> keys,
        ref RuntimeBakeResult result)
    {
        if (_lastVisibleNavMeshTiles.Count >= MaxRuntimeVisibleNavMeshTiles)
        {
            return;
        }

        long key = BuildDirtyChunkKey(tile.TileId.ChunkX, tile.TileId.ChunkY);
        if (!keys.Add(key))
        {
            return;
        }

        _lastVisibleNavMeshTiles.Add(tile);
        result.ActiveWindowTileCount++;
        result.ActiveWindowTriangleCount += tile.TriangleCount;
    }

    private static bool ContainsRuntimeVisibleChunk(
        MassNavigationBakeDataDiagnostics diagnostics,
        int x,
        int y)
    {
        if (x < 0 || y < 0 || x >= diagnostics.MacroChunkColumns || y >= diagnostics.MacroChunkRows)
        {
            return false;
        }

        return !diagnostics.HasActiveNavMeshWindow ||
            (x >= diagnostics.ActiveNavMeshMinChunkX &&
                y >= diagnostics.ActiveNavMeshMinChunkY &&
                x <= diagnostics.ActiveNavMeshMaxChunkX &&
                y <= diagnostics.ActiveNavMeshMaxChunkY);
    }

    private static bool TryResolveProfile(
        MassNavigationBakeDataDiagnostics diagnostics,
        NavMeshProfileRegistry navProfiles,
        out int layer,
        out int profileIndex,
        out string profileId)
    {
        for (int i = 0; i < diagnostics.Profiles.Length; i++)
        {
            MassNavigationBakeDataProfileSummary profile = diagnostics.Profiles[i];
            if (!string.IsNullOrWhiteSpace(profile.NavProfileId) &&
                navProfiles.TryGetIndex(profile.NavProfileId, out profileIndex))
            {
                layer = profile.Layer;
                profileId = profile.NavProfileId;
                return true;
            }
        }

        layer = 0;
        profileIndex = -1;
        profileId = string.Empty;
        return false;
    }

    private static bool TryResolveProfileConfig(
        NavMeshBakeConfig navMeshConfig,
        string profileId,
        out NavAgentProfileConfig profileConfig)
    {
        for (int i = 0; i < navMeshConfig.Profiles.Count; i++)
        {
            NavAgentProfileConfig? profile = navMeshConfig.Profiles[i];
            if (profile != null &&
                string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
            {
                profileConfig = profile;
                return true;
            }
        }

        profileConfig = null!;
        return false;
    }

    private NavObstacleSet BuildObstacleSet(
        NavMeshBakeConfig navMeshConfig,
        int layer,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper)
    {
        string layerId = ResolveLayerId(navMeshConfig, layer);
        var set = new NavObstacleSet();
        for (int i = 0; i < _polygons.Count; i++)
        {
            MassNavigationRuntimeAuthoredObstaclePolygon polygon = _polygons[i];
            if (polygon.PointsWorldCm.Length < 3)
            {
                continue;
            }

            var obstacle = new NavObstacle
            {
                Id = $"runtime_polygon_{polygon.Id}",
                Enabled = true,
                Kind = NavObstacleKind.Polygon,
                LayerId = layerId
            };

            for (int p = 0; p < polygon.PointsWorldCm.Length; p++)
            {
                Vector2 point = polygon.PointsWorldCm[p];
                obstacle.Points.Add(new NavPointCm(
                    mapper.WorldToBakedAbsoluteXcm(point.X),
                    mapper.WorldToBakedAbsoluteYcm(point.Y)));
            }

            set.Obstacles.Add(obstacle);
        }

        return set;
    }

    private static string ResolveLayerId(NavMeshBakeConfig navMeshConfig, int layer)
    {
        if (navMeshConfig.Layers != null)
        {
            for (int i = 0; i < navMeshConfig.Layers.Count; i++)
            {
                NavLayerConfig? item = navMeshConfig.Layers[i];
                if (item != null &&
                    item.Layer == layer &&
                    !string.IsNullOrWhiteSpace(item.Id))
                {
                    return item.Id;
                }
            }
        }

        return layer switch
        {
            0 => "Ground",
            1 => "Water",
            2 => "Air",
            3 => "Mountain",
            _ => "Ground"
        };
    }

    private static NavTile? TryGetCurrentTile(NavTileStore store, NavTileId id)
    {
        if (store.TryGet(id, out NavTile loaded))
        {
            return loaded;
        }

        try
        {
            return store.GetOrLoad(id);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static uint NextTileVersion(NavTile? before, int updateRevision)
    {
        if (before != null && before.TileVersion < uint.MaxValue)
        {
            return before.TileVersion + 1U;
        }

        return (uint)Math.Max(1, updateRevision + 1);
    }

    private static NavTile NormalizeTile(NavTile tile)
    {
        using var stream = new MemoryStream();
        NavTileBinary.Write(stream, tile);
        stream.Position = 0;
        return NavTileBinary.Read(stream);
    }

    private MassNavigationRuntimeDirtyChunk[] BuildBakeDirtyChunks(
        LogicHeightmapFileReader reader,
        MassNavigationBakeDataDiagnostics diagnostics)
    {
        MassNavigationRuntimeDirtyTileGrid grid = CreateDirtyTileGrid(reader, diagnostics);
        var result = new List<MassNavigationRuntimeDirtyChunk>(Math.Max(1, _dirtyChunks.Count));
        var keys = new HashSet<long>();

        for (int i = 0; i < _polygons.Count; i++)
        {
            MassNavigationRuntimeAuthoredObstaclePolygon polygon = _polygons[i];
            if (polygon.PointsWorldCm.Length < 3)
            {
                continue;
            }

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            for (int p = 0; p < polygon.PointsWorldCm.Length; p++)
            {
                Vector2 point = polygon.PointsWorldCm[p];
                int chunkX = Math.Clamp(
                    (int)MathF.Floor((point.X - grid.WorldMinXCm) / grid.TileSizeXCm),
                    0,
                    grid.Columns - 1);
                int chunkY = Math.Clamp(
                    (int)MathF.Floor((point.Y - grid.WorldMinYCm) / grid.TileSizeYCm),
                    0,
                    grid.Rows - 1);
                minX = Math.Min(minX, chunkX);
                minY = Math.Min(minY, chunkY);
                maxX = Math.Max(maxX, chunkX);
                maxY = Math.Max(maxY, chunkY);
            }

            if (minX == int.MaxValue)
            {
                continue;
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    MassNavigationRuntimeDirtyChunk dirtyChunk = CreateDirtyTileChunk(x, y, grid);
                    AddBakeDirtyChunk(dirtyChunk, result, keys);
                    MarkDirtyChunk(dirtyChunk);
                }
            }
        }

        if (result.Count == 0)
        {
            for (int i = 0; i < _dirtyChunks.Count; i++)
            {
                MassNavigationRuntimeDirtyChunk chunk = _dirtyChunks[i];
                AddBakeDirtyChunk(chunk.HasWorldBounds ? chunk : CreateDirtyTileChunk(chunk.X, chunk.Y, grid), result, keys);
            }
        }

        return result.ToArray();
    }

    private static void AddBakeDirtyChunk(
        MassNavigationRuntimeDirtyChunk chunk,
        List<MassNavigationRuntimeDirtyChunk> result,
        HashSet<long> keys)
    {
        if (keys.Add(BuildDirtyChunkKey(chunk.X, chunk.Y)))
        {
            result.Add(chunk);
        }
    }

    private static ulong ComputeGeometryHash(NavTile? tile)
    {
        if (tile == null)
        {
            return 0UL;
        }

        ulong hash = 1469598103934665603UL;
        AddInt(ref hash, tile.TileId.ChunkX);
        AddInt(ref hash, tile.TileId.ChunkY);
        AddInt(ref hash, tile.TileId.Layer);
        AddInt(ref hash, tile.OriginXcm);
        AddInt(ref hash, tile.OriginZcm);
        AddArray(ref hash, tile.VertexXcm);
        AddArray(ref hash, tile.VertexYcm);
        AddArray(ref hash, tile.VertexZcm);
        AddArray(ref hash, tile.TriA);
        AddArray(ref hash, tile.TriB);
        AddArray(ref hash, tile.TriC);
        AddArray(ref hash, tile.N0);
        AddArray(ref hash, tile.N1);
        AddArray(ref hash, tile.N2);
        for (int i = 0; i < tile.TriAreaIds.Length; i++)
        {
            AddByte(ref hash, tile.TriAreaIds[i]);
        }

        for (int i = 0; i < tile.Portals.Length; i++)
        {
            NavBorderPortal portal = tile.Portals[i];
            AddByte(ref hash, (byte)portal.Side);
            AddInt(ref hash, portal.U0);
            AddInt(ref hash, portal.V0);
            AddInt(ref hash, portal.U1);
            AddInt(ref hash, portal.V1);
            AddInt(ref hash, portal.LeftXcm);
            AddInt(ref hash, portal.LeftZcm);
            AddInt(ref hash, portal.RightXcm);
            AddInt(ref hash, portal.RightZcm);
            AddInt(ref hash, portal.ClearanceCm);
        }

        return hash;
    }

    private static void AddArray(ref ulong hash, int[] values)
    {
        AddInt(ref hash, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            AddInt(ref hash, values[i]);
        }
    }

    private static void AddInt(ref ulong hash, int value)
    {
        unchecked
        {
            uint raw = (uint)value;
            AddByte(ref hash, (byte)raw);
            AddByte(ref hash, (byte)(raw >> 8));
            AddByte(ref hash, (byte)(raw >> 16));
            AddByte(ref hash, (byte)(raw >> 24));
        }
    }

    private static void AddByte(ref ulong hash, byte value)
    {
        hash ^= value;
        hash *= 1099511628211UL;
    }

    private struct RuntimeBakeResult
    {
        public int BakedTileCount;
        public int ChangedTileCount;
        public int FailedTileCount;
        public int ActiveWindowTileCount;
        public int ActiveWindowTriangleCount;
        public int BeforeTriangleCount;
        public int AfterTriangleCount;
        public ulong BeforeChecksumXor;
        public ulong AfterChecksumXor;
        public ulong BeforeGeometryHashXor;
        public ulong AfterGeometryHashXor;
        public string Source;
        public string ProductionGap;

        public static RuntimeBakeResult Unavailable(string reason)
        {
            return new RuntimeBakeResult
            {
                Source = reason,
                ProductionGap = reason
            };
        }
    }

    private static bool TryResolveQueryEndpoints(
        MassNavigationSimulationRuntime simulation,
        NavQueryServiceRegistry? navRegistry,
        NavMeshProfileRegistry? navProfiles,
        out Vector2 startWorldCm,
        out Vector2 goalWorldCm)
    {
        MassNavigationPathOnlyQueryDiagnostics query = simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (simulation.AcceptanceDiagnostics.HasReusablePathQueryEndpoints &&
            IsFinite(query.StartWorldCm) &&
            IsFinite(query.GoalWorldCm) &&
            query.StartWorldCm != Vector2.Zero &&
            query.GoalWorldCm != Vector2.Zero)
        {
            startWorldCm = query.StartWorldCm;
            goalWorldCm = query.GoalWorldCm;
            return true;
        }

        MassNavigationBakeDataDiagnostics? diagnostics = simulation.BakeDataDiagnostics;
        if (diagnostics == null)
        {
            startWorldCm = default;
            goalWorldCm = default;
            return false;
        }

        if (MassNavigationRuntimeWorldPathEndpointResolver.TryResolve(
                diagnostics,
                navRegistry,
                navProfiles,
                out MassNavigationRuntimeWorldPathEndpointResult endpoints))
        {
            startWorldCm = endpoints.StartWorldCm;
            goalWorldCm = endpoints.GoalWorldCm;
            return true;
        }

        startWorldCm = default;
        goalWorldCm = default;
        return false;
    }

    private void MarkDirtyChunkBounds(
        int minChunkX,
        int minChunkY,
        int maxChunkX,
        int maxChunkY,
        MassNavigationRuntimeDirtyTileGrid grid)
    {
        if (minChunkX < 0 || minChunkY < 0 || maxChunkX < minChunkX || maxChunkY < minChunkY)
        {
            return;
        }

        for (int y = minChunkY; y <= maxChunkY; y++)
        {
            for (int x = minChunkX; x <= maxChunkX; x++)
            {
                MarkDirtyChunk(CreateDirtyTileChunk(x, y, grid));
            }
        }
    }

    private void MarkDirtyChunk(MassNavigationRuntimeDirtyChunk chunk)
    {
        if (_dirtyChunks.Count >= MaxDirtyChunkCount)
        {
            return;
        }

        long key = BuildDirtyChunkKey(chunk.X, chunk.Y);
        if (!_dirtyChunkKeys.Add(key))
        {
            return;
        }

        _dirtyChunks.Add(chunk);
    }

    private static bool TryResolveDirtyTileBounds(
        ReadOnlySpan<Vector2> points,
        MassNavigationBakeDataDiagnostics? diagnostics,
        out int minChunkX,
        out int minChunkY,
        out int maxChunkX,
        out int maxChunkY,
        out MassNavigationRuntimeDirtyTileGrid grid,
        out string failureReason)
    {
        grid = default;
        failureReason = string.Empty;
        minChunkX = int.MaxValue;
        minChunkY = int.MaxValue;
        maxChunkX = int.MinValue;
        maxChunkY = int.MinValue;
        if (!TryResolveDirtyTileGrid(diagnostics, out grid, out failureReason))
        {
            minChunkX = -1;
            minChunkY = -1;
            maxChunkX = -1;
            maxChunkY = -1;
            return false;
        }

        for (int i = 0; i < points.Length; i++)
        {
            MassNavigationRuntimeDirtyChunk chunk = ResolveDirtyTile(points[i], grid);

            minChunkX = Math.Min(minChunkX, chunk.X);
            minChunkY = Math.Min(minChunkY, chunk.Y);
            maxChunkX = Math.Max(maxChunkX, chunk.X);
            maxChunkY = Math.Max(maxChunkY, chunk.Y);
        }

        if (minChunkX == int.MaxValue)
        {
            minChunkX = -1;
            minChunkY = -1;
            maxChunkX = -1;
            maxChunkY = -1;
            failureReason = "runtime_dirty_tile_points_missing";
            return false;
        }

        return true;
    }

    private static bool TryResolveDirtyTile(
        Vector2 worldCm,
        MassNavigationBakeDataDiagnostics? diagnostics,
        out MassNavigationRuntimeDirtyChunk chunk,
        out string failureReason)
    {
        chunk = default;
        if (!TryResolveDirtyTileGrid(diagnostics, out MassNavigationRuntimeDirtyTileGrid grid, out failureReason))
        {
            return false;
        }

        chunk = ResolveDirtyTile(worldCm, grid);
        return true;
    }

    private static MassNavigationRuntimeDirtyChunk ResolveDirtyTile(
        Vector2 worldCm,
        MassNavigationRuntimeDirtyTileGrid grid)
    {
        int x = Math.Clamp(
            (int)MathF.Floor((worldCm.X - grid.WorldMinXCm) / grid.TileSizeXCm),
            0,
            grid.Columns - 1);
        int y = Math.Clamp(
            (int)MathF.Floor((worldCm.Y - grid.WorldMinYCm) / grid.TileSizeYCm),
            0,
            grid.Rows - 1);
        return CreateDirtyTileChunk(x, y, grid);
    }

    private static MassNavigationRuntimeDirtyChunk CreateDirtyTileChunk(
        int x,
        int y,
        MassNavigationRuntimeDirtyTileGrid grid)
    {
        return new MassNavigationRuntimeDirtyChunk(
            x,
            y,
            grid.WorldMinXCm + (x * grid.TileSizeXCm),
            grid.WorldMinYCm + (y * grid.TileSizeYCm),
            grid.TileSizeXCm,
            grid.TileSizeYCm,
            MassNavigationRuntimeDirtyChunkGrid.NavTile);
    }

    private static bool TryResolveDirtyTileGrid(
        MassNavigationBakeDataDiagnostics? diagnostics,
        out MassNavigationRuntimeDirtyTileGrid grid,
        out string failureReason)
    {
        grid = default;
        if (diagnostics == null)
        {
            failureReason = "runtime_dirty_tile_bake_diagnostics_missing";
            return false;
        }

        if (string.IsNullOrWhiteSpace(diagnostics.LogicHeightmapSource) ||
            !File.Exists(diagnostics.LogicHeightmapSource))
        {
            failureReason = "runtime_dirty_tile_logic_heightmap_source_missing";
            return false;
        }

        try
        {
            using LogicHeightmapFileReader reader = LogicHeightmapFileReader.Open(diagnostics.LogicHeightmapSource);
            grid = CreateDirtyTileGrid(reader, diagnostics);
            failureReason = string.Empty;
            return true;
        }
        catch (InvalidDataException)
        {
            failureReason = "runtime_dirty_tile_logic_heightmap_source_invalid";
            return false;
        }
        catch (InvalidOperationException)
        {
            failureReason = "runtime_dirty_tile_logic_heightmap_source_invalid";
            return false;
        }
    }

    private static MassNavigationRuntimeDirtyTileGrid CreateDirtyTileGrid(
        LogicHeightmapFileReader reader,
        MassNavigationBakeDataDiagnostics diagnostics)
    {
        if (!MassNavigationNavMeshRuntimeCoordinateMapper.TryCreate(
                diagnostics,
                reader,
                out MassNavigationNavMeshRuntimeCoordinateMapper mapper))
        {
            throw new InvalidOperationException("Mass navigation runtime dirty tile grid requires a valid navmesh coordinate mapper.");
        }

        return new MassNavigationRuntimeDirtyTileGrid(
            diagnostics.WorldMinXCm,
            diagnostics.WorldMinYCm,
            mapper.RuntimeTileWidthCm,
            mapper.RuntimeTileHeightCm,
            Math.Max(1, reader.WidthInChunks),
            Math.Max(1, reader.HeightInChunks),
            0,
            0,
            Math.Max(0, reader.WidthInChunks - 1),
            Math.Max(0, reader.HeightInChunks - 1));
    }

    private static long BuildDirtyChunkKey(int x, int y)
    {
        return (((long)x) << 32) ^ (uint)y;
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
