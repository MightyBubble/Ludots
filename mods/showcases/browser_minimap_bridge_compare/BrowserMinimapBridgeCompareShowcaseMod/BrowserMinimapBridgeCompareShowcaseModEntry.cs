using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;

namespace BrowserMinimapBridgeCompareShowcaseMod;

public abstract class BrowserMinimapBridgeShowcaseModEntry : IMod
{
	private const string BrowserServiceKey = "BrowserRuntime";
	private const int SharedBufferCapacityBytes = 16 * 1024 * 1024;
	private const string MarkerCountEnvironmentKey = "LUDOTS_MINIMAP_BRIDGE_MARKERS";
	private const string PublishHzEnvironmentKey = "LUDOTS_MINIMAP_BRIDGE_HZ";

	private readonly string _logName;
	private readonly string _surfaceLeaseId;
	private readonly string _assetIndexPath;
	private readonly string _modeQuery;
	private IBrowserSurface? _surface;
	private BrowserSurfaceCanvasContent? _browserContent;
	private WebUiDataPlaneRuntime? _dataPlaneRuntime;
	private BrowserMinimapBridgeCompareDataPlaneSystem? _dataPlaneSystem;
	private IUiSurfaceHost? _surfaceHost;
	private UiSurfaceLeaseHandle _lease;

	protected BrowserMinimapBridgeShowcaseModEntry(
		string logName,
		string surfaceLeaseId,
		string assetIndexPath,
		string modeQuery)
	{
		_logName = string.IsNullOrWhiteSpace(logName) ? throw new ArgumentException("Log name is required.", nameof(logName)) : logName;
		_surfaceLeaseId = string.IsNullOrWhiteSpace(surfaceLeaseId) ? throw new ArgumentException("Surface lease id is required.", nameof(surfaceLeaseId)) : surfaceLeaseId;
		_assetIndexPath = string.IsNullOrWhiteSpace(assetIndexPath) ? throw new ArgumentException("Asset index path is required.", nameof(assetIndexPath)) : assetIndexPath;
		_modeQuery = string.IsNullOrWhiteSpace(modeQuery) ? throw new ArgumentException("Mode query is required.", nameof(modeQuery)) : modeQuery;
	}

	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log($"[{_logName}] Loaded.");
		context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
	}

	public void OnUnload()
	{
		_dataPlaneSystem?.Dispose();
		_dataPlaneSystem = null;
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
		IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
			?? throw new InvalidOperationException("UiSurfaceHost service is missing from ScriptContext.");
		_surfaceHost = surfaceHost;
		_lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
			_surfaceLeaseId,
			UiSurfaceSegment.Main,
			priority: 10,
			exclusive: true));
		UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
			?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");
		GameEngine engine = context.Get(CoreServiceKeys.Engine)
			?? throw new InvalidOperationException("GameEngine service is missing from ScriptContext.");

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
		BrowserSurfaceCanvasContent browserContent = _browserContent;
		surfaceHost.Publish(
			_lease,
			UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(browserContent)));

		Uri navigationUri = BrowserLocalAppUri.Create("/", $"mode={Uri.EscapeDataString(_modeQuery)}");
		await _surface.NavigateAsync(new BrowserNavigationRequest(navigationUri)).ConfigureAwait(false);
	}

	private void SetupDataPlane(GameEngine engine, IBrowserSurface surface)
	{
		if (surface is not IBrowserSharedBufferSurface sharedBufferSurface)
		{
			throw new InvalidOperationException(
				"BrowserMinimapBridgeCompareShowcaseMod requires a browser surface with shared-buffer support.");
		}

		int markerCount = ResolveMarkerCount();
		float publishHz = ResolvePublishHz();
		var markerWorld = new BrowserMinimapBridgeCompareMarkerWorld(markerCount);
		var compactTopic = new BrowserMinimapBridgeCompactMarkerTopicProducer(markerWorld);

		var store = new BrowserSharedMemoryBufferStore(sharedBufferSurface.SharedBuffers);
		var transport = new BrowserSharedMemoryDataTransport(
			surface.Messages,
			store,
			new[]
			{
				new BrowserSharedMemoryTopicBuffer(
					BrowserMinimapBridgeCompareTopics.CompactMarkers,
					$"browser-minimap-bridge.{_modeQuery}.markers.0",
					WebUiColumnarPacketSchemaRegistry.MinimapMarkersSchemaId,
					SharedBufferCapacityBytes)
			});

		_dataPlaneRuntime = new WebUiDataPlaneRuntime();
		_dataPlaneRuntime.RegisterTopic(compactTopic);
		_dataPlaneRuntime.AttachSession($"browser-minimap-bridge-{_modeQuery}", transport);

		var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime);
		pump.TrackTopic(BrowserMinimapBridgeCompareTopics.CompactMarkers);
		_dataPlaneSystem = new BrowserMinimapBridgeCompareDataPlaneSystem(markerWorld, pump, publishHz);
		engine.RegisterSystem(_dataPlaneSystem, SystemGroup.InputCollection);
	}

	private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
	{
		return Ui.Canvas(browserContent)
			.Id("minimap-bridge-compare-browser-surface")
			.WidthPercent(100f)
			.HeightPercent(100f)
			.Absolute(0f, 0f)
			.ZIndex(20);
	}

	private static UiElementBuilder BuildMissingRuntimeRoot()
	{
		return Ui.Column(
				Ui.Text("Browser runtime missing").FontSize(32f).Bold(),
				Ui.Text("Run this showcase with the CEF runtime preset to compare minimap marker bridge paths."))
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

	private string ResolveAssetRoot(GameEngine engine)
	{
		if (engine.VFS != null &&
			engine.VFS.TryResolveFullPath(_assetIndexPath, out string indexPath))
		{
			string? root = Path.GetDirectoryName(indexPath);
			if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
			{
				return root;
			}
		}

		throw new DirectoryNotFoundException($"Minimap browser app assets were not found: {_assetIndexPath}");
	}

	private static int ResolveMarkerCount()
	{
		string? raw = Environment.GetEnvironmentVariable(MarkerCountEnvironmentKey);
		return int.TryParse(raw, out int value)
			? Math.Clamp(value, 1_000, 60_000)
			: BrowserMinimapBridgeCompareTopics.DefaultMarkerCount;
	}

	private static float ResolvePublishHz()
	{
		string? raw = Environment.GetEnvironmentVariable(PublishHzEnvironmentKey);
		return float.TryParse(raw, out float value)
			? Math.Clamp(value, 1f, 60f)
			: 10f;
	}
}

public sealed class BrowserMinimapBridgeCompareShowcaseModEntry : BrowserMinimapBridgeShowcaseModEntry
{
	public BrowserMinimapBridgeCompareShowcaseModEntry()
		: base(
			"BrowserMinimapBridgeCompareShowcaseMod",
			"BrowserMinimapBridgeCompare.Showcase",
			"BrowserMinimapBridgeCompareShowcaseMod:Assets/minimap-bridge-compare-app/index.html",
			"compare")
	{
	}
}
