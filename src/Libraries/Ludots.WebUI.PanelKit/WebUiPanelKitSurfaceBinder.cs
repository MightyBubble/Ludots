using System.Collections.ObjectModel;
using Ludots.UI.Compose;
using Ludots.UI.Surface;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Binds a validated panel kit manifest to one <see cref="IUiSurfaceHost"/>.
/// Each panel acquires its own lease under the shared host; no parallel host is created.
/// </summary>
public sealed class WebUiPanelKitSurfaceBinder : IDisposable
{
	private readonly IUiSurfaceHost _host;
	private readonly WebUiPanelKitManifest _manifest;
	private readonly Dictionary<string, UiSurfaceLeaseHandle> _leases = new(StringComparer.Ordinal);
	private bool _disposed;

	public WebUiPanelKitSurfaceBinder(IUiSurfaceHost host, WebUiPanelKitManifest manifest)
	{
		_host = host ?? throw new ArgumentNullException(nameof(host));
		_manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
	}

	public WebUiPanelKitManifest Manifest => _manifest;

	public IReadOnlyList<string> BoundPanelIds => new ReadOnlyCollection<string>(_leases.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToArray());

	/// <summary>
	/// Topics the browser must subscribe to for this bound manifest. Derived only from manifest declarations.
	/// </summary>
	public IReadOnlyList<string> BrowserSubscriptionTopics => _manifest.DeclaredTopics;

	public void Bind(Func<WebUiPanelDeclaration, UiSurfaceContribution>? contributionFactory = null)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		contributionFactory ??= CreatePlaceholderContribution;

		foreach (WebUiPanelDeclaration panel in _manifest.Panels)
		{
			string ownerId = $"{_manifest.HostOwnerId}.{panel.PanelId}";
			var request = new UiSurfaceLeaseRequest(ownerId, panel.SurfaceSegment, panel.SurfacePriority);
			UiSurfaceLeaseHandle handle = _host.Acquire(request);
			_host.Publish(handle, contributionFactory(panel));
			_leases[panel.PanelId] = handle;
		}
	}

	public bool TryGetLease(string panelId, out UiSurfaceLeaseHandle handle)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (string.IsNullOrWhiteSpace(panelId))
		{
			handle = default;
			return false;
		}

		return _leases.TryGetValue(panelId.Trim(), out handle);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		foreach (UiSurfaceLeaseHandle handle in _leases.Values)
		{
			_host.Release(handle);
		}

		_leases.Clear();
		_disposed = true;
	}

	private static UiSurfaceContribution CreatePlaceholderContribution(WebUiPanelDeclaration panel)
	{
		// Composition contract only: placeholder content proves host binding without inventing gameplay UI.
		return UiSurfaceContribution.FromBuilder(() =>
			Ui.Panel(
					Ui.Text(panel.PanelId).Id($"panel-kit-{panel.PanelId}-label"))
				.Id($"panel-kit-{panel.PanelId}"));
	}
}
