namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Simple ordinal id registry for panel kit reference catalogs (surface region, profile, layout, etc.).
/// </summary>
public sealed class WebUiPanelIdRegistry : IWebUiPanelIdRegistry
{
	private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
	private readonly string _kind;

	public WebUiPanelIdRegistry(string kind)
	{
		if (string.IsNullOrWhiteSpace(kind))
		{
			throw new ArgumentException("Registry kind is required.", nameof(kind));
		}

		_kind = kind.Trim();
	}

	public IReadOnlyCollection<string> Ids => _ids;

	public void Register(string id)
	{
		string normalized = RequireId(id);
		if (!_ids.Add(normalized))
		{
			throw new InvalidOperationException($"Duplicate {_kind} id '{normalized}'.");
		}
	}

	public void RegisterAll(IEnumerable<string> ids)
	{
		ArgumentNullException.ThrowIfNull(ids);
		foreach (string id in ids)
		{
			Register(id);
		}
	}

	public bool Contains(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return false;
		}

		return _ids.Contains(id.Trim());
	}

	private static string RequireId(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new ArgumentException("Id is required.", nameof(id));
		}

		string trimmed = id.Trim();
		if (!string.Equals(id, trimmed, StringComparison.Ordinal))
		{
			throw new ArgumentException("Id must not contain leading or trailing whitespace.", nameof(id));
		}

		return trimmed;
	}
}
