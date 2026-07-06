using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Map.Authoring;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.WebUI.DataPlane;

namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorRuntime : IDisposable
{
    private static readonly QueryDescription MapEntityQuery = new QueryDescription().WithAll<MapEntity>();
    private static readonly QueryDescription MapEntityPositionQuery = new QueryDescription().WithAll<MapEntity, WorldPositionCm>();
    private static readonly QueryDescription MapObstacleQuery =
        new QueryDescription().WithAll<MapEntity, WorldPositionCm, ManifestationObstacleIntent2D>();

    private readonly Dictionary<int, LiveMapEditorAuthoredEntity> _pendingByReceiptId = new();
    private readonly List<LiveMapEditorAuthoredEntity> _authoredEntities = new();
    private int _nextInstanceOrdinal = 1;
    private int _nextReceiptId = 1;
    private int _spawnReceiptChannelId;

    public bool PanelOpen { get; set; }
    public string Tool { get; private set; } = "inspect";
    public LiveMapEditorBrushState Brush { get; } = new();
    public LiveMapEditorObstacleState Obstacle { get; } = new();
    public LiveMapEditorNavState Nav { get; } = new();
    public LiveMapEditorSaveState Save { get; } = new();
    public LiveMapEditorMapState MapLifecycle { get; } = new();
    public LiveMapEditorViewState View { get; } = new();
    public LiveMapEditorTransportAuthoring Transport { get; } = new();
    public bool HasPickedWorld { get; private set; }
    public WorldCmInt2 PickedWorld { get; private set; }
    public bool HasPickedCell { get; private set; }
    public int PickedCellCol { get; private set; }
    public int PickedCellRow { get; private set; }
    public Entity SelectedEntity { get; private set; } = Entity.Null;
    public string SelectedInstanceId { get; private set; } = string.Empty;
    public int TerrainRevision { get; private set; }
    public bool HasDirtyAabb { get; private set; }
    public WorldAabbCm DirtyAabb { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public WebUiDataPlaneRuntime? DataPlane { get; set; }
    public WebUiDataPlaneTickPump? DataPlaneTickPump { get; set; }
    public WebUiQueuedCommandDispatcher? QueuedCommandDispatcher { get; set; }

    public void Dispose()
    {
        Transport.Dispose();
        DataPlaneTickPump = null;
        QueuedCommandDispatcher?.Dispose();
        QueuedCommandDispatcher = null;
        DataPlane?.Dispose();
        DataPlane = null;
    }

    public void SetTool(string tool)
    {
        if (string.IsNullOrWhiteSpace(tool))
        {
            throw new InvalidOperationException("Live editor tool is required.");
        }

        Tool = tool.Trim() switch
        {
            "inspect" => "inspect",
            "paint" => "paint",
            "entity" => "entity",
            "obstacle" => "obstacle",
            "sim" => "sim",
            "nav" => "nav",
            "config" => "config",
            "map" => "map",
            "minimap" => "minimap",
            "transport" => "transport",
            _ => throw new InvalidOperationException($"Unknown live editor tool '{tool}'.")
        };
    }

    public void SetBrush(
        int? radiusCells,
        string? mode,
        string? target,
        int? heightLevel,
        int? waterHeightLevel,
        int? areaId,
        float? cost,
        bool? blocked,
        bool? water,
        bool? ramp)
    {
        if (radiusCells.HasValue)
        {
            if (radiusCells.Value < 0 || radiusCells.Value > 64)
            {
                throw new InvalidOperationException("Brush radius cells must be between 0 and 64.");
            }

            Brush.RadiusCells = radiusCells.Value;
        }

        if (!string.IsNullOrWhiteSpace(mode))
        {
            Brush.Mode = mode.Trim() switch
            {
                "set" => "set",
                "raise" => "raise",
                "lower" => "lower",
                _ => throw new InvalidOperationException($"Unknown brush mode '{mode}'.")
            };
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            Brush.Target = target.Trim() switch
            {
                "all" => "all",
                "height" => "height",
                "water" => "water",
                "area" => "area",
                "cost" => "cost",
                "blocked" => "blocked",
                "ramp" => "ramp",
                _ => throw new InvalidOperationException($"Unknown brush target '{target}'.")
            };
        }

        if (heightLevel.HasValue)
        {
            if (heightLevel.Value < 0 || heightLevel.Value > 255)
            {
                throw new InvalidOperationException("Brush height level must be between 0 and 255.");
            }

            Brush.HeightLevel = (byte)heightLevel.Value;
        }

        if (waterHeightLevel.HasValue)
        {
            if (waterHeightLevel.Value < 0 || waterHeightLevel.Value > 255)
            {
                throw new InvalidOperationException("Brush water height level must be between 0 and 255.");
            }

            Brush.WaterHeightLevel = (byte)waterHeightLevel.Value;
        }

        if (areaId.HasValue)
        {
            if (areaId.Value < 0 || areaId.Value > 255)
            {
                throw new InvalidOperationException("Brush area id must be between 0 and 255.");
            }

            Brush.AreaId = (byte)areaId.Value;
        }

        if (cost.HasValue)
        {
            if (cost.Value <= 0f || float.IsNaN(cost.Value))
            {
                throw new InvalidOperationException("Brush cost must be > 0.");
            }

            Brush.Cost = cost.Value;
        }

        if (blocked.HasValue) Brush.Blocked = blocked.Value;
        if (water.HasValue) Brush.Water = water.Value;
        if (ramp.HasValue) Brush.Ramp = ramp.Value;
    }

    public void UpdatePickedWorld(GameEngine engine, WorldCmInt2 worldCm)
    {
        PickedWorld = worldCm;
        HasPickedWorld = true;
        if (TryResolveGridCell(engine, worldCm, out int col, out int row))
        {
            PickedCellCol = col;
            PickedCellRow = row;
            HasPickedCell = true;
        }
        else
        {
            HasPickedCell = false;
        }
    }

    public void ClearPick()
    {
        HasPickedWorld = false;
        HasPickedCell = false;
    }

    public WebUiCommandResult PaintTerrain(GameEngine engine, int? col, int? row, int? radiusCells)
    {
        try
        {
            MutableGridLogicTerrainField mutable = EnsureMutableGridTerrain(engine);
            int targetCol = col ?? PickedCellCol;
            int targetRow = row ?? PickedCellRow;
            if ((col == null || row == null) && !HasPickedCell)
            {
                return Fail("no_pick", "Paint requires a picked grid cell or explicit col/row.");
            }

            int radius = radiusCells ?? Brush.RadiusCells;
            if (radius < 0 || radius > 64)
            {
                return Fail("brush_radius_invalid", "Brush radius cells must be between 0 and 64.");
            }
            int minCol = Math.Max(0, targetCol - radius);
            int maxCol = Math.Min(mutable.WidthCells - 1, targetCol + radius);
            int minRow = Math.Max(0, targetRow - radius);
            int maxRow = Math.Min(mutable.HeightCells - 1, targetRow + radius);
            int radiusSq = radius * radius;
            RuntimeIncrementalNavMeshRebuildQueue? navQueue = null;
            bool includeNeighborTiles = true;
            if (engine.TryGetService(CoreServiceKeys.NavMeshBakeConfig, out NavMeshBakeConfig? navConfig))
            {
                if (navConfig.ParsedMode != NavBakeMode.RuntimeIncremental ||
                    navConfig.ParsedAlgorithm != NavBakeAlgorithmKind.Cdt)
                {
                    return Fail("nav_mode_unsupported", "Terrain paint on a nav-enabled map requires navmesh mode runtime-incremental + cdt.");
                }

                navQueue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
                    ?? throw new InvalidOperationException("RuntimeIncrementalNavMeshRebuildQueue is required for terrain paint on a nav-enabled map.");
                includeNeighborTiles = navConfig.RuntimeIncremental?.IncludeNeighborTiles ?? true;
            }

            for (int y = minRow; y <= maxRow; y++)
            {
                for (int x = minCol; x <= maxCol; x++)
                {
                    int dx = x - targetCol;
                    int dy = y - targetRow;
                    if (radius > 0 && dx * dx + dy * dy > radiusSq)
                    {
                        continue;
                    }

                    LogicTerrainCell current = mutable.GetCell(x, y);
                    mutable.SetCell(x, y, ApplyBrush(current));
                }
            }

            TerrainRevision++;
            WorldAabbCm dirty = CellsToAabb(mutable, minCol, minRow, maxCol, maxRow);
            MergeDirtyAabb(dirty);
            engine.RefreshFocusedLogicTerrainVisualHeightmap(TerrainRevision);
            if (navQueue != null)
            {
                navQueue.EnqueueDirtyAabb(dirty, includeNeighborTiles);
                Nav.PendingTiles = navQueue.PendingTileCount;
            }

            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("paint_failed", ex.Message);
        }
    }

    public WebUiCommandResult BucketFillWater(GameEngine engine, int? col, int? row, int? waterHeightLevel)
    {
        try
        {
            MutableGridLogicTerrainField mutable = EnsureMutableGridTerrain(engine);
            int targetCol = col ?? PickedCellCol;
            int targetRow = row ?? PickedCellRow;
            if ((col == null || row == null) && !HasPickedCell)
            {
                return Fail("no_pick", "Water bucket requires a picked grid cell or explicit col/row.");
            }

            if (!mutable.IsInBounds(targetCol, targetRow))
            {
                return Fail("bucket_out_of_bounds", "Water bucket target is outside the terrain grid.");
            }

            RuntimeIncrementalNavMeshRebuildQueue? navQueue = null;
            bool includeNeighborTiles = true;
            if (engine.TryGetService(CoreServiceKeys.NavMeshBakeConfig, out NavMeshBakeConfig? navConfig))
            {
                if (navConfig.ParsedMode != NavBakeMode.RuntimeIncremental ||
                    navConfig.ParsedAlgorithm != NavBakeAlgorithmKind.Cdt)
                {
                    return Fail("nav_mode_unsupported", "Water bucket on a nav-enabled map requires navmesh mode runtime-incremental + cdt.");
                }

                navQueue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
                    ?? throw new InvalidOperationException("RuntimeIncrementalNavMeshRebuildQueue is required for water bucket on a nav-enabled map.");
                includeNeighborTiles = navConfig.RuntimeIncremental?.IncludeNeighborTiles ?? true;
            }

            LogicTerrainCell seed = mutable.GetCell(targetCol, targetRow);
            byte fillWaterHeight = waterHeightLevel.HasValue
                ? ValidateByte(waterHeightLevel.Value, "Water bucket height level")
                : ResolveBrushWaterHeight(seed.HeightLevel);
            var visited = new bool[checked(mutable.WidthCells * mutable.HeightCells)];
            var queue = new Queue<(int Col, int Row)>();
            queue.Enqueue((targetCol, targetRow));
            int minCol = targetCol;
            int maxCol = targetCol;
            int minRow = targetRow;
            int maxRow = targetRow;
            int changed = 0;

            while (queue.Count > 0)
            {
                (int x, int y) = queue.Dequeue();
                if (!mutable.IsInBounds(x, y))
                {
                    continue;
                }

                int index = y * mutable.WidthCells + x;
                if (visited[index])
                {
                    continue;
                }

                visited[index] = true;
                LogicTerrainCell current = mutable.GetCell(x, y);
                if (current.HeightLevel != seed.HeightLevel)
                {
                    continue;
                }

                mutable.SetCell(x, y, new LogicTerrainCell(
                    current.HeightLevel,
                    fillWaterHeight,
                    current.SurfaceFlags | LogicTerrainSurfaceFlags.Water,
                    current.AreaId,
                    current.Cost));
                changed++;
                minCol = Math.Min(minCol, x);
                maxCol = Math.Max(maxCol, x);
                minRow = Math.Min(minRow, y);
                maxRow = Math.Max(maxRow, y);

                queue.Enqueue((x - 1, y));
                queue.Enqueue((x + 1, y));
                queue.Enqueue((x, y - 1));
                queue.Enqueue((x, y + 1));
            }

            if (changed == 0)
            {
                return WebUiCommandResult.Ok();
            }

            TerrainRevision++;
            WorldAabbCm dirty = CellsToAabb(mutable, minCol, minRow, maxCol, maxRow);
            MergeDirtyAabb(dirty);
            engine.RefreshFocusedLogicTerrainVisualHeightmap(TerrainRevision);
            if (navQueue != null)
            {
                navQueue.EnqueueDirtyAabb(dirty, includeNeighborTiles);
                Nav.PendingTiles = navQueue.PendingTileCount;
            }

            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("water_bucket_failed", ex.Message);
        }
    }

    public WebUiCommandResult PlaceEntity(GameEngine engine, string templateId, int? xCm, int? yCm)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return Fail("template_required", "Entity placement requires a template id.");
            }

            WorldCmInt2 world = ResolveCommandWorld(xCm, yCm);
            MapSession session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("Entity placement requires a focused map.");
            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue is required for live entity placement.");
            int channelId = ResolveSpawnReceiptChannel(engine);
            int receiptId = _nextReceiptId++;
            string instanceId = $"live_{session.MapId.Value}_{_nextInstanceOrdinal++:0000}";
            EntitySpawnData spawnData = CreateSpawnData(instanceId, templateId.Trim(), world);
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = templateId.Trim(),
                WorldPositionCm = Fix64Vec2.FromInt(world.X, world.Y),
                HasWorldPosition = 1,
                MapId = session.MapId,
                EmitReceipt = 1,
                ReceiptChannelId = channelId,
                ReceiptId = receiptId,
            };

            if (!queue.TryEnqueue(in request))
            {
                return Fail("spawn_queue_full", "RuntimeEntitySpawnQueue is full.");
            }

            var authored = new LiveMapEditorAuthoredEntity
            {
                InstanceId = instanceId,
                SpawnData = spawnData,
                ReceiptId = receiptId,
            };
            _authoredEntities.Add(authored);
            _pendingByReceiptId[receiptId] = authored;
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("place_entity_failed", ex.Message);
        }
    }

    public WebUiCommandResult SelectNearestEntity(GameEngine engine, int? xCm, int? yCm, int radiusCm = 150)
    {
        try
        {
            WorldCmInt2 world = ResolveCommandWorld(xCm, yCm);
            Entity best = Entity.Null;
            string instanceId = string.Empty;
            long bestD2 = (long)radiusCm * radiusCm;
            MapId mapId = engine.CurrentMapSession?.MapId ?? default;

            engine.World.Query(in MapEntityPositionQuery, (Entity entity, ref MapEntity mapEntity, ref WorldPositionCm position) =>
            {
                if (!mapEntity.MapId.Equals(mapId) || engine.World.Has<PresentationDestroyPending>(entity))
                {
                    return;
                }

                WorldCmInt2 pos = position.ToWorldCmInt2();
                long dx = pos.X - world.X;
                long dy = pos.Y - world.Y;
                long d2 = dx * dx + dy * dy;
                if (d2 <= bestD2)
                {
                    bestD2 = d2;
                    best = entity;
                    instanceId = ResolveInstanceId(engine.CurrentMapSession, entity);
                }
            });

            SelectedEntity = best;
            SelectedInstanceId = instanceId;
            return best == Entity.Null
                ? Fail("entity_not_found", "No map entity was found near the requested point.")
                : WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("select_entity_failed", ex.Message);
        }
    }

    public WebUiCommandResult RemoveSelectedEntity(GameEngine engine)
    {
        if (SelectedEntity == Entity.Null || !engine.World.IsAlive(SelectedEntity))
        {
            return Fail("no_selection", "No live selected entity is available to remove.");
        }

        return RemoveEntity(engine, SelectedEntity, SelectedInstanceId);
    }

    public WebUiCommandResult RemoveEntity(GameEngine engine, Entity entity, string instanceId)
    {
        try
        {
            if (entity == Entity.Null || !engine.World.IsAlive(entity))
            {
                return Fail("entity_not_alive", "The requested entity is not alive.");
            }

            string resolvedInstanceId = string.IsNullOrWhiteSpace(instanceId)
                ? ResolveInstanceId(engine.CurrentMapSession, entity)
                : instanceId.Trim();
            if (!string.IsNullOrWhiteSpace(resolvedInstanceId))
            {
                MarkAuthoredRemoved(resolvedInstanceId);
                RemoveFromMapConfig(engine.CurrentMapSession, resolvedInstanceId);
            }

            if (engine.World.Has<PresentationStableId>(entity))
            {
                PresentationEntityLifecycle.RequestDestroy(engine.World, entity, "LiveMapEditor entity removal");
            }
            else
            {
                engine.World.Destroy(entity);
            }

            if (SelectedEntity == entity)
            {
                SelectedEntity = Entity.Null;
                SelectedInstanceId = string.Empty;
            }

            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("remove_entity_failed", ex.Message);
        }
    }

    public WebUiCommandResult SetObstacleOptions(
        string? templateId,
        string? shape,
        int? radiusCm,
        int? halfWidthCm,
        int? halfHeightCm,
        int? navRadiusCm,
        bool? sinkPhysicsCollider,
        bool? sinkNavigationObstacle,
        WorldCmInt2[]? polygonVertices)
    {
        try
        {
            ApplyObstacleOptions(
                templateId,
                shape,
                radiusCm,
                halfWidthCm,
                halfHeightCm,
                navRadiusCm,
                sinkPhysicsCollider,
                sinkNavigationObstacle,
                polygonVertices);
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("set_obstacle_failed", ex.Message);
        }
    }

    public WebUiCommandResult PlaceObstacle(
        GameEngine engine,
        string? templateId,
        string? shape,
        int? radiusCm,
        int? halfWidthCm,
        int? halfHeightCm,
        int? navRadiusCm,
        bool? sinkPhysicsCollider,
        bool? sinkNavigationObstacle,
        WorldCmInt2[]? polygonVertices,
        int? xCm,
        int? yCm)
    {
        try
        {
            ApplyObstacleOptions(
                templateId,
                shape,
                radiusCm,
                halfWidthCm,
                halfHeightCm,
                navRadiusCm,
                sinkPhysicsCollider,
                sinkNavigationObstacle,
                polygonVertices);

            if (!Obstacle.SinkPhysicsCollider && !Obstacle.SinkNavigationObstacle)
            {
                return Fail("obstacle_sink_required", "Obstacle requires at least one physics or navigation sink.");
            }

            WorldCmInt2 world = ResolveCommandWorld(xCm, yCm);
            MapSession session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("Obstacle placement requires a focused map.");
            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue is required for live obstacle placement.");
            EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
                ?? throw new InvalidOperationException("EntityTemplateKeyRegistry is required for live obstacle placement.");
            if (!templateKeys.TryGetId(Obstacle.TemplateId, out _))
            {
                return Fail("obstacle_template_missing", $"Obstacle template '{Obstacle.TemplateId}' is not registered.");
            }

            int channelId = ResolveSpawnReceiptChannel(engine);
            int receiptId = _nextReceiptId++;
            string instanceId = $"live_obstacle_{session.MapId.Value}_{_nextInstanceOrdinal++:0000}";
            Dictionary<string, JsonNode> overrides = BuildObstacleOverrides(world);
            EntitySpawnData spawnData = CreateSpawnData(instanceId, Obstacle.TemplateId, world, overrides);
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = Obstacle.TemplateId,
                WorldPositionCm = Fix64Vec2.FromInt(world.X, world.Y),
                HasWorldPosition = 1,
                MapId = session.MapId,
                EmitReceipt = 1,
                ReceiptChannelId = channelId,
                ReceiptId = receiptId,
                ComponentPatches = BuildObstacleComponentPatches(overrides),
            };

            if (!queue.TryEnqueue(in request))
            {
                return Fail("spawn_queue_full", "RuntimeEntitySpawnQueue is full.");
            }

            var authored = new LiveMapEditorAuthoredEntity
            {
                InstanceId = instanceId,
                SpawnData = spawnData,
                ReceiptId = receiptId,
            };
            _authoredEntities.Add(authored);
            _pendingByReceiptId[receiptId] = authored;
            MarkObstacleDirty(engine, ComputeObstacleAabb(world, Obstacle));
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("place_obstacle_failed", ex.Message);
        }
    }

    public WebUiCommandResult EraseObstacleAt(GameEngine engine, int? xCm, int? yCm)
    {
        try
        {
            WorldCmInt2 world = ResolveCommandWorld(xCm, yCm);
            MapId mapId = engine.CurrentMapSession?.MapId ?? default;
            Entity best = Entity.Null;
            string instanceId = string.Empty;
            WorldAabbCm dirty = default;
            long bestScore = long.MaxValue;

            engine.World.Query(in MapObstacleQuery, (Entity entity, ref MapEntity mapEntity, ref WorldPositionCm position, ref ManifestationObstacleIntent2D intent) =>
            {
                if (!mapEntity.MapId.Equals(mapId) || engine.World.Has<PresentationDestroyPending>(entity))
                {
                    return;
                }

                if (!TryHitObstacle(engine.World, entity, world, position.ToWorldCmInt2(), in intent, out long score))
                {
                    return;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = entity;
                    instanceId = ResolveInstanceId(engine.CurrentMapSession, entity);
                    dirty = ComputeObstacleAabb(engine.World, entity, position.ToWorldCmInt2(), in intent);
                }
            });

            if (best == Entity.Null)
            {
                return Fail("obstacle_not_found", "No obstacle was found at the requested point.");
            }

            MarkObstacleDirty(engine, dirty);
            return RemoveEntity(engine, best, instanceId);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("erase_obstacle_failed", ex.Message);
        }
    }

    public WebUiCommandResult SetSelectedEntityOverride(GameEngine engine, string? componentName, string? json)
    {
        try
        {
            if (SelectedEntity == Entity.Null || !engine.World.IsAlive(SelectedEntity))
            {
                return Fail("no_selection", "No selected entity is available for override editing.");
            }

            string component = NormalizeComponentName(componentName);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Fail("override_json_required", "Entity override JSON is required.");
            }

            JsonNode node = JsonNode.Parse(json)
                ?? throw new InvalidOperationException("Entity override JSON must not be null.");
            if (!Ludots.Core.Config.ComponentRegistry.TryGetComponentType(component, out ComponentType componentType))
            {
                return Fail("override_component_unknown", $"Component '{component}' is not registered.");
            }

            ValidateComponentOverridePayload(engine.World, component, node.DeepClone());
            EntitySpawnData spawnData = ResolveSelectedSpawnData(engine.CurrentMapSession)
                ?? throw new InvalidOperationException("Selected entity is not backed by a map authoring entry.");

            if (engine.World.Has(SelectedEntity, componentType))
            {
                engine.World.Remove(SelectedEntity, componentType);
            }

            Ludots.Core.Config.ComponentRegistry.Apply(
                SelectedEntity,
                component,
                node.DeepClone(),
                $"LiveMapEditor selected entity '{SelectedInstanceId}' override '{component}'");
            spawnData.Overrides ??= new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            spawnData.Overrides[component] = node.DeepClone();
            MarkDirtyIfObstacleOverride(engine, SelectedEntity, component);
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (JsonException ex)
        {
            LastError = ex.Message;
            return Fail("override_json_invalid", ex.Message);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("set_entity_override_failed", ex.Message);
        }
    }

    public WebUiCommandResult DeleteSelectedEntityOverride(GameEngine engine, string? componentName)
    {
        try
        {
            string component = NormalizeComponentName(componentName);
            EntitySpawnData spawnData = ResolveSelectedSpawnData(engine.CurrentMapSession)
                ?? throw new InvalidOperationException("Selected entity is not backed by a map authoring entry.");
            if (spawnData.Overrides == null || !spawnData.Overrides.Remove(component))
            {
                return Fail("override_missing", $"Selected entity has no '{component}' override.");
            }

            MarkDirtyIfObstacleOverride(engine, SelectedEntity, component);
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("delete_entity_override_failed", ex.Message);
        }
    }

    public WebUiCommandResult RebuildDirtyNav(GameEngine engine, int maxTiles)
        => RebuildNav(engine, "dirty", maxTiles, Nav.BakeIncludeNeighbors, Nav.BakeParallel);

    public WebUiCommandResult SetBakeOptions(string? scope, int? maxTiles, bool? includeNeighbors, bool? parallel)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(scope))
            {
                Nav.BakeScope = NormalizeBakeScope(scope);
            }

            if (maxTiles.HasValue)
            {
                if (maxTiles.Value < 1 || maxTiles.Value > 512)
                {
                    return Fail("bake_budget_invalid", "Bake max tiles must be between 1 and 512.");
                }

                Nav.BakeMaxTiles = maxTiles.Value;
            }

            if (includeNeighbors.HasValue) Nav.BakeIncludeNeighbors = includeNeighbors.Value;
            if (parallel.HasValue) Nav.BakeParallel = parallel.Value;
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("set_bake_options_failed", ex.Message);
        }
    }

    public WebUiCommandResult EstimateNavBake(GameEngine engine, string? scope, bool? includeNeighbors)
    {
        try
        {
            LogicTerrainField terrain = engine.LogicTerrain
                ?? throw new InvalidOperationException("Nav bake estimate requires LogicTerrainField.");
            string resolvedScope = NormalizeBakeScope(scope ?? Nav.BakeScope);
            bool neighbors = includeNeighbors ?? Nav.BakeIncludeNeighbors;
            RuntimeIncrementalNavMeshRebuildQueue? queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue);
            int estimate = resolvedScope switch
            {
                "full" => checked(terrain.WidthChunks * terrain.HeightChunks),
                "dirty" or "dirtyNeighbors" => HasDirtyAabb
                    ? CountTilesForAabb(terrain, DirtyAabb, includeNeighbors: resolvedScope == "dirtyNeighbors" || neighbors)
                    : queue?.PendingTileCount ?? 0,
                _ => throw new InvalidOperationException($"Unknown bake scope '{resolvedScope}'.")
            };

            Nav.BakeScope = resolvedScope;
            Nav.BakeIncludeNeighbors = neighbors;
            Nav.LastEstimatedTiles = estimate;
            Nav.LastMessage = $"estimate {estimate} tiles for {resolvedScope}";
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("estimate_nav_bake_failed", ex.Message);
        }
    }

    public WebUiCommandResult RebuildNav(GameEngine engine, string? scope, int? maxTiles, bool? includeNeighbors, bool? parallel)
    {
        try
        {
            if (Nav.ConfigDirty)
            {
                return Fail("nav_config_reload_required", "Navigation config changed; reload the current map before baking.");
            }

            NavMeshBakeConfig config = engine.GetService(CoreServiceKeys.NavMeshBakeConfig)
                ?? throw new InvalidOperationException("NavMeshBakeConfig is missing.");
            if (config.ParsedMode != NavBakeMode.RuntimeIncremental || config.ParsedAlgorithm != NavBakeAlgorithmKind.Cdt)
            {
                return Fail("nav_mode_unsupported", "Live editor runtime rebake requires navmesh mode runtime-incremental + cdt.");
            }

            RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
                ?? throw new InvalidOperationException("RuntimeIncrementalNavMeshRebuildQueue is missing.");
            LogicTerrainField terrain = engine.LogicTerrain
                ?? throw new InvalidOperationException("Nav rebuild requires LogicTerrainField.");
            string resolvedScope = NormalizeBakeScope(scope ?? Nav.BakeScope);
            bool neighbors = includeNeighbors ?? Nav.BakeIncludeNeighbors;
            int budget = Math.Clamp(maxTiles ?? Nav.BakeMaxTiles, 1, 512);
            EnqueueBakeScope(queue, terrain, resolvedScope, neighbors);
            RuntimeNavMeshRebuildBatch batch = queue.ProcessBudget(budget);
            Nav.LastRebuiltTiles = batch.RebuiltTileCount;
            Nav.LastFailedTiles = batch.FailedEntryCount;
            Nav.PendingTiles = batch.PendingTileCount;
            Nav.BakeScope = resolvedScope;
            Nav.BakeIncludeNeighbors = neighbors;
            Nav.BakeMaxTiles = budget;
            if (parallel.HasValue) Nav.BakeParallel = parallel.Value;
            Nav.LastMessage = $"rebuilt {batch.RebuiltTileCount}, failed {batch.FailedEntryCount}, pending {batch.PendingTileCount}";
            if (batch.FailedEntryCount == 0 && batch.PendingTileCount == 0)
            {
                HasDirtyAabb = false;
            }

            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("rebake_failed", ex.Message);
        }
    }

    public WebUiCommandResult ClearNavTiles(GameEngine engine)
    {
        try
        {
            NavQueryServiceRegistry registry = engine.GetService(CoreServiceKeys.NavQueryServices)
                ?? throw new InvalidOperationException("NavQueryServices is missing.");
            IReadOnlyList<KeyValuePair<NavQueryServiceKey, NavTileStore>> stores = registry.SnapshotStores();
            for (int i = 0; i < stores.Count; i++)
            {
                stores[i].Value.Clear();
            }

            Nav.PendingTiles = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)?.PendingTileCount ?? 0;
            Nav.LastRebuiltTiles = 0;
            Nav.LastFailedTiles = 0;
            Nav.LastMessage = $"cleared {stores.Count} nav tile stores";
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("clear_nav_failed", ex.Message);
        }
    }

    public WebUiCommandResult ReloadNavConfig(GameEngine engine)
    {
        try
        {
            string? mapId = engine.CurrentMapSession?.MapId.Value;
            engine.ReloadConfigs("Navigation", NavMeshConfigPaths.BakeConfigPath);
            AgentProfileRegistry agentProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
                ?? throw new InvalidOperationException("AgentProfiles is missing after navigation config reload.");
            NavMeshBakeConfig bakeConfig = new NavMeshBakeConfigLoader(engine.ConfigPipeline, agentProfiles)
                .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
            engine.SetService(CoreServiceKeys.NavMeshBakeConfig, bakeConfig);
            engine.SetService(CoreServiceKeys.NavMeshProfiles, new NavMeshProfileRegistry(bakeConfig, agentProfiles));
            if (!string.IsNullOrWhiteSpace(mapId))
            {
                engine.LoadMap(mapId);
            }

            Nav.ConfigDirty = false;
            Nav.ConfigStatus = "reloaded";
            Nav.ConfigMessage = string.IsNullOrWhiteSpace(mapId)
                ? "navigation config reloaded"
                : $"navigation config reloaded with map '{mapId}'";
            Nav.LastMessage = Nav.ConfigMessage;
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_config_reload_failed", ex.Message);
        }
    }

    public WebUiCommandResult SaveNavConfig(GameEngine engine)
    {
        try
        {
            MapSession session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("Navigation config save requires a focused map session.");
            string targetModId = ResolveWritableMapConfigTargetModId(engine, session);
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            var writer = new NavigationConfigAuthoringWriter(engine.VFS);
            NavigationConfigAuthoringSaveResult result = writer.Save(targetModId, agentProfiles, bakeConfig);
            Nav.ConfigDirty = true;
            Nav.ConfigStatus = "saved";
            Nav.ConfigTargetModId = result.ModId;
            Nav.ConfigMessage =
                $"saved A{result.AgentProfileCount}/P{result.BakeProfileCount}/L{result.LayerCount}/R{result.AreaCount}; reload required";
            Nav.LastMessage = Nav.ConfigMessage;
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_config_save_failed", ex.Message);
        }
    }

    public WebUiCommandResult UpsertAgentProfile(
        GameEngine engine,
        string? id,
        float? radiusCm,
        float? heightCm,
        float? clearanceCm,
        float? draftCm,
        float? beamCm,
        float? mass,
        int? layer)
    {
        try
        {
            string profileId = NormalizeConfigId(id, "Agent profile id");
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            int index = FindAgentProfileIndex(agentProfiles, profileId);
            AgentProfileConfig profile = index >= 0
                ? CloneAgentProfile(agentProfiles[index])
                : new AgentProfileConfig
                {
                    Id = profileId,
                    RadiusCm = 50f,
                    HeightCm = 180f,
                    ClearanceCm = 0f,
                    DraftCm = 0f,
                    BeamCm = 0f,
                    Mass = 1f,
                    Layer = 0
                };

            if (radiusCm.HasValue) profile.RadiusCm = radiusCm.Value;
            if (heightCm.HasValue) profile.HeightCm = heightCm.Value;
            if (clearanceCm.HasValue) profile.ClearanceCm = clearanceCm.Value;
            if (draftCm.HasValue) profile.DraftCm = draftCm.Value;
            if (beamCm.HasValue) profile.BeamCm = beamCm.Value;
            if (mass.HasValue) profile.Mass = mass.Value;
            if (layer.HasValue) profile.Layer = layer.Value;
            profile.Validate(index >= 0 ? index : agentProfiles.Count);

            if (index >= 0) agentProfiles[index] = profile;
            else agentProfiles.Add(profile);
            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"agent profile '{profileId}' updated");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_profile_update_failed", ex.Message);
        }
    }

    public WebUiCommandResult DeleteAgentProfile(GameEngine engine, string? id)
    {
        try
        {
            string profileId = NormalizeConfigId(id, "Agent profile id");
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            int index = FindAgentProfileIndex(agentProfiles, profileId);
            if (index < 0)
            {
                return Fail("nav_profile_missing", $"Agent profile '{profileId}' is missing.");
            }

            agentProfiles.RemoveAt(index);
            ApplyNavigationConfigEdit(engine, agentProfiles, CaptureNavMeshBakeConfig(engine), $"agent profile '{profileId}' deleted");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_profile_delete_failed", ex.Message);
        }
    }

    public WebUiCommandResult UpsertBakeProfile(GameEngine engine, string? id, int? maxClimbCm, float? maxSlopeDeg)
    {
        try
        {
            string profileId = NormalizeConfigId(id, "Bake profile id");
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            int index = FindBakeProfileIndex(bakeConfig.Profiles, profileId);
            NavMeshAgentProfileConfig profile = index >= 0
                ? CloneBakeProfile(bakeConfig.Profiles[index])
                : new NavMeshAgentProfileConfig { Id = profileId, MaxClimbCm = 40, MaxSlopeDeg = 45f };
            if (maxClimbCm.HasValue) profile.MaxClimbCm = maxClimbCm.Value;
            if (maxSlopeDeg.HasValue) profile.MaxSlopeDeg = maxSlopeDeg.Value;
            if (index >= 0) bakeConfig.Profiles[index] = profile;
            else bakeConfig.Profiles.Add(profile);
            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"bake profile '{profileId}' updated");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_bake_profile_update_failed", ex.Message);
        }
    }

    public WebUiCommandResult DeleteBakeProfile(GameEngine engine, string? id)
    {
        try
        {
            string profileId = NormalizeConfigId(id, "Bake profile id");
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            int index = FindBakeProfileIndex(bakeConfig.Profiles, profileId);
            if (index < 0)
            {
                return Fail("nav_bake_profile_missing", $"Bake profile '{profileId}' is missing.");
            }

            bakeConfig.Profiles.RemoveAt(index);
            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"bake profile '{profileId}' deleted");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_bake_profile_delete_failed", ex.Message);
        }
    }

    public WebUiCommandResult UpsertNavLayer(GameEngine engine, string? id, int? layer)
    {
        try
        {
            string layerId = NormalizeConfigId(id, "Layer id");
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            int index = FindLayerIndex(bakeConfig.Layers, layerId);
            NavLayerConfig config = index >= 0
                ? CloneLayer(bakeConfig.Layers[index])
                : new NavLayerConfig { Id = layerId, Layer = 0 };
            if (layer.HasValue) config.Layer = layer.Value;
            if (index >= 0) bakeConfig.Layers[index] = config;
            else bakeConfig.Layers.Add(config);
            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"layer '{layerId}' updated");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_layer_update_failed", ex.Message);
        }
    }

    public WebUiCommandResult DeleteNavLayer(GameEngine engine, string? id)
    {
        try
        {
            string layerId = NormalizeConfigId(id, "Layer id");
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            int index = FindLayerIndex(bakeConfig.Layers, layerId);
            if (index < 0)
            {
                return Fail("nav_layer_missing", $"Layer '{layerId}' is missing.");
            }

            bakeConfig.Layers.RemoveAt(index);
            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"layer '{layerId}' deleted");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_layer_delete_failed", ex.Message);
        }
    }

    public WebUiCommandResult UpsertNavArea(GameEngine engine, string? id, int? areaId, float? cost)
    {
        try
        {
            string configId = NormalizeConfigId(id, "Area id");
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            int index = FindAreaIndex(bakeConfig.Areas, configId);
            NavAreaCostConfig config = index >= 0
                ? CloneArea(bakeConfig.Areas[index])
                : new NavAreaCostConfig { Id = configId, AreaId = 0, Cost = 1f };
            if (areaId.HasValue) config.AreaId = areaId.Value;
            if (cost.HasValue) config.Cost = cost.Value;
            if (index >= 0) bakeConfig.Areas[index] = config;
            else bakeConfig.Areas.Add(config);
            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"area '{configId}' updated");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_area_update_failed", ex.Message);
        }
    }

    public WebUiCommandResult DeleteNavArea(GameEngine engine, string? id)
    {
        try
        {
            string configId = NormalizeConfigId(id, "Area id");
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            int index = FindAreaIndex(bakeConfig.Areas, configId);
            if (index < 0)
            {
                return Fail("nav_area_missing", $"Area '{configId}' is missing.");
            }

            bakeConfig.Areas.RemoveAt(index);
            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"area '{configId}' deleted");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_area_delete_failed", ex.Message);
        }
    }

    public WebUiCommandResult SetNavMode(GameEngine engine, string? mode)
    {
        try
        {
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            bakeConfig.Mode = NormalizeNavMode(mode);
            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"mode set to '{bakeConfig.Mode}'");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_mode_update_failed", ex.Message);
        }
    }

    public WebUiCommandResult SetNavAlgorithm(GameEngine engine, string? algorithm)
    {
        try
        {
            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            bakeConfig.Algorithm = NormalizeNavAlgorithm(algorithm);
            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"algorithm set to '{bakeConfig.Algorithm}'");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_algorithm_update_failed", ex.Message);
        }
    }

    public WebUiCommandResult SetNavRuntimeField(GameEngine engine, string? field, float? numberValue, bool? boolValue)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return Fail("nav_runtime_field_required", "Runtime incremental field is required.");
            }

            List<AgentProfileConfig> agentProfiles = CaptureAgentProfiles(engine);
            NavMeshBakeConfig bakeConfig = CaptureNavMeshBakeConfig(engine);
            string name = field.Trim();
            switch (name)
            {
                case "tileBudgetPerFixedTick":
                    bakeConfig.RuntimeIncremental.TileBudgetPerFixedTick = checked((int)(numberValue
                        ?? throw new InvalidOperationException("tileBudgetPerFixedTick requires a numeric value.")));
                    break;
                case "includeNeighborTiles":
                    bakeConfig.RuntimeIncremental.IncludeNeighborTiles = boolValue
                        ?? throw new InvalidOperationException("includeNeighborTiles requires a boolean value.");
                    break;
                case "heightScaleMeters":
                    bakeConfig.RuntimeIncremental.HeightScaleMeters = numberValue
                        ?? throw new InvalidOperationException("heightScaleMeters requires a numeric value.");
                    break;
                case "minWalkableUpDot":
                    bakeConfig.RuntimeIncremental.MinWalkableUpDot = numberValue
                        ?? throw new InvalidOperationException("minWalkableUpDot requires a numeric value.");
                    break;
                case "cliffHeightThreshold":
                    bakeConfig.RuntimeIncremental.CliffHeightThreshold = checked((int)(numberValue
                        ?? throw new InvalidOperationException("cliffHeightThreshold requires a numeric value.")));
                    break;
                default:
                    return Fail("nav_runtime_field_unknown", $"Unknown runtime incremental field '{field}'.");
            }

            ApplyNavigationConfigEdit(engine, agentProfiles, bakeConfig, $"{name} updated");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Nav.ConfigStatus = "failed";
            Nav.ConfigMessage = ex.Message;
            LastError = ex.Message;
            return Fail("nav_runtime_update_failed", ex.Message);
        }
    }

    public WebUiCommandResult SetPathOptions(GameEngine engine, string? profileId, int? layer, int? maxPortals)
    {
        try
        {
            if (layer.HasValue)
            {
                if (layer.Value < 0 || layer.Value > 31)
                {
                    return Fail("path_layer_invalid", "Path simulation layer must be between 0 and 31.");
                }

                Nav.QueryLayer = layer.Value;
            }

            if (!string.IsNullOrWhiteSpace(profileId))
            {
                NavMeshProfileRegistry profiles = engine.GetService(CoreServiceKeys.NavMeshProfiles)
                    ?? throw new InvalidOperationException("NavMeshProfiles is missing.");
                string trimmed = profileId.Trim();
                if (!profiles.TryGetIndex(trimmed, out int index))
                {
                    return Fail("path_profile_missing", $"Nav profile '{trimmed}' is not registered.");
                }

                Nav.QueryProfileId = trimmed;
                Nav.QueryProfileIndex = index;
            }

            if (maxPortals.HasValue)
            {
                if (maxPortals.Value < 1 || maxPortals.Value > 4096)
                {
                    return Fail("path_max_portals_invalid", "Path simulation max portals must be between 1 and 4096.");
                }

                Nav.MaxPortals = maxPortals.Value;
            }

            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("set_path_options_failed", ex.Message);
        }
    }

    public WebUiCommandResult QueryPath(GameEngine engine, int? startXcm, int? startYcm, int? goalXcm, int? goalYcm)
    {
        try
        {
            if (Nav.ConfigDirty)
            {
                return Fail("nav_config_reload_required", "Navigation config changed; reload the current map before querying paths.");
            }

            if (startXcm.HasValue && startYcm.HasValue)
            {
                Nav.Start = new WorldCmInt2(startXcm.Value, startYcm.Value);
                Nav.HasStart = true;
            }

            if (goalXcm.HasValue && goalYcm.HasValue)
            {
                Nav.Goal = new WorldCmInt2(goalXcm.Value, goalYcm.Value);
                Nav.HasGoal = true;
            }

            if (!Nav.HasStart || !Nav.HasGoal)
            {
                return Fail("path_points_required", "Path query requires start and goal.");
            }

            NavQueryServiceRegistry registry = engine.GetService(CoreServiceKeys.NavQueryServices)
                ?? throw new InvalidOperationException("NavQueryServices is missing.");
            ResolveActiveNavProfile(engine);
            if (!registry.TryCreateQuery(Nav.QueryLayer, Nav.QueryProfileIndex, NavAreaCostTable.CreateDefault(), out NavQueryService service))
            {
                return Fail("nav_query_missing", $"No nav query service is registered for layer {Nav.QueryLayer} profile {Nav.QueryProfileIndex}.");
            }

            long before = Stopwatch.GetTimestamp();
            NavPathResult result = service.TryFindPath(
                Nav.Start.X,
                Nav.Start.Y,
                Nav.Goal.X,
                Nav.Goal.Y,
                Nav.MaxPortals);
            long elapsedUs = Stopwatch.GetElapsedTime(before).Ticks / 10;
            Nav.SetPath(result, elapsedUs);
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("path_query_failed", ex.Message);
        }
    }

    public WebUiCommandResult SaveMap(GameEngine engine)
    {
        try
        {
            MapSession session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("Save requires a focused map session.");
            var writer = new MapAuthoringAssetWriter(engine);
            MapAuthoringSaveResult result = writer.Save(new MapAuthoringSaveRequest
            {
                Session = session,
                LogicTerrain = engine.LogicTerrain,
                Entities = BuildEntitySaveList(session),
                WriteNavTiles = true,
            });
            string transportMessage = "transport unchanged";
            if (Transport.Available)
            {
                WebUiCommandResult transportSave = Transport.Save(engine);
                if (!transportSave.Success)
                {
                    Save.Status = "failed";
                    Save.Message = transportSave.Message;
                    LastError = transportSave.Message;
                    return transportSave;
                }

                transportMessage = Transport.LastSaveMessage;
            }

            Save.Status = "saved";
            Save.Message = $"saved {result.EntityCount} entities, {result.NavTileCount} nav tiles; {transportMessage}";
            Save.MapConfigPath = result.MapConfigPath;
            Save.EntityCount = result.EntityCount;
            Save.NavTileCount = result.NavTileCount;
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            Save.Status = "failed";
            Save.Message = ex.Message;
            LastError = ex.Message;
            return Fail("save_failed", ex.Message);
        }
    }

    public void DrainSpawnReceipts(GameEngine engine)
    {
        if (_spawnReceiptChannelId <= 0 ||
            engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue) is not RuntimeEntitySpawnReceiptQueue receipts)
        {
            return;
        }

        while (receipts.TryDequeueForChannel(_spawnReceiptChannelId, out RuntimeEntitySpawnReceipt receipt))
        {
            if (!_pendingByReceiptId.Remove(receipt.ReceiptId, out LiveMapEditorAuthoredEntity? authored))
            {
                continue;
            }

            authored.Entity = receipt.Entity;
            MapSession? session = engine.CurrentMapSession;
            if (session != null && !string.IsNullOrWhiteSpace(authored.InstanceId))
            {
                session.EntityIndex.Register(session.MapId.Value, authored.InstanceId, receipt.Entity);
            }
        }
    }

    public int CountMapEntities(GameEngine engine)
    {
        int count = 0;
        MapId mapId = engine.CurrentMapSession?.MapId ?? default;
        engine.World.Query(in MapEntityQuery, (Entity entity, ref MapEntity mapEntity) =>
        {
            if (mapEntity.MapId.Equals(mapId) && !engine.World.Has<PresentationDestroyPending>(entity))
            {
                count++;
            }
        });
        return count;
    }

    public int CountObstacles(GameEngine engine)
    {
        int count = 0;
        MapId mapId = engine.CurrentMapSession?.MapId ?? default;
        engine.World.Query(in MapObstacleQuery, (Entity entity, ref MapEntity mapEntity, ref WorldPositionCm _, ref ManifestationObstacleIntent2D __) =>
        {
            if (mapEntity.MapId.Equals(mapId) && !engine.World.Has<PresentationDestroyPending>(entity))
            {
                count++;
            }
        });
        return count;
    }

    public EntitySpawnData? GetSelectedSpawnData(MapSession? session)
        => ResolveSelectedSpawnData(session);

    public IReadOnlyList<LiveMapEditorAuthoredEntity> AuthoredEntities => _authoredEntities;

    public WebUiCommandResult SetViewToggle(string? name, bool? enabled)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name) || !enabled.HasValue)
            {
                return Fail("view_toggle_required", "View toggle requires name and enabled.");
            }

            switch (name.Trim())
            {
                case "grid":
                    View.ShowGrid = enabled.Value;
                    break;
                case "chunks":
                    View.ShowChunks = enabled.Value;
                    break;
                case "navmesh":
                    View.ShowNavMesh = enabled.Value;
                    break;
                case "path":
                    View.ShowPath = enabled.Value;
                    break;
                case "transport":
                    View.ShowTransport = enabled.Value;
                    break;
                case "entities":
                    View.ShowEntities = enabled.Value;
                    break;
                case "minimap":
                    View.ShowMinimap = enabled.Value;
                    break;
                default:
                    return Fail("view_toggle_unknown", $"Unknown view toggle '{name}'.");
            }

            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("set_view_toggle_failed", ex.Message);
        }
    }

    public WebUiCommandResult PanCameraTo(GameEngine engine, int? xCm, int? yCm)
    {
        try
        {
            if (!xCm.HasValue || !yCm.HasValue)
            {
                return Fail("camera_pan_target_required", "cameraPanTo requires xCm and yCm.");
            }

            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
            {
                TargetCm = new Vector2(xCm.Value, yCm.Value)
            });
            engine.GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("camera_pan_failed", ex.Message);
        }
    }

    public WebUiCommandResult PreviewBoardAllocation(
        string? slot,
        float? widthMeters,
        float? heightMeters,
        int? cellSizeCm)
    {
        try
        {
            BoardAllocationPreview preview = CreateAllocationPreview(widthMeters, heightMeters, cellSizeCm);
            string resolvedSlot = string.IsNullOrWhiteSpace(slot) ? "addBoard" : slot.Trim();
            if (string.Equals(resolvedSlot, "createMap", StringComparison.OrdinalIgnoreCase))
            {
                MapLifecycle.CreateMapPreview = preview;
            }
            else if (string.Equals(resolvedSlot, "addBoard", StringComparison.OrdinalIgnoreCase))
            {
                MapLifecycle.AddBoardPreview = preview;
            }
            else
            {
                return Fail("board_preview_slot_unknown", $"Unknown board allocation preview slot '{slot}'.");
            }

            MapLifecycle.Status = "preview";
            MapLifecycle.Message = $"preview {preview.WidthMacroTiles}x{preview.HeightMacroTiles} macro tiles";
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("board_preview_failed", ex.Message);
        }
    }

    public WebUiCommandResult SelectBoard(GameEngine engine, string? boardName)
    {
        try
        {
            MapSession session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("Board selection requires a focused map session.");
            BoardConfig board = ResolveAuthoredBoard(session.MapConfig, boardName ?? MapLifecycle.SelectedBoardName);
            MapLifecycle.SelectedBoardName = board.Name;
            MapLifecycle.Status = "selected";
            MapLifecycle.Message = $"selected board '{board.Name}'";
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Fail("select_board_failed", ex.Message);
        }
    }

    public WebUiCommandResult CreateMap(
        GameEngine engine,
        string? mapId,
        string? boardName,
        string? topology,
        float? widthMeters,
        float? heightMeters,
        int? cellSizeCm,
        int? hexEdgeLengthCm,
        bool? navigationEnabled,
        bool? loadAfterCreate)
    {
        try
        {
            MapSession currentSession = engine.CurrentMapSession
                ?? throw new InvalidOperationException("CreateMap requires a focused map session to resolve the writable target mod.");
            string resolvedMapId = NormalizeAssetId(mapId, "Map id");
            string resolvedBoardName = NormalizeBoardName(
                string.IsNullOrWhiteSpace(boardName) ? "default" : boardName,
                "Board name");
            string resolvedTopology = NormalizeTerrainBoardTopology(topology);
            BoardAllocationPreview preview = CreateAllocationPreview(widthMeters, heightMeters, cellSizeCm);
            EnsureValidAllocation(preview);

            var writer = new MapAuthoringAssetWriter(engine);
            string targetModId = writer.ResolveWritableTargetModId(currentSession);
            bool navEnabled = navigationEnabled ?? true;
            var config = new MapConfig
            {
                Id = resolvedMapId,
                Boards = new List<BoardConfig>
                {
                    CreateBoardConfig(resolvedBoardName, resolvedTopology, preview, hexEdgeLengthCm, navEnabled)
                },
                Entities = new List<EntitySpawnData>(),
                Tags = new List<string>()
            };
            EnsureLiveEditorMetadata(config);

            MapAuthoringConfigSaveResult result = writer.SaveConfig(targetModId, config, overwriteExisting: false);
            bool loaded = false;
            if (loadAfterCreate == true)
            {
                try
                {
                    engine.LoadMap(result.MapId);
                    loaded = string.Equals(
                        engine.CurrentMapSession?.MapId.Value,
                        result.MapId,
                        StringComparison.Ordinal);
                    if (!loaded)
                    {
                        throw new InvalidOperationException($"Map '{result.MapId}' was created but did not become the focused map.");
                    }
                }
                catch (Exception loadEx)
                {
                    MapLifecycle.CreateMapPreview = preview;
                    MapLifecycle.SelectedBoardName = resolvedBoardName;
                    MapLifecycle.Status = "created";
                    MapLifecycle.ReloadRequired = false;
                    MapLifecycle.TargetModId = result.ModId;
                    MapLifecycle.MapConfigPath = result.MapConfigPath;
                    MapLifecycle.Message =
                        $"created map '{result.MapId}' in mod '{result.ModId}', but load failed: {loadEx.Message}";
                    LastError = MapLifecycle.Message;
                    return Fail("create_map_load_failed", MapLifecycle.Message);
                }
            }

            MapLifecycle.CreateMapPreview = preview;
            MapLifecycle.SelectedBoardName = resolvedBoardName;
            MapLifecycle.Status = loaded ? "loaded" : "created";
            MapLifecycle.ReloadRequired = false;
            MapLifecycle.TargetModId = result.ModId;
            MapLifecycle.MapConfigPath = result.MapConfigPath;
            MapLifecycle.Message =
                loaded
                    ? $"created and loaded map '{result.MapId}' from mod '{result.ModId}'"
                    : $"created map '{result.MapId}' in mod '{result.ModId}'; launch or load that map to edit it";
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            MapLifecycle.Status = "failed";
            MapLifecycle.Message = ex.Message;
            LastError = ex.Message;
            return Fail("create_map_failed", ex.Message);
        }
    }

    public WebUiCommandResult AddBoard(
        GameEngine engine,
        string? boardName,
        string? topology,
        float? widthMeters,
        float? heightMeters,
        int? cellSizeCm,
        int? hexEdgeLengthCm,
        bool? navigationEnabled)
    {
        try
        {
            MapSession session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("AddBoard requires a focused map session.");
            string resolvedBoardName = NormalizeBoardName(boardName, "Board name");
            EnsureBoardNameAvailable(session.MapConfig, resolvedBoardName);
            string resolvedTopology = NormalizeTerrainBoardTopology(topology);
            BoardAllocationPreview preview = CreateAllocationPreview(widthMeters, heightMeters, cellSizeCm);
            EnsureValidAllocation(preview);

            session.MapConfig.Boards ??= new List<BoardConfig>();
            session.MapConfig.Boards.Add(CreateBoardConfig(
                resolvedBoardName,
                resolvedTopology,
                preview,
                hexEdgeLengthCm,
                navigationEnabled ?? true));
            MapAuthoringSaveResult result = SaveCurrentMapConfig(engine, session);
            ClearLoadedNavTilesIfAvailable(engine);
            MarkBoardLifecycleSaved(
                result,
                $"added board '{resolvedBoardName}'; save complete, reload required");
            MapLifecycle.AddBoardPreview = preview;
            MapLifecycle.SelectedBoardName = resolvedBoardName;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            MapLifecycle.Status = "failed";
            MapLifecycle.Message = ex.Message;
            LastError = ex.Message;
            return Fail("add_board_failed", ex.Message);
        }
    }

    public WebUiCommandResult DeleteBoard(GameEngine engine, string? boardName)
    {
        try
        {
            MapSession session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("DeleteBoard requires a focused map session.");
            BoardConfig board = ResolveAuthoredBoard(session.MapConfig, boardName ?? MapLifecycle.SelectedBoardName);
            if (session.MapConfig.Boards == null || session.MapConfig.Boards.Count <= 1)
            {
                return Fail("delete_board_last_board", "Cannot delete the last board in a map.");
            }

            session.MapConfig.Boards.Remove(board);
            MapAuthoringSaveResult result = SaveCurrentMapConfig(engine, session);
            ClearLoadedNavTilesIfAvailable(engine);
            MapLifecycle.SelectedBoardName = session.MapConfig.Boards[0].Name;
            MarkBoardLifecycleSaved(
                result,
                $"deleted board '{board.Name}'; save complete, reload required");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            MapLifecycle.Status = "failed";
            MapLifecycle.Message = ex.Message;
            LastError = ex.Message;
            return Fail("delete_board_failed", ex.Message);
        }
    }

    public WebUiCommandResult UpdateBoardSettings(
        GameEngine engine,
        string? boardName,
        int? cellSizeCm,
        int? hexEdgeLengthCm,
        bool? navigationEnabled)
    {
        try
        {
            MapSession session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("UpdateBoard requires a focused map session.");
            BoardConfig board = ResolveAuthoredBoard(session.MapConfig, boardName ?? MapLifecycle.SelectedBoardName);
            if (cellSizeCm.HasValue && cellSizeCm.Value <= 0)
            {
                return Fail("board_cell_size_invalid", "Board cell size cm must be > 0.");
            }

            if (hexEdgeLengthCm.HasValue && hexEdgeLengthCm.Value <= 0)
            {
                return Fail("board_hex_edge_invalid", "Board hex edge length cm must be > 0.");
            }

            bool scaleChanged =
                (cellSizeCm.HasValue && cellSizeCm.Value != board.GridCellSizeCm) ||
                (hexEdgeLengthCm.HasValue && hexEdgeLengthCm.Value != board.HexEdgeLengthCm);
            if (scaleChanged && !string.IsNullOrWhiteSpace(board.DataFile))
            {
                return Fail(
                    "board_datafile_scale_locked",
                    $"Board '{board.Name}' has DataFile '{board.DataFile}'; refuse to change scale without matching terrain data.");
            }

            if (cellSizeCm.HasValue) board.GridCellSizeCm = cellSizeCm.Value;
            if (hexEdgeLengthCm.HasValue) board.HexEdgeLengthCm = hexEdgeLengthCm.Value;
            if (navigationEnabled.HasValue) board.NavigationEnabled = navigationEnabled.Value;

            MapAuthoringSaveResult result = SaveCurrentMapConfig(engine, session);
            if (scaleChanged || navigationEnabled.HasValue)
            {
                ClearLoadedNavTilesIfAvailable(engine);
            }

            MapLifecycle.SelectedBoardName = board.Name;
            MarkBoardLifecycleSaved(
                result,
                $"updated board '{board.Name}'; save complete, reload required");
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            MapLifecycle.Status = "failed";
            MapLifecycle.Message = ex.Message;
            LastError = ex.Message;
            return Fail("update_board_failed", ex.Message);
        }
    }

    public WebUiCommandResult ReloadCurrentMap(GameEngine engine)
    {
        try
        {
            string mapId = engine.CurrentMapSession?.MapId.Value
                ?? throw new InvalidOperationException("ReloadMap requires a focused map session.");
            engine.LoadMap(mapId);
            MapLifecycle.ReloadRequired = false;
            MapLifecycle.Status = "reloaded";
            MapLifecycle.Message = $"reloaded map '{mapId}'";
            if (engine.CurrentMapSession?.PrimaryBoard != null)
            {
                MapLifecycle.SelectedBoardName = engine.CurrentMapSession.PrimaryBoard.Name;
            }

            Nav.ConfigDirty = false;
            Nav.ConfigStatus = "reloaded";
            Nav.ConfigMessage = MapLifecycle.Message;
            LastError = string.Empty;
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            MapLifecycle.Status = "failed";
            MapLifecycle.Message = ex.Message;
            LastError = ex.Message;
            return Fail("reload_map_failed", ex.Message);
        }
    }

    private static BoardAllocationPreview CreateAllocationPreview(
        float? widthMeters,
        float? heightMeters,
        int? cellSizeCm)
    {
        if (!widthMeters.HasValue || !heightMeters.HasValue)
        {
            throw new InvalidOperationException("Board allocation preview requires widthMeters and heightMeters.");
        }

        return BoardAllocationPreviewCalculator.FromDesiredMeters(
            widthMeters.Value,
            heightMeters.Value,
            cellSizeCm ?? SpatialScaleDefaults.CellCm);
    }

    private static void EnsureValidAllocation(BoardAllocationPreview preview)
    {
        if (!preview.IsValid ||
            preview.WidthMacroTiles <= 0 ||
            preview.HeightMacroTiles <= 0)
        {
            throw new InvalidOperationException("Board dimensions must allocate at least one MacroTile per axis.");
        }
    }

    private static BoardConfig CreateBoardConfig(
        string name,
        string topology,
        BoardAllocationPreview preview,
        int? hexEdgeLengthCm,
        bool navigationEnabled)
    {
        return new BoardConfig
        {
            Name = name,
            SpatialType = topology,
            WidthInMacroTiles = preview.WidthMacroTiles,
            HeightInMacroTiles = preview.HeightMacroTiles,
            GridCellSizeCm = preview.CellSizeCm,
            HexEdgeLengthCm = topology == "HexGrid"
                ? Math.Max(1, hexEdgeLengthCm ?? SpatialScaleDefaults.DefaultHexEdgeLengthCm)
                : SpatialScaleDefaults.DefaultHexEdgeLengthCm,
            ChunkSizeCells = SpatialScaleDefaults.PartitionChunkCells,
            NavigationEnabled = navigationEnabled
        };
    }

    private static string NormalizeTerrainBoardTopology(string? topology)
    {
        string value = string.IsNullOrWhiteSpace(topology) ? "Grid" : topology.Trim();
        if (string.Equals(value, "Grid", StringComparison.OrdinalIgnoreCase))
        {
            return "Grid";
        }

        if (string.Equals(value, "HexGrid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Hex", StringComparison.OrdinalIgnoreCase))
        {
            return "HexGrid";
        }

        throw new InvalidOperationException($"Live map editor board lifecycle supports Grid and HexGrid, got '{topology}'.");
    }

    private static string NormalizeAssetId(string? value, string label)
    {
        string normalized = NormalizeBoardName(value, label);
        if (normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        return normalized;
    }

    private static string NormalizeBoardName(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        string normalized = value.Trim();
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            bool allowed =
                (c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '_' ||
                c == '-' ||
                c == '.';
            if (!allowed)
            {
                throw new InvalidOperationException($"{label} contains unsupported character '{c}'.");
            }
        }

        return normalized;
    }

    private static void EnsureBoardNameAvailable(MapConfig config, string boardName)
    {
        if (config.Boards == null)
        {
            return;
        }

        for (int i = 0; i < config.Boards.Count; i++)
        {
            if (string.Equals(config.Boards[i].Name, boardName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Map already contains board '{boardName}'.");
            }
        }
    }

    private static BoardConfig ResolveAuthoredBoard(MapConfig config, string? boardName)
    {
        if (config.Boards == null || config.Boards.Count == 0)
        {
            throw new InvalidOperationException("Focused map has no authored boards.");
        }

        string name = string.IsNullOrWhiteSpace(boardName) ? config.Boards[0].Name : boardName.Trim();
        for (int i = 0; i < config.Boards.Count; i++)
        {
            BoardConfig board = config.Boards[i];
            if (string.Equals(board.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return board;
            }
        }

        throw new InvalidOperationException($"Focused map does not contain authored board '{name}'.");
    }

    private static void EnsureLiveEditorMetadata(MapConfig config)
    {
        config.Metadata ??= new Dictionary<string, JsonNode>();
        config.Metadata["liveMapEditor"] = new JsonObject
        {
            ["saveTarget"] = true
        };
    }

    private MapAuthoringSaveResult SaveCurrentMapConfig(GameEngine engine, MapSession session)
    {
        var writer = new MapAuthoringAssetWriter(engine);
        return writer.Save(new MapAuthoringSaveRequest
        {
            Session = session,
            Entities = BuildEntitySaveList(session),
            WriteNavTiles = false
        });
    }

    private void ClearLoadedNavTilesIfAvailable(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.NavQueryServices) is NavQueryServiceRegistry registry)
        {
            IReadOnlyList<KeyValuePair<NavQueryServiceKey, NavTileStore>> stores = registry.SnapshotStores();
            for (int i = 0; i < stores.Count; i++)
            {
                stores[i].Value.Clear();
            }
        }

        Nav.PendingTiles = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)?.PendingTileCount ?? 0;
        Nav.LastRebuiltTiles = 0;
        Nav.LastFailedTiles = 0;
        Nav.ConfigDirty = true;
        Nav.ConfigStatus = "map-reload-required";
        Nav.ConfigMessage = "map/board authoring changed; reload required before nav bake or path query";
        Nav.LastMessage = Nav.ConfigMessage;
    }

    private void MarkBoardLifecycleSaved(MapAuthoringSaveResult result, string message)
    {
        MapLifecycle.Status = "saved";
        MapLifecycle.ReloadRequired = true;
        MapLifecycle.TargetModId = result.ModId;
        MapLifecycle.MapConfigPath = result.MapConfigPath;
        MapLifecycle.Message = message;
        Save.Status = "saved";
        Save.Message = message;
        Save.MapConfigPath = result.MapConfigPath;
        Save.EntityCount = result.EntityCount;
        Save.NavTileCount = 0;
        LastError = string.Empty;
    }

    private void ApplyNavigationConfigEdit(
        GameEngine engine,
        List<AgentProfileConfig> agentProfiles,
        NavMeshBakeConfig bakeConfig,
        string message)
    {
        NavigationConfigAuthoringWriter.Validate(agentProfiles, bakeConfig);
        var agentRegistry = new AgentProfileRegistry(CloneAgentProfiles(agentProfiles));
        var navProfiles = new NavMeshProfileRegistry(bakeConfig, agentRegistry);
        engine.SetService(CoreServiceKeys.AgentProfiles, agentRegistry);
        engine.SetService(CoreServiceKeys.NavMeshBakeConfig, bakeConfig);
        engine.SetService(CoreServiceKeys.NavMeshProfiles, navProfiles);
        Nav.ConfigDirty = true;
        Nav.ConfigStatus = "edited";
        Nav.ConfigMessage = $"{message}; save and reload required";
        Nav.LastMessage = Nav.ConfigMessage;
        LastError = string.Empty;
    }

    private static List<AgentProfileConfig> CaptureAgentProfiles(GameEngine engine)
    {
        AgentProfileRegistry registry = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("AgentProfiles is missing.");
        var result = new List<AgentProfileConfig>(registry.Count);
        for (int i = 0; i < registry.Count; i++)
        {
            result.Add(CloneAgentProfile(registry[i]));
        }

        return result;
    }

    private static NavMeshBakeConfig CaptureNavMeshBakeConfig(GameEngine engine)
    {
        NavMeshBakeConfig source = engine.GetService(CoreServiceKeys.NavMeshBakeConfig)
            ?? throw new InvalidOperationException("NavMeshBakeConfig is missing.");
        return CloneNavMeshBakeConfig(source);
    }

    private static List<AgentProfileConfig> CloneAgentProfiles(IReadOnlyList<AgentProfileConfig> source)
    {
        var result = new List<AgentProfileConfig>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            result.Add(CloneAgentProfile(source[i]));
        }

        return result;
    }

    private static AgentProfileConfig CloneAgentProfile(AgentProfileConfig source)
    {
        return new AgentProfileConfig
        {
            Id = source.Id,
            RadiusCm = source.RadiusCm,
            HeightCm = source.HeightCm,
            ClearanceCm = source.ClearanceCm,
            DraftCm = source.DraftCm,
            BeamCm = source.BeamCm,
            Mass = source.Mass,
            Layer = source.Layer
        };
    }

    private static NavMeshBakeConfig CloneNavMeshBakeConfig(NavMeshBakeConfig source)
    {
        var profiles = new List<NavMeshAgentProfileConfig>(source.Profiles?.Count ?? 0);
        if (source.Profiles != null)
        {
            for (int i = 0; i < source.Profiles.Count; i++)
            {
                profiles.Add(CloneBakeProfile(source.Profiles[i]));
            }
        }

        var layers = new List<NavLayerConfig>(source.Layers?.Count ?? 0);
        if (source.Layers != null)
        {
            for (int i = 0; i < source.Layers.Count; i++)
            {
                layers.Add(CloneLayer(source.Layers[i]));
            }
        }

        var areas = new List<NavAreaCostConfig>(source.Areas?.Count ?? 0);
        if (source.Areas != null)
        {
            for (int i = 0; i < source.Areas.Count; i++)
            {
                areas.Add(CloneArea(source.Areas[i]));
            }
        }

        return new NavMeshBakeConfig
        {
            Mode = source.Mode,
            Algorithm = source.Algorithm,
            Profiles = profiles,
            Layers = layers,
            Areas = areas,
            RuntimeIncremental = new NavRuntimeIncrementalConfig
            {
                TileBudgetPerFixedTick = source.RuntimeIncremental?.TileBudgetPerFixedTick ?? 1,
                IncludeNeighborTiles = source.RuntimeIncremental?.IncludeNeighborTiles ?? true,
                HeightScaleMeters = source.RuntimeIncremental?.HeightScaleMeters ?? 1f,
                MinWalkableUpDot = source.RuntimeIncremental?.MinWalkableUpDot ?? 0.6f,
                CliffHeightThreshold = source.RuntimeIncremental?.CliffHeightThreshold ?? 1
            }
        };
    }

    private static NavMeshAgentProfileConfig CloneBakeProfile(NavMeshAgentProfileConfig source)
    {
        return new NavMeshAgentProfileConfig
        {
            Id = source.Id,
            MaxClimbCm = source.MaxClimbCm,
            MaxSlopeDeg = source.MaxSlopeDeg
        };
    }

    private static NavLayerConfig CloneLayer(NavLayerConfig source)
    {
        return new NavLayerConfig
        {
            Id = source.Id,
            Layer = source.Layer
        };
    }

    private static NavAreaCostConfig CloneArea(NavAreaCostConfig source)
    {
        return new NavAreaCostConfig
        {
            Id = source.Id,
            AreaId = source.AreaId,
            Cost = source.Cost
        };
    }

    private static string ResolveWritableMapConfigTargetModId(GameEngine engine, MapSession session)
    {
        string mapId = session.MapId.Value;
        var matches = new List<NavConfigMapTarget>(4);
        for (int i = 0; i < engine.ModLoader.LoadedModIds.Count; i++)
        {
            string modId = engine.ModLoader.LoadedModIds[i];
            AddExistingMapTarget(engine, matches, modId, $"assets/Maps/{mapId}.json");
            AddExistingMapTarget(engine, matches, modId, $"assets/maps/{mapId}.json");
        }

        NavConfigMapTarget? explicitMatch = null;
        for (int i = 0; i < matches.Count; i++)
        {
            if (!matches[i].ExplicitSaveTarget)
            {
                continue;
            }

            if (explicitMatch.HasValue)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' has multiple explicit live editor save targets: {explicitMatch.Value.ModId}, {matches[i].ModId}.");
            }

            explicitMatch = matches[i];
        }

        if (explicitMatch.HasValue)
        {
            return explicitMatch.Value.ModId;
        }

        if (matches.Count == 1)
        {
            return matches[0].ModId;
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Map '{mapId}' has multiple writable authoring map fragments with boards: {string.Join(", ", matches.ConvertAll(static m => m.ModId))}.");
        }

        throw new FileNotFoundException(
            $"Map '{mapId}' has no writable authoring map fragment with boards under loaded mod assets/Maps.");
    }

    private static void AddExistingMapTarget(
        GameEngine engine,
        List<NavConfigMapTarget> matches,
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

        JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
        if (root is not JsonObject obj)
        {
            throw new InvalidDataException($"Map config '{path}' must be a JSON object.");
        }

        bool? saveTarget = ReadLiveEditorSaveTarget(obj);
        if (saveTarget == false || !DeclaresAuthoringBoards(obj))
        {
            return;
        }

        matches.Add(new NavConfigMapTarget(modId, path, saveTarget == true));
    }

    private static bool DeclaresAuthoringBoards(JsonObject root)
    {
        foreach (KeyValuePair<string, JsonNode?> kvp in root)
        {
            if (string.Equals(kvp.Key, "boards", StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value is JsonArray boards && boards.Count > 0;
            }
        }

        return false;
    }

    private static bool? ReadLiveEditorSaveTarget(JsonObject root)
    {
        if (!TryGetObjectCaseInsensitive(root, "metadata", out JsonObject? metadata) || metadata == null)
        {
            return null;
        }

        if (!TryGetObjectCaseInsensitive(metadata, "liveMapEditor", out JsonObject? liveMapEditor) ||
            liveMapEditor == null)
        {
            return null;
        }

        foreach (KeyValuePair<string, JsonNode?> kvp in liveMapEditor)
        {
            if (string.Equals(kvp.Key, "saveTarget", StringComparison.OrdinalIgnoreCase) &&
                kvp.Value is JsonValue value &&
                value.TryGetValue(out bool saveTarget))
            {
                return saveTarget;
            }
        }

        return null;
    }

    private static bool TryGetObjectCaseInsensitive(JsonObject root, string name, out JsonObject? value)
    {
        foreach (KeyValuePair<string, JsonNode?> kvp in root)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase) &&
                kvp.Value is JsonObject obj)
            {
                value = obj;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string NormalizeConfigId(string? id, string label)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        string trimmed = id.Trim();
        if (!string.Equals(trimmed, id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label} must not contain leading or trailing whitespace.");
        }

        return trimmed;
    }

    private static string NormalizeNavMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            throw new InvalidOperationException("Nav mode is required.");
        }

        string trimmed = mode.Trim();
        _ = NavBakeNames.ParseMode(trimmed, "NavMeshBakeConfig.mode");
        return trimmed;
    }

    private static string NormalizeNavAlgorithm(string? algorithm)
    {
        if (string.IsNullOrWhiteSpace(algorithm))
        {
            throw new InvalidOperationException("Nav algorithm is required.");
        }

        string trimmed = algorithm.Trim();
        _ = NavBakeNames.ParseAlgorithm(trimmed, "NavMeshBakeConfig.algorithm");
        return trimmed;
    }

    private static int FindAgentProfileIndex(IReadOnlyList<AgentProfileConfig> profiles, string id)
    {
        for (int i = 0; i < profiles.Count; i++)
        {
            if (string.Equals(profiles[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindBakeProfileIndex(IReadOnlyList<NavMeshAgentProfileConfig> profiles, string id)
    {
        for (int i = 0; i < profiles.Count; i++)
        {
            if (string.Equals(profiles[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindLayerIndex(IReadOnlyList<NavLayerConfig> layers, string id)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (string.Equals(layers[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindAreaIndex(IReadOnlyList<NavAreaCostConfig> areas, string id)
    {
        for (int i = 0; i < areas.Count; i++)
        {
            if (string.Equals(areas[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private readonly record struct NavConfigMapTarget(string ModId, string Path, bool ExplicitSaveTarget);

    private void ApplyObstacleOptions(
        string? templateId,
        string? shape,
        int? radiusCm,
        int? halfWidthCm,
        int? halfHeightCm,
        int? navRadiusCm,
        bool? sinkPhysicsCollider,
        bool? sinkNavigationObstacle,
        WorldCmInt2[]? polygonVertices)
    {
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            Obstacle.TemplateId = templateId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(shape))
        {
            Obstacle.Shape = NormalizeObstacleShape(shape);
        }

        if (radiusCm.HasValue) Obstacle.RadiusCm = ValidatePositiveCm(radiusCm.Value, "Obstacle radius");
        if (halfWidthCm.HasValue) Obstacle.HalfWidthCm = ValidatePositiveCm(halfWidthCm.Value, "Obstacle half width");
        if (halfHeightCm.HasValue) Obstacle.HalfHeightCm = ValidatePositiveCm(halfHeightCm.Value, "Obstacle half height");
        if (navRadiusCm.HasValue) Obstacle.NavRadiusCm = ValidatePositiveCm(navRadiusCm.Value, "Obstacle nav radius");
        if (sinkPhysicsCollider.HasValue) Obstacle.SinkPhysicsCollider = sinkPhysicsCollider.Value;
        if (sinkNavigationObstacle.HasValue) Obstacle.SinkNavigationObstacle = sinkNavigationObstacle.Value;
        if (polygonVertices != null)
        {
            ValidateObstaclePolygon(polygonVertices);
            Obstacle.PolygonVertices = polygonVertices;
        }
    }

    private Dictionary<string, JsonNode> BuildObstacleOverrides(WorldCmInt2 world)
    {
        var overrides = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
        {
            ["WorldPositionCm"] = CreateWorldPositionOverride(world),
            ["ManifestationObstacleIntent2D"] = CreateObstacleIntentOverride(),
            ["RuntimeNavMeshStructuralObstacle"] = new JsonObject()
        };

        if (string.Equals(Obstacle.Shape, "polygon", StringComparison.Ordinal))
        {
            overrides["ManifestationObstaclePolygon2D"] = CreateObstaclePolygonOverride();
        }

        return overrides;
    }

    private RuntimeEntitySpawnComponentPatch[] BuildObstacleComponentPatches(Dictionary<string, JsonNode> overrides)
    {
        int count = string.Equals(Obstacle.Shape, "polygon", StringComparison.Ordinal) ? 3 : 2;
        var patches = new RuntimeEntitySpawnComponentPatch[count];
        patches[0] = new RuntimeEntitySpawnComponentPatch(
            "ManifestationObstacleIntent2D",
            overrides["ManifestationObstacleIntent2D"].DeepClone());
        int index = 1;
        if (string.Equals(Obstacle.Shape, "polygon", StringComparison.Ordinal))
        {
            patches[index++] = new RuntimeEntitySpawnComponentPatch(
                "ManifestationObstaclePolygon2D",
                overrides["ManifestationObstaclePolygon2D"].DeepClone());
        }

        patches[index] = new RuntimeEntitySpawnComponentPatch(
            "RuntimeNavMeshStructuralObstacle",
            overrides["RuntimeNavMeshStructuralObstacle"].DeepClone());
        return patches;
    }

    private JsonObject CreateObstacleIntentOverride()
    {
        string shape = Obstacle.Shape switch
        {
            "circle" => "Circle",
            "box" => "Box",
            "polygon" => "Polygon",
            _ => throw new InvalidOperationException($"Unknown obstacle shape '{Obstacle.Shape}'.")
        };
        var obj = new JsonObject
        {
            ["shape"] = shape,
            ["sinkPhysicsCollider"] = Obstacle.SinkPhysicsCollider,
            ["sinkNavigationObstacle"] = Obstacle.SinkNavigationObstacle,
            ["navRadiusCm"] = Obstacle.NavRadiusCm,
            ["localOffsetXCm"] = 0,
            ["localOffsetYCm"] = 0
        };
        if (string.Equals(Obstacle.Shape, "circle", StringComparison.Ordinal))
        {
            obj["radiusCm"] = Obstacle.RadiusCm;
        }
        else if (string.Equals(Obstacle.Shape, "box", StringComparison.Ordinal))
        {
            obj["halfWidthCm"] = Obstacle.HalfWidthCm;
            obj["halfHeightCm"] = Obstacle.HalfHeightCm;
        }

        return obj;
    }

    private JsonObject CreateObstaclePolygonOverride()
    {
        ValidateObstaclePolygon(Obstacle.PolygonVertices);
        var vertices = new JsonArray();
        for (int i = 0; i < Obstacle.PolygonVertices.Length; i++)
        {
            vertices.Add(new JsonObject
            {
                ["x"] = Obstacle.PolygonVertices[i].X,
                ["y"] = Obstacle.PolygonVertices[i].Y
            });
        }

        return new JsonObject { ["vertices"] = vertices };
    }

    private static JsonObject CreateWorldPositionOverride(WorldCmInt2 world)
    {
        return new JsonObject
        {
            ["Value"] = new JsonObject
            {
                ["X"] = world.X,
                ["Y"] = world.Y
            }
        };
    }

    private static string NormalizeObstacleShape(string shape)
    {
        return shape.Trim().ToLowerInvariant() switch
        {
            "circle" => "circle",
            "box" => "box",
            "polygon" => "polygon",
            _ => throw new InvalidOperationException($"Unknown obstacle shape '{shape}'.")
        };
    }

    private static int ValidatePositiveCm(int value, string label)
    {
        if (value <= 0 || value > 100_000)
        {
            throw new InvalidOperationException($"{label} must be between 1 and 100000 cm.");
        }

        return value;
    }

    private static void ValidateObstaclePolygon(WorldCmInt2[] vertices)
    {
        if (vertices.Length < 3 || vertices.Length > ManifestationObstaclePolygon2D.MaxVertices)
        {
            throw new InvalidOperationException(
                $"Obstacle polygon requires 3..{ManifestationObstaclePolygon2D.MaxVertices} vertices.");
        }
    }

    private void MarkObstacleDirty(GameEngine engine, WorldAabbCm dirty)
    {
        MergeDirtyAabb(dirty);
        if (engine.TryGetService(CoreServiceKeys.NavMeshBakeConfig, out NavMeshBakeConfig? navConfig) &&
            navConfig.ParsedMode == NavBakeMode.RuntimeIncremental &&
            navConfig.ParsedAlgorithm == NavBakeAlgorithmKind.Cdt &&
            engine.TryGetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, out RuntimeIncrementalNavMeshRebuildQueue? queue))
        {
            bool includeNeighbors = navConfig.RuntimeIncremental?.IncludeNeighborTiles ?? true;
            queue.EnqueueDirtyAabb(dirty, includeNeighbors);
            Nav.PendingTiles = queue.PendingTileCount;
        }
    }

    private void MarkDirtyIfObstacleOverride(GameEngine engine, Entity entity, string component)
    {
        if (entity == Entity.Null || !engine.World.IsAlive(entity))
        {
            return;
        }

        if (!string.Equals(component, "ManifestationObstacleIntent2D", StringComparison.Ordinal) &&
            !string.Equals(component, "ManifestationObstaclePolygon2D", StringComparison.Ordinal) &&
            !string.Equals(component, "CompoundObstacle2D", StringComparison.Ordinal))
        {
            return;
        }

        if (!engine.World.Has<ManifestationObstacleBridge2DDirty>(entity))
        {
            engine.World.Add(entity, new ManifestationObstacleBridge2DDirty());
        }

        if (engine.World.TryGet(entity, out WorldPositionCm position) &&
            engine.World.TryGet(entity, out ManifestationObstacleIntent2D intent))
        {
            MarkObstacleDirty(engine, ComputeObstacleAabb(engine.World, entity, position.ToWorldCmInt2(), in intent));
        }
    }

    private static WorldAabbCm ComputeObstacleAabb(WorldCmInt2 world, LiveMapEditorObstacleState state)
    {
        if (string.Equals(state.Shape, "circle", StringComparison.Ordinal))
        {
            int diameter = checked(state.RadiusCm * 2);
            return new WorldAabbCm(world.X - state.RadiusCm, world.Y - state.RadiusCm, diameter, diameter);
        }

        if (string.Equals(state.Shape, "box", StringComparison.Ordinal))
        {
            return new WorldAabbCm(
                world.X - state.HalfWidthCm,
                world.Y - state.HalfHeightCm,
                checked(state.HalfWidthCm * 2),
                checked(state.HalfHeightCm * 2));
        }

        return ComputePolygonAabb(world, state.PolygonVertices);
    }

    private static WorldAabbCm ComputeObstacleAabb(
        World world,
        Entity entity,
        WorldCmInt2 center,
        in ManifestationObstacleIntent2D intent)
    {
        return intent.Shape switch
        {
            ManifestationObstacleShape2D.Circle => new WorldAabbCm(
                center.X - intent.RadiusCm,
                center.Y - intent.RadiusCm,
                checked(intent.RadiusCm * 2),
                checked(intent.RadiusCm * 2)),
            ManifestationObstacleShape2D.Box => new WorldAabbCm(
                center.X - intent.HalfWidthCm,
                center.Y - intent.HalfHeightCm,
                checked(intent.HalfWidthCm * 2),
                checked(intent.HalfHeightCm * 2)),
            ManifestationObstacleShape2D.Polygon when world.TryGet(entity, out ManifestationObstaclePolygon2D polygon) =>
                ComputePolygonAabb(center, polygon),
            _ => new WorldAabbCm(center.X - 1, center.Y - 1, 2, 2)
        };
    }

    private static WorldAabbCm ComputePolygonAabb(WorldCmInt2 center, ManifestationObstaclePolygon2D polygon)
    {
        var vertices = new WorldCmInt2[polygon.VertexCount];
        for (int i = 0; i < polygon.VertexCount; i++)
        {
            vertices[i] = polygon.GetVertex(i);
        }

        return ComputePolygonAabb(center, vertices);
    }

    private static WorldAabbCm ComputePolygonAabb(WorldCmInt2 center, IReadOnlyList<WorldCmInt2> vertices)
    {
        int minX = center.X + vertices[0].X;
        int maxX = minX;
        int minY = center.Y + vertices[0].Y;
        int maxY = minY;
        for (int i = 1; i < vertices.Count; i++)
        {
            int x = center.X + vertices[i].X;
            int y = center.Y + vertices[i].Y;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        return new WorldAabbCm(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    private static bool TryHitObstacle(
        World world,
        Entity entity,
        WorldCmInt2 point,
        WorldCmInt2 center,
        in ManifestationObstacleIntent2D intent,
        out long score)
    {
        (float lx, float ly) = ToObstacleLocal(world, entity, point, center);
        score = (long)lx * (long)lx + (long)ly * (long)ly;
        return intent.Shape switch
        {
            ManifestationObstacleShape2D.Circle => score <= (long)intent.RadiusCm * intent.RadiusCm,
            ManifestationObstacleShape2D.Box =>
                Math.Abs(lx) <= intent.HalfWidthCm &&
                Math.Abs(ly) <= intent.HalfHeightCm,
            ManifestationObstacleShape2D.Polygon when world.TryGet(entity, out ManifestationObstaclePolygon2D polygon) =>
                PointInPolygon(lx, ly, polygon),
            _ => false
        };
    }

    private static (float X, float Y) ToObstacleLocal(World world, Entity entity, WorldCmInt2 point, WorldCmInt2 center)
    {
        float dx = point.X - center.X;
        float dy = point.Y - center.Y;
        if (!world.TryGet(entity, out FacingDirection facing) || Math.Abs(facing.AngleRad) <= 0.0001f)
        {
            return (dx, dy);
        }

        float cos = MathF.Cos(-facing.AngleRad);
        float sin = MathF.Sin(-facing.AngleRad);
        return (dx * cos - dy * sin, dx * sin + dy * cos);
    }

    private static bool PointInPolygon(float x, float y, ManifestationObstaclePolygon2D polygon)
    {
        bool inside = false;
        int count = polygon.VertexCount;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            WorldCmInt2 vi = polygon.GetVertex(i);
            WorldCmInt2 vj = polygon.GetVertex(j);
            bool crosses = ((vi.Y > y) != (vj.Y > y)) &&
                x < (float)(vj.X - vi.X) * (y - vi.Y) / (vj.Y - vi.Y) + vi.X;
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static string NormalizeComponentName(string? componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            throw new InvalidOperationException("Component name is required.");
        }

        return componentName.Trim();
    }

    private static void ValidateComponentOverridePayload(World world, string component, JsonNode node)
    {
        Entity probe = world.Create();
        try
        {
            Ludots.Core.Config.ComponentRegistry.Apply(
                probe,
                component,
                node,
                $"LiveMapEditor override validation '{component}'");
        }
        finally
        {
            if (world.IsAlive(probe))
            {
                world.Destroy(probe);
            }
        }
    }

    private static string NormalizeBakeScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new InvalidOperationException("Bake scope is required.");
        }

        return scope.Trim() switch
        {
            "dirty" => "dirty",
            "dirty+n" => "dirtyNeighbors",
            "dirtyNeighbors" => "dirtyNeighbors",
            "full" => "full",
            _ => throw new InvalidOperationException($"Unknown bake scope '{scope}'.")
        };
    }

    private void EnqueueBakeScope(
        RuntimeIncrementalNavMeshRebuildQueue queue,
        LogicTerrainField terrain,
        string scope,
        bool includeNeighbors)
    {
        if (string.Equals(scope, "full", StringComparison.Ordinal))
        {
            for (int y = 0; y < terrain.HeightChunks; y++)
            {
                for (int x = 0; x < terrain.WidthChunks; x++)
                {
                    queue.EnqueueDirtyTile(new NavBakeTileCoord(x, y));
                }
            }

            return;
        }

        if (HasDirtyAabb)
        {
            queue.EnqueueDirtyAabb(
                DirtyAabb,
                includeNeighbors || string.Equals(scope, "dirtyNeighbors", StringComparison.Ordinal));
        }
    }

    private static int CountTilesForAabb(LogicTerrainField terrain, WorldAabbCm dirtyAabb, bool includeNeighbors)
    {
        int tileWidthCm = checked(terrain.ChunkSizeCells * terrain.HorizontalStepCm);
        int tileHeightCm = checked(terrain.ChunkSizeCells * terrain.VerticalStepCm);
        int minChunkX = MathUtil.FloorDiv(dirtyAabb.Left, tileWidthCm);
        int minChunkY = MathUtil.FloorDiv(dirtyAabb.Top, tileHeightCm);
        int maxChunkX = MathUtil.FloorDiv(dirtyAabb.Right - 1, tileWidthCm);
        int maxChunkY = MathUtil.FloorDiv(dirtyAabb.Bottom - 1, tileHeightCm);

        if (includeNeighbors)
        {
            minChunkX--;
            minChunkY--;
            maxChunkX++;
            maxChunkY++;
        }

        minChunkX = MathUtil.Clamp(minChunkX, 0, terrain.WidthChunks - 1);
        maxChunkX = MathUtil.Clamp(maxChunkX, 0, terrain.WidthChunks - 1);
        minChunkY = MathUtil.Clamp(minChunkY, 0, terrain.HeightChunks - 1);
        maxChunkY = MathUtil.Clamp(maxChunkY, 0, terrain.HeightChunks - 1);
        if (minChunkX > maxChunkX || minChunkY > maxChunkY)
        {
            return 0;
        }

        return checked((maxChunkX - minChunkX + 1) * (maxChunkY - minChunkY + 1));
    }

    private void ResolveActiveNavProfile(GameEngine engine)
    {
        NavMeshProfileRegistry? profiles = engine.GetService(CoreServiceKeys.NavMeshProfiles);
        if (profiles == null || profiles.Count == 0)
        {
            Nav.QueryProfileIndex = 0;
            Nav.QueryProfileId = string.Empty;
            return;
        }

        if (!string.IsNullOrWhiteSpace(Nav.QueryProfileId) &&
            profiles.TryGetIndex(Nav.QueryProfileId, out int selectedIndex))
        {
            Nav.QueryProfileIndex = selectedIndex;
            return;
        }

        if (Nav.QueryProfileIndex < 0 || Nav.QueryProfileIndex >= profiles.Count)
        {
            Nav.QueryProfileIndex = 0;
        }

        Nav.QueryProfileId = profiles.GetId(Nav.QueryProfileIndex);
    }

    private LogicTerrainCell ApplyBrush(in LogicTerrainCell current)
    {
        byte height = current.HeightLevel;
        byte waterHeight = current.WaterHeightLevel;
        LogicTerrainSurfaceFlags flags = current.SurfaceFlags;
        byte areaId = current.AreaId;
        float cost = current.Cost;

        switch (Brush.Target)
        {
            case "all":
                if (string.Equals(Brush.Mode, "set", StringComparison.Ordinal))
                {
                    height = Brush.HeightLevel;
                    waterHeight = Brush.Water ? ResolveBrushWaterHeight(height) : (byte)0;
                    flags = Brush.ResolveFlags();
                    areaId = Brush.AreaId;
                    cost = Brush.Cost;
                }
                else
                {
                    height = AdjustHeight(current.HeightLevel);
                    if (current.HasWater || Brush.Water)
                    {
                        waterHeight = AdjustHeight(current.WaterHeightLevel > 0 ? current.WaterHeightLevel : current.HeightLevel);
                        flags |= LogicTerrainSurfaceFlags.Water;
                    }
                }
                break;
            case "height":
                height = string.Equals(Brush.Mode, "set", StringComparison.Ordinal)
                    ? Brush.HeightLevel
                    : AdjustHeight(current.HeightLevel);
                break;
            case "water":
                if (string.Equals(Brush.Mode, "set", StringComparison.Ordinal))
                {
                    waterHeight = Brush.Water ? ResolveBrushWaterHeight(height) : (byte)0;
                }
                else
                {
                    waterHeight = AdjustHeight(current.WaterHeightLevel > 0 ? current.WaterHeightLevel : current.HeightLevel);
                }

                flags = Brush.Water || waterHeight > 0
                    ? flags | LogicTerrainSurfaceFlags.Water
                    : flags & ~LogicTerrainSurfaceFlags.Water;
                break;
            case "area":
                areaId = Brush.AreaId;
                break;
            case "cost":
                cost = Brush.Cost;
                break;
            case "blocked":
                flags = Brush.Blocked
                    ? flags | LogicTerrainSurfaceFlags.Blocked
                    : flags & ~LogicTerrainSurfaceFlags.Blocked;
                break;
            case "ramp":
                flags = Brush.Ramp
                    ? flags | LogicTerrainSurfaceFlags.Ramp
                    : flags & ~LogicTerrainSurfaceFlags.Ramp;
                break;
            default:
                throw new InvalidOperationException($"Unknown brush target '{Brush.Target}'.");
        }

        return new LogicTerrainCell(height, waterHeight, flags, areaId, cost);
    }

    private byte AdjustHeight(byte current)
    {
        int delta = Math.Max(1, (int)Brush.HeightLevel);
        return Brush.Mode switch
        {
            "raise" => (byte)Math.Min(255, current + delta),
            "lower" => (byte)Math.Max(0, current - delta),
            _ => Brush.HeightLevel
        };
    }

    private byte ResolveBrushWaterHeight(byte currentHeight)
        => Brush.WaterHeightLevel > 0 ? Brush.WaterHeightLevel : currentHeight;

    private static byte ValidateByte(int value, string label)
    {
        if (value < 0 || value > 255)
        {
            throw new InvalidOperationException($"{label} must be between 0 and 255.");
        }

        return (byte)value;
    }

    private MutableGridLogicTerrainField EnsureMutableGridTerrain(GameEngine engine)
    {
        LogicTerrainField terrain = engine.LogicTerrain
            ?? throw new InvalidOperationException("Focused map has no LogicTerrainField.");
        if (terrain.Topology != LogicTerrainTopology.Grid)
        {
            throw new InvalidOperationException($"Live terrain paint supports Grid LogicTerrain only, got '{terrain.Topology}'.");
        }

        if (terrain is MutableGridLogicTerrainField mutable)
        {
            return mutable;
        }

        MutableGridLogicTerrainField replacement;
        if (terrain is FlatGridLogicTerrainField flat)
        {
            replacement = new MutableGridLogicTerrainField(
                flat.WidthCells,
                flat.HeightCells,
                flat.CellSizeCm,
                flat.ChunkSizeCells);
            replacement.Fill(flat.GetCell(0, 0));
        }
        else
        {
            replacement = new MutableGridLogicTerrainField(
                terrain.WidthCells,
                terrain.HeightCells,
                terrain.HorizontalStepCm,
                terrain.ChunkSizeCells);
            for (int y = 0; y < terrain.HeightCells; y++)
            {
                for (int x = 0; x < terrain.WidthCells; x++)
                {
                    replacement.SetCell(x, y, terrain.GetCell(x, y));
                }
            }
        }

        engine.ReplaceFocusedLogicTerrain(replacement, reloadNavServices: true);
        return replacement;
    }

    private static bool TryResolveGridCell(GameEngine engine, WorldCmInt2 worldCm, out int col, out int row)
    {
        col = 0;
        row = 0;
        LogicTerrainField? terrain = engine.LogicTerrain;
        if (terrain == null || terrain.Topology != LogicTerrainTopology.Grid)
        {
            return false;
        }

        col = Math.Clamp(Math.DivRem(worldCm.X, terrain.HorizontalStepCm, out _), 0, terrain.WidthCells - 1);
        row = Math.Clamp(Math.DivRem(worldCm.Y, terrain.VerticalStepCm, out _), 0, terrain.HeightCells - 1);
        return true;
    }

    private WorldCmInt2 ResolveCommandWorld(int? xCm, int? yCm)
    {
        if (xCm.HasValue && yCm.HasValue)
        {
            return new WorldCmInt2(xCm.Value, yCm.Value);
        }

        if (!HasPickedWorld)
        {
            throw new InvalidOperationException("Command requires explicit world cm or a picked viewport point.");
        }

        return PickedWorld;
    }

    private int ResolveSpawnReceiptChannel(GameEngine engine)
    {
        if (_spawnReceiptChannelId > 0)
        {
            return _spawnReceiptChannelId;
        }

        RuntimeEntitySpawnReceiptChannelRegistry registry = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
            ?? throw new InvalidOperationException("RuntimeEntitySpawnReceiptChannelRegistry is required for live entity placement.");
        _spawnReceiptChannelId = registry.Register(LiveMapEditorIds.SpawnReceiptChannelKey);
        return _spawnReceiptChannelId;
    }

    private static EntitySpawnData CreateSpawnData(string instanceId, string templateId, WorldCmInt2 world)
    {
        return CreateSpawnData(
            instanceId,
            templateId,
            world,
            new Dictionary<string, JsonNode>(StringComparer.Ordinal)
            {
                ["WorldPositionCm"] = CreateWorldPositionOverride(world)
            });
    }

    private static EntitySpawnData CreateSpawnData(
        string instanceId,
        string templateId,
        WorldCmInt2 world,
        Dictionary<string, JsonNode> overrides)
    {
        return new EntitySpawnData
        {
            InstanceId = instanceId,
            Template = templateId,
            Position = new IntVector2(world.X, world.Y),
            Overrides = overrides
        };
    }

    private IReadOnlyList<EntitySpawnData> BuildEntitySaveList(MapSession session)
    {
        var entities = new List<EntitySpawnData>(session.MapConfig.Entities ?? new List<EntitySpawnData>());
        for (int i = entities.Count - 1; i >= 0; i--)
        {
            EntitySpawnData entry = entities[i];
            if (entry != null && IsAuthoredRemoved(entry.InstanceId))
            {
                entities.RemoveAt(i);
            }
        }

        for (int i = 0; i < _authoredEntities.Count; i++)
        {
            LiveMapEditorAuthoredEntity authored = _authoredEntities[i];
            if (!authored.Removed)
            {
                entities.Add(authored.SpawnData);
            }
        }

        return entities;
    }

    private EntitySpawnData? ResolveSelectedSpawnData(MapSession? session)
    {
        if (string.IsNullOrWhiteSpace(SelectedInstanceId))
        {
            return null;
        }

        for (int i = 0; i < _authoredEntities.Count; i++)
        {
            LiveMapEditorAuthoredEntity authored = _authoredEntities[i];
            if (!authored.Removed &&
                string.Equals(authored.InstanceId, SelectedInstanceId, StringComparison.Ordinal))
            {
                return authored.SpawnData;
            }
        }

        if (session?.MapConfig.Entities == null)
        {
            return null;
        }

        for (int i = 0; i < session.MapConfig.Entities.Count; i++)
        {
            EntitySpawnData entity = session.MapConfig.Entities[i];
            if (string.Equals(entity.InstanceId, SelectedInstanceId, StringComparison.Ordinal))
            {
                return entity;
            }
        }

        return null;
    }

    private void MarkAuthoredRemoved(string instanceId)
    {
        for (int i = 0; i < _authoredEntities.Count; i++)
        {
            if (string.Equals(_authoredEntities[i].InstanceId, instanceId, StringComparison.Ordinal))
            {
                _authoredEntities[i].Removed = true;
            }
        }
    }

    private bool IsAuthoredRemoved(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        for (int i = 0; i < _authoredEntities.Count; i++)
        {
            if (string.Equals(_authoredEntities[i].InstanceId, instanceId, StringComparison.Ordinal) &&
                _authoredEntities[i].Removed)
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveFromMapConfig(MapSession? session, string instanceId)
    {
        if (session?.MapConfig.Entities == null || string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        for (int i = session.MapConfig.Entities.Count - 1; i >= 0; i--)
        {
            if (string.Equals(session.MapConfig.Entities[i].InstanceId, instanceId, StringComparison.Ordinal))
            {
                session.MapConfig.Entities.RemoveAt(i);
            }
        }
    }

    private static string ResolveInstanceId(MapSession? session, Entity entity)
    {
        if (session == null || entity == Entity.Null)
        {
            return string.Empty;
        }

        foreach (KeyValuePair<string, Entity> pair in session.EntityIndex.ByInstanceId)
        {
            if (pair.Value == entity)
            {
                return pair.Key;
            }
        }

        return string.Empty;
    }

    private void MergeDirtyAabb(WorldAabbCm dirty)
    {
        if (!HasDirtyAabb)
        {
            DirtyAabb = dirty;
            HasDirtyAabb = true;
            return;
        }

        int left = Math.Min(DirtyAabb.Left, dirty.Left);
        int top = Math.Min(DirtyAabb.Top, dirty.Top);
        int right = Math.Max(DirtyAabb.Right, dirty.Right);
        int bottom = Math.Max(DirtyAabb.Bottom, dirty.Bottom);
        DirtyAabb = new WorldAabbCm(left, top, right - left, bottom - top);
    }

    private static WorldAabbCm CellsToAabb(LogicTerrainField terrain, int minCol, int minRow, int maxCol, int maxRow)
    {
        int x = minCol * terrain.HorizontalStepCm;
        int y = minRow * terrain.VerticalStepCm;
        int w = (maxCol - minCol + 1) * terrain.HorizontalStepCm;
        int h = (maxRow - minRow + 1) * terrain.VerticalStepCm;
        return new WorldAabbCm(x, y, w, h);
    }

    private static WebUiCommandResult Fail(string code, string message)
        => WebUiCommandResult.Fail(code, message);
}
