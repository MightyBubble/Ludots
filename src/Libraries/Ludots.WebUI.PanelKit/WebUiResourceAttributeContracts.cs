using System.Collections.ObjectModel;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// How a resource panel field obtains its numeric value. Resource is a display view over
/// Attribute / Graph outputs — never a parallel ResourceStore.
/// </summary>
public enum WebUiResourceAttributeSourceKind
{
	/// <summary>Final value from an entity AttributeBuffer slot.</summary>
	SingleAttribute = 1,

	/// <summary>
	/// Derived/computed attribute already written into AttributeBuffer by
	/// AttributeAggregatorSystem / AttributeDerivedGraphBinding. Panel only reads the result.
	/// </summary>
	DerivedAttribute = 2,

	/// <summary>
	/// Cross-entity aggregate already projected into GraphOutputValueStore (or equivalent Core
	/// projection). Web/showcase must not hand-sum entities.
	/// </summary>
	AggregateProjection = 3
}

/// <summary>
/// One display field in a resource attribute panel descriptor.
/// </summary>
public sealed class WebUiResourceAttributeField
{
	public WebUiResourceAttributeField(
		string fieldId,
		string groupId,
		string displayTokenId,
		string unitTokenId,
		int sortOrder,
		WebUiResourceAttributeSourceKind sourceKind,
		string? attributeId,
		string? graphOutputKey)
	{
		FieldId = RequireId(fieldId, nameof(fieldId));
		GroupId = RequireId(groupId, nameof(groupId));
		DisplayTokenId = RequireId(displayTokenId, nameof(displayTokenId));
		UnitTokenId = RequireId(unitTokenId, nameof(unitTokenId));
		SortOrder = sortOrder;
		SourceKind = sourceKind;

		switch (sourceKind)
		{
			case WebUiResourceAttributeSourceKind.SingleAttribute:
			case WebUiResourceAttributeSourceKind.DerivedAttribute:
				AttributeId = RequireId(attributeId, nameof(attributeId));
				GraphOutputKey = null;
				break;
			case WebUiResourceAttributeSourceKind.AggregateProjection:
				if (!string.IsNullOrWhiteSpace(attributeId))
				{
					throw new ArgumentException(
						$"AggregateProjection field '{FieldId}' must not declare attributeId; use graphOutputKey.",
						nameof(attributeId));
				}

				GraphOutputKey = RequireId(graphOutputKey, nameof(graphOutputKey));
				AttributeId = null;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unknown resource attribute source kind.");
		}
	}

	public string FieldId { get; }
	public string GroupId { get; }
	public string DisplayTokenId { get; }
	public string UnitTokenId { get; }
	public int SortOrder { get; }
	public WebUiResourceAttributeSourceKind SourceKind { get; }
	public string? AttributeId { get; }
	public string? GraphOutputKey { get; }

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
/// Validated resource attribute panel descriptor: display fields only, no gameplay truth.
/// </summary>
public sealed class WebUiResourceAttributeDescriptor
{
	private readonly IReadOnlyList<WebUiResourceAttributeField> _fields;

	public WebUiResourceAttributeDescriptor(string descriptorId, IReadOnlyList<WebUiResourceAttributeField> fields)
	{
		if (string.IsNullOrWhiteSpace(descriptorId))
		{
			throw new ArgumentException("Descriptor id is required.", nameof(descriptorId));
		}

		ArgumentNullException.ThrowIfNull(fields);
		if (fields.Count == 0)
		{
			throw new ArgumentException("Descriptor must declare at least one field.", nameof(fields));
		}

		DescriptorId = descriptorId.Trim();
		var ordered = fields.OrderBy(static field => field.SortOrder).ThenBy(static field => field.FieldId, StringComparer.Ordinal).ToArray();
		_fields = new ReadOnlyCollection<WebUiResourceAttributeField>(ordered);
	}

	public string DescriptorId { get; }
	public IReadOnlyList<WebUiResourceAttributeField> Fields => _fields;
}

/// <summary>
/// Reference catalogs required to validate a resource attribute descriptor at load time.
/// Missing ids fail fast; there is no empty/Unknown/default fallback.
/// </summary>
public sealed class WebUiResourceAttributeReferenceCatalog
{
	public WebUiResourceAttributeReferenceCatalog(
		IWebUiPanelIdRegistry displayTokens,
		IWebUiPanelIdRegistry unitTokens,
		Func<string, bool> isAttributeRegistered,
		Func<string, bool>? isGraphOutputKeyRegistered = null)
	{
		DisplayTokens = displayTokens ?? throw new ArgumentNullException(nameof(displayTokens));
		UnitTokens = unitTokens ?? throw new ArgumentNullException(nameof(unitTokens));
		IsAttributeRegistered = isAttributeRegistered ?? throw new ArgumentNullException(nameof(isAttributeRegistered));
		IsGraphOutputKeyRegistered = isGraphOutputKeyRegistered;
	}

	public IWebUiPanelIdRegistry DisplayTokens { get; }
	public IWebUiPanelIdRegistry UnitTokens { get; }
	public Func<string, bool> IsAttributeRegistered { get; }

	/// <summary>
	/// Optional load-time graph output key registry. When null, aggregate keys are still required
	/// non-empty at load and missing outputs fail fast at produce time.
	/// </summary>
	public Func<string, bool>? IsGraphOutputKeyRegistered { get; }
}
