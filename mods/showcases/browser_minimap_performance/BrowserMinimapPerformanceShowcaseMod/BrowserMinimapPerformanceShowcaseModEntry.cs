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

namespace BrowserMinimapPerformanceShowcaseMod;

public sealed class BrowserMinimapPerformanceShowcaseModEntry : IMod
{
	private const string BrowserServiceKey = "BrowserRuntime";
	private const string Topic = "webui.minimapRawMarkers";
	private const int SharedBufferCapacityBytes = 8 * 1024 * 1024;
	private const string PublishHzEnvironmentKey = "LUDOTS_BROWSER_MINIMAP_PERFORMANCE_HZ";

	private IBrowserSurface? _surface;
	private BrowserSurfaceCanvasContent? _browserContent;
	private WebUiDataPlaneRuntime? _dataPlaneRuntime;
	private BrowserMinimapPerformanceProjectionSystem? _projectionSystem;
	private BrowserMinimapPerformanceHudSuppressionSystem? _hudSuppressionSystem;
	private IUiSurfaceHost? _surfaceHost;
	private UiSurfaceLeaseHandle _lease;

	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[BrowserMinimapPerformanceShowcaseMod] Loaded.");
		context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
	}

	public void OnUnload()
	{
		_projectionSystem?.Dispose();
		_projectionSystem = null;
		_hudSuppressionSystem?.Dispose();
		_hudSuppressionSystem = null;
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
			"BrowserMinimapPerformance.Showcase",
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

		await _surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Create("/", "route=raw-performance"))).ConfigureAwait(false);
	}

	private void SetupDataPlane(GameEngine engine, IBrowserSurface surface)
	{
		if (surface is not IBrowserSharedBufferSurface sharedBufferSurface)
		{
			throw new InvalidOperationException(
				"BrowserMinimapPerformanceShowcaseMod requires a browser surface with shared-buffer support.");
		}

		MinimapMarkerBuffer markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
			?? throw new InvalidOperationException("MinimapMarkerBuffer service is required for raw minimap performance showcase.");
		var producer = new BrowserMinimapPerformanceRawMarkerTopicProducer(Topic, markerBuffer);

		var store = new BrowserSharedMemoryBufferStore(sharedBufferSurface.SharedBuffers);
		var transport = new BrowserSharedMemoryDataTransport(
			surface.Messages,
			store,
			new[]
			{
				new BrowserSharedMemoryTopicBuffer(
					Topic,
					"browser-minimap-performance.raw-markers.0",
					BrowserMinimapPerformanceRawMarkerTopicProducer.RawSchemaId,
					SharedBufferCapacityBytes)
			});

		_dataPlaneRuntime = new WebUiDataPlaneRuntime();
		_dataPlaneRuntime.RegisterTopic(producer);
		_dataPlaneRuntime.AttachSession("browser-minimap-performance", transport);

		var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime);
		pump.TrackTopic(Topic);
		_projectionSystem = new BrowserMinimapPerformanceProjectionSystem(pump, ResolvePublishHz());
		_hudSuppressionSystem = new BrowserMinimapPerformanceHudSuppressionSystem(engine);
		engine.RegisterPresentationSystem(_projectionSystem);
		engine.RegisterPresentationSystem(_hudSuppressionSystem);
	}

	private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
	{
		return Ui.Canvas(browserContent)
			.Id("browser-minimap-performance-surface")
			.WidthPercent(100f)
			.HeightPercent(100f)
			.Absolute(0f, 0f)
			.ZIndex(24);
	}

	private static UiElementBuilder BuildMissingRuntimeRoot()
	{
		return Ui.Column(
				Ui.Text("Browser runtime missing").FontSize(28f).Bold(),
				Ui.Text("Run browser_minimap_performance_cef_raylib so CEF can consume the raw minimap packet."))
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
		const string indexAssetPath = "BrowserMinimapPerformanceShowcaseMod:Assets/minimap-single-app/index.html";
		if (engine.VFS != null &&
			engine.VFS.TryResolveFullPath(indexAssetPath, out string indexPath))
		{
			string? root = Path.GetDirectoryName(indexPath);
			if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
			{
				return root;
			}
		}

		throw new DirectoryNotFoundException($"Raw minimap performance app assets were not found: {indexAssetPath}");
	}

	private static float ResolvePublishHz()
	{
		string? raw = Environment.GetEnvironmentVariable(PublishHzEnvironmentKey);
		return float.TryParse(raw, out float value)
			? Math.Clamp(value, 1f, 60f)
			: 30f;
	}
}
