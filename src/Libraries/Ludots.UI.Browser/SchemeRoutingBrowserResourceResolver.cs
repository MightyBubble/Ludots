using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ludots.UI.Browser;

public sealed class SchemeRoutingBrowserResourceResolver : IBrowserResourceResolver
{
	private readonly IBrowserResourceResolver _panelRoot;
	private readonly IBrowserResourceResolver _vfs;

	public SchemeRoutingBrowserResourceResolver(
		IBrowserResourceResolver panelRoot,
		IBrowserResourceResolver vfs)
	{
		_panelRoot = panelRoot ?? throw new ArgumentNullException(nameof(panelRoot));
		_vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
	}

	public ValueTask<BrowserResource?> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(uri);
		if (BrowserVfsUri.Matches(uri))
		{
			return _vfs.ResolveAsync(uri, cancellationToken);
		}

		return _panelRoot.ResolveAsync(uri, cancellationToken);
	}
}
