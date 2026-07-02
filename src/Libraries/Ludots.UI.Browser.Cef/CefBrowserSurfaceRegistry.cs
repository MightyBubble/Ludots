using System;
using System.Collections.Concurrent;
using CefSharp;

namespace Ludots.UI.Browser.Cef;

internal sealed class CefBrowserSurfaceRegistry
{
	private const string ProcessRegistryKey = "Ludots.UI.Browser.Cef.CefBrowserSurfaceRegistry.v1";

	private readonly ConcurrentDictionary<int, Func<string, IResourceHandler?>> _resourceResolvers =
		GetProcessResourceResolvers();

	public void Register(int browserId, CefBrowserSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);
		RegisterResolver(browserId, surface.ResolveResource);
	}

	public void Unregister(int browserId)
	{
		_resourceResolvers.TryRemove(browserId, out _);
	}

	public bool TryResolveResource(int browserId, string requestUrl, out IResourceHandler? resourceHandler)
	{
		resourceHandler = null;
		if (!_resourceResolvers.TryGetValue(browserId, out Func<string, IResourceHandler?>? resolver))
		{
			return false;
		}

		resourceHandler = resolver(requestUrl);
		return resourceHandler != null;
	}

	internal void RegisterResolver(int browserId, Func<string, IResourceHandler?> resolveResource)
	{
		ArgumentNullException.ThrowIfNull(resolveResource);
		_resourceResolvers[browserId] = resolveResource;
	}

	private static ConcurrentDictionary<int, Func<string, IResourceHandler?>> GetProcessResourceResolvers()
	{
		lock (AppDomain.CurrentDomain)
		{
			if (AppDomain.CurrentDomain.GetData(ProcessRegistryKey) is ConcurrentDictionary<int, Func<string, IResourceHandler?>> existing)
			{
				return existing;
			}

			var created = new ConcurrentDictionary<int, Func<string, IResourceHandler?>>();
			AppDomain.CurrentDomain.SetData(ProcessRegistryKey, created);
			return created;
		}
	}
}
