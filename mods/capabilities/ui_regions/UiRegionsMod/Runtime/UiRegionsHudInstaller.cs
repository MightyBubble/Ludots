using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Scripting;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.DataPlane;
using Ludots.WebUI.PanelKit;

namespace UiRegionsMod.Runtime;

public sealed class UiRegionsHudInstallation : IDisposable
{
	private readonly GameEngine _engine;
	private readonly IUiSurfaceHost _host;
	private readonly WebUiDataPlaneRuntime _dataPlane;
	private readonly WebUiPanelKitSurfaceBinder _binder;
	private readonly UiRegionsHudPumpSystem _pumpSystem;
	private readonly float _viewportWidth;
	private readonly float _viewportHeight;
	private bool _disposed;

	internal UiRegionsHudInstallation(
		GameEngine engine,
		IUiSurfaceHost host,
		WebUiDataPlaneRuntime dataPlane,
		WebUiPanelKitSurfaceBinder binder,
		UiRegionsHudPumpSystem pumpSystem,
		float viewportWidth,
		float viewportHeight)
	{
		_engine = engine;
		_host = host;
		_dataPlane = dataPlane;
		_binder = binder;
		_pumpSystem = pumpSystem;
		_viewportWidth = viewportWidth;
		_viewportHeight = viewportHeight;
	}

	public WebUiPanelKitSurfaceBinder Binder => _binder;
	public IReadOnlyList<string> BoundPanelIds => _binder.BoundPanelIds;
	public IReadOnlyList<string> Topics => _binder.BrowserSubscriptionTopics;

	public void RefreshLivePanels()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		TaskRuntimeService tasks = _engine.GetService(CoreServiceKeys.TaskRuntimeService)
			?? throw new InvalidOperationException("TaskRuntimeService missing.");
		ActivityRuntimeService activities = _engine.GetService(CoreServiceKeys.ActivityRuntimeService)
			?? throw new InvalidOperationException("ActivityRuntimeService missing.");
		HudLiveSnapshot snapshot = HudLiveSnapshot.Capture(tasks, activities);

		foreach (WebUiPanelDeclaration panel in _binder.Manifest.Panels)
		{
			if (!_binder.TryGetLease(panel.PanelId, out UiSurfaceLeaseHandle handle))
			{
				continue;
			}

			_host.Publish(
				handle,
				UiRegionsHudInstaller.CreateRegionContribution(
					panel,
					_viewportWidth,
					_viewportHeight,
					snapshot));
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_pumpSystem.Dispose();
		_binder.Dispose();
		_dataPlane.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_disposed = true;
	}
}

public readonly record struct HudLiveSnapshot(
	IReadOnlyList<string> TaskLines,
	IReadOnlyList<string> ActivityOptionLines,
	string? ForcedActivityTitle,
	string? ForcedActivitySummary,
	bool HasForcedActivity)
{
	public static HudLiveSnapshot Capture(
		TaskRuntimeService tasks,
		ActivityRuntimeService activities)
	{
		ArgumentNullException.ThrowIfNull(tasks);
		ArgumentNullException.ThrowIfNull(activities);

		List<TaskView> taskViews = tasks.CaptureViews();
		var taskLines = new List<string>(taskViews.Count);
		for (int i = 0; i < taskViews.Count; i++)
		{
			TaskView view = taskViews[i];
			if (view.State is TaskInstanceState.Offered or TaskInstanceState.Active)
			{
				taskLines.Add($"{view.State} · {view.DisplayName}");
			}
		}

		List<ActivityView> activityViews = activities.CaptureViews();
		string? forcedTitle = null;
		string? forcedSummary = null;
		var optionLines = new List<string>();
		for (int i = 0; i < activityViews.Count; i++)
		{
			ActivityView activity = activityViews[i];
			if (activity.State != ActivityInstanceState.Active ||
			    activity.DispatchPolicy != ActivityDispatchPolicy.Forced)
			{
				continue;
			}

			forcedTitle = activity.DisplayName;
			forcedSummary = activity.Summary;
			var options = new List<ActivityOptionView>();
			if (activities.TryGetActiveOptions(activity.Entity, null, options))
			{
				for (int o = 0; o < options.Count; o++)
				{
					ActivityOptionView option = options[o];
					string suffix = option.Executable ? string.Empty : $" [blocked: {option.BlockReason}]";
					optionLines.Add($"○ {option.Title}{suffix}");
				}
			}

			break;
		}

		return new HudLiveSnapshot(
			taskLines,
			optionLines,
			forcedTitle,
			forcedSummary,
			forcedTitle != null);
	}
}

public static class UiRegionsHudInstaller
{
	/// <summary>
	/// Topic producers are caller-supplied: the content pack owns topic naming (e.g. y5k.*)
	/// and wires real runtimes. This mod registers no topics of its own.
	/// </summary>
	public static UiRegionsHudInstallation Install(
		GameEngine engine,
		string manifestPath,
		IReadOnlyList<IWebUiTopicProducer>? topicProducers = null)
	{
		ArgumentNullException.ThrowIfNull(engine);
		if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
		{
			throw new FileNotFoundException("HUD manifest was not found.", manifestPath);
		}

		IUiSurfaceHost host = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
			?? throw new InvalidOperationException("UiSurfaceHost is required for HUD binding.");
		TaskRuntimeService tasks = engine.GetService(CoreServiceKeys.TaskRuntimeService)
			?? throw new InvalidOperationException("TaskRuntimeService is required.");
		ActivityRuntimeService activities = engine.GetService(CoreServiceKeys.ActivityRuntimeService)
			?? throw new InvalidOperationException("ActivityRuntimeService is required.");

		var dataPlane = new WebUiDataPlaneRuntime();
		RegisterTopics(dataPlane, topicProducers);

		WebUiPanelKitReferenceCatalog catalog = UiRegionsCatalogFactory.Create(dataPlane.IsTopicRegistered);
		WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromFile(manifestPath, catalog);
		float viewportWidth = ResolveViewportWidth(engine);
		float viewportHeight = ResolveViewportHeight(engine);
		HudLiveSnapshot snapshot = HudLiveSnapshot.Capture(tasks, activities);
		var binder = new WebUiPanelKitSurfaceBinder(host, manifest);
		binder.Bind(panel => CreateRegionContribution(panel, viewportWidth, viewportHeight, snapshot));

		var pump = new WebUiDataPlaneTickPump(dataPlane);
		foreach (string topic in binder.BrowserSubscriptionTopics)
		{
			pump.TrackTopic(topic);
		}

		var pumpSystem = new UiRegionsHudPumpSystem(pump);
		engine.RegisterPresentationSystem(pumpSystem);

		var uiRuntime = new UiRegionsRuntime();
		uiRuntime.Install(dataPlane.IsTopicRegistered);
		engine.SetService(UiRegionsServiceKeys.Runtime, uiRuntime);

		return new UiRegionsHudInstallation(
			engine,
			host,
			dataPlane,
			binder,
			pumpSystem,
			viewportWidth,
			viewportHeight);
	}

	private static void RegisterTopics(
		WebUiDataPlaneRuntime dataPlane,
		IReadOnlyList<IWebUiTopicProducer>? topicProducers)
	{
		if (topicProducers == null)
		{
			return;
		}

		var registered = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < topicProducers.Count; i++)
		{
			IWebUiTopicProducer producer = topicProducers[i];
			if (!registered.Add(producer.Topic))
			{
				continue;
			}

			dataPlane.RegisterTopic(producer);
		}
	}

	private static float ResolveViewportWidth(GameEngine engine)
	{
		if (engine.MergedConfig.WindowWidth > 0)
		{
			return engine.MergedConfig.WindowWidth;
		}

		if (engine.GetService(CoreServiceKeys.UIRoot) is Ludots.UI.UIRoot root && root.Width > 0f)
		{
			return root.Width;
		}

		return 1280f;
	}

	private static float ResolveViewportHeight(GameEngine engine)
	{
		if (engine.MergedConfig.WindowHeight > 0)
		{
			return engine.MergedConfig.WindowHeight;
		}

		if (engine.GetService(CoreServiceKeys.UIRoot) is Ludots.UI.UIRoot root && root.Height > 0f)
		{
			return root.Height;
		}

		return 720f;
	}

	internal static UiSurfaceContribution CreateRegionContribution(
		WebUiPanelDeclaration panel,
		float viewportWidth,
		float viewportHeight,
		HudLiveSnapshot snapshot)
	{
		bool isActivityModal =
			string.Equals(panel.PanelType, WebUiRegionPanelDescriptors.ActivityModalPanelType, StringComparison.Ordinal);

		if (!isActivityModal && string.Equals(panel.SurfaceRegionId, WebUiNineGridRegions.Center, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				$"Panel '{panel.PanelId}' declares the reserved center region; only overlays/modals may cover it.");
		}

		WebUiNineGridRegionRect rect = WebUiNineGridRegions.GetDefaultGeometry(panel.SurfaceRegionId);
		float x = viewportWidth * rect.XPercent / 100f;
		float y = viewportHeight * rect.YPercent / 100f;
		float w = viewportWidth * rect.WidthPercent / 100f;
		float h = viewportHeight * rect.HeightPercent / 100f;

		if (isActivityModal && !snapshot.HasForcedActivity)
		{
			return UiSurfaceContribution.FromBuilder(() =>
				Ui.Panel()
					.Id($"panel-kit-{panel.PanelId}")
					.Absolute(viewportWidth * 0.5f, viewportHeight * 0.5f)
					.Width(1f)
					.Height(1f)
					.ZIndex(panel.SurfacePriority));
		}

		if (isActivityModal)
		{
			var children = new List<UiElementBuilder>
			{
				Ui.Text(panel.Title ?? panel.PanelType).Id($"uir-{panel.PanelId}-title"),
				Ui.Text(snapshot.ForcedActivityTitle ?? string.Empty).Id($"uir-{panel.PanelId}-name"),
				Ui.Text(snapshot.ForcedActivitySummary ?? string.Empty).Id($"uir-{panel.PanelId}-summary"),
			};
			for (int i = 0; i < snapshot.ActivityOptionLines.Count; i++)
			{
				children.Add(Ui.Text(snapshot.ActivityOptionLines[i]).Id($"uir-{panel.PanelId}-opt-{i}"));
			}

			return UiSurfaceContribution.FromBuilder(() =>
				Ui.Panel(Ui.Column(children.ToArray()))
					.Id($"panel-kit-{panel.PanelId}")
					.Absolute(x, y)
					.Width(w)
					.Height(h)
					.ZIndex(panel.SurfacePriority));
		}

		var lines = new List<UiElementBuilder>
		{
			Ui.Text(panel.Title ?? panel.PanelType).Id($"uir-{panel.PanelId}-title"),
		};

		if (string.Equals(panel.PanelType, "objective", StringComparison.Ordinal) && snapshot.TaskLines.Count > 0)
		{
			int limit = Math.Min(4, snapshot.TaskLines.Count);
			for (int i = 0; i < limit; i++)
			{
				lines.Add(Ui.Text(snapshot.TaskLines[i]).Id($"uir-{panel.PanelId}-task-{i}"));
			}
		}
		else if (!string.IsNullOrWhiteSpace(panel.Subtitle))
		{
			lines.Add(Ui.Text(panel.Subtitle).Id($"uir-{panel.PanelId}-subtitle"));
		}

		return UiSurfaceContribution.FromBuilder(() =>
			Ui.Panel(Ui.Column(lines.ToArray()))
				.Id($"panel-kit-{panel.PanelId}")
				.Absolute(x, y)
				.Width(w)
				.Height(h)
				.ZIndex(panel.SurfacePriority));
	}
}

internal sealed class UiRegionsHudPumpSystem : ISystem<float>
{
	private readonly WebUiDataPlaneTickPump _pump;
	private bool _disposed;

	public UiRegionsHudPumpSystem(WebUiDataPlaneTickPump pump)
	{
		_pump = pump ?? throw new ArgumentNullException(nameof(pump));
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

		_pump.PublishTopicsAsync().AsTask().GetAwaiter().GetResult();
	}

	public void AfterUpdate(in float dt)
	{
	}

	public void Dispose()
	{
		_disposed = true;
	}
}
