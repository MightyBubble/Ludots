using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.WebUI.DataPlane;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// DataPlane topic producer for a TechTree / Progression panel. Projects ProgressionDefinitionRegistry,
/// ProgressionStateBuffer, and ProgressionRequirementEvaluator — never invents TechTreeStore or
/// browser-side tech state.
/// </summary>
public sealed class WebUiTechTreeTopicProducer : IWebUiTopicProducer
{
	public const string JsonContentType = "application/json+ludots-techtree";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	private readonly World _world;
	private readonly Entity _actor;
	private readonly Entity _scopeHost;
	private readonly WebUiTechTreeDescriptor _descriptor;
	private readonly ProgressionDefinitionRegistry _progressions;
	private readonly ProgressionRequirementEvaluator _evaluator;
	private readonly ScopeKeyRegistry _scopeKeys;
	private readonly Func<string, int> _resolveProgressionId;
	private readonly Func<string, int> _resolveRequirementId;
	private readonly Func<string, bool>? _isProgressionActive;
	private readonly Func<string, bool>? _isActionRegistered;
	private uint _revision;

	public WebUiTechTreeTopicProducer(
		string topic,
		World world,
		Entity actor,
		Entity scopeHost,
		WebUiTechTreeDescriptor descriptor,
		ProgressionDefinitionRegistry progressions,
		ProgressionRequirementEvaluator evaluator,
		ScopeKeyRegistry scopeKeys,
		Func<string, int>? resolveProgressionId = null,
		Func<string, int>? resolveRequirementId = null,
		Func<string, bool>? isProgressionActive = null,
		Func<string, bool>? isActionRegistered = null)
	{
		Topic = string.IsNullOrWhiteSpace(topic)
			? throw new ArgumentException("Topic is required.", nameof(topic))
			: topic.Trim();
		_world = world ?? throw new ArgumentNullException(nameof(world));
		if (actor == Entity.Null)
		{
			throw new ArgumentException("Actor entity is required.", nameof(actor));
		}

		if (scopeHost == Entity.Null)
		{
			throw new ArgumentException("Scope host entity is required.", nameof(scopeHost));
		}

		_actor = actor;
		_scopeHost = scopeHost;
		_descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
		_progressions = progressions ?? throw new ArgumentNullException(nameof(progressions));
		_evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
		_scopeKeys = scopeKeys ?? throw new ArgumentNullException(nameof(scopeKeys));
		_resolveProgressionId = resolveProgressionId ?? ProgressionIdRegistry.GetId;
		_resolveRequirementId = resolveRequirementId ?? ProgressionRequirementIdRegistry.GetId;
		_isProgressionActive = isProgressionActive;
		_isActionRegistered = isActionRegistered;

		EnsureDescriptorCanProduce();
	}

	public string Topic { get; }
	public string DescriptorId => _descriptor.DescriptorId;
	public Entity Actor => _actor;
	public Entity ScopeHost => _scopeHost;
	public uint Revision => _revision;

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		WebUiTechTreeSnapshot snapshot = CreateSnapshot();
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			JsonContentType,
			context.RequestId);
		return true;
	}

	public WebUiTechTreeSnapshot CreateSnapshot()
	{
		if (!_world.IsAlive(_actor))
		{
			throw new InvalidOperationException(
				$"TechTree topic '{Topic}' actor entity {_actor.Id} is not alive.");
		}

		if (!_world.IsAlive(_scopeHost))
		{
			throw new InvalidOperationException(
				$"TechTree topic '{Topic}' scope host entity {_scopeHost.Id} is not alive.");
		}

		if (!_world.TryGet(_scopeHost, out ProgressionStateBuffer state))
		{
			throw new InvalidOperationException(
				$"TechTree topic '{Topic}' scope host {_scopeHost.Id} is missing ProgressionStateBuffer.");
		}

		var roleContext = new RoleResolverContext(
			actor: _actor,
			subject: _actor,
			explicitScopeHost: _scopeHost);

		var nodes = new WebUiTechTreeNodePayload[_descriptor.Nodes.Count];
		uint fieldRevision = state.Revision;
		for (int i = 0; i < _descriptor.Nodes.Count; i++)
		{
			WebUiTechTreeNode node = _descriptor.Nodes[i];
			nodes[i] = ProjectNode(node, in state, in roleContext, out uint contribution);
			fieldRevision ^= contribution + ((uint)(i + 1) * 397u);
		}

		_revision++;
		uint revision = _revision ^ fieldRevision;
		return new WebUiTechTreeSnapshot(
			ScopeHost: new WebUiTechTreeEntityRef(_scopeHost.Id, _scopeHost.WorldId, _scopeHost.Version),
			Actor: new WebUiTechTreeEntityRef(_actor.Id, _actor.WorldId, _actor.Version),
			Descriptor: _descriptor.DescriptorId,
			ProfileId: _descriptor.ProfileId,
			LayoutId: _descriptor.LayoutId,
			LocaleId: _descriptor.LocaleId,
			Revision: revision,
			Nodes: nodes);
	}

	private void EnsureDescriptorCanProduce()
	{
		foreach (WebUiTechTreeNode node in _descriptor.Nodes)
		{
			RequireProgressionDefinition(node.NodeId, node.ProgressionId);

			if (!_scopeKeys.TryGetId(node.ScopeKeyId, out int scopeKeyId) || scopeKeyId <= 0)
			{
				throw new InvalidOperationException(
					$"TechTree descriptor '{_descriptor.DescriptorId}' node '{node.NodeId}' references unknown scope '{node.ScopeKeyId}'.");
			}

			if (node.UnlockRequirementId != null)
			{
				int requirementId = _resolveRequirementId(node.UnlockRequirementId);
				if (requirementId == ProgressionRequirementIdRegistry.InvalidId || requirementId <= 0)
				{
					throw new InvalidOperationException(
						$"TechTree descriptor '{_descriptor.DescriptorId}' node '{node.NodeId}' references unknown requirement '{node.UnlockRequirementId}'.");
				}
			}

			foreach (string prereq in node.PrerequisiteProgressionIds)
			{
				RequireProgressionDefinition(node.NodeId, prereq, prerequisite: true);
			}

			if (_isActionRegistered != null && !_isActionRegistered(node.ActionId))
			{
				throw new InvalidOperationException(
					$"TechTree descriptor '{_descriptor.DescriptorId}' node '{node.NodeId}' references unknown action '{node.ActionId}'.");
			}
		}
	}

	private int RequireProgressionDefinition(string nodeId, string progressionName, bool prerequisite = false)
	{
		int progressionId = _resolveProgressionId(progressionName);
		if (progressionId == ProgressionIdRegistry.InvalidId || progressionId <= 0)
		{
			string role = prerequisite ? "prerequisite references" : "references";
			throw new InvalidOperationException(
				$"TechTree descriptor '{_descriptor.DescriptorId}' node '{nodeId}' {role} unknown progression '{progressionName}'.");
		}

		if (!_progressions.TryGet(progressionId, out _))
		{
			string role = prerequisite ? "prerequisite references" : "references";
			throw new InvalidOperationException(
				$"TechTree descriptor '{_descriptor.DescriptorId}' node '{nodeId}' {role} unregistered progression definition '{progressionName}' (id {progressionId}).");
		}

		return progressionId;
	}

	private WebUiTechTreeNodePayload ProjectNode(
		WebUiTechTreeNode node,
		in ProgressionStateBuffer state,
		in RoleResolverContext roleContext,
		out uint contribution)
	{
		int progressionId = RequireProgressionDefinition(node.NodeId, node.ProgressionId);

		int level = state.GetLevel(progressionId);
		bool completed = state.HasCompleted(progressionId);
		bool active = !completed && _isProgressionActive != null && _isProgressionActive(node.ProgressionId);

		bool prerequisitesMet = true;
		var prerequisitePayload = new WebUiTechTreePrerequisitePayload[node.PrerequisiteProgressionIds.Count];
		for (int i = 0; i < node.PrerequisiteProgressionIds.Count; i++)
		{
			string prereqName = node.PrerequisiteProgressionIds[i];
			int prereqId = RequireProgressionDefinition(node.NodeId, prereqName, prerequisite: true);

			bool prereqCompleted = state.HasCompleted(prereqId);
			prerequisitesMet &= prereqCompleted;
			prerequisitePayload[i] = new WebUiTechTreePrerequisitePayload(prereqName, prereqCompleted);
		}

		bool requirementMet = true;
		uint requirementRevision = 0;
		if (node.UnlockRequirementId != null)
		{
			int requirementId = _resolveRequirementId(node.UnlockRequirementId);
			if (requirementId == ProgressionRequirementIdRegistry.InvalidId || requirementId <= 0)
			{
				throw new InvalidOperationException(
					$"TechTree node '{node.NodeId}' references unknown requirement '{node.UnlockRequirementId}'.");
			}

			requirementMet = _evaluator.Evaluate(requirementId, in roleContext);
			requirementRevision = _evaluator.ComputeRevision(requirementId, in roleContext);
		}

		WebUiTechTreeNodeStatus status;
		if (completed)
		{
			status = WebUiTechTreeNodeStatus.Completed;
		}
		else if (active)
		{
			status = WebUiTechTreeNodeStatus.Active;
		}
		else if (prerequisitesMet && requirementMet)
		{
			status = WebUiTechTreeNodeStatus.Available;
		}
		else
		{
			status = WebUiTechTreeNodeStatus.Locked;
		}

		string? blockedReasonTokenId = status == WebUiTechTreeNodeStatus.Locked
			? node.BlockedReasonTokenId
			: null;

		WebUiTechTreeActionPayload? action = null;
		if (status is WebUiTechTreeNodeStatus.Available or WebUiTechTreeNodeStatus.Active)
		{
			action = new WebUiTechTreeActionPayload(node.ActionKind, node.ActionId);
		}

		contribution = (uint)level
			^ ((uint)status * 17u)
			^ requirementRevision
			^ (prerequisitesMet ? 1u : 0u)
			^ (requirementMet ? 2u : 0u);

		return new WebUiTechTreeNodePayload(
			NodeId: node.NodeId,
			ProgressionId: node.ProgressionId,
			ScopeKeyId: node.ScopeKeyId,
			UnlockRequirementId: node.UnlockRequirementId,
			Status: status,
			Level: level,
			TitleTokenId: node.TitleTokenId,
			BodyTokenId: node.BodyTokenId,
			EffectTokenId: node.EffectTokenId,
			BlockedReasonTokenId: blockedReasonTokenId,
			GroupId: node.GroupId,
			SortOrder: node.SortOrder,
			LayoutX: node.LayoutX,
			LayoutY: node.LayoutY,
			TooltipDescriptorId: node.TooltipDescriptorId,
			Prerequisites: prerequisitePayload,
			Action: action);
	}
}

/// <summary>
/// DataPlane payload for a TechTree / Progression panel snapshot.
/// Browser must not maintain independent tech state from this projection.
/// Wire enums serialize as camelCase via JsonStringEnumConverter (locked/available/active/completed,
/// command/ability/progression) — never PascalCase ToString().
/// </summary>
public sealed record WebUiTechTreeSnapshot(
	WebUiTechTreeEntityRef ScopeHost,
	WebUiTechTreeEntityRef Actor,
	string Descriptor,
	string ProfileId,
	string LayoutId,
	string LocaleId,
	uint Revision,
	WebUiTechTreeNodePayload[] Nodes);

public sealed record WebUiTechTreeEntityRef(int EntityId, int WorldId, int Version);

public sealed record WebUiTechTreeNodePayload(
	string NodeId,
	string ProgressionId,
	string ScopeKeyId,
	string? UnlockRequirementId,
	WebUiTechTreeNodeStatus Status,
	int Level,
	string TitleTokenId,
	string BodyTokenId,
	string EffectTokenId,
	string? BlockedReasonTokenId,
	string GroupId,
	int SortOrder,
	float LayoutX,
	float LayoutY,
	string? TooltipDescriptorId,
	WebUiTechTreePrerequisitePayload[] Prerequisites,
	WebUiTechTreeActionPayload? Action);

public sealed record WebUiTechTreePrerequisitePayload(string ProgressionId, bool Completed);

public sealed record WebUiTechTreeActionPayload(WebUiTechTreeActionKind ActionKind, string ActionId);
