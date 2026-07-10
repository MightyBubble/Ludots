using System.Text;
using System.Text.Json;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using Ludots.WebUI.DataPlane;
using Ludots.WebUI.PanelKit;
using NUnit.Framework;

namespace Ludots.Tests.WebUiPanelKit;

[TestFixture]
public sealed class WebUiPanelKitManifestTests
{
	[Test]
	public void LoadSampleManifest_RegistersFourPanels_OnSameUiSurfaceHost()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		RegisterSampleTopics(runtime);
		WebUiPanelKitReferenceCatalog catalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);
		WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromFile(
			WebUiPanelKitSampleCatalog.SampleManifestPath(),
			catalog);

		Assert.That(manifest.Panels, Has.Count.EqualTo(4));
		Assert.That(manifest.Panels.Select(panel => panel.PanelId), Is.EquivalentTo(new[]
		{
			"hud.resource-bar",
			"hud.command-deck",
			"hud.objective",
			"hud.production-overview"
		}));

		UIRoot root = CreateRoot(out UiSurfaceHost host);
		using var binder = new WebUiPanelKitSurfaceBinder(host, manifest);
		binder.Bind();

		Assert.That(binder.BoundPanelIds, Has.Count.EqualTo(4));
		Assert.That(root.Scene, Is.SameAs(host.Scene));
		Assert.That(root.Scene!.FindByElementId("panel-kit-hud.resource-bar"), Is.Not.Null);
		Assert.That(root.Scene.FindByElementId("panel-kit-hud.command-deck"), Is.Not.Null);
		Assert.That(root.Scene.FindByElementId("panel-kit-hud.objective"), Is.Not.Null);
		Assert.That(root.Scene.FindByElementId("panel-kit-hud.production-overview"), Is.Not.Null);
	}

	[Test]
	public void Load_DuplicatePanelId_FailsFastWithConcreteId()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		RegisterSampleTopics(runtime);
		WebUiPanelKitReferenceCatalog catalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);
		string json = BuildManifestJson(
			("hud.resource-bar", "resource-bar", "region.top-left", WebUiPanelKitSampleCatalog.ResourceTopic, "profile.resource.generic", "layout.bar.horizontal"),
			("hud.resource-bar", "command-deck", "region.bottom-center", WebUiPanelKitSampleCatalog.CommandTopic, "profile.command.generic", "layout.deck.grid"));

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiPanelKitManifestLoader.LoadFromJson(json, catalog, "dup-test"))!;

		Assert.That(ex.Message, Does.Contain("hud.resource-bar"));
		Assert.That(ex.Message, Does.Contain("duplicates panel id"));
	}

	[Test]
	public void Load_UnknownTopic_FailsFastWithConcreteId()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		RegisterSampleTopics(runtime);
		WebUiPanelKitReferenceCatalog catalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);
		string json = BuildManifestJson(
			("hud.resource-bar", "resource-bar", "region.top-left", "panel-kit.missing.topic", "profile.resource.generic", "layout.bar.horizontal"));

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiPanelKitManifestLoader.LoadFromJson(json, catalog, "topic-test"))!;

		Assert.That(ex.Message, Does.Contain("panel-kit.missing.topic"));
		Assert.That(ex.Message, Does.Contain("unknown DataPlane topic"));
	}

	[Test]
	public void Load_UnknownProfile_FailsFastWithConcreteId()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		RegisterSampleTopics(runtime);
		WebUiPanelKitReferenceCatalog catalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);
		string json = BuildManifestJson(
			("hud.resource-bar", "resource-bar", "region.top-left", WebUiPanelKitSampleCatalog.ResourceTopic, "profile.missing", "layout.bar.horizontal"));

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiPanelKitManifestLoader.LoadFromJson(json, catalog, "profile-test"))!;

		Assert.That(ex.Message, Does.Contain("profile.missing"));
		Assert.That(ex.Message, Does.Contain("unknown profile"));
	}

	[Test]
	public void Load_UnknownLayout_FailsFastWithConcreteId()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		RegisterSampleTopics(runtime);
		WebUiPanelKitReferenceCatalog catalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);
		string json = BuildManifestJson(
			("hud.resource-bar", "resource-bar", "region.top-left", WebUiPanelKitSampleCatalog.ResourceTopic, "profile.resource.generic", "layout.missing"));

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiPanelKitManifestLoader.LoadFromJson(json, catalog, "layout-test"))!;

		Assert.That(ex.Message, Does.Contain("layout.missing"));
		Assert.That(ex.Message, Does.Contain("unknown layout"));
	}

	[Test]
	public void Load_UnknownSurfaceRegion_FailsFastWithConcreteId()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		RegisterSampleTopics(runtime);
		WebUiPanelKitReferenceCatalog catalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);
		string json = BuildManifestJson(
			("hud.resource-bar", "resource-bar", "region.missing", WebUiPanelKitSampleCatalog.ResourceTopic, "profile.resource.generic", "layout.bar.horizontal"));

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiPanelKitManifestLoader.LoadFromJson(json, catalog, "surface-test"))!;

		Assert.That(ex.Message, Does.Contain("region.missing"));
		Assert.That(ex.Message, Does.Contain("unknown surface region"));
	}

	[Test]
	public void BrowserSubscriptionTopics_ComeOnlyFromManifestDeclarations()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		RegisterSampleTopics(runtime);
		runtime.RegisterTopic(new StubTopicProducer("panel-kit.extra.unrelated"));
		WebUiPanelKitReferenceCatalog catalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);
		WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromFile(
			WebUiPanelKitSampleCatalog.SampleManifestPath(),
			catalog);

		CreateRoot(out UiSurfaceHost host);
		using var binder = new WebUiPanelKitSurfaceBinder(host, manifest);
		binder.Bind();

		Assert.That(binder.BrowserSubscriptionTopics, Is.EqualTo(new[]
		{
			WebUiPanelKitSampleCatalog.ResourceTopic,
			WebUiPanelKitSampleCatalog.CommandTopic,
			WebUiPanelKitSampleCatalog.ObjectiveTopic,
			WebUiPanelKitSampleCatalog.ProductionTopic
		}));
		Assert.That(binder.BrowserSubscriptionTopics, Does.Not.Contain("panel-kit.extra.unrelated"));
		Assert.That(manifest.DeclaredTopics, Is.EqualTo(binder.BrowserSubscriptionTopics));
	}

	[Test]
	public void SampleManifest_ContainsNoHardcodedGameNames()
	{
		string json = File.ReadAllText(WebUiPanelKitSampleCatalog.SampleManifestPath());
		string[] forbidden =
		[
			"CK3", "Stellaris", "群星", "Command", "Conquer", "Age of Empires", "StarCraft",
			"Minerals", "Vespene", "Spice", "Gold", "Wood", "Food", "Infantry", "Marine"
		];

		foreach (string token in forbidden)
		{
			Assert.That(json, Does.Not.Contain(token), $"Sample manifest must not hardcode '{token}'.");
		}
	}

	[Test]
	public void QuestObjectiveDescriptors_MatchSampleCatalogVocabulary()
	{
		Assert.That(WebUiQuestObjectivePanelDescriptors.PanelType, Is.EqualTo("objective"));
		Assert.That(WebUiQuestObjectivePanelDescriptors.GenericProfileId, Is.EqualTo("profile.objective.generic"));
		Assert.That(WebUiQuestObjectivePanelDescriptors.SampleTopic, Is.EqualTo(WebUiPanelKitSampleCatalog.ObjectiveTopic));
		Assert.That(WebUiQuestObjectivePanelDescriptors.VerticalListLayoutId, Is.EqualTo("layout.list.vertical"));

		string json = File.ReadAllText(WebUiPanelKitSampleCatalog.SampleManifestPath());
		Assert.That(json, Does.Contain(WebUiQuestObjectivePanelDescriptors.SampleTopic));
		Assert.That(json, Does.Contain(WebUiQuestObjectivePanelDescriptors.GenericProfileId));
		Assert.That(json, Does.Contain($"\"panelType\": \"{WebUiQuestObjectivePanelDescriptors.PanelType}\""));
	}

	[Test]
	public void DataPlane_IsTopicRegistered_ReportsRegisteredTopics()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		Assert.That(runtime.IsTopicRegistered(WebUiPanelKitSampleCatalog.ResourceTopic), Is.False);
		RegisterSampleTopics(runtime);
		Assert.That(runtime.IsTopicRegistered(WebUiPanelKitSampleCatalog.ResourceTopic), Is.True);
		Assert.That(runtime.GetRegisteredTopics(), Does.Contain(WebUiPanelKitSampleCatalog.CommandTopic));
	}

	private static void RegisterSampleTopics(WebUiDataPlaneRuntime runtime)
	{
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.ResourceTopic));
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.CommandTopic));
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.ObjectiveTopic));
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.ProductionTopic));
	}

	private static string BuildManifestJson(params (string panelId, string panelType, string region, string topic, string profile, string layout)[] panels)
	{
		var panelNodes = panels.Select((panel, index) => new
		{
			panelId = panel.panelId,
			panelType = panel.panelType,
			surfaceRegionId = panel.region,
			surfaceSegment = "Overlay",
			surfacePriority = (index + 1) * 10,
			anchor = "top-left",
			visibleConditionId = "condition.always",
			topic = panel.topic,
			profileId = panel.profile,
			layoutId = panel.layout,
			densityId = "density.compact",
			inputCapabilityId = "input.none"
		});

		return JsonSerializer.Serialize(new
		{
			manifestId = "panel-kit.test",
			hostOwnerId = "panel-kit.test",
			panels = panelNodes
		});
	}

	private static UIRoot CreateRoot(out UiSurfaceHost host)
	{
		var root = new UIRoot(new NullUiRenderer());
		host = new UiSurfaceHost(root, new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		return root;
	}

	private sealed class StubTopicProducer : IWebUiTopicProducer
	{
		public StubTopicProducer(string topic)
		{
			Topic = topic;
		}

		public string Topic { get; }

		public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
		{
			packet = new WebUiOutboundPacket(
				context.SessionId,
				Topic,
				WebUiPacketKind.Snapshot,
				WebUiDeliverySemantics.LatestWins,
				Encoding.UTF8.GetBytes("{}"),
				"application/json",
				context.RequestId);
			return true;
		}
	}

	private sealed class NullUiRenderer : IUiRenderer
	{
		public void Render(UiScene scene, float width, float height)
		{
		}
	}
}
