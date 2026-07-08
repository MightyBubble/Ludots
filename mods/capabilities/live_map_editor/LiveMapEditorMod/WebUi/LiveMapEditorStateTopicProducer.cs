using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Config;
using Ludots.Core.Map.Authoring;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;
using LiveMapEditorMod.Runtime;

namespace LiveMapEditorMod.WebUi;

internal sealed class LiveMapEditorStateTopicProducer : IWebUiTopicProducer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GameEngine _engine;
    private readonly LiveMapEditorRuntime _runtime;

    public LiveMapEditorStateTopicProducer(GameEngine engine, LiveMapEditorRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public string Topic => LiveMapEditorIds.StateTopic;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        object snapshot = CaptureSnapshot();
        packet = new WebUiOutboundPacket(
            context.SessionId,
            Topic,
            WebUiPacketKind.Snapshot,
            WebUiDeliverySemantics.LatestWins,
            JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions),
            "application/json",
            context.RequestId);
        return true;
    }

    private object CaptureSnapshot()
    {
        var session = _engine.CurrentMapSession;
        var terrain = _engine.LogicTerrain;
        NavMeshBakeConfig? navConfig = _engine.GetService(CoreServiceKeys.NavMeshBakeConfig);
        RuntimeIncrementalNavMeshRebuildQueue? queue = _engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue);
        NavQueryServiceRegistry? navRegistry = _engine.GetService(CoreServiceKeys.NavQueryServices);
        NavMeshProfileRegistry? navProfiles = _engine.GetService(CoreServiceKeys.NavMeshProfiles);
        AgentProfileRegistry? agentProfiles = _engine.GetService(CoreServiceKeys.AgentProfiles);

        return new
        {
            schemaVersion = 1,
            panelOpen = _runtime.PanelOpen,
            tool = _runtime.Tool,
            lastError = _runtime.LastError,
            map = session == null
                ? null
                : new
                {
                    id = session.MapId.Value,
                    tags = session.MapConfig.Tags,
                    boards = CaptureBoards(session.AllBoards),
                    authoredBoards = CaptureAuthoredBoards(session.MapConfig.Boards),
                    selectedBoardName = _runtime.MapLifecycle.SelectedBoardName
                },
            mapLifecycle = new
            {
                _runtime.MapLifecycle.SelectedBoardName,
                _runtime.MapLifecycle.Status,
                _runtime.MapLifecycle.Message,
                _runtime.MapLifecycle.ReloadRequired,
                _runtime.MapLifecycle.TargetModId,
                _runtime.MapLifecycle.MapConfigPath,
                createMapPreview = CaptureAllocationPreview(_runtime.MapLifecycle.CreateMapPreview),
                addBoardPreview = CaptureAllocationPreview(_runtime.MapLifecycle.AddBoardPreview)
            },
            terrain = terrain == null
                ? null
                : new
                {
                    topology = terrain.Topology.ToString(),
                    widthCells = terrain.WidthCells,
                    heightCells = terrain.HeightCells,
                    chunkSizeCells = terrain.ChunkSizeCells,
                    cellSizeCm = terrain.HorizontalStepCm,
                    revision = _runtime.TerrainRevision,
                    editable = terrain.Topology == LogicTerrainTopology.Grid,
                    dirty = _runtime.HasDirtyAabb
                        ? new
                        {
                            x = _runtime.DirtyAabb.X,
                            y = _runtime.DirtyAabb.Y,
                            width = _runtime.DirtyAabb.Width,
                            height = _runtime.DirtyAabb.Height
                        }
                        : null
                },
            brush = new
            {
                _runtime.Brush.RadiusCells,
                _runtime.Brush.Mode,
                _runtime.Brush.Target,
                _runtime.Brush.HeightLevel,
                _runtime.Brush.WaterHeightLevel,
                _runtime.Brush.AreaId,
                _runtime.Brush.Cost,
                _runtime.Brush.Blocked,
                _runtime.Brush.Water,
                _runtime.Brush.Ramp
            },
            obstacle = new
            {
                _runtime.Obstacle.TemplateId,
                _runtime.Obstacle.Shape,
                _runtime.Obstacle.RadiusCm,
                _runtime.Obstacle.HalfWidthCm,
                _runtime.Obstacle.HalfHeightCm,
                _runtime.Obstacle.NavRadiusCm,
                _runtime.Obstacle.SinkPhysicsCollider,
                _runtime.Obstacle.SinkNavigationObstacle,
                polygon = CaptureObstaclePolygon(_runtime.Obstacle.PolygonVertices)
            },
            pick = new
            {
                hasWorld = _runtime.HasPickedWorld,
                xCm = _runtime.PickedWorld.X,
                yCm = _runtime.PickedWorld.Y,
                hasCell = _runtime.HasPickedCell,
                col = _runtime.PickedCellCol,
                row = _runtime.PickedCellRow
            },
            entities = new
            {
                count = session == null ? 0 : _runtime.CountMapEntities(_engine),
                obstacleCount = session == null ? 0 : _runtime.CountObstacles(_engine),
                authoredCount = _runtime.AuthoredEntities.Count,
                templates = CaptureEntityTemplates(),
                selected = DescribeSelectedEntity()
            },
            nav = new
            {
                available = navRegistry != null,
                runtime = navConfig == null ? "missing" : $"{navConfig.ParsedMode} + {navConfig.ParsedAlgorithm}",
                supportedRuntime = navConfig != null &&
                    navConfig.ParsedMode == NavBakeMode.RuntimeIncremental &&
                    navConfig.ParsedAlgorithm == NavBakeAlgorithmKind.Cdt,
                pendingTiles = queue?.PendingTileCount ?? _runtime.Nav.PendingTiles,
                loadedTiles = CountLoadedTiles(navRegistry),
                lastRebuiltTiles = _runtime.Nav.LastRebuiltTiles,
                lastFailedTiles = _runtime.Nav.LastFailedTiles,
                bakeScope = _runtime.Nav.BakeScope,
                bakeIncludeNeighbors = _runtime.Nav.BakeIncludeNeighbors,
                bakeParallel = _runtime.Nav.BakeParallel,
                bakeMaxTiles = _runtime.Nav.BakeMaxTiles,
                estimatedTiles = _runtime.Nav.LastEstimatedTiles,
                queryLayer = _runtime.Nav.QueryLayer,
                queryProfileId = _runtime.Nav.QueryProfileId,
                queryProfileIndex = _runtime.Nav.QueryProfileIndex,
                maxPortals = _runtime.Nav.MaxPortals,
                profiles = CaptureNavProfiles(navProfiles),
                message = _runtime.Nav.LastMessage
            },
            navConfig = CaptureNavConfig(navConfig, agentProfiles),
            sim = new
            {
                hasStart = _runtime.Nav.HasStart,
                startXcm = _runtime.Nav.Start.X,
                startYcm = _runtime.Nav.Start.Y,
                hasGoal = _runtime.Nav.HasGoal,
                goalXcm = _runtime.Nav.Goal.X,
                goalYcm = _runtime.Nav.Goal.Y,
                profileId = ResolveSimulationProfileId(navProfiles),
                layer = _runtime.Nav.QueryLayer,
                maxPortals = _runtime.Nav.MaxPortals,
                profile = CaptureSimulationProfile(navConfig, agentProfiles, navProfiles),
                status = _runtime.Nav.PathStatus.ToString(),
                pointCount = _runtime.Nav.PathXcm.Length,
                elapsedUs = _runtime.Nav.LastQueryElapsedMicroseconds,
                path = CapturePath()
            },
            view = new
            {
                grid = _runtime.View.ShowGrid,
                chunks = _runtime.View.ShowChunks,
                navmesh = _runtime.View.ShowNavMesh,
                path = _runtime.View.ShowPath,
                transport = _runtime.View.ShowTransport,
                entities = _runtime.View.ShowEntities,
                minimap = _runtime.View.ShowMinimap
            },
            minimap = CaptureMinimap(terrain),
            transport = _runtime.Transport.CaptureSnapshot(_engine),
            save = new
            {
                _runtime.Save.Status,
                _runtime.Save.Message,
                _runtime.Save.MapConfigPath,
                _runtime.Save.EntityCount,
                _runtime.Save.NavTileCount
            }
        };
    }

    private object[] CaptureNavProfiles(NavMeshProfileRegistry? profiles)
    {
        if (profiles == null || profiles.Count == 0)
        {
            return Array.Empty<object>();
        }

        var result = new object[profiles.Count];
        for (int i = 0; i < profiles.Count; i++)
        {
            result[i] = new { id = profiles.GetId(i), index = i };
        }

        return result;
    }

    private object? CaptureNavConfig(NavMeshBakeConfig? config, AgentProfileRegistry? agentProfiles)
    {
        if (config == null)
        {
            return null;
        }

        return new
        {
            mode = config.Mode,
            algorithm = config.Algorithm,
            dirty = _runtime.Nav.ConfigDirty,
            status = _runtime.Nav.ConfigStatus,
            message = _runtime.Nav.ConfigMessage,
            targetModId = _runtime.Nav.ConfigTargetModId,
            agentProfiles = CaptureAgentProfiles(agentProfiles),
            bakeProfiles = CaptureBakeProfiles(config),
            layers = CaptureLayers(config),
            areas = CaptureAreas(config),
            runtimeIncremental = config.RuntimeIncremental == null
                ? null
                : new
                {
                    config.RuntimeIncremental.TileBudgetPerFixedTick,
                    config.RuntimeIncremental.IncludeNeighborTiles,
                    config.RuntimeIncremental.HeightScaleMeters,
                    config.RuntimeIncremental.MinWalkableUpDot,
                    config.RuntimeIncremental.CliffHeightThreshold
                }
        };
    }

    private static object[] CaptureAgentProfiles(AgentProfileRegistry? profiles)
    {
        if (profiles == null || profiles.Count == 0)
        {
            return Array.Empty<object>();
        }

        var result = new object[profiles.Count];
        for (int i = 0; i < profiles.Count; i++)
        {
            AgentProfileConfig profile = profiles[i];
            result[i] = new
            {
                profile.Id,
                profile.RadiusCm,
                profile.HeightCm,
                profile.ClearanceCm,
                profile.DraftCm,
                profile.BeamCm,
                profile.Mass,
                profile.Layer
            };
        }

        return result;
    }

    private static object[] CaptureBakeProfiles(NavMeshBakeConfig config)
    {
        if (config.Profiles == null || config.Profiles.Count == 0)
        {
            return Array.Empty<object>();
        }

        var result = new object[config.Profiles.Count];
        for (int i = 0; i < config.Profiles.Count; i++)
        {
            NavMeshAgentProfileConfig profile = config.Profiles[i];
            result[i] = new
            {
                profile.Id,
                profile.MaxClimbCm,
                profile.MaxSlopeDeg
            };
        }

        return result;
    }

    private static object[] CaptureLayers(NavMeshBakeConfig config)
    {
        if (config.Layers == null || config.Layers.Count == 0)
        {
            return Array.Empty<object>();
        }

        var result = new object[config.Layers.Count];
        for (int i = 0; i < config.Layers.Count; i++)
        {
            NavLayerConfig layer = config.Layers[i];
            result[i] = new { layer.Id, layer.Layer };
        }

        return result;
    }

    private static object[] CaptureAreas(NavMeshBakeConfig config)
    {
        if (config.Areas == null || config.Areas.Count == 0)
        {
            return Array.Empty<object>();
        }

        var result = new object[config.Areas.Count];
        for (int i = 0; i < config.Areas.Count; i++)
        {
            NavAreaCostConfig area = config.Areas[i];
            result[i] = new { area.Id, area.AreaId, area.Cost };
        }

        return result;
    }

    private object? CaptureSimulationProfile(
        NavMeshBakeConfig? config,
        AgentProfileRegistry? agentProfiles,
        NavMeshProfileRegistry? navProfiles)
    {
        if (config == null || agentProfiles == null)
        {
            return null;
        }

        string profileId = ResolveSimulationProfileId(navProfiles);

        if (string.IsNullOrWhiteSpace(profileId) ||
            !agentProfiles.TryGet(profileId, out AgentProfileConfig agent))
        {
            return null;
        }

        NavMeshAgentProfileConfig? bake = null;
        if (config.Profiles != null)
        {
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                if (string.Equals(config.Profiles[i].Id, profileId, StringComparison.Ordinal))
                {
                    bake = config.Profiles[i];
                    break;
                }
            }
        }

        return new
        {
            id = profileId,
            agent.RadiusCm,
            agent.HeightCm,
            agent.ClearanceCm,
            agent.Layer,
            maxClimbCm = bake?.MaxClimbCm,
            maxSlopeDeg = bake?.MaxSlopeDeg
        };
    }

    private string ResolveSimulationProfileId(NavMeshProfileRegistry? navProfiles)
    {
        string profileId = _runtime.Nav.QueryProfileId;
        if (!string.IsNullOrWhiteSpace(profileId) || navProfiles == null || navProfiles.Count == 0)
        {
            return profileId;
        }

        int index = _runtime.Nav.QueryProfileIndex;
        if (index < 0 || index >= navProfiles.Count)
        {
            index = 0;
        }

        return navProfiles.GetId(index);
    }

    private object? CaptureMinimap(LogicTerrainField? terrain)
    {
        if (terrain == null || terrain.Topology != LogicTerrainTopology.Grid || !_runtime.View.ShowMinimap)
        {
            return null;
        }

        var chunks = new object[checked(terrain.WidthChunks * terrain.HeightChunks)];
        int index = 0;
        for (int cy = 0; cy < terrain.HeightChunks; cy++)
        {
            for (int cx = 0; cx < terrain.WidthChunks; cx++)
            {
                int sampleCol = Math.Min(terrain.WidthCells - 1, cx * terrain.ChunkSizeCells + terrain.TileWidthCells(cx) / 2);
                int sampleRow = Math.Min(terrain.HeightCells - 1, cy * terrain.ChunkSizeCells + terrain.TileHeightCells(cy) / 2);
                LogicTerrainCell cell = terrain.GetCell(sampleCol, sampleRow);
                chunks[index++] = new
                {
                    x = cx,
                    y = cy,
                    h = cell.HeightLevel,
                    water = cell.HasWater,
                    blocked = cell.IsBlocked,
                    area = cell.AreaId,
                    dirty = IsMinimapChunkDirty(terrain, cx, cy)
                };
            }
        }

        return new
        {
            widthChunks = terrain.WidthChunks,
            heightChunks = terrain.HeightChunks,
            widthCells = terrain.WidthCells,
            heightCells = terrain.HeightCells,
            chunkSizeCells = terrain.ChunkSizeCells,
            cellSizeCm = terrain.HorizontalStepCm,
            camera = CaptureCamera(),
            chunks,
            dirty = _runtime.HasDirtyAabb
                ? new
                {
                    x = _runtime.DirtyAabb.X,
                    y = _runtime.DirtyAabb.Y,
                    width = _runtime.DirtyAabb.Width,
                    height = _runtime.DirtyAabb.Height
                }
                : null
        };
    }

    private bool IsMinimapChunkDirty(LogicTerrainField terrain, int chunkX, int chunkY)
    {
        if (!_runtime.HasDirtyAabb)
        {
            return false;
        }

        int left = checked(chunkX * terrain.ChunkSizeCells * terrain.HorizontalStepCm);
        int top = checked(chunkY * terrain.ChunkSizeCells * terrain.VerticalStepCm);
        int right = checked(left + terrain.TileWidthCells(chunkX) * terrain.HorizontalStepCm);
        int bottom = checked(top + terrain.TileHeightCells(chunkY) * terrain.VerticalStepCm);
        var dirty = _runtime.DirtyAabb;
        return dirty.Left < right &&
               dirty.Right > left &&
               dirty.Top < bottom &&
               dirty.Bottom > top;
    }

    private object? CaptureCamera()
    {
        var camera = _engine.GameSession?.Camera;
        if (camera == null)
        {
            return null;
        }

        var state = camera.State;
        return new
        {
            targetXcm = state.TargetCm.X,
            targetYcm = state.TargetCm.Y,
            yawDeg = state.Yaw,
            pitchDeg = state.Pitch,
            distanceCm = state.DistanceCm,
            fovYDeg = state.FovYDeg
        };
    }

    private object? DescribeSelectedEntity()
    {
        if (_runtime.SelectedEntity == Arch.Core.Entity.Null ||
            !_engine.World.IsAlive(_runtime.SelectedEntity))
        {
            return null;
        }

        string name = _engine.World.TryGet(_runtime.SelectedEntity, out Name nameComponent) &&
            !string.IsNullOrWhiteSpace(nameComponent.Value)
                ? nameComponent.Value
                : $"Entity {_runtime.SelectedEntity.Id}";
        int stableId = _engine.World.TryGet(_runtime.SelectedEntity, out PresentationStableId stable)
            ? stable.Value
            : 0;
        EntitySpawnData? spawnData = _runtime.GetSelectedSpawnData(_engine.CurrentMapSession);
        return new
        {
            entityId = _runtime.SelectedEntity.Id,
            generation = _runtime.SelectedEntity.Version,
            stableId,
            name,
            instanceId = _runtime.SelectedInstanceId,
            template = spawnData?.Template ?? string.Empty,
            overrides = CaptureEntityOverrides(spawnData),
            obstacle = DescribeSelectedObstacle()
        };
    }

    private object? DescribeSelectedObstacle()
    {
        if (_runtime.SelectedEntity == Arch.Core.Entity.Null ||
            !_engine.World.IsAlive(_runtime.SelectedEntity) ||
            !_engine.World.TryGet(_runtime.SelectedEntity, out ManifestationObstacleIntent2D intent))
        {
            return null;
        }

        return new
        {
            shape = intent.Shape.ToString(),
            sinkPhysicsCollider = intent.SinkPhysicsCollider != 0,
            sinkNavigationObstacle = intent.SinkNavigationObstacle != 0,
            radiusCm = intent.RadiusCm,
            halfWidthCm = intent.HalfWidthCm,
            halfHeightCm = intent.HalfHeightCm,
            navRadiusCm = intent.NavRadiusCm,
            polygon = _engine.World.TryGet(_runtime.SelectedEntity, out ManifestationObstaclePolygon2D polygon)
                ? CapturePolygon(polygon)
                : Array.Empty<object>()
        };
    }

    private object[] CaptureEntityOverrides(EntitySpawnData? spawnData)
    {
        if (spawnData?.Overrides == null || spawnData.Overrides.Count == 0)
        {
            return Array.Empty<object>();
        }

        var result = new object[spawnData.Overrides.Count];
        int index = 0;
        foreach (KeyValuePair<string, JsonNode> kvp in spawnData.Overrides)
        {
            result[index++] = new
            {
                component = kvp.Key,
                json = kvp.Value.ToJsonString(JsonOptions)
            };
        }

        return result;
    }

    private static object[] CaptureObstaclePolygon(IReadOnlyList<Ludots.Core.Mathematics.WorldCmInt2> vertices)
    {
        var result = new object[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            result[i] = new { x = vertices[i].X, y = vertices[i].Y };
        }

        return result;
    }

    private static object[] CapturePolygon(ManifestationObstaclePolygon2D polygon)
    {
        var result = new object[polygon.VertexCount];
        for (int i = 0; i < polygon.VertexCount; i++)
        {
            var vertex = polygon.GetVertex(i);
            result[i] = new { x = vertex.X, y = vertex.Y };
        }

        return result;
    }

    private object[] CaptureEntityTemplates()
    {
        EntityTemplateKeyRegistry? registry = _engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry);
        if (registry == null || registry.Count == 0)
        {
            return Array.Empty<object>();
        }

        var mappings = registry.SnapshotMappings();
        var result = new object[mappings.Length];
        for (int i = 0; i < mappings.Length; i++)
        {
            result[i] = new { id = mappings[i].Name, key = mappings[i].Id };
        }

        return result;
    }

    private object[] CapturePath()
    {
        int count = Math.Min(_runtime.Nav.PathXcm.Length, _runtime.Nav.PathZcm.Length);
        var points = new object[count];
        for (int i = 0; i < count; i++)
        {
            points[i] = new { xCm = _runtime.Nav.PathXcm[i], yCm = _runtime.Nav.PathZcm[i] };
        }

        return points;
    }

    private static object[] CaptureBoards(IReadOnlyList<IBoard> boards)
    {
        var result = new object[boards.Count];
        for (int i = 0; i < boards.Count; i++)
        {
            IBoard board = boards[i];
            result[i] = new
            {
                board.Name,
                type = board.GetType().Name,
                widthCm = board.WorldSize.Bounds.Width,
                heightCm = board.WorldSize.Bounds.Height,
                terrain = board is ITerrainBoard terrainBoard && terrainBoard.LogicTerrain != null
                    ? new
                    {
                        topology = terrainBoard.LogicTerrain.Topology.ToString(),
                        widthCells = terrainBoard.LogicTerrain.WidthCells,
                        heightCells = terrainBoard.LogicTerrain.HeightCells,
                        chunkSizeCells = terrainBoard.LogicTerrain.ChunkSizeCells,
                        cellSizeCm = terrainBoard.LogicTerrain.HorizontalStepCm
                    }
                    : null
            };
        }

        return result;
    }

    private static object[] CaptureAuthoredBoards(IReadOnlyList<BoardConfig>? boards)
    {
        if (boards == null || boards.Count == 0)
        {
            return Array.Empty<object>();
        }

        var result = new object[boards.Count];
        for (int i = 0; i < boards.Count; i++)
        {
            BoardConfig board = boards[i];
            result[i] = new
            {
                board.Name,
                board.SpatialType,
                board.WidthInMacroTiles,
                board.HeightInMacroTiles,
                board.GridCellSizeCm,
                board.HexEdgeLengthCm,
                board.ChunkSizeCells,
                board.NavigationEnabled,
                board.DataFile,
                board.VisualHeightmapAsset,
                allocation = CaptureAllocationPreview(BoardAllocationPreviewCalculator.FromMacroTiles(
                    board.WidthInMacroTiles,
                    board.HeightInMacroTiles,
                    board.GridCellSizeCm))
            };
        }

        return result;
    }

    private static object? CaptureAllocationPreview(BoardAllocationPreview? preview)
    {
        if (preview == null)
        {
            return null;
        }

        return new
        {
            preview.IsValid,
            preview.WithinEditorBudget,
            preview.ExceedsDefaultWorldFootprint,
            preview.SnappedToMacroTile,
            preview.RequestedWidthMeters,
            preview.RequestedHeightMeters,
            preview.CellSizeCm,
            preview.MacroTileMeters,
            preview.TerrainChunkMeters,
            preview.RequestedWidthCells,
            preview.RequestedHeightCells,
            preview.WidthMacroTiles,
            preview.HeightMacroTiles,
            preview.AllocatedWidthCells,
            preview.AllocatedHeightCells,
            preview.WidthTerrainChunks,
            preview.HeightTerrainChunks,
            preview.TotalTerrainChunks,
            preview.FullTerrainBytes,
            preview.AllocatedWidthMeters,
            preview.AllocatedHeightMeters,
            eagerFullTerrainFileMacroTilesPerAxis =
                BoardAllocationPreviewCalculator.EagerFullTerrainFileMacroTilesPerAxis
        };
    }

    private static int CountLoadedTiles(NavQueryServiceRegistry? registry)
    {
        if (registry == null)
        {
            return 0;
        }

        int count = 0;
        IReadOnlyList<KeyValuePair<NavQueryServiceKey, NavTileStore>> stores = registry.SnapshotStores();
        for (int i = 0; i < stores.Count; i++)
        {
            count += stores[i].Value.SnapshotLoadedTiles().Length;
        }

        return count;
    }
}
