using System.Text.Json;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Map.Board;
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
                    boards = CaptureBoards(session.AllBoards)
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
                _runtime.Brush.HeightLevel,
                _runtime.Brush.AreaId,
                _runtime.Brush.Cost,
                _runtime.Brush.Blocked,
                _runtime.Brush.Water,
                _runtime.Brush.Ramp
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
                authoredCount = _runtime.AuthoredEntities.Count,
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
                message = _runtime.Nav.LastMessage
            },
            sim = new
            {
                hasStart = _runtime.Nav.HasStart,
                startXcm = _runtime.Nav.Start.X,
                startYcm = _runtime.Nav.Start.Y,
                hasGoal = _runtime.Nav.HasGoal,
                goalXcm = _runtime.Nav.Goal.X,
                goalYcm = _runtime.Nav.Goal.Y,
                status = _runtime.Nav.PathStatus.ToString(),
                pointCount = _runtime.Nav.PathXcm.Length,
                elapsedUs = _runtime.Nav.LastQueryElapsedMicroseconds,
                path = CapturePath()
            },
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
        return new
        {
            entityId = _runtime.SelectedEntity.Id,
            generation = _runtime.SelectedEntity.Version,
            stableId,
            name,
            instanceId = _runtime.SelectedInstanceId
        };
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
