using System.Collections.ObjectModel;

using System.Collections.ObjectModel;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Kind of tooltip target. Entity tooltips project EntityInsightProfile; ability tooltips
/// project ability presentation tokens. No parallel TooltipProfile owns entity truth.
/// </summary>
public enum WebUiTooltipTargetKind
{
	EntityInsight = 1,
	Ability = 2
}

/// <summary>
/// One section in a tooltip descriptor. Sections compose rich-text blocks; they do not
/// invent gameplay state.
/// </summary>
public sealed class WebUiTooltipSection
{
	private readonly IReadOnlyList<WebUiRichTextBlock> _blocks;

	public WebUiTooltipSection(string sectionId, string templateId, IReadOnlyList<WebUiRichTextBlock> blocks)
	{
		SectionId = RequireId(sectionId, nameof(sectionId));
		TemplateId = RequireId(templateId, nameof(templateId));
		ArgumentNullException.ThrowIfNull(blocks);
		if (blocks.Count == 0)
		{
			throw new ArgumentException($"Tooltip section '{SectionId}' must declare at least one rich-text block.", nameof(blocks));
		}

		_blocks = new ReadOnlyCollection<WebUiRichTextBlock>(blocks.ToArray());
	}

	public string SectionId { get; }
	public string TemplateId { get; }
	public IReadOnlyList<WebUiRichTextBlock> Blocks => _blocks;

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
/// Validated tooltip descriptor: projection layout only. Entity truth stays on EntityInsightProfile.
/// </summary>
public sealed class WebUiTooltipDescriptor
{
	private readonly IReadOnlyList<WebUiTooltipSection> _sections;

	public WebUiTooltipDescriptor(
		string descriptorId,
		WebUiTooltipTargetKind targetKind,
		string profileId,
		string templateId,
		string localeId,
		string anchor,
		IReadOnlyList<WebUiTooltipSection> sections)
	{
		DescriptorId = RequireId(descriptorId, nameof(descriptorId));
		TargetKind = targetKind;
		ProfileId = RequireId(profileId, nameof(profileId));
		TemplateId = RequireId(templateId, nameof(templateId));
		LocaleId = RequireId(localeId, nameof(localeId));
		Anchor = RequireId(anchor, nameof(anchor));
		ArgumentNullException.ThrowIfNull(sections);
		if (sections.Count == 0)
		{
			throw new ArgumentException("Tooltip descriptor must declare at least one section.", nameof(sections));
		}

		_sections = new ReadOnlyCollection<WebUiTooltipSection>(sections.ToArray());
	}

	public string DescriptorId { get; }
	public WebUiTooltipTargetKind TargetKind { get; }
	public string ProfileId { get; }
	public string TemplateId { get; }
	public string LocaleId { get; }
	public string Anchor { get; }
	public IReadOnlyList<WebUiTooltipSection> Sections => _sections;

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
/// Reference catalogs required to validate a tooltip descriptor at load time.
/// Missing ids fail fast; there is no empty/Unknown/default fallback.
/// </summary>
public sealed class WebUiTooltipReferenceCatalog
{
	public WebUiTooltipReferenceCatalog(
		IWebUiPanelIdRegistry profiles,
		IWebUiPanelIdRegistry templates,
		IWebUiPanelIdRegistry locales,
		IWebUiPanelIdRegistry anchors,
		Func<string, bool> isTokenRegistered,
		Func<string, string, bool> hasLocaleTemplate,
		Func<string, bool>? isEntityInsightProfileRegistered = null,
		Func<string, bool>? isAbilityPresentationTokenRegistered = null)
	{
		Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
		Templates = templates ?? throw new ArgumentNullException(nameof(templates));
		Locales = locales ?? throw new ArgumentNullException(nameof(locales));
		Anchors = anchors ?? throw new ArgumentNullException(nameof(anchors));
		IsTokenRegistered = isTokenRegistered ?? throw new ArgumentNullException(nameof(isTokenRegistered));
		HasLocaleTemplate = hasLocaleTemplate ?? throw new ArgumentNullException(nameof(hasLocaleTemplate));
		IsEntityInsightProfileRegistered = isEntityInsightProfileRegistered;
		IsAbilityPresentationTokenRegistered = isAbilityPresentationTokenRegistered;
	}

	public IWebUiPanelIdRegistry Profiles { get; }
	public IWebUiPanelIdRegistry Templates { get; }
	public IWebUiPanelIdRegistry Locales { get; }
	public IWebUiPanelIdRegistry Anchors { get; }
	public Func<string, bool> IsTokenRegistered { get; }
	public Func<string, string, bool> HasLocaleTemplate { get; }

	/// <summary>
	/// Optional EntityInsightProfile id registry. When provided, EntityInsight targets must resolve.
	/// </summary>
	public Func<string, bool>? IsEntityInsightProfileRegistered { get; }

	/// <summary>
	/// Optional ability presentation token registry used when validating ability-target descriptors.
	/// </summary>
	public Func<string, bool>? IsAbilityPresentationTokenRegistered { get; }
}

/// <summary>
/// Projection input for an entity tooltip. Built from EntityInsightProfile fields — not a parallel profile.
/// </summary>
public sealed class WebUiTooltipEntityInsightProjection
{
	public WebUiTooltipEntityInsightProjection(
		string insightProfileId,
		string titleTokenId,
		string bodyTokenId,
		IReadOnlyList<string> badgeTokenIds,
		IReadOnlyList<string> statLabelTokenIds,
		IReadOnlyList<string> tipTokenIds,
		IReadOnlyList<(string TitleTokenId, string BodyTokenId)> actionTokenPairs)
	{
		InsightProfileId = RequireId(insightProfileId, nameof(insightProfileId));
		TitleTokenId = RequireId(titleTokenId, nameof(titleTokenId));
		BodyTokenId = RequireId(bodyTokenId, nameof(bodyTokenId));
		BadgeTokenIds = CopyRequired(badgeTokenIds, nameof(badgeTokenIds));
		StatLabelTokenIds = CopyRequired(statLabelTokenIds, nameof(statLabelTokenIds));
		TipTokenIds = CopyRequired(tipTokenIds, nameof(tipTokenIds));
		ArgumentNullException.ThrowIfNull(actionTokenPairs);
		var actions = new List<(string TitleTokenId, string BodyTokenId)>(actionTokenPairs.Count);
		for (int i = 0; i < actionTokenPairs.Count; i++)
		{
			(string title, string body) = actionTokenPairs[i];
			actions.Add((RequireId(title, $"actionTokenPairs[{i}].TitleTokenId"), RequireId(body, $"actionTokenPairs[{i}].BodyTokenId")));
		}

		ActionTokenPairs = actions;
	}

	public string InsightProfileId { get; }
	public string TitleTokenId { get; }
	public string BodyTokenId { get; }
	public IReadOnlyList<string> BadgeTokenIds { get; }
	public IReadOnlyList<string> StatLabelTokenIds { get; }
	public IReadOnlyList<string> TipTokenIds { get; }
	public IReadOnlyList<(string TitleTokenId, string BodyTokenId)> ActionTokenPairs { get; }

	private static IReadOnlyList<string> CopyRequired(IReadOnlyList<string> values, string paramName)
	{
		ArgumentNullException.ThrowIfNull(values);
		var copy = new string[values.Count];
		for (int i = 0; i < values.Count; i++)
		{
			copy[i] = RequireId(values[i], $"{paramName}[{i}]");
		}

		return new ReadOnlyCollection<string>(copy);
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
/// Projection input for an ability tooltip. Token ids must already pass AbilityPresentationTextValidator.
/// </summary>
public sealed class WebUiTooltipAbilityProjection
{
	public WebUiTooltipAbilityProjection(
		string abilityId,
		string displayNameTokenId,
		string hintTextTokenId,
		IReadOnlyDictionary<string, string> modeHintTokenIds)
	{
		AbilityId = RequireId(abilityId, nameof(abilityId));
		DisplayNameTokenId = RequireId(displayNameTokenId, nameof(displayNameTokenId));
		HintTextTokenId = RequireId(hintTextTokenId, nameof(hintTextTokenId));
		ArgumentNullException.ThrowIfNull(modeHintTokenIds);
		var copy = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, string> pair in modeHintTokenIds)
		{
			copy[RequireId(pair.Key, "modeHintTokenIds.key")] = RequireId(pair.Value, $"modeHintTokenIds[{pair.Key}]");
		}

		ModeHintTokenIds = new ReadOnlyDictionary<string, string>(copy);
	}

	public string AbilityId { get; }
	public string DisplayNameTokenId { get; }
	public string HintTextTokenId { get; }
	public IReadOnlyDictionary<string, string> ModeHintTokenIds { get; }

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
