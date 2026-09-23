using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using Ludots.WebUI.DataPlane;
using Ludots.WebUI.PanelKit;
using NUnit.Framework;

namespace Ludots.Tests.WebUiPanelKit;

[TestFixture]
public sealed class WebUiTechTreePanelTests
{
	[SetUp]
	public void SetUp()
	{
		ProgressionIdRegistry.Clear();
		ProgressionRequirementIdRegistry.Clear();
	}

	[TearDown]
	public void TearDown()
	{
		ProgressionIdRegistry.Clear();
		ProgressionRequirementIdRegistry.Clear();
	}

	[Test]
	public void LoadSampleDescriptor_ValidatesNodesAndReferences()
	{
		RegisterSampleProgressionIds();
		WebUiTechTreeReferenceCatalog catalog = CreateSampleCatalog(commandRegistered: true);
		WebUiTechTreeDescriptor descriptor = WebUiTechTreeDescriptorLoader.LoadFromFile(
			WebUiTechTreeSampleCatalog.SampleDescriptorPath(),
			catalog);

		Assert.That(descriptor.DescriptorId, Is.EqualTo(WebUiTechTreeSampleCatalog.DescriptorId));
		Assert.That(descriptor.ProfileId, Is.EqualTo(WebUiTechTreeSampleCatalog.ProfileId));
		Assert.That(descriptor.LayoutId, Is.EqualTo(WebUiTechTreeSampleCatalog.LayoutId));
		Assert.That(descriptor.ScopeKey, Is.EqualTo(WebUiTechTreeSampleCatalog.ScopeKey));
		Assert.That(descriptor.Nodes, Has.Count.EqualTo(2));
		Assert.That(descriptor.Nodes[0].NodeId, Is.EqualTo("node.root"));
		Assert.That(descriptor.Nodes[1].PrerequisiteNodeIds, Is.EqualTo(new[] { "node.root" }));
		Assert.That(descriptor.Nodes[1].RequirementId, Is.EqualTo(WebUiTechTreeSampleCatalog.ChildRequirementId));
		Assert.That(descriptor.Nodes[1].CommandId, Is.EqualTo(WebUiTechTreeSampleCatalog.ResearchCommandId));
	}

	[Test]
	public void TopicProducer_ProjectsStatusFromProgressionStateBufferAndEvaluator()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		int scopeId = scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKey);
		Entity host = world.Create(new ProgressionStateBuffer());
		PrepareScopeHost(world, host);

		var definitions = new ProgressionDefinitionRegistry();
		int rootId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootProgressionId);
		int childId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.ChildProgressionId);
		definitions.Register(rootId, new ProgressionDefinition
		{
			ProgressionId = rootId,
			DeclaredScope = ScopeKey.Named(scopeId)
		});
		definitions.Register(childId, new ProgressionDefinition
		{
			ProgressionId = childId,
			DeclaredScope = ScopeKey.Named(scopeId)
		});

		int requirementId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.ChildRequirementId);
		var requirements = new ProgressionRequirementRegistry();
		requirements.Register(requirementId, CreateCompletedRequirement(requirementId, rootId));
		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);

		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();
		var router = CreateRouter();
		router.Register(WebUiTechTreeSampleCatalog.ResearchCommandId, new RecordingResearchHandler());
		var producer = new WebUiTechTreeTopicProducer(
			WebUiTechTreeSampleCatalog.Topic,
			world,
			host,
			descriptor,
			definitions,
			evaluator,
			scopeKeys,
			router.IsRegistered);

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(producer);
		Assert.That(runtime.IsTopicRegistered(WebUiTechTreeSampleCatalog.Topic), Is.True);

		WebUiTechTreeSnapshot lockedSnapshot = producer.CreateSnapshot();
		WebUiTechTreeNodeSnapshot childLocked = lockedSnapshot.Nodes.Single(node => node.NodeId == "node.child");
		Assert.That(childLocked.Status, Is.EqualTo(nameof(WebUiTechTreeNodeStatus.Locked)));
		Assert.That(childLocked.CommandId, Is.Null);

		Assert.That(evaluator.TryComplete(host, rootId), Is.True);
		WebUiTechTreeSnapshot availableSnapshot = producer.CreateSnapshot();
		WebUiTechTreeNodeSnapshot rootCompleted = availableSnapshot.Nodes.Single(node => node.NodeId == "node.root");
		WebUiTechTreeNodeSnapshot childAvailable = availableSnapshot.Nodes.Single(node => node.NodeId == "node.child");
		Assert.That(rootCompleted.Status, Is.EqualTo(nameof(WebUiTechTreeNodeStatus.Completed)));
		Assert.That(childAvailable.Status, Is.EqualTo(nameof(WebUiTechTreeNodeStatus.Available)));
		Assert.That(childAvailable.CommandId, Is.EqualTo(WebUiTechTreeSampleCatalog.ResearchCommandId));
		Assert.That(availableSnapshot.Revision, Is.GreaterThan(0u));
		Assert.That(availableSnapshot.ScopeKey, Is.EqualTo(WebUiTechTreeSampleCatalog.ScopeKey));
	}

	[Test]
	public async Task ResearchCommand_GoesThroughRegisteredWebUiCommandRouter()
	{
		var handler = new RecordingResearchHandler();
		var router = CreateRouter();
		router.Register(WebUiTechTreeSampleCatalog.ResearchCommandId, handler);

		WebUiInboundPacket packet = new(
			"session-a",
			WebUiTechTreeSampleCatalog.Topic,
			WebUiPacketKind.Command,
			WebUiDeliverySemantics.ReliableOrdered,
			JsonSerializer.SerializeToUtf8Bytes(new WebUiCommandRequest(
				WebUiTechTreeSampleCatalog.ResearchCommandId,
				9,
				Array.Empty<WebUiEntityRef>(),
				JsonSerializer.SerializeToElement(new
				{
					nodeId = "node.child",
					progressionId = WebUiTechTreeSampleCatalog.ChildProgressionId
				})), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
			"application/json",
			RequestId: 1,
			ClientSeq: 9);

		WebUiOutboundPacket response = await router.HandleAsync(packet, TestContext.CurrentContext.CancellationToken);
		Assert.That(response.Kind, Is.EqualTo(WebUiPacketKind.CommandAck));
		Assert.That(handler.Handled, Has.Count.EqualTo(1));
		Assert.That(handler.Handled[0].Name, Is.EqualTo(WebUiTechTreeSampleCatalog.ResearchCommandId));
		Assert.That(handler.Handled[0].Payload.GetProperty("nodeId").GetString(), Is.EqualTo("node.child"));
	}

	[Test]
	public void Load_UnknownProgression_FailsFastWithConcreteId()
	{
		ProgressionIdRegistry.Register(WebUiTechTreeSampleCatalog.RootProgressionId);
		ProgressionRequirementIdRegistry.Register(WebUiTechTreeSampleCatalog.ChildRequirementId);
		WebUiTechTreeReferenceCatalog catalog = CreateSampleCatalog(commandRegistered: true);

		string json = """
		{
		  "descriptorId": "panel-kit.test.techtree",
		  "profileId": "profile.techtree.generic",
		  "layoutId": "layout.techtree.tree",
		  "scopeKey": "scope.sample",
		  "nodes": [
		    {
		      "nodeId": "node.missing",
		      "progressionId": "progression.missing",
		      "displayTokenId": "token.techtree.node.root",
		      "targetLevel": 1,
		      "sortOrder": 1,
		      "layoutSlotId": "slot.a"
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTechTreeDescriptorLoader.LoadFromJson(json, catalog, "progression-test"))!;
		Assert.That(ex.Message, Does.Contain("progression.missing"));
		Assert.That(ex.Message, Does.Contain("unknown progression"));
	}

	[Test]
	public void Load_UnknownCommand_FailsFastWithConcreteId()
	{
		RegisterSampleProgressionIds();
		WebUiTechTreeReferenceCatalog catalog = CreateSampleCatalog(commandRegistered: false);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTechTreeDescriptorLoader.LoadFromFile(WebUiTechTreeSampleCatalog.SampleDescriptorPath(), catalog))!;
		Assert.That(ex.Message, Does.Contain(WebUiTechTreeSampleCatalog.ResearchCommandId));
		Assert.That(ex.Message, Does.Contain("unknown WebUI command"));
	}

	[Test]
	public void Load_UnknownScope_FailsFastWithConcreteId()
	{
		RegisterSampleProgressionIds();
		var catalog = new WebUiTechTreeReferenceCatalog(
			CreateTokenRegistry("display token", WebUiTechTreeSampleCatalog.RootDisplayTokenId, WebUiTechTreeSampleCatalog.ChildDisplayTokenId),
			CreateTokenRegistry("blocked reason token", WebUiTechTreeSampleCatalog.BlockedReasonTokenId),
			CreateTokenRegistry("profile", WebUiTechTreeSampleCatalog.ProfileId),
			CreateTokenRegistry("layout", WebUiTechTreeSampleCatalog.LayoutId),
			name => ProgressionIdRegistry.GetId(name) != ProgressionIdRegistry.InvalidId,
			name => ProgressionRequirementIdRegistry.GetId(name) != ProgressionRequirementIdRegistry.InvalidId,
			_ => false,
			_ => true);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTechTreeDescriptorLoader.LoadFromFile(WebUiTechTreeSampleCatalog.SampleDescriptorPath(), catalog))!;
		Assert.That(ex.Message, Does.Contain(WebUiTechTreeSampleCatalog.ScopeKey));
		Assert.That(ex.Message, Does.Contain("unknown scope"));
	}

	[Test]
	public void Produce_MissingProgressionDefinition_FailsFast()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKey);
		Entity host = world.Create(new ProgressionStateBuffer());
		var definitions = new ProgressionDefinitionRegistry();
		var requirements = new ProgressionRequirementRegistry();
		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			_ = new WebUiTechTreeTopicProducer(
				WebUiTechTreeSampleCatalog.Topic,
				world,
				host,
				descriptor,
				definitions,
				evaluator,
				scopeKeys,
				_ => true))!;
		Assert.That(ex.Message, Does.Contain("ProgressionDefinitionRegistry"));
		Assert.That(ex.Message, Does.Contain(WebUiTechTreeSampleCatalog.RootProgressionId));
	}

	[Test]
	public void GenericPanelKitCode_ContainsNoTechTreeStoreOrGameTechNames()
	{
		string repoRoot = FindRepoRoot();
		string[] files =
		[
			Path.Combine(repoRoot, "src", "Libraries", "Ludots.WebUI.PanelKit", "WebUiTechTreeContracts.cs"),
			Path.Combine(repoRoot, "src", "Libraries", "Ludots.WebUI.PanelKit", "WebUiTechTreeDescriptorLoader.cs"),
			Path.Combine(repoRoot, "src", "Libraries", "Ludots.WebUI.PanelKit", "WebUiTechTreeTopicProducer.cs"),
			Path.Combine(repoRoot, "src", "Libraries", "Ludots.WebUI.PanelKit", "WebUiTechTreeSampleCatalog.cs"),
			Path.Combine(repoRoot, "src", "Libraries", "Ludots.WebUI.PanelKit", "WebUiTechTreePanelDescriptors.cs")
		];

		string[] forbidden =
		[
			"Stellaris",
			"群星",
			"Age of Empires",
			"StarCraft",
			"Infantry",
			"Marine",
			"Minerals"
		];

		foreach (string file in files)
		{
			string text = File.ReadAllText(file);
			Assert.That(text, Does.Not.Contain("class TechTreeStore"));
			Assert.That(text, Does.Not.Contain("new TechTreeStore"));
			foreach (string token in forbidden)
			{
				Assert.That(text, Does.Not.Contain(token), $"{Path.GetFileName(file)} must not hardcode '{token}'.");
			}

			Assert.That(text, Does.Not.Contain("\"Unknown\""), $"{Path.GetFileName(file)} must not use Unknown string fallback.");
		}
	}

	[Test]
	public void SampleManifest_IncludesTechTreePanelTopic()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		foreach (string topic in WebUiPanelKitSampleCatalog.SampleTopics)
		{
			runtime.RegisterTopic(new StubTopicProducer(topic));
		}

		WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromFile(
			WebUiPanelKitSampleCatalog.SampleManifestPath(),
			WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered));

		Assert.That(manifest.DeclaredTopics, Does.Contain(WebUiTechTreePanelDescriptors.SampleTopic));
		Assert.That(
			manifest.Panels.Any(panel =>
				panel.PanelType == WebUiTechTreePanelDescriptors.PanelType &&
				panel.ProfileId == WebUiTechTreePanelDescriptors.GenericProfileId),
			Is.True);
	}

	private static WebUiTechTreeDescriptor LoadSampleDescriptor()
	{
		return WebUiTechTreeDescriptorLoader.LoadFromFile(
			WebUiTechTreeSampleCatalog.SampleDescriptorPath(),
			CreateSampleCatalog(commandRegistered: true));
	}

	private static WebUiTechTreeReferenceCatalog CreateSampleCatalog(bool commandRegistered)
	{
		return WebUiTechTreeSampleCatalog.Create(
			name => ProgressionIdRegistry.GetId(name) != ProgressionIdRegistry.InvalidId,
			name => ProgressionRequirementIdRegistry.GetId(name) != ProgressionRequirementIdRegistry.InvalidId,
			name => string.Equals(name, WebUiTechTreeSampleCatalog.ScopeKey, StringComparison.Ordinal),
			_ => commandRegistered);
	}

	private static void RegisterSampleProgressionIds()
	{
		ProgressionIdRegistry.Register(WebUiTechTreeSampleCatalog.RootProgressionId);
		ProgressionIdRegistry.Register(WebUiTechTreeSampleCatalog.ChildProgressionId);
		ProgressionRequirementIdRegistry.Register(WebUiTechTreeSampleCatalog.ChildRequirementId);
	}

	private static ProgressionRequirementDefinition CreateCompletedRequirement(int requirementId, int progressionId)
	{
		return new ProgressionRequirementDefinition(
			requirementId,
			[
				new ProgressionRequirementNode(
					ProgressionRequirementNodeKind.ProgressionCompleted,
					ScopeKey.Explicit(),
					RoleSlot.ScopeHost,
					firstChild: 0,
					childCount: 0,
					progressionId,
					requiredCount: 1,
					graphProgramId: 0,
					requiredTags: default)
			],
			Array.Empty<int>());
	}

	private static void PrepareScopeHost(World world, Entity entity)
	{
		if (!world.Has<ScopeMembershipRevision>(entity))
		{
			world.Add(entity, new ScopeMembershipRevision());
		}
	}

	private static WebUiCommandRouter CreateRouter()
	{
		return new WebUiCommandRouter(new AlwaysCurrentEntities(), new AllowAllPermissions());
	}

	private static WebUiPanelIdRegistry CreateTokenRegistry(string kind, params string[] ids)
	{
		var registry = new WebUiPanelIdRegistry(kind);
		registry.RegisterAll(ids);
		return registry;
	}

	private static string FindRepoRoot()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
			    Directory.Exists(Path.Combine(current.FullName, "src")) &&
			    Directory.Exists(Path.Combine(current.FullName, "mods")))
			{
				return current.FullName;
			}

			current = current.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
	}

	private sealed class RecordingResearchHandler : IWebUiCommandHandler
	{
		public List<WebUiCommandRequest> Handled { get; } = new();

		public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
		{
			Handled.Add(request);
			return ValueTask.FromResult(WebUiCommandResult.Ok());
		}
	}

	private sealed class AlwaysCurrentEntities : IWebUiEntityGenerationResolver
	{
		public bool IsCurrent(WebUiEntityRef entityRef) => true;
	}

	private sealed class AllowAllPermissions : IWebUiCommandPermissionValidator
	{
		public bool CanUse(WebUiCommandRequest request, out string error)
		{
			error = string.Empty;
			return true;
		}
	}

	private sealed class StubTopicProducer : IWebUiTopicProducer
	{
		public StubTopicProducer(string topic) => Topic = topic;
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
}
