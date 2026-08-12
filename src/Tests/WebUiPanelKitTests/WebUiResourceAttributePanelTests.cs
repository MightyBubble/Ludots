using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using Ludots.WebUI.DataPlane;
using Ludots.WebUI.PanelKit;
using NUnit.Framework;

namespace Ludots.Tests.WebUiPanelKit;

[TestFixture]
public sealed class WebUiResourceAttributePanelTests
{
	[SetUp]
	public void SetUp()
	{
		AttributeRegistry.Clear();
	}

	[TearDown]
	public void TearDown()
	{
		AttributeRegistry.Clear();
	}

	[Test]
	public void SameDescriptor_CanDeclareSingleEntityAndAggregateFields()
	{
		RegisterSampleAttributes();
		WebUiResourceAttributeReferenceCatalog catalog = WebUiResourceAttributeSampleCatalog.Create(
			IsAttributeRegistered,
			key => string.Equals(key, "graph.output.sample.player.total", StringComparison.Ordinal));

		WebUiResourceAttributeDescriptor descriptor = WebUiResourceAttributeDescriptorLoader.LoadFromFile(
			WebUiResourceAttributeSampleCatalog.SampleDescriptorPath(),
			catalog);

		Assert.That(descriptor.DescriptorId, Is.EqualTo(WebUiResourceAttributeSampleCatalog.DescriptorId));
		Assert.That(descriptor.Fields, Has.Count.EqualTo(3));
		Assert.That(descriptor.Fields.Select(field => field.SourceKind), Is.EquivalentTo(new[]
		{
			WebUiResourceAttributeSourceKind.SingleAttribute,
			WebUiResourceAttributeSourceKind.DerivedAttribute,
			WebUiResourceAttributeSourceKind.AggregateProjection
		}));
		Assert.That(descriptor.Fields.Any(field => field.AttributeId == "attr.sample.primary"), Is.True);
		Assert.That(descriptor.Fields.Any(field => field.GraphOutputKey == "graph.output.sample.player.total"), Is.True);
	}

	[Test]
	public void TopicProducer_Payload_ContainsOwnerDescriptorRevisionAndValues()
	{
		RegisterSampleAttributes();
		using World world = World.Create();
		Entity owner = world.Create();
		world.Add(owner, AttributeBuffer.CreateAttached());
		ref AttributeBuffer buffer = ref world.Get<AttributeBuffer>(owner);
		buffer.SetBase(AttributeRegistry.GetId("attr.sample.primary"), 12f);
		buffer.SetBase(AttributeRegistry.GetId("attr.sample.derived"), 3.5f);

		var keyRegistry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
		var graphOutputs = new GraphOutputValueStore(keyRegistry, initialCapacity: 16);
		graphOutputs.SetFloat(owner, "graph.output.sample.player.total", 42f);

		WebUiResourceAttributeDescriptor descriptor = LoadSampleDescriptor(requireGraphKey: true);
		var producer = new WebUiResourceAttributeTopicProducer(
			WebUiResourceAttributeSampleCatalog.Topic,
			world,
			owner,
			descriptor,
			graphOutputs);

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(producer);
		Assert.That(runtime.IsTopicRegistered(WebUiResourceAttributeSampleCatalog.Topic), Is.True);

		var context = new WebUiTopicContext("session-a", producer.Topic, 7, JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		Assert.That(packet.ContentType, Is.EqualTo(WebUiResourceAttributeTopicProducer.JsonContentType));

		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement root = document.RootElement;
		Assert.That(root.GetProperty("owner").GetProperty("entityId").GetInt32(), Is.EqualTo(owner.Id));
		Assert.That(root.GetProperty("descriptor").GetString(), Is.EqualTo(descriptor.DescriptorId));
		Assert.That(root.GetProperty("revision").GetUInt32(), Is.GreaterThan(0u));
		JsonElement values = root.GetProperty("values");
		Assert.That(values.GetArrayLength(), Is.EqualTo(3));

		JsonElement primary = FindValue(values, "field.primary");
		Assert.That(primary.GetProperty("sourceKind").GetString(), Is.EqualTo(nameof(WebUiResourceAttributeSourceKind.SingleAttribute)));
		Assert.That(primary.GetProperty("value").GetSingle(), Is.EqualTo(12f));

		JsonElement aggregate = FindValue(values, "field.aggregate");
		Assert.That(aggregate.GetProperty("sourceKind").GetString(), Is.EqualTo(nameof(WebUiResourceAttributeSourceKind.AggregateProjection)));
		Assert.That(aggregate.GetProperty("graphOutputKey").GetString(), Is.EqualTo("graph.output.sample.player.total"));
		Assert.That(aggregate.GetProperty("value").GetSingle(), Is.EqualTo(42f));
	}

	[Test]
	public void Load_UnknownAttribute_FailsFastWithConcreteId()
	{
		AttributeRegistry.Register("attr.sample.primary");
		AttributeRegistry.Register("attr.sample.derived");
		WebUiResourceAttributeReferenceCatalog catalog = WebUiResourceAttributeSampleCatalog.Create(
			name => AttributeRegistry.GetId(name) != AttributeRegistry.InvalidId);

		string json = """
		{
		  "descriptorId": "panel-kit.test.resource",
		  "fields": [
		    {
		      "fieldId": "field.missing",
		      "groupId": "group.a",
		      "displayTokenId": "token.resource.primary",
		      "unitTokenId": "unit.count",
		      "sortOrder": 1,
		      "sourceKind": "singleAttribute",
		      "attributeId": "attr.missing.resource"
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiResourceAttributeDescriptorLoader.LoadFromJson(json, catalog, "attr-test"))!;

		Assert.That(ex.Message, Does.Contain("attr.missing.resource"));
		Assert.That(ex.Message, Does.Contain("unknown attribute"));
	}

	[Test]
	public void Load_UnknownDisplayToken_FailsFastWithConcreteId()
	{
		RegisterSampleAttributes();
		WebUiResourceAttributeReferenceCatalog catalog = WebUiResourceAttributeSampleCatalog.Create(IsAttributeRegistered);

		string json = """
		{
		  "descriptorId": "panel-kit.test.resource",
		  "fields": [
		    {
		      "fieldId": "field.a",
		      "groupId": "group.a",
		      "displayTokenId": "token.missing",
		      "unitTokenId": "unit.count",
		      "sortOrder": 1,
		      "sourceKind": "singleAttribute",
		      "attributeId": "attr.sample.primary"
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiResourceAttributeDescriptorLoader.LoadFromJson(json, catalog, "token-test"))!;

		Assert.That(ex.Message, Does.Contain("token.missing"));
		Assert.That(ex.Message, Does.Contain("unknown display token"));
	}

	[Test]
	public void Produce_MissingGraphOutput_FailsFastWithConcreteId()
	{
		RegisterSampleAttributes();
		using World world = World.Create();
		Entity owner = world.Create();
		world.Add(owner, AttributeBuffer.CreateAttached());
		ref AttributeBuffer buffer = ref world.Get<AttributeBuffer>(owner);
		buffer.SetBase(AttributeRegistry.GetId("attr.sample.primary"), 1f);
		buffer.SetBase(AttributeRegistry.GetId("attr.sample.derived"), 2f);

		var keyRegistry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
		var graphOutputs = new GraphOutputValueStore(keyRegistry, initialCapacity: 16);
		WebUiResourceAttributeDescriptor descriptor = LoadSampleDescriptor(requireGraphKey: false);
		var producer = new WebUiResourceAttributeTopicProducer(
			WebUiResourceAttributeSampleCatalog.Topic,
			world,
			owner,
			descriptor,
			graphOutputs);

		var context = new WebUiTopicContext("session-a", producer.Topic, 1, JsonSerializer.SerializeToElement(new { }));
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			producer.TryCreateSnapshot(in context, out _))!;

		Assert.That(ex.Message, Does.Contain("graph.output.sample.player.total"));
		Assert.That(ex.Message, Does.Contain("missing graph output"));
	}

	[Test]
	public void BrowserSubscriptionTopics_StillComeFromWpkManifest_WhenResourceTopicUsesProducer()
	{
		RegisterSampleAttributes();
		using World world = World.Create();
		Entity owner = world.Create();
		world.Add(owner, AttributeBuffer.CreateAttached());
		ref AttributeBuffer buffer = ref world.Get<AttributeBuffer>(owner);
		buffer.SetBase(AttributeRegistry.GetId("attr.sample.primary"), 1f);
		buffer.SetBase(AttributeRegistry.GetId("attr.sample.derived"), 2f);

		var keyRegistry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
		var graphOutputs = new GraphOutputValueStore(keyRegistry, initialCapacity: 16);
		graphOutputs.SetFloat(owner, "graph.output.sample.player.total", 9f);

		WebUiResourceAttributeDescriptor descriptor = LoadSampleDescriptor(requireGraphKey: true);
		var resourceProducer = new WebUiResourceAttributeTopicProducer(
			WebUiPanelKitSampleCatalog.ResourceTopic,
			world,
			owner,
			descriptor,
			graphOutputs);

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(resourceProducer);
		foreach (string topic in WebUiPanelKitSampleCatalog.SampleTopics)
		{
			if (topic == WebUiPanelKitSampleCatalog.ResourceTopic)
			{
				continue;
			}

			runtime.RegisterTopic(new StubTopicProducer(topic));
		}

		runtime.RegisterTopic(new StubTopicProducer("panel-kit.extra.unrelated"));

		WebUiPanelKitReferenceCatalog panelCatalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);
		WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromFile(
			WebUiPanelKitSampleCatalog.SampleManifestPath(),
			panelCatalog);

		CreateRoot(out UiSurfaceHost host);
		using var binder = new WebUiPanelKitSurfaceBinder(host, manifest);
		binder.Bind();

		Assert.That(binder.BrowserSubscriptionTopics, Is.EqualTo(WebUiPanelKitSampleCatalog.SampleTopics));
		Assert.That(binder.BrowserSubscriptionTopics, Does.Not.Contain("panel-kit.extra.unrelated"));
		Assert.That(manifest.DeclaredTopics, Is.EqualTo(binder.BrowserSubscriptionTopics));
	}

	[Test]
	public void SampleDescriptor_ContainsNoHardcodedGameResourceNames()
	{
		string json = File.ReadAllText(WebUiResourceAttributeSampleCatalog.SampleDescriptorPath());
		string[] forbidden =
		[
			"Minerals", "Vespene", "Credits", "Power", "Ore", "Lumber", "Gold", "Food", "Supply",
			"StarCraft", "CK3", "Stellaris"
		];

		foreach (string token in forbidden)
		{
			Assert.That(json, Does.Not.Contain(token), $"Sample descriptor must not hardcode '{token}'.");
		}
	}

	private static WebUiResourceAttributeDescriptor LoadSampleDescriptor(bool requireGraphKey)
	{
		WebUiResourceAttributeReferenceCatalog catalog = WebUiResourceAttributeSampleCatalog.Create(
			IsAttributeRegistered,
			requireGraphKey
				? key => string.Equals(key, "graph.output.sample.player.total", StringComparison.Ordinal)
				: null);
		return WebUiResourceAttributeDescriptorLoader.LoadFromFile(
			WebUiResourceAttributeSampleCatalog.SampleDescriptorPath(),
			catalog);
	}

	private static void RegisterSampleAttributes()
	{
		AttributeRegistry.Register("attr.sample.primary");
		AttributeRegistry.Register("attr.sample.derived");
	}

	private static bool IsAttributeRegistered(string name)
	{
		return AttributeRegistry.GetId(name) != AttributeRegistry.InvalidId;
	}

	private static JsonElement FindValue(JsonElement values, string fieldId)
	{
		foreach (JsonElement value in values.EnumerateArray())
		{
			if (string.Equals(value.GetProperty("fieldId").GetString(), fieldId, StringComparison.Ordinal))
			{
				return value;
			}
		}

		throw new AssertionException($"Missing value field '{fieldId}'.");
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
