using System;
using System.Collections.Concurrent;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.CefLinux;

internal sealed class CefLinuxBrowserSurfaceRegistry
{
	private const string ProcessRegistryKey = "Ludots.UI.Browser.CefLinux.CefLinuxBrowserSurfaceRegistry.v1";

	private readonly ConcurrentDictionary<int, Func<string, BrowserResource?>> _resourceResolvers =
		GetProcessResourceResolvers();

	public void Register(int browserId, Func<string, BrowserResource?> resolveResource)
	{
		ArgumentNullException.ThrowIfNull(resolveResource);
		_resourceResolvers[browserId] = resolveResource;
	}

	public void Unregister(int browserId)
	{
		_resourceResolvers.TryRemove(browserId, out _);
	}

	public bool TryResolveResource(int browserId, string requestUrl, out BrowserResource? resource)
	{
		resource = null;
		if (!_resourceResolvers.TryGetValue(browserId, out Func<string, BrowserResource?>? resolver))
		{
			return false;
		}

		resource = resolver(requestUrl);
		return resource != null;
	}

	private static ConcurrentDictionary<int, Func<string, BrowserResource?>> GetProcessResourceResolvers()
	{
		lock (AppDomain.CurrentDomain)
		{
			if (AppDomain.CurrentDomain.GetData(ProcessRegistryKey) is ConcurrentDictionary<int, Func<string, BrowserResource?>> existing)
			{
				return existing;
			}

			var created = new ConcurrentDictionary<int, Func<string, BrowserResource?>>();
			AppDomain.CurrentDomain.SetData(ProcessRegistryKey, created);
			return created;
		}
	}
}
