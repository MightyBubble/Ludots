using System.Collections.ObjectModel;
using Ludots.UI.Surface;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// One panel entry in a WebUI Panel Kit manifest. Describes composition and references only;
/// it does not carry gameplay truth (resources, units, commands).
/// </summary>
public sealed class WebUiPanelDeclaration
{
	public WebUiPanelDeclaration(
		string panelId,
		string panelType,
		string surfaceRegionId,
		UiSurfaceSegment surfaceSegment,
		int surfacePriority,
		string anchor,
		string visibleConditionId,
		string topic,
		string profileId,
		string layoutId,
		string densityId,
		string inputCapabilityId,
		string? title = null,
		string? subtitle = null)
	{
		PanelId = RequireId(panelId, nameof(panelId));
		PanelType = RequireId(panelType, nameof(panelType));
		SurfaceRegionId = RequireId(surfaceRegionId, nameof(surfaceRegionId));
		SurfaceSegment = surfaceSegment;
		SurfacePriority = surfacePriority;
		Anchor = RequireId(anchor, nameof(anchor));
		VisibleConditionId = RequireId(visibleConditionId, nameof(visibleConditionId));
		Topic = RequireId(topic, nameof(topic));
		ProfileId = RequireId(profileId, nameof(profileId));
		LayoutId = RequireId(layoutId, nameof(layoutId));
		DensityId = RequireId(densityId, nameof(densityId));
		InputCapabilityId = RequireId(inputCapabilityId, nameof(inputCapabilityId));
		Title = RequireOptionalText(title, nameof(title));
		Subtitle = RequireOptionalText(subtitle, nameof(subtitle));
	}

	public string PanelId { get; }
	public string PanelType { get; }
	public string SurfaceRegionId { get; }
	public UiSurfaceSegment SurfaceSegment { get; }
	public int SurfacePriority { get; }
	public string Anchor { get; }
	public string VisibleConditionId { get; }
	public string Topic { get; }
	public string ProfileId { get; }
	public string LayoutId { get; }
	public string DensityId { get; }
	public string InputCapabilityId { get; }

	/// <summary>Display text owned by the manifest content layer; null means render the panel id.</summary>
	public string? Title { get; }

	/// <summary>Optional secondary display line owned by the manifest content layer.</summary>
	public string? Subtitle { get; }

	private static string? RequireOptionalText(string? value, string paramName)
	{
		if (value is null)
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{paramName} must be non-empty when provided.", paramName);
		}

		return value.Trim();
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
/// Validated panel kit manifest: panel composition contract for one UiSurfaceHost.
/// </summary>
public sealed class WebUiPanelKitManifest
{
	private readonly IReadOnlyList<WebUiPanelDeclaration> _panels;
	private readonly IReadOnlyList<string> _declaredTopics;

	public WebUiPanelKitManifest(string manifestId, string hostOwnerId, IReadOnlyList<WebUiPanelDeclaration> panels)
	{
		if (string.IsNullOrWhiteSpace(manifestId))
		{
			throw new ArgumentException("Manifest id is required.", nameof(manifestId));
		}

		if (string.IsNullOrWhiteSpace(hostOwnerId))
		{
			throw new ArgumentException("Host owner id is required.", nameof(hostOwnerId));
		}

		ArgumentNullException.ThrowIfNull(panels);
		if (panels.Count == 0)
		{
			throw new ArgumentException("Manifest must declare at least one panel.", nameof(panels));
		}

		ManifestId = manifestId.Trim();
		HostOwnerId = hostOwnerId.Trim();
		_panels = new ReadOnlyCollection<WebUiPanelDeclaration>(panels.ToArray());

		var topics = new List<string>(_panels.Count);
		var seenTopics = new HashSet<string>(StringComparer.Ordinal);
		foreach (WebUiPanelDeclaration panel in _panels)
		{
			if (seenTopics.Add(panel.Topic))
			{
				topics.Add(panel.Topic);
			}
		}

		_declaredTopics = new ReadOnlyCollection<string>(topics);
	}

	public string ManifestId { get; }
	public string HostOwnerId { get; }
	public IReadOnlyList<WebUiPanelDeclaration> Panels => _panels;

	/// <summary>
	/// Distinct DataPlane topics declared by this manifest. Browser clients must subscribe only to these.
	/// </summary>
	public IReadOnlyList<string> DeclaredTopics => _declaredTopics;
}

/// <summary>
/// Reference catalogs required to validate a panel kit manifest at load time.
/// Missing ids fail fast; there is no empty/Unknown/default fallback.
/// </summary>
public sealed class WebUiPanelKitReferenceCatalog
{
	public WebUiPanelKitReferenceCatalog(
		IWebUiPanelIdRegistry surfaceRegions,
		IWebUiPanelIdRegistry profiles,
		IWebUiPanelIdRegistry layouts,
		IWebUiPanelIdRegistry densities,
		IWebUiPanelIdRegistry inputCapabilities,
		IWebUiPanelIdRegistry visibleConditions,
		Func<string, bool> isTopicRegistered)
	{
		SurfaceRegions = surfaceRegions ?? throw new ArgumentNullException(nameof(surfaceRegions));
		Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
		Layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
		Densities = densities ?? throw new ArgumentNullException(nameof(densities));
		InputCapabilities = inputCapabilities ?? throw new ArgumentNullException(nameof(inputCapabilities));
		VisibleConditions = visibleConditions ?? throw new ArgumentNullException(nameof(visibleConditions));
		IsTopicRegistered = isTopicRegistered ?? throw new ArgumentNullException(nameof(isTopicRegistered));
	}

	public IWebUiPanelIdRegistry SurfaceRegions { get; }
	public IWebUiPanelIdRegistry Profiles { get; }
	public IWebUiPanelIdRegistry Layouts { get; }
	public IWebUiPanelIdRegistry Densities { get; }
	public IWebUiPanelIdRegistry InputCapabilities { get; }
	public IWebUiPanelIdRegistry VisibleConditions { get; }
	public Func<string, bool> IsTopicRegistered { get; }
}

public interface IWebUiPanelIdRegistry
{
	void Register(string id);
	bool Contains(string id);
	IReadOnlyCollection<string> Ids { get; }
}
