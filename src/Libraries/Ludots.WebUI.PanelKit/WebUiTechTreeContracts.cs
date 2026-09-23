using System.Collections.ObjectModel;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Runtime status projected for a TechTree / Progression panel node.
/// Status comes from ProgressionStateBuffer + ProgressionRequirementEvaluator — never a TechTreeStore.
/// </summary>
public enum WebUiTechTreeNodeStatus
{
	Locked = 1,
	Available = 2,
	Active = 3,
	Completed = 4
}

/// <summary>
/// How clicking an available node submits work. Panel only carries registered entry ids.
/// </summary>
public enum WebUiTechTreeActionKind
{
	/// <summary>Registered WebUI / ability command name.</summary>
	Command = 1,

	/// <summary>Registered ability id activated through the formal ability entry.</summary>
	Ability = 2,

	/// <summary>Progression runtime entry (e.g. CompleteProgression effect / evaluator apply).</summary>
	Progression = 3
}

/// <summary>
/// One authored node in a TechTree panel descriptor. Display + references only; gameplay truth
/// stays on Progression definitions / requirements / scope hosts.
/// </summary>
public sealed class WebUiTechTreeNode
{
	private readonly IReadOnlyList<string> _prerequisiteProgressionIds;

	public WebUiTechTreeNode(
		string nodeId,
		string progressionId,
		string scopeKeyId,
		string? unlockRequirementId,
		string titleTokenId,
		string bodyTokenId,
		string effectTokenId,
		string blockedReasonTokenId,
		string groupId,
		int sortOrder,
		float layoutX,
		float layoutY,
		WebUiTechTreeActionKind actionKind,
		string actionId,
		IReadOnlyList<string>? prerequisiteProgressionIds = null,
		string? tooltipDescriptorId = null)
	{
		NodeId = RequireId(nodeId, nameof(nodeId));
		ProgressionId = RequireId(progressionId, nameof(progressionId));
		ScopeKeyId = RequireId(scopeKeyId, nameof(scopeKeyId));
		UnlockRequirementId = string.IsNullOrWhiteSpace(unlockRequirementId)
			? null
			: RequireId(unlockRequirementId, nameof(unlockRequirementId));
		TitleTokenId = RequireId(titleTokenId, nameof(titleTokenId));
		BodyTokenId = RequireId(bodyTokenId, nameof(bodyTokenId));
		EffectTokenId = RequireId(effectTokenId, nameof(effectTokenId));
		BlockedReasonTokenId = RequireId(blockedReasonTokenId, nameof(blockedReasonTokenId));
		GroupId = RequireId(groupId, nameof(groupId));
		SortOrder = sortOrder;
		LayoutX = layoutX;
		LayoutY = layoutY;
		ActionKind = actionKind;
		ActionId = RequireId(actionId, nameof(actionId));
		TooltipDescriptorId = string.IsNullOrWhiteSpace(tooltipDescriptorId)
			? null
			: RequireId(tooltipDescriptorId, nameof(tooltipDescriptorId));

		ArgumentNullException.ThrowIfNull(prerequisiteProgressionIds ?? Array.Empty<string>());
		IReadOnlyList<string> prereqs = prerequisiteProgressionIds ?? Array.Empty<string>();
		var copy = new string[prereqs.Count];
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < prereqs.Count; i++)
		{
			string id = RequireId(prereqs[i], $"{nameof(prerequisiteProgressionIds)}[{i}]");
			if (!seen.Add(id))
			{
				throw new ArgumentException(
					$"Node '{NodeId}' declares duplicate prerequisite progression '{id}'.",
					nameof(prerequisiteProgressionIds));
			}

			copy[i] = id;
		}

		_prerequisiteProgressionIds = new ReadOnlyCollection<string>(copy);
	}

	public string NodeId { get; }
	public string ProgressionId { get; }
	public string ScopeKeyId { get; }
	public string? UnlockRequirementId { get; }
	public string TitleTokenId { get; }
	public string BodyTokenId { get; }
	public string EffectTokenId { get; }
	public string BlockedReasonTokenId { get; }
	public string GroupId { get; }
	public int SortOrder { get; }
	public float LayoutX { get; }
	public float LayoutY { get; }
	public WebUiTechTreeActionKind ActionKind { get; }
	public string ActionId { get; }
	public string? TooltipDescriptorId { get; }
	public IReadOnlyList<string> PrerequisiteProgressionIds => _prerequisiteProgressionIds;

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
/// Validated TechTree / Progression panel descriptor: layout + node references only.
/// Does not invent Technology / TechTreeStore gameplay truth.
/// </summary>
public sealed class WebUiTechTreeDescriptor
{
	private readonly IReadOnlyList<WebUiTechTreeNode> _nodes;

	public WebUiTechTreeDescriptor(
		string descriptorId,
		string profileId,
		string layoutId,
		string localeId,
		IReadOnlyList<WebUiTechTreeNode> nodes)
	{
		DescriptorId = RequireId(descriptorId, nameof(descriptorId));
		ProfileId = RequireId(profileId, nameof(profileId));
		LayoutId = RequireId(layoutId, nameof(layoutId));
		LocaleId = RequireId(localeId, nameof(localeId));
		ArgumentNullException.ThrowIfNull(nodes);
		if (nodes.Count == 0)
		{
			throw new ArgumentException("Descriptor must declare at least one node.", nameof(nodes));
		}

		var ordered = nodes
			.OrderBy(static node => node.SortOrder)
			.ThenBy(static node => node.NodeId, StringComparer.Ordinal)
			.ToArray();
		_nodes = new ReadOnlyCollection<WebUiTechTreeNode>(ordered);
	}

	public string DescriptorId { get; }
	public string ProfileId { get; }
	public string LayoutId { get; }
	public string LocaleId { get; }
	public IReadOnlyList<WebUiTechTreeNode> Nodes => _nodes;

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
/// Missing progression / requirement / scope / token / action ids fail fast.
/// </summary>
public sealed class WebUiTechTreeReferenceCatalog
{
	public WebUiTechTreeReferenceCatalog(
		IWebUiPanelIdRegistry profiles,
		IWebUiPanelIdRegistry layouts,
		IWebUiPanelIdRegistry locales,
		IWebUiPanelIdRegistry displayTokens,
		Func<string, bool> isProgressionRegistered,
		Func<string, bool> isRequirementRegistered,
		Func<string, bool> isScopeKeyRegistered,
		Func<string, bool> isActionRegistered,
		Func<string, string, bool>? hasLocaleTemplate = null,
		Func<string, bool>? isTooltipDescriptorRegistered = null)
	{
		Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
		Layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
		Locales = locales ?? throw new ArgumentNullException(nameof(locales));
		DisplayTokens = displayTokens ?? throw new ArgumentNullException(nameof(displayTokens));
		IsProgressionRegistered = isProgressionRegistered
			?? throw new ArgumentNullException(nameof(isProgressionRegistered));
		IsRequirementRegistered = isRequirementRegistered
			?? throw new ArgumentNullException(nameof(isRequirementRegistered));
		IsScopeKeyRegistered = isScopeKeyRegistered
			?? throw new ArgumentNullException(nameof(isScopeKeyRegistered));
		IsActionRegistered = isActionRegistered
			?? throw new ArgumentNullException(nameof(isActionRegistered));
		HasLocaleTemplate = hasLocaleTemplate;
		IsTooltipDescriptorRegistered = isTooltipDescriptorRegistered;
	}

	public IWebUiPanelIdRegistry Profiles { get; }
	public IWebUiPanelIdRegistry Layouts { get; }
	public IWebUiPanelIdRegistry Locales { get; }
	public IWebUiPanelIdRegistry DisplayTokens { get; }
	public Func<string, bool> IsProgressionRegistered { get; }
	public Func<string, bool> IsRequirementRegistered { get; }
	public Func<string, bool> IsScopeKeyRegistered { get; }
	public Func<string, bool> IsActionRegistered { get; }

	/// <summary>
	/// Optional locale coverage check for display tokens. When provided, missing locale templates fail fast.
	/// </summary>
	public Func<string, string, bool>? HasLocaleTemplate { get; }

	/// <summary>
	/// Optional WPK-5 tooltip descriptor registry. When provided, tooltipDescriptorId must resolve.
	/// </summary>
	public Func<string, bool>? IsTooltipDescriptorRegistered { get; }
}
