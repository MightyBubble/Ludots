using System.Numerics;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;

namespace BrowserMinimapCompositedOverlayShowcaseMod;

internal sealed class BrowserMinimapCompositedOverlayFocusCommandHandler : IWebUiCommandHandler
{
	private readonly GameEngine _engine;
	private readonly BrowserMinimapCompositedOverlayLayoutState _layoutState;

	public BrowserMinimapCompositedOverlayFocusCommandHandler(
		GameEngine engine,
		BrowserMinimapCompositedOverlayLayoutState layoutState)
	{
		_engine = engine ?? throw new ArgumentNullException(nameof(engine));
		_layoutState = layoutState ?? throw new ArgumentNullException(nameof(layoutState));
	}

	public ValueTask<WebUiCommandResult> HandleAsync(
		WebUiCommandRequest request,
		CancellationToken cancellationToken = default)
	{
		if (!BrowserMinimapCompositedOverlayCommandPayload.TryReadFocusPoint(
			request.Payload,
			out float normalizedX,
			out float normalizedY,
			out string error))
		{
			return ValueTask.FromResult(WebUiCommandResult.Fail("invalid_payload", error));
		}

		if (_engine.GetService(CoreServiceKeys.MinimapRuntime) is not MinimapRuntime runtime)
		{
			return ValueTask.FromResult(WebUiCommandResult.Fail(
				"minimap_runtime_missing",
				"MinimapRuntime is required to focus the camera from the Web minimap shell."));
		}

		if (!_layoutState.TryGetRect(out BrowserMinimapCompositedOverlayRect rect))
		{
			return ValueTask.FromResult(WebUiCommandResult.Fail(
				"minimap_viewport_missing",
				"The Web minimap viewport has not published its native field rect yet."));
		}

		var screenPosition = new Vector2(
			rect.X + (normalizedX * rect.Width),
			rect.Y + (normalizedY * rect.Height));
		if (!runtime.TryScreenToWorldClamped(screenPosition, out Vector2 worldCm))
		{
			return ValueTask.FromResult(WebUiCommandResult.Fail(
				"minimap_world_projection_unavailable",
				"The minimap could not resolve the clicked point to a world position."));
		}

		runtime.JumpCameraTo(_engine, worldCm);
		return ValueTask.FromResult(WebUiCommandResult.Ok());
	}
}

internal sealed class BrowserMinimapCompositedOverlayPermissionValidator : IWebUiCommandPermissionValidator
{
	public bool CanUse(WebUiCommandRequest request, out string error)
	{
		if (!string.Equals(
			request.Name,
			BrowserMinimapCompositedOverlayPanelKitIds.FocusMinimapCommand,
			StringComparison.Ordinal))
		{
			error = $"Command '{request.Name}' is not registered for the minimap shell.";
			return false;
		}

		if (request.EntityRefs is { Length: > 0 })
		{
			error = "Minimap focus commands must not carry entity references.";
			return false;
		}

		if (!BrowserMinimapCompositedOverlayCommandPayload.TryReadFocusPoint(
			request.Payload,
			out _,
			out _,
			out error))
		{
			return false;
		}

		error = string.Empty;
		return true;
	}
}

internal sealed class BrowserMinimapCompositedOverlayGenerationResolver : IWebUiEntityGenerationResolver
{
	public bool IsCurrent(WebUiEntityRef entityRef)
	{
		return false;
	}
}

internal static class BrowserMinimapCompositedOverlayCommandPayload
{
	public static bool TryReadFocusPoint(
		JsonElement payload,
		out float normalizedX,
		out float normalizedY,
		out string error)
	{
		normalizedX = 0f;
		normalizedY = 0f;
		error = string.Empty;
		if (payload.ValueKind != JsonValueKind.Object)
		{
			error = "Minimap focus payload must be an object.";
			return false;
		}

		if (payload.TryGetProperty("panelId", out JsonElement panelElement) &&
			panelElement.ValueKind == JsonValueKind.String &&
			!string.Equals(
				panelElement.GetString(),
				BrowserMinimapCompositedOverlayPanelKitIds.PanelId,
				StringComparison.Ordinal))
		{
			error = $"Minimap focus payload targets unknown panel '{panelElement.GetString()}'.";
			return false;
		}

		if (!TryGetNormalizedSingle(payload, "normalizedX", out normalizedX, out error) ||
			!TryGetNormalizedSingle(payload, "normalizedY", out normalizedY, out error))
		{
			return false;
		}

		return true;
	}

	private static bool TryGetNormalizedSingle(
		JsonElement payload,
		string propertyName,
		out float value,
		out string error)
	{
		value = 0f;
		error = string.Empty;
		if (!payload.TryGetProperty(propertyName, out JsonElement property) ||
			property.ValueKind != JsonValueKind.Number ||
			!property.TryGetSingle(out value) ||
			!float.IsFinite(value))
		{
			error = $"Minimap focus payload requires finite numeric '{propertyName}'.";
			return false;
		}

		if (value < 0f || value > 1f)
		{
			error = $"Minimap focus payload '{propertyName}' must be between 0 and 1.";
			return false;
		}

		return true;
	}
}
