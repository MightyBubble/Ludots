using System;
using System.IO;
using System.Threading.Tasks;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using LiveSkillWorkbenchMod.Runtime;

namespace LiveSkillWorkbenchMod.DataPlane;

public static class LiveSkillWorkbenchDataPlaneInstaller
{
	/// <summary>
	/// Installs the workbench WebUI dataplane. Browser runtime is required; absence fails explicitly.
	/// </summary>
	public static async Task<LiveSkillWorkbenchDataPlaneInstallation> InstallAsync(
		GameEngine engine,
		IModContext modContext,
		LiveSkillWorkbenchRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(engine);
		ArgumentNullException.ThrowIfNull(modContext);
		ArgumentNullException.ThrowIfNull(runtime);

		var runtimeKey = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
		if (!engine.TryGetService(runtimeKey, out IBrowserRuntime browserRuntime) || browserRuntime == null)
		{
			throw new InvalidOperationException(
				"LiveSkillWorkbenchMod requires IBrowserRuntime. " +
				"Load a browser runtime capability before enabling this Mod; WebUI dataplane will not start without it.");
		}

		IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
			?? throw new InvalidOperationException("UiSurfaceHost service is missing.");
		string assetRoot = ResolveAssetRoot(engine);

		var producer = new LiveSkillWorkbenchTopicProducer(runtime);
		var handler = new LiveSkillWorkbenchCommandHandler(runtime);
		var router = new WebUiCommandRouter(
			new LiveSkillWorkbenchGenerationResolver(),
			new LiveSkillWorkbenchPermissionValidator());
		router.Register(LiveSkillWorkbenchIds.StageEditCommand, handler);
		router.Register(LiveSkillWorkbenchIds.DiscardEditsCommand, handler);
		router.Register(LiveSkillWorkbenchIds.SelectCatalogItemCommand, handler);
		router.Register(LiveSkillWorkbenchIds.PrecheckCommand, handler);
		router.Register(LiveSkillWorkbenchIds.ApplyNextCastCommand, handler);
		router.Register(LiveSkillWorkbenchIds.ApplyImmediateAttributeCommand, handler);
		router.Register(LiveSkillWorkbenchIds.GenerateAiDraftCommand, handler);
		router.Register(LiveSkillWorkbenchIds.BindAiDraftCommand, handler);
		router.Register(LiveSkillWorkbenchIds.PreviewSaveCommand, handler);
		router.Register(LiveSkillWorkbenchIds.SaveToModCommand, handler);
		router.Register(LiveSkillWorkbenchIds.RefreshEffectChainCommand, handler);

		var dispatcher = new WebUiQueuedCommandDispatcher(router);
		var dataPlaneRuntime = new WebUiDataPlaneRuntime(dispatcher);
		dataPlaneRuntime.RegisterTopic(producer);

		var resolver = new BrowserAppResourceResolver(assetRoot);
		var viewport = new BrowserViewport(
			Math.Max(1280, engine.MergedConfig.WindowWidth > 0 ? engine.MergedConfig.WindowWidth : 1600),
			Math.Max(720, engine.MergedConfig.WindowHeight > 0 ? engine.MergedConfig.WindowHeight : 900));
		IBrowserSurface surface = await browserRuntime
			.CreateSurfaceAsync(viewport, resolver)
			.ConfigureAwait(false);
		dataPlaneRuntime.AttachSession(
			LiveSkillWorkbenchIds.WebUiSessionId,
			new BrowserMessageBridgeDataTransport(surface.Messages));

		var pump = new WebUiDataPlaneTickPump(dataPlaneRuntime, dispatcher);
		pump.TrackTopic(LiveSkillWorkbenchIds.Topic);
		var pumpSystem = new LiveSkillWorkbenchDataPlanePumpSystem(pump, producer);
		engine.RegisterPresentationSystem(pumpSystem);

		var browserContent = new BrowserSurfaceCanvasContent(
			surface,
			hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
		UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
			"LiveSkillWorkbench.WebUI",
			UiSurfaceSegment.Main,
			priority: 40,
			exclusive: true));
		surfaceHost.Publish(
			lease,
			UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(browserContent)));

		await surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root)).ConfigureAwait(false);
		modContext.Log("[LiveSkillWorkbenchMod] WebUI dataplane active: topic " + LiveSkillWorkbenchIds.Topic);
		return new LiveSkillWorkbenchDataPlaneInstallation(
			surface,
			browserContent,
			dataPlaneRuntime,
			dispatcher,
			pumpSystem,
			surfaceHost,
			lease);
	}

	private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
	{
		return Ui.Canvas(browserContent)
			.Id("live-skill-workbench-browser-surface")
			.WidthPercent(100f)
			.HeightPercent(100f)
			.Absolute(0f, 0f)
			.ZIndex(40);
	}

	private static string ResolveAssetRoot(GameEngine engine)
	{
		if (engine.VFS != null &&
			engine.VFS.TryResolveFullPath(LiveSkillWorkbenchIds.AssetIndexPath, out string indexPath))
		{
			string? root = Path.GetDirectoryName(indexPath);
			if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
			{
				return root;
			}
		}

		throw new DirectoryNotFoundException(
			$"Live Skill Workbench browser app assets were not found: {LiveSkillWorkbenchIds.AssetIndexPath}");
	}
}

internal sealed class LiveSkillWorkbenchDataPlanePumpSystem : ISystem<float>
{
	private readonly WebUiDataPlaneTickPump _pump;
	private readonly LiveSkillWorkbenchTopicProducer _producer;
	private bool _disposed;

	public LiveSkillWorkbenchDataPlanePumpSystem(
		WebUiDataPlaneTickPump pump,
		LiveSkillWorkbenchTopicProducer producer)
	{
		_pump = pump ?? throw new ArgumentNullException(nameof(pump));
		_producer = producer ?? throw new ArgumentNullException(nameof(producer));
	}

	public void Initialize()
	{
	}

	public void BeforeUpdate(in float dt)
	{
	}

	public void Update(in float dt)
	{
		if (_disposed)
		{
			return;
		}

		_pump.FlushCommandsAsync().GetAwaiter().GetResult();
		if (_producer.HasUnpublishedStateChange)
		{
			_pump.PublishTopicsAsync().GetAwaiter().GetResult();
		}
	}

	public void AfterUpdate(in float dt)
	{
	}

	public void Dispose()
	{
		_disposed = true;
	}
}

public sealed class LiveSkillWorkbenchDataPlaneInstallation : IDisposable
{
	private readonly IBrowserSurface _surface;
	private readonly BrowserSurfaceCanvasContent _browserContent;
	private readonly WebUiDataPlaneRuntime _dataPlaneRuntime;
	private readonly WebUiQueuedCommandDispatcher _dispatcher;
	private readonly LiveSkillWorkbenchDataPlanePumpSystem _pumpSystem;
	private IUiSurfaceHost? _surfaceHost;
	private UiSurfaceLeaseHandle _lease;
	private bool _disposed;

	internal LiveSkillWorkbenchDataPlaneInstallation(
		IBrowserSurface surface,
		BrowserSurfaceCanvasContent browserContent,
		WebUiDataPlaneRuntime dataPlaneRuntime,
		WebUiQueuedCommandDispatcher dispatcher,
		LiveSkillWorkbenchDataPlanePumpSystem pumpSystem,
		IUiSurfaceHost surfaceHost,
		UiSurfaceLeaseHandle lease)
	{
		_surface = surface ?? throw new ArgumentNullException(nameof(surface));
		_browserContent = browserContent ?? throw new ArgumentNullException(nameof(browserContent));
		_dataPlaneRuntime = dataPlaneRuntime ?? throw new ArgumentNullException(nameof(dataPlaneRuntime));
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_pumpSystem = pumpSystem ?? throw new ArgumentNullException(nameof(pumpSystem));
		_surfaceHost = surfaceHost ?? throw new ArgumentNullException(nameof(surfaceHost));
		_lease = lease;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_pumpSystem.Dispose();
		if (_lease.IsValid && _surfaceHost != null)
		{
			_surfaceHost.ReleaseLease(ref _lease);
		}

		_surfaceHost = null;
		_browserContent.Dispose();
		_dataPlaneRuntime.DisposeAsync().GetAwaiter().GetResult();
		_dispatcher.Dispose();
		_surface.DisposeAsync().GetAwaiter().GetResult();
	}
}
