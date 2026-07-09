using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Loads and validates a resource attribute panel descriptor from JSON.
/// Missing attribute / token / graph output key references fail fast with the concrete id.
/// </summary>
public static class WebUiResourceAttributeDescriptorLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static WebUiResourceAttributeDescriptor LoadFromJson(
		string json,
		WebUiResourceAttributeReferenceCatalog catalog,
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

		WebUiResourceAttributeDescriptorDocument? document;
		try
		{
			document = JsonSerializer.Deserialize<WebUiResourceAttributeDescriptorDocument>(json, JsonOptions);
		}
		catch (JsonException ex)
		{
			throw new InvalidOperationException($"Failed to deserialize resource attribute descriptor '{source}': {ex.Message}", ex);
		}

		if (document == null)
		{
			throw new InvalidOperationException($"Resource attribute descriptor '{source}' deserialized to null.");
		}

		return ValidateAndBuild(document, catalog, source);
	}

	public static WebUiResourceAttributeDescriptor LoadFromFile(string path, WebUiResourceAttributeReferenceCatalog catalog)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Descriptor path is required.", nameof(path));
		}

		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Resource attribute descriptor file not found: '{path}'.", path);
		}

		string json = File.ReadAllText(path);
		return LoadFromJson(json, catalog, path);
	}

	public static WebUiResourceAttributeDescriptor ValidateAndBuild(
		WebUiResourceAttributeDescriptorDocument document,
		WebUiResourceAttributeReferenceCatalog catalog,
		string source)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(catalog);
		if (string.IsNullOrWhiteSpace(source))
		{
			throw new ArgumentException("Source label is required.", nameof(source));
		}

		RequireTrimmedNonEmpty(document.DescriptorId, $"{source}.descriptorId");
		if (document.Fields == null)
		{
			throw new InvalidOperationException($"Resource attribute descriptor '{source}' must explicitly define fields.");
		}

		if (document.Fields.Count == 0)
		{
			throw new InvalidOperationException($"Resource attribute descriptor '{source}' must declare at least one field.");
		}

		var fieldIds = new HashSet<string>(StringComparer.Ordinal);
		var fields = new List<WebUiResourceAttributeField>(document.Fields.Count);
		for (int i = 0; i < document.Fields.Count; i++)
		{
			WebUiResourceAttributeFieldDocument field = document.Fields[i]
				?? throw new InvalidOperationException($"{source}.fields[{i}] must be an object.");
			string path = $"{source}.fields[{i}]";
			RequireTrimmedNonEmpty(field.FieldId, $"{path}.fieldId");
			if (!fieldIds.Add(field.FieldId))
			{
				throw new InvalidOperationException($"{path}.fieldId duplicates field id '{field.FieldId}'.");
			}

			RequireTrimmedNonEmpty(field.GroupId, $"{path}.groupId");
			RequireTrimmedNonEmpty(field.DisplayTokenId, $"{path}.displayTokenId");
			RequireTrimmedNonEmpty(field.UnitTokenId, $"{path}.unitTokenId");
			RequireTrimmedNonEmpty(field.SourceKind, $"{path}.sourceKind");

			if (!TryParseSourceKind(field.SourceKind, out WebUiResourceAttributeSourceKind sourceKind))
			{
				throw new InvalidOperationException(
					$"{path}.sourceKind '{field.SourceKind}' is not a known WebUiResourceAttributeSourceKind.");
			}

			RequireRegistered(catalog.DisplayTokens, field.DisplayTokenId, $"{path}.displayTokenId", "display token");
			RequireRegistered(catalog.UnitTokens, field.UnitTokenId, $"{path}.unitTokenId", "unit token");

			string? attributeId = null;
			string? graphOutputKey = null;
			switch (sourceKind)
			{
				case WebUiResourceAttributeSourceKind.SingleAttribute:
				case WebUiResourceAttributeSourceKind.DerivedAttribute:
					RequireTrimmedNonEmpty(field.AttributeId, $"{path}.attributeId");
					if (!string.IsNullOrWhiteSpace(field.GraphOutputKey))
					{
						throw new InvalidOperationException(
							$"{path}.graphOutputKey must be omitted for sourceKind '{field.SourceKind}'.");
					}

					if (!catalog.IsAttributeRegistered(field.AttributeId!))
					{
						throw new InvalidOperationException(
							$"{path}.attributeId references unknown attribute '{field.AttributeId}'.");
					}

					attributeId = field.AttributeId;
					break;

				case WebUiResourceAttributeSourceKind.AggregateProjection:
					RequireTrimmedNonEmpty(field.GraphOutputKey, $"{path}.graphOutputKey");
					if (!string.IsNullOrWhiteSpace(field.AttributeId))
					{
						throw new InvalidOperationException(
							$"{path}.attributeId must be omitted for AggregateProjection; use graphOutputKey.");
					}

					if (catalog.IsGraphOutputKeyRegistered != null &&
					    !catalog.IsGraphOutputKeyRegistered(field.GraphOutputKey!))
					{
						throw new InvalidOperationException(
							$"{path}.graphOutputKey references unknown graph output key '{field.GraphOutputKey}'.");
					}

					graphOutputKey = field.GraphOutputKey;
					break;

				default:
					throw new InvalidOperationException($"{path}.sourceKind '{field.SourceKind}' is unsupported.");
			}

			fields.Add(new WebUiResourceAttributeField(
				field.FieldId,
				field.GroupId,
				field.DisplayTokenId,
				field.UnitTokenId,
				field.SortOrder,
				sourceKind,
				attributeId,
				graphOutputKey));
		}

		return new WebUiResourceAttributeDescriptor(document.DescriptorId, fields);
	}

	private static bool TryParseSourceKind(string value, out WebUiResourceAttributeSourceKind kind)
	{
		kind = default;
		string normalized = value.Trim();
		if (Enum.TryParse(normalized, ignoreCase: true, out kind))
		{
			return true;
		}

		// Accept camelCase JSON aliases used in samples.
		return normalized switch
		{
			"singleAttribute" => Assign(WebUiResourceAttributeSourceKind.SingleAttribute, out kind),
			"derivedAttribute" => Assign(WebUiResourceAttributeSourceKind.DerivedAttribute, out kind),
			"aggregateProjection" => Assign(WebUiResourceAttributeSourceKind.AggregateProjection, out kind),
			_ => false
		};
	}

	private static bool Assign(WebUiResourceAttributeSourceKind value, out WebUiResourceAttributeSourceKind kind)
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

public sealed class WebUiResourceAttributeDescriptorDocument
{
	[JsonPropertyName("descriptorId")]
	public string DescriptorId { get; set; } = string.Empty;

	[JsonPropertyName("fields")]
	public List<WebUiResourceAttributeFieldDocument>? Fields { get; set; }
}

public sealed class WebUiResourceAttributeFieldDocument
{
	[JsonPropertyName("fieldId")]
	public string FieldId { get; set; } = string.Empty;

	[JsonPropertyName("groupId")]
	public string GroupId { get; set; } = string.Empty;

	[JsonPropertyName("displayTokenId")]
	public string DisplayTokenId { get; set; } = string.Empty;

	[JsonPropertyName("unitTokenId")]
	public string UnitTokenId { get; set; } = string.Empty;

	[JsonPropertyName("sortOrder")]
	public int SortOrder { get; set; }

	[JsonPropertyName("sourceKind")]
	public string SourceKind { get; set; } = string.Empty;

	[JsonPropertyName("attributeId")]
	public string? AttributeId { get; set; }

	[JsonPropertyName("graphOutputKey")]
	public string? GraphOutputKey { get; set; }
}
