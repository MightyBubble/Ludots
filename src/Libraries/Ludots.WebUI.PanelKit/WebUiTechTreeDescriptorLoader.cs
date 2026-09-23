using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Loads and validates a TechTree / Progression panel descriptor from JSON.
/// Missing progression / requirement / scope / token / command references fail fast with the concrete id.
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
		RequireTrimmedNonEmpty(document.ScopeKey, $"{source}.scopeKey");

		RequireRegistered(catalog.Profiles, document.ProfileId, $"{source}.profileId", "profile");
		RequireRegistered(catalog.Layouts, document.LayoutId, $"{source}.layoutId", "layout");
		if (!catalog.IsScopeKeyRegistered(document.ScopeKey))
		{
			throw new InvalidOperationException($"{source}.scopeKey references unknown scope '{document.ScopeKey}'.");
		}

		if (document.Nodes == null)
		{
			throw new InvalidOperationException($"TechTree descriptor '{source}' must explicitly define nodes.");
		}

		if (document.Nodes.Count == 0)
		{
			throw new InvalidOperationException($"TechTree descriptor '{source}' must declare at least one node.");
		}

		var nodeIds = new HashSet<string>(StringComparer.Ordinal);
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
			RequireTrimmedNonEmpty(node.DisplayTokenId, $"{path}.displayTokenId");
			RequireTrimmedNonEmpty(node.LayoutSlotId, $"{path}.layoutSlotId");
			if (node.TargetLevel <= 0)
			{
				throw new InvalidOperationException($"{path}.targetLevel must be >= 1.");
			}

			if (!catalog.IsProgressionRegistered(node.ProgressionId))
			{
				throw new InvalidOperationException(
					$"{path}.progressionId references unknown progression '{node.ProgressionId}'.");
			}

			RequireRegistered(catalog.DisplayTokens, node.DisplayTokenId, $"{path}.displayTokenId", "display token");

			string? requirementId = null;
			if (!string.IsNullOrWhiteSpace(node.RequirementId))
			{
				RequireTrimmedNonEmpty(node.RequirementId, $"{path}.requirementId");
				if (!catalog.IsRequirementRegistered(node.RequirementId!))
				{
					throw new InvalidOperationException(
						$"{path}.requirementId references unknown requirement '{node.RequirementId}'.");
				}

				requirementId = node.RequirementId;
			}

			string? blockedReasonTokenId = null;
			if (!string.IsNullOrWhiteSpace(node.BlockedReasonTokenId))
			{
				RequireTrimmedNonEmpty(node.BlockedReasonTokenId, $"{path}.blockedReasonTokenId");
				RequireRegistered(
					catalog.BlockedReasonTokens,
					node.BlockedReasonTokenId!,
					$"{path}.blockedReasonTokenId",
					"blocked reason token");
				blockedReasonTokenId = node.BlockedReasonTokenId;
			}

			if (requirementId != null && blockedReasonTokenId == null)
			{
				throw new InvalidOperationException(
					$"{path} declares requirementId '{requirementId}' but missing blockedReasonTokenId.");
			}

			string? commandId = null;
			if (!string.IsNullOrWhiteSpace(node.CommandId))
			{
				RequireTrimmedNonEmpty(node.CommandId, $"{path}.commandId");
				if (!catalog.IsCommandRegistered(node.CommandId!))
				{
					throw new InvalidOperationException(
						$"{path}.commandId references unknown WebUI command '{node.CommandId}'.");
				}

				commandId = node.CommandId;
			}

			var prerequisites = new List<string>();
			if (node.PrerequisiteNodeIds != null)
			{
				for (int p = 0; p < node.PrerequisiteNodeIds.Count; p++)
				{
					string? prerequisite = node.PrerequisiteNodeIds[p];
					RequireTrimmedNonEmpty(prerequisite, $"{path}.prerequisiteNodeIds[{p}]");
					prerequisites.Add(prerequisite!);
				}
			}

			nodes.Add(new WebUiTechTreeNode(
				node.NodeId,
				node.ProgressionId,
				node.DisplayTokenId,
				node.SortOrder,
				node.LayoutSlotId,
				node.TargetLevel,
				requirementId,
				prerequisites,
				blockedReasonTokenId,
				commandId));
		}

		return new WebUiTechTreeDescriptor(
			document.DescriptorId,
			document.ProfileId,
			document.LayoutId,
			document.ScopeKey,
			nodes);
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

	[JsonPropertyName("scopeKey")]
	public string ScopeKey { get; set; } = string.Empty;

	[JsonPropertyName("nodes")]
	public List<WebUiTechTreeNodeDocument>? Nodes { get; set; }
}

public sealed class WebUiTechTreeNodeDocument
{
	[JsonPropertyName("nodeId")]
	public string NodeId { get; set; } = string.Empty;

	[JsonPropertyName("progressionId")]
	public string ProgressionId { get; set; } = string.Empty;

	[JsonPropertyName("requirementId")]
	public string? RequirementId { get; set; }

	[JsonPropertyName("prerequisiteNodeIds")]
	public List<string>? PrerequisiteNodeIds { get; set; }

	[JsonPropertyName("displayTokenId")]
	public string DisplayTokenId { get; set; } = string.Empty;

	[JsonPropertyName("blockedReasonTokenId")]
	public string? BlockedReasonTokenId { get; set; }

	[JsonPropertyName("commandId")]
	public string? CommandId { get; set; }

	[JsonPropertyName("targetLevel")]
	public int TargetLevel { get; set; } = 1;

	[JsonPropertyName("sortOrder")]
	public int SortOrder { get; set; }

	[JsonPropertyName("layoutSlotId")]
	public string LayoutSlotId { get; set; } = string.Empty;
}
