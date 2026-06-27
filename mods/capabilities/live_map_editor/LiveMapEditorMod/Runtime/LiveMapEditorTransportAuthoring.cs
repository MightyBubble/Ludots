using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Scripting;
using Ludots.Core.TransportNetwork;
using Ludots.WebUI.DataPlane;

namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorTransportAuthoring : IDisposable
{
    private const int SelectionRadiusCm = 220;
    private const int ProjectionRadiusCm = 200000;
    private const int RoutePointCapacity = 256;

    private readonly List<TransportNetworkPoint> _draftPoints = new();
    private TransportNetworkAsset? _asset;
    private TransportNetworkBakedAsset? _baked;
    private TransportNetworkChunkGraphSource? _graphSource;
    private TransportNetworkRibbonSource? _ribbonSource;
    private SurfaceSourcePayloadRegistry? _payloads;
    private string _loadedMapId = string.Empty;
    private int _nextRouteRequestId = 1;

    public bool Available { get; private set; }
    public string Status { get; private set; } = "unavailable";
    public string LastError { get; private set; } = string.Empty;
    public string LastBakeMessage { get; private set; } = string.Empty;
    public string LastSaveMessage { get; private set; } = string.Empty;
    public string Mode { get; private set; } = "node";
    public string SelectedNodeId { get; private set; } = string.Empty;
    public string SelectedSegmentId { get; private set; } = string.Empty;
    public int SelectedPointIndex { get; private set; } = -1;
    public bool HasRouteStart { get; private set; }
    public WorldCmInt2 RouteStart { get; private set; }
    public bool HasRouteGoal { get; private set; }
    public WorldCmInt2 RouteGoal { get; private set; }
    public string RouteAgentTypeId { get; private set; } = string.Empty;
    public PathStatus RouteStatus { get; private set; } = PathStatus.NotReady;
    public PathDomain RouteResolvedDomain { get; private set; } = PathDomain.None;
    public int RouteExpanded { get; private set; }
    public int RouteErrorCode { get; private set; }
    public long LastRouteElapsedMicroseconds { get; private set; }
    public int[] RoutePathXcm { get; private set; } = Array.Empty<int>();
    public int[] RoutePathYcm { get; private set; } = Array.Empty<int>();

    public TransportNetworkAsset? Asset => _asset;
    public IReadOnlyList<TransportNetworkPoint> DraftPoints => _draftPoints;

    public void Dispose()
    {
        ClearRuntimeSources();
    }

    public void SetMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            throw new InvalidOperationException("Transport editor mode is required.");
        }

        Mode = mode.Trim() switch
        {
            "node" => "node",
            "segment" => "segment",
            "route" => "route",
            _ => throw new InvalidOperationException($"Unknown transport editor mode '{mode}'.")
        };
    }

    public bool TryEnsureLoaded(GameEngine engine)
    {
        if (engine == null) throw new ArgumentNullException(nameof(engine));
        MapSession? session = engine.CurrentMapSession;
        string mapId = session?.MapId.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mapId))
        {
            ResetAuthoringState("unavailable", "Transport editing requires a focused map.");
            return false;
        }

        if (_asset != null && string.Equals(_loadedMapId, mapId, StringComparison.Ordinal))
        {
            return true;
        }

        ClearRuntimeSources();
        _asset = null;
        _loadedMapId = mapId;
        SelectedNodeId = string.Empty;
        SelectedSegmentId = string.Empty;
        SelectedPointIndex = -1;
        _draftPoints.Clear();

        try
        {
            _asset = new TransportNetworkAssetLoader(engine.ConfigPipeline)
                .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
            Available = true;
            Status = "loaded";
            LastError = string.Empty;
            RebuildDerivedOutputs(engine, "loaded");
            return true;
        }
        catch (Exception ex)
        {
            ResetAuthoringState("unavailable", ex.Message);
            return false;
        }
    }

    public WebUiCommandResult AddNode(
        GameEngine engine,
        string? id,
        string? kind,
        string? tags,
        int? xCm,
        int? yCm,
        bool hasPickedWorld,
        WorldCmInt2 pickedWorld)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            WorldCmInt2 world = ResolveWorld(xCm, yCm, hasPickedWorld, pickedWorld);
            var node = new TransportNetworkNode
            {
                Id = string.IsNullOrWhiteSpace(id) ? GenerateNodeId(asset) : id!,
                Xcm = world.X,
                Ycm = world.Y,
                Kind = ParseEnum(kind, TransportNetworkNodeKind.Normal, nameof(TransportNetworkNodeKind)),
                Tags = ParseTags(tags)
            };

            asset.Nodes.Add(node);
            ValidateAndRebuild(engine, $"added node {node.Id}");
            SelectedNodeId = node.Id;
            SelectedSegmentId = string.Empty;
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_add_node_failed", ex);
        }
    }

    public WebUiCommandResult SelectNearestNode(GameEngine engine, int? xCm, int? yCm, bool hasPickedWorld, WorldCmInt2 pickedWorld)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            WorldCmInt2 world = ResolveWorld(xCm, yCm, hasPickedWorld, pickedWorld);
            TransportNetworkNode? node = FindNearestNode(asset, world, SelectionRadiusCm);
            if (node == null)
            {
                return WebUiCommandResult.Fail("transport_node_not_found", "No transport node was found near the requested point.");
            }

            SelectedNodeId = node.Id;
            SelectedSegmentId = string.Empty;
            SelectedPointIndex = -1;
            LastError = string.Empty;
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_select_node_failed", ex);
        }
    }

    public WebUiCommandResult MoveSelectedNode(GameEngine engine, int? xCm, int? yCm, bool hasPickedWorld, WorldCmInt2 pickedWorld)
    {
        try
        {
            TransportNetworkNode node = RequireSelectedNode(engine);
            WorldCmInt2 world = ResolveWorld(xCm, yCm, hasPickedWorld, pickedWorld);
            node.Xcm = world.X;
            node.Ycm = world.Y;
            ValidateAndRebuild(engine, $"moved node {node.Id}");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_move_node_failed", ex);
        }
    }

    public WebUiCommandResult UpdateSelectedNode(GameEngine engine, string? id, string? kind, string? tags)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            TransportNetworkNode node = RequireSelectedNode(engine);
            string oldId = node.Id;
            string newId = string.IsNullOrWhiteSpace(id) ? oldId : id!;
            if (!string.Equals(newId, oldId, StringComparison.Ordinal))
            {
                for (int i = 0; i < asset.Nodes.Count; i++)
                {
                    if (!ReferenceEquals(asset.Nodes[i], node) &&
                        string.Equals(asset.Nodes[i].Id, newId, StringComparison.Ordinal))
                    {
                        return WebUiCommandResult.Fail(
                            "transport_node_duplicate_id",
                            $"Transport node id '{newId}' is already used.");
                    }
                }

                node.Id = newId;
                RewriteNodeReferences(asset, oldId, newId);
                SelectedNodeId = newId;
            }

            if (kind != null)
            {
                node.Kind = ParseEnum(kind, node.Kind, nameof(TransportNetworkNodeKind));
            }

            if (tags != null)
            {
                node.Tags = ParseTags(tags);
            }

            ValidateAndRebuild(engine, $"updated node {node.Id}");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_update_node_failed", ex);
        }
    }

    public WebUiCommandResult DeleteSelectedNode(GameEngine engine)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            TransportNetworkNode node = RequireSelectedNode(engine);
            for (int i = 0; i < asset.Segments.Count; i++)
            {
                TransportNetworkSegment segment = asset.Segments[i];
                for (int p = 0; p < segment.Points.Count; p++)
                {
                    if (string.Equals(segment.Points[p].NodeId, node.Id, StringComparison.Ordinal))
                    {
                        return WebUiCommandResult.Fail(
                            "transport_node_referenced",
                            $"Node '{node.Id}' is referenced by segment '{segment.Id}' point {p}.");
                    }
                }
            }

            asset.Nodes.Remove(node);
            SelectedNodeId = string.Empty;
            ValidateAndRebuild(engine, $"deleted node {node.Id}");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_delete_node_failed", ex);
        }
    }

    public WebUiCommandResult BeginSegment(GameEngine engine)
    {
        try
        {
            EnsureAsset(engine);
            _draftPoints.Clear();
            SelectedPointIndex = -1;
            LastError = string.Empty;
            Status = "drafting";
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_begin_segment_failed", ex);
        }
    }

    public WebUiCommandResult AppendSegmentPoint(
        GameEngine engine,
        bool snapToNode,
        int? xCm,
        int? yCm,
        bool hasPickedWorld,
        WorldCmInt2 pickedWorld)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            WorldCmInt2 world = ResolveWorld(xCm, yCm, hasPickedWorld, pickedWorld);
            TransportNetworkPoint point;
            if (snapToNode)
            {
                TransportNetworkNode? node = FindNearestNode(asset, world, SelectionRadiusCm);
                if (node == null)
                {
                    return WebUiCommandResult.Fail("transport_node_not_found", "Snap-to-node point append requires a nearby transport node.");
                }

                point = TransportNetworkPoint.FromNode(node.Id);
            }
            else
            {
                point = TransportNetworkPoint.At(world.X, world.Y);
            }

            _draftPoints.Add(point);
            SelectedPointIndex = _draftPoints.Count - 1;
            LastError = string.Empty;
            Status = "drafting";
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_append_point_failed", ex);
        }
    }

    public WebUiCommandResult RemoveLastSegmentPoint(GameEngine engine)
    {
        try
        {
            EnsureAsset(engine);
            if (_draftPoints.Count == 0)
            {
                return WebUiCommandResult.Fail("transport_draft_empty", "Transport segment draft has no points.");
            }

            _draftPoints.RemoveAt(_draftPoints.Count - 1);
            SelectedPointIndex = _draftPoints.Count - 1;
            LastError = string.Empty;
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_remove_draft_point_failed", ex);
        }
    }

    public WebUiCommandResult CommitSegment(
        GameEngine engine,
        string? id,
        string? areaId,
        string? tags,
        string? direction,
        string? flowDirection,
        int? depthCm,
        int? widthCm,
        int? laneCount,
        float? visualWidthMeters,
        int? sampleStepCm)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            if (_draftPoints.Count < 2)
            {
                return WebUiCommandResult.Fail("transport_segment_points_required", "Transport segment draft requires at least two points.");
            }

            var segment = new TransportNetworkSegment
            {
                Id = string.IsNullOrWhiteSpace(id) ? GenerateSegmentId(asset) : id!,
                Points = ClonePoints(_draftPoints),
                SampleStepCm = sampleStepCm ?? 0,
                Direction = ParseEnum(direction, TransportNetworkDirection.Bidirectional, nameof(TransportNetworkDirection)),
                FlowDirection = ParseEnum(flowDirection, TransportNetworkFlowDirection.None, nameof(TransportNetworkFlowDirection)),
                AreaId = areaId ?? string.Empty,
                Tags = ParseTags(tags),
                DepthCm = depthCm ?? 0,
                WidthCm = widthCm ?? 0,
                LaneCount = laneCount ?? 0,
                VisualWidthMeters = visualWidthMeters ?? 0f
            };

            asset.Segments.Add(segment);
            _draftPoints.Clear();
            SelectedSegmentId = segment.Id;
            SelectedNodeId = string.Empty;
            SelectedPointIndex = -1;
            ValidateAndRebuild(engine, $"committed segment {segment.Id}");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_commit_segment_failed", ex);
        }
    }

    public WebUiCommandResult SelectNearestSegment(GameEngine engine, int? xCm, int? yCm, bool hasPickedWorld, WorldCmInt2 pickedWorld)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            WorldCmInt2 world = ResolveWorld(xCm, yCm, hasPickedWorld, pickedWorld);
            if (!TryFindNearestSegment(asset, world, SelectionRadiusCm, out TransportNetworkSegment? segment, out int pointIndex))
            {
                return WebUiCommandResult.Fail("transport_segment_not_found", "No transport segment was found near the requested point.");
            }

            SelectedSegmentId = segment!.Id;
            SelectedPointIndex = pointIndex;
            SelectedNodeId = string.Empty;
            LastError = string.Empty;
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_select_segment_failed", ex);
        }
    }

    public WebUiCommandResult UpdateSelectedSegment(
        GameEngine engine,
        string? areaId,
        string? tags,
        string? direction,
        string? flowDirection,
        int? depthCm,
        int? widthCm,
        int? laneCount,
        float? visualWidthMeters,
        int? sampleStepCm)
    {
        try
        {
            TransportNetworkSegment segment = RequireSelectedSegment(engine);
            if (areaId != null) segment.AreaId = areaId;
            if (tags != null) segment.Tags = ParseTags(tags);
            if (direction != null) segment.Direction = ParseEnum(direction, segment.Direction, nameof(TransportNetworkDirection));
            if (flowDirection != null) segment.FlowDirection = ParseEnum(flowDirection, segment.FlowDirection, nameof(TransportNetworkFlowDirection));
            if (depthCm.HasValue) segment.DepthCm = depthCm.Value;
            if (widthCm.HasValue) segment.WidthCm = widthCm.Value;
            if (laneCount.HasValue) segment.LaneCount = laneCount.Value;
            if (visualWidthMeters.HasValue) segment.VisualWidthMeters = visualWidthMeters.Value;
            if (sampleStepCm.HasValue) segment.SampleStepCm = sampleStepCm.Value;
            ValidateAndRebuild(engine, $"updated segment {segment.Id}");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_update_segment_failed", ex);
        }
    }

    public WebUiCommandResult MoveSelectedSegmentPoint(GameEngine engine, int? pointIndex, int? xCm, int? yCm, bool hasPickedWorld, WorldCmInt2 pickedWorld)
    {
        try
        {
            TransportNetworkSegment segment = RequireSelectedSegment(engine);
            int index = ResolvePointIndex(pointIndex, segment);
            WorldCmInt2 world = ResolveWorld(xCm, yCm, hasPickedWorld, pickedWorld);
            segment.Points[index] = TransportNetworkPoint.At(world.X, world.Y);
            SelectedPointIndex = index;
            ValidateAndRebuild(engine, $"moved segment {segment.Id} point {index}");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_move_segment_point_failed", ex);
        }
    }

    public WebUiCommandResult InsertSegmentPoint(GameEngine engine, int? pointIndex, int? xCm, int? yCm, bool hasPickedWorld, WorldCmInt2 pickedWorld)
    {
        try
        {
            TransportNetworkSegment segment = RequireSelectedSegment(engine);
            int index = ResolveInsertPointIndex(pointIndex, segment);
            WorldCmInt2 world = ResolveWorld(xCm, yCm, hasPickedWorld, pickedWorld);
            segment.Points.Insert(index, TransportNetworkPoint.At(world.X, world.Y));
            SelectedPointIndex = index;
            ValidateAndRebuild(engine, $"inserted segment {segment.Id} point {index}");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_insert_segment_point_failed", ex);
        }
    }

    public WebUiCommandResult DeleteSelectedSegmentPoint(GameEngine engine, int? pointIndex)
    {
        try
        {
            TransportNetworkSegment segment = RequireSelectedSegment(engine);
            int index = ResolvePointIndex(pointIndex, segment);
            if (segment.Points.Count <= 2)
            {
                return WebUiCommandResult.Fail("transport_segment_points_required", "Transport segment must keep at least two points.");
            }

            segment.Points.RemoveAt(index);
            SelectedPointIndex = Math.Min(segment.Points.Count - 1, index);
            ValidateAndRebuild(engine, $"deleted segment {segment.Id} point {index}");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_delete_segment_point_failed", ex);
        }
    }

    public WebUiCommandResult DeleteSelectedSegment(GameEngine engine)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            TransportNetworkSegment segment = RequireSelectedSegment(engine);
            asset.Segments.Remove(segment);
            SelectedSegmentId = string.Empty;
            SelectedPointIndex = -1;
            ValidateAndRebuild(engine, $"deleted segment {segment.Id}");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_delete_segment_failed", ex);
        }
    }

    public WebUiCommandResult SetRoot(GameEngine engine, int? sampleStepCm, float? defaultVisualWidthMeters)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            if (sampleStepCm.HasValue) asset.SampleStepCm = sampleStepCm.Value;
            if (defaultVisualWidthMeters.HasValue) asset.DefaultVisualWidthMeters = defaultVisualWidthMeters.Value;
            ValidateAndRebuild(engine, "updated transport root settings");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_set_root_failed", ex);
        }
    }

    public WebUiCommandResult Rebuild(GameEngine engine)
    {
        try
        {
            EnsureAsset(engine);
            RebuildDerivedOutputs(engine, "manual rebake");
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_rebake_failed", ex);
        }
    }

    public WebUiCommandResult QueryRoute(
        GameEngine engine,
        string? agentTypeId,
        int? startXcm,
        int? startYcm,
        int? goalXcm,
        int? goalYcm,
        bool hasPickedWorld,
        WorldCmInt2 pickedWorld)
    {
        try
        {
            EnsureAsset(engine);
            if (startXcm.HasValue && startYcm.HasValue)
            {
                RouteStart = new WorldCmInt2(startXcm.Value, startYcm.Value);
                HasRouteStart = true;
            }
            else if (!HasRouteStart)
            {
                if (!hasPickedWorld)
                {
                    return WebUiCommandResult.Fail("transport_route_points_required", "Transport route validation requires an explicit start or a picked world point.");
                }

                RouteStart = pickedWorld;
                HasRouteStart = true;
            }

            if (goalXcm.HasValue && goalYcm.HasValue)
            {
                RouteGoal = new WorldCmInt2(goalXcm.Value, goalYcm.Value);
                HasRouteGoal = true;
            }

            if (!string.IsNullOrWhiteSpace(agentTypeId))
            {
                RouteAgentTypeId = agentTypeId.Trim();
            }

            if (!HasRouteStart || !HasRouteGoal)
            {
                return WebUiCommandResult.Fail("transport_route_points_required", "Transport route validation requires start and goal.");
            }

            RunRouteQuery(engine);
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_route_failed", ex);
        }
    }

    public WebUiCommandResult SetRouteAgent(GameEngine engine, string? agentTypeId)
    {
        try
        {
            EnsureAsset(engine);
            PathingConfig pathingConfig = engine.GetService(CoreServiceKeys.PathingConfig)
                ?? throw new InvalidOperationException("Transport route validation requires PathingConfig.");
            RouteAgentTypeId = ResolveAgentTypeId(pathingConfig, agentTypeId ?? string.Empty);
            LastError = string.Empty;
            if (HasRouteStart && HasRouteGoal)
            {
                RunRouteQuery(engine);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_route_agent_failed", ex);
        }
    }

    public WebUiCommandResult SetRouteStart(GameEngine engine, WorldCmInt2 world)
    {
        try
        {
            EnsureAsset(engine);
            RouteStart = world;
            HasRouteStart = true;
            LastError = string.Empty;
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_route_start_failed", ex);
        }
    }

    public WebUiCommandResult SetRouteGoalAndQuery(GameEngine engine, WorldCmInt2 world)
    {
        try
        {
            EnsureAsset(engine);
            RouteGoal = world;
            HasRouteGoal = true;
            if (HasRouteStart)
            {
                RunRouteQuery(engine);
            }

            LastError = string.Empty;
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_route_goal_failed", ex);
        }
    }

    public WebUiCommandResult Save(GameEngine engine)
    {
        try
        {
            TransportNetworkAsset asset = EnsureAsset(engine);
            asset.Validate();
            (string modId, string assetPath) = ResolveWritableAssetPath(engine);
            EnsureCatalogRegistration(engine, modId);

            JsonSerializerOptions options = CreateTransportJsonOptions(writeIndented: true);
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            File.WriteAllText(assetPath, JsonSerializer.Serialize(asset, options));

            TransportNetworkAsset roundTrip = new TransportNetworkAssetLoader(engine.ConfigPipeline)
                .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
            EnsureRoundTripEquivalent(asset, roundTrip);
            _asset = roundTrip;
            RebuildDerivedOutputs(engine, "saved round-trip");
            LastSaveMessage = $"saved transport asset to {assetPath}";
            LastError = string.Empty;
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("transport_save_failed", ex);
        }
    }

    public object CaptureSnapshot(GameEngine engine)
    {
        TryEnsureLoaded(engine);
        TransportNetworkAsset? asset = _asset;
        return new
        {
            available = Available,
            status = Status,
            lastError = LastError,
            mode = Mode,
            assetId = asset?.Id ?? string.Empty,
            sampleStepCm = asset?.SampleStepCm ?? 0,
            defaultVisualWidthMeters = asset?.DefaultVisualWidthMeters ?? 0f,
            nodeCount = asset?.Nodes.Count ?? 0,
            segmentCount = asset?.Segments.Count ?? 0,
            selectedNodeId = SelectedNodeId,
            selectedSegmentId = SelectedSegmentId,
            selectedPointIndex = SelectedPointIndex,
            draftPointCount = _draftPoints.Count,
            bakedGraphChunks = _baked?.GraphChunks.Count ?? 0,
            bakedRibbonChunks = _baked?.RibbonChunks.Count ?? 0,
            sampledNodeCount = _baked?.SampledNodeCount ?? 0,
            directedEdgeCount = _baked?.DirectedEdgeCount ?? 0,
            lastBakeMessage = LastBakeMessage,
            lastSaveMessage = LastSaveMessage,
            nodes = CaptureNodes(asset),
            segments = CaptureSegments(asset),
            draft = asset == null ? Array.Empty<object>() : CaptureSegmentPoints(asset, _draftPoints),
            agentTypes = CaptureAgentTypes(engine),
            route = new
            {
                hasStart = HasRouteStart,
                startXcm = RouteStart.X,
                startYcm = RouteStart.Y,
                hasGoal = HasRouteGoal,
                goalXcm = RouteGoal.X,
                goalYcm = RouteGoal.Y,
                agentTypeId = RouteAgentTypeId,
                status = RouteStatus.ToString(),
                resolvedDomain = RouteResolvedDomain.ToString(),
                expanded = RouteExpanded,
                errorCode = RouteErrorCode,
                elapsedUs = LastRouteElapsedMicroseconds,
                pointCount = RoutePathXcm.Length,
                path = CaptureRoutePath()
            }
        };
    }

    private void ValidateAndRebuild(GameEngine engine, string reason)
    {
        TransportNetworkAsset asset = EnsureAsset(engine);
        asset.Validate();
        RebuildDerivedOutputs(engine, reason);
    }

    private void RebuildDerivedOutputs(GameEngine engine, string reason)
    {
        TransportNetworkAsset asset = EnsureAsset(engine);
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Transport rebuild requires a focused map session.");
        if (session.PrimaryBoard is not INodeGraphBoard graphBoard)
        {
            throw new InvalidOperationException("Transport rebuild requires a NodeGraph primary board.");
        }

        if (session.PrimaryBoard.LoadedChunks is not WorldGridLoadedChunks loadedChunks)
        {
            throw new InvalidOperationException("Transport rebuild requires WorldGridLoadedChunks.");
        }

        _payloads = engine.GetService(CoreServiceKeys.SurfaceSourcePayloadRegistry)
            ?? throw new InvalidOperationException("Transport rebuild requires SurfaceSourcePayloadRegistry.");

        if (_ribbonSource != null)
        {
            _ribbonSource.SyncPayloads(
                Array.Empty<long>(),
                _payloads,
                TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId);
        }

        _graphSource?.Dispose();
        _graphSource = null;
        _ribbonSource = null;
        graphBoard.GraphStore.Clear();

        _baked = new TransportNetworkBaker().Bake(asset, loadedChunks.ChunkSizeCm);
        _graphSource = new TransportNetworkChunkGraphSource(graphBoard.GraphStore, loadedChunks, _baked);
        _graphSource.LoadActiveChunks();
        _ribbonSource = new TransportNetworkRibbonSource(_baked);
        _ribbonSource.SyncPayloads(
            loadedChunks.ActiveChunkKeys,
            _payloads,
            TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId);
        Status = "ready";
        LastBakeMessage =
            $"{reason}: graphChunks={_baked.GraphChunks.Count}, ribbonChunks={_baked.RibbonChunks.Count}, sampledNodes={_baked.SampledNodeCount}, edges={_baked.DirectedEdgeCount}";
        LastError = string.Empty;
    }

    private void RunRouteQuery(GameEngine engine)
    {
        if (engine.CurrentMapSession?.PrimaryBoard?.LoadedChunks is WorldGridLoadedChunks loadedChunks)
        {
            Span<Vector2> points = stackalloc[]
            {
                new Vector2(RouteStart.X, RouteStart.Y),
                new Vector2(RouteGoal.X, RouteGoal.Y)
            };
            LoadedChunkSolvePrimer.PrimeForBounds(loadedChunks, points, paddingChunks: 1);
            _graphSource?.LoadActiveChunks();
            if (_ribbonSource != null && _payloads != null)
            {
                _ribbonSource.SyncPayloads(
                    loadedChunks.ActiveChunkKeys,
                    _payloads,
                    TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId);
            }
        }

        LoadedGraphRuntime graphRuntime = engine.GetService(CoreServiceKeys.LoadedGraphRuntime)
            ?? throw new InvalidOperationException("Transport route validation requires LoadedGraphRuntime.");
        IPathService pathService = engine.GetService(CoreServiceKeys.PathService)
            ?? throw new InvalidOperationException("Transport route validation requires PathService.");
        PathStore pathStore = engine.GetService(CoreServiceKeys.PathStore)
            ?? throw new InvalidOperationException("Transport route validation requires PathStore.");
        PathingConfig pathingConfig = engine.GetService(CoreServiceKeys.PathingConfig)
            ?? throw new InvalidOperationException("Transport route validation requires PathingConfig.");

        string agentTypeId = ResolveAgentTypeId(pathingConfig, RouteAgentTypeId);
        RouteStart = ProjectEndpoint(graphRuntime, RouteStart, "start");
        RouteGoal = ProjectEndpoint(graphRuntime, RouteGoal, "goal");
        RouteAgentTypeId = agentTypeId;

        var request = new PathRequest(
            _nextRouteRequestId++,
            Entity.Null,
            PathDomain.Auto,
            agentTypeId,
            PathEndpoint.FromWorldCm(RouteStart.X, RouteStart.Y),
            PathEndpoint.FromWorldCm(RouteGoal.X, RouteGoal.Y),
            new PathBudget(maxExpanded: 0, maxPoints: Math.Min(RoutePointCapacity, pathStore.MaxPointsPerPath)));

        long before = Stopwatch.GetTimestamp();
        bool solved = pathService.TrySolve(in request, out PathResult result);
        LastRouteElapsedMicroseconds = Stopwatch.GetElapsedTime(before).Ticks / 10;
        RouteStatus = result.Status;
        RouteResolvedDomain = result.ResolvedDomain;
        RouteExpanded = result.Expanded;
        RouteErrorCode = result.ErrorCode;

        if (!solved || result.Status != PathStatus.Found || !result.Handle.IsValid)
        {
            RoutePathXcm = Array.Empty<int>();
            RoutePathYcm = Array.Empty<int>();
            return;
        }

        Span<int> xScratch = stackalloc int[RoutePointCapacity];
        Span<int> yScratch = stackalloc int[RoutePointCapacity];
        try
        {
            if (!pathService.TryCopyPath(in result.Handle, xScratch, yScratch, out int count))
            {
                RoutePathXcm = Array.Empty<int>();
                RoutePathYcm = Array.Empty<int>();
                RouteStatus = PathStatus.Error;
                RouteErrorCode = 91;
                return;
            }

            int snappedCount = PolylineGoalSnapQuery.SnapGoalOntoPolyline(
                RouteGoal.X,
                RouteGoal.Y,
                xScratch,
                yScratch,
                count);
            RoutePathXcm = xScratch.Slice(0, snappedCount).ToArray();
            RoutePathYcm = yScratch.Slice(0, snappedCount).ToArray();
        }
        finally
        {
            if (pathStore.IsAlive(result.Handle))
            {
                pathStore.Release(result.Handle);
            }
        }
    }

    private TransportNetworkAsset EnsureAsset(GameEngine engine)
    {
        if (!TryEnsureLoaded(engine) || _asset == null)
        {
            throw new InvalidOperationException(LastError.Length == 0
                ? "TransportNetworkAsset is not available for the focused map."
                : LastError);
        }

        return _asset;
    }

    private TransportNetworkNode RequireSelectedNode(GameEngine engine)
    {
        TransportNetworkAsset asset = EnsureAsset(engine);
        if (string.IsNullOrWhiteSpace(SelectedNodeId))
        {
            throw new InvalidOperationException("No transport node is selected.");
        }

        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            if (string.Equals(asset.Nodes[i].Id, SelectedNodeId, StringComparison.Ordinal))
            {
                return asset.Nodes[i];
            }
        }

        throw new InvalidOperationException($"Selected transport node '{SelectedNodeId}' no longer exists.");
    }

    private TransportNetworkSegment RequireSelectedSegment(GameEngine engine)
    {
        TransportNetworkAsset asset = EnsureAsset(engine);
        if (string.IsNullOrWhiteSpace(SelectedSegmentId))
        {
            throw new InvalidOperationException("No transport segment is selected.");
        }

        for (int i = 0; i < asset.Segments.Count; i++)
        {
            if (string.Equals(asset.Segments[i].Id, SelectedSegmentId, StringComparison.Ordinal))
            {
                return asset.Segments[i];
            }
        }

        throw new InvalidOperationException($"Selected transport segment '{SelectedSegmentId}' no longer exists.");
    }

    private int ResolvePointIndex(int? pointIndex, TransportNetworkSegment segment)
    {
        int index = pointIndex ?? SelectedPointIndex;
        if ((uint)index >= (uint)segment.Points.Count)
        {
            throw new InvalidOperationException($"Transport segment point index {index} is out of range.");
        }

        return index;
    }

    private int ResolveInsertPointIndex(int? pointIndex, TransportNetworkSegment segment)
    {
        int index = pointIndex ?? SelectedPointIndex;
        if (index < 0)
        {
            index = segment.Points.Count;
        }

        if (index > segment.Points.Count)
        {
            throw new InvalidOperationException($"Transport segment insert point index {index} is out of range.");
        }

        return index;
    }

    private void ClearRuntimeSources()
    {
        if (_ribbonSource != null && _payloads != null)
        {
            _ribbonSource.SyncPayloads(
                Array.Empty<long>(),
                _payloads,
                TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId);
        }

        _graphSource?.Dispose();
        _graphSource = null;
        _ribbonSource = null;
        _baked = null;
    }

    private void ResetAuthoringState(string status, string error)
    {
        ClearRuntimeSources();
        _asset = null;
        Available = false;
        Status = status;
        LastError = error;
        LastBakeMessage = string.Empty;
        LastSaveMessage = string.Empty;
        SelectedNodeId = string.Empty;
        SelectedSegmentId = string.Empty;
        SelectedPointIndex = -1;
        _draftPoints.Clear();
    }

    private (string ModId, string AssetPath) ResolveWritableAssetPath(GameEngine engine)
    {
        var matches = new List<(string ModId, string Path)>();
        for (int i = 0; i < engine.ModLoader.LoadedModIds.Count; i++)
        {
            string modId = engine.ModLoader.LoadedModIds[i];
            AddExistingAssetPath(engine, matches, modId, $"assets/{TransportNetworkAssetLoader.DefaultRelativePath}");
            AddExistingAssetPath(engine, matches, modId, $"assets/Configs/{TransportNetworkAssetLoader.DefaultRelativePath}");
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"TransportNetworkAsset has multiple writable sources: {string.Join(", ", matches.Select(m => m.ModId))}.");
        }

        string targetModId = ResolveFocusedMapSaveTargetModId(engine);
        string relativePath = $"assets/{TransportNetworkAssetLoader.DefaultRelativePath}";
        if (!engine.VFS.TryResolveFullPath($"{targetModId}:{relativePath}", out string createdPath))
        {
            throw new InvalidOperationException($"Cannot resolve transport asset path '{targetModId}:{relativePath}'.");
        }

        return (targetModId, createdPath);
    }

    private static void AddExistingAssetPath(
        GameEngine engine,
        List<(string ModId, string Path)> matches,
        string modId,
        string relativePath)
    {
        if (!engine.VFS.TryResolveFullPath($"{modId}:{relativePath}", out string path) || !File.Exists(path))
        {
            return;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            if (string.Equals(matches[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        matches.Add((modId, path));
    }

    private static string ResolveFocusedMapSaveTargetModId(GameEngine engine)
    {
        string mapId = engine.CurrentMapSession?.MapId.Value
            ?? throw new InvalidOperationException("Transport save requires a focused map.");
        var matches = new List<string>();
        for (int i = 0; i < engine.ModLoader.LoadedModIds.Count; i++)
        {
            string modId = engine.ModLoader.LoadedModIds[i];
            AddMapSaveTarget(engine, matches, modId, $"assets/Maps/{mapId}.json");
            AddMapSaveTarget(engine, matches, modId, $"assets/maps/{mapId}.json");
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Focused map '{mapId}' has multiple explicit live editor save targets: {string.Join(", ", matches)}.");
        }

        throw new InvalidOperationException(
            $"Focused map '{mapId}' has no explicit live editor save target for creating a transport asset.");
    }

    private static void AddMapSaveTarget(GameEngine engine, List<string> matches, string modId, string relativePath)
    {
        if (!engine.VFS.TryResolveFullPath($"{modId}:{relativePath}", out string path) || !File.Exists(path))
        {
            return;
        }

        JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
        if (root?["Metadata"]?["liveMapEditor"]?["saveTarget"]?.GetValue<bool>() == true ||
            root?["metadata"]?["liveMapEditor"]?["saveTarget"]?.GetValue<bool>() == true)
        {
            matches.Add(modId);
        }
    }

    private static void EnsureCatalogRegistration(GameEngine engine, string modId)
    {
        const string catalogRelativePath = "assets/Configs/config_catalog.json";
        if (!engine.VFS.TryResolveFullPath($"{modId}:{catalogRelativePath}", out string catalogPath))
        {
            throw new InvalidOperationException($"Cannot resolve config catalog path '{modId}:{catalogRelativePath}'.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        JsonArray catalog;
        if (File.Exists(catalogPath))
        {
            catalog = JsonNode.Parse(File.ReadAllText(catalogPath)) as JsonArray
                ?? throw new InvalidDataException($"Config catalog '{catalogPath}' must be a JSON array.");
        }
        else
        {
            catalog = new JsonArray();
        }

        bool hasEntry = false;
        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog[i] is JsonObject obj &&
                string.Equals(obj["Path"]?.GetValue<string>(), TransportNetworkAssetLoader.DefaultRelativePath, StringComparison.Ordinal))
            {
                string policy = obj["Policy"]?.GetValue<string>() ?? string.Empty;
                if (!string.Equals(policy, "Replace", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Config catalog entry '{TransportNetworkAssetLoader.DefaultRelativePath}' must use Policy=Replace.");
                }

                hasEntry = true;
            }
        }

        if (!hasEntry)
        {
            catalog.Add(new JsonObject
            {
                ["Path"] = TransportNetworkAssetLoader.DefaultRelativePath,
                ["Policy"] = "Replace"
            });
            File.WriteAllText(catalogPath, catalog.ToJsonString(CreateTransportJsonOptions(writeIndented: true)));
        }

        if (!engine.ConfigCatalog.TryGet(TransportNetworkAssetLoader.DefaultRelativePath, out _))
        {
            engine.ConfigCatalog.Add(new ConfigCatalogEntry(
                TransportNetworkAssetLoader.DefaultRelativePath,
                ConfigMergePolicy.Replace));
        }
    }

    private static void EnsureRoundTripEquivalent(TransportNetworkAsset expected, TransportNetworkAsset actual)
    {
        JsonSerializerOptions options = CreateTransportJsonOptions(writeIndented: false);
        string expectedJson = JsonSerializer.Serialize(expected, options);
        string actualJson = JsonSerializer.Serialize(actual, options);
        if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("TransportNetworkAsset save round-trip changed the asset semantics.");
        }
    }

    private WorldCmInt2 ProjectEndpoint(LoadedGraphRuntime graphRuntime, WorldCmInt2 point, string label)
    {
        Span<int> candidates = stackalloc int[64];
        if (!GraphEdgeProjectionQuery.TryProjectNearestEdge(
                graphRuntime.CurrentGraph,
                graphRuntime.CurrentSpatialIndex,
                point,
                ProjectionRadiusCm,
                candidates,
                out GraphEdgeProjection projection))
        {
            throw new InvalidOperationException($"Transport route {label} point could not be projected onto the loaded transport graph.");
        }

        return new WorldCmInt2(projection.ProjectedXcm, projection.ProjectedYcm);
    }

    private static string ResolveAgentTypeId(PathingConfig config, string requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            for (int i = 0; i < config.AgentTypes.Count; i++)
            {
                if (string.Equals(config.AgentTypes[i].Id, requested, StringComparison.Ordinal))
                {
                    return requested;
                }
            }

            throw new InvalidOperationException($"Pathing agent type '{requested}' is not registered.");
        }

        if (config.AgentTypes.Count == 0)
        {
            throw new InvalidOperationException("PathingConfig.agentTypes is empty.");
        }

        return config.AgentTypes[0].Id;
    }

    private static WorldCmInt2 ResolveWorld(int? xCm, int? yCm, bool hasPickedWorld, WorldCmInt2 pickedWorld)
    {
        if (xCm.HasValue && yCm.HasValue)
        {
            return new WorldCmInt2(xCm.Value, yCm.Value);
        }

        if (!hasPickedWorld)
        {
            throw new InvalidOperationException("Transport command requires explicit world cm or a picked viewport point.");
        }

        return pickedWorld;
    }

    private static List<string> ParseTags(string? text)
    {
        var tags = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return tags;
        }

        string[] parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string tag = parts[i].Trim();
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            tags.Add(tag);
        }

        return tags;
    }

    private static void RewriteNodeReferences(TransportNetworkAsset asset, string oldId, string newId)
    {
        for (int i = 0; i < asset.Segments.Count; i++)
        {
            TransportNetworkSegment segment = asset.Segments[i];
            for (int p = 0; p < segment.Points.Count; p++)
            {
                TransportNetworkPoint point = segment.Points[p];
                if (string.Equals(point.NodeId, oldId, StringComparison.Ordinal))
                {
                    point.NodeId = newId;
                }
            }
        }
    }

    private static TEnum ParseEnum<TEnum>(string? text, TEnum defaultValue, string field)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        if (Enum.TryParse(text, ignoreCase: false, out TEnum parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"{field} value '{text}' is not valid.");
    }

    private static List<TransportNetworkPoint> ClonePoints(IReadOnlyList<TransportNetworkPoint> source)
    {
        var result = new List<TransportNetworkPoint>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            TransportNetworkPoint point = source[i];
            result.Add(string.IsNullOrWhiteSpace(point.NodeId)
                ? TransportNetworkPoint.At(point.Xcm, point.Ycm)
                : TransportNetworkPoint.FromNode(point.NodeId));
        }

        return result;
    }

    private static TransportNetworkNode? FindNearestNode(TransportNetworkAsset asset, WorldCmInt2 world, int radiusCm)
    {
        TransportNetworkNode? best = null;
        long bestD2 = (long)radiusCm * radiusCm;
        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            TransportNetworkNode node = asset.Nodes[i];
            long dx = node.Xcm - world.X;
            long dy = node.Ycm - world.Y;
            long d2 = dx * dx + dy * dy;
            if (d2 <= bestD2)
            {
                bestD2 = d2;
                best = node;
            }
        }

        return best;
    }

    private static bool TryFindNearestSegment(
        TransportNetworkAsset asset,
        WorldCmInt2 world,
        int radiusCm,
        out TransportNetworkSegment? segment,
        out int pointIndex)
    {
        segment = null;
        pointIndex = -1;
        float bestD2 = radiusCm * radiusCm;
        for (int i = 0; i < asset.Segments.Count; i++)
        {
            TransportNetworkSegment candidate = asset.Segments[i];
            for (int p = 0; p < candidate.Points.Count - 1; p++)
            {
                WorldCmInt2 a = ResolvePoint(asset, candidate.Points[p]);
                WorldCmInt2 b = ResolvePoint(asset, candidate.Points[p + 1]);
                GraphEdgeProjectionQuery.ProjectPointOnSegment(
                    world.X,
                    world.Y,
                    a.X,
                    a.Y,
                    b.X,
                    b.Y,
                    out int projectedX,
                    out int projectedY,
                    out _);
                float dx = world.X - projectedX;
                float dy = world.Y - projectedY;
                float d2 = (dx * dx) + (dy * dy);
                if (d2 <= bestD2)
                {
                    bestD2 = d2;
                    segment = candidate;
                    pointIndex = p + 1;
                }
            }
        }

        return segment != null;
    }

    public static WorldCmInt2 ResolvePoint(TransportNetworkAsset asset, TransportNetworkPoint point)
    {
        if (string.IsNullOrWhiteSpace(point.NodeId))
        {
            return new WorldCmInt2(point.Xcm, point.Ycm);
        }

        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            TransportNetworkNode node = asset.Nodes[i];
            if (string.Equals(node.Id, point.NodeId, StringComparison.Ordinal))
            {
                return new WorldCmInt2(node.Xcm, node.Ycm);
            }
        }

        throw new InvalidOperationException($"Transport point references unknown node '{point.NodeId}'.");
    }

    private static string GenerateNodeId(TransportNetworkAsset asset)
    {
        for (int i = asset.Nodes.Count + 1; i < asset.Nodes.Count + 10000; i++)
        {
            string id = $"node_{i:000}";
            if (!asset.Nodes.Any(node => string.Equals(node.Id, id, StringComparison.Ordinal)))
            {
                return id;
            }
        }

        throw new InvalidOperationException("Could not generate a unique transport node id.");
    }

    private static string GenerateSegmentId(TransportNetworkAsset asset)
    {
        for (int i = asset.Segments.Count + 1; i < asset.Segments.Count + 10000; i++)
        {
            string id = $"segment_{i:000}";
            if (!asset.Segments.Any(segment => string.Equals(segment.Id, id, StringComparison.Ordinal)))
            {
                return id;
            }
        }

        throw new InvalidOperationException("Could not generate a unique transport segment id.");
    }

    private static object[] CaptureNodes(TransportNetworkAsset? asset)
    {
        if (asset == null) return Array.Empty<object>();
        var nodes = new object[asset.Nodes.Count];
        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            TransportNetworkNode node = asset.Nodes[i];
            nodes[i] = new
            {
                node.Id,
                node.Xcm,
                node.Ycm,
                kind = node.Kind.ToString(),
                tags = node.Tags.ToArray()
            };
        }

        return nodes;
    }

    private static object[] CaptureSegments(TransportNetworkAsset? asset)
    {
        if (asset == null) return Array.Empty<object>();
        var segments = new object[asset.Segments.Count];
        for (int i = 0; i < asset.Segments.Count; i++)
        {
            TransportNetworkSegment segment = asset.Segments[i];
            segments[i] = new
            {
                segment.Id,
                pointCount = segment.Points.Count,
                points = CaptureSegmentPoints(asset, segment.Points),
                segment.SampleStepCm,
                direction = segment.Direction.ToString(),
                flowDirection = segment.FlowDirection.ToString(),
                segment.AreaId,
                tags = segment.Tags.ToArray(),
                segment.DepthCm,
                segment.WidthCm,
                segment.LaneCount,
                segment.VisualWidthMeters
            };
        }

        return segments;
    }

    private static object[] CaptureSegmentPoints(TransportNetworkAsset asset, IReadOnlyList<TransportNetworkPoint>? points)
    {
        if (points == null) return Array.Empty<object>();
        var result = new object[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            TransportNetworkPoint point = points[i];
            WorldCmInt2 world = ResolvePoint(asset, point);
            result[i] = new
            {
                point.NodeId,
                world.X,
                world.Y
            };
        }

        return result;
    }

    private object[] CaptureRoutePath()
    {
        int count = Math.Min(RoutePathXcm.Length, RoutePathYcm.Length);
        var result = new object[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new { xCm = RoutePathXcm[i], yCm = RoutePathYcm[i] };
        }

        return result;
    }

    private static object[] CaptureAgentTypes(GameEngine engine)
    {
        PathingConfig? config = engine.GetService(CoreServiceKeys.PathingConfig);
        AgentProfileRegistry? profiles = engine.GetService(CoreServiceKeys.AgentProfiles);
        if (config?.AgentTypes == null)
        {
            return Array.Empty<object>();
        }

        var result = new object[config.AgentTypes.Count];
        for (int i = 0; i < config.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig agent = config.AgentTypes[i];
            AgentProfileConfig? profile = null;
            profiles?.TryGet(agent.ProfileId, out profile);
            result[i] = new
            {
                agent.Id,
                agent.ProfileId,
                draftCm = profile?.DraftCm ?? 0f,
                beamCm = profile?.BeamCm ?? 0f
            };
        }

        return result;
    }

    private static JsonSerializerOptions CreateTransportJsonOptions(bool writeIndented)
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase();
        options.WriteIndented = writeIndented;
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private WebUiCommandResult Fail(string code, Exception ex)
    {
        LastError = ex.Message;
        Status = "error";
        return WebUiCommandResult.Fail(code, ex.Message);
    }

    private WebUiCommandResult Ok()
    {
        LastError = string.Empty;
        if (Status == "error")
        {
            Status = "ready";
        }

        return WebUiCommandResult.Ok();
    }
}
