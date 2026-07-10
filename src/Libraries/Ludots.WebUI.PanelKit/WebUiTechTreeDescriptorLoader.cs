using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Loads and validates a TechTree / Progression panel descriptor from JSON.
/// Missing progression / requirement / scope / token / action / layout references fail fast.
/// </summary>
public static class WebUiTechTreeDescriptorLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static WebUiTechTreeDescriptor LoadFromJson(
		string json,
		WebUiTechTreeReferenceCatalog catalog,
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

		WebUiTechTreeDescriptorDocument? document;
		try
		{
			document = JsonSerializer.Deserialize<WebUiTechTreeDescriptorDocument>(json, JsonOptions);
		}
		catch (JsonException ex)
		{
			throw new InvalidOperationException($"Failed to deserialize TechTree descriptor '{source}': {ex.Message}", ex);
		}

		if (document == null)
		{
			throw new InvalidOperationException($"TechTree descriptor '{source}' deserialized to null.");
		}

		return ValidateAndBuild(document, catalog, source);
	}

	public static WebUiTechTreeDescriptor LoadFromFile(string path, WebUiTechTreeReferenceCatalog catalog)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Descriptor path is required.", nameof(path));
		}

		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"TechTree descriptor file not found: '{path}'.", path);
		}

		string json = File.ReadAllText(path);
		return LoadFromJson(json, catalog, path);
	}

	public static WebUiTechTreeDescriptor ValidateAndBuild(
		WebUiTechTreeDescriptorDocument document,
		WebUiTechTreeReferenceCatalog catalog,
		string source)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(catalog);
		if (string.IsNullOrWhiteSpace(source))
		{
			throw new ArgumentException("Source label is required.", nameof(source));
		}

		RequireTrimmedNonEmpty(document.DescriptorId, $"{source}.descriptorId");
		RequireTrimmedNonEmpty(document.ProfileId, $"{source}.profileId");
		RequireTrimmedNonEmpty(document.LayoutId, $"{source}.layoutId");
		RequireTrimmedNonEmpty(document.LocaleId, $"{source}.localeId");

		RequireRegistered(catalog.Profiles, document.ProfileId, $"{source}.profileId", "profile");
		RequireRegistered(catalog.Layouts, document.LayoutId, $"{source}.layoutId", "layout");
		RequireRegistered(catalog.Locales, document.LocaleId, $"{source}.localeId", "locale");

		if (document.Nodes == null)
		{
			throw new InvalidOperationException($"TechTree descriptor '{source}' must explicitly define nodes.");
		}

		if (document.Nodes.Count == 0)
		{
			throw new InvalidOperationException($"TechTree descriptor '{source}' must declare at least one node.");
		}

		var nodeIds = new HashSet<string>(StringComparer.Ordinal);
		var progressionIds = new HashSet<string>(StringComparer.Ordinal);
		var nodes = new List<WebUiTechTreeNode>(document.Nodes.Count);
		for (int i = 0; i < document.Nodes.Count; i++)
		{
			WebUiTechTreeNodeDocument node = document.Nodes[i]
				?? throw new InvalidOperationException($"{source}.nodes[{i}] must be an object.");
			string path = $"{source}.nodes[{i}]";
			RequireTrimmedNonEmpty(node.NodeId, $"{path}.nodeId");
			if (!nodeIds.Add(node.NodeId))
			{
				throw new InvalidOperationException($"{path}.nodeId duplicates node id '{node.NodeId}'.");
			}

			RequireTrimmedNonEmpty(node.ProgressionId, $"{path}.progressionId");
			if (!progressionIds.Add(node.ProgressionId))
			{
				throw new InvalidOperationException(
					$"{path}.progressionId duplicates progression id '{node.ProgressionId}'.");
			}

			if (!catalog.IsProgressionRegistered(node.ProgressionId))
			{
				throw new InvalidOperationException(
					$"{path}.progressionId references unknown progression '{node.ProgressionId}'.");
			}

			RequireTrimmedNonEmpty(node.ScopeKeyId, $"{path}.scopeKeyId");
			if (!catalog.IsScopeKeyRegistered(node.ScopeKeyId))
			{
				throw new InvalidOperationException(
					$"{path}.scopeKeyId references unknown scope '{node.ScopeKeyId}'.");
			}

			string? unlockRequirementId = null;
			if (!string.IsNullOrWhiteSpace(node.UnlockRequirementId))
			{
				RequireTrimmedNonEmpty(node.UnlockRequirementId, $"{path}.unlockRequirementId");
				if (!catalog.IsRequirementRegistered(node.UnlockRequirementId!))
				{
					throw new InvalidOperationException(
						$"{path}.unlockRequirementId references unknown requirement '{node.UnlockRequirementId}'.");
				}

				unlockRequirementId = node.UnlockRequirementId;
			}

			RequireTrimmedNonEmpty(node.TitleTokenId, $"{path}.titleTokenId");
			RequireTrimmedNonEmpty(node.BodyTokenId, $"{path}.bodyTokenId");
			RequireTrimmedNonEmpty(node.EffectTokenId, $"{path}.effectTokenId");
			RequireTrimmedNonEmpty(node.BlockedReasonTokenId, $"{path}.blockedReasonTokenId");
			RequireToken(catalog, node.TitleTokenId!, document.LocaleId, $"{path}.titleTokenId");
			RequireToken(catalog, node.BodyTokenId!, document.LocaleId, $"{path}.bodyTokenId");
			RequireToken(catalog, node.EffectTokenId!, document.LocaleId, $"{path}.effectTokenId");
			RequireToken(catalog, node.BlockedReasonTokenId!, document.LocaleId, $"{path}.blockedReasonTokenId");

			RequireTrimmedNonEmpty(node.GroupId, $"{path}.groupId");
			RequireTrimmedNonEmpty(node.ActionKind, $"{path}.actionKind");
			RequireTrimmedNonEmpty(node.ActionId, $"{path}.actionId");

			if (!TryParseActionKind(node.ActionKind, out WebUiTechTreeActionKind actionKind))
			{
				throw new InvalidOperationException(
					$"{path}.actionKind '{node.ActionKind}' is not a known WebUiTechTreeActionKind.");
			}

			if (!catalog.IsActionRegistered(node.ActionId!))
			{
				throw new InvalidOperationException(
					$"{path}.actionId references unknown action '{node.ActionId}'.");
			}

			string? tooltipDescriptorId = null;
			if (!string.IsNullOrWhiteSpace(node.TooltipDescriptorId))
			{
				RequireTrimmedNonEmpty(node.TooltipDescriptorId, $"{path}.tooltipDescriptorId");
				if (catalog.IsTooltipDescriptorRegistered != null &&
				    !catalog.IsTooltipDescriptorRegistered(node.TooltipDescriptorId!))
				{
					throw new InvalidOperationException(
						$"{path}.tooltipDescriptorId references unknown tooltip descriptor '{node.TooltipDescriptorId}'.");
				}

				tooltipDescriptorId = node.TooltipDescriptorId;
			}

			IReadOnlyList<string> prerequisites = Array.Empty<string>();
			if (node.PrerequisiteProgressionIds != null)
			{
				var prereqCopy = new List<string>(node.PrerequisiteProgressionIds.Count);
				var seenPrereq = new HashSet<string>(StringComparer.Ordinal);
				for (int p = 0; p < node.PrerequisiteProgressionIds.Count; p++)
				{
					string? prereq = node.PrerequisiteProgressionIds[p];
					string prereqPath = $"{path}.prerequisiteProgressionIds[{p}]";
					RequireTrimmedNonEmpty(prereq, prereqPath);
					if (!seenPrereq.Add(prereq!))
					{
						throw new InvalidOperationException(
							$"{prereqPath} duplicates prerequisite progression '{prereq}'.");
					}

					if (!catalog.IsProgressionRegistered(prereq!))
					{
						throw new InvalidOperationException(
							$"{prereqPath} references unknown progression '{prereq}'.");
					}

					prereqCopy.Add(prereq!);
				}

				prerequisites = prereqCopy;
			}

			nodes.Add(new WebUiTechTreeNode(
				node.NodeId!,
				node.ProgressionId!,
				node.ScopeKeyId!,
				unlockRequirementId,
				node.TitleTokenId!,
				node.BodyTokenId!,
				node.EffectTokenId!,
				node.BlockedReasonTokenId!,
				node.GroupId!,
				node.SortOrder,
				node.LayoutX,
				node.LayoutY,
				actionKind,
				node.ActionId!,
				prerequisites,
				tooltipDescriptorId));
		}

		return new WebUiTechTreeDescriptor(
			document.DescriptorId!,
			document.ProfileId!,
			document.LayoutId!,
			document.LocaleId!,
			nodes);
	}

	private static void RequireToken(
		WebUiTechTreeReferenceCatalog catalog,
		string tokenId,
		string localeId,
		string path)
	{
		if (!catalog.DisplayTokens.Contains(tokenId))
		{
			throw new InvalidOperationException($"{path} references unknown display token '{tokenId}'.");
		}

		if (catalog.HasLocaleTemplate != null && !catalog.HasLocaleTemplate(tokenId, localeId))
		{
			throw new InvalidOperationException(
				$"{path} token '{tokenId}' has no template for locale '{localeId}'.");
		}
	}

	private static bool TryParseActionKind(string value, out WebUiTechTreeActionKind kind)
	{
		kind = default;
		string normalized = value.Trim();
		if (Enum.TryParse(normalized, ignoreCase: true, out kind) &&
		    Enum.IsDefined(typeof(WebUiTechTreeActionKind), kind))
		{
			return true;
		}

		return normalized switch
		{
			"command" => Assign(WebUiTechTreeActionKind.Command, out kind),
			"ability" => Assign(WebUiTechTreeActionKind.Ability, out kind),
			"progression" => Assign(WebUiTechTreeActionKind.Progression, out kind),
			_ => false
		};
	}

	private static bool Assign(WebUiTechTreeActionKind value, out WebUiTechTreeActionKind kind)
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

public sealed class WebUiTechTreeDescriptorDocument
{
	[JsonPropertyName("descriptorId")]
	public string DescriptorId { get; set; } = string.Empty;

	[JsonPropertyName("profileId")]
	public string ProfileId { get; set; } = string.Empty;

	[JsonPropertyName("layoutId")]
	public string LayoutId { get; set; } = string.Empty;

	[JsonPropertyName("localeId")]
	public string LocaleId { get; set; } = string.Empty;

	[JsonPropertyName("nodes")]
	public List<WebUiTechTreeNodeDocument>? Nodes { get; set; }
}

public sealed class WebUiTechTreeNodeDocument
{
	[JsonPropertyName("nodeId")]
	public string NodeId { get; set; } = string.Empty;

	[JsonPropertyName("progressionId")]
	public string ProgressionId { get; set; } = string.Empty;

	[JsonPropertyName("scopeKeyId")]
	public string ScopeKeyId { get; set; } = string.Empty;

	[JsonPropertyName("unlockRequirementId")]
	public string? UnlockRequirementId { get; set; }

	[JsonPropertyName("titleTokenId")]
	public string TitleTokenId { get; set; } = string.Empty;

	[JsonPropertyName("bodyTokenId")]
	public string BodyTokenId { get; set; } = string.Empty;

	[JsonPropertyName("effectTokenId")]
	public string EffectTokenId { get; set; } = string.Empty;

	[JsonPropertyName("blockedReasonTokenId")]
	public string BlockedReasonTokenId { get; set; } = string.Empty;

	[JsonPropertyName("groupId")]
	public string GroupId { get; set; } = string.Empty;

	[JsonPropertyName("sortOrder")]
	public int SortOrder { get; set; }

	[JsonPropertyName("layoutX")]
	public float LayoutX { get; set; }

	[JsonPropertyName("layoutY")]
	public float LayoutY { get; set; }

	[JsonPropertyName("actionKind")]
	public string ActionKind { get; set; } = string.Empty;

	[JsonPropertyName("actionId")]
	public string ActionId { get; set; } = string.Empty;

	[JsonPropertyName("tooltipDescriptorId")]
	public string? TooltipDescriptorId { get; set; }

	[JsonPropertyName("prerequisiteProgressionIds")]
	public List<string>? PrerequisiteProgressionIds { get; set; }
}
