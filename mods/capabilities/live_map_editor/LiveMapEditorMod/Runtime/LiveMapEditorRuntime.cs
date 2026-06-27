using System.Diagnostics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Map.Authoring;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;

namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorRuntime : IDisposable
{
    private static readonly QueryDescription MapEntityQuery = new QueryDescription().WithAll<MapEntity>();
    private static readonly QueryDescription MapEntityPositionQuery = new QueryDescription().WithAll<MapEntity, WorldPositionCm>();

    private readonly Dictionary<int, LiveMapEditorAuthoredEntity> _pendingByReceiptId = new();
    private readonly List<LiveMapEditorAuthoredEntity> _authoredEntities = new();
    private int _nextInstanceOrdinal = 1;
    private int _nextReceiptId = 1;
    private int _spawnReceiptChannelId;

    public bool PanelOpen { get; set; }
    public string Tool { get; private set; } = "inspect";
    public LiveMapEditorBrushState Brush { get; } = new();
    public LiveMapEditorNavState Nav { get; } = new();
    public LiveMapEditorSaveState Save { get; } = new();
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
            "sim" => "sim",
            "nav" => "nav",
            _ => throw new InvalidOperationException($"Unknown live editor tool '{tool}'.")
        };
    }

    public void SetBrush(
        int? radiusCells,
        int? heightLevel,
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

        if (heightLevel.HasValue)
        {
            if (heightLevel.Value < 0 || heightLevel.Value > 255)
            {
                throw new InvalidOperationException("Brush height level must be between 0 and 255.");
            }

            Brush.HeightLevel = (byte)heightLevel.Value;
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

            var cell = new LogicTerrainCell(
                Brush.HeightLevel,
                Brush.Water ? Brush.HeightLevel : (byte)0,
                Brush.ResolveFlags(),
                Brush.AreaId,
                Brush.Cost);

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

                    mutable.SetCell(x, y, cell);
                }
            }

            TerrainRevision++;
            WorldAabbCm dirty = CellsToAabb(mutable, minCol, minRow, maxCol, maxRow);
            MergeDirtyAabb(dirty);
            engine.RefreshFocusedLogicTerrainVisualHeightmap(TerrainRevision);
            if (navQueue != null)
            {
                Nav.PendingTiles = navQueue.EnqueueDirtyAabb(dirty, includeNeighborTiles);
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

    public WebUiCommandResult RebuildDirtyNav(GameEngine engine, int maxTiles)
    {
        try
        {
            NavMeshBakeConfig config = engine.GetService(CoreServiceKeys.NavMeshBakeConfig)
                ?? throw new InvalidOperationException("NavMeshBakeConfig is missing.");
            if (config.ParsedMode != NavBakeMode.RuntimeIncremental || config.ParsedAlgorithm != NavBakeAlgorithmKind.Cdt)
            {
                return Fail("nav_mode_unsupported", "Live editor runtime rebake requires navmesh mode runtime-incremental + cdt.");
            }

            RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
                ?? throw new InvalidOperationException("RuntimeIncrementalNavMeshRebuildQueue is missing.");
            RuntimeNavMeshRebuildBatch batch = queue.ProcessBudget(Math.Clamp(maxTiles, 1, 512));
            Nav.LastRebuiltTiles = batch.RebuiltTileCount;
            Nav.LastFailedTiles = batch.FailedEntryCount;
            Nav.PendingTiles = batch.PendingTileCount;
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

    public WebUiCommandResult QueryPath(GameEngine engine, int? startXcm, int? startYcm, int? goalXcm, int? goalYcm)
    {
        try
        {
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
            if (!registry.TryCreateQuery(0, 0, NavAreaCostTable.CreateDefault(), out NavQueryService service))
            {
                return Fail("nav_query_missing", "No nav query service is registered for layer 0 profile 0.");
            }

            long before = Stopwatch.GetTimestamp();
            NavPathResult result = service.TryFindPath(Nav.Start.X, Nav.Start.Y, Nav.Goal.X, Nav.Goal.Y);
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
            Save.Status = "saved";
            Save.Message = $"saved {result.EntityCount} entities, {result.NavTileCount} nav tiles";
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

    public IReadOnlyList<LiveMapEditorAuthoredEntity> AuthoredEntities => _authoredEntities;

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
        return new EntitySpawnData
        {
            InstanceId = instanceId,
            Template = templateId,
            Position = new IntVector2(world.X, world.Y),
            Overrides = new Dictionary<string, JsonNode>
            {
                ["WorldPositionCm"] = new JsonObject
                {
                    ["Value"] = new JsonObject
                    {
                        ["X"] = world.X,
                        ["Y"] = world.Y
                    }
                }
            }
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
