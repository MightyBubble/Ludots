using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;

namespace BrowserMinimapCompactBufferShowcaseMod;

public sealed class BrowserMinimapCompactBufferShowcaseModEntry : IMod
{
	private const string BrowserServiceKey = "BrowserRuntime";
	private const string Topic = "webui.minimapMarkers";
	private const int SharedBufferCapacityBytes = 4 * 1024 * 1024;
	private const string PublishHzEnvironmentKey = "LUDOTS_BROWSER_MINIMAP_COMPACT_HZ";

	private IBrowserSurface? _surface;
	private BrowserSurfaceCanvasContent? _browserContent;
	private WebUiDataPlaneRuntime? _dataPlaneRuntime;
	private BrowserMinimapCompactBufferProjectionSystem? _projectionSystem;
	private BrowserMinimapCompactBufferKnowledgeProjectionSystem? _knowledgeProjectionSystem;
	private IUiSurfaceHost? _surfaceHost;
	private UiSurfaceLeaseHandle _lease;

	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[BrowserMinimapCompactBufferShowcaseMod] Loaded.");
		context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
	}

	public void OnUnload()
	{
		_projectionSystem?.Dispose();
		_projectionSystem = null;
		_knowledgeProjectionSystem?.Dispose();
		_knowledgeProjectionSystem = null;
		if (_dataPlaneRuntime != null)
		{
			_dataPlaneRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
			_dataPlaneRuntime = null;
		}

		_browserContent?.Dispose();
		_browserContent = null;
		if (_lease.IsValid && _surfaceHost != null)
		{
			_surfaceHost.ReleaseLease(ref _lease);
		}

		_surfaceHost = null;
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
			"BrowserMinimapCompactBuffer.Showcase",
			UiSurfaceSegment.Main,
			priority: 12,
			exclusive: true));

		if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
		{
			surfaceHost.Publish(_lease, UiSurfaceContribution.FromBuilder(BuildMissingRuntimeRoot));
			return;
		}

		string assetRoot = ResolveAssetRoot(engine);
		var resolver = new BrowserAppResourceResolver(assetRoot);
		var viewport = new BrowserViewport(
			Math.Max(1280, (int)MathF.Ceiling(root.Width)),
			Math.Max(720, (int)MathF.Ceiling(root.Height)));

		_surface = await runtime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
		SetupDataPlane(engine, _surface);

		_browserContent = new BrowserSurfaceCanvasContent(
			_surface,
			hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
		surfaceHost.Publish(
			_lease,
			UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(_browserContent)));

		await _surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Create("/", "route=compact-buffer"))).ConfigureAwait(false);
	}

	private void SetupDataPlane(GameEngine engine, IBrowserSurface surface)
	{
		if (surface is not IBrowserSharedBufferSurface sharedBufferSurface)
		{
			throw new InvalidOperationException(
				"BrowserMinimapCompactBufferShowcaseMod requires a browser surface with shared-buffer support.");
		}

		MinimapMarkerBuffer markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
			?? throw new InvalidOperationException("MinimapMarkerBuffer service is required for compact minimap buffer showcase.");
		var producer = new MinimapMarkerWebUiTopicProducer(
			Topic,
			markerBuffer,
			WebUiColumnarPacketSchemaRegistry.MinimapMarkersSchemaId);

		var store = new BrowserSharedMemoryBufferStore(sharedBufferSurface.SharedBuffers);
		var transport = new BrowserSharedMemoryDataTransport(
			surface.Messages,
			store,
			new[]
			{
				new BrowserSharedMemoryTopicBuffer(
					Topic,
					"browser-minimap-compact-buffer.markers.0",
					WebUiColumnarPacketSchemaRegistry.MinimapMarkersSchemaId,
					SharedBufferCapacityBytes)
			});

		_dataPlaneRuntime = new WebUiDataPlaneRuntime();
		_dataPlaneRuntime.RegisterTopic(producer);
		_dataPlaneRuntime.AttachSession("browser-minimap-compact-buffer", transport);

		var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime);
		pump.TrackTopic(Topic);
		_projectionSystem = new BrowserMinimapCompactBufferProjectionSystem(pump, ResolvePublishHz());
		_knowledgeProjectionSystem = new BrowserMinimapCompactBufferKnowledgeProjectionSystem(engine);
		engine.RegisterPresentationSystem(_projectionSystem);
		engine.RegisterPresentationSystem(_knowledgeProjectionSystem);
	}

	private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
	{
		return Ui.Canvas(browserContent)
			.Id("browser-minimap-compact-buffer-surface")
			.WidthPercent(100f)
			.HeightPercent(100f)
			.Absolute(0f, 0f)
			.ZIndex(24);
	}

	private static UiElementBuilder BuildMissingRuntimeRoot()
	{
		return Ui.Column(
				Ui.Text("Browser runtime missing").FontSize(28f).Bold(),
				Ui.Text("Run browser_minimap_compact_buffer_cef_raylib so CEF can consume the compact minimap buffer."))
			.WidthPercent(100f)
			.HeightPercent(100f)
			.Padding(32f)
			.Gap(12f);
	}

	private static bool TryGetBrowserRuntime(ScriptContext context, out IBrowserRuntime runtime)
	{
		var key = new ServiceKey<IBrowserRuntime>(BrowserServiceKey);
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
		const string indexAssetPath = "BrowserMinimapCompactBufferShowcaseMod:Assets/minimap-single-app/index.html";
		if (engine.VFS != null &&
			engine.VFS.TryResolveFullPath(indexAssetPath, out string indexPath))
		{
			string? root = Path.GetDirectoryName(indexPath);
			if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
			{
				return root;
			}
		}

		throw new DirectoryNotFoundException($"Compact minimap browser app assets were not found: {indexAssetPath}");
	}

	private static float ResolvePublishHz()
	{
		string? raw = Environment.GetEnvironmentVariable(PublishHzEnvironmentKey);
		return float.TryParse(raw, out float value)
			? Math.Clamp(value, 1f, 60f)
			: 30f;
	}
}
