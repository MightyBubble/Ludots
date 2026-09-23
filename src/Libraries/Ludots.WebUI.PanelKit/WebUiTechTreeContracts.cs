using System.Collections.ObjectModel;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Node status projected for a TechTree / Progression panel. Derived from
/// <c>ProgressionStateBuffer</c> + <c>ProgressionRequirementEvaluator</c> — never browser-owned.
/// </summary>
public enum WebUiTechTreeNodeStatus
{
	Locked = 1,
	Available = 2,
	Active = 3,
	Completed = 4,
	Blocked = 5
}

/// <summary>
/// One authored node in a TechTree / Progression panel descriptor.
/// Ids only — no gameplay truth and no TechTreeStore.
/// </summary>
public sealed class WebUiTechTreeNode
{
	public WebUiTechTreeNode(
		string nodeId,
		string progressionId,
		string displayTokenId,
		int sortOrder,
		string layoutSlotId,
		int targetLevel,
		string? requirementId,
		IReadOnlyList<string> prerequisiteNodeIds,
		string? blockedReasonTokenId,
		string? commandId)
	{
		NodeId = RequireId(nodeId, nameof(nodeId));
		ProgressionId = RequireId(progressionId, nameof(progressionId));
		DisplayTokenId = RequireId(displayTokenId, nameof(displayTokenId));
		SortOrder = sortOrder;
		LayoutSlotId = RequireId(layoutSlotId, nameof(layoutSlotId));
		if (targetLevel <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(targetLevel), targetLevel, "Target level must be >= 1.");
		}

		TargetLevel = targetLevel;
		RequirementId = string.IsNullOrWhiteSpace(requirementId) ? null : RequireId(requirementId, nameof(requirementId));
		PrerequisiteNodeIds = new ReadOnlyCollection<string>((prerequisiteNodeIds ?? Array.Empty<string>()).ToArray());
		BlockedReasonTokenId = string.IsNullOrWhiteSpace(blockedReasonTokenId)
			? null
			: RequireId(blockedReasonTokenId, nameof(blockedReasonTokenId));
		CommandId = string.IsNullOrWhiteSpace(commandId) ? null : RequireId(commandId, nameof(commandId));

		if (RequirementId != null && BlockedReasonTokenId == null)
		{
			throw new InvalidOperationException(
				$"TechTree node '{NodeId}' declares requirementId '{RequirementId}' but missing blockedReasonTokenId.");
		}

		foreach (string prerequisite in PrerequisiteNodeIds)
		{
			RequireId(prerequisite, nameof(prerequisiteNodeIds));
		}
	}

	public string NodeId { get; }
	public string ProgressionId { get; }
	public string DisplayTokenId { get; }
	public int SortOrder { get; }
	public string LayoutSlotId { get; }
	public int TargetLevel { get; }
	public string? RequirementId { get; }
	public IReadOnlyList<string> PrerequisiteNodeIds { get; }
	public string? BlockedReasonTokenId { get; }
	public string? CommandId { get; }

	private static string RequireId(string? value, string paramName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{paramName} is required.", paramName);
		}

		string trimmed = value.Trim();
		if (!string.Equals(value, trimmed, StringComparison.Ordinal))
		{
			throw new ArgumentException($"{paramName} must not contain leading or trailing whitespace.", paramName);
		}

		return trimmed;
	}
}

/// <summary>
/// Validated TechTree / Progression panel descriptor. Display graph only; Progression runtime owns truth.
/// </summary>
public sealed class WebUiTechTreeDescriptor
{
	private readonly IReadOnlyList<WebUiTechTreeNode> _nodes;
	private readonly Dictionary<string, WebUiTechTreeNode> _byNodeId;

	public WebUiTechTreeDescriptor(
		string descriptorId,
		string profileId,
		string layoutId,
		string scopeKey,
		IReadOnlyList<WebUiTechTreeNode> nodes)
	{
		DescriptorId = RequireId(descriptorId, nameof(descriptorId));
		ProfileId = RequireId(profileId, nameof(profileId));
		LayoutId = RequireId(layoutId, nameof(layoutId));
		ScopeKey = RequireId(scopeKey, nameof(scopeKey));
		ArgumentNullException.ThrowIfNull(nodes);
		if (nodes.Count == 0)
		{
			throw new ArgumentException("Descriptor must declare at least one node.", nameof(nodes));
		}

		var ordered = nodes
			.OrderBy(static node => node.SortOrder)
			.ThenBy(static node => node.NodeId, StringComparer.Ordinal)
			.ToArray();
		_byNodeId = new Dictionary<string, WebUiTechTreeNode>(ordered.Length, StringComparer.Ordinal);
		foreach (WebUiTechTreeNode node in ordered)
		{
			if (!_byNodeId.TryAdd(node.NodeId, node))
			{
				throw new InvalidOperationException($"Duplicate TechTree node id '{node.NodeId}'.");
			}
		}

		foreach (WebUiTechTreeNode node in ordered)
		{
			foreach (string prerequisite in node.PrerequisiteNodeIds)
			{
				if (!_byNodeId.ContainsKey(prerequisite))
				{
					throw new InvalidOperationException(
						$"TechTree node '{node.NodeId}' references unknown prerequisite node '{prerequisite}'.");
				}

				if (string.Equals(prerequisite, node.NodeId, StringComparison.Ordinal))
				{
					throw new InvalidOperationException(
						$"TechTree node '{node.NodeId}' cannot list itself as a prerequisite.");
				}
			}
		}

		_nodes = new ReadOnlyCollection<WebUiTechTreeNode>(ordered);
	}

	public string DescriptorId { get; }
	public string ProfileId { get; }
	public string LayoutId { get; }
	public string ScopeKey { get; }
	public IReadOnlyList<WebUiTechTreeNode> Nodes => _nodes;

	public bool TryGetNode(string nodeId, out WebUiTechTreeNode node)
	{
		if (string.IsNullOrWhiteSpace(nodeId))
		{
			node = null!;
			return false;
		}

		return _byNodeId.TryGetValue(nodeId.Trim(), out node!);
	}

	private static string RequireId(string value, string paramName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{paramName} is required.", paramName);
		}

		string trimmed = value.Trim();
		if (!string.Equals(value, trimmed, StringComparison.Ordinal))
		{
			throw new ArgumentException($"{paramName} must not contain leading or trailing whitespace.", paramName);
		}

		return trimmed;
	}
}

/// <summary>
/// Reference catalogs required to validate a TechTree descriptor at load time.
/// Missing ids fail fast; there is no empty/Unknown/default fallback.
/// </summary>
public sealed class WebUiTechTreeReferenceCatalog
{
	public WebUiTechTreeReferenceCatalog(
		IWebUiPanelIdRegistry displayTokens,
		IWebUiPanelIdRegistry blockedReasonTokens,
		IWebUiPanelIdRegistry profiles,
		IWebUiPanelIdRegistry layouts,
		Func<string, bool> isProgressionRegistered,
		Func<string, bool> isRequirementRegistered,
		Func<string, bool> isScopeKeyRegistered,
		Func<string, bool> isCommandRegistered)
	{
		DisplayTokens = displayTokens ?? throw new ArgumentNullException(nameof(displayTokens));
		BlockedReasonTokens = blockedReasonTokens ?? throw new ArgumentNullException(nameof(blockedReasonTokens));
		Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
		Layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
		IsProgressionRegistered = isProgressionRegistered ?? throw new ArgumentNullException(nameof(isProgressionRegistered));
		IsRequirementRegistered = isRequirementRegistered ?? throw new ArgumentNullException(nameof(isRequirementRegistered));
		IsScopeKeyRegistered = isScopeKeyRegistered ?? throw new ArgumentNullException(nameof(isScopeKeyRegistered));
		IsCommandRegistered = isCommandRegistered ?? throw new ArgumentNullException(nameof(isCommandRegistered));
	}

	public IWebUiPanelIdRegistry DisplayTokens { get; }
	public IWebUiPanelIdRegistry BlockedReasonTokens { get; }
	public IWebUiPanelIdRegistry Profiles { get; }
	public IWebUiPanelIdRegistry Layouts { get; }
	public Func<string, bool> IsProgressionRegistered { get; }
	public Func<string, bool> IsRequirementRegistered { get; }
	public Func<string, bool> IsScopeKeyRegistered { get; }
	public Func<string, bool> IsCommandRegistered { get; }
}
