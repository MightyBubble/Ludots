using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Loads and validates a tooltip descriptor from JSON.
/// Missing profile / template / locale / token / unknown run role fail fast with concrete ids.
/// </summary>
public static class WebUiTooltipDescriptorLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static WebUiTooltipDescriptor LoadFromJson(
		string json,
		WebUiTooltipReferenceCatalog catalog,
		string source = "<inline>")
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			throw new ArgumentException("Descriptor JSON is required.", nameof(json));
		}

		ArgumentNullException.ThrowIfNull(catalog);
		if (string.IsNullOrWhiteSpace(source))
		{
			throw new ArgumentException("Source label is required.", nameof(source));
		}

		WebUiTooltipDescriptorDocument? document;
		try
		{
			document = JsonSerializer.Deserialize<WebUiTooltipDescriptorDocument>(json, JsonOptions);
		}
		catch (JsonException ex)
		{
			throw new InvalidOperationException($"Failed to deserialize tooltip descriptor '{source}': {ex.Message}", ex);
		}

		if (document == null)
		{
			throw new InvalidOperationException($"Tooltip descriptor '{source}' deserialized to null.");
		}

		return ValidateAndBuild(document, catalog, source);
	}

	public static WebUiTooltipDescriptor LoadFromFile(string path, WebUiTooltipReferenceCatalog catalog)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Descriptor path is required.", nameof(path));
		}

		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Tooltip descriptor file not found: '{path}'.", path);
		}

		string json = File.ReadAllText(path);
		return LoadFromJson(json, catalog, path);
	}

	public static WebUiTooltipDescriptor ValidateAndBuild(
		WebUiTooltipDescriptorDocument document,
		WebUiTooltipReferenceCatalog catalog,
		string source)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(catalog);
		if (string.IsNullOrWhiteSpace(source))
		{
			throw new ArgumentException("Source label is required.", nameof(source));
		}

		RequireTrimmedNonEmpty(document.DescriptorId, $"{source}.descriptorId");
		RequireTrimmedNonEmpty(document.TargetKind, $"{source}.targetKind");
		RequireTrimmedNonEmpty(document.ProfileId, $"{source}.profileId");
		RequireTrimmedNonEmpty(document.TemplateId, $"{source}.templateId");
		RequireTrimmedNonEmpty(document.LocaleId, $"{source}.localeId");
		RequireTrimmedNonEmpty(document.Anchor, $"{source}.anchor");

		if (!TryParseTargetKind(document.TargetKind, out WebUiTooltipTargetKind targetKind))
		{
			throw new InvalidOperationException(
				$"{source}.targetKind '{document.TargetKind}' is not a known WebUiTooltipTargetKind.");
		}

		RequireRegistered(catalog.Profiles, document.ProfileId, $"{source}.profileId", "profile");
		RequireRegistered(catalog.Templates, document.TemplateId, $"{source}.templateId", "template");
		RequireRegistered(catalog.Locales, document.LocaleId, $"{source}.localeId", "locale");
		RequireRegistered(catalog.Anchors, document.Anchor, $"{source}.anchor", "anchor");

		if (targetKind == WebUiTooltipTargetKind.EntityInsight &&
		    catalog.IsEntityInsightProfileRegistered != null &&
		    !catalog.IsEntityInsightProfileRegistered(document.ProfileId))
		{
			throw new InvalidOperationException(
				$"{source}.profileId '{document.ProfileId}' is not a registered EntityInsightProfile. Tooltip must reuse EntityInsightProfile; do not invent a parallel TooltipProfile.");
		}

		if (document.Sections == null)
		{
			throw new InvalidOperationException($"Tooltip descriptor '{source}' must explicitly define sections.");
		}

		if (document.Sections.Count == 0)
		{
			throw new InvalidOperationException($"Tooltip descriptor '{source}' must declare at least one section.");
		}

		var sectionIds = new HashSet<string>(StringComparer.Ordinal);
		var sections = new List<WebUiTooltipSection>(document.Sections.Count);
		for (int i = 0; i < document.Sections.Count; i++)
		{
			WebUiTooltipSectionDocument section = document.Sections[i]
				?? throw new InvalidOperationException($"{source}.sections[{i}] must be an object.");
			string path = $"{source}.sections[{i}]";
			RequireTrimmedNonEmpty(section.SectionId, $"{path}.sectionId");
			if (!sectionIds.Add(section.SectionId))
			{
				throw new InvalidOperationException($"{path}.sectionId duplicates section id '{section.SectionId}'.");
			}

			RequireTrimmedNonEmpty(section.TemplateId, $"{path}.templateId");
			RequireRegistered(catalog.Templates, section.TemplateId, $"{path}.templateId", "template");

			if (section.Blocks == null || section.Blocks.Count == 0)
			{
				throw new InvalidOperationException($"{path} must declare at least one rich-text block.");
			}

			var blockIds = new HashSet<string>(StringComparer.Ordinal);
			var blocks = new List<WebUiRichTextBlock>(section.Blocks.Count);
			for (int b = 0; b < section.Blocks.Count; b++)
			{
				WebUiRichTextBlockDocument block = section.Blocks[b]
					?? throw new InvalidOperationException($"{path}.blocks[{b}] must be an object.");
				string blockPath = $"{path}.blocks[{b}]";
				RequireTrimmedNonEmpty(block.BlockId, $"{blockPath}.blockId");
				if (!blockIds.Add(block.BlockId))
				{
					throw new InvalidOperationException($"{blockPath}.blockId duplicates block id '{block.BlockId}'.");
				}

				if (block.Runs == null || block.Runs.Count == 0)
				{
					throw new InvalidOperationException($"{blockPath} must declare at least one rich-text run.");
				}

				var runs = new List<WebUiRichTextRun>(block.Runs.Count);
				for (int r = 0; r < block.Runs.Count; r++)
				{
					WebUiRichTextRunDocument run = block.Runs[r]
						?? throw new InvalidOperationException($"{blockPath}.runs[{r}] must be an object.");
					string runPath = $"{blockPath}.runs[{r}]";
					WebUiRichTextRunRole role = WebUiRichTextGuard.ParseRole(run.Role, $"{runPath}.role");
					WebUiRichTextRun built = BuildRun(role, run, runPath, catalog, document.LocaleId, targetKind);
					runs.Add(built);
				}

				blocks.Add(new WebUiRichTextBlock(block.BlockId, runs));
			}

			sections.Add(new WebUiTooltipSection(section.SectionId, section.TemplateId, blocks));
		}

		return new WebUiTooltipDescriptor(
			document.DescriptorId,
			targetKind,
			document.ProfileId,
			document.TemplateId,
			document.LocaleId,
			document.Anchor,
			sections);
	}

	private static WebUiRichTextRun BuildRun(
		WebUiRichTextRunRole role,
		WebUiRichTextRunDocument run,
		string path,
		WebUiTooltipReferenceCatalog catalog,
		string localeId,
		WebUiTooltipTargetKind targetKind)
	{
		switch (role)
		{
			case WebUiRichTextRunRole.Text:
			case WebUiRichTextRunRole.Emphasis:
				RequireTrimmedNonEmpty(run.Text, $"{path}.text");
				WebUiRichTextGuard.RejectHtml(run.Text!, $"{path}.text");
				return new WebUiRichTextRun(role, text: run.Text);

			case WebUiRichTextRunRole.Token:
				RequireTrimmedNonEmpty(run.TokenId, $"{path}.tokenId");
				WebUiRichTextGuard.RequireRegisteredToken(catalog.IsTokenRegistered, run.TokenId!, $"{path}.tokenId");
				WebUiRichTextGuard.RequireLocaleCoverage(
					catalog.HasLocaleTemplate,
					run.TokenId!,
					localeId,
					$"{path}.tokenId");
				if (targetKind == WebUiTooltipTargetKind.Ability &&
				    catalog.IsAbilityPresentationTokenRegistered != null &&
				    !catalog.IsAbilityPresentationTokenRegistered(run.TokenId!))
				{
					throw new InvalidOperationException(
						$"{path}.tokenId '{run.TokenId}' is not a validated ability presentation token.");
				}

				return new WebUiRichTextRun(role, tokenId: run.TokenId);

			case WebUiRichTextRunRole.Icon:
				RequireTrimmedNonEmpty(run.IconId, $"{path}.iconId");
				return new WebUiRichTextRun(role, iconId: run.IconId);

			case WebUiRichTextRunRole.Value:
				RequireTrimmedNonEmpty(run.ValueId, $"{path}.valueId");
				return new WebUiRichTextRun(role, valueId: run.ValueId);

			case WebUiRichTextRunRole.State:
				RequireTrimmedNonEmpty(run.StateId, $"{path}.stateId");
				return new WebUiRichTextRun(role, stateId: run.StateId);

			default:
				throw new InvalidOperationException($"{path}.role '{role}' is unsupported.");
		}
	}

	private static bool TryParseTargetKind(string value, out WebUiTooltipTargetKind kind)
	{
		kind = default;
		string normalized = value.Trim();
		if (Enum.TryParse(normalized, ignoreCase: true, out kind) &&
		    Enum.IsDefined(typeof(WebUiTooltipTargetKind), kind))
		{
			return true;
		}

		return normalized switch
		{
			"entityInsight" => Assign(WebUiTooltipTargetKind.EntityInsight, out kind),
			"ability" => Assign(WebUiTooltipTargetKind.Ability, out kind),
			_ => false
		};
	}

	private static bool Assign(WebUiTooltipTargetKind value, out WebUiTooltipTargetKind kind)
	{
		kind = value;
		return true;
	}

	private static void RequireRegistered(IWebUiPanelIdRegistry registry, string id, string path, string kind)
	{
		if (!registry.Contains(id))
		{
			throw new InvalidOperationException($"{path} references unknown {kind} '{id}'.");
		}
	}

	private static void RequireTrimmedNonEmpty(string? value, string path)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"{path} must be a non-empty string.");
		}

		if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
		{
			throw new InvalidOperationException($"{path} must not contain leading or trailing whitespace.");
		}
	}
}

public sealed class WebUiTooltipDescriptorDocument
{
	[JsonPropertyName("descriptorId")]
	public string DescriptorId { get; set; } = string.Empty;

	[JsonPropertyName("targetKind")]
	public string TargetKind { get; set; } = string.Empty;

	[JsonPropertyName("profileId")]
	public string ProfileId { get; set; } = string.Empty;

	[JsonPropertyName("templateId")]
	public string TemplateId { get; set; } = string.Empty;

	[JsonPropertyName("localeId")]
	public string LocaleId { get; set; } = string.Empty;

	[JsonPropertyName("anchor")]
	public string Anchor { get; set; } = string.Empty;

	[JsonPropertyName("sections")]
	public List<WebUiTooltipSectionDocument>? Sections { get; set; }
}

public sealed class WebUiTooltipSectionDocument
{
	[JsonPropertyName("sectionId")]
	public string SectionId { get; set; } = string.Empty;

	[JsonPropertyName("templateId")]
	public string TemplateId { get; set; } = string.Empty;

	[JsonPropertyName("blocks")]
	public List<WebUiRichTextBlockDocument>? Blocks { get; set; }
}

public sealed class WebUiRichTextBlockDocument
{
	[JsonPropertyName("blockId")]
	public string BlockId { get; set; } = string.Empty;

	[JsonPropertyName("runs")]
	public List<WebUiRichTextRunDocument>? Runs { get; set; }
}

public sealed class WebUiRichTextRunDocument
{
	[JsonPropertyName("role")]
	public string Role { get; set; } = string.Empty;

	[JsonPropertyName("text")]
	public string? Text { get; set; }

	[JsonPropertyName("tokenId")]
	public string? TokenId { get; set; }

	[JsonPropertyName("iconId")]
	public string? IconId { get; set; }

	[JsonPropertyName("valueId")]
	public string? ValueId { get; set; }

	[JsonPropertyName("stateId")]
	public string? StateId { get; set; }
}
