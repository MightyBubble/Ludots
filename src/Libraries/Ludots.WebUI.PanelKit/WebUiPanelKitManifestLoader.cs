using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.UI.Surface;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Loads and validates a WebUI Panel Kit manifest from JSON. Unknown topic/profile/layout/surface
/// references fail fast with the concrete id in the exception message.
/// </summary>
public static class WebUiPanelKitManifestLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static WebUiPanelKitManifest LoadFromJson(string json, WebUiPanelKitReferenceCatalog catalog, string source = "<inline>")
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			throw new ArgumentException("Manifest JSON is required.", nameof(json));
		}

		ArgumentNullException.ThrowIfNull(catalog);
		if (string.IsNullOrWhiteSpace(source))
		{
			throw new ArgumentException("Source label is required.", nameof(source));
		}

		WebUiPanelKitManifestDocument? document;
		try
		{
			document = JsonSerializer.Deserialize<WebUiPanelKitManifestDocument>(json, JsonOptions);
		}
		catch (JsonException ex)
		{
			throw new InvalidOperationException($"Failed to deserialize panel kit manifest '{source}': {ex.Message}", ex);
		}

		if (document == null)
		{
			throw new InvalidOperationException($"Panel kit manifest '{source}' deserialized to null.");
		}

		return ValidateAndBuild(document, catalog, source);
	}

	public static WebUiPanelKitManifest LoadFromFile(string path, WebUiPanelKitReferenceCatalog catalog)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Manifest path is required.", nameof(path));
		}

		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Panel kit manifest file not found: '{path}'.", path);
		}

		string json = File.ReadAllText(path);
		return LoadFromJson(json, catalog, path);
	}

	public static WebUiPanelKitManifest ValidateAndBuild(
		WebUiPanelKitManifestDocument document,
		WebUiPanelKitReferenceCatalog catalog,
		string source)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(catalog);
		if (string.IsNullOrWhiteSpace(source))
		{
			throw new ArgumentException("Source label is required.", nameof(source));
		}

		RequireTrimmedNonEmpty(document.ManifestId, $"{source}.manifestId");
		RequireTrimmedNonEmpty(document.HostOwnerId, $"{source}.hostOwnerId");
		if (document.Panels == null)
		{
			throw new InvalidOperationException($"Panel kit manifest '{source}' must explicitly define panels.");
		}

		if (document.Panels.Count == 0)
		{
			throw new InvalidOperationException($"Panel kit manifest '{source}' must declare at least one panel.");
		}

		var panelIds = new HashSet<string>(StringComparer.Ordinal);
		var panels = new List<WebUiPanelDeclaration>(document.Panels.Count);
		for (int i = 0; i < document.Panels.Count; i++)
		{
			WebUiPanelDeclarationDocument panel = document.Panels[i]
				?? throw new InvalidOperationException($"{source}.panels[{i}] must be an object.");
			string path = $"{source}.panels[{i}]";
			RequireTrimmedNonEmpty(panel.PanelId, $"{path}.panelId");
			if (!panelIds.Add(panel.PanelId))
			{
				throw new InvalidOperationException($"{path}.panelId duplicates panel id '{panel.PanelId}'.");
			}

			RequireTrimmedNonEmpty(panel.PanelType, $"{path}.panelType");
			RequireTrimmedNonEmpty(panel.SurfaceRegionId, $"{path}.surfaceRegionId");
			RequireTrimmedNonEmpty(panel.Anchor, $"{path}.anchor");
			RequireTrimmedNonEmpty(panel.VisibleConditionId, $"{path}.visibleConditionId");
			RequireTrimmedNonEmpty(panel.Topic, $"{path}.topic");
			RequireTrimmedNonEmpty(panel.ProfileId, $"{path}.profileId");
			RequireTrimmedNonEmpty(panel.LayoutId, $"{path}.layoutId");
			RequireTrimmedNonEmpty(panel.DensityId, $"{path}.densityId");
			RequireTrimmedNonEmpty(panel.InputCapabilityId, $"{path}.inputCapabilityId");
			RequireTrimmedNonEmpty(panel.SurfaceSegment, $"{path}.surfaceSegment");

			if (!Enum.TryParse(panel.SurfaceSegment, ignoreCase: true, out UiSurfaceSegment segment))
			{
				throw new InvalidOperationException(
					$"{path}.surfaceSegment '{panel.SurfaceSegment}' is not a known UiSurfaceSegment.");
			}

			RequireRegistered(catalog.SurfaceRegions, panel.SurfaceRegionId, $"{path}.surfaceRegionId", "surface region");
			RequireRegistered(catalog.Profiles, panel.ProfileId, $"{path}.profileId", "profile");
			RequireRegistered(catalog.Layouts, panel.LayoutId, $"{path}.layoutId", "layout");
			RequireRegistered(catalog.Densities, panel.DensityId, $"{path}.densityId", "density");
			RequireRegistered(catalog.InputCapabilities, panel.InputCapabilityId, $"{path}.inputCapabilityId", "input capability");
			RequireRegistered(catalog.VisibleConditions, panel.VisibleConditionId, $"{path}.visibleConditionId", "visible condition");

			if (!catalog.IsTopicRegistered(panel.Topic))
			{
				throw new InvalidOperationException(
					$"{path}.topic references unknown DataPlane topic '{panel.Topic}'.");
			}

			panels.Add(new WebUiPanelDeclaration(
				panel.PanelId,
				panel.PanelType,
				panel.SurfaceRegionId,
				segment,
				panel.SurfacePriority,
				panel.Anchor,
				panel.VisibleConditionId,
				panel.Topic,
				panel.ProfileId,
				panel.LayoutId,
				panel.DensityId,
				panel.InputCapabilityId));
		}

		return new WebUiPanelKitManifest(document.ManifestId, document.HostOwnerId, panels);
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

public sealed class WebUiPanelKitManifestDocument
{
	[JsonPropertyName("manifestId")]
	public string ManifestId { get; set; } = string.Empty;

	[JsonPropertyName("hostOwnerId")]
	public string HostOwnerId { get; set; } = string.Empty;

	[JsonPropertyName("panels")]
	public List<WebUiPanelDeclarationDocument>? Panels { get; set; }
}

public sealed class WebUiPanelDeclarationDocument
{
	[JsonPropertyName("panelId")]
	public string PanelId { get; set; } = string.Empty;

	[JsonPropertyName("panelType")]
	public string PanelType { get; set; } = string.Empty;

	[JsonPropertyName("surfaceRegionId")]
	public string SurfaceRegionId { get; set; } = string.Empty;

	[JsonPropertyName("surfaceSegment")]
	public string SurfaceSegment { get; set; } = string.Empty;

	[JsonPropertyName("surfacePriority")]
	public int SurfacePriority { get; set; }

	[JsonPropertyName("anchor")]
	public string Anchor { get; set; } = string.Empty;

	[JsonPropertyName("visibleConditionId")]
	public string VisibleConditionId { get; set; } = string.Empty;

	[JsonPropertyName("topic")]
	public string Topic { get; set; } = string.Empty;

	[JsonPropertyName("profileId")]
	public string ProfileId { get; set; } = string.Empty;

	[JsonPropertyName("layoutId")]
	public string LayoutId { get; set; } = string.Empty;

	[JsonPropertyName("densityId")]
	public string DensityId { get; set; } = string.Empty;

	[JsonPropertyName("inputCapabilityId")]
	public string InputCapabilityId { get; set; } = string.Empty;
}
