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
	public static HudLiveSnapshot Capture(TaskRuntimeService tasks, ActivityRuntimeService activities)
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
				string stateLabel = view.State == TaskInstanceState.Offered ? "可领取" : "进行中";
				taskLines.Add($"{stateLabel} · {view.DisplayName}");
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
					string suffix = option.Executable ? string.Empty : $"（不可执行：{option.BlockReason}）";
					optionLines.Add($"○ {option.Title}{suffix}");
				}
			}

			break;
		}

		return new HudLiveSnapshot(taskLines, optionLines, forcedTitle, forcedSummary, forcedTitle != null);
	}
}

public static class UiRegionsHudInstaller
{
	public static UiRegionsHudInstallation Install(
		GameEngine engine,
		string manifestPath,
		IReadOnlyDictionary<string, Func<object>>? staticTopicFactories = null)
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
		ActivityPresentationBuffer activityPresentation = engine.GetService(CoreServiceKeys.ActivityPresentationBuffer)
			?? throw new InvalidOperationException("ActivityPresentationBuffer is required.");

		var dataPlane = new WebUiDataPlaneRuntime();
		RegisterDefaultTopics(dataPlane, tasks, activities, activityPresentation, staticTopicFactories);

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

		UiRegionsRuntime? uiRuntime = null;
		if (engine.TryGetService(UiRegionsServiceKeys.Runtime, out UiRegionsRuntime existing) && existing != null)
		{
			uiRuntime = existing;
		}

		uiRuntime ??= new UiRegionsRuntime();
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

	private static void RegisterDefaultTopics(
		WebUiDataPlaneRuntime dataPlane,
		TaskRuntimeService tasks,
		ActivityRuntimeService activities,
		ActivityPresentationBuffer activityPresentation,
		IReadOnlyDictionary<string, Func<object>>? staticTopicFactories)
	{
		var registered = new HashSet<string>(StringComparer.Ordinal);

		void Register(IWebUiTopicProducer producer)
		{
			if (registered.Add(producer.Topic))
			{
				dataPlane.RegisterTopic(producer);
			}
		}

		Register(new TaskObjectiveTopicProducer("y5k.topic.objective", tasks));
		Register(new ActivityModalTopicProducer("y5k.topic.activity", activities, activityPresentation));
		Register(new StaticHudTopicProducer("y5k.topic.time", "time-control", () => new { paused = false, label = "cycle" }));
		Register(new StaticHudTopicProducer("y5k.topic.filter", "view-filter", () => new { filters = Array.Empty<string>() }));
		Register(new StaticHudTopicProducer("y5k.topic.notification", "notification", () => new { items = Array.Empty<object>() }));
		Register(new StaticHudTopicProducer("y5k.topic.minimap", "minimap.web-shell", () => new { ready = true }));
		Register(new StaticHudTopicProducer("y5k.topic.entity-insight", "entity-insight", () => new { selection = Array.Empty<object>() }));
		Register(new StaticHudTopicProducer("y5k.topic.production", "production-overview", () => new { queues = Array.Empty<object>() }));
		Register(new StaticHudTopicProducer("y5k.topic.entity-list", "entity-list", () => new { entities = Array.Empty<object>() }));
		Register(new StaticHudTopicProducer("y5k.topic.command", "command-deck", () => new { slots = Array.Empty<object>() }));

		if (staticTopicFactories == null)
		{
			return;
		}

		foreach (KeyValuePair<string, Func<object>> pair in staticTopicFactories)
		{
			Register(new StaticHudTopicProducer(pair.Key, "custom", pair.Value));
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
		(float xPct, float yPct, float wPct, float hPct) = ResolveNineGridPercent(panel.SurfaceRegionId);
		float x = viewportWidth * xPct / 100f;
		float y = viewportHeight * yPct / 100f;
		float w = viewportWidth * wPct / 100f;
		float h = viewportHeight * hPct / 100f;

		bool isActivityModal =
			string.Equals(panel.PanelType, WebUiRegionPanelDescriptors.ActivityModalPanelType, StringComparison.Ordinal);

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

		if (isActivityModal && snapshot.HasForcedActivity)
		{
			var children = new List<UiElementBuilder>
			{
				Ui.Text("活动抉择").Id($"uir-{panel.PanelId}-title"),
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

		string title = ResolvePlayerFacingTitle(panel.PanelType);
		var lines = new List<UiElementBuilder>
		{
			Ui.Text(title).Id($"uir-{panel.PanelId}-title"),
		};

		if (string.Equals(panel.PanelType, "objective", StringComparison.Ordinal) && snapshot.TaskLines.Count > 0)
		{
			int limit = Math.Min(4, snapshot.TaskLines.Count);
			for (int i = 0; i < limit; i++)
			{
				lines.Add(Ui.Text(snapshot.TaskLines[i]).Id($"uir-{panel.PanelId}-task-{i}"));
			}
		}
		else
		{
			lines.Add(Ui.Text(ResolvePlayerFacingSubtitle(panel.PanelType)).Id($"uir-{panel.PanelId}-subtitle"));
		}

		return UiSurfaceContribution.FromBuilder(() =>
			Ui.Panel(Ui.Column(lines.ToArray()))
				.Id($"panel-kit-{panel.PanelId}")
				.Absolute(x, y)
				.Width(w)
				.Height(h)
				.ZIndex(panel.SurfacePriority));
	}

	private static string ResolvePlayerFacingTitle(string panelType) =>
		panelType switch
		{
			"objective" => "任务追踪",
			"time-control" => "时间控制",
			"view-filter" => "视图过滤",
			"notification" => "通报",
			"minimap.web-shell" => "小地图",
			"entity-insight" => "实体详情",
			"production-overview" => "全局生产",
			"entity-list" => "实体列表",
			"command-deck" => "命令栏",
			"activity-modal" => "活动抉择",
			"event-log" => "事件日志",
			_ => panelType,
		};

	private static string ResolvePlayerFacingSubtitle(string panelType) =>
		panelType switch
		{
			"objective" => "进行中 / 可领取",
			"time-control" => "推进 · 暂停",
			"view-filter" => "收窄地图关注",
			"notification" => "结算与告警",
			"minimap.web-shell" => "跳转视野",
			"entity-insight" => "选中城 / 英雄",
			"production-overview" => "队列摘要",
			"entity-list" => "Tab 切换浏览",
			"command-deck" => "下令 / 技能位",
			"activity-modal" => "当面拍板",
			"event-log" => "最近条目",
			_ => string.Empty,
		};

	private static (float X, float Y, float W, float H) ResolveNineGridPercent(string regionId) =>
		regionId switch
		{
			WebUiNineGridRegions.TopLeft => (1f, 1f, 26f, 16f),
			WebUiNineGridRegions.TopCenter => (28f, 1f, 42f, 12f),
			WebUiNineGridRegions.TopRight => (71f, 1f, 28f, 18f),
			WebUiNineGridRegions.MiddleLeft => (1f, 18f, 22f, 52f),
			WebUiNineGridRegions.Center => (24f, 20f, 52f, 48f),
			WebUiNineGridRegions.MiddleRight => (77f, 18f, 22f, 52f),
			WebUiNineGridRegions.BottomLeft => (1f, 72f, 22f, 26f),
			WebUiNineGridRegions.BottomCenter => (24f, 70f, 52f, 28f),
			WebUiNineGridRegions.BottomRight => (77f, 72f, 22f, 26f),
			_ => throw new InvalidOperationException($"Unknown nine-grid surface region '{regionId}'."),
		};
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
