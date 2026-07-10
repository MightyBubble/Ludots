using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;

namespace BrowserMinimapCompositedOverlayShowcaseMod;

public sealed class BrowserMinimapCompositedOverlayShowcaseModEntry : IMod
{
	private const string AssetIndexPath = "BrowserMinimapCompositedOverlayShowcaseMod:Assets/overlay-app/index.html";
	private const int BrowserCanvasSize = 288;
	private const int BrowserCanvasMargin = 24;

	private IBrowserSurface? _surface;
	private BrowserMinimapCompositedOverlayBrowserCanvasContent? _browserContent;
	private readonly BrowserMinimapCompositedOverlayLayoutState _layoutState = new();
	private BrowserMinimapCompositedOverlayNativeMarkerBridgeSystem? _nativeMarkerBridgeSystem;
	private IUiSurfaceHost? _surfaceHost;
	private UiSurfaceLeaseHandle _lease;

	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[BrowserMinimapCompositedOverlayShowcaseMod] Loaded.");
		context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
	}

	public void OnUnload()
	{
		_nativeMarkerBridgeSystem?.Dispose();
		_nativeMarkerBridgeSystem = null;
		if (_surface != null)
		{
			_surface.Messages.MessageReceived -= OnBrowserMessageReceived;
		}

		if (_lease.IsValid && _surfaceHost != null)
		{
			_surfaceHost.ReleaseLease(ref _lease);
		}

		_surfaceHost = null;
		_browserContent?.Dispose();
		_browserContent = null;
		_surface?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_surface = null;
	}

	private async Task OnGameStartAsync(ScriptContext context)
	{
		GameEngine engine = context.Get(CoreServiceKeys.Engine)
			?? throw new InvalidOperationException("GameEngine service is missing from ScriptContext.");
		IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
			?? throw new InvalidOperationException("UiSurfaceHost service is missing from ScriptContext.");
		UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
			?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");
		_surfaceHost = surfaceHost;
		_lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
			"BrowserMinimapCompositedOverlay.Showcase",
			UiSurfaceSegment.Main,
			priority: 12,
			exclusive: true));

		_nativeMarkerBridgeSystem = new BrowserMinimapCompositedOverlayNativeMarkerBridgeSystem(engine, _layoutState);
		engine.InsertPresentationSystemBefore<MinimapPresentationSystem>(_nativeMarkerBridgeSystem);

		if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
		{
			surfaceHost.Publish(_lease, UiSurfaceContribution.FromBuilder(BuildMissingRuntimeRoot));
			return;
		}

		string assetRoot = ResolveAssetRoot(engine);
		var resolver = new BrowserAppResourceResolver(assetRoot);
		int screenWidth = Math.Max(BrowserCanvasSize + (BrowserCanvasMargin * 2), (int)MathF.Ceiling(root.Width));
		int screenHeight = Math.Max(BrowserCanvasSize + (BrowserCanvasMargin * 2), (int)MathF.Ceiling(root.Height));
		_layoutState.ConfigureCanvas(
			Math.Max(BrowserCanvasMargin, screenWidth - BrowserCanvasSize - BrowserCanvasMargin),
			BrowserCanvasMargin,
			BrowserCanvasSize,
			BrowserCanvasSize,
			screenWidth,
			screenHeight);
		var viewport = new BrowserViewport(BrowserCanvasSize, BrowserCanvasSize);

		_surface = await runtime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
		_surface.Messages.MessageReceived += OnBrowserMessageReceived;
		_browserContent = new BrowserMinimapCompositedOverlayBrowserCanvasContent(
			_surface,
			_layoutState,
			hitTestOptions: BrowserSurfaceHitTestOptions.Bounds);
		surfaceHost.Publish(
			_lease,
			UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(_browserContent)));

		await _surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Create("/", "route=composited-overlay"))).ConfigureAwait(false);
	}

	private UiElementBuilder BuildBrowserRoot(BrowserMinimapCompositedOverlayBrowserCanvasContent browserContent)
	{
		return Ui.Canvas(browserContent)
			.Id("browser-minimap-composited-overlay-surface")
			.WidthPercent(100f)
			.HeightPercent(100f)
			.Absolute(0f, 0f)
			.ZIndex(24);
	}

	private static UiElementBuilder BuildMissingRuntimeRoot()
	{
		return Ui.Column(
				Ui.Text("Browser runtime missing").FontSize(28f).Bold(),
				Ui.Text("Run browser_minimap_composited_overlay_cef_raylib so the host can mount the transparent browser overlay."))
			.WidthPercent(100f)
			.HeightPercent(100f)
			.Padding(32f)
			.Gap(12f);
	}

	private static bool TryGetBrowserRuntime(ScriptContext context, out IBrowserRuntime runtime)
	{
		var key = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
		if (context.TryGet(key, out runtime))
		{
			return true;
		}

		if (context.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) &&
			engine != null &&
			engine.TryGetService(key, out runtime))
		{
			context.Set(key, runtime);
			return true;
		}

		runtime = null!;
		return false;
	}

	private static string ResolveAssetRoot(GameEngine engine)
	{
		if (engine.VFS != null &&
			engine.VFS.TryResolveFullPath(AssetIndexPath, out string indexPath))
		{
			string? root = Path.GetDirectoryName(indexPath);
			if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
			{
				return root;
			}
		}

		throw new DirectoryNotFoundException($"Composited overlay browser app assets were not found: {AssetIndexPath}");
	}

	private void OnBrowserMessageReceived(object? sender, BrowserScriptMessage message)
	{
		if (!TryParseMinimapRectMessage(
			message.Payload,
			out float x,
			out float y,
			out float width,
			out float height,
			out float coordinateSpaceWidth,
			out float coordinateSpaceHeight,
			out PresentationClipShapeKind clipKind,
			out long sequence,
			out float dragDeltaX,
			out float dragDeltaY))
		{
			return;
		}

		_layoutState.ApplyViewportMessage(
			x,
			y,
			width,
			height,
			coordinateSpaceWidth,
			coordinateSpaceHeight,
			clipKind,
			sequence,
			dragDeltaX,
			dragDeltaY);
	}

	private static bool TryParseMinimapRectMessage(
		string payload,
		out float x,
		out float y,
		out float width,
		out float height,
		out float coordinateSpaceWidth,
		out float coordinateSpaceHeight,
		out PresentationClipShapeKind clipKind,
		out long sequence,
		out float dragDeltaX,
		out float dragDeltaY)
	{
		x = 0;
		y = 0;
		width = 0;
		height = 0;
		coordinateSpaceWidth = BrowserCanvasSize;
		coordinateSpaceHeight = BrowserCanvasSize;
		clipKind = PresentationClipShapeKind.None;
		sequence = 0L;
		dragDeltaX = 0;
		dragDeltaY = 0;
		if (string.IsNullOrWhiteSpace(payload))
		{
			return false;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(payload);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("type", out JsonElement typeElement) ||
				!string.Equals(typeElement.GetString(), "ludots.minimapOverlay.rect", StringComparison.Ordinal) ||
				!root.TryGetProperty("rect", out JsonElement rectElement) ||
				rectElement.ValueKind != JsonValueKind.Object)
			{
				return false;
			}

			if (!TryGetFiniteSingle(rectElement, "x", out float xf) ||
				!TryGetFiniteSingle(rectElement, "y", out float yf) ||
				!TryGetFiniteSingle(rectElement, "width", out float widthf) ||
				!TryGetFiniteSingle(rectElement, "height", out float heightf))
			{
				return false;
			}

			if (root.TryGetProperty("sequence", out JsonElement sequenceElement) &&
				sequenceElement.ValueKind == JsonValueKind.Number &&
				sequenceElement.TryGetInt64(out long parsedSequence))
			{
				sequence = parsedSequence;
			}

			clipKind = TryGetClipKind(root, out PresentationClipShapeKind parsedClipKind)
				? parsedClipKind
				: PresentationClipShapeKind.None;
			if (root.TryGetProperty("coordinateSpace", out JsonElement coordinateSpaceElement) &&
				coordinateSpaceElement.ValueKind == JsonValueKind.Object)
			{
				if (TryGetFiniteSingle(coordinateSpaceElement, "width", out float coordinateSpaceWidthf) &&
					coordinateSpaceWidthf > 0f)
				{
					coordinateSpaceWidth = Math.Clamp(coordinateSpaceWidthf, 1f, 4096f);
				}

				if (TryGetFiniteSingle(coordinateSpaceElement, "height", out float coordinateSpaceHeightf) &&
					coordinateSpaceHeightf > 0f)
				{
					coordinateSpaceHeight = Math.Clamp(coordinateSpaceHeightf, 1f, 4096f);
				}
			}

			if (root.TryGetProperty("dragDelta", out JsonElement dragDeltaElement) &&
				dragDeltaElement.ValueKind == JsonValueKind.Object)
			{
				if (TryGetFiniteSingle(dragDeltaElement, "x", out float dragDeltaXf))
				{
					dragDeltaX = Math.Clamp(dragDeltaXf, -4096f, 4096f);
				}

				if (TryGetFiniteSingle(dragDeltaElement, "y", out float dragDeltaYf))
				{
					dragDeltaY = Math.Clamp(dragDeltaYf, -4096f, 4096f);
				}
			}

			x = Math.Clamp(xf, -4096f, 4096f);
			y = Math.Clamp(yf, -4096f, 4096f);
			width = Math.Clamp(widthf, 1f, 4096f);
			height = Math.Clamp(heightf, 1f, 4096f);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool TryGetFiniteSingle(JsonElement element, string propertyName, out float value)
	{
		value = 0f;
		if (!element.TryGetProperty(propertyName, out JsonElement property) ||
			property.ValueKind != JsonValueKind.Number ||
			!property.TryGetSingle(out value))
		{
			return false;
		}

		return float.IsFinite(value);
	}

	private static bool TryGetClipKind(JsonElement root, out PresentationClipShapeKind clipKind)
	{
		clipKind = PresentationClipShapeKind.None;
		if (!root.TryGetProperty("clip", out JsonElement clipElement) ||
			clipElement.ValueKind != JsonValueKind.Object ||
			!clipElement.TryGetProperty("kind", out JsonElement kindElement))
		{
			return false;
		}

		return kindElement.GetString()?.Trim().ToLowerInvariant() switch
		{
			"rect" => SetClipKind(PresentationClipShapeKind.Rect, out clipKind),
			"circle" => SetClipKind(PresentationClipShapeKind.Circle, out clipKind),
			"diamond" => SetClipKind(PresentationClipShapeKind.Diamond, out clipKind),
			"none" => SetClipKind(PresentationClipShapeKind.None, out clipKind),
			_ => false,
		};
	}

	private static bool SetClipKind(PresentationClipShapeKind value, out PresentationClipShapeKind target)
	{
		target = value;
		return true;
	}
}
