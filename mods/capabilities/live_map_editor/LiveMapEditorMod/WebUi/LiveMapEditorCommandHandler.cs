using System.Text.Json;
using System.Globalization;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.WebUI.DataPlane;
using LiveMapEditorMod.Runtime;

namespace LiveMapEditorMod.WebUi;

internal sealed class LiveMapEditorCommandHandler : IWebUiCommandHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GameEngine _engine;
    private readonly LiveMapEditorRuntime _runtime;

    public LiveMapEditorCommandHandler(GameEngine engine, LiveMapEditorRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(
        WebUiCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        WebUiCommandResult result;
        try
        {
            result = request.Name switch
            {
                "setTool" => SetTool(request.Payload),
                "setBrush" => SetBrush(request.Payload),
                "paintTerrain" => PaintTerrain(request.Payload),
                "bucketFillWater" => BucketFillWater(request.Payload),
                "placeEntity" => PlaceEntity(request.Payload),
                "selectEntity" => SelectEntity(request.Payload),
                "removeEntity" => RemoveEntity(request),
                "setObstacle" => SetObstacle(request.Payload),
                "placeObstacle" => PlaceObstacle(request.Payload),
                "eraseObstacle" => EraseObstacle(request.Payload),
                "setEntityOverride" => SetEntityOverride(request.Payload),
                "deleteEntityOverride" => DeleteEntityOverride(request.Payload),
                "navConfigReload" => _runtime.ReloadNavConfig(_engine),
                "navConfigSave" => _runtime.SaveNavConfig(_engine),
                "navAddProfile" => NavAddProfile(request.Payload),
                "navDeleteProfile" => NavDeleteProfile(request.Payload),
                "navAddBakeProfile" => NavAddBakeProfile(request.Payload),
                "navDeleteBakeProfile" => NavDeleteBakeProfile(request.Payload),
                "navAddLayer" => NavAddLayer(request.Payload),
                "navDeleteLayer" => NavDeleteLayer(request.Payload),
                "navAddArea" => NavAddArea(request.Payload),
                "navDeleteArea" => NavDeleteArea(request.Payload),
                "navSetMode" => _runtime.SetNavMode(_engine, ReadString(request.Payload, "mode")),
                "navSetAlgorithm" => _runtime.SetNavAlgorithm(_engine, ReadString(request.Payload, "algorithm")),
                "navSetRuntimeField" => NavSetRuntimeField(request.Payload),
                "setBakeOptions" => SetBakeOptions(request.Payload),
                "estimateNavBake" => EstimateNavBake(request.Payload),
                "rebakeNav" => RebuildNav(request.Payload),
                "rebakeDirty" => RebuildDirtyNav(request.Payload),
                "clearNavTiles" => _runtime.ClearNavTiles(_engine),
                "setPathOptions" => SetPathOptions(request.Payload),
                "queryPath" => QueryPath(request.Payload),
                "setViewToggle" => SetViewToggle(request.Payload),
                "cameraPanTo" => CameraPanTo(request.Payload),
                "previewBoardAllocation" => PreviewBoardAllocation(request.Payload),
                "createMap" => CreateMap(request.Payload),
                "addBoard" => AddBoard(request.Payload),
                "deleteBoard" => DeleteBoard(request.Payload),
                "updateBoard" => UpdateBoard(request.Payload),
                "selectBoard" => SelectBoard(request.Payload),
                "reloadMap" => _runtime.ReloadCurrentMap(_engine),
                "transportSetMode" => TransportSetMode(request.Payload),
                "transportSetRoot" => TransportSetRoot(request.Payload),
                "transportAddNode" => TransportAddNode(request.Payload),
                "transportSelectNode" => TransportSelectNode(request.Payload),
                "transportMoveNode" => TransportMoveNode(request.Payload),
                "transportUpdateNode" => TransportUpdateNode(request.Payload),
                "transportDeleteNode" => _runtime.Transport.DeleteSelectedNode(_engine),
                "transportBeginSegment" => _runtime.Transport.BeginSegment(_engine),
                "transportAppendSegmentPoint" => TransportAppendSegmentPoint(request.Payload),
                "transportUndoSegmentPoint" => _runtime.Transport.RemoveLastSegmentPoint(_engine),
                "transportCommitSegment" => TransportCommitSegment(request.Payload),
                "transportSelectSegment" => TransportSelectSegment(request.Payload),
                "transportUpdateSegment" => TransportUpdateSegment(request.Payload),
                "transportInsertSegmentPoint" => TransportInsertSegmentPoint(request.Payload),
                "transportMoveSegmentPoint" => TransportMoveSegmentPoint(request.Payload),
                "transportDeleteSegmentPoint" => TransportDeleteSegmentPoint(request.Payload),
                "transportDeleteSegment" => _runtime.Transport.DeleteSelectedSegment(_engine),
                "transportRebake" => _runtime.Transport.Rebuild(_engine),
                "transportSetRouteAgent" => TransportSetRouteAgent(request.Payload),
                "transportQueryRoute" => TransportQueryRoute(request.Payload),
                "transportSave" => _runtime.Transport.Save(_engine),
                "saveMap" => _runtime.SaveMap(_engine),
                _ => WebUiCommandResult.Fail("unknown_command", $"Unknown LiveMapEditor command '{request.Name}'.")
            };
        }
        catch (Exception ex)
        {
            result = WebUiCommandResult.Fail("invalid_payload", ex.Message);
        }

        return ValueTask.FromResult(result);
    }

    private WebUiCommandResult SetTool(JsonElement payload)
    {
        string? tool = ReadString(payload, "tool");
        try
        {
            _runtime.SetTool(tool ?? string.Empty);
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            return WebUiCommandResult.Fail("set_tool_failed", ex.Message);
        }
    }

    private WebUiCommandResult SetBrush(JsonElement payload)
    {
        try
        {
            _runtime.SetBrush(
                ReadInt(payload, "radiusCells"),
                ReadString(payload, "mode"),
                ReadString(payload, "target"),
                ReadInt(payload, "heightLevel"),
                ReadInt(payload, "waterHeightLevel"),
                ReadInt(payload, "areaId"),
                ReadFloat(payload, "cost"),
                ReadBool(payload, "blocked"),
                ReadBool(payload, "water"),
                ReadBool(payload, "ramp"));
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            return WebUiCommandResult.Fail("set_brush_failed", ex.Message);
        }
    }

    private WebUiCommandResult PaintTerrain(JsonElement payload)
    {
        return _runtime.PaintTerrain(
            _engine,
            ReadInt(payload, "col"),
            ReadInt(payload, "row"),
            ReadInt(payload, "radiusCells"));
    }

    private WebUiCommandResult BucketFillWater(JsonElement payload)
    {
        return _runtime.BucketFillWater(
            _engine,
            ReadInt(payload, "col"),
            ReadInt(payload, "row"),
            ReadInt(payload, "waterHeightLevel"));
    }

    private WebUiCommandResult PlaceEntity(JsonElement payload)
    {
        string? templateId = ReadString(payload, "template");
        return _runtime.PlaceEntity(
            _engine,
            templateId ?? string.Empty,
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"));
    }

    private WebUiCommandResult SelectEntity(JsonElement payload)
    {
        return _runtime.SelectNearestEntity(
            _engine,
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"),
            ReadInt(payload, "radiusCm") ?? 150);
    }

    private WebUiCommandResult RemoveEntity(WebUiCommandRequest request)
    {
        if (request.EntityRefs == null || request.EntityRefs.Length == 0)
        {
            return WebUiCommandResult.Fail(
                "entity_ref_required",
                "removeEntity requires a current stable entity ref.");
        }

        if (_runtime.SelectedEntity == Arch.Core.Entity.Null ||
            !_engine.World.IsAlive(_runtime.SelectedEntity) ||
            !_engine.World.TryGet(_runtime.SelectedEntity, out PresentationStableId stableId) ||
            stableId.Value != request.EntityRefs[0].StableId)
        {
            return WebUiCommandResult.Fail(
                "selection_ref_mismatch",
                "removeEntity ref does not match the current selected entity.");
        }

        return _runtime.RemoveSelectedEntity(_engine);
    }

    private WebUiCommandResult SetObstacle(JsonElement payload)
    {
        return _runtime.SetObstacleOptions(
            ReadString(payload, "template"),
            ReadString(payload, "shape"),
            ReadInt(payload, "radiusCm"),
            ReadInt(payload, "halfWidthCm"),
            ReadInt(payload, "halfHeightCm"),
            ReadInt(payload, "navRadiusCm"),
            ReadBool(payload, "sinkPhysicsCollider"),
            ReadBool(payload, "sinkNavigationObstacle"),
            ReadObstacleVertices(payload));
    }

    private WebUiCommandResult PlaceObstacle(JsonElement payload)
    {
        return _runtime.PlaceObstacle(
            _engine,
            ReadString(payload, "template"),
            ReadString(payload, "shape"),
            ReadInt(payload, "radiusCm"),
            ReadInt(payload, "halfWidthCm"),
            ReadInt(payload, "halfHeightCm"),
            ReadInt(payload, "navRadiusCm"),
            ReadBool(payload, "sinkPhysicsCollider"),
            ReadBool(payload, "sinkNavigationObstacle"),
            ReadObstacleVertices(payload),
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"));
    }

    private WebUiCommandResult EraseObstacle(JsonElement payload)
    {
        return _runtime.EraseObstacleAt(
            _engine,
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"));
    }

    private WebUiCommandResult SetEntityOverride(JsonElement payload)
    {
        return _runtime.SetSelectedEntityOverride(
            _engine,
            ReadString(payload, "component"),
            ReadRawJson(payload, "json"));
    }

    private WebUiCommandResult DeleteEntityOverride(JsonElement payload)
    {
        return _runtime.DeleteSelectedEntityOverride(
            _engine,
            ReadString(payload, "component"));
    }

    private WebUiCommandResult NavAddProfile(JsonElement payload)
    {
        return _runtime.UpsertAgentProfile(
            _engine,
            ReadString(payload, "id"),
            ReadFloat(payload, "radiusCm"),
            ReadFloat(payload, "heightCm"),
            ReadFloat(payload, "clearanceCm"),
            ReadFloat(payload, "draftCm"),
            ReadFloat(payload, "beamCm"),
            ReadFloat(payload, "mass"),
            ReadInt(payload, "layer"));
    }

    private WebUiCommandResult NavDeleteProfile(JsonElement payload)
        => _runtime.DeleteAgentProfile(_engine, ReadString(payload, "id"));

    private WebUiCommandResult NavAddBakeProfile(JsonElement payload)
    {
        return _runtime.UpsertBakeProfile(
            _engine,
            ReadString(payload, "id"),
            ReadInt(payload, "maxClimbCm"),
            ReadFloat(payload, "maxSlopeDeg"));
    }

    private WebUiCommandResult NavDeleteBakeProfile(JsonElement payload)
        => _runtime.DeleteBakeProfile(_engine, ReadString(payload, "id"));

    private WebUiCommandResult NavAddLayer(JsonElement payload)
    {
        return _runtime.UpsertNavLayer(
            _engine,
            ReadString(payload, "id"),
            ReadInt(payload, "layer"));
    }

    private WebUiCommandResult NavDeleteLayer(JsonElement payload)
        => _runtime.DeleteNavLayer(_engine, ReadString(payload, "id"));

    private WebUiCommandResult NavAddArea(JsonElement payload)
    {
        return _runtime.UpsertNavArea(
            _engine,
            ReadString(payload, "id"),
            ReadInt(payload, "areaId"),
            ReadFloat(payload, "cost"));
    }

    private WebUiCommandResult NavDeleteArea(JsonElement payload)
        => _runtime.DeleteNavArea(_engine, ReadString(payload, "id"));

    private WebUiCommandResult NavSetRuntimeField(JsonElement payload)
    {
        return _runtime.SetNavRuntimeField(
            _engine,
            ReadString(payload, "field"),
            ReadFloat(payload, "value"),
            ReadBool(payload, "enabled"));
    }

    private WebUiCommandResult RebuildDirtyNav(JsonElement payload)
    {
        return _runtime.RebuildDirtyNav(_engine, ReadInt(payload, "maxTiles") ?? 16);
    }

    private WebUiCommandResult SetBakeOptions(JsonElement payload)
    {
        return _runtime.SetBakeOptions(
            ReadString(payload, "scope"),
            ReadInt(payload, "maxTiles"),
            ReadBool(payload, "includeNeighbors"),
            ReadBool(payload, "parallel"));
    }

    private WebUiCommandResult EstimateNavBake(JsonElement payload)
    {
        return _runtime.EstimateNavBake(
            _engine,
            ReadString(payload, "scope"),
            ReadBool(payload, "includeNeighbors"));
    }

    private WebUiCommandResult RebuildNav(JsonElement payload)
    {
        return _runtime.RebuildNav(
            _engine,
            ReadString(payload, "scope"),
            ReadInt(payload, "maxTiles"),
            ReadBool(payload, "includeNeighbors"),
            ReadBool(payload, "parallel"));
    }

    private WebUiCommandResult SetPathOptions(JsonElement payload)
    {
        return _runtime.SetPathOptions(
            _engine,
            ReadString(payload, "profileId"),
            ReadInt(payload, "layer"),
            ReadInt(payload, "maxPortals"));
    }

    private WebUiCommandResult QueryPath(JsonElement payload)
    {
        return _runtime.QueryPath(
            _engine,
            ReadInt(payload, "startXcm"),
            ReadInt(payload, "startYcm"),
            ReadInt(payload, "goalXcm"),
            ReadInt(payload, "goalYcm"));
    }

    private WebUiCommandResult SetViewToggle(JsonElement payload)
    {
        return _runtime.SetViewToggle(
            ReadString(payload, "name"),
            ReadBool(payload, "enabled"));
    }

    private WebUiCommandResult CameraPanTo(JsonElement payload)
    {
        return _runtime.PanCameraTo(
            _engine,
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"));
    }

    private WebUiCommandResult PreviewBoardAllocation(JsonElement payload)
    {
        return _runtime.PreviewBoardAllocation(
            ReadString(payload, "slot"),
            ReadFloat(payload, "widthMeters"),
            ReadFloat(payload, "heightMeters"),
            ReadInt(payload, "cellSizeCm"));
    }

    private WebUiCommandResult CreateMap(JsonElement payload)
    {
        return _runtime.CreateMap(
            _engine,
            ReadString(payload, "mapId"),
            ReadBoardName(payload),
            ReadString(payload, "topology"),
            ReadFloat(payload, "widthMeters"),
            ReadFloat(payload, "heightMeters"),
            ReadInt(payload, "cellSizeCm"),
            ReadInt(payload, "hexEdgeLengthCm"),
            ReadBool(payload, "navigationEnabled"),
            ReadBool(payload, "loadAfterCreate"));
    }

    private WebUiCommandResult AddBoard(JsonElement payload)
    {
        return _runtime.AddBoard(
            _engine,
            ReadBoardName(payload),
            ReadString(payload, "topology"),
            ReadFloat(payload, "widthMeters"),
            ReadFloat(payload, "heightMeters"),
            ReadInt(payload, "cellSizeCm"),
            ReadInt(payload, "hexEdgeLengthCm"),
            ReadBool(payload, "navigationEnabled"));
    }

    private WebUiCommandResult DeleteBoard(JsonElement payload)
        => _runtime.DeleteBoard(_engine, ReadBoardName(payload));

    private WebUiCommandResult UpdateBoard(JsonElement payload)
    {
        return _runtime.UpdateBoardSettings(
            _engine,
            ReadBoardName(payload),
            ReadInt(payload, "cellSizeCm"),
            ReadInt(payload, "hexEdgeLengthCm"),
            ReadBool(payload, "navigationEnabled"));
    }

    private WebUiCommandResult SelectBoard(JsonElement payload)
        => _runtime.SelectBoard(_engine, ReadBoardName(payload));

    private WebUiCommandResult TransportSetMode(JsonElement payload)
    {
        try
        {
            _runtime.Transport.SetMode(ReadString(payload, "mode") ?? string.Empty);
            return WebUiCommandResult.Ok();
        }
        catch (Exception ex)
        {
            return WebUiCommandResult.Fail("transport_set_mode_failed", ex.Message);
        }
    }

    private WebUiCommandResult TransportSetRoot(JsonElement payload)
    {
        return _runtime.Transport.SetRoot(
            _engine,
            ReadInt(payload, "sampleStepCm"),
            ReadFloat(payload, "defaultVisualWidthMeters"));
    }

    private WebUiCommandResult TransportAddNode(JsonElement payload)
    {
        return _runtime.Transport.AddNode(
            _engine,
            ReadString(payload, "id"),
            ReadString(payload, "kind"),
            ReadString(payload, "tags"),
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"),
            _runtime.HasPickedWorld,
            _runtime.PickedWorld);
    }

    private WebUiCommandResult TransportSelectNode(JsonElement payload)
    {
        return _runtime.Transport.SelectNearestNode(
            _engine,
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"),
            _runtime.HasPickedWorld,
            _runtime.PickedWorld);
    }

    private WebUiCommandResult TransportMoveNode(JsonElement payload)
    {
        return _runtime.Transport.MoveSelectedNode(
            _engine,
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"),
            _runtime.HasPickedWorld,
            _runtime.PickedWorld);
    }

    private WebUiCommandResult TransportUpdateNode(JsonElement payload)
    {
        return _runtime.Transport.UpdateSelectedNode(
            _engine,
            ReadString(payload, "id"),
            ReadString(payload, "kind"),
            ReadString(payload, "tags"));
    }

    private WebUiCommandResult TransportAppendSegmentPoint(JsonElement payload)
    {
        return _runtime.Transport.AppendSegmentPoint(
            _engine,
            ReadBool(payload, "snapToNode") ?? false,
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"),
            _runtime.HasPickedWorld,
            _runtime.PickedWorld);
    }

    private WebUiCommandResult TransportCommitSegment(JsonElement payload)
    {
        return _runtime.Transport.CommitSegment(
            _engine,
            ReadString(payload, "id"),
            ReadString(payload, "areaId"),
            ReadString(payload, "tags"),
            ReadString(payload, "direction"),
            ReadString(payload, "flowDirection"),
            ReadInt(payload, "depthCm"),
            ReadInt(payload, "widthCm"),
            ReadInt(payload, "laneCount"),
            ReadFloat(payload, "visualWidthMeters"),
            ReadInt(payload, "sampleStepCm"));
    }

    private WebUiCommandResult TransportSelectSegment(JsonElement payload)
    {
        return _runtime.Transport.SelectNearestSegment(
            _engine,
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"),
            _runtime.HasPickedWorld,
            _runtime.PickedWorld);
    }

    private WebUiCommandResult TransportUpdateSegment(JsonElement payload)
    {
        return _runtime.Transport.UpdateSelectedSegment(
            _engine,
            ReadString(payload, "areaId"),
            ReadString(payload, "tags"),
            ReadString(payload, "direction"),
            ReadString(payload, "flowDirection"),
            ReadInt(payload, "depthCm"),
            ReadInt(payload, "widthCm"),
            ReadInt(payload, "laneCount"),
            ReadFloat(payload, "visualWidthMeters"),
            ReadInt(payload, "sampleStepCm"));
    }

    private WebUiCommandResult TransportInsertSegmentPoint(JsonElement payload)
    {
        return _runtime.Transport.InsertSegmentPoint(
            _engine,
            ReadInt(payload, "pointIndex"),
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"),
            _runtime.HasPickedWorld,
            _runtime.PickedWorld);
    }

    private WebUiCommandResult TransportMoveSegmentPoint(JsonElement payload)
    {
        return _runtime.Transport.MoveSelectedSegmentPoint(
            _engine,
            ReadInt(payload, "pointIndex"),
            ReadInt(payload, "xCm"),
            ReadInt(payload, "yCm"),
            _runtime.HasPickedWorld,
            _runtime.PickedWorld);
    }

    private WebUiCommandResult TransportDeleteSegmentPoint(JsonElement payload)
    {
        return _runtime.Transport.DeleteSelectedSegmentPoint(
            _engine,
            ReadInt(payload, "pointIndex"));
    }

    private WebUiCommandResult TransportSetRouteAgent(JsonElement payload)
    {
        return _runtime.Transport.SetRouteAgent(
            _engine,
            ReadString(payload, "agentTypeId"));
    }

    private WebUiCommandResult TransportQueryRoute(JsonElement payload)
    {
        return _runtime.Transport.QueryRoute(
            _engine,
            ReadString(payload, "agentTypeId"),
            ReadInt(payload, "startXcm"),
            ReadInt(payload, "startYcm"),
            ReadInt(payload, "goalXcm"),
            ReadInt(payload, "goalYcm"),
            _runtime.HasPickedWorld,
            _runtime.PickedWorld);
    }

    private static string? ReadString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static string? ReadBoardName(JsonElement payload)
        => ReadString(payload, "boardName") ?? ReadString(payload, "name");

    private static int? ReadInt(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed))
        {
            return parsed;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Command payload field '{name}' must be an integer.");
    }

    private static float? ReadFloat(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float parsed))
        {
            return parsed;
        }

        if (value.ValueKind == JsonValueKind.String &&
            float.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Command payload field '{name}' must be a number.");
    }

    private static bool? ReadBool(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        if (value.ValueKind == JsonValueKind.String &&
            bool.TryParse(value.GetString(), out bool parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Command payload field '{name}' must be a boolean.");
    }

    private static string? ReadRawJson(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static WorldCmInt2[]? ReadObstacleVertices(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (payload.TryGetProperty("vertices", out JsonElement vertices) &&
            vertices.ValueKind == JsonValueKind.Array)
        {
            var result = new List<WorldCmInt2>(vertices.GetArrayLength());
            foreach (JsonElement vertex in vertices.EnumerateArray())
            {
                result.Add(new WorldCmInt2(
                    ReadRequiredInt(vertex, "x", "Obstacle polygon vertex.x"),
                    ReadRequiredInt(vertex, "y", "Obstacle polygon vertex.y")));
            }

            return result.Count == 0 ? null : result.ToArray();
        }

        string? text = ReadString(payload, "polygon");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] tokens = text.Split(
            new[] { '\r', '\n', ';', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = new List<WorldCmInt2>(tokens.Length);
        for (int i = 0; i < tokens.Length; i++)
        {
            string[] pair = tokens[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pair.Length != 2 ||
                !int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                throw new InvalidOperationException("Obstacle polygon must use 'x,y x,y x,y' local-cm vertices.");
            }

            parsed.Add(new WorldCmInt2(x, y));
        }

        return parsed.ToArray();
    }

    private static int ReadRequiredInt(JsonElement payload, string name, string label)
    {
        int? value = ReadInt(payload, name);
        return value ?? throw new InvalidOperationException($"{label} is required.");
    }
}
