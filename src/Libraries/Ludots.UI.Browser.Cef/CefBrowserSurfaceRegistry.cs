using System;
using System.Collections.Concurrent;

namespace Ludots.UI.Browser.Cef;

internal sealed class CefBrowserSurfaceRegistry
{
	private readonly ConcurrentDictionary<int, CefBrowserSurface> _surfaces = new();

	public void Register(int browserId, CefBrowserSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);
		_surfaces[browserId] = surface;
	}

	public void Unregister(int browserId)
	{
		_surfaces.TryRemove(browserId, out _);
	}

	public bool TryGet(int browserId, out CefBrowserSurface surface)
	{
		return _surfaces.TryGetValue(browserId, out surface!);
	}
}
