using System.Text.Json;
using System.Globalization;
using Ludots.Core.Engine;
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
                "placeEntity" => PlaceEntity(request.Payload),
                "selectEntity" => SelectEntity(request.Payload),
                "removeEntity" => RemoveEntity(request),
                "rebakeDirty" => RebuildDirtyNav(request.Payload),
                "queryPath" => QueryPath(request.Payload),
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
                ReadInt(payload, "heightLevel"),
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

    private WebUiCommandResult RebuildDirtyNav(JsonElement payload)
    {
        return _runtime.RebuildDirtyNav(_engine, ReadInt(payload, "maxTiles") ?? 16);
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
}
