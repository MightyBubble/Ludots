using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.GAS;
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
	public void LoadSampleDescriptor_BindsProgressionNodesWithoutGameFlavorNames()
	{
		RegisterSampleProgressionIds();
		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();

		Assert.That(descriptor.DescriptorId, Is.EqualTo(WebUiTechTreeSampleCatalog.DescriptorId));
		Assert.That(descriptor.ProfileId, Is.EqualTo(WebUiTechTreeSampleCatalog.ProfileId));
		Assert.That(descriptor.LayoutId, Is.EqualTo(WebUiTechTreeSampleCatalog.LayoutId));
		Assert.That(descriptor.Nodes, Has.Count.EqualTo(2));
		Assert.That(descriptor.Nodes[0].NodeId, Is.EqualTo("node.root"));
		Assert.That(descriptor.Nodes[1].PrerequisiteProgressionIds, Is.EqualTo(new[]
		{
			WebUiTechTreeSampleCatalog.RootProgressionId
		}));
	}

	[Test]
	public void TopicProducer_PrerequisiteMet_ProjectsAvailableStatusAndFormalAction()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKeyId);

		int rootProgressionId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootProgressionId);
		int branchProgressionId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.BranchProgressionId);
		int rootReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootRequirementId);
		int branchReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.BranchRequirementId);

		var requirements = new ProgressionRequirementRegistry();
		requirements.Register(rootReqId, CreateAlwaysTrueRequirement(rootReqId));
		requirements.Register(
			branchReqId,
			CreateProgressionCompletedRequirement(branchReqId, rootProgressionId));

		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
		var progressions = CreateSampleProgressionDefinitions();
		Entity scopeHost = world.Create(new ProgressionStateBuffer());
		Entity actor = world.Create();
		PrepareScopeHost(world, scopeHost);
		PrepareScopeMember(world, actor);
		Assert.That(evaluator.TryBindScope(actor, WebUiTechTreeSampleCatalog.ScopeKeyId, scopeHost), Is.True);
		Assert.That(evaluator.TryComplete(scopeHost, rootProgressionId), Is.True);

		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();
		var producer = new WebUiTechTreeTopicProducer(
			WebUiTechTreeSampleCatalog.Topic,
			world,
			actor,
			scopeHost,
			descriptor,
			progressions,
			evaluator,
			scopeKeys,
			isActionRegistered: id => string.Equals(id, WebUiTechTreeSampleCatalog.ResearchActionId, StringComparison.Ordinal));

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(producer);
		Assert.That(runtime.IsTopicRegistered(WebUiTechTreeSampleCatalog.Topic), Is.True);

		var context = new WebUiTopicContext("session-a", producer.Topic, 3, JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		Assert.That(packet.ContentType, Is.EqualTo(WebUiTechTreeTopicProducer.JsonContentType));

		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement root = document.RootElement;
		Assert.That(root.GetProperty("scopeHost").GetProperty("entityId").GetInt32(), Is.EqualTo(scopeHost.Id));
		Assert.That(root.GetProperty("actor").GetProperty("entityId").GetInt32(), Is.EqualTo(actor.Id));
		Assert.That(root.GetProperty("descriptor").GetString(), Is.EqualTo(descriptor.DescriptorId));
		Assert.That(root.GetProperty("profileId").GetString(), Is.EqualTo(WebUiTechTreeSampleCatalog.ProfileId));
		Assert.That(root.GetProperty("layoutId").GetString(), Is.EqualTo(WebUiTechTreeSampleCatalog.LayoutId));
		Assert.That(root.GetProperty("revision").GetUInt32(), Is.GreaterThan(0u));

		JsonElement nodes = root.GetProperty("nodes");
		Assert.That(nodes.GetArrayLength(), Is.EqualTo(2));

		JsonElement rootNode = FindNode(nodes, "node.root");
		Assert.That(rootNode.GetProperty("status").GetString(), Is.EqualTo("completed"));
		Assert.That(rootNode.GetProperty("level").GetInt32(), Is.EqualTo(1));
		Assert.That(rootNode.TryGetProperty("action", out _), Is.False);

		JsonElement branchNode = FindNode(nodes, "node.branch");
		Assert.That(branchNode.GetProperty("status").GetString(), Is.EqualTo("available"));
		Assert.That(branchNode.GetProperty("progressionId").GetString(), Is.EqualTo(WebUiTechTreeSampleCatalog.BranchProgressionId));
		Assert.That(branchNode.GetProperty("action").GetProperty("actionKind").GetString(), Is.EqualTo("command"));
		Assert.That(branchNode.GetProperty("action").GetProperty("actionId").GetString(), Is.EqualTo(WebUiTechTreeSampleCatalog.ResearchActionId));
		Assert.That(branchNode.GetProperty("prerequisites")[0].GetProperty("progressionId").GetString(), Is.EqualTo(WebUiTechTreeSampleCatalog.RootProgressionId));
		Assert.That(branchNode.GetProperty("prerequisites")[0].GetProperty("completed").GetBoolean(), Is.True);
		if (branchNode.TryGetProperty("blockedReasonTokenId", out JsonElement blocked))
		{
			Assert.That(blocked.ValueKind, Is.EqualTo(JsonValueKind.Null));
		}

		_ = branchProgressionId;
	}

	[Test]
	public void TopicProducer_PrerequisiteMissing_ProjectsLockedWithBlockedReasonToken()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKeyId);

		int rootProgressionId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootProgressionId);
		int rootReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootRequirementId);
		int branchReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.BranchRequirementId);

		var requirements = new ProgressionRequirementRegistry();
		requirements.Register(rootReqId, CreateAlwaysTrueRequirement(rootReqId));
		requirements.Register(
			branchReqId,
			CreateProgressionCompletedRequirement(branchReqId, rootProgressionId));

		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
		var progressions = CreateSampleProgressionDefinitions();
		Entity scopeHost = world.Create(new ProgressionStateBuffer());
		Entity actor = world.Create();
		PrepareScopeHost(world, scopeHost);
		PrepareScopeMember(world, actor);
		Assert.That(evaluator.TryBindScope(actor, WebUiTechTreeSampleCatalog.ScopeKeyId, scopeHost), Is.True);

		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();
		var producer = new WebUiTechTreeTopicProducer(
			WebUiTechTreeSampleCatalog.Topic,
			world,
			actor,
			scopeHost,
			descriptor,
			progressions,
			evaluator,
			scopeKeys,
			isActionRegistered: id => string.Equals(id, WebUiTechTreeSampleCatalog.ResearchActionId, StringComparison.Ordinal));

		WebUiTechTreeSnapshot snapshot = producer.CreateSnapshot();
		WebUiTechTreeNodePayload branch = snapshot.Nodes.Single(node => node.NodeId == "node.branch");
		Assert.That(branch.Status, Is.EqualTo(WebUiTechTreeNodeStatus.Locked));
		Assert.That(branch.BlockedReasonTokenId, Is.EqualTo(WebUiTechTreeSampleCatalog.BlockedTokenBranch));
		Assert.That(branch.Action, Is.Null);
		Assert.That(branch.Prerequisites[0].Completed, Is.False);

		WebUiTechTreeNodePayload rootNode = snapshot.Nodes.Single(node => node.NodeId == "node.root");
		Assert.That(rootNode.Status, Is.EqualTo(WebUiTechTreeNodeStatus.Available));
		Assert.That(rootNode.Action, Is.Not.Null);
		Assert.That(rootNode.Action!.ActionKind, Is.EqualTo(WebUiTechTreeActionKind.Progression));

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(producer);
		var context = new WebUiTopicContext("session-locked", producer.Topic, 1, JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement nodes = document.RootElement.GetProperty("nodes");
		Assert.That(FindNode(nodes, "node.branch").GetProperty("status").GetString(), Is.EqualTo("locked"));
		Assert.That(FindNode(nodes, "node.root").GetProperty("status").GetString(), Is.EqualTo("available"));
		Assert.That(
			FindNode(nodes, "node.root").GetProperty("action").GetProperty("actionKind").GetString(),
			Is.EqualTo("progression"));
	}

	[Test]
	public void TopicProducer_RevisionChanges_WhenProgressionStateBufferMutates()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKeyId);

		int rootProgressionId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootProgressionId);
		int rootReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootRequirementId);
		int branchReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.BranchRequirementId);

		var requirements = new ProgressionRequirementRegistry();
		requirements.Register(rootReqId, CreateAlwaysTrueRequirement(rootReqId));
		requirements.Register(
			branchReqId,
			CreateProgressionCompletedRequirement(branchReqId, rootProgressionId));

		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
		var progressions = CreateSampleProgressionDefinitions();
		Entity scopeHost = world.Create(new ProgressionStateBuffer());
		Entity actor = world.Create();
		PrepareScopeHost(world, scopeHost);
		PrepareScopeMember(world, actor);
		Assert.That(evaluator.TryBindScope(actor, WebUiTechTreeSampleCatalog.ScopeKeyId, scopeHost), Is.True);

		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();
		var producer = new WebUiTechTreeTopicProducer(
			WebUiTechTreeSampleCatalog.Topic,
			world,
			actor,
			scopeHost,
			descriptor,
			progressions,
			evaluator,
			scopeKeys);

		uint before = producer.CreateSnapshot().Revision;
		Assert.That(evaluator.TryComplete(scopeHost, rootProgressionId), Is.True);
		uint after = producer.CreateSnapshot().Revision;

		Assert.That(after, Is.Not.EqualTo(before));
		WebUiTechTreeNodePayload branch = producer.CreateSnapshot().Nodes.Single(node => node.NodeId == "node.branch");
		Assert.That(branch.Status, Is.EqualTo(WebUiTechTreeNodeStatus.Available));
	}

	[Test]
	public void TopicProducer_MissingProgressionStateBuffer_FailsFast()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKeyId);

		int rootReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootRequirementId);
		int branchReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.BranchRequirementId);
		int rootProgressionId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootProgressionId);

		var requirements = new ProgressionRequirementRegistry();
		requirements.Register(rootReqId, CreateAlwaysTrueRequirement(rootReqId));
		requirements.Register(
			branchReqId,
			CreateProgressionCompletedRequirement(branchReqId, rootProgressionId));

		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
		var progressions = CreateSampleProgressionDefinitions();
		Entity scopeHost = world.Create();
		Entity actor = world.Create();
		PrepareScopeHost(world, scopeHost);
		PrepareScopeMember(world, actor);
		Assert.That(evaluator.TryBindScope(actor, WebUiTechTreeSampleCatalog.ScopeKeyId, scopeHost), Is.True);

		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();
		var producer = new WebUiTechTreeTopicProducer(
			WebUiTechTreeSampleCatalog.Topic,
			world,
			actor,
			scopeHost,
			descriptor,
			progressions,
			evaluator,
			scopeKeys);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => producer.CreateSnapshot())!;
		Assert.That(ex.Message, Does.Contain("ProgressionStateBuffer"));
		Assert.That(ex.Message, Does.Contain(scopeHost.Id.ToString()));
	}

	[Test]
	public void TopicProducer_MissingProgressionDefinition_FailsFastWithConcreteId()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKeyId);

		int rootReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootRequirementId);
		int branchReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.BranchRequirementId);
		int rootProgressionId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootProgressionId);

		var requirements = new ProgressionRequirementRegistry();
		requirements.Register(rootReqId, CreateAlwaysTrueRequirement(rootReqId));
		requirements.Register(
			branchReqId,
			CreateProgressionCompletedRequirement(branchReqId, rootProgressionId));

		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
		var progressions = new ProgressionDefinitionRegistry();
		RegisterSampleProgressionDefinition(progressions, WebUiTechTreeSampleCatalog.RootProgressionId);
		// Branch progression id is registered, but its definition is intentionally omitted.

		Entity scopeHost = world.Create(new ProgressionStateBuffer());
		Entity actor = world.Create();
		PrepareScopeHost(world, scopeHost);
		PrepareScopeMember(world, actor);
		Assert.That(evaluator.TryBindScope(actor, WebUiTechTreeSampleCatalog.ScopeKeyId, scopeHost), Is.True);

		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			_ = new WebUiTechTreeTopicProducer(
				WebUiTechTreeSampleCatalog.Topic,
				world,
				actor,
				scopeHost,
				descriptor,
				progressions,
				evaluator,
				scopeKeys))!;

		Assert.That(ex.Message, Does.Contain("unregistered progression definition"));
		Assert.That(ex.Message, Does.Contain(WebUiTechTreeSampleCatalog.BranchProgressionId));
	}

	[Test]
	public void TopicProducer_ActiveCallback_ProjectsActiveStatusWithFormalAction()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKeyId);

		int rootProgressionId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootProgressionId);
		int rootReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootRequirementId);
		int branchReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.BranchRequirementId);

		var requirements = new ProgressionRequirementRegistry();
		requirements.Register(rootReqId, CreateAlwaysTrueRequirement(rootReqId));
		requirements.Register(
			branchReqId,
			CreateProgressionCompletedRequirement(branchReqId, rootProgressionId));

		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
		var progressions = CreateSampleProgressionDefinitions();
		Entity scopeHost = world.Create(new ProgressionStateBuffer());
		Entity actor = world.Create();
		PrepareScopeHost(world, scopeHost);
		PrepareScopeMember(world, actor);
		Assert.That(evaluator.TryBindScope(actor, WebUiTechTreeSampleCatalog.ScopeKeyId, scopeHost), Is.True);
		Assert.That(evaluator.TryComplete(scopeHost, rootProgressionId), Is.True);

		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();
		var producer = new WebUiTechTreeTopicProducer(
			WebUiTechTreeSampleCatalog.Topic,
			world,
			actor,
			scopeHost,
			descriptor,
			progressions,
			evaluator,
			scopeKeys,
			isProgressionActive: id => string.Equals(
				id,
				WebUiTechTreeSampleCatalog.BranchProgressionId,
				StringComparison.Ordinal),
			isActionRegistered: id => string.Equals(
				id,
				WebUiTechTreeSampleCatalog.ResearchActionId,
				StringComparison.Ordinal));

		WebUiTechTreeNodePayload branch = producer.CreateSnapshot().Nodes.Single(node => node.NodeId == "node.branch");
		Assert.That(branch.Status, Is.EqualTo(WebUiTechTreeNodeStatus.Active));
		Assert.That(branch.Action, Is.Not.Null);
		Assert.That(branch.Action!.ActionKind, Is.EqualTo(WebUiTechTreeActionKind.Command));
		Assert.That(branch.Action!.ActionId, Is.EqualTo(WebUiTechTreeSampleCatalog.ResearchActionId));

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(producer);
		var context = new WebUiTopicContext("session-active", producer.Topic, 2, JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement branchNode = FindNode(document.RootElement.GetProperty("nodes"), "node.branch");
		Assert.That(branchNode.GetProperty("status").GetString(), Is.EqualTo("active"));
		Assert.That(branchNode.GetProperty("action").GetProperty("actionKind").GetString(), Is.EqualTo("command"));
	}

	[Test]
	public void TopicProducer_AbilityActionKind_SerializesAsLowerCamelCase()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKeyId);

		int rootProgressionId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootProgressionId);
		int rootReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootRequirementId);
		int branchReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.BranchRequirementId);

		var requirements = new ProgressionRequirementRegistry();
		requirements.Register(rootReqId, CreateAlwaysTrueRequirement(rootReqId));
		requirements.Register(
			branchReqId,
			CreateProgressionCompletedRequirement(branchReqId, rootProgressionId));

		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
		var progressions = CreateSampleProgressionDefinitions();
		Entity scopeHost = world.Create(new ProgressionStateBuffer());
		Entity actor = world.Create();
		PrepareScopeHost(world, scopeHost);
		PrepareScopeMember(world, actor);
		Assert.That(evaluator.TryBindScope(actor, WebUiTechTreeSampleCatalog.ScopeKeyId, scopeHost), Is.True);
		Assert.That(evaluator.TryComplete(scopeHost, rootProgressionId), Is.True);

		WebUiTechTreeReferenceCatalog catalog = WebUiTechTreeSampleCatalog.Create();
		string json = """
		{
		  "descriptorId": "panel-kit.test.techtree.ability",
		  "profileId": "profile.techtree.generic",
		  "layoutId": "layout.tree.generic",
		  "localeId": "locale.sample",
		  "nodes": [
		    {
		      "nodeId": "node.root",
		      "progressionId": "progression.sample.root",
		      "scopeKeyId": "scope.sample.host",
		      "unlockRequirementId": "requirement.sample.root.unlock",
		      "titleTokenId": "token.techtree.node.root.title",
		      "bodyTokenId": "token.techtree.node.root.body",
		      "effectTokenId": "token.techtree.node.root.effect",
		      "blockedReasonTokenId": "token.techtree.node.root.blocked",
		      "groupId": "group.a",
		      "sortOrder": 1,
		      "layoutX": 0,
		      "layoutY": 0,
		      "actionKind": "progression",
		      "actionId": "action.progression.research"
		    },
		    {
		      "nodeId": "node.branch",
		      "progressionId": "progression.sample.branch",
		      "scopeKeyId": "scope.sample.host",
		      "unlockRequirementId": "requirement.sample.branch.unlock",
		      "titleTokenId": "token.techtree.node.branch.title",
		      "bodyTokenId": "token.techtree.node.branch.body",
		      "effectTokenId": "token.techtree.node.branch.effect",
		      "blockedReasonTokenId": "token.techtree.node.branch.blocked",
		      "groupId": "group.b",
		      "sortOrder": 2,
		      "layoutX": 1,
		      "layoutY": 0,
		      "actionKind": "ability",
		      "actionId": "action.progression.research",
		      "prerequisiteProgressionIds": [ "progression.sample.root" ]
		    }
		  ]
		}
		""";

		WebUiTechTreeDescriptor descriptor = WebUiTechTreeDescriptorLoader.LoadFromJson(json, catalog, "ability-wire");
		Assert.That(descriptor.Nodes[1].ActionKind, Is.EqualTo(WebUiTechTreeActionKind.Ability));

		var producer = new WebUiTechTreeTopicProducer(
			"panel-kit.test.techtree.ability",
			world,
			actor,
			scopeHost,
			descriptor,
			progressions,
			evaluator,
			scopeKeys,
			isActionRegistered: id => string.Equals(id, WebUiTechTreeSampleCatalog.ResearchActionId, StringComparison.Ordinal));

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(producer);
		var context = new WebUiTopicContext("session-ability", producer.Topic, 4, JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement branchNode = FindNode(document.RootElement.GetProperty("nodes"), "node.branch");
		Assert.That(branchNode.GetProperty("status").GetString(), Is.EqualTo("available"));
		Assert.That(branchNode.GetProperty("action").GetProperty("actionKind").GetString(), Is.EqualTo("ability"));
	}

	[Test]
	public void Load_UnknownActionKind_FailsFastWithConcreteValue()
	{
		RegisterSampleProgressionIds();
		WebUiTechTreeReferenceCatalog catalog = WebUiTechTreeSampleCatalog.Create();

		string json = """
		{
		  "descriptorId": "panel-kit.test.techtree",
		  "profileId": "profile.techtree.generic",
		  "layoutId": "layout.tree.generic",
		  "localeId": "locale.sample",
		  "nodes": [
		    {
		      "nodeId": "node.a",
		      "progressionId": "progression.sample.root",
		      "scopeKeyId": "scope.sample.host",
		      "titleTokenId": "token.techtree.node.root.title",
		      "bodyTokenId": "token.techtree.node.root.body",
		      "effectTokenId": "token.techtree.node.root.effect",
		      "blockedReasonTokenId": "token.techtree.node.root.blocked",
		      "groupId": "group.a",
		      "sortOrder": 1,
		      "layoutX": 0,
		      "layoutY": 0,
		      "actionKind": "techStore",
		      "actionId": "action.progression.research"
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTechTreeDescriptorLoader.LoadFromJson(json, catalog, "action-kind-test"))!;

		Assert.That(ex.Message, Does.Contain("techStore"));
		Assert.That(ex.Message, Does.Contain("actionKind"));
	}

	[Test]
	public void Load_UnknownProgression_FailsFastWithConcreteId()
	{
		RegisterSampleProgressionIds();
		WebUiTechTreeReferenceCatalog catalog = WebUiTechTreeSampleCatalog.Create();

		string json = """
		{
		  "descriptorId": "panel-kit.test.techtree",
		  "profileId": "profile.techtree.generic",
		  "layoutId": "layout.tree.generic",
		  "localeId": "locale.sample",
		  "nodes": [
		    {
		      "nodeId": "node.missing",
		      "progressionId": "progression.missing.node",
		      "scopeKeyId": "scope.sample.host",
		      "titleTokenId": "token.techtree.node.root.title",
		      "bodyTokenId": "token.techtree.node.root.body",
		      "effectTokenId": "token.techtree.node.root.effect",
		      "blockedReasonTokenId": "token.techtree.node.root.blocked",
		      "groupId": "group.a",
		      "sortOrder": 1,
		      "layoutX": 0,
		      "layoutY": 0,
		      "actionKind": "progression",
		      "actionId": "action.progression.research"
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTechTreeDescriptorLoader.LoadFromJson(json, catalog, "progression-test"))!;

		Assert.That(ex.Message, Does.Contain("progression.missing.node"));
		Assert.That(ex.Message, Does.Contain("unknown progression"));
	}

	[Test]
	public void Load_UnknownRequirement_FailsFastWithConcreteId()
	{
		RegisterSampleProgressionIds();
		WebUiTechTreeReferenceCatalog catalog = WebUiTechTreeSampleCatalog.Create();

		string json = """
		{
		  "descriptorId": "panel-kit.test.techtree",
		  "profileId": "profile.techtree.generic",
		  "layoutId": "layout.tree.generic",
		  "localeId": "locale.sample",
		  "nodes": [
		    {
		      "nodeId": "node.a",
		      "progressionId": "progression.sample.root",
		      "scopeKeyId": "scope.sample.host",
		      "unlockRequirementId": "requirement.missing.unlock",
		      "titleTokenId": "token.techtree.node.root.title",
		      "bodyTokenId": "token.techtree.node.root.body",
		      "effectTokenId": "token.techtree.node.root.effect",
		      "blockedReasonTokenId": "token.techtree.node.root.blocked",
		      "groupId": "group.a",
		      "sortOrder": 1,
		      "layoutX": 0,
		      "layoutY": 0,
		      "actionKind": "progression",
		      "actionId": "action.progression.research"
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTechTreeDescriptorLoader.LoadFromJson(json, catalog, "requirement-test"))!;

		Assert.That(ex.Message, Does.Contain("requirement.missing.unlock"));
		Assert.That(ex.Message, Does.Contain("unknown requirement"));
	}

	[Test]
	public void Load_UnknownToken_FailsFastWithConcreteId()
	{
		RegisterSampleProgressionIds();
		WebUiTechTreeReferenceCatalog catalog = WebUiTechTreeSampleCatalog.Create();

		string json = """
		{
		  "descriptorId": "panel-kit.test.techtree",
		  "profileId": "profile.techtree.generic",
		  "layoutId": "layout.tree.generic",
		  "localeId": "locale.sample",
		  "nodes": [
		    {
		      "nodeId": "node.a",
		      "progressionId": "progression.sample.root",
		      "scopeKeyId": "scope.sample.host",
		      "titleTokenId": "token.missing.title",
		      "bodyTokenId": "token.techtree.node.root.body",
		      "effectTokenId": "token.techtree.node.root.effect",
		      "blockedReasonTokenId": "token.techtree.node.root.blocked",
		      "groupId": "group.a",
		      "sortOrder": 1,
		      "layoutX": 0,
		      "layoutY": 0,
		      "actionKind": "progression",
		      "actionId": "action.progression.research"
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTechTreeDescriptorLoader.LoadFromJson(json, catalog, "token-test"))!;

		Assert.That(ex.Message, Does.Contain("token.missing.title"));
		Assert.That(ex.Message, Does.Contain("unknown display token"));
	}

	[Test]
	public void Load_UnknownScope_FailsFastWithConcreteId()
	{
		RegisterSampleProgressionIds();
		WebUiTechTreeReferenceCatalog catalog = WebUiTechTreeSampleCatalog.Create();

		string json = """
		{
		  "descriptorId": "panel-kit.test.techtree",
		  "profileId": "profile.techtree.generic",
		  "layoutId": "layout.tree.generic",
		  "localeId": "locale.sample",
		  "nodes": [
		    {
		      "nodeId": "node.a",
		      "progressionId": "progression.sample.root",
		      "scopeKeyId": "scope.missing.host",
		      "titleTokenId": "token.techtree.node.root.title",
		      "bodyTokenId": "token.techtree.node.root.body",
		      "effectTokenId": "token.techtree.node.root.effect",
		      "blockedReasonTokenId": "token.techtree.node.root.blocked",
		      "groupId": "group.a",
		      "sortOrder": 1,
		      "layoutX": 0,
		      "layoutY": 0,
		      "actionKind": "progression",
		      "actionId": "action.progression.research"
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTechTreeDescriptorLoader.LoadFromJson(json, catalog, "scope-test"))!;

		Assert.That(ex.Message, Does.Contain("scope.missing.host"));
		Assert.That(ex.Message, Does.Contain("unknown scope"));
	}

	[Test]
	public void BrowserSubscriptionTopics_IncludeTechTreeTopic_FromWpkManifest()
	{
		RegisterSampleProgressionIds();
		using World world = World.Create();
		var scopeKeys = new ScopeKeyRegistry();
		scopeKeys.Register(WebUiTechTreeSampleCatalog.ScopeKeyId);

		int rootProgressionId = ProgressionIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootProgressionId);
		int rootReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.RootRequirementId);
		int branchReqId = ProgressionRequirementIdRegistry.GetId(WebUiTechTreeSampleCatalog.BranchRequirementId);
		var requirements = new ProgressionRequirementRegistry();
		requirements.Register(rootReqId, CreateAlwaysTrueRequirement(rootReqId));
		requirements.Register(branchReqId, CreateProgressionCompletedRequirement(branchReqId, rootProgressionId));
		var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
		var progressions = CreateSampleProgressionDefinitions();

		Entity scopeHost = world.Create(new ProgressionStateBuffer());
		Entity actor = world.Create();
		PrepareScopeHost(world, scopeHost);
		PrepareScopeMember(world, actor);
		Assert.That(evaluator.TryBindScope(actor, WebUiTechTreeSampleCatalog.ScopeKeyId, scopeHost), Is.True);

		WebUiTechTreeDescriptor descriptor = LoadSampleDescriptor();
		var techTreeProducer = new WebUiTechTreeTopicProducer(
			WebUiPanelKitSampleCatalog.TechTreeTopic,
			world,
			actor,
			scopeHost,
			descriptor,
			progressions,
			evaluator,
			scopeKeys);

		using var runtime = new WebUiDataPlaneRuntime();
		foreach (string topic in WebUiPanelKitSampleCatalog.SampleTopics)
		{
			if (string.Equals(topic, WebUiPanelKitSampleCatalog.TechTreeTopic, StringComparison.Ordinal))
			{
				runtime.RegisterTopic(techTreeProducer);
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
		Assert.That(binder.BrowserSubscriptionTopics, Does.Contain(WebUiPanelKitSampleCatalog.TechTreeTopic));
		Assert.That(binder.BrowserSubscriptionTopics, Does.Not.Contain("panel-kit.extra.unrelated"));
		Assert.That(manifest.DeclaredTopics, Is.EqualTo(binder.BrowserSubscriptionTopics));
	}

	[Test]
	public void SampleDescriptor_ContainsNoHardcodedGameFlavorNames()
	{
		string json = File.ReadAllText(WebUiTechTreeSampleCatalog.SampleDescriptorPath());
		string[] forbidden =
		[
			"科技", "时代", "传统", "法令", "飞升",
			"Technology", "Tradition", "Edict", "Ascension",
			"StarCraft", "CK3", "Stellaris", "Age of Empires"
		];

		foreach (string token in forbidden)
		{
			Assert.That(json, Does.Not.Contain(token), $"Sample descriptor must not hardcode '{token}'.");
		}
	}


	private static ProgressionDefinitionRegistry CreateSampleProgressionDefinitions()
	{
		var progressions = new ProgressionDefinitionRegistry();
		RegisterSampleProgressionDefinition(progressions, WebUiTechTreeSampleCatalog.RootProgressionId);
		RegisterSampleProgressionDefinition(progressions, WebUiTechTreeSampleCatalog.BranchProgressionId);
		return progressions;
	}

	private static void RegisterSampleProgressionDefinition(
		ProgressionDefinitionRegistry progressions,
		string progressionName)
	{
		int progressionId = ProgressionIdRegistry.GetId(progressionName);
		var definition = new ProgressionDefinition
		{
			ProgressionId = progressionId,
			DeclaredScope = ScopeKey.Explicit()
		};
		progressions.Register(progressionId, in definition);
	}

	private static WebUiTechTreeDescriptor LoadSampleDescriptor()
	{
		return WebUiTechTreeDescriptorLoader.LoadFromFile(
			WebUiTechTreeSampleCatalog.SampleDescriptorPath(),
			WebUiTechTreeSampleCatalog.Create());
	}

	private static void RegisterSampleProgressionIds()
	{
		ProgressionIdRegistry.Register(WebUiTechTreeSampleCatalog.RootProgressionId);
		ProgressionIdRegistry.Register(WebUiTechTreeSampleCatalog.BranchProgressionId);
		ProgressionRequirementIdRegistry.Register(WebUiTechTreeSampleCatalog.RootRequirementId);
		ProgressionRequirementIdRegistry.Register(WebUiTechTreeSampleCatalog.BranchRequirementId);
	}

	private static ProgressionRequirementDefinition CreateAlwaysTrueRequirement(int requirementId)
	{
		var nodes = new[]
		{
			new ProgressionRequirementNode(
				ProgressionRequirementNodeKind.None,
				ScopeKey.Explicit(),
				RoleSlot.ScopeHost,
				firstChild: 0,
				childCount: 0,
				progressionId: 0,
				requiredCount: 0,
				graphProgramId: 0,
				requiredTags: default)
		};
		return new ProgressionRequirementDefinition(requirementId, nodes, Array.Empty<int>());
	}

	private static ProgressionRequirementDefinition CreateProgressionCompletedRequirement(
		int requirementId,
		int progressionId)
	{
		var nodes = new[]
		{
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
		};
		return new ProgressionRequirementDefinition(requirementId, nodes, Array.Empty<int>());
	}

	private static void PrepareScopeHost(World world, Entity entity)
	{
		if (!world.Has<ScopeMembershipRevision>(entity))
		{
			world.Add(entity, new ScopeMembershipRevision());
		}
	}

	private static void PrepareScopeMember(World world, Entity entity)
	{
		if (!world.Has<ScopeRefBuffer>(entity))
		{
			world.Add(entity, new ScopeRefBuffer());
		}

		if (!world.Has<ScopeMemberTag>(entity))
		{
			world.Add(entity, new ScopeMemberTag());
		}
	}

	private static JsonElement FindNode(JsonElement nodes, string nodeId)
	{
		foreach (JsonElement node in nodes.EnumerateArray())
		{
			if (string.Equals(node.GetProperty("nodeId").GetString(), nodeId, StringComparison.Ordinal))
			{
				return node;
			}
		}

		throw new AssertionException($"Missing TechTree node '{nodeId}'.");
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
