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
/// DataPlane topic producer for a TechTree / Progression panel.
/// Reads <see cref="ProgressionStateBuffer"/> and <see cref="ProgressionRequirementEvaluator"/>;
/// never invents TechTreeStore and never keeps browser-owned progression state.
/// </summary>
public sealed class WebUiTechTreeTopicProducer : IWebUiTopicProducer
{
	public const string JsonContentType = "application/json+ludots-techtree";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly World _world;
	private readonly Entity _scopeHost;
	private readonly Entity _actor;
	private readonly WebUiTechTreeDescriptor _descriptor;
	private readonly ProgressionDefinitionRegistry _definitions;
	private readonly ProgressionRequirementEvaluator _evaluator;
	private readonly ScopeKeyRegistry _scopeKeys;
	private readonly Func<string, bool> _isCommandRegistered;
	private readonly Func<string, int> _resolveProgressionId;
	private readonly Func<string, int> _resolveRequirementId;
	private uint _revision;

	public WebUiTechTreeTopicProducer(
		string topic,
		World world,
		Entity scopeHost,
		WebUiTechTreeDescriptor descriptor,
		ProgressionDefinitionRegistry definitions,
		ProgressionRequirementEvaluator evaluator,
		ScopeKeyRegistry scopeKeys,
		Func<string, bool> isCommandRegistered,
		Entity actor = default,
		Func<string, int>? resolveProgressionId = null,
		Func<string, int>? resolveRequirementId = null)
	{
		Topic = string.IsNullOrWhiteSpace(topic)
			? throw new ArgumentException("Topic is required.", nameof(topic))
			: topic.Trim();
		_world = world ?? throw new ArgumentNullException(nameof(world));
		if (scopeHost == Entity.Null)
		{
			throw new ArgumentException("Scope host entity is required.", nameof(scopeHost));
		}

		_scopeHost = scopeHost;
		_actor = actor == Entity.Null ? scopeHost : actor;
		_descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
		_definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
		_evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
		_scopeKeys = scopeKeys ?? throw new ArgumentNullException(nameof(scopeKeys));
		_isCommandRegistered = isCommandRegistered ?? throw new ArgumentNullException(nameof(isCommandRegistered));
		_resolveProgressionId = resolveProgressionId ?? ProgressionIdRegistry.GetId;
		_resolveRequirementId = resolveRequirementId ?? ProgressionRequirementIdRegistry.GetId;

		EnsureDescriptorCanProduce();
	}

	public string Topic { get; }
	public string DescriptorId => _descriptor.DescriptorId;
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
		if (!_world.IsAlive(_scopeHost))
		{
			throw new InvalidOperationException(
				$"TechTree topic '{Topic}' scope host entity {_scopeHost.Id} is not alive.");
		}

		if (!_world.TryGet(_scopeHost, out ProgressionStateBuffer state))
		{
			throw new InvalidOperationException(
				$"TechTree topic '{Topic}' requires ProgressionStateBuffer on scope host {_scopeHost.Id}.");
		}

		if (!_scopeKeys.TryGetId(_descriptor.ScopeKey, out int scopeKeyId) || scopeKeyId <= 0)
		{
			throw new InvalidOperationException(
				$"TechTree descriptor '{_descriptor.DescriptorId}' references unknown scope '{_descriptor.ScopeKey}'.");
		}

		var context = new RoleResolverContext(
			actor: _actor,
			subject: _actor,
			explicitScopeHost: _scopeHost);

		var nodes = new WebUiTechTreeNodeSnapshot[_descriptor.Nodes.Count];
		uint fieldRevision = state.Revision;
		for (int i = 0; i < _descriptor.Nodes.Count; i++)
		{
			WebUiTechTreeNode node = _descriptor.Nodes[i];
			nodes[i] = ResolveNode(node, in state, in context, out uint contribution);
			fieldRevision ^= contribution + ((uint)(i + 1) * 397u);
		}

		_revision++;
		uint revision = _revision ^ fieldRevision;
		return new WebUiTechTreeSnapshot(
			Descriptor: _descriptor.DescriptorId,
			ProfileId: _descriptor.ProfileId,
			LayoutId: _descriptor.LayoutId,
			ScopeKey: _descriptor.ScopeKey,
			ScopeHost: new WebUiTechTreeScopeHostRef(_scopeHost.Id, _scopeHost.WorldId, _scopeHost.Version),
			Revision: revision,
			Nodes: nodes);
	}

	private void EnsureDescriptorCanProduce()
	{
		if (!_scopeKeys.TryGetId(_descriptor.ScopeKey, out int scopeKeyId) || scopeKeyId <= 0)
		{
			throw new InvalidOperationException(
				$"TechTree descriptor '{_descriptor.DescriptorId}' references unknown scope '{_descriptor.ScopeKey}'.");
		}

		foreach (WebUiTechTreeNode node in _descriptor.Nodes)
		{
			int progressionId = _resolveProgressionId(node.ProgressionId);
			if (progressionId == ProgressionIdRegistry.InvalidId || progressionId <= 0)
			{
				throw new InvalidOperationException(
					$"TechTree descriptor '{_descriptor.DescriptorId}' node '{node.NodeId}' references unknown progression '{node.ProgressionId}'.");
			}

			if (!_definitions.TryGet(progressionId, out _))
			{
				throw new InvalidOperationException(
					$"TechTree descriptor '{_descriptor.DescriptorId}' node '{node.NodeId}' progression '{node.ProgressionId}' is not registered in ProgressionDefinitionRegistry.");
			}

			if (node.RequirementId != null)
			{
				int requirementId = _resolveRequirementId(node.RequirementId);
				if (requirementId == ProgressionRequirementIdRegistry.InvalidId || requirementId <= 0)
				{
					throw new InvalidOperationException(
						$"TechTree descriptor '{_descriptor.DescriptorId}' node '{node.NodeId}' references unknown requirement '{node.RequirementId}'.");
				}
			}

			if (node.CommandId != null && !_isCommandRegistered(node.CommandId))
			{
				throw new InvalidOperationException(
					$"TechTree descriptor '{_descriptor.DescriptorId}' node '{node.NodeId}' references unknown WebUI command '{node.CommandId}'.");
			}
		}
	}

	private WebUiTechTreeNodeSnapshot ResolveNode(
		WebUiTechTreeNode node,
		in ProgressionStateBuffer state,
		in RoleResolverContext context,
		out uint contribution)
	{
		int progressionId = _resolveProgressionId(node.ProgressionId);
		if (progressionId == ProgressionIdRegistry.InvalidId || progressionId <= 0)
		{
			throw new InvalidOperationException(
				$"TechTree node '{node.NodeId}' references unknown progression '{node.ProgressionId}'.");
		}

		if (!_definitions.TryGet(progressionId, out _))
		{
			throw new InvalidOperationException(
				$"TechTree node '{node.NodeId}' progression '{node.ProgressionId}' is not registered in ProgressionDefinitionRegistry.");
		}

		int level = state.GetLevel(progressionId);
		WebUiTechTreeNodeStatus status;
		string? blockedReasonTokenId = null;
		string? commandId = null;

		if (state.HasLevelAtLeast(progressionId, node.TargetLevel))
		{
			status = WebUiTechTreeNodeStatus.Completed;
		}
		else if (level > 0)
		{
			status = WebUiTechTreeNodeStatus.Active;
			commandId = RequireRegisteredCommand(node);
		}
		else if (HasUnmetPrerequisites(node, in state))
		{
			status = WebUiTechTreeNodeStatus.Locked;
		}
		else if (node.RequirementId != null)
		{
			int requirementId = _resolveRequirementId(node.RequirementId);
			if (requirementId == ProgressionRequirementIdRegistry.InvalidId || requirementId <= 0)
			{
				throw new InvalidOperationException(
					$"TechTree node '{node.NodeId}' references unknown requirement '{node.RequirementId}'.");
			}

			if (_evaluator.Evaluate(requirementId, in context))
			{
				status = WebUiTechTreeNodeStatus.Available;
				commandId = RequireRegisteredCommand(node);
			}
			else
			{
				status = WebUiTechTreeNodeStatus.Blocked;
				blockedReasonTokenId = node.BlockedReasonTokenId
					?? throw new InvalidOperationException(
						$"TechTree node '{node.NodeId}' is blocked but missing blockedReasonTokenId.");
			}
		}
		else
		{
			status = WebUiTechTreeNodeStatus.Available;
			commandId = RequireRegisteredCommand(node);
		}

		contribution = (uint)progressionId
			^ (uint)(level * 31)
			^ (uint)((int)status * 17)
			^ (uint)node.SortOrder;
		return new WebUiTechTreeNodeSnapshot(
			node.NodeId,
			node.ProgressionId,
			node.RequirementId,
			node.PrerequisiteNodeIds.ToArray(),
			status.ToString(),
			level,
			node.TargetLevel,
			blockedReasonTokenId,
			commandId,
			node.DisplayTokenId,
			node.LayoutSlotId,
			node.SortOrder);
	}

	private bool HasUnmetPrerequisites(WebUiTechTreeNode node, in ProgressionStateBuffer state)
	{
		for (int i = 0; i < node.PrerequisiteNodeIds.Count; i++)
		{
			string prerequisiteNodeId = node.PrerequisiteNodeIds[i];
			if (!_descriptor.TryGetNode(prerequisiteNodeId, out WebUiTechTreeNode prerequisite))
			{
				throw new InvalidOperationException(
					$"TechTree node '{node.NodeId}' references unknown prerequisite node '{prerequisiteNodeId}'.");
			}

			int prerequisiteProgressionId = _resolveProgressionId(prerequisite.ProgressionId);
			if (prerequisiteProgressionId == ProgressionIdRegistry.InvalidId || prerequisiteProgressionId <= 0)
			{
				throw new InvalidOperationException(
					$"TechTree prerequisite node '{prerequisiteNodeId}' references unknown progression '{prerequisite.ProgressionId}'.");
			}

			if (!state.HasLevelAtLeast(prerequisiteProgressionId, prerequisite.TargetLevel))
			{
				return true;
			}
		}

		return false;
	}

	private string? RequireRegisteredCommand(WebUiTechTreeNode node)
	{
		if (node.CommandId == null)
		{
			return null;
		}

		if (!_isCommandRegistered(node.CommandId))
		{
			throw new InvalidOperationException(
				$"TechTree node '{node.NodeId}' references unknown WebUI command '{node.CommandId}'.");
		}

		return node.CommandId;
	}
}

/// <summary>DataPlane payload for a TechTree / Progression panel snapshot.</summary>
public sealed record WebUiTechTreeSnapshot(
	string Descriptor,
	string ProfileId,
	string LayoutId,
	string ScopeKey,
	WebUiTechTreeScopeHostRef ScopeHost,
	uint Revision,
	WebUiTechTreeNodeSnapshot[] Nodes);

public sealed record WebUiTechTreeScopeHostRef(int EntityId, int WorldId, int Version);

public sealed record WebUiTechTreeNodeSnapshot(
	string NodeId,
	string ProgressionId,
	string? RequirementId,
	string[] PrerequisiteNodeIds,
	string Status,
	int Level,
	int TargetLevel,
	string? BlockedReasonTokenId,
	string? CommandId,
	string DisplayTokenId,
	string LayoutSlotId,
	int SortOrder);
