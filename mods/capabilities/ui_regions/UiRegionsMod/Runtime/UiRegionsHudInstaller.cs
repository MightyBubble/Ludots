using System.Globalization;
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
	private readonly WebUiDataPlaneRuntime _dataPlane;
	private readonly WebUiPanelKitSurfaceBinder _binder;
	private readonly UiRegionsHudPumpSystem _pumpSystem;
	private bool _disposed;

	internal UiRegionsHudInstallation(
		WebUiDataPlaneRuntime dataPlane,
		WebUiPanelKitSurfaceBinder binder,
		UiRegionsHudPumpSystem pumpSystem)
	{
		_dataPlane = dataPlane;
		_binder = binder;
		_pumpSystem = pumpSystem;
	}

	public WebUiPanelKitSurfaceBinder Binder => _binder;
	public IReadOnlyList<string> BoundPanelIds => _binder.BoundPanelIds;
	public IReadOnlyList<string> Topics => _binder.BrowserSubscriptionTopics;

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
		var binder = new WebUiPanelKitSurfaceBinder(host, manifest);
		binder.Bind(panel => CreateRegionContribution(panel));

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

		return new UiRegionsHudInstallation(dataPlane, binder, pumpSystem);
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

		// Common generic topic names used by region manifests.
		Register(new TaskObjectiveTopicProducer("y5k.topic.objective", tasks));
		Register(new ActivityModalTopicProducer("y5k.topic.activity", activities, activityPresentation));
		Register(new StaticHudTopicProducer("y5k.topic.time", "time-control", () => new
		{
			paused = false,
			label = "cycle",
		}));
		Register(new StaticHudTopicProducer("y5k.topic.filter", "view-filter", () => new
		{
			filters = Array.Empty<string>(),
		}));
		Register(new StaticHudTopicProducer("y5k.topic.notification", "notification", () => new
		{
			items = Array.Empty<object>(),
		}));
		Register(new StaticHudTopicProducer("y5k.topic.minimap", "minimap.web-shell", () => new
		{
			ready = true,
		}));
		Register(new StaticHudTopicProducer("y5k.topic.entity-insight", "entity-insight", () => new
		{
			selection = Array.Empty<object>(),
		}));
		Register(new StaticHudTopicProducer("y5k.topic.production", "production-overview", () => new
		{
			queues = Array.Empty<object>(),
		}));
		Register(new StaticHudTopicProducer("y5k.topic.entity-list", "entity-list", () => new
		{
			entities = Array.Empty<object>(),
		}));
		Register(new StaticHudTopicProducer("y5k.topic.command", "command-deck", () => new
		{
			slots = Array.Empty<object>(),
		}));

		if (staticTopicFactories == null)
		{
			return;
		}

		foreach (KeyValuePair<string, Func<object>> pair in staticTopicFactories)
		{
			Register(new StaticHudTopicProducer(pair.Key, "custom", pair.Value));
		}
	}

	private const float ReferenceViewportWidth = 1600f;
	private const float ReferenceViewportHeight = 900f;

	private static UiSurfaceContribution CreateRegionContribution(WebUiPanelDeclaration panel)
	{
		(float xPct, float yPct, float wPct, float hPct) = ResolveNineGridPercent(panel.SurfaceRegionId);
		float x = ReferenceViewportWidth * xPct / 100f;
		float y = ReferenceViewportHeight * yPct / 100f;
		float w = ReferenceViewportWidth * wPct / 100f;
		float h = ReferenceViewportHeight * hPct / 100f;
		string title = string.Create(CultureInfo.InvariantCulture, $"{panel.PanelType} · {panel.PanelId}");
		return UiSurfaceContribution.FromBuilder(() =>
			Ui.Panel(
					Ui.Column(
						Ui.Text(title).Id($"uir-{panel.PanelId}-title"),
						Ui.Text(panel.Topic).Id($"uir-{panel.PanelId}-topic")))
				.Id($"panel-kit-{panel.PanelId}")
				.Absolute(x, y)
				.Width(w)
				.Height(h)
				.ZIndex(panel.SurfacePriority));
	}

	private static (float X, float Y, float W, float H) ResolveNineGridPercent(string regionId)
	{
		// Percent-based 3x3 grid. Center remains mostly clear for the 3D world;
		// activity-modal may still bind to region.center as a temporary overlay.
		return regionId switch
		{
			WebUiNineGridRegions.TopLeft => (0f, 0f, 28f, 18f),
			WebUiNineGridRegions.TopCenter => (28f, 0f, 44f, 14f),
			WebUiNineGridRegions.TopRight => (72f, 0f, 28f, 22f),
			WebUiNineGridRegions.MiddleLeft => (0f, 18f, 24f, 56f),
			WebUiNineGridRegions.Center => (24f, 22f, 52f, 48f),
			WebUiNineGridRegions.MiddleRight => (76f, 18f, 24f, 56f),
			WebUiNineGridRegions.BottomLeft => (0f, 74f, 24f, 26f),
			WebUiNineGridRegions.BottomCenter => (24f, 70f, 52f, 30f),
			WebUiNineGridRegions.BottomRight => (76f, 74f, 24f, 26f),
			_ => throw new InvalidOperationException(
				$"Unknown nine-grid surface region '{regionId}'."),
		};
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
