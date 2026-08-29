using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;

namespace ActivityDispatchShowcaseMod;

internal sealed class ActivityDispatchTriggerCommandHandler : IWebUiCommandHandler
{
    private readonly GameEngine _engine;

    public ActivityDispatchTriggerCommandHandler(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(
        WebUiCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadEventKey(request.Payload, out string eventKey))
        {
            return ValueTask.FromResult(WebUiCommandResult.Fail(
                "invalid_payload",
                "Trigger payload requires 'eventKey' of a declared ActivityShowcase custom event."));
        }

        var registry = _engine.GetService(CoreServiceKeys.CustomEventNameRegistry);
        if (registry == null || _engine.CurrentMapSession == null)
        {
            return ValueTask.FromResult(WebUiCommandResult.Fail(
                "map_session_missing",
                "The showcase map session is not running."));
        }

        var context = _engine.CreateContext();
        context.Set(CoreServiceKeys.MapId, _engine.CurrentMapSession.MapId);
        context.Set(CoreServiceKeys.MapSession, _engine.CurrentMapSession);
        _engine.TriggerManager.FireMapCustomEvent(
            _engine.CurrentMapSession.MapId,
            eventKey,
            context,
            registry);
        return ValueTask.FromResult(WebUiCommandResult.Ok());
    }

    private static bool TryReadEventKey(JsonElement payload, out string eventKey)
    {
        eventKey = string.Empty;
        return payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("eventKey", out JsonElement keyElement) &&
            keyElement.ValueKind == JsonValueKind.String &&
            ActivityDispatchShowcaseIds.TriggerEvents.Contains(keyElement.GetString()) &&
            (eventKey = keyElement.GetString()!) is not null;
    }
}

internal sealed class ActivityDispatchConfirmCommandHandler : IWebUiCommandHandler
{
    private readonly GameEngine _engine;

    public ActivityDispatchConfirmCommandHandler(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(
        WebUiCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadSelection(request.Payload, out int instanceId, out string optionId))
        {
            return ValueTask.FromResult(WebUiCommandResult.Fail(
                "invalid_payload",
                "Confirm payload requires numeric 'instanceId' and non-empty 'optionId'."));
        }

        if (_engine.GetService(CoreServiceKeys.ActivityRuntimeService) is not ActivityRuntimeService activities)
        {
            return ValueTask.FromResult(WebUiCommandResult.Fail(
                "activity_runtime_missing",
                "ActivityRuntimeService is not running."));
        }

        foreach (ActivityView view in activities.CaptureViews())
        {
            if (view.InstanceId != instanceId)
            {
                continue;
            }

            try
            {
                activities.ResolveOption(view.Entity, optionId);
                return ValueTask.FromResult(WebUiCommandResult.Ok());
            }
            catch (InvalidOperationException ex)
            {
                return ValueTask.FromResult(WebUiCommandResult.Fail("resolve_rejected", ex.Message));
            }
        }

        return ValueTask.FromResult(WebUiCommandResult.Fail(
            "unknown_instance",
            $"No activity instance {instanceId} exists."));
    }

    private static bool TryReadSelection(JsonElement payload, out int instanceId, out string optionId)
    {
        instanceId = 0;
        optionId = string.Empty;
        return payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("instanceId", out JsonElement instanceElement) &&
            instanceElement.ValueKind == JsonValueKind.Number &&
            instanceElement.TryGetInt32(out instanceId) &&
            payload.TryGetProperty("optionId", out JsonElement optionElement) &&
            optionElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(optionElement.GetString()) &&
            (optionId = optionElement.GetString()!) is not null;
    }
}

/// <summary>
/// Runtime knob for the ablation demo: adjusts the scope host's GAS attribute so the
/// Execution Condition of the "forward camp" option visibly flips between blocked and
/// executable while the forced activity is on screen.
/// </summary>
internal sealed class ActivityDispatchSetAttributeCommandHandler : IWebUiCommandHandler
{
    private readonly GameEngine _engine;

    public ActivityDispatchSetAttributeCommandHandler(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(
        WebUiCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadAttribute(request.Payload, out string attributeKey, out float value))
        {
            return ValueTask.FromResult(WebUiCommandResult.Fail(
                "invalid_payload",
                "setAttribute payload requires 'attributeKey' and finite numeric 'value'."));
        }

        if (_engine.GetService(CoreServiceKeys.ActivityRuntimeService) is not ActivityRuntimeService activities)
        {
            return ValueTask.FromResult(WebUiCommandResult.Fail(
                "activity_runtime_missing",
                "ActivityRuntimeService is not running."));
        }

        int attributeId = AttributeRegistry.GetId(attributeKey);
        if (attributeId < 0)
        {
            return ValueTask.FromResult(WebUiCommandResult.Fail(
                "unknown_attribute",
                $"Attribute '{attributeKey}' is not registered."));
        }

        foreach (ActivityView view in activities.CaptureViews())
        {
            if (view.State != ActivityInstanceState.Active)
            {
                continue;
            }

            if (!_engine.World.IsAlive(view.ScopeHost) ||
                !_engine.World.TryGet<AttributeBuffer>(view.ScopeHost, out AttributeBuffer buffer))
            {
                continue;
            }

            buffer.SetCurrent(attributeId, value);
            return ValueTask.FromResult(WebUiCommandResult.Ok());
        }

        return ValueTask.FromResult(WebUiCommandResult.Fail(
            "no_active_activity",
            "No active activity scope host is available; trigger one first."));
    }

    private static bool TryReadAttribute(JsonElement payload, out string attributeKey, out float value)
    {
        attributeKey = string.Empty;
        value = 0f;
        return payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("attributeKey", out JsonElement keyElement) &&
            keyElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(keyElement.GetString()) &&
            (attributeKey = keyElement.GetString()!) is not null &&
            payload.TryGetProperty("value", out JsonElement valueElement) &&
            valueElement.ValueKind == JsonValueKind.Number &&
            valueElement.TryGetSingle(out value) &&
            float.IsFinite(value);
    }
}

internal sealed class ActivityDispatchPermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        ActivityDispatchShowcaseIds.ConfirmCommand,
        ActivityDispatchShowcaseIds.TriggerCommand,
        ActivityDispatchShowcaseIds.SetAttributeCommand,
    };

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (!AllowedCommands.Contains(request.Name))
        {
            error = $"Command '{request.Name}' is not registered for the activity dispatch showcase.";
            return false;
        }

        if (request.EntityRefs is { Length: > 0 })
        {
            error = "Activity dispatch commands carry their selection in the payload, not entity references.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

internal sealed class ActivityDispatchGenerationResolver : IWebUiEntityGenerationResolver
{
    public bool IsCurrent(WebUiEntityRef entityRef) => false;
}
