using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::CefNet;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.CefLinux;

public sealed class CefLinuxBrowserRuntime : IBrowserRuntime
{
	public const string LocalAppSchemeName = BrowserLocalAppUri.Scheme;
	public const string LocalAppHostName = BrowserLocalAppUri.Host;

	private readonly List<CefLinuxBrowserSurface> _surfaces = new();
	private readonly object _surfacesSync = new();

	private bool _disposed;

	public CefLinuxBrowserRuntime(CefLinuxBrowserRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		CefLinuxProcessRuntime.AcquireRuntimeOwner(options);
		Info = new BrowserRuntimeInfo(
			BrowserEngineKind.Cef,
			"CefNet OffScreen",
			ResolveVersion(),
			BrowserEngineCapabilityProfiles.Cef);
	}

	public BrowserRuntimeInfo Info { get; }

	public ValueTask<IBrowserSurface> CreateSurfaceAsync(
		BrowserViewport viewport,
		IBrowserResourceResolver? resourceResolver = null,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		var surface = new CefLinuxBrowserSurface(viewport, resourceResolver, CefLinuxProcessRuntime.SurfaceRegistry);
		lock (_surfacesSync)
		{
			_surfaces.Add(surface);
		}

		return ValueTask.FromResult<IBrowserSurface>(surface);
	}

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		CefLinuxBrowserSurface[] surfaces;
		lock (_surfacesSync)
		{
			surfaces = _surfaces.ToArray();
			_surfaces.Clear();
		}

		foreach (CefLinuxBrowserSurface surface in surfaces)
		{
			surface.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}

		CefLinuxProcessRuntime.ReleaseRuntimeOwner();
		return ValueTask.CompletedTask;
	}

	private static string ResolveVersion()
	{
		return typeof(CefApi).Assembly.GetName().Version?.ToString(3) ?? "unknown";
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(CefLinuxBrowserRuntime));
		}
	}
}
